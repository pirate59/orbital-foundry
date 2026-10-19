#!/usr/bin/env python3
"""Serve Build/Web locally so Unity can load its .unityweb assets.

Usage: python3 tools/serve_web.py [port] [build dir]   (default 8080, Build/Web)
e.g.   python3 tools/serve_web.py 8081 Build/Web-station
"""
import http.server
import os
import sys

PROJECT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
ROOT = os.path.join(PROJECT, sys.argv[2] if len(sys.argv) > 2 else os.path.join("Build", "Web"))
class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def end_headers(self):
        # Rebuilding overwrites Unity's fixed asset filenames. Prevent a browser from
        # mixing old wasm/data with a new loader when a local tester refreshes.
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    print("Serving", os.path.normpath(ROOT), "at http://localhost:%d" % port)
    http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
