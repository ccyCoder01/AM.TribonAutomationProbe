# -*- coding: ascii -*-
import os
import math
import kcs_draft
import KcsCaptureRegion2D
import KcsContour2D
import KcsPoint2D

OUTPUT = r"C:\AM_TribonBridge\diagnostics\lifting-beam-candidates.tsv"

ARC_TOLERANCE = 0.000001
CLOSE_TOLERANCE = 0.001
COORD_TOLERANCE = 0.10
MIN_ASPECT_RATIO = 5.0

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

def nearly_equal(first, second):
    return (
        math.fabs(first - second) <=
        COORD_TOLERANCE
    )

def calculate_arc(
    start_point,
    end_point,
    amplitude
):
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

    middle_x = (
        start_point.X + end_point.X
    ) / 2.0

    middle_y = (
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
        middle_x -
        (
            normal_x *
            sign *
            center_offset
        )
    )

    center_y = (
        middle_y -
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

def parse_contour(handle, index):
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
    vertex_count = len(vertices)

    item = {
        "index": index,
        "handle": handle,
        "handle_name": clean(handle),
        "kind": "OTHER"
    }

    if vertex_count == 2:
        start_point = get_point(
            vertices[0]
        )

        end_point = get_point(
            vertices[1]
        )

        amplitude = get_amplitude(
            vertices[1]
        )

        if (
            start_point is None or
            end_point is None
        ):
            return item

        if math.fabs(amplitude) <= ARC_TOLERANCE:
            dx = end_point.X - start_point.X
            dy = end_point.Y - start_point.Y

            item["kind"] = "LINE"
            item["start"] = start_point
            item["end"] = end_point
            item["length"] = distance_xy(
                start_point.X,
                start_point.Y,
                end_point.X,
                end_point.Y
            )

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
                item["orientation"] = "OTHER"

        return item

    if vertex_count != 3:
        return item

    first_point = get_point(
        vertices[0]
    )

    middle_point = get_point(
        vertices[1]
    )

    last_point = get_point(
        vertices[2]
    )

    if (
        first_point is None or
        middle_point is None or
        last_point is None
    ):
        return item

    if distance_xy(
        first_point.X,
        first_point.Y,
        last_point.X,
        last_point.Y
    ) > CLOSE_TOLERANCE:
        return item

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
        return item

    first_arc = calculate_arc(
        first_point,
        middle_point,
        first_amplitude
    )

    second_arc = calculate_arc(
        middle_point,
        last_point,
        second_amplitude
    )

    if (
        first_arc is None or
        second_arc is None
    ):
        return item

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
        center_difference > COORD_TOLERANCE or
        radius_difference > COORD_TOLERANCE
    ):
        return item

    item["kind"] = "CIRCLE"
    item["center_x"] = (
        first_arc["center_x"] +
        second_arc["center_x"]
    ) / 2.0
    item["center_y"] = (
        first_arc["center_y"] +
        second_arc["center_y"]
    ) / 2.0
    item["radius"] = (
        first_arc["radius"] +
        second_arc["radius"]
    ) / 2.0

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

def find_end_circles(
    rectangle,
    circles
):
    first_end = []
    second_end = []

    if rectangle["orientation"] == "HORIZONTAL":
        major_length = rectangle["width"]
        minor_length = rectangle["height"]

        end_zone = major_length * 0.25
        perpendicular_limit = max(
            minor_length * 1.25,
            5.0
        )

        for circle in circles:
            if circle["center_x"] < (
                rectangle["x1"] -
                perpendicular_limit
            ):
                continue

            if circle["center_x"] > (
                rectangle["x2"] +
                perpendicular_limit
            ):
                continue

            if circle["center_y"] < rectangle["y1"]:
                perpendicular_distance = (
                    rectangle["y1"] -
                    circle["center_y"]
                )

            elif circle["center_y"] > rectangle["y2"]:
                perpendicular_distance = (
                    circle["center_y"] -
                    rectangle["y2"]
                )

            else:
                perpendicular_distance = 0.0

            if (
                perpendicular_distance >
                perpendicular_limit
            ):
                continue

            if circle["center_x"] <= (
                rectangle["x1"] +
                end_zone
            ):
                first_end.append(circle)

            elif circle["center_x"] >= (
                rectangle["x2"] -
                end_zone
            ):
                second_end.append(circle)

    else:
        major_length = rectangle["height"]
        minor_length = rectangle["width"]

        end_zone = major_length * 0.25
        perpendicular_limit = max(
            minor_length * 1.25,
            5.0
        )

        for circle in circles:
            if circle["center_y"] < (
                rectangle["y1"] -
                perpendicular_limit
            ):
                continue

            if circle["center_y"] > (
                rectangle["y2"] +
                perpendicular_limit
            ):
                continue

            if circle["center_x"] < rectangle["x1"]:
                perpendicular_distance = (
                    rectangle["x1"] -
                    circle["center_x"]
                )

            elif circle["center_x"] > rectangle["x2"]:
                perpendicular_distance = (
                    circle["center_x"] -
                    rectangle["x2"]
                )

            else:
                perpendicular_distance = 0.0

            if (
                perpendicular_distance >
                perpendicular_limit
            ):
                continue

            if circle["center_y"] <= (
                rectangle["y1"] +
                end_zone
            ):
                first_end.append(circle)

            elif circle["center_y"] >= (
                rectangle["y2"] -
                end_zone
            ):
                second_end.append(circle)

    return (
        first_end,
        second_end
    )

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

    capture_result = capture_contours(region)
    handles = capture_result[1:]

    horizontal_lines = []
    vertical_lines = []
    circles = []

    parse_failure_count = 0
    index = 0

    for handle in handles:
        index = index + 1

        try:
            item = parse_contour(
                handle,
                index
            )

            if item["kind"] == "CIRCLE":
                circles.append(item)

            elif item["kind"] == "LINE":
                if (
                    item["orientation"] ==
                    "HORIZONTAL"
                ):
                    horizontal_lines.append(item)

                elif (
                    item["orientation"] ==
                    "VERTICAL"
                ):
                    vertical_lines.append(item)

        except:
            parse_failure_count = (
                parse_failure_count + 1
            )

    rectangles = []
    rectangle_keys = {}

    first_index = 0

    while first_index < len(horizontal_lines):
        first_line = horizontal_lines[
            first_index
        ]

        second_index = first_index + 1

        while second_index < len(horizontal_lines):
            second_line = horizontal_lines[
                second_index
            ]

            if (
                nearly_equal(
                    first_line["x1"],
                    second_line["x1"]
                ) and
                nearly_equal(
                    first_line["x2"],
                    second_line["x2"]
                )
            ):
                width = (
                    first_line["x2"] -
                    first_line["x1"]
                )

                height = math.fabs(
                    first_line["y1"] -
                    second_line["y1"]
                )

                if (
                    height > COORD_TOLERANCE and
                    width / height >=
                    MIN_ASPECT_RATIO
                ):
                    left_line = find_vertical_connector(
                        vertical_lines,
                        first_line["x1"],
                        first_line["y1"],
                        second_line["y1"]
                    )

                    right_line = find_vertical_connector(
                        vertical_lines,
                        first_line["x2"],
                        first_line["y1"],
                        second_line["y1"]
                    )

                    if (
                        left_line is not None and
                        right_line is not None
                    ):
                        body_handles = [
                            first_line["handle_name"],
                            second_line["handle_name"],
                            left_line["handle_name"],
                            right_line["handle_name"]
                        ]

                        body_handles.sort()
                        key = ",".join(body_handles)

                        if not rectangle_keys.has_key(key):
                            rectangle_keys[key] = 1

                            rectangles.append({
                                "orientation":
                                    "HORIZONTAL",
                                "x1": first_line["x1"],
                                "y1": min(
                                    first_line["y1"],
                                    second_line["y1"]
                                ),
                                "x2": first_line["x2"],
                                "y2": max(
                                    first_line["y1"],
                                    second_line["y1"]
                                ),
                                "width": width,
                                "height": height,
                                "aspect_ratio":
                                    width / height,
                                "body_handles":
                                    body_handles
                            })

            second_index = second_index + 1

        first_index = first_index + 1

    first_index = 0

    while first_index < len(vertical_lines):
        first_line = vertical_lines[
            first_index
        ]

        second_index = first_index + 1

        while second_index < len(vertical_lines):
            second_line = vertical_lines[
                second_index
            ]

            if (
                nearly_equal(
                    first_line["y1"],
                    second_line["y1"]
                ) and
                nearly_equal(
                    first_line["y2"],
                    second_line["y2"]
                )
            ):
                width = math.fabs(
                    first_line["x1"] -
                    second_line["x1"]
                )

                height = (
                    first_line["y2"] -
                    first_line["y1"]
                )

                if (
                    width > COORD_TOLERANCE and
                    height / width >=
                    MIN_ASPECT_RATIO
                ):
                    bottom_line = find_horizontal_connector(
                        horizontal_lines,
                        first_line["y1"],
                        first_line["x1"],
                        second_line["x1"]
                    )

                    top_line = find_horizontal_connector(
                        horizontal_lines,
                        first_line["y2"],
                        first_line["x1"],
                        second_line["x1"]
                    )

                    if (
                        bottom_line is not None and
                        top_line is not None
                    ):
                        body_handles = [
                            first_line["handle_name"],
                            second_line["handle_name"],
                            bottom_line["handle_name"],
                            top_line["handle_name"]
                        ]

                        body_handles.sort()
                        key = ",".join(body_handles)

                        if not rectangle_keys.has_key(key):
                            rectangle_keys[key] = 1

                            rectangles.append({
                                "orientation":
                                    "VERTICAL",
                                "x1": min(
                                    first_line["x1"],
                                    second_line["x1"]
                                ),
                                "y1": first_line["y1"],
                                "x2": max(
                                    first_line["x1"],
                                    second_line["x1"]
                                ),
                                "y2": first_line["y2"],
                                "width": width,
                                "height": height,
                                "aspect_ratio":
                                    height / width,
                                "body_handles":
                                    body_handles
                            })

            second_index = second_index + 1

        first_index = first_index + 1

    candidates = []

    for rectangle in rectangles:
        (
            first_end,
            second_end
        ) = find_end_circles(
            rectangle,
            circles
        )

        if (
            len(first_end) == 0 or
            len(second_end) == 0
        ):
            continue

        first_handles = []
        second_handles = []

        for circle in first_end:
            first_handles.append(
                circle["handle_name"]
            )

        for circle in second_end:
            second_handles.append(
                circle["handle_name"]
            )

        first_handles.sort()
        second_handles.sort()

        candidates.append({
            "rectangle": rectangle,
            "first_end_count":
                len(first_end),
            "second_end_count":
                len(second_end),
            "first_handles":
                first_handles,
            "second_handles":
                second_handles
        })

    write_line(
        f,
        "FORMAT\tAM_LIFTING_BEAM_CANDIDATES_V1"
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
        "CIRCLE_COUNT\t%s" %
        str(len(circles))
    )

    write_line(
        f,
        "SLENDER_RECTANGLE_COUNT\t%s" %
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
        "\tX1\tY1\tX2\tY2"
        "\tWIDTH\tHEIGHT"
        "\tASPECT_RATIO"
        "\tFIRST_END_CIRCLE_COUNT"
        "\tSECOND_END_CIRCLE_COUNT"
        "\tCONFIDENCE"
        "\tBODY_HANDLES"
        "\tFIRST_END_CIRCLE_HANDLES"
        "\tSECOND_END_CIRCLE_HANDLES"
    )

    candidate_index = 0

    for candidate in candidates:
        candidate_index = (
            candidate_index + 1
        )

        rectangle = candidate[
            "rectangle"
        ]

        write_line(
            f,
            "%s\t%s"
            "\t%s\t%s\t%s\t%s"
            "\t%s\t%s\t%s"
            "\t%s\t%s"
            "\tHIGH"
            "\t%s\t%s\t%s" % (
                str(candidate_index),
                rectangle["orientation"],
                str(rectangle["x1"]),
                str(rectangle["y1"]),
                str(rectangle["x2"]),
                str(rectangle["y2"]),
                str(rectangle["width"]),
                str(rectangle["height"]),
                str(rectangle[
                    "aspect_ratio"
                ]),
                str(candidate[
                    "first_end_count"
                ]),
                str(candidate[
                    "second_end_count"
                ]),
                ",".join(
                    rectangle[
                        "body_handles"
                    ]
                ),
                ",".join(
                    candidate[
                        "first_handles"
                    ]
                ),
                ",".join(
                    candidate[
                        "second_handles"
                    ]
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
