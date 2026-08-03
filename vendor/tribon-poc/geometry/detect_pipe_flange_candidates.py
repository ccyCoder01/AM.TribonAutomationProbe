# -*- coding: ascii -*-
import os
import math
import kcs_draft
import KcsCaptureRegion2D
import KcsContour2D
import KcsPoint2D

OUTPUT = r"C:\AM_TribonBridge\diagnostics\pipe-flange-candidates.tsv"

ARC_TOLERANCE = 0.000001
CLOSE_TOLERANCE = 0.001
CENTER_TOLERANCE = 0.05
RADIUS_TOLERANCE = 0.05

def clean(value):
    try:
        text = str(value)
    except:
        text = "<conversion_failed>"

    text = text.replace("\t", " ")
    text = text.replace("\r", " ")
    text = text.replace("\n", " ")

    return text

def write_line(file_object, text):
    file_object.write(text + "\n")
    file_object.flush()

def distance_xy(x1, y1, x2, y2):
    dx = x2 - x1
    dy = y2 - y1

    return math.sqrt(
        (dx * dx) + (dy * dy)
    )

def get_point(segment):
    try:
        if len(segment) >= 1:
            return segment[0]
    except:
        pass

    return None

def get_amplitude(segment):
    try:
        if len(segment) >= 2:
            return float(segment[1])
    except:
        pass

    return 0.0

def calculate_arc(start_point, end_point, amplitude):
    dx = end_point.X - start_point.X
    dy = end_point.Y - start_point.Y

    chord = math.sqrt(
        (dx * dx) + (dy * dy)
    )

    sagitta = math.fabs(amplitude)

    if (
        chord <= ARC_TOLERANCE or
        sagitta <= ARC_TOLERANCE
    ):
        return None

    radius = (
        (chord * chord) /
        (8.0 * sagitta)
    ) + (sagitta / 2.0)

    midpoint_x = (
        start_point.X + end_point.X
    ) / 2.0

    midpoint_y = (
        start_point.Y + end_point.Y
    ) / 2.0

    normal_x = dy / chord
    normal_y = -dx / chord

    if amplitude >= 0.0:
        sign = 1.0
    else:
        sign = -1.0

    center_offset = radius - sagitta

    center_x = (
        midpoint_x -
        (
            normal_x *
            sign *
            center_offset
        )
    )

    center_y = (
        midpoint_y -
        (
            normal_y *
            sign *
            center_offset
        )
    )

    return {
        "center_x": center_x,
        "center_y": center_y,
        "radius": radius
    }

def capture_contours(region):
    try:
        return kcs_draft.contour_capture(region)
    except:
        if str(kcs_draft.error) == "kcs_NotFound":
            return [0]

        raise

def parse_circle(handle, index):
    seed = KcsPoint2D.Point2D(
        0.0,
        0.0
    )

    contour = KcsContour2D.Contour2D(
        seed
    )

    contour = (
        kcs_draft.contour_properties_get(
            handle,
            contour
        )
    )

    vertices = contour.Contour

    if len(vertices) != 3:
        return None

    first_point = get_point(vertices[0])
    last_point = get_point(vertices[2])

    if (
        first_point is None or
        last_point is None
    ):
        return None

    if distance_xy(
        first_point.X,
        first_point.Y,
        last_point.X,
        last_point.Y
    ) > CLOSE_TOLERANCE:
        return None

    first_amplitude = get_amplitude(
        vertices[1]
    )

    second_amplitude = get_amplitude(
        vertices[2]
    )

    if (
        math.fabs(first_amplitude) <=
        ARC_TOLERANCE or
        math.fabs(second_amplitude) <=
        ARC_TOLERANCE
    ):
        return None

    first_arc = calculate_arc(
        get_point(vertices[0]),
        get_point(vertices[1]),
        first_amplitude
    )

    second_arc = calculate_arc(
        get_point(vertices[1]),
        get_point(vertices[2]),
        second_amplitude
    )

    if (
        first_arc is None or
        second_arc is None
    ):
        return None

    center_difference = distance_xy(
        first_arc["center_x"],
        first_arc["center_y"],
        second_arc["center_x"],
        second_arc["center_y"]
    )

    radius_difference = math.fabs(
        first_arc["radius"] -
        second_arc["radius"]
    )

    if (
        center_difference >
        CENTER_TOLERANCE or
        radius_difference >
        RADIUS_TOLERANCE
    ):
        return None

    return {
        "index": index,
        "handle": handle,
        "center_x": (
            first_arc["center_x"] +
            second_arc["center_x"]
        ) / 2.0,
        "center_y": (
            first_arc["center_y"] +
            second_arc["center_y"]
        ) / 2.0,
        "radius": (
            first_arc["radius"] +
            second_arc["radius"]
        ) / 2.0
    }

def group_concentric_circles(circles):
    groups = []

    for circle in circles:
        found_group = None

        for group in groups:
            center_distance = distance_xy(
                circle["center_x"],
                circle["center_y"],
                group["center_x"],
                group["center_y"]
            )

            if center_distance <= CENTER_TOLERANCE:
                found_group = group
                break

        if found_group is None:
            found_group = {
                "center_x": circle["center_x"],
                "center_y": circle["center_y"],
                "circles": []
            }

            groups.append(found_group)

        found_group["circles"].append(circle)

    return groups

def average(values):
    if len(values) == 0:
        return 0.0

    total = 0.0

    for value in values:
        total = total + value

    return total / float(len(values))

def find_bolt_pattern(
    core_group,
    circles
):
    central_circles = core_group["circles"]

    outer_radius = 0.0
    central_handles = []

    for circle in central_circles:
        central_handles.append(
            clean(circle["handle"])
        )

        if circle["radius"] > outer_radius:
            outer_radius = circle["radius"]

    candidates = []

    for circle in circles:
        is_central = 0

        for central_circle in central_circles:
            if (
                circle["handle"] ==
                central_circle["handle"]
            ):
                is_central = 1
                break

        if is_central:
            continue

        pitch_radius = distance_xy(
            core_group["center_x"],
            core_group["center_y"],
            circle["center_x"],
            circle["center_y"]
        )

        if (
            circle["radius"] <=
            outer_radius * 0.30 and
            pitch_radius >
            circle["radius"] * 2.0 and
            pitch_radius <
            outer_radius * 1.05
        ):
            item = {
                "circle": circle,
                "pitch_radius": pitch_radius
            }

            candidates.append(item)

    pattern_groups = []

    for candidate in candidates:
        matched_group = None

        for pattern_group in pattern_groups:
            radius_limit = max(
                0.05,
                pattern_group[
                    "reference_radius"
                ] * 0.05
            )

            pitch_limit = max(
                0.10,
                outer_radius * 0.03
            )

            if (
                math.fabs(
                    candidate["circle"]["radius"] -
                    pattern_group[
                        "reference_radius"
                    ]
                ) <= radius_limit and
                math.fabs(
                    candidate["pitch_radius"] -
                    pattern_group[
                        "reference_pitch"
                    ]
                ) <= pitch_limit
            ):
                matched_group = pattern_group
                break

        if matched_group is None:
            matched_group = {
                "reference_radius":
                    candidate["circle"]["radius"],
                "reference_pitch":
                    candidate["pitch_radius"],
                "items": []
            }

            pattern_groups.append(
                matched_group
            )

        matched_group["items"].append(
            candidate
        )

    best_group = None

    for pattern_group in pattern_groups:
        if (
            best_group is None or
            len(pattern_group["items"]) >
            len(best_group["items"])
        ):
            best_group = pattern_group

    if best_group is None:
        return None

    if len(best_group["items"]) < 4:
        return None

    bolt_radii = []
    pitch_radii = []
    bolt_handles = []

    for item in best_group["items"]:
        bolt_radii.append(
            item["circle"]["radius"]
        )

        pitch_radii.append(
            item["pitch_radius"]
        )

        bolt_handles.append(
            clean(
                item["circle"]["handle"]
            )
        )

    central_radii = []

    for circle in central_circles:
        central_radii.append(
            circle["radius"]
        )

    central_radii.sort()

    return {
        "center_x":
            core_group["center_x"],
        "center_y":
            core_group["center_y"],
        "central_count":
            len(central_circles),
        "central_radii":
            central_radii,
        "outer_radius":
            outer_radius,
        "bolt_count":
            len(best_group["items"]),
        "bolt_radius":
            average(bolt_radii),
        "pitch_radius":
            average(pitch_radii),
        "central_handles":
            central_handles,
        "bolt_handles":
            bolt_handles
    }

folder = os.path.dirname(OUTPUT)

if not os.path.exists(folder):
    os.makedirs(folder)

f = open(OUTPUT, "w")

try:
    drawing_extent = kcs_draft.element_extent_get()

    region = KcsCaptureRegion2D.CaptureRegion2D()
    region.SetRectangle(drawing_extent)
    region.SetInside()
    region.SetNoCut()

    result = capture_contours(region)
    handles = result[1:]

    circles = []
    failure_count = 0
    index = 0

    for handle in handles:
        index = index + 1

        try:
            circle = parse_circle(
                handle,
                index
            )

            if circle is not None:
                circles.append(circle)

        except:
            failure_count = failure_count + 1

    groups = group_concentric_circles(
        circles
    )

    candidates = []

    for group in groups:
        if len(group["circles"]) < 2:
            continue

        candidate = find_bolt_pattern(
            group,
            circles
        )

        if candidate is not None:
            candidates.append(candidate)

    write_line(
        f,
        "FORMAT\tAM_PIPE_FLANGE_CANDIDATES_V1"
    )

    write_line(
        f,
        "CAPTURED_CONTOUR_COUNT\t%s" %
        str(len(handles))
    )

    write_line(
        f,
        "CIRCLE_COUNT\t%s" %
        str(len(circles))
    )

    write_line(
        f,
        "CONCENTRIC_GROUP_COUNT\t%s" %
        str(len(groups))
    )

    write_line(
        f,
        "CANDIDATE_COUNT\t%s" %
        str(len(candidates))
    )

    write_line(
        f,
        "CANDIDATE_INDEX"
        "\tCENTER_X\tCENTER_Y"
        "\tCENTRAL_CIRCLE_COUNT"
        "\tCENTRAL_RADII"
        "\tOUTER_RADIUS"
        "\tBOLT_COUNT"
        "\tBOLT_RADIUS"
        "\tPITCH_RADIUS"
        "\tCONFIDENCE"
        "\tCENTRAL_HANDLES"
        "\tBOLT_HANDLES"
    )

    candidate_index = 0

    for candidate in candidates:
        candidate_index = (
            candidate_index + 1
        )

        radii_text = ""

        for radius in candidate[
            "central_radii"
        ]:
            if radii_text != "":
                radii_text = radii_text + ","

            radii_text = (
                radii_text +
                str(radius)
            )

        confidence = "HIGH"

        write_line(
            f,
            "%s\t%s\t%s\t%s\t%s"
            "\t%s\t%s\t%s\t%s\t%s"
            "\t%s\t%s" % (
                str(candidate_index),
                str(candidate["center_x"]),
                str(candidate["center_y"]),
                str(candidate[
                    "central_count"
                ]),
                radii_text,
                str(candidate[
                    "outer_radius"
                ]),
                str(candidate[
                    "bolt_count"
                ]),
                str(candidate[
                    "bolt_radius"
                ]),
                str(candidate[
                    "pitch_radius"
                ]),
                confidence,
                ",".join(
                    candidate[
                        "central_handles"
                    ]
                ),
                ",".join(
                    candidate[
                        "bolt_handles"
                    ]
                )
            )
        )

    write_line(f, "")
    write_line(f, "SUMMARY")

    write_line(
        f,
        "PARSE_FAILURE_COUNT\t%s" %
        str(failure_count)
    )

    write_line(
        f,
        "STATUS\tSUCCESS"
    )

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
        "STATUS\tFAILED"
    )

f.close()
