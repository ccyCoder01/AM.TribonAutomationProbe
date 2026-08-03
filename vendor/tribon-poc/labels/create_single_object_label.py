# -*- coding: ascii -*-
import os
import kcs_draft
import kcs_ui
import kcs_util
import KcsCaptureRegion2D
import KcsText
import KcsPoint2D
import KcsColour

OUTPUT = r"C:\AM_TribonBridge\diagnostics\single-label-write-result.txt"

TARGET_TEXT = "AUTO-LB-01"
TARGET_X = 145.0
TARGET_Y = 246.0
TARGET_HEIGHT = 3.0
TARGET_COLOUR = "Yellow"

POSITION_TOLERANCE = 0.01
HEIGHT_TOLERANCE = 0.01

def clean(value):
    try:
        result = str(value)
    except:
        result = "<conversion_failed>"

    result = result.replace("\t", " ")
    result = result.replace("\r", " ")
    result = result.replace("\n", " ")
    return result

def capture_text_handles():
    drawing_extent = kcs_draft.element_extent_get()

    region = KcsCaptureRegion2D.CaptureRegion2D()
    region.SetRectangle(drawing_extent)
    region.SetInside()
    region.SetNoCut()

    try:
        result = kcs_draft.text_capture(region)
        return result[1:]
    except:
        if str(kcs_draft.error) == "kcs_NotFound":
            return []

        raise

def find_exact_matches():
    matches = []

    for handle in capture_text_handles():
        try:
            text = KcsText.Text()

            text = kcs_draft.text_properties_get(
                handle,
                text
            )

            if text.GetString() != TARGET_TEXT:
                continue

            position = text.GetPosition()
            colour = text.GetColour()

            matches.append({
                "handle": clean(handle),
                "x": position.X,
                "y": position.Y,
                "height": text.GetHeight(),
                "colour": colour.GetName()
            })

        except:
            pass

    return matches

folder = os.path.dirname(OUTPUT)

if not os.path.exists(folder):
    os.makedirs(folder)

f = open(OUTPUT, "w")

try:
    before_matches = find_exact_matches()

    f.write(
        "TARGET_TEXT=%s\n" %
        TARGET_TEXT
    )

    f.write(
        "PRECHECK_MATCH_COUNT=%s\n" %
        str(len(before_matches))
    )

    if len(before_matches) > 1:
        f.write(
            "STATUS=FAILED_DUPLICATE_PRECHECK\n"
        )

    elif len(before_matches) == 1:
        item = before_matches[0]

        f.write(
            "EXISTING_HANDLE=%s\n" %
            item["handle"]
        )

        f.write("WRITE_PERFORMED=0\n")
        f.write("STATUS=ALREADY_PRESENT\n")

    else:
        answer = kcs_ui.answer_req(
            "AM single label test",
            "Create text AUTO-LB-01?"
        )

        if answer != kcs_util.yes():
            f.write("WRITE_PERFORMED=0\n")
            f.write("STATUS=CANCELLED\n")

        else:
            text = KcsText.Text()
            text.SetString(TARGET_TEXT)

            point = KcsPoint2D.Point2D()
            point.X = TARGET_X
            point.Y = TARGET_Y

            text.SetPosition(point)
            text.SetHeight(TARGET_HEIGHT)

            text.SetColour(
                KcsColour.Colour(TARGET_COLOUR)
            )

            kcs_draft.text_new(text)
            kcs_ui.app_window_refresh()

            after_matches = find_exact_matches()

            f.write("WRITE_PERFORMED=1\n")

            f.write(
                "POSTCHECK_MATCH_COUNT=%s\n" %
                str(len(after_matches))
            )

            if len(after_matches) != 1:
                f.write(
                    "STATUS=FAILED_MATCH_COUNT\n"
                )

            else:
                item = after_matches[0]

                position_ok = (
                    abs(item["x"] - TARGET_X)
                    <= POSITION_TOLERANCE and
                    abs(item["y"] - TARGET_Y)
                    <= POSITION_TOLERANCE
                )

                height_ok = (
                    abs(
                        item["height"] -
                        TARGET_HEIGHT
                    )
                    <= HEIGHT_TOLERANCE
                )

                colour_ok = (
                    item["colour"] ==
                    TARGET_COLOUR
                )

                f.write(
                    "CREATED_HANDLE=%s\n" %
                    item["handle"]
                )

                f.write(
                    "ACTUAL_X=%s\n" %
                    str(item["x"])
                )

                f.write(
                    "ACTUAL_Y=%s\n" %
                    str(item["y"])
                )

                f.write(
                    "ACTUAL_HEIGHT=%s\n" %
                    str(item["height"])
                )

                f.write(
                    "ACTUAL_COLOUR=%s\n" %
                    item["colour"]
                )

                f.write(
                    "POSITION_OK=%s\n" %
                    str(position_ok)
                )

                f.write(
                    "HEIGHT_OK=%s\n" %
                    str(height_ok)
                )

                f.write(
                    "COLOUR_OK=%s\n" %
                    str(colour_ok)
                )

                if (
                    position_ok and
                    height_ok and
                    colour_ok
                ):
                    f.write(
                        "STATUS=SUCCESS\n"
                    )
                else:
                    f.write(
                        "STATUS=FAILED_PROPERTY_CHECK\n"
                    )

except Exception, e:
    f.write(
        "ERROR=%s\n" %
        clean(e)
    )

    f.write(
        "KCS_ERROR=%s\n" %
        clean(kcs_draft.error)
    )

    f.write("STATUS=FAILED\n")

f.close()
