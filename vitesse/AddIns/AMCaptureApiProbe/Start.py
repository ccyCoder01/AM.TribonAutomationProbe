# -*- coding: ascii -*-
import os
import time
import traceback
import kcs_draft
import KcsCaptureRegion2D
import KcsPoint2D
import KcsRectangle2D

ROOT = r"C:\AM_TribonBridge"
CONFIG = os.path.join(ROOT, "config", "capture-api-probe-kind.txt")
PROBE_VERSION = "0.3"
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
    if isinstance(value, list): return "[" + ",".join([_json(item) for item in value]) + "]"
    if isinstance(value, dict): return "{" + ",".join([_json(str(key)) + ":" + _json(value[key]) for key in value]) + "}"
    return '"' + _text(value).replace("\\", "\\\\").replace('"', '\\"').replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t") + '"'

def _safe_log(message):
    try:
        if _log_handle is not None:
            _log_handle.write((_text(time.time()) + " " + _text(message) + "\n").encode("utf-8")); _log_handle.flush()
    except Exception, e: pass

def _bootstrap_log(message):
    try:
        logs = os.path.join(ROOT, "logs")
        if not os.path.isdir(logs): os.makedirs(logs)
        handle = open(os.path.join(logs, "am_capture_api_probe.log"), "ab")
        try: handle.write((_text(time.time()) + " " + message + "\n").encode("utf-8")); handle.flush()
        finally: handle.close()
    except Exception, e: pass

def _read_kind(warnings):
    selected = "TEXT"
    try:
        handle = open(CONFIG, "rb")
        try: selected = handle.read().strip().upper()
        finally: handle.close()
    except Exception, e: pass
    if selected not in ("TEXT", "DIMENSION", "POSITION_NUMBER", "NOTE"):
        selected = "TEXT"
    return selected

def _read_region_mode():
    selected = "DRAWING_EXTENT"
    path = os.path.join(ROOT, "config", "capture-api-probe-region.txt")
    try:
        handle = open(path, "rb")
        try: selected = handle.read().strip().upper()
        finally: handle.close()
    except Exception, e: pass
    if selected not in ("INFINITE", "CURRENT_VIEW_EXTENT", "DRAWING_EXTENT"): selected = "DRAWING_EXTENT"
    return selected

def _resolve_current_view():
    _safe_log("CURRENT_PATH_CALL_START")
    value, error = _call(kcs_draft.subpicture_current_get)
    count = 0
    if value is not None:
        try: count = len(value)
        except Exception, e: count = 0
    _safe_log("CURRENT_PATH_CALL_DONE count=" + _text(count))
    if error: _safe_log("CURRENT_VIEW_RESOLUTION_FAILED"); return None, "current_view_not_resolved"
    _safe_log("CURRENT_VIEW_RESOLUTION_START")
    index = 0
    for item in value:
        is_view, check_error = _call(kcs_draft.element_is_view, item)
        flag = 1
        if not is_view: flag = 0
        _safe_log("CURRENT_VIEW_ITEM index=" + _text(index) + " runtimeHandle=" + _text(item) + " isView=" + _text(flag))
        if is_view:
            _safe_log("CURRENT_VIEW_RESOLVED runtimeHandle=" + _text(item)); _safe_log("CURRENT_VIEW_RESOLUTION_DONE")
            return item, None
        index += 1
    _safe_log("CURRENT_VIEW_RESOLUTION_FAILED"); _safe_log("CURRENT_VIEW_RESOLUTION_DONE")
    return None, "current_view_not_resolved"

def _create_region(mode):
    if mode == "DRAWING_EXTENT":
        started = time.time(); _safe_log("DRAWING_EXTENT_CALL_START")
        try: drawing_extent = kcs_draft.element_extent_get()
        except Exception, e: _safe_log("DRAWING_EXTENT_CALL_DONE elapsedMilliseconds=" + _text(int((time.time() - started) * 1000))); return None, None, {"currentViewRuntimeHandle": "", "status": "not_requested"}, "drawing_extent_failed"
        _safe_log("DRAWING_EXTENT_CALL_DONE elapsedMilliseconds=" + _text(int((time.time() - started) * 1000)))
        try: empty = drawing_extent.IsEmpty()
        except Exception, e: return None, None, {"currentViewRuntimeHandle": "", "status": "not_requested"}, "drawing_extent_empty"
        if empty: return None, None, {"currentViewRuntimeHandle": "", "status": "not_requested"}, "drawing_extent_empty"
        try:
            point1 = KcsPoint2D.Point2D(drawing_extent.Corner1.X, drawing_extent.Corner1.Y)
            point2 = KcsPoint2D.Point2D(drawing_extent.Corner2.X, drawing_extent.Corner2.Y)
            _safe_log("DRAWING_EXTENT_VALUE corner1X=" + _text(drawing_extent.Corner1.X) + " corner1Y=" + _text(drawing_extent.Corner1.Y) + " corner2X=" + _text(drawing_extent.Corner2.X) + " corner2Y=" + _text(drawing_extent.Corner2.Y) + " isEmpty=0")
            rectangle = KcsRectangle2D.Rectangle2D(point1, point2)
            region = KcsCaptureRegion2D.CaptureRegion2D(); region.SetRectangle(rectangle); region.SetInside(); region.SetNoCut()
            return region, {"corner1": {"x": drawing_extent.Corner1.X, "y": drawing_extent.Corner1.Y}, "corner2": {"x": drawing_extent.Corner2.X, "y": drawing_extent.Corner2.Y}, "isEmpty": False}, {"currentViewRuntimeHandle": "", "status": "not_requested"}, None
        except Exception, e: return None, None, {"currentViewRuntimeHandle": "", "status": "not_requested"}, "capture_region_failed"
    elif mode == "INFINITE":
        region = KcsCaptureRegion2D.CaptureRegion2D(); region.SetBoundaryInfinite()
        return region, None, {"currentViewRuntimeHandle": "", "status": "not_resolved"}, None
    view, error = _resolve_current_view()
    if error is not None: return None, None, {"currentViewRuntimeHandle": "", "status": "not_resolved"}, error
    started = time.time(); _safe_log("VIEW_EXTENT_CALL_START runtimeHandle=" + _text(view))
    extent, error = _call(kcs_draft.element_extent_get, view)
    _safe_log("VIEW_EXTENT_CALL_DONE elapsedMilliseconds=" + _text(int((time.time() - started) * 1000)))
    if error is not None: return None, None, {"currentViewRuntimeHandle": _text(view), "status": "resolved"}, "view_extent_failed"
    try: empty = extent.IsEmpty()
    except Exception, e: return None, None, {"currentViewRuntimeHandle": _text(view), "status": "resolved"}, "view_extent_empty"
    if empty:
        return None, None, {"currentViewRuntimeHandle": _text(view), "status": "resolved"}, "view_extent_empty"
    try:
        point1 = KcsPoint2D.Point2D(extent.Corner1.X, extent.Corner1.Y)
        point2 = KcsPoint2D.Point2D(extent.Corner2.X, extent.Corner2.Y)
        _safe_log("VIEW_EXTENT_VALUE corner1X=" + _text(extent.Corner1.X) + " corner1Y=" + _text(extent.Corner1.Y) + " corner2X=" + _text(extent.Corner2.X) + " corner2Y=" + _text(extent.Corner2.Y) + " isEmpty=0")
        rectangle = KcsRectangle2D.Rectangle2D(point1, point2)
        region = KcsCaptureRegion2D.CaptureRegion2D(); region.SetRectangle(rectangle); region.SetInside(); region.SetNoCut()
        return region, {"corner1": {"x": extent.Corner1.X, "y": extent.Corner1.Y}, "corner2": {"x": extent.Corner2.X, "y": extent.Corner2.Y}, "isEmpty": False}, {"currentViewRuntimeHandle": _text(view), "status": "resolved"}, None
    except Exception, e: return None, None, {"currentViewRuntimeHandle": _text(view), "status": "resolved"}, "capture_region_failed"

def _parse_result(value, warnings):
    if value is None: return 0, []
    reported = 0
    try: reported = value[0]
    except Exception, e: reported = 0
    try: handles = list(value[1:])
    except Exception, e: handles = []
    actual = len(handles)
    if reported != actual: warnings.append("Capture count mismatch: reported=" + _text(reported) + ", actual=" + _text(actual))
    runtime_handles = []
    for item in handles: runtime_handles.append(_text(item))
    return reported, runtime_handles

def _write(report):
    diagnostics = os.path.join(ROOT, "diagnostics")
    if not os.path.isdir(diagnostics): os.makedirs(diagnostics)
    target = os.path.join(diagnostics, "capture-api-probe-" + time.strftime("%Y%m%dT%H%M%S") + "-" + _text(os.getpid()) + ".json")
    temp = target + ".tmp"
    handle = open(temp, "wb")
    try: handle.write(_json(report)); handle.flush()
    finally: handle.close()
    os.rename(temp, target); _safe_log("OUTPUT_WRITE_DONE"); return target

def run(*args):
    global _log_handle, _run_active
    if _run_active:
        _bootstrap_log("RUN_SKIPPED_ALREADY_ACTIVE"); return "AMCaptureApiProbe already active"
    _run_active = True
    try:
        try:
            logs = os.path.join(ROOT, "logs"); diagnostics = os.path.join(ROOT, "diagnostics")
            if not os.path.isdir(logs): os.makedirs(logs)
            if not os.path.isdir(diagnostics): os.makedirs(diagnostics)
            _log_handle = open(os.path.join(logs, "am_capture_api_probe.log"), "ab")
            _safe_log("RUN_ENTER probeVersion=0.3 pid=" + _text(os.getpid()) + " timestamp=" + _text(time.time()))
            warnings = []; _safe_log("CONFIG_READ_START"); selected = _read_kind(warnings)
            region_mode = _read_region_mode()
            mapping = {"TEXT": ("text_capture", kcs_draft.text_capture), "DIMENSION": ("dim_capture", kcs_draft.dim_capture), "POSITION_NUMBER": ("posno_capture", kcs_draft.posno_capture), "NOTE": ("note_capture", kcs_draft.note_capture)}
            api, capture_fn = mapping[selected]; _safe_log("CONFIG_READ_DONE selectedKind=" + selected + " regionMode=" + region_mode)
            _safe_log("CAPTURE_REGION_START mode=" + region_mode); region, view_extent, context, region_error = _create_region(region_mode)
            drawing_extent = None; view_extent_json = view_extent
            if region_mode == "DRAWING_EXTENT": drawing_extent = view_extent; view_extent_json = None
            if region_error is not None:
                report = {"probeVersion": PROBE_VERSION, "createdAt": time.strftime("%Y-%m-%dT%H:%M:%S"), "readOnly": True, "selectedKind": selected, "captureApi": api, "regionMode": region_mode, "context": context, "drawingExtent": drawing_extent, "viewExtent": view_extent_json, "captureRegion": {"type": "rectangle", "source": "drawing_extent", "inside": True, "cut": False, "api": "KcsCaptureRegion2D.CaptureRegion2D"}, "reportedCount": 0, "actualCount": 0, "runtimeHandles": [], "elapsedMilliseconds": 0, "completed": False, "error": region_error, "warnings": warnings}
                _safe_log("CAPTURE_REGION_FAILED mode=" + region_mode + " error=" + region_error); _safe_log("OUTPUT_WRITE_START"); target = _write(report); _safe_log("RUN_SUCCESS"); return target
            _safe_log("CAPTURE_REGION_DONE mode=" + region_mode)
            # Python timing cannot interrupt a Vitesse native capture call if it blocks.
            started = time.time(); _safe_log("CAPTURE_CALL_START kind=" + selected + " api=" + api)
            try:
                result = capture_fn(region); capture_error = None
            except Exception, e:
                if _text(getattr(kcs_draft, "error", "")) == "kcs_NotFound":
                    _safe_log("CAPTURE_EMPTY kind=" + selected); result = [0]; capture_error = None
                else:
                    capture_error = _text(e); result = None; _safe_log("CAPTURE_CALL_FAILED kind=" + selected + " api=" + api + " error=" + capture_error)
            _safe_log("CAPTURE_CALL_DONE kind=" + selected + " api=" + api + " elapsedMilliseconds=" + _text(int((time.time() - started) * 1000)))
            warnings = []; _safe_log("RESULT_PARSE_START")
            reported, runtime_handles = _parse_result(result, warnings)
            _safe_log("RESULT_PARSE_DONE reportedCount=" + _text(reported) + " actualCount=" + _text(len(runtime_handles)))
            capture_region = {"type": "infinite", "source": "SetBoundaryInfinite", "api": "KcsCaptureRegion2D.CaptureRegion2D"}
            if region_mode == "CURRENT_VIEW_EXTENT": capture_region = {"type": "rectangle", "source": "current_view_extent", "inside": True, "cut": False, "api": "KcsCaptureRegion2D.CaptureRegion2D"}
            report = {"probeVersion": PROBE_VERSION, "createdAt": time.strftime("%Y-%m-%dT%H:%M:%S"), "readOnly": True, "selectedKind": selected, "captureApi": api, "regionMode": region_mode, "context": context, "drawingExtent": drawing_extent, "viewExtent": view_extent_json, "captureRegion": capture_region, "reportedCount": reported, "actualCount": len(runtime_handles), "runtimeHandles": runtime_handles, "elapsedMilliseconds": int((time.time() - started) * 1000), "completed": capture_error is None, "error": capture_error, "warnings": warnings}
            _safe_log("OUTPUT_WRITE_START"); target = _write(report); _safe_log("RUN_SUCCESS"); return target
        except Exception, e:
            _safe_log("RUN_FAILED exceptionType=" + _text(type(e)) + " exceptionText=" + _text(e) + " traceback=" + _text(traceback.format_exc()))
            return _text(e)
    finally:
        try:
            if _log_handle is not None: _log_handle.close()
        except Exception, e: pass
        _log_handle = None; _run_active = False
