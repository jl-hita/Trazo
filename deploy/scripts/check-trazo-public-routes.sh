#!/usr/bin/env bash
# Comprueba que nginx entrega cada ruta pública a quien le toca. Una "location" que falta no da error: la web Angular
# responde 200 en su lugar, así que hay que mirar el contenido.
#
#   bash deploy/scripts/check-trazo-public-routes.sh https://trazostrength.duckdns.org [opciones de curl...]
set -uo pipefail
BASE="${1:?uso: $0 <url base> [opciones de curl]}"
shift
CURL_OPTS=("$@")
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
failed=0

# Deja cuerpo y cabeceras en $TMP e imprime el código HTTP.
fetch() {
  curl --silent --show-error --max-time 10 -D "$TMP/headers" -o "$TMP/body" -w '%{http_code}' "${CURL_OPTS[@]}" "$BASE$1"
}
fail() { echo "FALLA $1: $2"; failed=1; }

for path in /account/delete /invite/route-check /c/route-check /.well-known/assetlinks.json; do
  code="$(fetch "$path")" || { fail "$path" "sin respuesta"; continue; }
  if grep -q '<trazo-root' "$TMP/body"; then
    fail "$path" "responde la web Angular, no la API"
  elif [[ "$code" =~ ^(000|5..)$ ]]; then
    fail "$path" "HTTP $code"
  else
    echo "ok $path (HTTP $code)"
    if [[ "$path" == /.well-known/* && "$code" == 404 ]]; then
      echo "AVISO: assetlinks.json sin configurar (AppLinks__Sha256Fingerprints): los App Links no se verifican."
    fi
  fi
done

code="$(fetch /)" || code=000
if [[ "$code" == 200 ]] && grep -q '<trazo-root' "$TMP/body" && grep -qi '^content-security-policy:' "$TMP/headers"; then
  echo "ok / (web con cabeceras de seguridad)"
else
  fail / "la web no responde o le faltan las cabeceras de seguridad (HTTP $code)"
fi
exit "$failed"
