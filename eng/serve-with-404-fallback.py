#!/usr/bin/env python3
"""Static file server that mimics GitHub Pages' per-directory 404.html SPA fallback.

Plain `python3 -m http.server` returns a bare 404 for any path with no matching file --
GitHub Pages instead serves the nearest ancestor directory's `404.html` (falling back to the
site root's) so a deep-link into a client-routed app still boots the app instead of a dead end.
This is the one behavior the demo's hard-refresh-deep-link smoke test needs and the stdlib
handler doesn't provide; used only by the CI smoke test (eng/assemble-demo.sh's own output is
otherwise served as-is).

Usage: serve-with-404-fallback.py <port> <root-dir>
"""

import http.server
import os
import sys


class FallbackHandler(http.server.SimpleHTTPRequestHandler):
    def send_head(self):
        path = self.translate_path(self.path)

        if os.path.exists(path) and not os.path.isdir(path):
            return super().send_head()

        if os.path.isdir(path) and os.path.exists(os.path.join(path, "index.html")):
            return super().send_head()

        # Walk from the requested directory up to the served root looking for a 404.html,
        # exactly as GitHub Pages does for an unmatched path.
        candidate_dir = path if os.path.isdir(path) else os.path.dirname(path)
        root = os.path.abspath(self.directory)

        while True:
            fallback = os.path.join(candidate_dir, "404.html")
            if os.path.exists(fallback):
                with open(fallback, "rb") as f:
                    body = f.read()
                self.send_response(404)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                return _BytesIO(body)

            if os.path.abspath(candidate_dir) == root:
                break
            candidate_dir = os.path.dirname(candidate_dir)

        return super().send_head()


class _BytesIO:
    """Minimal file-like wrapper so send_head's caller can copyfile() the body we already read."""

    def __init__(self, data: bytes) -> None:
        import io

        self._buf = io.BytesIO(data)

    def read(self, *args):
        return self._buf.read(*args)

    def close(self):
        self._buf.close()


if __name__ == "__main__":
    port = int(sys.argv[1])
    root_dir = sys.argv[2]
    handler = lambda *args, **kwargs: FallbackHandler(*args, directory=root_dir, **kwargs)
    with http.server.ThreadingHTTPServer(("127.0.0.1", port), handler) as httpd:
        httpd.serve_forever()
