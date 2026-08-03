# -*- coding: ascii -*-
import os
import sys
import time

ROOT = r"C:\AM_TribonBridge"
DIAGNOSTICS = os.path.join(ROOT, "diagnostics")


def _bootstrap_status(status, detail):
    try:
        if not os.path.exists(DIAGNOSTICS):
            os.makedirs(DIAGNOSTICS)
        handle = open(os.path.join(DIAGNOSTICS, "geometry-object-worker-bootstrap.txt"), "wb")
        try:
            lines = []
            lines.append("STAGE=BOOTSTRAP STATUS=%s" % status)
            lines.append("PYTHON_VERSION=%s" % sys.version)
            lines.append("PYTHON_HEXVERSION=%s" % sys.hexversion)
            lines.append("NAME=%s" % globals().get("__name__", ""))
            lines.append("FILE=%s" % globals().get("__file__", ""))
            lines.append("CWD=%s" % os.getcwd())
            lines.append("TIME=%s" % time.time())
            if detail:
                lines.append("DETAIL=%s" % detail)
            handle.write("\n".join(lines) + "\n")
            handle.flush()
        finally:
            handle.close()
    except:
        pass


_bootstrap_status("MODULE_STARTED", "")
_bootstrap_status("BASIC_IMPORTS_OK", "")

import re
import traceback
import math
import imp

try:
    import hashlib
except:
    hashlib = None

try:
    import kcs_draft
    import kcs_ui
    import KcsCaptureRegion2D
    import KcsText
    import KcsPoint2D
    import KcsColour
except Exception, error:
    _bootstrap_status("FAILED", error)
    raise

_bootstrap_status("KCS_IMPORTS_OK", "")

INBOX = os.path.join(ROOT, "inbox")
PROCESSING = os.path.join(ROOT, "processing")
OUTPUT = os.path.join(ROOT, "output")
ARCHIVE = os.path.join(ROOT, "archive")
PLAN_PATH = os.path.join(DIAGNOSTICS, "geometry-object-label-plan.json")
EXPANSION_PATH = os.path.join(DIAGNOSTICS, "geometry-object-expansion.tsv")

CONTRACT_VERSION = "geometry-label-plan/v1"
SELF_TEST_EXPECTED_HASH = (
    "F2B14D4200E1AC239FBF1CFD28D2F994"
    "39E631EC2D6FA129ECB6A92A841B75F2"
)
SHA256_EMPTY_EXPECTED = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"
SHA256_ABC_EXPECTED = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"
SHA256_PLAN_EXPECTED = "F2B14D4200E1AC239FBF1CFD28D2F99439E631EC2D6FA129ECB6A92A841B75F2"

try:
    TEXT_TYPE = unicode
except NameError:
    TEXT_TYPE = str


def _text(value):
    try:
        if isinstance(value, TEXT_TYPE):
            return value
        return TEXT_TYPE(value)
    except:
        return "<unprintable>"


class _InlinePlanBindingError(Exception):
    def __init__(self, code, category, message):
        Exception.__init__(self, message)
        self.code = code
        self.category = category
        self.message = message


def _utf8(value):
    if value is None:
        value = ""
    if isinstance(value, TEXT_TYPE):
        return value.encode("utf-8")
    return str(value)


def _sort_text(value):
    data = _utf8(value)
    try:
        return data.decode("utf-8")
    except:
        return TEXT_TYPE(data)


def _append_field(parts, value):
    data = _utf8(value)
    parts.append(str(len(data)) + ":" + data + "\n")


_SHA256_K = [
    0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5,
    0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
    0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3,
    0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
    0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC,
    0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
    0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7,
    0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
    0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13,
    0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
    0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3,
    0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
    0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5,
    0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
    0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208,
    0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2
]
_SHA256_H0 = [
    0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
    0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
]


def _sha256_rotr(value, count):
    return ((value >> count) | ((value << (32 - count)) & 0xffffffff)) & 0xffffffff


def _sha256_fallback(data):
    bit_length = len(data) * 8
    padded = data + chr(128)
    while ((len(padded) + 8) % 64) != 0:
        padded += chr(0)
    count = 56
    while count >= 0:
        padded += chr((bit_length >> count) & 255)
        count -= 8
    state = list(_SHA256_H0)
    offset = 0
    while offset < len(padded):
        chunk = padded[offset:offset + 64]
        words = []
        index = 0
        while index < 16:
            base = index * 4
            words.append((ord(chunk[base]) << 24) | (ord(chunk[base + 1]) << 16) | (ord(chunk[base + 2]) << 8) | ord(chunk[base + 3]))
            index += 1
        while len(words) < 64:
            value = words[-15]
            s0 = _sha256_rotr(value, 7) ^ _sha256_rotr(value, 18) ^ (value >> 3)
            value = words[-2]
            s1 = _sha256_rotr(value, 17) ^ _sha256_rotr(value, 19) ^ (value >> 10)
            words.append((words[-16] + s0 + words[-7] + s1) & 0xffffffff)
        a, b, c, d, e, f, g, h = state
        index = 0
        while index < 64:
            s1 = _sha256_rotr(e, 6) ^ _sha256_rotr(e, 11) ^ _sha256_rotr(e, 25)
            choose = (e & f) ^ ((~e) & g)
            temp1 = (h + s1 + choose + _SHA256_K[index] + words[index]) & 0xffffffff
            s0 = _sha256_rotr(a, 2) ^ _sha256_rotr(a, 13) ^ _sha256_rotr(a, 22)
            majority = (a & b) ^ (a & c) ^ (b & c)
            temp2 = (s0 + majority) & 0xffffffff
            h, g, f, e, d, c, b, a = g, f, e, (d + temp1) & 0xffffffff, c, b, a, (temp1 + temp2) & 0xffffffff
            index += 1
        values = (a, b, c, d, e, f, g, h)
        index = 0
        while index < 8:
            state[index] = (state[index] + values[index]) & 0xffffffff
            index += 1
        offset += 64
    result = ""
    for value in state:
        result += "%08x" % value
    return result.upper()


def _sha256_hex(data):
    if hashlib is not None:
        return hashlib.sha256(data).hexdigest().upper()
    return _sha256_fallback(data)


def _inline_compute_plan_hash(preflight):
    parts = []
    _append_field(parts, CONTRACT_VERSION)
    _append_field(parts, preflight.get("status", ""))
    _append_field(
        parts,
        preflight.get("preAlreadyPresentCount", 0)
    )
    _append_field(
        parts,
        preflight.get("preMissingCount", 0)
    )
    _append_field(
        parts,
        preflight.get("preDuplicateTextCount", 0)
    )
    _append_field(
        parts,
        preflight.get("preTextConflictCount", 0)
    )
    _append_field(
        parts,
        preflight.get("preInspectionErrorCount", 0)
    )

    items = list(preflight.get("items", []))
    items.sort(
        key=lambda item: (
            _sort_text(item.get("operationId", "")),
            _sort_text(item.get("stableObjectId", ""))
        )
    )

    for item in items:
        _append_field(
            parts,
            item.get("operationId", "")
        )
        _append_field(
            parts,
            item.get("sourceObjectId", "")
        )
        _append_field(
            parts,
            item.get("stableObjectId", "")
        )
        _append_field(
            parts,
            item.get("expectedText", "")
        )
        _append_field(
            parts,
            item.get("matchCount", 0)
        )
        _append_field(
            parts,
            item.get("decision", "")
        )
        _append_field(
            parts,
            item.get("matchHandle", "") or ""
        )

    return _sha256_hex("".join(parts))


def _inline_ready_operation_ids(preflight):
    result = []

    for item in preflight.get("items", []):
        if item.get("decision") == "READY_TO_CREATE":
            result.append(
                item.get("operationId", "")
            )

    result.sort(key=_sort_text)
    return result


def _inline_attach_plan_binding(preflight):
    preflight["planHash"] = _inline_compute_plan_hash(
        preflight
    )
    preflight["readyOperationIds"] = (
        _inline_ready_operation_ids(preflight)
    )
    return preflight


def _invalid(message):
    raise _InlinePlanBindingError(
        "geometry_label_plan_binding_invalid",
        "validation",
        message
    )


def _changed(message):
    raise _InlinePlanBindingError(
        "geometry_label_plan_changed",
        "safety",
        message
    )


def _inline_json_string(text, start):
    value = ""
    index = start + 1
    while index < len(text):
        char = text[index]
        if char == '"':
            return value, index + 1
        if char == "\\":
            index += 1
            if index >= len(text):
                _invalid("JSON string is incomplete.")
            escaped = text[index]
            mapping = {"\"": '"', "\\": "\\", "/": "/", "b": "\b", "f": "\f", "n": "\n", "r": "\r", "t": "\t"}
            if mapping.has_key(escaped):
                value += mapping[escaped]
            elif escaped == "u":
                if index + 4 >= len(text):
                    _invalid("JSON unicode escape is incomplete.")
                value += "?"
                index += 4
            else:
                _invalid("JSON string escape is invalid.")
        else:
            value += char
        index += 1
    _invalid("JSON string is incomplete.")


def _inline_field_start(text, name):
    match = re.search('"' + re.escape(name) + '"\\s*:', text)
    if match is None:
        return -1
    return match.end()


def _inline_skip_space(text, index):
    while index < len(text) and text[index] in " \t\r\n":
        index += 1
    return index


def _string_array_field(text, name):
    index = _inline_skip_space(text, _inline_field_start(text, name))
    if index < 0 or index >= len(text) or text[index] != "[":
        _invalid("JSON string array field is missing: " + name)
    index += 1
    result = []
    while True:
        index = _inline_skip_space(text, index)
        if index >= len(text):
            _invalid("JSON array is incomplete: " + name)
        if text[index] == "]":
            return result
        if text[index] != '"':
            _invalid("JSON array contains a non-string value: " + name)
        value, index = _inline_json_string(text, index)
        result.append(value)
        index = _inline_skip_space(text, index)
        if index < len(text) and text[index] == ",":
            index += 1
            continue
        if index < len(text) and text[index] == "]":
            return result
        _invalid("JSON array separator is invalid: " + name)


def _inline_bool_field(text, name):
    index = _inline_skip_space(text, _inline_field_start(text, name))
    if index < 0 or text[index:index + 4] != "true":
        return False
    return True


def _inline_string_field(text, name):
    index = _inline_skip_space(text, _inline_field_start(text, name))
    if index < 0 or index >= len(text) or text[index] != '"':
        return ""
    value, unused = _inline_json_string(text, index)
    return value


def _inline_parse_request_binding(request_text):
    if _inline_field_start(request_text, "payload") < 0:
        _invalid("Request payload is required.")
    return {
        "allowWrite": _inline_bool_field(request_text, "allowWrite"),
        "writeConfirmed": _inline_bool_field(request_text, "writeConfirmed"),
        "confirmedPreflightOperationId": _inline_string_field(request_text, "confirmedPreflightOperationId"),
        "confirmedPlanHash": _inline_string_field(request_text, "confirmedPlanHash"),
        "confirmedOperationIds": _string_array_field(request_text, "confirmedOperationIds")
    }


def _inline_validate_authorization(binding):
    if binding.get("allowWrite") is not True:
        _invalid("allowWrite must be true.")

    if binding.get("writeConfirmed") is not True:
        _invalid("writeConfirmed must be true.")

    preflight_operation_id = binding.get(
        "confirmedPreflightOperationId",
        ""
    )

    if (
        not isinstance(
            preflight_operation_id,
            TEXT_TYPE
        ) or
        preflight_operation_id.strip() == ""
    ):
        _invalid(
            "confirmedPreflightOperationId is required."
        )

    plan_hash = binding.get(
        "confirmedPlanHash",
        ""
    )

    if (
        not isinstance(plan_hash, TEXT_TYPE) or
        re.match(
            r"^[0-9A-Fa-f]{64}$",
            plan_hash
        ) is None
    ):
        _invalid(
            "confirmedPlanHash must be a "
            "64-character SHA-256 value."
        )

    operation_ids = binding.get(
        "confirmedOperationIds",
        []
    )

    if (
        not isinstance(
            operation_ids,
            (list, tuple)
        ) or
        len(operation_ids) == 0
    ):
        _invalid(
            "confirmedOperationIds must contain "
            "at least one operation."
        )

    seen = {}

    for operation_id in operation_ids:
        if (
            not isinstance(operation_id, TEXT_TYPE) or
            operation_id.strip() == ""
        ):
            _invalid(
                "confirmedOperationIds cannot "
                "contain blank values."
            )

        if operation_id in seen:
            _invalid(
                "confirmedOperationIds cannot "
                "contain duplicates."
            )

        seen[operation_id] = 1


def _inline_validate_against_preflight(
    binding,
    current_preflight
):
    _inline_validate_authorization(binding)
    attached = _inline_attach_plan_binding(
        current_preflight
    )

    if attached.get("status") == "BLOCKED":
        _changed(
            "The current label preflight is blocked."
        )

    if (
        binding["confirmedPlanHash"].upper() !=
        attached["planHash"].upper()
    ):
        _changed(
            "The current label plan hash differs "
            "from the confirmed plan."
        )

    confirmed = list(
        binding["confirmedOperationIds"]
    )
    confirmed.sort(key=_sort_text)

    current = list(
        attached["readyOperationIds"]
    )
    current.sort(key=_sort_text)

    if confirmed != current:
        _changed(
            "The current missing-label operation "
            "set differs from the confirmed set."
        )

    return attached


def _self_test_preflight():
    return {
        "status": "SUCCESS",
        "preAlreadyPresentCount": 0,
        "preMissingCount": 2,
        "preDuplicateTextCount": 0,
        "preTextConflictCount": 0,
        "preInspectionErrorCount": 0,
        "items": [
            {
                "operationId": "label:PF-02",
                "sourceObjectId": "OBJ-2",
                "stableObjectId": "PF-02",
                "expectedText": "PF-02",
                "matchCount": 0,
                "decision": "READY_TO_CREATE",
                "matchHandle": None
            },
            {
                "operationId": "label:LB-01",
                "sourceObjectId": "OBJ-1",
                "stableObjectId": "LB-01",
                "expectedText": "LB-01",
                "matchCount": 0,
                "decision": "READY_TO_CREATE",
                "matchHandle": None
            }
        ]
    }


def _expect_binding_error(callback):
    raised = False

    try:
        callback()
    except _InlinePlanBindingError:
        raised = True

    if not raised:
        raise Exception(
            "expected plan-binding error was not raised"
        )


def _inline_self_test():
    preflight = _inline_attach_plan_binding(
        _self_test_preflight()
    )

    if (
        preflight["planHash"] !=
        SELF_TEST_EXPECTED_HASH
    ):
        raise Exception(
            "plan hash self-test failed: " +
            preflight["planHash"]
        )

    if preflight["readyOperationIds"] != [
        "label:LB-01",
        "label:PF-02"
    ]:
        raise Exception(
            "ready operation ordering self-test failed"
        )

    valid = {
        "allowWrite": True,
        "writeConfirmed": True,
        "confirmedPreflightOperationId":
            "PREFLIGHT-1",
        "confirmedPlanHash":
            preflight["planHash"].lower(),
        "confirmedOperationIds":
            list(preflight["readyOperationIds"])
    }

    _inline_validate_against_preflight(
        valid,
        _self_test_preflight()
    )

    invalid_hash = dict(valid)
    invalid_hash["confirmedPlanHash"] = (
        "A" * 64
    )

    _expect_binding_error(
        lambda: _inline_validate_against_preflight(
            invalid_hash,
            _self_test_preflight()
        )
    )

    duplicate = dict(valid)
    duplicate["confirmedOperationIds"] = [
        "label:LB-01",
        "label:LB-01"
    ]

    _expect_binding_error(
        lambda: _inline_validate_authorization(
            duplicate
        )
    )



# BEGIN INLINE GEOMETRY LABEL PLAN BINDING
class _InlinePlanBinding(object):
    def attach_plan_binding(self, preflight):
        return _inline_attach_plan_binding(preflight)
    def parse_request_binding(self, request_text):
        return _inline_parse_request_binding(request_text)
    def validate_authorization(self, binding):
        return _inline_validate_authorization(binding)
    def validate_against_preflight(self, binding, current_preflight):
        return _inline_validate_against_preflight(binding, current_preflight)
# END INLINE GEOMETRY LABEL PLAN BINDING

PLAN_BINDING = _InlinePlanBinding()
_bootstrap_status("PLAN_BINDING_DEFINED", "")

def _resolve_addin_root():
    candidates = []
    try:
        candidates.append(os.path.dirname(os.path.abspath(__file__)))
    except:
        pass
    cwd = os.getcwd()
    candidates.append(cwd)
    candidates.append(os.path.join(cwd, "AddIns", "AMGeometryObjectAutomation"))
    current = cwd
    depth = 0
    while depth < 6:
        candidates.append(os.path.join(current, "AddIns", "AMGeometryObjectAutomation"))
        parent = os.path.dirname(current)
        if parent == current:
            break
        current = parent
        depth = depth + 1
    checked = []
    for candidate in candidates:
        candidate = os.path.abspath(candidate)
        if candidate in checked:
            continue
        checked.append(candidate)
        runtime = os.path.join(candidate, "runtime")
        if (os.path.isfile(os.path.join(runtime, "detect_pipe_flange_candidates.py")) and
                os.path.isfile(os.path.join(runtime, "expand_detected_objects_by_connectivity.py"))):
            return candidate
    raise Exception("addin root not found cwd=%s file=%s checked=%s" % (_text(cwd), _text(globals().get("__file__", "")), _text(checked)))

try:
    ADDIN_ROOT = _resolve_addin_root()
except Exception, error:
    _bootstrap_status("FAILED", error)
    raise
RUNTIME_ROOT = os.path.join(ADDIN_ROOT, "runtime")
_bootstrap_status("ADDIN_ROOT_RESOLVED", ADDIN_ROOT)

DETECTOR_SCRIPTS = [
    "detect_pipe_flange_candidates.py",
    "detect_pipe_flange_side_candidates.py",
    "detect_lifting_lug_candidates.py",
    "detect_lifting_beam_candidates.py",
    "detect_structural_flange_candidates.py",
    "expand_detected_objects_by_connectivity.py"
]

LIFTING_CATEGORIES = {
    "LIFTING_BEAM": 1,
    "LIFTING_LUG": 1
}

FLANGE_CATEGORIES = {
    "PIPE_FLANGE_FRONT": 1,
    "PIPE_FLANGE_SIDE": 1,
    "STRUCTURAL_FLANGE": 1
}

POSITION_TOLERANCE = 0.01
HEIGHT_TOLERANCE = 0.01


def _now():
    return time.strftime(
        "%Y-%m-%dT%H:%M:%SZ",
        time.gmtime()
    )


def _json(value):
    if value is None:
        return "null"

    if value is True:
        return "true"

    if value is False:
        return "false"

    if isinstance(value, (int, long, float)):
        return str(value)

    if isinstance(value, list) or isinstance(value, tuple):
        parts = []

        for item in value:
            parts.append(_json(item))

        return "[" + ",".join(parts) + "]"

    if isinstance(value, dict):
        parts = []

        for key in value.keys():
            parts.append(
                _json(str(key)) + ":" +
                _json(value[key])
            )

        return "{" + ",".join(parts) + "}"

    return (
        '"' +
        _text(value)
        .replace("\\", "\\\\")
        .replace('"', '\\"')
        .replace("\r", "\\r")
        .replace("\n", "\\n") +
        '"'
    )


def _field(text, name, default):
    match = re.search(
        '"' + name +
        '"\\s*:\\s*"([^"\\\\]*(?:\\\\.[^"\\\\]*)*)"',
        text
    )

    if match is None:
        return default

    return match.group(1)


def _bool_field(text, name, default):
    if text.find('"' + name + '"') < 0:
        return default

    return (
        re.search(
            '"' + name + '"\\s*:\\s*true',
            text
        ) is not None
    )


def _atomic(path, value):
    folder = os.path.dirname(path)

    if not os.path.exists(folder):
        os.makedirs(folder)

    temp = path + ".tmp"
    handle = open(temp, "wb")

    try:
        handle.write(value)
        handle.flush()
    finally:
        handle.close()

    if os.path.exists(path):
        os.remove(path)

    os.rename(temp, path)


def _region():
    region = KcsCaptureRegion2D.CaptureRegion2D()
    region.SetBoundaryInfinite()
    return region


def _normalize_capture(value):
    if value is None:
        return 0, []

    reported = 0
    handles = []

    try:
        reported = int(value[0])
        handles = list(value[1:])
    except:
        try:
            handles = list(value)
            reported = len(handles)
        except:
            handles = []
            reported = 0

    return reported, handles


def _capture_contours():
    try:
        return _normalize_capture(
            kcs_draft.contour_capture(_region())
        )
    except:
        if _text(kcs_draft.error) == "kcs_NotFound":
            return 0, []

        raise


def _capture_labels():
    try:
        return _normalize_capture(
            kcs_draft.text_capture(_region())
        )
    except:
        if _text(kcs_draft.error) == "kcs_NotFound":
            return 0, []

        raise


def _extent(handle):
    value = kcs_draft.element_extent_get(handle)

    x1 = min(value.Corner1.X, value.Corner2.X)
    y1 = min(value.Corner1.Y, value.Corner2.Y)
    x2 = max(value.Corner1.X, value.Corner2.X)
    y2 = max(value.Corner1.Y, value.Corner2.Y)

    return {
        "x1": x1,
        "y1": y1,
        "x2": x2,
        "y2": y2
    }


def _center(extent):
    return (
        (extent["x1"] + extent["x2"]) / 2.0,
        (extent["y1"] + extent["y2"]) / 2.0
    )


def _point_to_extent_distance(point, extent):
    dx = 0.0
    dy = 0.0

    if point[0] < extent["x1"]:
        dx = extent["x1"] - point[0]
    elif point[0] > extent["x2"]:
        dx = point[0] - extent["x2"]

    if point[1] < extent["y1"]:
        dy = extent["y1"] - point[1]
    elif point[1] > extent["y2"]:
        dy = point[1] - extent["y2"]

    return math.sqrt((dx * dx) + (dy * dy))


def _label_details(handle):
    value = KcsText.Text()
    value = kcs_draft.text_properties_get(
        handle,
        value
    )

    position = value.GetPosition()
    colour = value.GetColour()
    label_extent = _extent(handle)

    return {
        "handle": handle,
        "runtimeHandle": _text(handle),
        "text": _text(value.GetString()),
        "x": float(position.X),
        "y": float(position.Y),
        "height": float(value.GetHeight()),
        "colour": _text(colour.GetName()),
        "extent": label_extent,
        "center": _center(label_extent)
    }


def _capture_label_index():
    reported, handles = _capture_labels()
    by_text = {}
    errors = []

    for handle in handles:
        try:
            details = _label_details(handle)
            text_value = details["text"]

            if not by_text.has_key(text_value):
                by_text[text_value] = []

            by_text[text_value].append(details)
        except Exception, error:
            errors.append(_text(error))

    return {
        "reportedCount": reported,
        "actualCount": len(handles),
        "byText": by_text,
        "errors": errors
    }


def _load_runtime_script(script_name, sequence):
    path = os.path.join(RUNTIME_ROOT, script_name)

    if not os.path.isfile(path):
        raise Exception(
            "runtime script not found: " + path
        )

    module_name = (
        "am_geometry_runtime_%s_%s_%s" % (
            str(sequence),
            str(int(time.time() * 1000.0)),
            script_name.replace(".", "_")
        )
    )

    imp.load_source(module_name, path)


def _split_handles(value):
    result = []

    for item in value.split(","):
        cleaned = item.strip()

        if cleaned != "":
            result.append(cleaned)

    return result


def _parse_int(value, default_value):
    try:
        return int(value)
    except:
        return default_value


def _parse_float(value, default_value):
    try:
        return float(value)
    except:
        return default_value


def _parse_expansion():
    if not os.path.isfile(EXPANSION_PATH):
        raise Exception(
            "expansion result not found: " +
            EXPANSION_PATH
        )

    handle = open(EXPANSION_PATH, "rb")

    try:
        lines = handle.readlines()
    finally:
        handle.close()

    objects = []
    values = {}
    parse_failures = 0
    in_summary = 0

    for raw_line in lines:
        line = raw_line.rstrip("\r\n")

        if line == "":
            continue

        if line == "SUMMARY":
            in_summary = 1
            continue

        fields = line.split("\t")

        if in_summary:
            if len(fields) >= 2:
                values[fields[0].strip()] = fields[1].strip()

            continue

        if len(fields) == 2:
            values[fields[0].strip()] = fields[1].strip()
            continue

        if len(fields) < 13:
            continue

        if fields[0] == "OBJECT_KEY":
            continue

        try:
            x1 = float(fields[5])
            y1 = float(fields[6])
            x2 = float(fields[7])
            y2 = float(fields[8])

            seed_handles = _split_handles(fields[11])
            geometry_handles = _split_handles(fields[12])

            objects.append({
                "runtimeObjectId": fields[0],
                "category": fields[1],
                "confidence": "verified-rule-poc",
                "extent": {
                    "x1": x1,
                    "y1": y1,
                    "x2": x2,
                    "y2": y2
                },
                "seedHandles": seed_handles,
                "geometryHandles": geometry_handles,
                "geometryCount": len(geometry_handles),
                "features": {
                    "geometryCount":
                        len(geometry_handles)
                }
            })
        except:
            parse_failures = parse_failures + 1

    return {
        "objects": objects,
        "capturedContourCount":
            _parse_int(
                values.get(
                    "CAPTURED_CONTOUR_COUNT",
                    "0"
                ),
                0
            ),
        "assignedUniqueContourCount":
            _parse_int(
                values.get(
                    "ASSIGNED_UNIQUE_CONTOUR_COUNT",
                    "0"
                ),
                0
            ),
        "unassignedContourCount":
            _parse_int(
                values.get(
                    "UNASSIGNED_CONTOUR_COUNT",
                    "0"
                ),
                0
            ),
        "conflictHandleCount":
            _parse_int(
                values.get(
                    "CONFLICT_HANDLE_COUNT",
                    "0"
                ),
                0
            ),
        "missingSeedHandleCount":
            _parse_int(
                values.get(
                    "MISSING_SEED_HANDLE_COUNT",
                    "0"
                ),
                0
            ),
        "sourceErrorCount":
            _parse_int(
                values.get(
                    "SOURCE_ERROR_COUNT",
                    "0"
                ),
                0
            ),
        "extentFailureCount":
            _parse_int(
                values.get(
                    "EXTENT_FAILURE_COUNT",
                    "0"
                ),
                0
            ),
        "parseFailureCount": parse_failures,
        "sourceStatus":
            values.get("STATUS", "")
    }


def _drawing_extent(objects):
    if len(objects) == 0:
        return {
            "x1": 0.0,
            "y1": 0.0,
            "x2": 0.0,
            "y2": 0.0
        }

    x1 = objects[0]["extent"]["x1"]
    y1 = objects[0]["extent"]["y1"]
    x2 = objects[0]["extent"]["x2"]
    y2 = objects[0]["extent"]["y2"]

    for item in objects[1:]:
        extent = item["extent"]
        x1 = min(x1, extent["x1"])
        y1 = min(y1, extent["y1"])
        x2 = max(x2, extent["x2"])
        y2 = max(y2, extent["y2"])

    return {
        "x1": x1,
        "y1": y1,
        "x2": x2,
        "y2": y2
    }


def _run_extraction():
    sequence = 0

    for script_name in DETECTOR_SCRIPTS:
        sequence = sequence + 1
        _load_runtime_script(
            script_name,
            sequence
        )

    result = _parse_expansion()

    result["status"] = "succeeded"

    if (
        result["sourceStatus"].upper() != "SUCCESS" or
        result["parseFailureCount"] != 0 or
        result["sourceErrorCount"] != 0 or
        result["extentFailureCount"] != 0 or
        result["missingSeedHandleCount"] != 0 or
        result["conflictHandleCount"] != 0
    ):
        result["status"] = "failed"

    return result


def _plan_match(chunk, pattern, default_value):
    match = re.search(pattern, chunk, re.S)

    if match is None:
        return default_value

    return match.group(1)


def _read_plan_items():
    if not os.path.isfile(PLAN_PATH):
        raise Exception(
            "label plan not found: " +
            PLAN_PATH
        )

    handle = open(PLAN_PATH, "rb")

    try:
        source = handle.read()
    finally:
        handle.close()

    if source[:3] == "\xef\xbb\xbf":
        source = source[3:]

    result = []
    chunks = source.split('"operationId"')

    for chunk in chunks[1:]:
        operation_id = _plan_match(
            chunk,
            r'^\s*:\s*"([^"]+)"',
            ""
        )

        source_object_id = _plan_match(
            chunk,
            r'"sourceObjectId"\s*:\s*"([^"]+)"',
            ""
        )

        stable_object_id = _plan_match(
            chunk,
            r'"stableObjectId"\s*:\s*"([^"]+)"',
            ""
        )

        category = _plan_match(
            chunk,
            r'"category"\s*:\s*"([^"]+)"',
            ""
        )

        expected_text = _plan_match(
            chunk,
            r'"text"\s*:\s*"([^"]+)"',
            ""
        )

        min_x = _parse_float(
            _plan_match(
                chunk,
                r'"minX"\s*:\s*([-+0-9.eE]+)',
                "0"
            ),
            0.0
        )

        min_y = _parse_float(
            _plan_match(
                chunk,
                r'"minY"\s*:\s*([-+0-9.eE]+)',
                "0"
            ),
            0.0
        )

        max_x = _parse_float(
            _plan_match(
                chunk,
                r'"maxX"\s*:\s*([-+0-9.eE]+)',
                "0"
            ),
            0.0
        )

        max_y = _parse_float(
            _plan_match(
                chunk,
                r'"maxY"\s*:\s*([-+0-9.eE]+)',
                "0"
            ),
            0.0
        )

        x_value = _parse_float(
            _plan_match(
                chunk,
                r'"provisionalX"\s*:\s*([-+0-9.eE]+)',
                "0"
            ),
            0.0
        )

        y_value = _parse_float(
            _plan_match(
                chunk,
                r'"provisionalY"\s*:\s*([-+0-9.eE]+)',
                "0"
            ),
            0.0
        )

        height = _parse_float(
            _plan_match(
                chunk,
                r'"textHeight"\s*:\s*([-+0-9.eE]+)',
                "3.5"
            ),
            3.5
        )

        colour = _plan_match(
            chunk,
            r'"colour"\s*:\s*"([^"]+)"',
            "Yellow"
        )

        if (
            operation_id == "" or
            source_object_id == "" or
            stable_object_id == "" or
            expected_text == ""
        ):
            continue

        width = max_x - min_x
        allowed_distance = max(
            12.0,
            width * 0.15
        )

        result.append({
            "operationId": operation_id,
            "sourceObjectId": source_object_id,
            "stableObjectId": stable_object_id,
            "category": category,
            "expectedText": expected_text,
            "x": x_value,
            "y": y_value,
            "height": height,
            "colour": colour,
            "targetExtent": {
                "x1": min_x,
                "y1": min_y,
                "x2": max_x,
                "y2": max_y
            },
            "allowedDistance": allowed_distance
        })

    return result


def _resolve_current_targets(plan_items, objects):
    by_id = {}

    for item in objects:
        by_id[item["runtimeObjectId"]] = item

    missing = []

    for plan_item in plan_items:
        source_object_id = plan_item[
            "sourceObjectId"
        ]

        if not by_id.has_key(source_object_id):
            missing.append(source_object_id)
            continue

        extent = by_id[source_object_id][
            "extent"
        ]

        plan_item["targetExtent"] = extent
        plan_item["allowedDistance"] = max(
            12.0,
            (
                extent["x2"] -
                extent["x1"]
            ) * 0.15
        )

    return missing


def _preflight(plan_items, label_index):
    items = []
    already_count = 0
    missing_count = 0
    duplicate_count = 0
    conflict_count = 0

    for plan_item in plan_items:
        matches = label_index[
            "byText"
        ].get(
            plan_item["expectedText"],
            []
        )

        match_count = len(matches)
        nearest_distance = 0.0
        nearest_match = None

        for match in matches:
            distance = (
                _point_to_extent_distance(
                    match["center"],
                    plan_item[
                        "targetExtent"
                    ]
                )
            )

            if (
                nearest_match is None or
                distance < nearest_distance
            ):
                nearest_match = match
                nearest_distance = distance

        decision = "READY_TO_CREATE"

        if match_count > 1:
            decision = "BLOCKED_DUPLICATE"
            duplicate_count = (
                duplicate_count + 1
            )
        elif match_count == 1:
            if (
                nearest_distance <=
                plan_item["allowedDistance"]
            ):
                decision = "ALREADY_APPLIED"
                already_count = (
                    already_count + 1
                )
            else:
                decision = (
                    "BLOCKED_TEXT_CONFLICT"
                )
                conflict_count = (
                    conflict_count + 1
                )
        else:
            missing_count = missing_count + 1

        result_item = {
            "operationId":
                plan_item["operationId"],
            "sourceObjectId":
                plan_item["sourceObjectId"],
            "stableObjectId":
                plan_item["stableObjectId"],
            "expectedText":
                plan_item["expectedText"],
            "matchCount": match_count,
            "nearestDistance":
                nearest_distance,
            "allowedDistance":
                plan_item["allowedDistance"],
            "decision": decision,
            "matchHandle": None,
            "targetExtent":
                plan_item["targetExtent"]
        }

        if nearest_match is not None:
            result_item["matchHandle"] = (
                nearest_match["runtimeHandle"]
            )
            result_item["matchExtent"] = (
                nearest_match["extent"]
            )

        items.append(result_item)

    status = "SUCCESS"

    if (
        duplicate_count > 0 or
        conflict_count > 0 or
        len(label_index["errors"]) > 0
    ):
        status = "BLOCKED"

    return {
        "status": status,
        "preAlreadyPresentCount":
            already_count,
        "preMissingCount": missing_count,
        "preDuplicateTextCount":
            duplicate_count,
        "preTextConflictCount":
            conflict_count,
        "preInspectionErrorCount":
            len(label_index["errors"]),
        "items": items
    }


def _execute_preflight():
    extraction = _run_extraction()
    plan_items = _read_plan_items()
    missing_targets = _resolve_current_targets(
        plan_items,
        extraction["objects"]
    )

    label_index = _capture_label_index()
    result = _preflight(
        plan_items,
        label_index
    )

    if (
        extraction["status"] != "succeeded" or
        len(missing_targets) > 0
    ):
        result["status"] = "BLOCKED"
        result["missingSourceObjectIds"] = (
            missing_targets
        )

    result["planItems"] = plan_items
    result["extraction"] = extraction
    PLAN_BINDING.attach_plan_binding(result)
    return result


def _create_label(plan_item):
    value = KcsText.Text()
    value.SetString(
        plan_item["expectedText"]
    )

    point = KcsPoint2D.Point2D()
    point.X = plan_item["x"]
    point.Y = plan_item["y"]

    value.SetPosition(point)
    value.SetHeight(
        plan_item["height"]
    )

    value.SetColour(
        KcsColour.Colour(
            plan_item["colour"]
        )
    )

    return kcs_draft.text_new(value)


def _same_colour(first, second):
    return (
        _text(first).lower() ==
        _text(second).lower()
    )


def _strict_created_ok(plan_item, observed):
    distance = _point_to_extent_distance(
        observed["center"],
        plan_item["targetExtent"]
    )

    return (
        abs(observed["x"] - plan_item["x"])
        <= POSITION_TOLERANCE and
        abs(observed["y"] - plan_item["y"])
        <= POSITION_TOLERANCE and
        abs(
            observed["height"] -
            plan_item["height"]
        ) <= HEIGHT_TOLERANCE and
        _same_colour(
            observed["colour"],
            plan_item["colour"]
        ) and
        distance <=
        plan_item["allowedDistance"]
    )


def _existing_match_ok(plan_item, observed):
    distance = _point_to_extent_distance(
        observed["center"],
        plan_item["targetExtent"]
    )

    return (
        distance <=
        plan_item["allowedDistance"]
    )


def _existing_drift_fields(plan_item, observed):
    result = []

    if (
        abs(observed["x"] - plan_item["x"])
        > POSITION_TOLERANCE
    ):
        result.append("X")

    if (
        abs(observed["y"] - plan_item["y"])
        > POSITION_TOLERANCE
    ):
        result.append("Y")

    if (
        abs(
            observed["height"] -
            plan_item["height"]
        ) > HEIGHT_TOLERANCE
    ):
        result.append("HEIGHT")

    if not _same_colour(
        observed["colour"],
        plan_item["colour"]
    ):
        result.append("COLOUR")

    return result


def _apply_missing(
    operation_id,
    request_text
):
    started_at = _now()
    binding = PLAN_BINDING.parse_request_binding(
        request_text
    )
    PLAN_BINDING.validate_authorization(
        binding
    )
    preflight = _execute_preflight()
    PLAN_BINDING.validate_against_preflight(
        binding,
        preflight
    )
    plan_items = preflight["planItems"]

    base = {
        "schemaVersion": "1.0",
        "taskType":
            "geometry.label-apply-missing",
        "operationId": operation_id,
        "drawingContext":
            "current_drafting_context",
        "startedAt": started_at,
        "completedAt": _now(),
        "preAlreadyPresentCount":
            preflight[
                "preAlreadyPresentCount"
            ],
        "preMissingCount":
            preflight["preMissingCount"],
        "preDuplicateTextCount":
            preflight[
                "preDuplicateTextCount"
            ],
        "preInspectionErrorCount":
            preflight[
                "preInspectionErrorCount"
            ],
        "createdCount": 0,
        "createFailedCount": 0,
        "postValidLabelCount":
            preflight[
                "preAlreadyPresentCount"
            ],
        "postMissingCount":
            preflight["preMissingCount"],
        "postDuplicateCount":
            preflight[
                "preDuplicateTextCount"
            ],
        "postCreatedValidCount": 0,
        "postCreatedPropertyErrorCount": 0,
        "postExistingMatchErrorCount": 0,
        "postExistingPropertyDriftCount": 0,
        "postInspectionErrorCount": 0,
        "drawingWritePerformed": False,
        "drawingWriteCount": 0,
        "manualRecoveryRequired": False,
        "createdOperationIds": [],
        "createdRuntimeHandles": [],
        "failedOperationIds": [],
        "savePerformed": False
    }

    if preflight["status"] == "BLOCKED":
        base["status"] = "BLOCKED"
        base["completedAt"] = _now()
        return base

    if preflight["preMissingCount"] == 0:
        base["status"] = "ALREADY_COMPLETE"
        base["postMissingCount"] = 0
        base["postDuplicateCount"] = 0
        base["postValidLabelCount"] = len(
            plan_items
        )
        base["completedAt"] = _now()
        return base

    plan_by_operation = {}
    pre_existing = {}
    ready_to_create = {}

    for plan_item in plan_items:
        plan_by_operation[
            plan_item["operationId"]
        ] = plan_item

    for item in preflight["items"]:
        if item["decision"] == "ALREADY_APPLIED":
            pre_existing[
                item["operationId"]
            ] = 1
        elif item["decision"] == "READY_TO_CREATE":
            ready_to_create[
                item["operationId"]
            ] = 1

    created = []
    failed = []

    for operation_key in ready_to_create.keys():
        plan_item = plan_by_operation[
            operation_key
        ]

        try:
            runtime_handle = _create_label(
                plan_item
            )

            created.append({
                "operationId": operation_key,
                "runtimeHandle":
                    runtime_handle
            })
        except Exception, error:
            failed.append({
                "operationId": operation_key,
                "error": _text(error)
            })

    if len(created) > 0:
        try:
            kcs_ui.app_window_refresh()
        except:
            pass

    after = _capture_label_index()
    post_missing = 0
    post_duplicate = 0
    created_valid = 0
    created_property_errors = 0
    created_handle_by_operation = {}
    existing_valid = 0
    existing_match_errors = 0
    existing_drift_count = 0
    drift_items = []

    for operation_key in plan_by_operation.keys():
        plan_item = plan_by_operation[
            operation_key
        ]

        matches = after["byText"].get(
            plan_item["expectedText"],
            []
        )

        if len(matches) == 0:
            post_missing = post_missing + 1

            if ready_to_create.has_key(
                operation_key
            ):
                created_property_errors = (
                    created_property_errors + 1
                )
            else:
                existing_match_errors = (
                    existing_match_errors + 1
                )

            continue

        if len(matches) > 1:
            post_duplicate = (
                post_duplicate + 1
            )

            if ready_to_create.has_key(
                operation_key
            ):
                created_property_errors = (
                    created_property_errors + 1
                )
            else:
                existing_match_errors = (
                    existing_match_errors + 1
                )

            continue

        observed = matches[0]

        if ready_to_create.has_key(
            operation_key
        ):
            created_handle_by_operation[
                operation_key
            ] = observed["runtimeHandle"]

            if _strict_created_ok(
                plan_item,
                observed
            ):
                created_valid = (
                    created_valid + 1
                )
            else:
                created_property_errors = (
                    created_property_errors + 1
                )
        else:
            if _existing_match_ok(
                plan_item,
                observed
            ):
                existing_valid = (
                    existing_valid + 1
                )

                drift_fields = (
                    _existing_drift_fields(
                        plan_item,
                        observed
                    )
                )

                if len(drift_fields) > 0:
                    existing_drift_count = (
                        existing_drift_count + 1
                    )

                    drift_items.append({
                        "operationId":
                            operation_key,
                        "stableObjectId":
                            plan_item[
                                "stableObjectId"
                            ],
                        "fields":
                            drift_fields,
                        "actualX":
                            observed["x"],
                        "actualY":
                            observed["y"],
                        "actualHeight":
                            observed["height"],
                        "actualColour":
                            observed["colour"],
                        "plannedX":
                            plan_item["x"],
                        "plannedY":
                            plan_item["y"],
                        "plannedHeight":
                            plan_item["height"],
                        "plannedColour":
                            plan_item["colour"]
                    })
            else:
                existing_match_errors = (
                    existing_match_errors + 1
                )

    failed_ids = []
    created_ids = []
    created_handles = []

    for item in failed:
        failed_ids.append(
            item["operationId"]
        )

    for item in created:
        created_ids.append(
            item["operationId"]
        )

        if created_handle_by_operation.has_key(
            item["operationId"]
        ):
            created_handles.append(
                created_handle_by_operation[
                    item["operationId"]
                ]
            )
        else:
            created_handles.append(
                _text(item["runtimeHandle"])
            )

    base["createdCount"] = len(created)
    base["createFailedCount"] = len(failed)
    base["postValidLabelCount"] = (
        created_valid + existing_valid
    )
    base["postMissingCount"] = post_missing
    base["postDuplicateCount"] = (
        post_duplicate
    )
    base["postCreatedValidCount"] = (
        created_valid
    )
    base[
        "postCreatedPropertyErrorCount"
    ] = created_property_errors
    base[
        "postExistingMatchErrorCount"
    ] = existing_match_errors
    base[
        "postExistingPropertyDriftCount"
    ] = existing_drift_count
    base["postInspectionErrorCount"] = len(
        after["errors"]
    )
    base["existingPropertyDrifts"] = (
        drift_items
    )
    base["drawingWritePerformed"] = (
        len(created) > 0
    )
    base["drawingWriteCount"] = len(created)
    base["createdOperationIds"] = (
        created_ids
    )
    base["createdRuntimeHandles"] = (
        created_handles
    )
    base["failedOperationIds"] = failed_ids
    base["manualRecoveryRequired"] = (
        len(failed) > 0
    )

    if len(failed) > 0:
        base["status"] = "PARTIAL_FAILURE"
    elif (
        post_missing > 0 or
        post_duplicate > 0 or
        created_property_errors > 0 or
        existing_match_errors > 0 or
        len(after["errors"]) > 0
    ):
        base["status"] = "FAILED_POSTCHECK"
    else:
        base["status"] = "SUCCESS"

    base["completedAt"] = _now()
    return base


def _detect_payload(operation_id, started_at):
    extraction = _run_extraction()

    return {
        "schemaVersion": "1.0",
        "taskType": "geometry.detect",
        "operationId": operation_id,
        "drawingContext":
            "current_drafting_context",
        "startedAt": started_at,
        "completedAt": _now(),
        "status": extraction["status"],
        "drawingWritePerformed": False,
        "objects": extraction["objects"],
        "drawingExtent":
            _drawing_extent(
                extraction["objects"]
            ),
        "diagnostics": {
            "capturedContourCount":
                extraction[
                    "capturedContourCount"
                ],
            "assignedUniqueContourCount":
                extraction[
                    "assignedUniqueContourCount"
                ],
            "unassignedContourCount":
                extraction[
                    "unassignedContourCount"
                ],
            "conflictHandleCount":
                extraction[
                    "conflictHandleCount"
                ],
            "parseFailureCount":
                extraction[
                    "parseFailureCount"
                ],
            "runtimeHandlesCurrentInvocation":
                True,
            "runtimeHandlesPersistent":
                False
        },
        "savePerformed": False
    }


def _highlight_payload(
    operation_id,
    action,
    started_at,
    target_categories
):
    extraction = _run_extraction()
    objects = []
    target_keys = {}

    for item in extraction["objects"]:
        if target_categories.has_key(
            item["category"]
        ):
            objects.append(item)

            for runtime_key in item[
                "geometryHandles"
            ]:
                target_keys[runtime_key] = 1

    reported, runtime_handles = (
        _capture_contours()
    )

    runtime_by_key = {}

    for runtime_handle in runtime_handles:
        runtime_by_key[
            _text(runtime_handle)
        ] = runtime_handle

    try:
        kcs_draft.highlight_off(0)
    except:
        pass

    success_count = 0
    missing_keys = []
    failure_keys = []

    for runtime_key in target_keys.keys():
        if not runtime_by_key.has_key(
            runtime_key
        ):
            missing_keys.append(runtime_key)
            continue

        try:
            kcs_draft.element_highlight(
                runtime_by_key[runtime_key]
            )
            success_count = (
                success_count + 1
            )
        except:
            failure_keys.append(runtime_key)

    try:
        kcs_ui.app_window_refresh()
    except:
        pass

    categories = target_categories.keys()
    categories.sort()

    status = "succeeded"

    if (
        extraction["status"] != "succeeded" or
        len(missing_keys) > 0 or
        len(failure_keys) > 0
    ):
        status = "failed"

    return {
        "schemaVersion": "1.0",
        "taskType": action,
        "operationId": operation_id,
        "drawingContext":
            "current_drafting_context",
        "startedAt": started_at,
        "completedAt": _now(),
        "status": status,
        "drawingWritePerformed": False,
        "highlightedObjectCount":
            len(objects),
        "highlightedHandleCount":
            len(target_keys),
        "highlightSuccessCount":
            success_count,
        "missingHandleCount":
            len(missing_keys),
        "highlightFailureCount":
            len(failure_keys),
        "categories": categories,
        "diagnostics": {
            "captureReportedCount":
                reported,
            "captureActualCount":
                len(runtime_handles),
            "missingRuntimeKeys":
                missing_keys,
            "failedRuntimeKeys":
                failure_keys,
            "runtimeHandlesCurrentInvocation":
                True,
            "runtimeHandlesPersistent":
                False
        },
        "savePerformed": False
    }


def _preflight_payload(operation_id, started_at):
    preflight = _execute_preflight()

    return {
        "schemaVersion": "1.0",
        "taskType":
            "geometry.label-preflight",
        "operationId": operation_id,
        "drawingContext":
            "current_drafting_context",
        "startedAt": started_at,
        "completedAt": _now(),
        "status": preflight["status"],
        "preAlreadyPresentCount":
            preflight[
                "preAlreadyPresentCount"
            ],
        "preMissingCount":
            preflight["preMissingCount"],
        "preDuplicateTextCount":
            preflight[
                "preDuplicateTextCount"
            ],
        "preInspectionErrorCount":
            preflight[
                "preInspectionErrorCount"
            ],
        "preTextConflictCount":
            preflight[
                "preTextConflictCount"
            ],
        "items": preflight["items"],
        "planHash": preflight["planHash"],
        "readyOperationIds":
            preflight["readyOperationIds"],
        "drawingWritePerformed": False,
        "savePerformed": False
    }


def _payload(
    operation_id,
    action,
    request_text
):
    started_at = _now()

    if action == "geometry.detect":
        return _detect_payload(
            operation_id,
            started_at
        )

    if action == "geometry.highlight-clear":
        kcs_draft.highlight_off(0)

        try:
            kcs_ui.app_window_refresh()
        except:
            pass

        return {
            "schemaVersion": "1.0",
            "taskType": action,
            "operationId": operation_id,
            "drawingContext":
                "current_drafting_context",
            "startedAt": started_at,
            "completedAt": _now(),
            "status": "succeeded",
            "cleared": True,
            "drawingWritePerformed": False,
            "savePerformed": False
        }

    if action == "geometry.highlight-lifting":
        return _highlight_payload(
            operation_id,
            action,
            started_at,
            LIFTING_CATEGORIES
        )

    if action == "geometry.highlight-flanges":
        return _highlight_payload(
            operation_id,
            action,
            started_at,
            FLANGE_CATEGORIES
        )

    if action == "geometry.label-preflight":
        return _preflight_payload(
            operation_id,
            started_at
        )

    if action == "geometry.label-apply-missing":
        return _apply_missing(
            operation_id,
            request_text
        )

    raise Exception(
        "unsupported action: " + action
    )


def _result_envelope(
    command_id,
    correlation_id,
    causation_id,
    status,
    result,
    error
):
    return {
        "protocol": "AM.TribonBridge",
        "version": "0.1",
        "messageType": "bridge.result",
        "messageId": "RES-" + command_id,
        "commandId": command_id,
        "correlationId": correlation_id,
        "causationId": causation_id,
        "createdAt": _now(),
        "status": status,
        "result": result,
        "warnings": [],
        "error": error
    }


def _write_worker_diagnostic(stage, status, message, request_name, details):
    try:
        lines = [
            "STAGE=" + _text(stage),
            "STATUS=" + _text(status),
            "TIME=" + _now(),
            "CWD=" + _text(os.getcwd()),
            "FILE=" + _text(globals().get("__file__", "")),
            "ADDIN_ROOT=" + _text(globals().get("ADDIN_ROOT", "")),
            "RUNTIME_ROOT=" + _text(globals().get("RUNTIME_ROOT", ""))
        ]
        if request_name is not None:
            lines.append("REQUEST_NAME=" + _text(request_name))
        if message is not None and _text(message) != "":
            lines.append("ERROR=" + _text(message)[:500])
        if details is not None and _text(details) != "":
            lines.append("DETAILS=" + _text(details)[:500])
        _atomic(
            os.path.join(DIAGNOSTICS, "geometry-object-worker-last-run.txt"),
            "\n".join(lines) + "\n"
        )
    except:
        pass


def _process(name):
    source = os.path.join(
        PROCESSING,
        name
    )

    handle = open(source, "rb")

    try:
        request_text = handle.read()
    finally:
        handle.close()

    command_id = _field(
        request_text,
        "commandId",
        ""
    )

    message_id = _field(
        request_text,
        "messageId",
        ""
    )

    correlation_id = _field(
        request_text,
        "correlationId",
        ""
    )

    action = _field(
        request_text,
        "action",
        ""
    )

    operation_id = _field(
        request_text,
        "operationId",
        command_id
    )

    envelope = None

    try:
        if (
            action ==
            "geometry.label-apply-missing" and
            not _bool_field(
                request_text,
                "allowWrite",
                False
            )
        ):
            envelope = _result_envelope(
                command_id,
                correlation_id,
                message_id,
                "failed",
                None,
                {
                    "code":
                        "allow_write_required",
                    "category":
                        "validation",
                    "message":
                        "allowWrite must be true",
                    "retryable": False
                }
            )
        else:
            result = _payload(
                operation_id,
                action,
                request_text
            )

            envelope = _result_envelope(
                command_id,
                correlation_id,
                message_id,
                "succeeded",
                result,
                None
            )
    except Exception, error:
        envelope = _result_envelope(
            command_id,
            correlation_id,
            message_id,
            "failed",
            None,
            {
                "code": getattr(
                    error,
                    "code",
                    "geometry_execution_failed"
                ),
                "category": getattr(
                    error,
                    "category",
                    "execution"
                ),
                "message": _text(error),
                "retryable": False,
                "details": {
                    "traceback":
                        traceback.format_exc()
                }
            }
        )

    _atomic(
        os.path.join(
            OUTPUT,
            command_id + ".result.json"
        ),
        _json(envelope)
    )

    archive_path = os.path.join(
        ARCHIVE,
        name
    )

    if os.path.exists(archive_path):
        os.remove(archive_path)

    os.rename(source, archive_path)
    if envelope.get("status") == "succeeded":
        _write_worker_diagnostic("PROCESS", "SUCCESS", None, name, "result written")
    else:
        _write_worker_diagnostic("PROCESS", "FAILED", envelope.get("error", {}).get("message", "processing failed"), name, "result written")


def run(*args):
    _write_worker_diagnostic("DIRECT_ENTRY", "STARTED", None, None, None)
    for directory in (
        INBOX,
        PROCESSING,
        OUTPUT,
        ARCHIVE,
        DIAGNOSTICS
    ):
        if not os.path.exists(directory):
            os.makedirs(directory)

    names = []

    for name in os.listdir(INBOX):
        if name.endswith(".request.json"):
            names.append(name)

    names.sort()

    if len(names) == 0:
        _write_worker_diagnostic("DIRECT_ENTRY", "NO_REQUEST", None, None, None)
        return "No request found"

    name = names[0]

    os.rename(
        os.path.join(INBOX, name),
        os.path.join(PROCESSING, name)
    )
    _write_worker_diagnostic("DIRECT_ENTRY", "REQUEST_SELECTED", None, name, None)

    try:
        _process(name)
        return "processed " + name
    except Exception, error:
        _write_worker_diagnostic("DIRECT_ENTRY", "FAILED", error, name, traceback.format_exc())
        return (
            "worker failure: " +
            _text(error) +
            "\n" +
            traceback.format_exc()
        )
# ---------------------------------------------------------------------------
# Direct Vitesse entry point.
#
# Vitesse's file chooser executes this file as __main__, but importing this
# module or executing it through a wrapper with another __name__ must only
# define the worker functions. The active flag prevents re-entrant execution
# while still allowing later independent requests in the same interpreter.
# ---------------------------------------------------------------------------

def _should_run_direct_entry():
    actual_name = ""
    try:
        actual_name = __name__
        _bootstrap_status("DIRECT_ENTRY_CHECK", "NAME=" + actual_name)
        if actual_name != "__main__":
            _bootstrap_status("NOT_MAIN", "NAME=" + actual_name)
            return False
    except:
        return False

    try:
        if _AM_GEOMETRY_SUPPRESS_AUTORUN:
            return False
    except:
        pass

    return True


def _run_direct_entry():
    global _AM_GEOMETRY_DIRECT_ENTRY_ACTIVE

    try:
        if _AM_GEOMETRY_DIRECT_ENTRY_ACTIVE:
            return None
    except:
        pass

    _AM_GEOMETRY_DIRECT_ENTRY_ACTIVE = True
    _bootstrap_status("RUN_STARTED", "")

    try:
        return run()
    finally:
        _AM_GEOMETRY_DIRECT_ENTRY_ACTIVE = False


if _should_run_direct_entry():
    _AM_GEOMETRY_DIRECT_ENTRY_RESULT = _run_direct_entry()
