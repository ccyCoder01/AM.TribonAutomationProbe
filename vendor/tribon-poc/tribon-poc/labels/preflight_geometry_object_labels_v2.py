# -*- coding: ascii -*-
import os
import re
import math
import kcs_draft
import KcsCaptureRegion2D
import KcsText

PLAN = r"C:\AM_TribonBridge\diagnostics\geometry-object-label-plan.json"
OUTPUT = r"C:\AM_TribonBridge\diagnostics\geometry-object-label-preflight-v2.tsv"

MIN_ALLOWED_DISTANCE = 12.0
MAX_ALLOWED_DISTANCE = 25.0
OBJECT_SIZE_FACTOR = 0.15

def clean(value):
    try:
        text = str(value)
    except:
        text = "<conversion_failed>"

    text = text.replace("\t", " ")
    text = text.replace("\r", " ")
    text = text.replace("\n", " ")

    return text.strip()

def write_line(file_object, text):
    file_object.write(text + "\n")
    file_object.flush()

def json_unescape(value):
    value = value.replace("\\\"", "\"")
    value = value.replace("\\\\", "\\")
    value = value.replace("\\n", "\n")
    value = value.replace("\\r", "\r")
    value = value.replace("\\t", "\t")

    return value

def read_plan_items():
    if not os.path.exists(PLAN):
        raise Exception(
            "PLAN_NOT_FOUND: " + PLAN
        )

    file_object = open(PLAN, "r")
    content = file_object.read()
    file_object.close()

    number = r"([-+0-9.eE]+)"

    pattern = re.compile(
        r'"operationId"\s*:\s*"([^"]+)"'
        r'.*?'
        r'"sourceObjectId"\s*:\s*"([^"]+)"'
        r'.*?'
        r'"stableObjectId"\s*:\s*"([^"]+)"'
        r'.*?'
        r'"category"\s*:\s*"([^"]+)"'
        r'.*?'
        r'"expectedExistingText"\s*:\s*"([^"]+)"'
        r'.*?'
        r'"target"\s*:\s*\{'
        r'.*?'
        r'"extent"\s*:\s*\{'
        r'.*?'
        r'"minX"\s*:\s*' + number +
        r'.*?'
        r'"minY"\s*:\s*' + number +
        r'.*?'
        r'"maxX"\s*:\s*' + number +
        r'.*?'
        r'"maxY"\s*:\s*' + number,
        re.S
    )

    matches = pattern.findall(content)
    items = []

    for match in matches:
        x1 = float(match[5])
        y1 = float(match[6])
        x2 = float(match[7])
        y2 = float(match[8])

        width = x2 - x1
        height = y2 - y1
        major_size = max(width, height)

        allowed_distance = (
            major_size *
            OBJECT_SIZE_FACTOR
        )

        if (
            allowed_distance <
            MIN_ALLOWED_DISTANCE
        ):
            allowed_distance = (
                MIN_ALLOWED_DISTANCE
            )

        if (
            allowed_distance >
            MAX_ALLOWED_DISTANCE
        ):
            allowed_distance = (
                MAX_ALLOWED_DISTANCE
            )

        items.append({
            "operation_id":
                json_unescape(match[0]),
            "source_object_id":
                json_unescape(match[1]),
            "stable_object_id":
                json_unescape(match[2]),
            "category":
                json_unescape(match[3]),
            "expected_text":
                json_unescape(match[4]),
            "target_extent": (
                x1,
                y1,
                x2,
                y2
            ),
            "allowed_distance":
                allowed_distance
        })

    return items

def capture_text_handles():
    drawing_extent = (
        kcs_draft.element_extent_get()
    )

    region = (
        KcsCaptureRegion2D.CaptureRegion2D()
    )

    region.SetRectangle(drawing_extent)
    region.SetInside()
    region.SetNoCut()

    try:
        return kcs_draft.text_capture(
            region
        )

    except:
        if str(kcs_draft.error) == "kcs_NotFound":
            return [0]

        raise

def get_extent(handle):
    extent = kcs_draft.element_extent_get(
        handle
    )

    return (
        min(
            extent.Corner1.X,
            extent.Corner2.X
        ),
        min(
            extent.Corner1.Y,
            extent.Corner2.Y
        ),
        max(
            extent.Corner1.X,
            extent.Corner2.X
        ),
        max(
            extent.Corner1.Y,
            extent.Corner2.Y
        )
    )

def extent_center(extent):
    return (
        (extent[0] + extent[2]) / 2.0,
        (extent[1] + extent[3]) / 2.0
    )

def point_to_extent_distance(
    point,
    extent
):
    dx = 0.0
    dy = 0.0

    if point[0] < extent[0]:
        dx = extent[0] - point[0]

    elif point[0] > extent[2]:
        dx = point[0] - extent[2]

    if point[1] < extent[1]:
        dy = extent[1] - point[1]

    elif point[1] > extent[3]:
        dy = point[1] - extent[3]

    return math.sqrt(
        (dx * dx) + (dy * dy)
    )

def extent_text(extent):
    return "%s,%s,%s,%s" % (
        str(extent[0]),
        str(extent[1]),
        str(extent[2]),
        str(extent[3])
    )

folder = os.path.dirname(OUTPUT)

if not os.path.exists(folder):
    os.makedirs(folder)

f = open(OUTPUT, "w")

try:
    plan_items = read_plan_items()

    capture_result = capture_text_handles()
    text_handles = capture_result[1:]

    matches_by_text = {}
    text_property_error_count = 0
    extent_error_count = 0

    for handle in text_handles:
        try:
            text_object = KcsText.Text()

            text_object = (
                kcs_draft.text_properties_get(
                    handle,
                    text_object
                )
            )

            value = clean(
                text_object.GetString()
            )

            try:
                text_extent = get_extent(
                    handle
                )

            except:
                extent_error_count = (
                    extent_error_count + 1
                )

                continue

            if not matches_by_text.has_key(
                value
            ):
                matches_by_text[value] = []

            matches_by_text[value].append({
                "handle": clean(handle),
                "extent": text_extent,
                "center": extent_center(
                    text_extent
                )
            })

        except:
            text_property_error_count = (
                text_property_error_count + 1
            )

    write_line(
        f,
        "FORMAT\tAM_GEOMETRY_OBJECT_LABEL_PREFLIGHT_V2"
    )

    write_line(
        f,
        "PLAN\t%s" %
        PLAN
    )

    write_line(
        f,
        "PLAN_OPERATION_COUNT\t%s" %
        str(len(plan_items))
    )

    write_line(
        f,
        "CAPTURED_TEXT_COUNT\t%s" %
        str(len(text_handles))
    )

    write_line(
        f,
        "OPERATION_INDEX"
        "\tOPERATION_ID"
        "\tSOURCE_OBJECT_ID"
        "\tEXPECTED_TEXT"
        "\tMATCH_COUNT"
        "\tNEAREST_DISTANCE"
        "\tALLOWED_DISTANCE"
        "\tDECISION"
        "\tTARGET_EXTENT"
        "\tMATCH_DETAILS"
    )

    ready_to_create_count = 0
    already_applied_count = 0
    blocked_duplicate_count = 0
    blocked_text_conflict_count = 0

    operation_index = 0

    for item in plan_items:
        operation_index = (
            operation_index + 1
        )

        expected_text = item[
            "expected_text"
        ]

        matches = matches_by_text.get(
            expected_text,
            []
        )

        match_count = len(matches)
        nearest_distance = None
        nearest_match = None

        for match in matches:
            distance = (
                point_to_extent_distance(
                    match["center"],
                    item["target_extent"]
                )
            )

            match["distance"] = distance

            if (
                nearest_distance is None or
                distance < nearest_distance
            ):
                nearest_distance = distance
                nearest_match = match

        if match_count == 0:
            decision = "READY_TO_CREATE"

            ready_to_create_count = (
                ready_to_create_count + 1
            )

        elif match_count > 1:
            decision = "BLOCKED_DUPLICATE"

            blocked_duplicate_count = (
                blocked_duplicate_count + 1
            )

        elif (
            nearest_distance <=
            item["allowed_distance"]
        ):
            decision = "ALREADY_APPLIED"

            already_applied_count = (
                already_applied_count + 1
            )

        else:
            decision = "BLOCKED_TEXT_CONFLICT"

            blocked_text_conflict_count = (
                blocked_text_conflict_count + 1
            )

        detail_values = []

        for match in matches:
            detail_values.append(
                "%s@%s@distance=%s" % (
                    match["handle"],
                    extent_text(
                        match["extent"]
                    ),
                    str(match["distance"])
                )
            )

        distance_value = ""

        if nearest_distance is not None:
            distance_value = str(
                nearest_distance
            )

        write_line(
            f,
            "%s\t%s\t%s\t%s"
            "\t%s\t%s\t%s\t%s"
            "\t%s\t%s" % (
                str(operation_index),
                item["operation_id"],
                item["source_object_id"],
                expected_text,
                str(match_count),
                distance_value,
                str(
                    item[
                        "allowed_distance"
                    ]
                ),
                decision,
                extent_text(
                    item["target_extent"]
                ),
                ";".join(detail_values)
            )
        )

    write_line(f, "")
    write_line(f, "SUMMARY")

    write_line(
        f,
        "READY_TO_CREATE_COUNT\t%s" %
        str(ready_to_create_count)
    )

    write_line(
        f,
        "ALREADY_APPLIED_COUNT\t%s" %
        str(already_applied_count)
    )

    write_line(
        f,
        "BLOCKED_DUPLICATE_COUNT\t%s" %
        str(blocked_duplicate_count)
    )

    write_line(
        f,
        "BLOCKED_TEXT_CONFLICT_COUNT\t%s" %
        str(blocked_text_conflict_count)
    )

    write_line(
        f,
        "TEXT_PROPERTY_ERROR_COUNT\t%s" %
        str(text_property_error_count)
    )

    write_line(
        f,
        "EXTENT_ERROR_COUNT\t%s" %
        str(extent_error_count)
    )

    write_line(
        f,
        "DRAWING_WRITE_PERFORMED\t0"
    )

    if (
        len(plan_items) == 12 and
        blocked_duplicate_count == 0 and
        blocked_text_conflict_count == 0 and
        text_property_error_count == 0 and
        extent_error_count == 0
    ):
        write_line(f, "STATUS\tSUCCESS")
    else:
        write_line(f, "STATUS\tFAILED")

except Exception, e:
    write_line(
        f,
        "ERROR\t%s" %
        clean(e)
    )

    write_line(
        f,
        "KCS_ERROR\t%s" %
        clean(kcs_draft.error)
    )

    write_line(
        f,
        "DRAWING_WRITE_PERFORMED\t0"
    )

    write_line(f, "STATUS\tFAILED")

f.close()
