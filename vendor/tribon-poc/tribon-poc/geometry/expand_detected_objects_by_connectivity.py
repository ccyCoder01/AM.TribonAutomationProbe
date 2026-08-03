# -*- coding: ascii -*-
import os
import kcs_draft
import KcsCaptureRegion2D

OUTPUT = r"C:\AM_TribonBridge\diagnostics\geometry-object-expansion.tsv"

TOLERANCE = 0.05

CANDIDATE_SOURCES = [
    {
        "path": r"C:\AM_TribonBridge\diagnostics\pipe-flange-candidates.tsv",
        "category": "PIPE_FLANGE_FRONT",
        "handle_columns": [10, 11]
    },
    {
        "path": r"C:\AM_TribonBridge\diagnostics\pipe-flange-side-candidates.tsv",
        "category": "PIPE_FLANGE_SIDE",
        "handle_columns": [11]
    },
    {
        "path": r"C:\AM_TribonBridge\diagnostics\lifting-lug-candidates.tsv",
        "category": "LIFTING_LUG",
        "handle_columns": [12, 13, 14, 15, 16]
    },
    {
        "path": r"C:\AM_TribonBridge\diagnostics\lifting-beam-candidates.tsv",
        "category": "LIFTING_BEAM",
        "handle_columns": [12, 13, 14]
    },
    {
        "path": r"C:\AM_TribonBridge\diagnostics\structural-flange-candidates.tsv",
        "category": "STRUCTURAL_FLANGE",
        "handle_columns": [15]
    }
]

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

def get_extent(handle):
    extent = kcs_draft.element_extent_get(handle)

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

def union_extent(first, second):
    if first is None:
        return second

    return (
        min(first[0], second[0]),
        min(first[1], second[1]),
        max(first[2], second[2]),
        max(first[3], second[3])
    )

def extents_connected(first, second):
    horizontal_gap = 0.0
    vertical_gap = 0.0

    if first[2] < second[0]:
        horizontal_gap = (
            second[0] - first[2]
        )

    elif second[2] < first[0]:
        horizontal_gap = (
            first[0] - second[2]
        )

    if first[3] < second[1]:
        vertical_gap = (
            second[1] - first[3]
        )

    elif second[3] < first[1]:
        vertical_gap = (
            first[1] - second[3]
        )

    return (
        horizontal_gap <= TOLERANCE and
        vertical_gap <= TOLERANCE
    )

def capture_contours():
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
        return kcs_draft.contour_capture(
            region
        )

    except:
        if str(kcs_draft.error) == "kcs_NotFound":
            return [0]

        raise

def add_handle_names(
    target,
    handle_text
):
    values = handle_text.split(",")

    for value in values:
        handle_name = value.strip()

        if handle_name != "":
            target[handle_name] = 1

def read_candidate_objects():
    objects = []
    source_error_count = 0

    for source in CANDIDATE_SOURCES:
        source_path = source["path"]

        if not os.path.exists(source_path):
            source_error_count = (
                source_error_count + 1
            )

            continue

        file_object = open(
            source_path,
            "r"
        )

        lines = file_object.readlines()
        file_object.close()

        for line in lines:
            line = line.rstrip("\r\n")

            if line == "":
                continue

            fields = line.split("\t")

            try:
                candidate_index = int(
                    fields[0]
                )
            except:
                continue

            seed_names = {}

            for column_index in source[
                "handle_columns"
            ]:
                if column_index >= len(fields):
                    continue

                add_handle_names(
                    seed_names,
                    fields[column_index]
                )

            names = seed_names.keys()
            names.sort()

            object_key = (
                "%s-%02d" % (
                    source["category"],
                    candidate_index
                )
            )

            objects.append({
                "object_key": object_key,
                "category":
                    source["category"],
                "candidate_index":
                    candidate_index,
                "seed_names": names
            })

    objects.sort(
        lambda first, second:
        cmp(
            first["object_key"],
            second["object_key"]
        )
    )

    return (
        objects,
        source_error_count
    )

def expand_object(
    object_item,
    contour_items,
    contour_index_by_name
):
    seed_indices = []
    missing_seed_names = []

    for seed_name in object_item[
        "seed_names"
    ]:
        if contour_index_by_name.has_key(
            seed_name
        ):
            seed_indices.append(
                contour_index_by_name[
                    seed_name
                ]
            )
        else:
            missing_seed_names.append(
                seed_name
            )

    visited = {}
    queue = []

    for seed_index in seed_indices:
        if not visited.has_key(
            seed_index
        ):
            visited[seed_index] = 1
            queue.append(seed_index)

    while len(queue) > 0:
        current_index = queue[0]
        del queue[0]

        current_item = contour_items[
            current_index
        ]

        candidate_index = 0

        while candidate_index < len(
            contour_items
        ):
            if not visited.has_key(
                candidate_index
            ):
                candidate_item = (
                    contour_items[
                        candidate_index
                    ]
                )

                if extents_connected(
                    current_item["extent"],
                    candidate_item["extent"]
                ):
                    visited[
                        candidate_index
                    ] = 1

                    queue.append(
                        candidate_index
                    )

            candidate_index = (
                candidate_index + 1
            )

    expanded_names = []
    expanded_extent = None

    visited_indices = visited.keys()
    visited_indices.sort()

    for contour_index in visited_indices:
        contour_item = contour_items[
            contour_index
        ]

        expanded_names.append(
            contour_item["handle_name"]
        )

        expanded_extent = union_extent(
            expanded_extent,
            contour_item["extent"]
        )

    expanded_names.sort()
    missing_seed_names.sort()

    return {
        "expanded_names":
            expanded_names,
        "expanded_extent":
            expanded_extent,
        "missing_seed_names":
            missing_seed_names
    }

folder = os.path.dirname(OUTPUT)

if not os.path.exists(folder):
    os.makedirs(folder)

f = open(OUTPUT, "w")

try:
    (
        candidate_objects,
        source_error_count
    ) = read_candidate_objects()

    capture_result = capture_contours()
    contour_handles = capture_result[1:]

    contour_items = []
    contour_index_by_name = {}

    extent_failure_count = 0

    for handle in contour_handles:
        try:
            handle_name = clean(handle)

            item = {
                "handle": handle,
                "handle_name": handle_name,
                "extent": get_extent(handle)
            }

            contour_index_by_name[
                handle_name
            ] = len(contour_items)

            contour_items.append(item)

        except:
            extent_failure_count = (
                extent_failure_count + 1
            )

    expanded_objects = []
    owners_by_handle = {}
    all_seed_names = {}

    missing_seed_total = 0

    for candidate_object in candidate_objects:
        for seed_name in candidate_object[
            "seed_names"
        ]:
            all_seed_names[seed_name] = 1

        expansion = expand_object(
            candidate_object,
            contour_items,
            contour_index_by_name
        )

        missing_seed_total = (
            missing_seed_total +
            len(
                expansion[
                    "missing_seed_names"
                ]
            )
        )

        expanded_object = {
            "object_key":
                candidate_object[
                    "object_key"
                ],
            "category":
                candidate_object[
                    "category"
                ],
            "candidate_index":
                candidate_object[
                    "candidate_index"
                ],
            "seed_names":
                candidate_object[
                    "seed_names"
                ],
            "expanded_names":
                expansion[
                    "expanded_names"
                ],
            "expanded_extent":
                expansion[
                    "expanded_extent"
                ],
            "missing_seed_names":
                expansion[
                    "missing_seed_names"
                ]
        }

        expanded_objects.append(
            expanded_object
        )

        for handle_name in expansion[
            "expanded_names"
        ]:
            if not owners_by_handle.has_key(
                handle_name
            ):
                owners_by_handle[
                    handle_name
                ] = []

            owners_by_handle[
                handle_name
            ].append(
                expanded_object[
                    "object_key"
                ]
            )

    write_line(
        f,
        "FORMAT\tAM_GEOMETRY_OBJECT_EXPANSION_V1"
    )

    write_line(
        f,
        "CAPTURED_CONTOUR_COUNT\t%s" %
        str(len(contour_items))
    )

    write_line(
        f,
        "OBJECT_COUNT\t%s" %
        str(len(expanded_objects))
    )

    write_line(
        f,
        "UNIQUE_SEED_HANDLE_COUNT\t%s" %
        str(len(all_seed_names))
    )

    write_line(
        f,
        "OBJECT_KEY\tCATEGORY"
        "\tCANDIDATE_INDEX"
        "\tSEED_COUNT"
        "\tEXPANDED_COUNT"
        "\tX1\tY1\tX2\tY2"
        "\tMISSING_SEED_COUNT"
        "\tMISSING_SEED_HANDLES"
        "\tSEED_HANDLES"
        "\tEXPANDED_HANDLES"
    )

    for expanded_object in expanded_objects:
        extent = expanded_object[
            "expanded_extent"
        ]

        if extent is None:
            x1 = ""
            y1 = ""
            x2 = ""
            y2 = ""
        else:
            x1 = str(extent[0])
            y1 = str(extent[1])
            x2 = str(extent[2])
            y2 = str(extent[3])

        write_line(
            f,
            "%s\t%s\t%s"
            "\t%s\t%s"
            "\t%s\t%s\t%s\t%s"
            "\t%s\t%s"
            "\t%s\t%s" % (
                expanded_object[
                    "object_key"
                ],
                expanded_object[
                    "category"
                ],
                str(
                    expanded_object[
                        "candidate_index"
                    ]
                ),
                str(
                    len(
                        expanded_object[
                            "seed_names"
                        ]
                    )
                ),
                str(
                    len(
                        expanded_object[
                            "expanded_names"
                        ]
                    )
                ),
                x1,
                y1,
                x2,
                y2,
                str(
                    len(
                        expanded_object[
                            "missing_seed_names"
                        ]
                    )
                ),
                ",".join(
                    expanded_object[
                        "missing_seed_names"
                    ]
                ),
                ",".join(
                    expanded_object[
                        "seed_names"
                    ]
                ),
                ",".join(
                    expanded_object[
                        "expanded_names"
                    ]
                )
            )
        )

    conflict_handle_count = 0

    owner_names = owners_by_handle.keys()
    owner_names.sort()

    for handle_name in owner_names:
        owners = owners_by_handle[
            handle_name
        ]

        if len(owners) > 1:
            conflict_handle_count = (
                conflict_handle_count + 1
            )

            owners.sort()

            write_line(
                f,
                "CONFLICT\t%s\t%s" % (
                    handle_name,
                    ",".join(owners)
                )
            )

    assigned_unique_count = len(
        owners_by_handle
    )

    unassigned_contour_count = (
        len(contour_items) -
        assigned_unique_count
    )

    write_line(f, "")
    write_line(f, "SUMMARY")

    write_line(
        f,
        "ASSIGNED_UNIQUE_CONTOUR_COUNT\t%s" %
        str(assigned_unique_count)
    )

    write_line(
        f,
        "UNASSIGNED_CONTOUR_COUNT\t%s" %
        str(unassigned_contour_count)
    )

    write_line(
        f,
        "CONFLICT_HANDLE_COUNT\t%s" %
        str(conflict_handle_count)
    )

    write_line(
        f,
        "MISSING_SEED_HANDLE_COUNT\t%s" %
        str(missing_seed_total)
    )

    write_line(
        f,
        "SOURCE_ERROR_COUNT\t%s" %
        str(source_error_count)
    )

    write_line(
        f,
        "EXTENT_FAILURE_COUNT\t%s" %
        str(extent_failure_count)
    )

    if (
        source_error_count == 0 and
        extent_failure_count == 0 and
        missing_seed_total == 0 and
        conflict_handle_count == 0
    ):
        write_line(f, "STATUS\tSUCCESS")
    else:
        write_line(f, "STATUS\tPARTIAL")

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
