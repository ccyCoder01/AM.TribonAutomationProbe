# -*- coding: ascii -*-
import os
import re

import kcs_draft
import kcs_ui
import KcsCaptureRegion2D

SNAPSHOT = r"C:\AM_TribonBridge\diagnostics\geometry-object-snapshot.json"
OUTPUT = r"C:\AM_TribonBridge\diagnostics\demo-highlight-lifting-objects-result.tsv"

EXPECTED_OBJECT_COUNT = 5
EXPECTED_HANDLE_COUNT = 42


def clean(value):
    try:
        result = str(value)
    except:
        result = "<conversion_failed>"

    result = result.replace("\t", " ")
    result = result.replace("\r", " ")
    result = result.replace("\n", " ")
    return result


def write_lines(lines):
    folder = os.path.dirname(OUTPUT)

    if not os.path.isdir(folder):
        os.makedirs(folder)

    handle = open(OUTPUT, "wb")

    try:
        handle.write(("\n".join(lines) + "\n").encode("utf-8"))
        handle.flush()
    finally:
        handle.close()


def load_target_objects():
    handle = open(SNAPSHOT, "rb")

    try:
        source = handle.read()
    finally:
        handle.close()

    if source[:3] == "\xef\xbb\xbf":
        source = source[3:]

    result = []

    # Split on each object record. Only objectId/category/geometryHandles
    # are required for the demo, so no JSON eval/parser is needed.
    chunks = source.split('"objectId"')

    for chunk in chunks[1:]:
        object_match = re.search(
            r'^\s*:\s*"([^"]+)"',
            chunk
        )

        category_match = re.search(
            r'"category"\s*:\s*"([^"]+)"',
            chunk
        )

        handles_match = re.search(
            r'"geometryHandles"\s*:\s*\[(.*?)\]',
            chunk,
            re.S
        )

        if object_match is None:
            continue

        if category_match is None:
            continue

        if handles_match is None:
            continue

        object_id = object_match.group(1)
        category = category_match.group(1)

        if category not in ("LIFTING_BEAM", "LIFTING_LUG"):
            continue

        geometry_handles = re.findall(
            r'"([^"]+)"',
            handles_match.group(1)
        )

        result.append({
            "objectId": object_id,
            "category": category,
            "geometryHandles": geometry_handles
        })

    return result


def capture_current_contours():
    region = KcsCaptureRegion2D.CaptureRegion2D()
    region.SetBoundaryInfinite()

    try:
        value = kcs_draft.contour_capture(region)
    except:
        if str(kcs_draft.error) == "kcs_NotFound":
            return 0, []

        raise

    if value is None:
        return 0, []

    try:
        reported = int(value[0])
    except:
        reported = 0

    try:
        handles = list(value[1:])
    except:
        handles = []

    return reported, handles


def main():
    lines = [
        "FORMAT\tAM_DEMO_HIGHLIGHT_LIFTING_OBJECTS_V2",
        "SNAPSHOT\t" + SNAPSHOT
    ]

    if not os.path.isfile(SNAPSHOT):
        lines.append("SNAPSHOT_EXISTS\t0")
        lines.append("DRAWING_WRITE_PERFORMED\t0")
        lines.append("STATUS\tFAILED_SNAPSHOT_NOT_FOUND")
        write_lines(lines)
        return

    lines.append("SNAPSHOT_EXISTS\t1")

    objects = load_target_objects()

    target_handles = {}
    duplicate_target_handle_count = 0
    object_rows = []

    for item in objects:
        object_id = clean(item["objectId"])
        category = clean(item["category"])
        geometry_handles = item["geometryHandles"]

        object_rows.append(
            "OBJECT\t%s\tCATEGORY\t%s\tGEOMETRY_COUNT\t%s" % (
                object_id,
                category,
                clean(len(geometry_handles))
            )
        )

        for runtime_key in geometry_handles:
            runtime_key = clean(runtime_key)

            if target_handles.has_key(runtime_key):
                duplicate_target_handle_count += 1
            else:
                target_handles[runtime_key] = 1

    reported_count, runtime_handles = capture_current_contours()

    runtime_by_key = {}

    for runtime_handle in runtime_handles:
        runtime_by_key[clean(runtime_handle)] = runtime_handle

    try:
        kcs_draft.highlight_off(0)
    except:
        pass

    highlighted_count = 0
    missing_keys = []
    failure_rows = []

    for runtime_key in target_handles.keys():
        if not runtime_by_key.has_key(runtime_key):
            missing_keys.append(runtime_key)
            continue

        try:
            kcs_draft.element_highlight(
                runtime_by_key[runtime_key]
            )
            highlighted_count += 1
        except Exception, error:
            failure_rows.append(
                "HIGHLIGHT_FAILURE\t%s\t%s" % (
                    runtime_key,
                    clean(error)
                )
            )

    try:
        kcs_ui.app_window_refresh()
    except:
        pass

    lines.append("TARGET_CATEGORY\tLIFTING_BEAM")
    lines.append("TARGET_CATEGORY\tLIFTING_LUG")
    lines.append("TARGET_OBJECT_COUNT\t" + clean(len(objects)))
    lines.append(
        "TARGET_UNIQUE_HANDLE_COUNT\t" +
        clean(len(target_handles))
    )
    lines.append(
        "DUPLICATE_TARGET_HANDLE_COUNT\t" +
        clean(duplicate_target_handle_count)
    )
    lines.append(
        "CAPTURE_REPORTED_COUNT\t" +
        clean(reported_count)
    )
    lines.append(
        "CAPTURE_ACTUAL_COUNT\t" +
        clean(len(runtime_handles))
    )
    lines.append(
        "HIGHLIGHT_SUCCESS_COUNT\t" +
        clean(highlighted_count)
    )
    lines.append(
        "MISSING_HANDLE_COUNT\t" +
        clean(len(missing_keys))
    )
    lines.append(
        "HIGHLIGHT_FAILURE_COUNT\t" +
        clean(len(failure_rows))
    )

    for row in object_rows:
        lines.append(row)

    for runtime_key in missing_keys:
        lines.append("MISSING_HANDLE\t" + runtime_key)

    for row in failure_rows:
        lines.append(row)

    lines.append("DRAWING_WRITE_PERFORMED\t0")

    if (
        len(objects) == EXPECTED_OBJECT_COUNT and
        len(target_handles) == EXPECTED_HANDLE_COUNT and
        duplicate_target_handle_count == 0 and
        len(missing_keys) == 0 and
        len(failure_rows) == 0 and
        highlighted_count == EXPECTED_HANDLE_COUNT
    ):
        lines.append("STATUS\tSUCCESS")
    else:
        lines.append("STATUS\tFAILED_VALIDATION")

    write_lines(lines)


try:
    main()
except Exception, error:
    write_lines([
        "FORMAT\tAM_DEMO_HIGHLIGHT_LIFTING_OBJECTS_V2",
        "ERROR\t" + clean(error),
        "DRAWING_WRITE_PERFORMED\t0",
        "STATUS\tFAILED_EXCEPTION"
    ])
