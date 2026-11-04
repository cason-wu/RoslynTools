"""End-to-end smoke test for roslyn-mcp over stdio.

Starts the MCP server, performs the initialize handshake, then runs the steps in a JSON scenario file.

Usage (from the repository root):
    dotnet build RoslynMcp
    uv run --no-project python -I scripts/mcp_e2e.py scripts/mcp_e2e.sample.json
    uv run --no-project python -I scripts/mcp_e2e.py my-steps.json --work <dir> --server <path to roslyn-mcp.exe>

Scenario file: a JSON array of steps.
    {"name": "<tool>", "args": {...}, "limit": 600}   call a tool; print up to `limit` characters of the text result
    {"action": "write", "path": "<relative to --work>", "content": "..."}   write a file (to test change sync)
    {"action": "tools", "names": ["<tool>", ...]}     print the tools/list entries (schema) for these tools
    {"action": "instructions"}                         print the server instructions from initialize

String argument values may use {repo} (repository root) and {work} (the --work directory).
"""

import argparse
import json
import subprocess
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DEFAULT_SERVER = REPO / "RoslynMcp" / "bin" / "Debug" / "net10.0" / "win-x64" / "roslyn-mcp.exe"


def main() -> None:
    parser = argparse.ArgumentParser(description="Run an MCP scenario against roslyn-mcp over stdio.")
    parser.add_argument("scenario", type=Path, help="JSON scenario file")
    parser.add_argument("--server", type=Path, default=DEFAULT_SERVER, help="Path to roslyn-mcp executable")
    parser.add_argument("--work", type=Path, default=REPO / "TestResults" / "mcp-e2e", help="Scratch directory for {work} and write steps")
    args = parser.parse_args()

    steps = json.loads(args.scenario.read_text(encoding="utf-8"))
    work = args.work.resolve()
    work.mkdir(parents=True, exist_ok=True)

    client = McpClient(args.server.resolve())
    try:
        init = client.request("initialize", {
            "protocolVersion": "2025-06-18",
            "capabilities": {},
            "clientInfo": {"name": "roslyn-mcp-e2e", "version": "1"},
        })
        client.notify("notifications/initialized")
        run_steps(client, init, steps, work)
    finally:
        client.close()


def run_steps(client: "McpClient", init: dict, steps: list, work: Path) -> None:
    for step in steps:
        action = step.get("action")
        if action == "write":
            path = work / step["path"]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(step["content"], encoding="utf-8")
            time.sleep(0.05)
            print(f"### wrote {path}")
            continue

        if action == "tools":
            tools = client.request("tools/list", {})["result"]["tools"]
            for tool in tools:
                if tool["name"] in step.get("names", []):
                    print(json.dumps(tool, indent=1))
            continue

        if action == "instructions":
            print(init.get("result", {}).get("instructions", ""))
            continue

        arguments = {key: expand(value, work) for key, value in step.get("args", {}).items()}
        started = time.time()
        reply = client.request("tools/call", {"name": step["name"], "arguments": arguments})
        elapsed = time.time() - started

        result = reply.get("result")
        if result is None:
            print(f"### {step['name']} protocol error ({elapsed:.1f}s)")
            print(json.dumps(reply.get("error")))
            continue

        text = "".join(item.get("text", "") for item in result.get("content", []))
        print(f"### {step['name']} {json.dumps(arguments)} isError={bool(result.get('isError'))} ({elapsed:.1f}s)")
        print(text[: step.get("limit", 600)])


def expand(value, work: Path):
    if isinstance(value, str):
        return value.replace("{repo}", REPO.as_posix()).replace("{work}", work.as_posix())
    return value


class McpClient:
    def __init__(self, server: Path) -> None:
        self._process = subprocess.Popen(
            [str(server)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
        )
        self._next_id = 0

    def request(self, method: str, params: dict) -> dict:
        self._next_id += 1
        self._send({"jsonrpc": "2.0", "id": self._next_id, "method": method, "params": params})
        while True:
            line = self._process.stdout.readline()
            if not line:
                raise RuntimeError("Server closed the stream: " + self._process.stderr.read())
            message = json.loads(line)
            if message.get("id") == self._next_id:
                return message

    def notify(self, method: str) -> None:
        self._send({"jsonrpc": "2.0", "method": method})

    def close(self) -> None:
        if self._process.stdin:
            self._process.stdin.close()
        try:
            self._process.wait(timeout=30)
        except subprocess.TimeoutExpired:
            self._process.kill()

    def _send(self, message: dict) -> None:
        self._process.stdin.write(json.dumps(message) + "\n")
        self._process.stdin.flush()


if __name__ == "__main__":
    main()
