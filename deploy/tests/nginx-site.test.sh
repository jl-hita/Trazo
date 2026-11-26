#!/usr/bin/env bash
# Prueba el instalador y las rutas con un nginx real. SOLO para máquinas desechables (CI): escribe en /etc/nginx,
# /etc/letsencrypt y /var/www. Ejecutar como root con CI=true.
set -Eeuo pipefail
[[ "${CI:-}" == true ]] || { echo "ERROR: pensado para CI; modifica el nginx de la máquina." >&2; exit 1; }
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HOST=trazostrength.duckdns.org
TLS_DIR="/etc/letsencrypt/live/$HOST"
trap 'echo "FALLO en la línea $LINENO: $BASH_COMMAND" >&2' ERR

mkdir -p "$TLS_DIR" /var/www/trazo
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=$HOST" -keyout "$TLS_DIR/privkey.pem" -out "$TLS_DIR/fullchain.pem" 2>/dev/null
openssl dhparam -dsaparam -out /etc/letsencrypt/ssl-dhparams.pem 2048 2>/dev/null
echo "ssl_session_timeout 1d;" > /etc/letsencrypt/options-ssl-nginx.conf
echo '<!doctype html><trazo-root></trazo-root>' > /var/www/trazo/index.html
rm -f /etc/nginx/sites-enabled/default
# Un servidor ya desplegado y certificado, sin las rutas públicas.
cp "$REPO/deploy/tests/trazo.certbot.conf" /etc/nginx/conf.d/trazo.conf

# API simulada: contesta a todo con un cuerpo reconocible.
python3 - <<'PY' &
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
class Api(BaseHTTPRequestHandler):
    def do_GET(self):
        body = b'api ' + self.path.encode()
        self.send_response(200)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)
    def do_POST(self):
        left = int(self.headers.get('Content-Length', 0))
        while left > 0:
            chunk = self.rfile.read(min(left, 65536))
            if not chunk:
                break
            left -= len(chunk)
        self.do_GET()
    def log_message(self, *args):
        pass
ThreadingHTTPServer(('127.0.0.1', 5001), Api).serve_forever()
PY
API=$!
trap 'kill $API' EXIT
nginx -t
systemctl restart nginx
sleep 2

check() { bash "$REPO/deploy/scripts/check-trazo-public-routes.sh" "https://$HOST" --insecure --resolve "$HOST:443:127.0.0.1"; }
if check; then echo "ERROR: la comprobación debía fallar con la configuración antigua."; exit 1; fi

export CHECK_CURL_OPTS=--insecure
bash "$REPO/deploy/scripts/install-trazo-nginx.sh"
cp /etc/nginx/conf.d/trazo.conf /tmp/first.conf
# Repetirlo no cambia nada.
bash "$REPO/deploy/scripts/install-trazo-nginx.sh"
diff /tmp/first.conf /etc/nginx/conf.d/trazo.conf
grep -q "ssl_certificate_key $TLS_DIR/privkey.pem" /etc/nginx/conf.d/trazo.conf
check

status() { curl -sk --resolve "$HOST:443:127.0.0.1" --resolve "$HOST:80:127.0.0.1" -o /dev/null -w '%{http_code}' "$@"; }
head -c 3000000 /dev/zero > /tmp/3m
expect() { local want="$1"; shift; local got; got="$(status "$@")"; [[ "$got" == "$want" ]] || { echo "ERROR: $got en vez de $want: $*"; exit 1; }; }
expect 413 --data-binary @/tmp/3m "https://$HOST/api/v1/anything"
expect 200 --data-binary @/tmp/3m "https://$HOST/api/internal/diagnostics/mappings"
expect 200 --data-binary @/tmp/3m "https://$HOST/api/internal/exercise-catalog/import"
expect 301 "http://$HOST/"
# Las respuestas de la API no llevan las cabeceras de la web (su CSP mandaría junto a la de la web).
if curl -sk -D - -o /dev/null --resolve "$HOST:443:127.0.0.1" "https://$HOST/invite/x" | grep -qi '^content-security-policy:'; then
  echo "ERROR: /invite/ hereda las cabeceras de la web."; exit 1
fi

# Una configuración que nginx rechaza no se queda instalada.
BROKEN="$(mktemp -d)"
cp -a "$REPO/deploy" "$BROKEN/"
echo "esto_no_existe on;" >> "$BROKEN/deploy/nginx/trazo-site.conf"
if bash "$BROKEN/deploy/scripts/install-trazo-nginx.sh"; then echo "ERROR: debía fallar."; exit 1; fi
nginx -t
cmp "$REPO/deploy/nginx/trazo-site.conf" /etc/nginx/snippets/trazo-site.conf
check
echo "OK nginx"
