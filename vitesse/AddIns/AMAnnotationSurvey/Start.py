# -*- coding: ascii -*-
import os
import time
import traceback
import kcs_draft
import KcsText
import KcsCaptureRegion2D

ROOT = r"C:\AM_TribonBridge"
SURVEY_VERSION = "1.0"
_log_handle = None
_run_active = False

def _text(value):
    try:
        if value is None: return ""
        if isinstance(value, unicode): return value.encode("utf-8", "replace")
        return str(value)
    except Exception, e: return "<value error>"

def _json(value):
    if value is None: return "null"
    if value is True: return "true"
    if value is False: return "false"
    if isinstance(value, (int, long, float)): return str(value)
    if isinstance(value, list): return "[" + ",".join([_json(x) for x in value]) + "]"
    if isinstance(value, dict): return "{" + ",".join([_json(str(k)) + ":" + _json(value[k]) for k in value]) + "}"
    return '"' + _text(value).replace("\\", "\\\\").replace('"', '\\"').replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t") + '"'

def _log(message):
    try:
        if _log_handle is not None:
            _log_handle.write((_text(time.time()) + " " + _text(message) + "\n").encode("utf-8")); _log_handle.flush()
    except Exception, e: pass

def _bootstrap(message):
    try:
        logs = os.path.join(ROOT, "logs")
        if not os.path.isdir(logs): os.makedirs(logs)
        handle = open(os.path.join(logs, "am_annotation_survey.log"), "ab")
        try: handle.write((_text(time.time()) + " " + message + "\n").encode("utf-8")); handle.flush()
        finally: handle.close()
    except Exception, e: pass

def _call(fn, *args):
    try: return fn(*args), None
    except Exception, e: return None, _text(e)

def _handle(value): return _text(value)

def _extent(value):
    try:
        x1 = value.Corner1.X; y1 = value.Corner1.Y; x2 = value.Corner2.X; y2 = value.Corner2.Y
        return {"x1": min(x1, x2), "y1": min(y1, y2), "x2": max(x1, x2), "y2": max(y1, y2)}, None
    except Exception, e: return None, _text(e)

def _warning(warnings, seen, message):
    if message not in seen: seen[message] = True; warnings.append(message)

def _parse_capture(value, kind, warnings, warning_seen, diagnostics):
    if value is None: return 0, []
    try: reported = value[0]
    except Exception, e: reported = 0
    try: handles = list(value[1:])
    except Exception, e: handles = []
    actual = len(handles)
    diagnostics["reported" + kind] = reported; diagnostics["actual" + kind] = actual
    if reported != actual: _warning(warnings, warning_seen, "Capture count mismatch for " + kind)
    return reported, handles

def _text_value(handle, diagnostics):
    text = KcsText.Text()
    value, error = _call(kcs_draft.text_properties_get, handle, text)
    if error is not None: diagnostics["textPropertyReadFailureCount"] += 1; return ""
    value, error = _call(value.GetString)
    if error is not None: diagnostics["textPropertyReadFailureCount"] += 1; return ""
    return _text(value)

def _capture(kind, fn, region, warnings, warning_seen, diagnostics):
    try:
        result = fn(region)
    except Exception, e:
        if _text(getattr(kcs_draft, "error", "")) == "kcs_NotFound": return 0, []
        diagnostics["captureFailureCount"] += 1; _warning(warnings, warning_seen, kind + " capture failed: " + _text(e)); return 0, []
    return _parse_capture(result, kind, warnings, warning_seen, diagnostics)

def _snapshot(started):
    warnings, warning_seen = [], {}
    diagnostics = {"reportedPositionNumberCount": 0, "actualPositionNumberCount": 0, "reportedDimensionCount": 0, "actualDimensionCount": 0, "reportedTextCount": 0, "capturedTextCount": 0, "independentTextCount": 0, "positionNumberChildTextCount": 0, "dimensionChildTextCount": 0, "unresolvedTextOwnerCount": 0, "labelExtentFallbackCount": 0, "parentLookupFailureCount": 0, "extentReadFailureCount": 0, "textExtentReadFailureCount": 0, "textPropertyReadFailureCount": 0, "captureFailureCount": 0}
    drawing, error = _call(kcs_draft.element_extent_get)
    if error is not None: raise Exception("drawing_context_unavailable")
    empty, error = _call(drawing.IsEmpty)
    if error is not None or empty: raise Exception("drawing_extent_empty")
    drawing_extent, error = _extent(drawing)
    if error is not None: raise Exception("drawing_extent_empty")
    region = KcsCaptureRegion2D.CaptureRegion2D(); region.SetRectangle(drawing); region.SetInside(); region.SetNoCut()
    posno_count, posnos = _capture("PositionNumber", kcs_draft.posno_capture, region, warnings, warning_seen, diagnostics)
    dim_count, dimensions = _capture("Dimension", kcs_draft.dim_capture, region, warnings, warning_seen, diagnostics)
    text_count, texts = _capture("Text", kcs_draft.text_capture, region, warnings, warning_seen, diagnostics)
    diagnostics["capturedTextCount"] = len(texts)
    posno_map, dim_map = {}, {}
    items = []
    for parent, kind, target in [(posnos, "position_number", posno_map), (dimensions, "dimension", dim_map)]:
        for handle in parent: target[_handle(handle)] = handle
    owned = {}
    for text_handle in texts:
        current = text_handle; visited = {}; owner = None; depth = 0
        while current is not None and depth < 6:
            key = _handle(current)
            if key in visited: break
            visited[key] = True
            parent, parent_error = _call(kcs_draft.element_parent_get, current)
            if parent_error is not None:
                diagnostics["parentLookupFailureCount"] += 1; diagnostics["unresolvedTextOwnerCount"] += 1; break
            if parent is None: break
            parent_key = _handle(parent)
            if parent_key in posno_map or parent_key in dim_map: owner = parent_key; break
            current = parent; depth += 1
        if owner is not None: owned.setdefault(owner, []).append(text_handle)
        else:
            text_extent, extent_error = _call(kcs_draft.element_extent_get, text_handle)
            text_box = None
            if extent_error is None: text_box, extent_error = _extent(text_extent)
            if text_box is None: diagnostics["textExtentReadFailureCount"] += 1; continue
            items.append({"role": "obstacle", "type": "text", "runtimeHandle": _handle(text_handle), "parentExtent": text_box, "labelExtent": text_box, "text": _text_value(text_handle, diagnostics), "childTextHandles": []}); diagnostics["independentTextCount"] += 1
    for parent_list, kind in [(posnos, "position_number"), (dimensions, "dimension")]:
        for parent in parent_list:
            parent_key = _handle(parent); parent_extent_value, parent_error = _call(kcs_draft.element_extent_get, parent)
            parent_box = None
            if parent_error is None: parent_box, extent_error = _extent(parent_extent_value)
            if parent_box is None: diagnostics["extentReadFailureCount"] += 1; continue
            child_boxes, child_handles, child_text = [], [], []
            for child in owned.get(parent_key, []):
                child_value, child_error = _call(kcs_draft.element_extent_get, child)
                child_box = None
                if child_error is None: child_box, extent_error = _extent(child_value)
                if child_box is None: diagnostics["textExtentReadFailureCount"] += 1; continue
                child_boxes.append(child_box); child_handles.append(_handle(child)); child_text.append(_text_value(child, diagnostics))
            label = parent_box
            if len(child_boxes) == 0: diagnostics["labelExtentFallbackCount"] += 1
            else:
                label = {"x1": min([x["x1"] for x in child_boxes]), "y1": min([x["y1"] for x in child_boxes]), "x2": max([x["x2"] for x in child_boxes]), "y2": max([x["y2"] for x in child_boxes])}
            if kind == "position_number": diagnostics["positionNumberChildTextCount"] += len(child_handles)
            else: diagnostics["dimensionChildTextCount"] += len(child_handles)
            items.append({"role": "movable", "type": kind, "runtimeHandle": parent_key, "parentExtent": parent_box, "labelExtent": label, "text": " | ".join(child_text), "childTextHandles": child_handles})
    return {"schemaVersion": "1.0", "scope": "current_drafting_context", "handleScope": "current_drafting_session_only", "snapshotId": _text(os.getpid()) + "-" + time.strftime("%Y%m%dT%H%M%S"), "drawingExtent": drawing_extent, "items": items, "diagnostics": diagnostics}

def _write_snapshot(snapshot):
    diagnostics = os.path.join(ROOT, "diagnostics")
    if not os.path.isdir(diagnostics): os.makedirs(diagnostics)
    target = os.path.join(diagnostics, "annotation-layout-snapshot.json"); temp = target + ".tmp"
    handle = open(temp, "wb")
    try: handle.write(_json(snapshot)); handle.flush()
    finally: handle.close()
    os.rename(temp, target); return target

def run(*args):
    global _log_handle, _run_active
    if _run_active: _bootstrap("RUN_SKIPPED_ALREADY_ACTIVE"); return "AMAnnotationSurvey already active"
    _run_active = True
    try:
        try:
            logs = os.path.join(ROOT, "logs"); diagnostics = os.path.join(ROOT, "diagnostics")
            if not os.path.isdir(logs): os.makedirs(logs)
            if not os.path.isdir(diagnostics): os.makedirs(diagnostics)
            _log_handle = open(os.path.join(logs, "am_annotation_survey.log"), "ab")
            _log("RUN_ENTER schemaVersion=1.0 pid=" + _text(os.getpid()))
            snapshot = _snapshot(time.time()); _log("SNAPSHOT_READY itemCount=" + _text(len(snapshot["items"])))
            target = _write_snapshot(snapshot); _log("RUN_SUCCESS snapshotPath=" + target); return target
        except Exception, e:
            _log("RUN_FAILED exceptionType=" + _text(type(e)) + " exceptionText=" + _text(e) + " traceback=" + _text(traceback.format_exc())); return _text(e)
    finally:
        try:
            if _log_handle is not None: _log_handle.close()
        except Exception, e: pass
        _log_handle = None; _run_active = False
