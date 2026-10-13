#!/usr/bin/env python3
"""Serve Build/Web locally with the same headers deploy/_headers gives Cloudflare Pages.

Usage: python3 tools/serve_web.py [port]   (default 8080)
"""
import http.server
import os
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Build", "Web")
TYPES = {".wasm.br": "application/wasm", ".js.br": "application/javascript", ".data.br": "application/octet-stream"}


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def guess_type(self, path):
        for suffix, mime in TYPES.items():
            if path.endswith(suffix):
                return mime
        return super().guess_type(path)

    def end_headers(self):
        path = self.path.split("?", 1)[0]
        if path.endswith(".br"):
            self.send_header("Content-Encoding", "br")
        if path in ("/", "/index.html"):
            self.send_header("Cache-Control", "no-cache")
        super().end_headers()


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    print("Serving", os.path.normpath(ROOT), "at http://localhost:%d" % port)
    http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
