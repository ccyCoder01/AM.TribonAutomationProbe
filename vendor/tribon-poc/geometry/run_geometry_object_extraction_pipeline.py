# -*- coding: ascii -*-
import os
import imp
import time
import traceback

TOOLS_ROOT = r"C:\AM_TribonBridge\tools"
DIAGNOSTICS_ROOT = r"C:\AM_TribonBridge\diagnostics"

OUTPUT = os.path.join(
    DIAGNOSTICS_ROOT,
    "geometry-object-extraction-pipeline-result.txt"
)

STEPS = [
    {
        "name": "PIPE_FLANGE_FRONT",
        "script": "detect_pipe_flange_candidates.py",
        "result": "pipe-flange-candidates.tsv",
        "count_key": "CANDIDATE_COUNT",
        "expected_count": 3
    },
    {
        "name": "PIPE_FLANGE_SIDE",
        "script": "detect_pipe_flange_side_candidates.py",
        "result": "pipe-flange-side-candidates.tsv",
        "count_key": "CANDIDATE_COUNT",
        "expected_count": 1
    },
    {
        "name": "LIFTING_LUG",
        "script": "detect_lifting_lug_candidates.py",
        "result": "lifting-lug-candidates.tsv",
        "count_key": "CANDIDATE_COUNT",
        "expected_count": 3
    },
    {
        "name": "LIFTING_BEAM",
        "script": "detect_lifting_beam_candidates.py",
        "result": "lifting-beam-candidates.tsv",
        "count_key": "CANDIDATE_COUNT",
        "expected_count": 2
    },
    {
        "name": "STRUCTURAL_FLANGE",
        "script": "detect_structural_flange_candidates.py",
        "result": "structural-flange-candidates.tsv",
        "count_key": "CANDIDATE_COUNT",
        "expected_count": 3
    },
    {
        "name": "CONNECTIVITY_EXPANSION",
        "script": "expand_detected_objects_by_connectivity.py",
        "result": "geometry-object-expansion.tsv",
        "count_key": "OBJECT_COUNT",
        "expected_count": 12
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

def read_key_values(path):
    values = {}

    if not os.path.exists(path):
        return values

    file_object = open(path, "r")
    lines = file_object.readlines()
    file_object.close()

    for line in lines:
        line = line.rstrip("\r\n")

        if line == "":
            continue

        fields = line.split("\t")

        if len(fields) < 2:
            continue

        key = fields[0].strip()
        value = fields[1].strip()

        if key != "":
            values[key] = value

    return values

def status_is_success(value):
    normalized = value.strip().upper()

    return (
        normalized == "SUCCESS" or
        normalized == "SUCCES"
    )

def run_script(step_index, step):
    script_path = os.path.join(
        TOOLS_ROOT,
        step["script"]
    )

    result_path = os.path.join(
        DIAGNOSTICS_ROOT,
        step["result"]
    )

    if not os.path.exists(script_path):
        return {
            "success": 0,
            "error": "SCRIPT_NOT_FOUND",
            "script_path": script_path,
            "result_path": result_path,
            "reported_count": "",
            "reported_status": ""
        }

    module_name = (
        "am_geometry_pipeline_step_%02d" %
        step_index
    )

    try:
        imp.load_source(
            module_name,
            script_path
        )

    except Exception, e:
        return {
            "success": 0,
            "error": (
                "SCRIPT_EXCEPTION: %s" %
                clean(e)
            ),
            "script_path": script_path,
            "result_path": result_path,
            "reported_count": "",
            "reported_status": ""
        }

    if not os.path.exists(result_path):
        return {
            "success": 0,
            "error": "RESULT_NOT_FOUND",
            "script_path": script_path,
            "result_path": result_path,
            "reported_count": "",
            "reported_status": ""
        }

    values = read_key_values(
        result_path
    )

    reported_count = values.get(
        step["count_key"],
        ""
    )

    reported_status = values.get(
        "STATUS",
        ""
    )

    count_matches = 0

    try:
        count_matches = (
            int(reported_count) ==
            step["expected_count"]
        )
    except:
        count_matches = 0

    success = (
        count_matches and
        status_is_success(reported_status)
    )

    error = ""

    if not count_matches:
        error = (
            "COUNT_MISMATCH expected=%s actual=%s" %
            (
                str(step["expected_count"]),
                reported_count
            )
        )

    elif not status_is_success(
        reported_status
    ):
        error = (
            "STATUS_NOT_SUCCESS: %s" %
            reported_status
        )

    return {
        "success": success,
        "error": error,
        "script_path": script_path,
        "result_path": result_path,
        "reported_count": reported_count,
        "reported_status": reported_status
    }

if not os.path.exists(
    DIAGNOSTICS_ROOT
):
    os.makedirs(
        DIAGNOSTICS_ROOT
    )

f = open(OUTPUT, "w")

try:
    write_line(
        f,
        "FORMAT\tAM_GEOMETRY_OBJECT_EXTRACTION_PIPELINE_V1"
    )

    write_line(
        f,
        "START_TIME_EPOCH\t%s" %
        str(time.time())
    )

    successful_step_count = 0
    failed_step_count = 0

    step_index = 0

    for step in STEPS:
        step_index = step_index + 1

        result = run_script(
            step_index,
            step
        )

        if result["success"]:
            successful_step_count = (
                successful_step_count + 1
            )

            step_status = "SUCCESS"
        else:
            failed_step_count = (
                failed_step_count + 1
            )

            step_status = "FAILED"

        write_line(
            f,
            "STEP\t%s"
            "\tNAME\t%s"
            "\tSTATUS\t%s"
            "\tCOUNT_KEY\t%s"
            "\tEXPECTED_COUNT\t%s"
            "\tACTUAL_COUNT\t%s"
            "\tSOURCE_STATUS\t%s"
            "\tERROR\t%s" % (
                str(step_index),
                step["name"],
                step_status,
                step["count_key"],
                str(step["expected_count"]),
                result["reported_count"],
                result["reported_status"],
                result["error"]
            )
        )

    expansion_path = os.path.join(
        DIAGNOSTICS_ROOT,
        "geometry-object-expansion.tsv"
    )

    expansion_values = read_key_values(
        expansion_path
    )

    object_count = expansion_values.get(
        "OBJECT_COUNT",
        ""
    )

    assigned_count = expansion_values.get(
        "ASSIGNED_UNIQUE_CONTOUR_COUNT",
        ""
    )

    conflict_count = expansion_values.get(
        "CONFLICT_HANDLE_COUNT",
        ""
    )

    missing_seed_count = expansion_values.get(
        "MISSING_SEED_HANDLE_COUNT",
        ""
    )

    source_error_count = expansion_values.get(
        "SOURCE_ERROR_COUNT",
        ""
    )

    extent_failure_count = expansion_values.get(
        "EXTENT_FAILURE_COUNT",
        ""
    )

    final_validation_success = 0

    try:
        final_validation_success = (
            int(object_count) == 12 and
            int(assigned_count) == 113 and
            int(conflict_count) == 0 and
            int(missing_seed_count) == 0 and
            int(source_error_count) == 0 and
            int(extent_failure_count) == 0
        )
    except:
        final_validation_success = 0

    write_line(f, "")
    write_line(f, "SUMMARY")

    write_line(
        f,
        "STEP_COUNT\t%s" %
        str(len(STEPS))
    )

    write_line(
        f,
        "SUCCESSFUL_STEP_COUNT\t%s" %
        str(successful_step_count)
    )

    write_line(
        f,
        "FAILED_STEP_COUNT\t%s" %
        str(failed_step_count)
    )

    write_line(
        f,
        "OBJECT_COUNT\t%s" %
        object_count
    )

    write_line(
        f,
        "ASSIGNED_UNIQUE_CONTOUR_COUNT\t%s" %
        assigned_count
    )

    write_line(
        f,
        "CONFLICT_HANDLE_COUNT\t%s" %
        conflict_count
    )

    write_line(
        f,
        "MISSING_SEED_HANDLE_COUNT\t%s" %
        missing_seed_count
    )

    write_line(
        f,
        "SOURCE_ERROR_COUNT\t%s" %
        source_error_count
    )

    write_line(
        f,
        "EXTENT_FAILURE_COUNT\t%s" %
        extent_failure_count
    )

    if (
        failed_step_count == 0 and
        final_validation_success
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
        "TRACEBACK\t%s" %
        clean(traceback.format_exc())
    )

    write_line(f, "STATUS\tFAILED")

f.close()
