# Receives files POSTed by opfs-dump.js and writes them into the directory given as the only argument.
# Listens on 127.0.0.1 only. See "The browser database" in .claude/app-local.md.
#
#   python3 -I .claude/tools/opfs-receive.py <outDir>
import http.server, os, sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)


class H(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        name = os.path.basename(self.path.strip('/')) or 'blob'
        body = self.rfile.read(int(self.headers['Content-Length']))
        with open(os.path.join(OUT, name), 'wb') as f:
            f.write(body)
        self.send_response(204)
        self.send_header('Access-Control-Allow-Origin', '*')
        self.end_headers()
        print(name, len(body), flush=True)


http.server.HTTPServer(('127.0.0.1', 18999), H).serve_forever()
