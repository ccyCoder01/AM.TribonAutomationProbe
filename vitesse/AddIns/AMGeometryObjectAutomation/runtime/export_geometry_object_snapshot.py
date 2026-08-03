# -*- coding: ascii -*-
import os
import time

SOURCE = r"C:\AM_TribonBridge\diagnostics\geometry-object-expansion.tsv"
PIPELINE_RESULT = r"C:\AM_TribonBridge\diagnostics\geometry-object-extraction-pipeline-result.txt"

OUTPUT = r"C:\AM_TribonBridge\diagnostics\geometry-object-snapshot.json"
LOG = r"C:\AM_TribonBridge\diagnostics\geometry-object-snapshot-result.txt"

def clean(value):
    try:
        text = str(value)
    except:
        text = "<conversion_failed>"

    text = text.replace("\t", " ")
    text = text.replace("\r", " ")
    text = text.replace("\n", " ")

    return text

def json_escape(value):
    text = clean(value)

    text = text.replace("\\", "\\\\")
    text = text.replace("\"", "\\\"")
    text = text.replace("\b", "\\b")
    text = text.replace("\f", "\\f")
    text = text.replace("\n", "\\n")
    text = text.replace("\r", "\\r")
    text = text.replace("\t", "\\t")

    return text

def json_string(value):
    return "\"" + json_escape(value) + "\""

def write_log(file_object, text):
    file_object.write(text + "\n")
    file_object.flush()

def parse_integer(value, default_value):
    try:
        return int(value)
    except:
        return default_value

def parse_float(value, default_value):
    try:
        return float(value)
    except:
        return default_value

def split_handles(value):
    result = []

    for item in value.split(","):
        handle_name = item.strip()

        if handle_name != "":
            result.append(handle_name)

    result.sort()
    return result

def read_summary(path):
    values = {}

    if not os.path.exists(path):
        return values

    file_object = open(path, "r")
    lines = file_object.readlines()
    file_object.close()

    in_summary = 0

    for line in lines:
        line = line.rstrip("\r\n")

        if line == "SUMMARY":
            in_summary = 1
            continue

        if not in_summary:
            continue

        fields = line.split("\t")

        if len(fields) >= 2:
            values[
                fields[0].strip()
            ] = fields[1].strip()

    return values

def read_objects(path):
    objects = []
    source_format = ""

    file_object = open(path, "r")
    lines = file_object.readlines()
    file_object.close()

    for line in lines:
        line = line.rstrip("\r\n")

        if line == "":
            continue

        fields = line.split("\t")

        if (
            len(fields) >= 2 and
            fields[0] == "FORMAT"
        ):
            source_format = fields[1]
            continue

        if len(fields) < 13:
            continue

        try:
            candidate_index = int(fields[2])
        except:
            continue

        x1 = parse_float(fields[5], 0.0)
        y1 = parse_float(fields[6], 0.0)
        x2 = parse_float(fields[7], 0.0)
        y2 = parse_float(fields[8], 0.0)

        seed_handles = split_handles(
            fields[11]
        )

        geometry_handles = split_handles(
            fields[12]
        )

        missing_handles = split_handles(
            fields[10]
        )

        objects.append({
            "object_id": fields[0].strip(),
            "category": fields[1].strip(),
            "candidate_index": candidate_index,
            "seed_count": parse_integer(
                fields[3],
                len(seed_handles)
            ),
            "geometry_count": parse_integer(
                fields[4],
                len(geometry_handles)
            ),
            "x1": x1,
            "y1": y1,
            "x2": x2,
            "y2": y2,
            "missing_seed_count":
                parse_integer(
                    fields[9],
                    len(missing_handles)
                ),
            "missing_seed_handles":
                missing_handles,
            "seed_handles": seed_handles,
            "geometry_handles":
                geometry_handles
        })

    objects.sort(
        lambda first, second:
        cmp(
            first["object_id"],
            second["object_id"]
        )
    )

    return source_format, objects

def write_string_array(
    file_object,
    values,
    indent
):
    file_object.write("[")

    index = 0

    for value in values:
        if index > 0:
            file_object.write(", ")

        file_object.write(
            json_string(value)
        )

        index = index + 1

    file_object.write("]")

folder = os.path.dirname(OUTPUT)

if not os.path.exists(folder):
    os.makedirs(folder)

log_file = open(LOG, "w")

try:
    if not os.path.exists(SOURCE):
        raise Exception(
            "SOURCE_NOT_FOUND: " + SOURCE
        )

    (
        source_format,
        objects
    ) = read_objects(SOURCE)

    expansion_summary = read_summary(
        SOURCE
    )

    pipeline_summary = read_summary(
        PIPELINE_RESULT
    )

    object_count = parse_integer(
        expansion_summary.get(
            "OBJECT_COUNT",
            str(len(objects))
        ),
        len(objects)
    )

    assigned_count = parse_integer(
        expansion_summary.get(
            "ASSIGNED_UNIQUE_CONTOUR_COUNT",
            "0"
        ),
        0
    )

    unassigned_count = parse_integer(
        expansion_summary.get(
            "UNASSIGNED_CONTOUR_COUNT",
            "0"
        ),
        0
    )

    conflict_count = parse_integer(
        expansion_summary.get(
            "CONFLICT_HANDLE_COUNT",
            "0"
        ),
        0
    )

    missing_seed_count = parse_integer(
        expansion_summary.get(
            "MISSING_SEED_HANDLE_COUNT",
            "0"
        ),
        0
    )

    source_error_count = parse_integer(
        expansion_summary.get(
            "SOURCE_ERROR_COUNT",
            "0"
        ),
        0
    )

    extent_failure_count = parse_integer(
        expansion_summary.get(
            "EXTENT_FAILURE_COUNT",
            "0"
        ),
        0
    )

    expansion_status = expansion_summary.get(
        "STATUS",
        ""
    )

    pipeline_status = pipeline_summary.get(
        "STATUS",
        ""
    )

    generated_at = time.time()

    output_file = open(OUTPUT, "w")

    output_file.write("{\n")
    output_file.write(
        "  \"schemaVersion\": \"1.0\",\n"
    )
    output_file.write(
        "  \"scope\": \"current_drawing_contours\",\n"
    )
    output_file.write(
        "  \"sourceFormat\": %s,\n" %
        json_string(source_format)
    )
    output_file.write(
        "  \"generatedAtEpoch\": %s,\n" %
        str(generated_at)
    )
    output_file.write(
        "  \"objectCount\": %s,\n" %
        str(object_count)
    )
    output_file.write(
        "  \"assignedUniqueContourCount\": %s,\n" %
        str(assigned_count)
    )
    output_file.write(
        "  \"unassignedContourCount\": %s,\n" %
        str(unassigned_count)
    )
    output_file.write(
        "  \"objects\": [\n"
    )

    object_index = 0

    for item in objects:
        width = item["x2"] - item["x1"]
        height = item["y2"] - item["y1"]

        center_x = (
            item["x1"] + item["x2"]
        ) / 2.0

        center_y = (
            item["y1"] + item["y2"]
        ) / 2.0

        output_file.write("    {\n")

        output_file.write(
            "      \"objectId\": %s,\n" %
            json_string(item["object_id"])
        )

        output_file.write(
            "      \"category\": %s,\n" %
            json_string(item["category"])
        )

        output_file.write(
            "      \"candidateIndex\": %s,\n" %
            str(item["candidate_index"])
        )

        output_file.write(
            "      \"seedCount\": %s,\n" %
            str(item["seed_count"])
        )

        output_file.write(
            "      \"geometryCount\": %s,\n" %
            str(item["geometry_count"])
        )

        output_file.write(
            "      \"extent\": {\n"
        )

        output_file.write(
            "        \"minX\": %s,\n" %
            str(item["x1"])
        )

        output_file.write(
            "        \"minY\": %s,\n" %
            str(item["y1"])
        )

        output_file.write(
            "        \"maxX\": %s,\n" %
            str(item["x2"])
        )

        output_file.write(
            "        \"maxY\": %s,\n" %
            str(item["y2"])
        )

        output_file.write(
            "        \"centerX\": %s,\n" %
            str(center_x)
        )

        output_file.write(
            "        \"centerY\": %s,\n" %
            str(center_y)
        )

        output_file.write(
            "        \"width\": %s,\n" %
            str(width)
        )

        output_file.write(
            "        \"height\": %s\n" %
            str(height)
        )

        output_file.write("      },\n")

        output_file.write(
            "      \"missingSeedCount\": %s,\n" %
            str(item["missing_seed_count"])
        )

        output_file.write(
            "      \"missingSeedHandles\": "
        )

        write_string_array(
            output_file,
            item["missing_seed_handles"],
            6
        )

        output_file.write(",\n")

        output_file.write(
            "      \"seedHandles\": "
        )

        write_string_array(
            output_file,
            item["seed_handles"],
            6
        )

        output_file.write(",\n")

        output_file.write(
            "      \"geometryHandles\": "
        )

        write_string_array(
            output_file,
            item["geometry_handles"],
            6
        )

        output_file.write("\n")
        output_file.write("    }")

        object_index = object_index + 1

        if object_index < len(objects):
            output_file.write(",")

        output_file.write("\n")

    output_file.write("  ],\n")
    output_file.write(
        "  \"diagnostics\": {\n"
    )

    output_file.write(
        "    \"expansionStatus\": %s,\n" %
        json_string(expansion_status)
    )

    output_file.write(
        "    \"pipelineStatus\": %s,\n" %
        json_string(pipeline_status)
    )

    output_file.write(
        "    \"conflictHandleCount\": %s,\n" %
        str(conflict_count)
    )

    output_file.write(
        "    \"missingSeedHandleCount\": %s,\n" %
        str(missing_seed_count)
    )

    output_file.write(
        "    \"sourceErrorCount\": %s,\n" %
        str(source_error_count)
    )

    output_file.write(
        "    \"extentFailureCount\": %s\n" %
        str(extent_failure_count)
    )

    output_file.write("  }\n")
    output_file.write("}\n")
    output_file.close()

    valid = (
        source_format ==
        "AM_GEOMETRY_OBJECT_EXPANSION_V1" and
        object_count == len(objects) and
        conflict_count == 0 and
        missing_seed_count == 0 and
        source_error_count == 0 and
        extent_failure_count == 0 and
        expansion_status == "SUCCESS" and
        pipeline_status == "SUCCESS"
    )

    write_log(
        log_file,
        "FORMAT\tAM_GEOMETRY_OBJECT_SNAPSHOT_EXPORT_V1"
    )

    write_log(
        log_file,
        "SOURCE\t%s" %
        SOURCE
    )

    write_log(
        log_file,
        "OUTPUT\t%s" %
        OUTPUT
    )

    write_log(
        log_file,
        "SOURCE_FORMAT\t%s" %
        source_format
    )

    write_log(
        log_file,
        "OBJECT_COUNT\t%s" %
        str(len(objects))
    )

    write_log(
        log_file,
        "ASSIGNED_UNIQUE_CONTOUR_COUNT\t%s" %
        str(assigned_count)
    )

    write_log(
        log_file,
        "UNASSIGNED_CONTOUR_COUNT\t%s" %
        str(unassigned_count)
    )

    write_log(
        log_file,
        "CONFLICT_HANDLE_COUNT\t%s" %
        str(conflict_count)
    )

    write_log(
        log_file,
        "MISSING_SEED_HANDLE_COUNT\t%s" %
        str(missing_seed_count)
    )

    write_log(
        log_file,
        "EXPANSION_STATUS\t%s" %
        expansion_status
    )

    write_log(
        log_file,
        "PIPELINE_STATUS\t%s" %
        pipeline_status
    )

    write_log(
        log_file,
        "JSON_BYTE_COUNT\t%s" %
        str(os.path.getsize(OUTPUT))
    )

    if valid:
        write_log(
            log_file,
            "STATUS\tSUCCESS"
        )
    else:
        write_log(
            log_file,
            "STATUS\tFAILED"
        )

except Exception, e:
    write_log(
        log_file,
        "ERROR\t%s" %
        clean(e)
    )

    write_log(
        log_file,
        "STATUS\tFAILED"
    )

log_file.close()
