# -*- coding: ascii -*-
import os
import math
import kcs_draft
import KcsCaptureRegion2D
import KcsContour2D
import KcsPoint2D

OUTPUT = r"C:\AM_TribonBridge\diagnostics\pipe-flange-side-candidates.tsv"

ARC_TOLERANCE = 0.000001
COORD_TOLERANCE = 0.10

MIN_PLATE_ASPECT_RATIO = 3.0
MAX_PLATE_ASPECT_RATIO = 12.0
MAX_HUB_ASPECT_RATIO = 2.5

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

def nearly_equal(first, second):
    return (
        math.fabs(first - second) <=
        COORD_TOLERANCE
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

def capture_contours(region):
    try:
        return kcs_draft.contour_capture(region)
    except:
        if str(kcs_draft.error) == "kcs_NotFound":
            return [0]

        raise

def parse_axis_line(handle, index):
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

    if len(vertices) != 2:
        return None

    start_point = get_point(vertices[0])
    end_point = get_point(vertices[1])

    if (
        start_point is None or
        end_point is None
    ):
        return None

    amplitude = get_amplitude(
        vertices[1]
    )

    if math.fabs(amplitude) > ARC_TOLERANCE:
        return None

    dx = end_point.X - start_point.X
    dy = end_point.Y - start_point.Y

    item = {
        "index": index,
        "handle": handle,
        "handle_name": clean(handle)
    }

    if math.fabs(dy) <= COORD_TOLERANCE:
        item["orientation"] = "HORIZONTAL"
        item["x1"] = min(
            start_point.X,
            end_point.X
        )
        item["x2"] = max(
            start_point.X,
            end_point.X
        )
        item["y1"] = (
            start_point.Y +
            end_point.Y
        ) / 2.0
        item["y2"] = item["y1"]

    elif math.fabs(dx) <= COORD_TOLERANCE:
        item["orientation"] = "VERTICAL"
        item["x1"] = (
            start_point.X +
            end_point.X
        ) / 2.0
        item["x2"] = item["x1"]
        item["y1"] = min(
            start_point.Y,
            end_point.Y
        )
        item["y2"] = max(
            start_point.Y,
            end_point.Y
        )

    else:
        return None

    return item

def find_vertical_connector(
    vertical_lines,
    x_value,
    y1,
    y2
):
    minimum_y = min(y1, y2)
    maximum_y = max(y1, y2)

    for line in vertical_lines:
        if not nearly_equal(
            line["x1"],
            x_value
        ):
            continue

        if (
            nearly_equal(
                line["y1"],
                minimum_y
            ) and
            nearly_equal(
                line["y2"],
                maximum_y
            )
        ):
            return line

    return None

def find_horizontal_connector(
    horizontal_lines,
    y_value,
    x1,
    x2
):
    minimum_x = min(x1, x2)
    maximum_x = max(x1, x2)

    for line in horizontal_lines:
        if not nearly_equal(
            line["y1"],
            y_value
        ):
            continue

        if (
            nearly_equal(
                line["x1"],
                minimum_x
            ) and
            nearly_equal(
                line["x2"],
                maximum_x
            )
        ):
            return line

    return None

def detect_rectangles(
    horizontal_lines,
    vertical_lines
):
    rectangles = []
    keys = {}

    first_index = 0

    while first_index < len(horizontal_lines):
        first = horizontal_lines[first_index]
        second_index = first_index + 1

        while second_index < len(horizontal_lines):
            second = horizontal_lines[
                second_index
            ]

            if (
                nearly_equal(
                    first["x1"],
                    second["x1"]
                ) and
                nearly_equal(
                    first["x2"],
                    second["x2"]
                ) and
                not nearly_equal(
                    first["y1"],
                    second["y1"]
                )
            ):
                left = find_vertical_connector(
                    vertical_lines,
                    first["x1"],
                    first["y1"],
                    second["y1"]
                )

                right = find_vertical_connector(
                    vertical_lines,
                    first["x2"],
                    first["y1"],
                    second["y1"]
                )

                if (
                    left is not None and
                    right is not None
                ):
                    handles = [
                        first["handle_name"],
                        second["handle_name"],
                        left["handle_name"],
                        right["handle_name"]
                    ]

                    handles.sort()
                    key = ",".join(handles)

                    if not keys.has_key(key):
                        keys[key] = 1

                        x1 = first["x1"]
                        x2 = first["x2"]
                        y1 = min(
                            first["y1"],
                            second["y1"]
                        )
                        y2 = max(
                            first["y1"],
                            second["y1"]
                        )

                        width = x2 - x1
                        height = y2 - y1

                        rectangles.append({
                            "x1": x1,
                            "y1": y1,
                            "x2": x2,
                            "y2": y2,
                            "width": width,
                            "height": height,
                            "center_x":
                                (x1 + x2) / 2.0,
                            "center_y":
                                (y1 + y2) / 2.0,
                            "handles": handles
                        })

            second_index = second_index + 1

        first_index = first_index + 1

    return rectangles

def aspect_ratio(rectangle):
    minimum_dimension = min(
        rectangle["width"],
        rectangle["height"]
    )

    maximum_dimension = max(
        rectangle["width"],
        rectangle["height"]
    )

    if minimum_dimension <= COORD_TOLERANCE:
        return 999999.0

    return (
        maximum_dimension /
        minimum_dimension
    )

def rectangles_share_center(
    first,
    second
):
    return (
        nearly_equal(
            first["center_x"],
            second["center_x"]
        ) and
        nearly_equal(
            first["center_y"],
            second["center_y"]
        )
    )

def find_horizontal_extensions(
    horizontal_lines,
    plate
):
    center_y = plate["center_y"]
    left_extension = None
    right_extension = None

    for line in horizontal_lines:
        if not nearly_equal(
            line["y1"],
            center_y
        ):
            continue

        if (
            nearly_equal(
                line["x2"],
                plate["x1"]
            ) and
            line["x1"] <
            plate["x1"] - COORD_TOLERANCE
        ):
            left_extension = line

        if (
            nearly_equal(
                line["x1"],
                plate["x2"]
            ) and
            line["x2"] >
            plate["x2"] + COORD_TOLERANCE
        ):
            right_extension = line

    return (
        left_extension,
        right_extension
    )

def find_vertical_extensions(
    vertical_lines,
    plate
):
    center_x = plate["center_x"]
    lower_extension = None
    upper_extension = None

    for line in vertical_lines:
        if not nearly_equal(
            line["x1"],
            center_x
        ):
            continue

        if (
            nearly_equal(
                line["y2"],
                plate["y1"]
            ) and
            line["y1"] <
            plate["y1"] - COORD_TOLERANCE
        ):
            lower_extension = line

        if (
            nearly_equal(
                line["y1"],
                plate["y2"]
            ) and
            line["y2"] >
            plate["y2"] + COORD_TOLERANCE
        ):
            upper_extension = line

    return (
        lower_extension,
        upper_extension
    )

folder = os.path.dirname(OUTPUT)

if not os.path.exists(folder):
    os.makedirs(folder)

f = open(OUTPUT, "w")

try:
    drawing_extent = (
        kcs_draft.element_extent_get()
    )

    region = (
        KcsCaptureRegion2D.CaptureRegion2D()
    )

    region.SetRectangle(drawing_extent)
    region.SetInside()
    region.SetNoCut()

    capture_result = capture_contours(
        region
    )

    handles = capture_result[1:]

    horizontal_lines = []
    vertical_lines = []
    parse_failure_count = 0
    index = 0

    for handle in handles:
        index = index + 1

        try:
            line = parse_axis_line(
                handle,
                index
            )

            if line is None:
                continue

            if (
                line["orientation"] ==
                "HORIZONTAL"
            ):
                horizontal_lines.append(line)
            else:
                vertical_lines.append(line)

        except:
            parse_failure_count = (
                parse_failure_count + 1
            )

    rectangles = detect_rectangles(
        horizontal_lines,
        vertical_lines
    )

    candidates = []
    candidate_keys = {}

    for plate in rectangles:
        plate_ratio = aspect_ratio(
            plate
        )

        if (
            plate_ratio <
            MIN_PLATE_ASPECT_RATIO or
            plate_ratio >
            MAX_PLATE_ASPECT_RATIO
        ):
            continue

        if plate["width"] >= plate["height"]:
            orientation = "HORIZONTAL"
            plate_major = plate["width"]
            plate_thickness = plate["height"]
        else:
            orientation = "VERTICAL"
            plate_major = plate["height"]
            plate_thickness = plate["width"]

        for hub in rectangles:
            if hub is plate:
                continue

            if not rectangles_share_center(
                plate,
                hub
            ):
                continue

            hub_ratio = aspect_ratio(hub)

            if hub_ratio > MAX_HUB_ASPECT_RATIO:
                continue

            if orientation == "HORIZONTAL":
                if (
                    hub["height"] <=
                    plate["height"] * 1.25
                ):
                    continue

                if (
                    hub["width"] >=
                    plate_major * 0.80
                ):
                    continue

                (
                    first_extension,
                    second_extension
                ) = find_horizontal_extensions(
                    horizontal_lines,
                    plate
                )

            else:
                if (
                    hub["width"] <=
                    plate["width"] * 1.25
                ):
                    continue

                if (
                    hub["height"] >=
                    plate_major * 0.80
                ):
                    continue

                (
                    first_extension,
                    second_extension
                ) = find_vertical_extensions(
                    vertical_lines,
                    plate
                )

            if (
                first_extension is None or
                second_extension is None
            ):
                continue

            if orientation == "HORIZONTAL":
                first_extension_length = (
                    first_extension["x2"] -
                    first_extension["x1"]
                )

                second_extension_length = (
                    second_extension["x2"] -
                    second_extension["x1"]
                )
            else:
                first_extension_length = (
                    first_extension["y2"] -
                    first_extension["y1"]
                )

                second_extension_length = (
                    second_extension["y2"] -
                    second_extension["y1"]
                )

            if (
                first_extension_length <
                plate_thickness or
                second_extension_length <
                plate_thickness
            ):
                continue

            candidate_handles = []

            for handle_name in plate["handles"]:
                candidate_handles.append(
                    handle_name
                )

            for handle_name in hub["handles"]:
                candidate_handles.append(
                    handle_name
                )

            candidate_handles.append(
                first_extension["handle_name"]
            )

            candidate_handles.append(
                second_extension["handle_name"]
            )

            candidate_handles.sort()

            unique_handles = []
            seen_handles = {}

            for handle_name in candidate_handles:
                if seen_handles.has_key(
                    handle_name
                ):
                    continue

                seen_handles[handle_name] = 1
                unique_handles.append(
                    handle_name
                )

            key = ",".join(unique_handles)

            if candidate_keys.has_key(key):
                continue

            candidate_keys[key] = 1

            candidates.append({
                "orientation": orientation,
                "center_x": plate["center_x"],
                "center_y": plate["center_y"],
                "plate_width": plate["width"],
                "plate_height": plate["height"],
                "hub_width": hub["width"],
                "hub_height": hub["height"],
                "first_extension_length":
                    first_extension_length,
                "second_extension_length":
                    second_extension_length,
                "handles": unique_handles
            })

    write_line(
        f,
        "FORMAT\tAM_PIPE_FLANGE_SIDE_CANDIDATES_V1"
    )

    write_line(
        f,
        "CAPTURED_CONTOUR_COUNT\t%s" %
        str(len(handles))
    )

    write_line(
        f,
        "HORIZONTAL_LINE_COUNT\t%s" %
        str(len(horizontal_lines))
    )

    write_line(
        f,
        "VERTICAL_LINE_COUNT\t%s" %
        str(len(vertical_lines))
    )

    write_line(
        f,
        "RECTANGLE_COUNT\t%s" %
        str(len(rectangles))
    )

    write_line(
        f,
        "CANDIDATE_COUNT\t%s" %
        str(len(candidates))
    )

    write_line(
        f,
        "CANDIDATE_INDEX"
        "\tORIENTATION"
        "\tCENTER_X\tCENTER_Y"
        "\tPLATE_WIDTH\tPLATE_HEIGHT"
        "\tHUB_WIDTH\tHUB_HEIGHT"
        "\tFIRST_EXTENSION_LENGTH"
        "\tSECOND_EXTENSION_LENGTH"
        "\tCONFIDENCE"
        "\tHANDLES"
    )

    candidate_index = 0

    for candidate in candidates:
        candidate_index = (
            candidate_index + 1
        )

        write_line(
            f,
            "%s\t%s\t%s\t%s"
            "\t%s\t%s\t%s\t%s"
            "\t%s\t%s\tHIGH\t%s" % (
                str(candidate_index),
                candidate["orientation"],
                str(candidate["center_x"]),
                str(candidate["center_y"]),
                str(candidate["plate_width"]),
                str(candidate["plate_height"]),
                str(candidate["hub_width"]),
                str(candidate["hub_height"]),
                str(candidate[
                    "first_extension_length"
                ]),
                str(candidate[
                    "second_extension_length"
                ]),
                ",".join(
                    candidate["handles"]
                )
            )
        )

    write_line(f, "")
    write_line(f, "SUMMARY")

    write_line(
        f,
        "PARSE_FAILURE_COUNT\t%s" %
        str(parse_failure_count)
    )

    write_line(f, "STATUS\tSUCCESS")

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

    write_line(f, "STATUS\tFAILED")

f.close()
