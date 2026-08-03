# -*- coding: ascii -*-
import os
import kcs_draft
import kcs_ui
import kcs_util
import KcsCaptureRegion2D
import KcsColour
import KcsText
import KcsPoint2D

OUTPUT = r"C:\AM_TribonBridge\diagnostics\all-object-labels-result.tsv"

TOLERANCE = 0.05
LABEL_OFFSET_Y = 3.0
LABEL_HEIGHT = 3.0
LABEL_COLOUR = "Yellow"

COLOUR_CATEGORY = {
    "Red": "LIFTING_BEAM",
    "Magenta": "LIFTING_LUG",
    "Cyan": "PIPE_FLANGE",
    "Blue": "STRUCTURAL_FLANGE"
}

PREFIX_CATEGORY = {
    "LB-": "LIFTING_BEAM",
    "LL-": "LIFTING_LUG",
    "PF-": "PIPE_FLANGE",
    "SF-": "STRUCTURAL_FLANGE"
}

EXPECTED_COUNTS = {
    "LIFTING_BEAM": 2,
    "LIFTING_LUG": 3,
    "PIPE_FLANGE": 4,
    "STRUCTURAL_FLANGE": 3
}

def clean(value):
    try:
        result = str(value)
    except:
        result = "<conversion_failed>"

    result = result.replace("\t", " ")
    result = result.replace("\r", " ")
    result = result.replace("\n", " ")
    return result

def capture_safely(kind, region):
    try:
        if kind == "geometry":
            return kcs_draft.geometry_capture(region)

        if kind == "text":
            return kcs_draft.text_capture(region)

    except:
        if str(kcs_draft.error) == "kcs_NotFound":
            return [0]

        raise

    return [0]

def get_extent(handle):
    extent = kcs_draft.element_extent_get(handle)

    return (
        min(extent.Corner1.X, extent.Corner2.X),
        min(extent.Corner1.Y, extent.Corner2.Y),
        max(extent.Corner1.X, extent.Corner2.X),
        max(extent.Corner1.Y, extent.Corner2.Y)
    )

def union_extent(first, second):
    if first is None:
        return second

    return (
        min(first[0], second[0]),
        min(first[1], second[1]),
        max(first[2], second[2]),
        max(first[3], second[3])
    )

def center_of(extent):
    return (
        (extent[0] + extent[2]) / 2.0,
        (extent[1] + extent[3]) / 2.0
    )

def extents_connected(first, second):
    horizontal_gap = 0.0
    vertical_gap = 0.0

    if first[2] < second[0]:
        horizontal_gap = second[0] - first[2]
    elif second[2] < first[0]:
        horizontal_gap = first[0] - second[2]

    if first[3] < second[1]:
        vertical_gap = second[1] - first[3]
    elif second[3] < first[1]:
        vertical_gap = first[1] - second[3]

    return (
        horizontal_gap <= TOLERANCE and
        vertical_gap <= TOLERANCE
    )

def point_to_extent_distance_squared(point, extent):
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

    return (dx * dx) + (dy * dy)

def get_text_category(value):
    upper_value = value.upper()

    prefixes = PREFIX_CATEGORY.keys()
    prefixes.sort()

    for prefix in prefixes:
        if upper_value.startswith(prefix):
            return PREFIX_CATEGORY[prefix]

    return ""

def inspect_texts(region):
    result = capture_safely("text", region)

    text_handle_keys = {}
    source_labels = []
    auto_labels = {}

    for handle in result[1:]:
        handle_key = str(handle)
        text_handle_keys[handle_key] = 1

        try:
            text = KcsText.Text()

            text = kcs_draft.text_properties_get(
                handle,
                text
            )

            value = clean(text.GetString())

            if value.startswith("AUTO-"):
                if not auto_labels.has_key(value):
                    auto_labels[value] = []

                position = text.GetPosition()

                auto_labels[value].append({
                    "handle": handle_key,
                    "x": position.X,
                    "y": position.Y,
                    "height": text.GetHeight(),
                    "colour": text.GetColour().GetName()
                })

            category = get_text_category(value)

            if category != "":
                extent = get_extent(handle)

                source_labels.append({
                    "id": value,
                    "category": category,
                    "handle": handle_key,
                    "center": center_of(extent)
                })

        except:
            pass

    return (
        text_handle_keys,
        source_labels,
        auto_labels
    )

def build_components(
    geometry_handles,
    text_handle_keys
):
    items_by_category = {}

    for category in EXPECTED_COUNTS.keys():
        items_by_category[category] = []

    for handle in geometry_handles:
        handle_key = str(handle)

        if text_handle_keys.has_key(handle_key):
            continue

        try:
            colour = KcsColour.Colour()

            kcs_draft.element_colour_get(
                handle,
                colour
            )

            colour_name = colour.GetName()

            if not COLOUR_CATEGORY.has_key(
                colour_name
            ):
                continue

            category = COLOUR_CATEGORY[colour_name]

            items_by_category[category].append({
                "handle": handle_key,
                "extent": get_extent(handle)
            })

        except:
            pass

    components = []

    for category in EXPECTED_COUNTS.keys():
        items = items_by_category[category]
        visited = {}

        for start_index in range(len(items)):
            if visited.has_key(start_index):
                continue

            queue = [start_index]
            visited[start_index] = 1

            handles = []
            extent = None

            while len(queue) > 0:
                current_index = queue[0]
                del queue[0]

                current = items[current_index]

                handles.append(current["handle"])

                extent = union_extent(
                    extent,
                    current["extent"]
                )

                for candidate_index in range(
                    len(items)
                ):
                    if visited.has_key(
                        candidate_index
                    ):
                        continue

                    candidate = items[candidate_index]

                    if extents_connected(
                        current["extent"],
                        candidate["extent"]
                    ):
                        visited[candidate_index] = 1
                        queue.append(candidate_index)

            components.append({
                "category": category,
                "extent": extent,
                "handles": handles,
                "object_id": "",
                "source_label_handle": "",
                "source_label_distance": None
            })

    return components

def assign_source_labels(
    components,
    source_labels
):
    candidates = []

    for component_index in range(
        len(components)
    ):
        component = components[component_index]

        for label_index in range(
            len(source_labels)
        ):
            label = source_labels[label_index]

            if (
                label["category"] !=
                component["category"]
            ):
                continue

            distance = (
                point_to_extent_distance_squared(
                    label["center"],
                    component["extent"]
                )
            )

            candidates.append({
                "distance": distance,
                "component_index": component_index,
                "label_index": label_index
            })

    candidates.sort(
        lambda first, second:
        cmp(first["distance"], second["distance"])
    )

    used_components = {}
    used_labels = {}

    for candidate in candidates:
        component_index = candidate[
            "component_index"
        ]

        label_index = candidate["label_index"]

        if used_components.has_key(
            component_index
        ):
            continue

        if used_labels.has_key(label_index):
            continue

        component = components[component_index]
        label = source_labels[label_index]

        component["object_id"] = label["id"]
        component["source_label_handle"] = (
            label["handle"]
        )

        component["source_label_distance"] = (
            candidate["distance"]
        )

        used_components[component_index] = 1
        used_labels[label_index] = 1

def create_label(value, x, y):
    text = KcsText.Text()
    text.SetString(value)

    point = KcsPoint2D.Point2D()
    point.X = x
    point.Y = y

    text.SetPosition(point)
    text.SetHeight(LABEL_HEIGHT)

    text.SetColour(
        KcsColour.Colour(LABEL_COLOUR)
    )

    kcs_draft.text_new(text)

folder = os.path.dirname(OUTPUT)

if not os.path.exists(folder):
    os.makedirs(folder)

f = open(OUTPUT, "w")

try:
    drawing_extent_object = (
        kcs_draft.element_extent_get()
    )

    drawing_extent = (
        min(
            drawing_extent_object.Corner1.X,
            drawing_extent_object.Corner2.X
        ),
        min(
            drawing_extent_object.Corner1.Y,
            drawing_extent_object.Corner2.Y
        ),
        max(
            drawing_extent_object.Corner1.X,
            drawing_extent_object.Corner2.X
        ),
        max(
            drawing_extent_object.Corner1.Y,
            drawing_extent_object.Corner2.Y
        )
    )

    region = KcsCaptureRegion2D.CaptureRegion2D()
    region.SetRectangle(drawing_extent_object)
    region.SetInside()
    region.SetNoCut()

    geometry_result = capture_safely(
        "geometry",
        region
    )

    (
        text_handle_keys,
        source_labels,
        auto_labels
    ) = inspect_texts(region)

    components = build_components(
        geometry_result[1:],
        text_handle_keys
    )

    assign_source_labels(
        components,
        source_labels
    )

    valid_components = []

    for component in components:
        if component["object_id"] != "":
            valid_components.append(component)

    valid_components.sort(
        lambda first, second:
        cmp(
            first["object_id"],
            second["object_id"]
        )
    )

    duplicate_count = 0
    already_present_count = 0
    missing_components = []

    for component in valid_components:
        auto_text = (
            "AUTO-" + component["object_id"]
        )

        existing_count = 0

        if auto_labels.has_key(auto_text):
            existing_count = len(
                auto_labels[auto_text]
            )

        if existing_count > 1:
            duplicate_count = (
                duplicate_count + 1
            )

        elif existing_count == 1:
            already_present_count = (
                already_present_count + 1
            )

        else:
            missing_components.append(
                component
            )

    f.write("FORMAT\tAM_OBJECT_LABEL_WRITE_V1\n")

    f.write(
        "COMPONENT_COUNT\t%s\n" %
        str(len(components))
    )

    f.write(
        "VALID_COMPONENT_COUNT\t%s\n" %
        str(len(valid_components))
    )

    f.write(
        "ALREADY_PRESENT_COUNT\t%s\n" %
        str(already_present_count)
    )

    f.write(
        "MISSING_COUNT\t%s\n" %
        str(len(missing_components))
    )

    f.write(
        "DUPLICATE_COUNT\t%s\n" %
        str(duplicate_count)
    )

    if duplicate_count > 0:
        f.write("WRITE_PERFORMED\t0\n")
        f.write(
            "STATUS\tFAILED_DUPLICATE_PRECHECK\n"
        )

    elif len(valid_components) != 12:
        f.write("WRITE_PERFORMED\t0\n")
        f.write(
            "STATUS\tFAILED_COMPONENT_COUNT\n"
        )

    elif len(missing_components) == 0:
        f.write("WRITE_PERFORMED\t0\n")
        f.write("STATUS\tALREADY_COMPLETE\n")

    else:
        answer = kcs_ui.answer_req(
            "AM object labels",
            "Create %d missing AUTO labels?" %
            len(missing_components)
        )

        if answer != kcs_util.yes():
            f.write("WRITE_PERFORMED\t0\n")
            f.write("STATUS\tCANCELLED\n")

        else:
            created_count = 0
            failed_count = 0

            f.write(
                "OBJECT_ID\tCATEGORY"
                "\tAUTO_TEXT"
                "\tX\tY"
                "\tGEOMETRY_COUNT"
                "\tRESULT\n"
            )

            for component in missing_components:
                object_id = component["object_id"]
                auto_text = "AUTO-" + object_id
                extent = component["extent"]

                x = extent[0]
                y = extent[3] + LABEL_OFFSET_Y

                maximum_y = (
                    drawing_extent[3] -
                    LABEL_HEIGHT
                )

                if y > maximum_y:
                    y = maximum_y

                try:
                    create_label(
                        auto_text,
                        x,
                        y
                    )

                    created_count = (
                        created_count + 1
                    )

                    result = "CREATED"

                except Exception, e:
                    failed_count = (
                        failed_count + 1
                    )

                    result = (
                        "FAILED:" + clean(e)
                    )

                f.write(
                    "%s\t%s\t%s"
                    "\t%s\t%s\t%s\t%s\n" % (
                        object_id,
                        component["category"],
                        auto_text,
                        str(x),
                        str(y),
                        str(len(
                            component["handles"]
                        )),
                        result
                    )
                )

            kcs_ui.app_window_refresh()

            (
                post_text_handle_keys,
                post_source_labels,
                post_auto_labels
            ) = inspect_texts(region)

            post_valid_count = 0
            post_duplicate_count = 0
            property_error_count = 0

            for component in valid_components:
                auto_text = (
                    "AUTO-" +
                    component["object_id"]
                )

                matches = []

                if post_auto_labels.has_key(
                    auto_text
                ):
                    matches = post_auto_labels[
                        auto_text
                    ]

                if len(matches) == 1:
                    post_valid_count = (
                        post_valid_count + 1
                    )

                    item = matches[0]

                    if (
                        abs(
                            item["height"] -
                            LABEL_HEIGHT
                        ) > 0.01 or
                        item["colour"] !=
                        LABEL_COLOUR
                    ):
                        property_error_count = (
                            property_error_count + 1
                        )

                elif len(matches) > 1:
                    post_duplicate_count = (
                        post_duplicate_count + 1
                    )

            f.write(
                "WRITE_PERFORMED\t1\n"
            )

            f.write(
                "CREATED_COUNT\t%s\n" %
                str(created_count)
            )

            f.write(
                "FAILED_COUNT\t%s\n" %
                str(failed_count)
            )

            f.write(
                "POST_VALID_LABEL_COUNT\t%s\n" %
                str(post_valid_count)
            )

            f.write(
                "POST_DUPLICATE_COUNT\t%s\n" %
                str(post_duplicate_count)
            )

            f.write(
                "PROPERTY_ERROR_COUNT\t%s\n" %
                str(property_error_count)
            )

            if (
                created_count ==
                len(missing_components) and
                failed_count == 0 and
                post_valid_count == 12 and
                post_duplicate_count == 0 and
                property_error_count == 0
            ):
                f.write("STATUS\tSUCCESS\n")
            else:
                f.write(
                    "STATUS\tFAILED_POSTCHECK\n"
                )

except Exception, e:
    f.write(
        "ERROR\t%s\n" %
        clean(e)
    )

    f.write(
        "KCS_ERROR\t%s\n" %
        clean(kcs_draft.error)
    )

    f.write("STATUS\tFAILED\n")

f.close()
