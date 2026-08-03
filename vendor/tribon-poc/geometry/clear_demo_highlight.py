# -*- coding: ascii -*-
import os

import kcs_draft
import kcs_ui

OUTPUT = r"C:\AM_TribonBridge\diagnostics\demo-clear-highlight-result.txt"


def clean(value):
    try:
        result = str(value)
    except:
        result = "<conversion_failed>"

    return result.replace("\t", " ").replace("\r", " ").replace("\n", " ")


def write_result(lines):
    folder = os.path.dirname(OUTPUT)

    if not os.path.isdir(folder):
        os.makedirs(folder)

    handle = open(OUTPUT, "wb")

    try:
        handle.write(("\n".join(lines) + "\n").encode("utf-8"))
        handle.flush()
    finally:
        handle.close()


try:
    kcs_draft.highlight_off(0)

    try:
        kcs_ui.app_window_refresh()
    except:
        pass

    write_result([
        "FORMAT\tAM_DEMO_CLEAR_HIGHLIGHT_V1",
        "HIGHLIGHT_CLEARED\t1",
        "DRAWING_WRITE_PERFORMED\t0",
        "STATUS\tSUCCESS"
    ])

except Exception, error:
    write_result([
        "FORMAT\tAM_DEMO_CLEAR_HIGHLIGHT_V1",
        "ERROR\t" + clean(error),
        "DRAWING_WRITE_PERFORMED\t0",
        "STATUS\tFAILED_EXCEPTION"
    ])
