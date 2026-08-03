# -*- coding: ascii -*-
import os

import kcs_draft
import kcs_ui
import kcs_util
import KcsCaptureRegion2D
import KcsColour
import KcsText
import KcsPoint2D

OUTPUT = r"C:\AM_TribonBridge\diagnostics\live-demo-object-label-write-result.tsv"
AUTHORIZATION = r"C:\AM_TribonBridge\diagnostics\live-demo-object-label-write.authorization"

AUTHORIZATION_TOKEN = "b58e9313288747128fc4f338741d7adf"
EXPECTED_OPERATION_COUNT = 12
EXPECTED_COLOUR = "Yellow"
POSITION_TOLERANCE = 0.05
HEIGHT_TOLERANCE = 0.01

OPERATIONS = [
    ("label:LB-01", "LB-01", "LB-01", 110, 250, 3.5),
    ("label:LB-02", "LB-02", "LB-02", 110, 216.5, 3.5),
    ("label:LL-01", "LL-01", "LL-01", 252, 242, 3.5),
    ("label:LL-02", "LL-02", "LL-02", 307, 242, 3.5),
    ("label:LL-03", "LL-03", "LL-03", 365, 240, 3.5),
    ("label:PF-01", "PF-01", "PF-01", 55, 119, 3.5),
    ("label:PF-02", "PF-02", "PF-02", 115, 115, 3.5),
    ("label:PF-03", "PF-03", "PF-03", 170, 111, 3.5),
    ("label:PF-SIDE-01", "PF-SIDE-01", "PF-SIDE-01", 56, 58, 3.5),
    ("label:SF-01", "SF-01", "SF-01", 268, 128, 3.5),
    ("label:SF-02", "SF-02", "SF-02", 342, 128, 3.5),
    ("label:SF-03", "SF-03", "SF-03", 381, 118, 3.5),
]

WRITE_COUNT = 0


def clean(value):
    try:
        result = str(value)
    except:
        result = "<conversion_failed>"

    result = result.replace("\t", " ")
    result = result.replace("\r", " ")
    result = result.replace("\n", " ")
    return result


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


def capture_text_handles():
    region = KcsCaptureRegion2D.CaptureRegion2D()
    region.SetBoundaryInfinite()

    try:
        value = kcs_draft.text_capture(region)
    except:
        if str(kcs_draft.error) == "kcs_NotFound":
            return 0, []

        raise

    if value is None:
        return 0, []

    try:
        reported_count = int(value[0])
    except:
        reported_count = 0

    try:
        handles = list(value[1:])
    except:
        handles = []

    return reported_count, handles


def point_x(point):
    try:
        return point.X
    except:
        return point.GetX()


def point_y(point):
    try:
        return point.Y
    except:
        return point.GetY()


def inspect_target_texts():
    target_texts = {}

    for operation in OPERATIONS:
        target_texts[operation[2]] = 1

    reported_count, handles = capture_text_handles()
    matches = {}

    for target_text in target_texts.keys():
        matches[target_text] = []

    property_error_count = 0
    inspection_error_count = 0

    for handle in handles:
        try:
            text = KcsText.Text()

            text = kcs_draft.text_properties_get(
                handle,
                text
            )

            value = clean(text.GetString())

            if not target_texts.has_key(value):
                continue

            position = text.GetPosition()

            record = {
                "handle": handle,
                "handle_key": clean(handle),
                "text": value,
                "x": float(point_x(position)),
                "y": float(point_y(position)),
                "height": float(text.GetHeight()),
                "colour": clean(text.GetColour().Name())
            }

            matches[value].append(record)

        except:
            inspection_error_count += 1

    return (
        reported_count,
        len(handles),
        matches,
        property_error_count,
        inspection_error_count
    )


def validate_authorization():
    if not os.path.isfile(AUTHORIZATION):
        return 0, "AUTHORIZATION_FILE_NOT_FOUND"

    handle = open(AUTHORIZATION, "rb")

    try:
        source = handle.read()
    finally:
        handle.close()

    required_token = "TOKEN\t" + AUTHORIZATION_TOKEN

    if source.find(required_token) < 0:
        return 0, "AUTHORIZATION_TOKEN_MISMATCH"

    if source.find("EXPECTED_OPERATION_COUNT\t12") < 0:
        return 0, "AUTHORIZATION_COUNT_MISMATCH"

    if source.find("STATUS\tAUTHORIZED") < 0:
        return 0, "AUTHORIZATION_STATUS_INVALID"

    return 1, "AUTHORIZED"


def consume_authorization():
    try:
        os.remove(AUTHORIZATION)
        return 1, ""
    except Exception, error:
        return 0, clean(error)


def create_label(value, x, y, height):
    global WRITE_COUNT

    text = KcsText.Text()
    text.SetString(value)

    point = KcsPoint2D.Point2D()
    point.X = x
    point.Y = y

    text.SetPosition(point)
    text.SetHeight(height)

    text.SetColour(
        KcsColour.Colour(EXPECTED_COLOUR)
    )

    kcs_draft.text_new(text)
    WRITE_COUNT += 1


def main():
    lines = [
        "FORMAT\tAM_LIVE_DEMO_OBJECT_LABEL_WRITE_V1",
        "EXPECTED_OPERATION_COUNT\t" +
        clean(EXPECTED_OPERATION_COUNT),
        "EXPECTED_COLOUR\t" + EXPECTED_COLOUR
    ]

    if len(OPERATIONS) != EXPECTED_OPERATION_COUNT:
        lines.append(
            "ACTUAL_OPERATION_COUNT\t" +
            clean(len(OPERATIONS))
        )
        lines.append("DRAWING_WRITE_PERFORMED\t0")
        lines.append("DRAWING_WRITE_COUNT\t0")
        lines.append("STATUS\tFAILED_OPERATION_COUNT")
        write_result(lines)
        return

    operation_ids = {}
    target_texts = {}

    for operation in OPERATIONS:
        operation_id = operation[0]
        target_text = operation[2]

        if operation_ids.has_key(operation_id):
            lines.append(
                "DUPLICATE_OPERATION_ID\t" +
                clean(operation_id)
            )
            lines.append("DRAWING_WRITE_PERFORMED\t0")
            lines.append("DRAWING_WRITE_COUNT\t0")
            lines.append(
                "STATUS\tFAILED_DUPLICATE_OPERATION"
            )
            write_result(lines)
            return

        if target_texts.has_key(target_text):
            lines.append(
                "DUPLICATE_TARGET_TEXT\t" +
                clean(target_text)
            )
            lines.append("DRAWING_WRITE_PERFORMED\t0")
            lines.append("DRAWING_WRITE_COUNT\t0")
            lines.append(
                "STATUS\tFAILED_DUPLICATE_TEXT"
            )
            write_result(lines)
            return

        operation_ids[operation_id] = 1
        target_texts[target_text] = 1

    authorized, authorization_status = (
        validate_authorization()
    )

    lines.append(
        "AUTHORIZATION_STATUS\t" +
        authorization_status
    )

    if not authorized:
        lines.append("DRAWING_WRITE_PERFORMED\t0")
        lines.append("DRAWING_WRITE_COUNT\t0")
        lines.append("STATUS\tFAILED_AUTHORIZATION")
        write_result(lines)
        return

    (
        pre_reported_count,
        pre_actual_count,
        pre_matches,
        pre_property_error_count,
        pre_inspection_error_count
    ) = inspect_target_texts()

    already_present = []
    missing_operations = []
    duplicate_texts = []

    for operation in OPERATIONS:
        target_text = operation[2]
        count = len(pre_matches[target_text])

        if count == 0:
            missing_operations.append(operation)
        elif count == 1:
            already_present.append(operation)
        else:
            duplicate_texts.append(target_text)

    lines.append(
        "PRE_CAPTURE_REPORTED_COUNT\t" +
        clean(pre_reported_count)
    )
    lines.append(
        "PRE_CAPTURE_ACTUAL_COUNT\t" +
        clean(pre_actual_count)
    )
    lines.append(
        "PRE_ALREADY_PRESENT_COUNT\t" +
        clean(len(already_present))
    )
    lines.append(
        "PRE_MISSING_COUNT\t" +
        clean(len(missing_operations))
    )
    lines.append(
        "PRE_DUPLICATE_TEXT_COUNT\t" +
        clean(len(duplicate_texts))
    )
    lines.append(
        "PRE_INSPECTION_ERROR_COUNT\t" +
        clean(pre_inspection_error_count)
    )

    for target_text in duplicate_texts:
        lines.append(
            "PRE_DUPLICATE_TEXT\t" +
            clean(target_text)
        )

    if (
        len(duplicate_texts) > 0 or
        pre_inspection_error_count > 0
    ):
        lines.append("DRAWING_WRITE_PERFORMED\t0")
        lines.append("DRAWING_WRITE_COUNT\t0")
        lines.append(
            "STATUS\tFAILED_RUNTIME_PRECHECK"
        )
        write_result(lines)
        return

    if len(missing_operations) == 0:
        consumed, consume_error = consume_authorization()

        lines.append(
            "AUTHORIZATION_CONSUMED\t" +
            clean(consumed)
        )

        if consume_error != "":
            lines.append(
                "AUTHORIZATION_CONSUME_ERROR\t" +
                consume_error
            )

        lines.append("DRAWING_WRITE_PERFORMED\t0")
        lines.append("DRAWING_WRITE_COUNT\t0")
        lines.append("STATUS\tALREADY_COMPLETE")
        write_result(lines)
        return

    answer = kcs_ui.answer_req(
        "AM live demo object labels",
        "Create %d planned labels in the current drawing?" %
        len(missing_operations)
    )

    if answer != kcs_util.yes():
        lines.append("USER_CONFIRMATION\tNO")
        lines.append("DRAWING_WRITE_PERFORMED\t0")
        lines.append("DRAWING_WRITE_COUNT\t0")
        lines.append("STATUS\tCANCELLED")
        write_result(lines)
        return

    lines.append("USER_CONFIRMATION\tYES")

    consumed, consume_error = consume_authorization()

    lines.append(
        "AUTHORIZATION_CONSUMED\t" +
        clean(consumed)
    )

    if not consumed:
        lines.append(
            "AUTHORIZATION_CONSUME_ERROR\t" +
            consume_error
        )
        lines.append("DRAWING_WRITE_PERFORMED\t0")
        lines.append("DRAWING_WRITE_COUNT\t0")
        lines.append(
            "STATUS\tFAILED_AUTHORIZATION_CONSUME"
        )
        write_result(lines)
        return

    created_count = 0
    failed_count = 0

    lines.append(
        "OPERATION_ID\tSTABLE_OBJECT_ID\tTEXT"
        "\tX\tY\tHEIGHT\tRESULT"
    )

    for operation in missing_operations:
        operation_id = operation[0]
        stable_object_id = operation[1]
        target_text = operation[2]
        x = operation[3]
        y = operation[4]
        height = operation[5]

        try:
            create_label(
                target_text,
                x,
                y,
                height
            )

            created_count += 1
            result = "CREATED"

        except Exception, error:
            failed_count += 1
            result = "FAILED:" + clean(error)

        lines.append(
            "%s\t%s\t%s\t%s\t%s\t%s\t%s" % (
                clean(operation_id),
                clean(stable_object_id),
                clean(target_text),
                clean(x),
                clean(y),
                clean(height),
                result
            )
        )

    try:
        kcs_ui.app_window_refresh()
    except:
        pass

    (
        post_reported_count,
        post_actual_count,
        post_matches,
        post_property_error_count,
        post_inspection_error_count
    ) = inspect_target_texts()

    post_valid_count = 0
    post_duplicate_count = 0
    post_missing_count = 0
    property_error_count = 0

    for operation in OPERATIONS:
        target_text = operation[2]
        expected_x = operation[3]
        expected_y = operation[4]
        expected_height = operation[5]

        records = post_matches[target_text]

        if len(records) == 0:
            post_missing_count += 1
            lines.append(
                "POST_MISSING\t" +
                clean(target_text)
            )

        elif len(records) > 1:
            post_duplicate_count += 1
            lines.append(
                "POST_DUPLICATE\t%s\tCOUNT\t%s" % (
                    clean(target_text),
                    clean(len(records))
                )
            )

        else:
            post_valid_count += 1
            record = records[0]

            errors = []

            if (
                abs(
                    record["height"] -
                    expected_height
                ) > HEIGHT_TOLERANCE
            ):
                errors.append("HEIGHT")

            if record["colour"] != EXPECTED_COLOUR:
                errors.append("COLOUR")

            if (
                abs(record["x"] - expected_x) >
                POSITION_TOLERANCE
            ):
                errors.append("X")

            if (
                abs(record["y"] - expected_y) >
                POSITION_TOLERANCE
            ):
                errors.append("Y")

            if len(errors) > 0:
                property_error_count += 1

                lines.append(
                    "POST_PROPERTY_ERROR\t%s"
                    "\tERRORS\t%s"
                    "\tACTUAL_X\t%s"
                    "\tACTUAL_Y\t%s"
                    "\tACTUAL_HEIGHT\t%s"
                    "\tACTUAL_COLOUR\t%s" % (
                        clean(target_text),
                        clean(",".join(errors)),
                        clean(record["x"]),
                        clean(record["y"]),
                        clean(record["height"]),
                        clean(record["colour"])
                    )
                )

    lines.append(
        "CREATED_COUNT\t" +
        clean(created_count)
    )
    lines.append(
        "FAILED_COUNT\t" +
        clean(failed_count)
    )
    lines.append(
        "POST_CAPTURE_REPORTED_COUNT\t" +
        clean(post_reported_count)
    )
    lines.append(
        "POST_CAPTURE_ACTUAL_COUNT\t" +
        clean(post_actual_count)
    )
    lines.append(
        "POST_VALID_LABEL_COUNT\t" +
        clean(post_valid_count)
    )
    lines.append(
        "POST_MISSING_COUNT\t" +
        clean(post_missing_count)
    )
    lines.append(
        "POST_DUPLICATE_COUNT\t" +
        clean(post_duplicate_count)
    )
    lines.append(
        "POST_PROPERTY_ERROR_COUNT\t" +
        clean(property_error_count)
    )
    lines.append(
        "POST_INSPECTION_ERROR_COUNT\t" +
        clean(post_inspection_error_count)
    )

    if WRITE_COUNT > 0:
        lines.append("DRAWING_WRITE_PERFORMED\t1")
    else:
        lines.append("DRAWING_WRITE_PERFORMED\t0")

    lines.append(
        "DRAWING_WRITE_COUNT\t" +
        clean(WRITE_COUNT)
    )

    if (
        created_count ==
        len(missing_operations) and
        failed_count == 0 and
        post_valid_count ==
        EXPECTED_OPERATION_COUNT and
        post_missing_count == 0 and
        post_duplicate_count == 0 and
        property_error_count == 0 and
        post_inspection_error_count == 0
    ):
        lines.append("STATUS\tSUCCESS")
    else:
        lines.append("STATUS\tFAILED_POSTCHECK")

    write_result(lines)


try:
    main()

except Exception, error:
    lines = [
        "FORMAT\tAM_LIVE_DEMO_OBJECT_LABEL_WRITE_V1",
        "ERROR\t" + clean(error),
        "KCS_ERROR\t" + clean(kcs_draft.error)
    ]

    if WRITE_COUNT > 0:
        lines.append("DRAWING_WRITE_PERFORMED\t1")
    else:
        lines.append("DRAWING_WRITE_PERFORMED\t0")

    lines.append(
        "DRAWING_WRITE_COUNT\t" +
        clean(WRITE_COUNT)
    )
    lines.append("STATUS\tFAILED_EXCEPTION")

    write_result(lines)