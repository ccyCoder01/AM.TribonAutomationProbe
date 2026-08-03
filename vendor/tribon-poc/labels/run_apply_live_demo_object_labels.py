# -*- coding: ascii -*-
import os
import time

TARGET = r"C:\AM_TribonBridge\tools\apply_live_demo_object_labels.py"
LOG = r"C:\AM_TribonBridge\diagnostics\live-demo-object-label-launcher-result.txt"


def clean(value):
    try:
        result = str(value)
    except:
        result = "<conversion_failed>"

    return result.replace("\t", " ").replace("\r", " ").replace("\n", " ")


def append_log(line):
    folder = os.path.dirname(LOG)

    if not os.path.isdir(folder):
        os.makedirs(folder)

    handle = open(LOG, "ab")

    try:
        handle.write((line + "\n").encode("utf-8"))
        handle.flush()
    finally:
        handle.close()


handle = open(LOG, "wb")

try:
    handle.write(
        (
            "FORMAT\tAM_LIVE_DEMO_OBJECT_LABEL_LAUNCHER_V1\n"
            "START_TIME_EPOCH\t%s\n"
            "TARGET\t%s\n"
            "TARGET_EXISTS\t%s\n"
            "LAUNCHER_ENTERED\t1\n"
        ) % (
            clean(time.time()),
            TARGET,
            clean(os.path.isfile(TARGET))
        )
    )
    handle.flush()
finally:
    handle.close()


try:
    if not os.path.isfile(TARGET):
        append_log("STATUS\tFAILED_TARGET_NOT_FOUND")
    else:
        append_log("EXECFILE_START\t1")

        namespace = {
            "__name__": "__main__",
            "__file__": TARGET
        }

        execfile(TARGET, namespace, namespace)

        append_log("EXECFILE_COMPLETED\t1")
        append_log("STATUS\tSUCCESS")

except SystemExit, error:
    append_log("SYSTEM_EXIT\t" + clean(error))
    append_log(
        "STATUS\tTARGET_COMPLETED_WITH_SYSTEM_EXIT"
    )

except Exception, error:
    append_log("ERROR\t" + clean(error))
    append_log("STATUS\tFAILED_EXCEPTION")