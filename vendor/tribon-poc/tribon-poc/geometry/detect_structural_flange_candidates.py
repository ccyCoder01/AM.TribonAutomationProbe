# -*- coding: ascii -*-
import os
import math
import kcs_draft
import KcsCaptureRegion2D
import KcsContour2D
import KcsPoint2D

OUTPUT = r"C:\AM_TribonBridge\diagnostics\structural-flange-candidates.tsv"

ARC_TOLERANCE = 0.000001
COORD_TOLERANCE = 0.10

MIN_LINE_COUNT = 8
MIN_HORIZONTAL_COUNT = 3
MIN_VERTICAL_COUNT = 3
MIN_INTERNAL_LINE_COUNT = 3

MIN_DIMENSION = 6.0
MAX_DIMENSION = 80.0
MAX_ASPECT_RATIO = 4.0

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

def ranges_touch(
    first_start,
    first_end,
    second_start,
    second_end
):
    return not (
        first_end <
        second_start - COORD_TOLERANCE or
        second_end <
        first_start - COORD_TOLERANCE
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

    start_point = get_point(
        vertices[0]
    )

    end_point = get_point(
        vertices[1]
    )

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

def lines_connected(first, second):
    first_orientation = first[
        "orientation"
    ]

    second_orientation = second[
        "orientation"
    ]

    if (
        first_orientation == "HORIZONTAL" and
        second_orientation == "HORIZONTAL"
    ):
        return (
            nearly_equal(
                first["y1"],
                second["y1"]
            ) and
            ranges_touch(
                first["x1"],
                first["x2"],
                second["x1"],
                second["x2"]
            )
        )

    if (
        first_orientation == "VERTICAL" and
        second_orientation == "VERTICAL"
    ):
        return (
            nearly_equal(
                first["x1"],
                second["x1"]
            ) and
            ranges_touch(
                first["y1"],
                first["y2"],
                second["y1"],
                second["y2"]
            )
        )

    if first_orientation == "HORIZONTAL":
        horizontal = first
        vertical = second
    else:
        horizontal = second
        vertical = first

    horizontal_y = horizontal["y1"]
    vertical_x = vertical["x1"]

    return (
        vertical_x >=
        horizontal["x1"] - COORD_TOLERANCE and
        vertical_x <=
        horizontal["x2"] + COORD_TOLERANCE and
        horizontal_y >=
        vertical["y1"] - COORD_TOLERANCE and
        horizontal_y <=
        vertical["y2"] + COORD_TOLERANCE
    )

def build_components(lines):
    components = []
    visited = {}

    line_index = 0

    while line_index < len(lines):
        if visited.has_key(line_index):
            line_index = line_index + 1
            continue

        queue = [line_index]
        visited[line_index] = 1
        component = []

        while len(queue) > 0:
            current_index = queue[0]
            del queue[0]

            current_line = lines[
                current_index
            ]

            component.append(
                current_line
            )

            other_index = 0

            while other_index < len(lines):
                if not visited.has_key(
                    other_index
                ):
                    if lines_connected(
                        current_line,
                        lines[other_index]
                    ):
                        visited[other_index] = 1
                        queue.append(
                            other_index
                        )

                other_index = other_index + 1

        components.append(component)
        line_index = line_index + 1

    return components

def analyse_component(component):
    x_values = []
    y_values = []

    horizontal_count = 0
    vertical_count = 0
    total_length = 0.0
    handles = []

    for line in component:
        x_values.append(line["x1"])
        x_values.append(line["x2"])
        y_values.append(line["y1"])
        y_values.append(line["y2"])

        handles.append(
            line["handle_name"]
        )

        if (
            line["orientation"] ==
            "HORIZONTAL"
        ):
            horizontal_count = (
                horizontal_count + 1
            )

            total_length = (
                total_length +
                line["x2"] -
                line["x1"]
            )

        else:
            vertical_count = (
                vertical_count + 1
            )

            total_length = (
                total_length +
                line["y2"] -
                line["y1"]
            )

    x1 = min(x_values)
    y1 = min(y_values)
    x2 = max(x_values)
    y2 = max(y_values)

    width = x2 - x1
    height = y2 - y1

    minimum_dimension = min(
        width,
        height
    )

    maximum_dimension = max(
        width,
        height
    )

    if minimum_dimension <= COORD_TOLERANCE:
        aspect_ratio = 999999.0
    else:
        aspect_ratio = (
            maximum_dimension /
            minimum_dimension
        )

    internal_line_count = 0

    for line in component:
        if (
            line["orientation"] ==
            "HORIZONTAL"
        ):
            if (
                not nearly_equal(
                    line["y1"],
                    y1
                ) and
                not nearly_equal(
                    line["y1"],
                    y2
                )
            ):
                internal_line_count = (
                    internal_line_count + 1
                )

        else:
            if (
                not nearly_equal(
                    line["x1"],
                    x1
                ) and
                not nearly_equal(
                    line["x1"],
                    x2
                )
            ):
                internal_line_count = (
                    internal_line_count + 1
                )

    handles.sort()

    return {
        "line_count": len(component),
        "horizontal_count":
            horizontal_count,
        "vertical_count":
            vertical_count,
        "internal_line_count":
            internal_line_count,
        "x1": x1,
        "y1": y1,
        "x2": x2,
        "y2": y2,
        "width": width,
        "height": height,
        "aspect_ratio": aspect_ratio,
        "total_length": total_length,
        "handles": handles
    }

def is_candidate(feature):
    if (
        feature["line_count"] <
        MIN_LINE_COUNT
    ):
        return 0

    if (
        feature["horizontal_count"] <
        MIN_HORIZONTAL_COUNT or
        feature["vertical_count"] <
        MIN_VERTICAL_COUNT
    ):
        return 0

    if (
        feature["internal_line_count"] <
        MIN_INTERNAL_LINE_COUNT
    ):
        return 0

    minimum_dimension = min(
        feature["width"],
        feature["height"]
    )

    maximum_dimension = max(
        feature["width"],
        feature["height"]
    )

    if minimum_dimension < MIN_DIMENSION:
        return 0

    if maximum_dimension > MAX_DIMENSION:
        return 0

    if (
        feature["aspect_ratio"] >
        MAX_ASPECT_RATIO
    ):
        return 0

    return 1

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

    lines = []
    parse_failure_count = 0
    index = 0

    for handle in handles:
        index = index + 1

        try:
            line = parse_axis_line(
                handle,
                index
            )

            if line is not None:
                lines.append(line)

        except:
            parse_failure_count = (
                parse_failure_count + 1
            )

    components = build_components(
        lines
    )

    features = []
    candidates = []

    component_index = 0

    for component in components:
        component_index = (
            component_index + 1
        )

        feature = analyse_component(
            component
        )

        feature["component_index"] = (
            component_index
        )

        features.append(feature)

        if is_candidate(feature):
            candidates.append(feature)

    write_line(
        f,
        "FORMAT\tAM_STRUCTURAL_FLANGE_CANDIDATES_V1"
    )

    write_line(
        f,
        "CAPTURED_CONTOUR_COUNT\t%s" %
        str(len(handles))
    )

    write_line(
        f,
        "AXIS_LINE_COUNT\t%s" %
        str(len(lines))
    )

    write_line(
        f,
        "CONNECTED_COMPONENT_COUNT\t%s" %
        str(len(components))
    )

    write_line(
        f,
        "CANDIDATE_COUNT\t%s" %
        str(len(candidates))
    )

    write_line(
        f,
        "CANDIDATE_INDEX"
        "\tSOURCE_COMPONENT_INDEX"
        "\tX1\tY1\tX2\tY2"
        "\tWIDTH\tHEIGHT"
        "\tASPECT_RATIO"
        "\tLINE_COUNT"
        "\tHORIZONTAL_COUNT"
        "\tVERTICAL_COUNT"
        "\tINTERNAL_LINE_COUNT"
        "\tTOTAL_LINE_LENGTH"
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
            "%s\t%s"
            "\t%s\t%s\t%s\t%s"
            "\t%s\t%s\t%s"
            "\t%s\t%s\t%s\t%s"
            "\t%s\tHIGH\t%s" % (
                str(candidate_index),
                str(candidate[
                    "component_index"
                ]),
                str(candidate["x1"]),
                str(candidate["y1"]),
                str(candidate["x2"]),
                str(candidate["y2"]),
                str(candidate["width"]),
                str(candidate["height"]),
                str(candidate[
                    "aspect_ratio"
                ]),
                str(candidate[
                    "line_count"
                ]),
                str(candidate[
                    "horizontal_count"
                ]),
                str(candidate[
                    "vertical_count"
                ]),
                str(candidate[
                    "internal_line_count"
                ]),
                str(candidate[
                    "total_length"
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
