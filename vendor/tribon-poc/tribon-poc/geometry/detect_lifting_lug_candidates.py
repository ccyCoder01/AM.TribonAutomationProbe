# -*- coding: ascii -*-
import os
import math
import kcs_draft
import KcsCaptureRegion2D
import KcsContour2D
import KcsPoint2D

OUTPUT = r"C:\AM_TribonBridge\diagnostics\lifting-lug-candidates.tsv"

ARC_TOLERANCE = 0.000001
CLOSE_TOLERANCE = 0.001
POINT_TOLERANCE = 0.10
CENTER_TOLERANCE = 0.05
ORIENTATION_TOLERANCE = 0.05

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

def point_distance(first, second):
    return distance_xy(
        first.X,
        first.Y,
        second.X,
        second.Y
    )

def points_near(first, second, tolerance):
    return (
        point_distance(first, second) <=
        tolerance
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
        "radius": radius,
        "start": start_point,
        "end": end_point
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
        "vertices": vertices,
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
            item["kind"] = "LINE"
            item["start"] = start_point
            item["end"] = end_point
            item["length"] = point_distance(
                start_point,
                end_point
            )

            return item

        arc = calculate_arc(
            start_point,
            end_point,
            amplitude
        )

        if arc is not None:
            item["kind"] = "OPEN_ARC"
            item["arc"] = arc
            item["amplitude"] = amplitude

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

    if (
        point_distance(
            first_point,
            last_point
        ) > CLOSE_TOLERANCE
    ):
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
        center_difference > CENTER_TOLERANCE or
        radius_difference > CENTER_TOLERANCE
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

def is_vertical(line):
    return (
        math.fabs(
            line["start"].X -
            line["end"].X
        ) <= ORIENTATION_TOLERANCE
    )

def is_horizontal(line):
    return (
        math.fabs(
            line["start"].Y -
            line["end"].Y
        ) <= ORIENTATION_TOLERANCE
    )

def find_line_from_point(
    lines,
    target_point,
    required_vertical
):
    matches = []

    for line in lines:
        if required_vertical and not is_vertical(line):
            continue

        if points_near(
            line["start"],
            target_point,
            POINT_TOLERANCE
        ):
            matches.append({
                "line": line,
                "other": line["end"]
            })

        elif points_near(
            line["end"],
            target_point,
            POINT_TOLERANCE
        ):
            matches.append({
                "line": line,
                "other": line["start"]
            })

    return matches

def find_bottom_line(
    lines,
    first_point,
    second_point
):
    for line in lines:
        if not is_horizontal(line):
            continue

        direct_match = (
            points_near(
                line["start"],
                first_point,
                POINT_TOLERANCE
            ) and
            points_near(
                line["end"],
                second_point,
                POINT_TOLERANCE
            )
        )

        reverse_match = (
            points_near(
                line["start"],
                second_point,
                POINT_TOLERANCE
            ) and
            points_near(
                line["end"],
                first_point,
                POINT_TOLERANCE
            )
        )

        if direct_match or reverse_match:
            return line

    return None

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

    circles = []
    open_arcs = []
    lines = []

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

            elif item["kind"] == "OPEN_ARC":
                open_arcs.append(item)

            elif item["kind"] == "LINE":
                lines.append(item)

        except:
            parse_failure_count = (
                parse_failure_count + 1
            )

    candidates = []
    used_keys = {}

    for circle in circles:
        for arc_item in open_arcs:
            arc = arc_item["arc"]

            center_distance = distance_xy(
                circle["center_x"],
                circle["center_y"],
                arc["center_x"],
                arc["center_y"]
            )

            if center_distance > CENTER_TOLERANCE:
                continue

            if (
                arc["radius"] <
                circle["radius"] * 1.5
            ):
                continue

            first_side_matches = (
                find_line_from_point(
                    lines,
                    arc["start"],
                    1
                )
            )

            second_side_matches = (
                find_line_from_point(
                    lines,
                    arc["end"],
                    1
                )
            )

            for first_side in first_side_matches:
                for second_side in second_side_matches:
                    first_delta_y = (
                        first_side["other"].Y -
                        arc["start"].Y
                    )

                    second_delta_y = (
                        second_side["other"].Y -
                        arc["end"].Y
                    )

                    if (
                        math.fabs(first_delta_y) <
                        circle["radius"] * 2.0 or
                        math.fabs(second_delta_y) <
                        circle["radius"] * 2.0
                    ):
                        continue

                    if (
                        first_delta_y *
                        second_delta_y <= 0.0
                    ):
                        continue

                    bottom_line = find_bottom_line(
                        lines,
                        first_side["other"],
                        second_side["other"]
                    )

                    if bottom_line is None:
                        continue

                    handles_for_key = [
                        circle["handle_name"],
                        arc_item["handle_name"],
                        first_side["line"][
                            "handle_name"
                        ],
                        second_side["line"][
                            "handle_name"
                        ],
                        bottom_line["handle_name"]
                    ]

                    handles_for_key.sort()
                    key = ",".join(handles_for_key)

                    if used_keys.has_key(key):
                        continue

                    used_keys[key] = 1

                    min_x = min(
                        arc["start"].X,
                        arc["end"].X,
                        first_side["other"].X,
                        second_side["other"].X
                    )

                    max_x = max(
                        arc["start"].X,
                        arc["end"].X,
                        first_side["other"].X,
                        second_side["other"].X
                    )

                    min_y = min(
                        arc["start"].Y,
                        arc["end"].Y,
                        first_side["other"].Y,
                        second_side["other"].Y
                    )

                    max_y = max(
                        arc["start"].Y,
                        arc["end"].Y,
                        first_side["other"].Y,
                        second_side["other"].Y
                    )

                    candidates.append({
                        "center_x":
                            circle["center_x"],
                        "center_y":
                            circle["center_y"],
                        "hole_radius":
                            circle["radius"],
                        "outer_radius":
                            arc["radius"],
                        "width":
                            max_x - min_x,
                        "height":
                            max_y - min_y,
                        "x1": min_x,
                        "y1": min_y,
                        "x2": max_x,
                        "y2": max_y,
                        "circle_handle":
                            circle["handle_name"],
                        "arc_handle":
                            arc_item["handle_name"],
                        "side_1_handle":
                            first_side["line"][
                                "handle_name"
                            ],
                        "side_2_handle":
                            second_side["line"][
                                "handle_name"
                            ],
                        "bottom_handle":
                            bottom_line[
                                "handle_name"
                            ]
                    })

    write_line(
        f,
        "FORMAT\tAM_LIFTING_LUG_CANDIDATES_V1"
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
        "OPEN_ARC_COUNT\t%s" %
        str(len(open_arcs))
    )

    write_line(
        f,
        "LINE_COUNT\t%s" %
        str(len(lines))
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
        "\tHOLE_RADIUS\tOUTER_RADIUS"
        "\tWIDTH\tHEIGHT"
        "\tX1\tY1\tX2\tY2"
        "\tCONFIDENCE"
        "\tCIRCLE_HANDLE"
        "\tARC_HANDLE"
        "\tSIDE_1_HANDLE"
        "\tSIDE_2_HANDLE"
        "\tBOTTOM_HANDLE"
    )

    candidate_index = 0

    for candidate in candidates:
        candidate_index = (
            candidate_index + 1
        )

        write_line(
            f,
            "%s\t%s\t%s\t%s\t%s"
            "\t%s\t%s"
            "\t%s\t%s\t%s\t%s"
            "\tHIGH"
            "\t%s\t%s\t%s\t%s\t%s" % (
                str(candidate_index),
                str(candidate["center_x"]),
                str(candidate["center_y"]),
                str(candidate["hole_radius"]),
                str(candidate["outer_radius"]),
                str(candidate["width"]),
                str(candidate["height"]),
                str(candidate["x1"]),
                str(candidate["y1"]),
                str(candidate["x2"]),
                str(candidate["y2"]),
                candidate["circle_handle"],
                candidate["arc_handle"],
                candidate["side_1_handle"],
                candidate["side_2_handle"],
                candidate["bottom_handle"]
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
