# -*- coding: ascii -*-
import hashlib
import json
import re
import sys

CONTRACT_VERSION = "geometry-label-plan/v1"
SELF_TEST_EXPECTED_HASH = (
    "F2B14D4200E1AC239FBF1CFD28D2F994"
    "39E631EC2D6FA129ECB6A92A841B75F2"
)

try:
    TEXT_TYPE = unicode
except NameError:
    TEXT_TYPE = str


class PlanBindingError(Exception):
    def __init__(self, code, category, message):
        Exception.__init__(self, message)
        self.code = code
        self.category = category
        self.message = message


def _utf8(value):
    if value is None:
        value = ""
    if isinstance(value, bytes):
        return value
    if isinstance(value, TEXT_TYPE):
        return value.encode("utf-8")
    return TEXT_TYPE(value).encode("utf-8")


def _sort_text(value):
    data = _utf8(value)
    try:
        return data.decode("utf-8")
    except:
        return TEXT_TYPE(data)


def _append_field(parts, value):
    data = _utf8(value)
    parts.append(
        str(len(data)).encode("ascii") +
        b":" +
        data +
        b"\n"
    )


def compute_plan_hash(preflight):
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

    return hashlib.sha256(
        b"".join(parts)
    ).hexdigest().upper()


def ready_operation_ids(preflight):
    result = []

    for item in preflight.get("items", []):
        if item.get("decision") == "READY_TO_CREATE":
            result.append(
                item.get("operationId", "")
            )

    result.sort(key=_sort_text)
    return result


def attach_plan_binding(preflight):
    preflight["planHash"] = compute_plan_hash(
        preflight
    )
    preflight["readyOperationIds"] = (
        ready_operation_ids(preflight)
    )
    return preflight


def _invalid(message):
    raise PlanBindingError(
        "geometry_label_plan_binding_invalid",
        "validation",
        message
    )


def _changed(message):
    raise PlanBindingError(
        "geometry_label_plan_changed",
        "safety",
        message
    )


def parse_request_binding(request_text):
    try:
        document = json.loads(request_text)
    except Exception:
        _invalid("Request JSON is invalid.")

    payload = document.get("payload")

    if not isinstance(payload, dict):
        _invalid("Request payload is required.")

    return {
        "allowWrite":
            payload.get("allowWrite") is True,
        "writeConfirmed":
            payload.get("writeConfirmed") is True,
        "confirmedPreflightOperationId":
            payload.get(
                "confirmedPreflightOperationId",
                ""
            ),
        "confirmedPlanHash":
            payload.get(
                "confirmedPlanHash",
                ""
            ),
        "confirmedOperationIds":
            payload.get(
                "confirmedOperationIds",
                []
            )
    }


def validate_authorization(binding):
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


def validate_against_preflight(
    binding,
    current_preflight
):
    validate_authorization(binding)
    attached = attach_plan_binding(
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
    except PlanBindingError:
        raised = True

    if not raised:
        raise Exception(
            "expected plan-binding error was not raised"
        )


def self_test():
    preflight = attach_plan_binding(
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

    validate_against_preflight(
        valid,
        _self_test_preflight()
    )

    invalid_hash = dict(valid)
    invalid_hash["confirmedPlanHash"] = (
        "A" * 64
    )

    _expect_binding_error(
        lambda: validate_against_preflight(
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
        lambda: validate_authorization(
            duplicate
        )
    )


if __name__ == "__main__":
    self_test()
    sys.stdout.write(
        "ROUND4_3A2_VITESSE_PLAN_BINDING=PASS\n"
    )