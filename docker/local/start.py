"""Start the local lab and open a normal authenticated browser session."""
import argparse
import http.server
import pathlib
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import webbrowser
from initialize import COMPOSE, TOOLS, provision, tools_base

BASE = tools_base()

def open_browser():
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args): return None
    token = (TOOLS / 'web-token').read_text().strip()
    try:
        urllib.request.build_opener(NoRedirect).open(BASE + '/login', urllib.parse.urlencode({'token': token}).encode(), timeout=15)
        raise RuntimeError('Expected an authenticated session redirect')
    except urllib.error.HTTPError as response:
        if response.code != 302: raise RuntimeError('Local web login failed') from None
        cookies = response.headers.get_all('Set-Cookie', [])
    del token
    if not any(c.startswith('IndexToolsSession=') for c in cookies):
        raise RuntimeError('Local session cookie was not issued')
    class Handoff(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_): pass
        def do_GET(self):
            if self.path != '/':
                self.send_response(404); self.end_headers(); return
            self.send_response(302)
            for cookie in cookies: self.send_header('Set-Cookie', cookie)
            self.send_header('Location', BASE + '/operations')
            self.send_header('Cache-Control', 'no-store')
            self.send_header('Content-Length', '0')
            self.end_headers()
            self.server.completed = True
    # Ephemeral loopback handoff; no token/cookie in browser URL, file, or output.
    with http.server.HTTPServer(('127.0.0.1', 0), Handoff) as server:
        server.timeout = 1
        server.completed = False
        webbrowser.open('http://127.0.0.1:' + str(server.server_port) + '/')
        deadline = time.monotonic() + 45
        while not server.completed and time.monotonic() < deadline: server.handle_request()
        if not server.completed: raise RuntimeError('Browser handoff timed out')

def start(no_build=False, no_browser=False):
    provision()
    command = COMPOSE + ['--profile', 'tools', 'up', '-d', '--no-deps', '--wait']
    if not no_build: command.append('--build')
    subprocess.run(command + ['tools'], check=True, stdout=subprocess.DEVNULL)
    if not no_browser: open_browser()
    print('LOCAL_OPERATOR_WORKSPACE_READY ' + BASE + '/operations')

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--no-build', action='store_true')
    parser.add_argument('--no-browser', action='store_true')
    args = parser.parse_args()
    try:
        start(args.no_build, args.no_browser)
    except Exception as error:
        print('LOCAL_START_FAILED: ' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
