"""wrangler dev で起動した Workers 版に、MCP の基本の呼び出しと正規化の表を送って確かめる。

ODPT には問い合わせない（CI からの外部アクセスで結果が揺れないように）。
wrangler dev は --var SELFTEST:1 を付けて起動しておくこと（正規化の確認用の口を開くため）。
使い方: python workers_smoke_test.py <ベース URL（例: http://127.0.0.1:8787）>
"""

import json
import pathlib
import sys
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
TOOL_DEFINITIONS = ROOT / "src/CycleSharingMcp.Core/ToolDefinitions.json"
NORMALIZATION_CASES = ROOT / "tests/CycleSharingMcp.Tests/Fixtures/normalization_cases.json"


def post(url: str, body: object) -> tuple[int, object | None]:
    request = urllib.request.Request(
        url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json"}, method="POST"
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.status, parse(response.read().decode())
    except urllib.error.HTTPError as error:
        return error.code, parse(error.read().decode())


def parse(text: str) -> object | None:
    # 404 などは JSON でない本文が返るので、読めなければ None にする
    try:
        return json.loads(text) if text else None
    except ValueError:
        return None


def wait_until_ready(base: str) -> None:
    for _ in range(120):
        try:
            with urllib.request.urlopen(base + "/", timeout=5):
                return
        except OSError:
            time.sleep(1)
    raise RuntimeError("Worker が起動しませんでした")


def main() -> int:
    base = sys.argv[1].rstrip("/")
    mcp = base + "/mcp"
    wait_until_ready(base)
    failures: list[str] = []

    _, initialize = post(mcp, {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18"}})
    server = initialize["result"]["serverInfo"]
    print(f"serverInfo: {server}")
    if server["name"] != "cycle-sharing-mcp":
        failures.append(f"serverInfo.name が違います: {server['name']}")

    status, _ = post(mcp, {"jsonrpc": "2.0", "method": "notifications/initialized"})
    if status != 202:
        failures.append(f"通知への応答が 202 ではありません: {status}")

    _, tools = post(mcp, {"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
    names = [t["name"] for t in tools["result"]["tools"]]
    print(f"tools: {names}")
    if tools["result"]["tools"] != json.loads(TOOL_DEFINITIONS.read_text(encoding="utf-8")):
        failures.append("tools/list が ToolDefinitions.json と一致しません")

    # 緯度と経度の取り違えは ODPT に問い合わせる前にエラーになる
    _, call = post(mcp, {
        "jsonrpc": "2.0", "id": 3, "method": "tools/call",
        "params": {"name": "search_ports_nearby", "arguments": {"latitude": 139.7, "longitude": 35.6}},
    })
    text = call["result"]["content"][0]["text"]
    print(f"tools/call（引数の誤り）: {text}")
    if not call["result"].get("isError") or "緯度" not in text:
        failures.append("緯度の誤りがツールのエラーになりません")

    cases = json.loads(NORMALIZATION_CASES.read_text(encoding="utf-8"))
    status, results = post(base + "/_selftest/normalize", [{"kind": c["kind"], "input": c["input"]} for c in cases])
    if status != 200:
        failures.append(f"正規化の確認用の口が使えません（{status}）。--var SELFTEST:1 を付けて起動してください")
    else:
        for case, result in zip(cases, results):
            ok = result == case["expected"]
            print(f"{'OK' if ok else 'NG'} {case['input']} -> {result}")
            if not ok:
                failures.append(f"正規化が違います: {case['input']} -> {result}（期待 {case['expected']}）")

    for failure in failures:
        print(failure, file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
