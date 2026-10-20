"""Keep a DotNetLab MCP connection alive until a submitted operation finishes."""
import argparse
import json
import pathlib
import subprocess
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--host", required=True, help="Installed Tedd.DotNetLab.Host.dll")
parser.add_argument("--workspace", required=True, help="Initialized task-owned DotNetLab workspace")
parser.add_argument("--output", help="JSON result filename relative to the workspace")
parser.add_argument("tool", help="For example lab.experiment, lab.run, or lab.inspect")
parser.add_argument("request", help="Raw JSON request, or @path to a JSON request file")
args = parser.parse_args()
workspace = pathlib.Path(args.workspace).resolve()
storage_root = pathlib.Path(r"D:\Workspaces\AI").resolve()
if workspace == storage_root or not workspace.is_relative_to(storage_root):
    parser.error("Use a dedicated workspace beneath D:\\Workspaces\\AI.")
output = (workspace / args.output).resolve() if args.output else None
if output is not None and not output.is_relative_to(workspace):
    parser.error("Output must remain inside the supplied workspace.")
request = json.loads(pathlib.Path(args.request[1:]).read_text(encoding="utf-8-sig")
                     if args.request.startswith("@") else args.request)
results = []

with (workspace / "connector.stderr.log").open("a", encoding="utf-8") as errors:
    process = subprocess.Popen(["dotnet", str(pathlib.Path(args.host).resolve()), "connect", str(workspace)],
                               cwd=workspace, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=errors, text=True, encoding="utf-8")
    sequence = 0

    def rpc(method, params):
        global sequence
        sequence += 1
        process.stdin.write(json.dumps(dict(jsonrpc="2.0", id=sequence, method=method, params=params)) + "\n")
        process.stdin.flush()
        for line in process.stdout:
            message = json.loads(line)
            if message.get("id") == sequence:
                if "error" in message:
                    raise RuntimeError(message["error"])
                return message["result"]
        raise RuntimeError("DotNetLab MCP disconnected; inspect the accepted operation before retrying.")

    def call(tool, payload):
        result = rpc("tools/call", dict(name=tool, arguments=dict(request=payload)))
        if "structuredContent" in result:
            return result["structuredContent"]
        text = next(item["text"] for item in result["content"] if item["type"] == "text")
        try:
            return json.loads(text)
        except ValueError:
            raise RuntimeError(text) from None

    def retain(result):
        results.append(result)
        print(json.dumps(result), flush=True)
        if output is not None:
            output.write_text(json.dumps(results, indent=2), encoding="utf-8")

    try:
        rpc("initialize", dict(protocolVersion="2024-11-05", capabilities={},
                               clientInfo=dict(name="fastnoise-performance", version="1")))
        process.stdin.write(json.dumps(dict(jsonrpc="2.0", method="notifications/initialized")) + "\n")
        process.stdin.flush()
        result = call(args.tool, request)
        retain(result)
        if result.get("status") != "ok":
            raise RuntimeError("DotNetLab rejected the request; inspect the retained result before retrying.")
        data = result.get("data") or {}
        if args.tool == "lab.run" and isinstance(data, dict) and data.get("id", "").startswith("op-"):
            operation = data["id"]
            while result["data"]["state"] in ("queued", "running", "accepted", "draining"):
                time.sleep(2)
                result = call("lab.run", dict(action="status", operation_id=operation))
                if result.get("status") != "ok":
                    retain(result)
                    raise RuntimeError("Cannot verify operation state; inspect its original ID before retrying.")
            retain(result)
            if result["data"]["state"] != "completed":
                raise RuntimeError("Operation did not complete successfully; inspect its original ID before retrying.")
            if result["data"].get("result_id"):
                retain(call("lab.inspect", dict(action="record", id=result["data"]["result_id"])))
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait()
