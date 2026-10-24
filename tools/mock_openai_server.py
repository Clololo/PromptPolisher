#!/usr/bin/env python3
"""本地 OpenAI 兼容 Mock 服务，用于验收（不需要真实 API Key）。

用法:
    python tools/mock_openai_server.py            # 127.0.0.1:8765
    python tools/mock_openai_server.py 9000

应用设置:
    Base URL: http://127.0.0.1:8765/v1
    Model:    mock
    API Key:  任意非空字符串（如 test）

特殊 model:
    slow   -> 延迟 10 秒（测试超时）
    empty  -> 返回空内容
    error  -> 返回 500
API Key:
    bad    -> 返回 401
    rate   -> 返回 429
"""
import json
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def do_POST(self):
        if not self.path.endswith("/chat/completions"):
            self.send_error(404)
            return

        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            payload = json.loads(raw or b"{}")
        except json.JSONDecodeError:
            self.send_error(400)
            return

        auth = self.headers.get("Authorization", "")
        if auth.endswith("bad"):
            self._json(401, {"error": {"message": "invalid api key"}})
            return
        if auth.endswith("rate"):
            self._json(429, {"error": {"message": "rate limited"}})
            return

        model = payload.get("model", "")
        if model == "error":
            self._json(500, {"error": {"message": "mock server error"}})
            return
        if model == "slow":
            time.sleep(10)

        system = ""
        for message in payload.get("messages", []):
            if message.get("role") == "system":
                system = message.get("content", "")
                break

        content = "" if model == "empty" else "【MOCK 润色结果】\n\n" + system

        if payload.get("stream"):
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream; charset=utf-8")
            self.end_headers()
            for index in range(0, len(content), 24):
                chunk = content[index:index + 24]
                data = json.dumps(
                    {"choices": [{"delta": {"content": chunk}}]},
                    ensure_ascii=False,
                )
                self.wfile.write(f"data: {data}\n\n".encode("utf-8"))
                self.wfile.flush()
                time.sleep(0.02)
            self.wfile.write(b"data: [DONE]\n\n")
            self.wfile.flush()
            return

        self._json(200, {"choices": [{"message": {"content": content}}]})

    def _json(self, status, obj):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
    print(f"Mock OpenAI server: http://127.0.0.1:{port}/v1")
    print("Model: mock / slow / empty / error; Key: test / bad / rate")
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
