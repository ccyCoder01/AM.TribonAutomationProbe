import os
import re
import time
import kcs_draft

BRIDGE_DIR = r"C:\AM_TribonBridge"
INBOX_DIR = os.path.join(BRIDGE_DIR, "inbox")
PROCESSING_DIR = os.path.join(BRIDGE_DIR, "processing")
OUTPUT_DIR = os.path.join(BRIDGE_DIR, "output")
FAILED_DIR = os.path.join(BRIDGE_DIR, "failed")
ARCHIVE_DIR = os.path.join(BRIDGE_DIR, "archive")
LOG_DIR = os.path.join(BRIDGE_DIR, "logs")
LOG_FILE = os.path.join(LOG_DIR, "am_bridge_worker.log")

def ensure_dir(path):
    if not os.path.isdir(path):
        os.makedirs(path)

def ensure_bridge_dirs():
    ensure_dir(INBOX_DIR)
    ensure_dir(PROCESSING_DIR)
    ensure_dir(OUTPUT_DIR)
    ensure_dir(FAILED_DIR)
    ensure_dir(ARCHIVE_DIR)
    ensure_dir(LOG_DIR)

def utc_now():
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

def log(message):
    f = open(LOG_FILE, "a")
    try:
        f.write("%s %s\r\n" % (utc_now(), message))
    finally:
        f.close()

def to_utf8(value):
    if value == None:
        return ""

    try:
        if isinstance(value, unicode):
            return value.encode("utf-8")
    except:
        pass

    text = str(value)

    try:
        return text.decode("mbcs").encode("utf-8")
    except:
        return text

def json_escape(value):
    text = to_utf8(value)
    result = []

    for ch in text:
        code = ord(ch)

        if ch == '"':
            result.append('\\"')
        elif ch == '\\':
            result.append('\\\\')
        elif ch == '\b':
            result.append('\\b')
        elif ch == '\f':
            result.append('\\f')
        elif ch == '\n':
            result.append('\\n')
        elif ch == '\r':
            result.append('\\r')
        elif ch == '\t':
            result.append('\\t')
        elif code < 32:
            result.append('\\u%04x' % code)
        else:
            result.append(ch)

    return "".join(result)

def json_string(value):
    return '"' + json_escape(value) + '"'

def json_array(values):
    items = []
    for value in values:
        items.append(json_string(value))
    return "[" + ", ".join(items) + "]"

def json_unescape(value):
    result = []
    index = 0

    while index < len(value):
        ch = value[index]

        if ch != '\\':
            result.append(ch)
            index = index + 1
            continue

        index = index + 1

        if index >= len(value):
            result.append('\\')
            break

        esc = value[index]

        if esc == '"':
            result.append('"')
        elif esc == '\\':
            result.append('\\')
        elif esc == '/':
            result.append('/')
        elif esc == 'b':
            result.append('\b')
        elif esc == 'f':
            result.append('\f')
        elif esc == 'n':
            result.append('\n')
        elif esc == 'r':
            result.append('\r')
        elif esc == 't':
            result.append('\t')
        elif esc == 'u' and index + 4 < len(value):
            hex_value = value[index + 1:index + 5]
            try:
                result.append(unichr(int(hex_value, 16)).encode("utf-8"))
                index = index + 4
            except:
                result.append("\\u" + hex_value)
                index = index + 4
        else:
            result.append(esc)

        index = index + 1

    return "".join(result)

def extract_string(text, key):
    pattern = r'"' + re.escape(key) + r'"\s*:\s*"((?:\\.|[^"\\])*)"'
    match = re.search(pattern, text)

    if match == None:
        raise ValueError("Missing JSON string field: " + key)

    return json_unescape(match.group(1))

def read_file(path):
    f = open(path, "rb")
    try:
        return f.read()
    finally:
        f.close()

def write_atomic(path, content):
    temp_path = path + ".tmp"

    if os.path.exists(temp_path):
        os.remove(temp_path)

    f = open(temp_path, "wb")
    try:
        f.write(content)
        f.flush()
    finally:
        f.close()

    if os.path.exists(path):
        os.remove(path)

    os.rename(temp_path, path)

def move_replace(source, target):
    if os.path.exists(target):
        os.remove(target)
    os.rename(source, target)

def claim_one_request():
    files = []

    for name in os.listdir(INBOX_DIR):
        if name.endswith(".request.json"):
            files.append(name)

    files.sort()

    if len(files) == 0:
        return (None, None)

    name = files[0]
    source = os.path.join(INBOX_DIR, name)
    target = os.path.join(PROCESSING_DIR, name)

    move_replace(source, target)

    return (name, target)

def new_result_message_id():
    return "RES-%s-%s" % (
        time.strftime("%Y%m%d%H%M%S", time.gmtime()),
        os.getpid()
    )

def build_context_result(command_id, correlation_id, causation_id):
    warnings = []
    drawing_name = ""

    try:
        drawing_name = kcs_draft.dwg_name_get()
    except:
        drawing_name = ""
        warnings.append("No active drawing was returned by dwg_name_get")

    drawing_json = "null"

    if drawing_name != "":
        drawing_json = (
            "{"
            '"id": %s, '
            '"name": %s, '
            '"writable": false, '
            '"revision": ""'
            "}"
            % (
                json_string(drawing_name),
                json_string(drawing_name)
            )
        )

        warnings.append("Drawing id currently uses the drawing name")
        warnings.append("Drawing writable state is unavailable and is reported as false")
        warnings.append("Drawing revision is unavailable and is reported as empty")

    warnings.append("Database name is not yet available and is reported as empty")
    warnings.append("Active view is not yet available and is reported as null")

    result_json = (
        "{"
        '"sessionActive": true, '
        '"module": "Drafting", '
        '"database": {"name": ""}, '
        '"drawing": %s, '
        '"view": null'
        "}"
        % drawing_json
    )

    return (
        "{\r\n"
        '  "protocol": "AM.TribonBridge",\r\n'
        '  "version": "0.1",\r\n'
        '  "messageType": "bridge.result",\r\n'
        '  "messageId": %s,\r\n'
        '  "commandId": %s,\r\n'
        '  "correlationId": %s,\r\n'
        '  "causationId": %s,\r\n'
        '  "createdAt": %s,\r\n'
        '  "status": "succeeded",\r\n'
        '  "result": %s,\r\n'
        '  "warnings": %s,\r\n'
        '  "error": null\r\n'
        "}\r\n"
        % (
            json_string(new_result_message_id()),
            json_string(command_id),
            json_string(correlation_id),
            json_string(causation_id),
            json_string(utc_now()),
            result_json,
            json_array(warnings)
        )
    )

def build_failed_result(command_id, correlation_id, causation_id,
                        error_code, category, message):
    return (
        "{\r\n"
        '  "protocol": "AM.TribonBridge",\r\n'
        '  "version": "0.1",\r\n'
        '  "messageType": "bridge.result",\r\n'
        '  "messageId": %s,\r\n'
        '  "commandId": %s,\r\n'
        '  "correlationId": %s,\r\n'
        '  "causationId": %s,\r\n'
        '  "createdAt": %s,\r\n'
        '  "status": "failed",\r\n'
        '  "warnings": [],\r\n'
        '  "error": {\r\n'
        '    "code": %s,\r\n'
        '    "category": %s,\r\n'
        '    "message": %s,\r\n'
        '    "retryable": false\r\n'
        "  }\r\n"
        "}\r\n"
        % (
            json_string(new_result_message_id()),
            json_string(command_id),
            json_string(correlation_id),
            json_string(causation_id),
            json_string(utc_now()),
            json_string(error_code),
            json_string(category),
            json_string(message)
        )
    )

def process_request(name, processing_path):
    text = read_file(processing_path)

    message_id = extract_string(text, "messageId")
    command_id = extract_string(text, "commandId")
    correlation_id = extract_string(text, "correlationId")
    protocol = extract_string(text, "protocol")
    version = extract_string(text, "version")
    message_type = extract_string(text, "messageType")
    action = extract_string(text, "action")

    result_path = os.path.join(
        OUTPUT_DIR,
        command_id + ".result.json"
    )

    if protocol != "AM.TribonBridge":
        payload = build_failed_result(
            command_id,
            correlation_id,
            message_id,
            "INVALID_MESSAGE",
            "validation",
            "Invalid protocol"
        )
    elif version != "0.1":
        payload = build_failed_result(
            command_id,
            correlation_id,
            message_id,
            "UNSUPPORTED_PROTOCOL_VERSION",
            "validation",
            "Unsupported protocol version: " + version
        )
    elif message_type != "bridge.command":
        payload = build_failed_result(
            command_id,
            correlation_id,
            message_id,
            "INVALID_MESSAGE",
            "validation",
            "Invalid messageType"
        )
    elif action == "context.get":
        payload = build_context_result(
            command_id,
            correlation_id,
            message_id
        )
    else:
        payload = build_failed_result(
            command_id,
            correlation_id,
            message_id,
            "UNSUPPORTED_ACTION",
            "validation",
            "Unsupported action: " + action
        )

    write_atomic(result_path, payload)

    archive_path = os.path.join(ARCHIVE_DIR, name)
    move_replace(processing_path, archive_path)

    log(
        "Processed commandId=%s action=%s result=%s"
        % (command_id, action, result_path)
    )

def run(*args):
    ensure_bridge_dirs()

    name = None
    processing_path = None

    try:
        (name, processing_path) = claim_one_request()

        if name == None:
            log("No request found")
            return

        process_request(name, processing_path)

    except Exception, e:
        log("Worker failure: " + str(e))

        if name != None and processing_path != None:
            try:
                failed_path = os.path.join(FAILED_DIR, name)
                move_replace(processing_path, failed_path)
            except:
                pass
