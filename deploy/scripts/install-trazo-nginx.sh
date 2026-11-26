#!/usr/bin/env bash
set -Eeuo pipefail

# Instala o actualiza el sitio nginx de TRAZO (idempotente). Ejecutar como root desde la raíz del repo:
#   sudo bash deploy/scripts/install-trazo-nginx.sh
# Conserva las líneas TLS que certbot escribió en trazo.conf, guarda una copia de lo anterior y la restaura si nginx
# rechaza la configuración o alguna ruta pública deja de responder como debe.
[[ "$(id -u)" -eq 0 ]] || { echo "ERROR: ejecutar como root." >&2; exit 1; }
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NGINX_DIR="${NGINX_DIR:-/etc/nginx}"
BACKUP_ROOT="${BACKUP_ROOT:-/var/backups/trazo-nginx}"
HOST="trazostrength.duckdns.org"
CONF="$NGINX_DIR/conf.d/trazo.conf"
SNIPPETS=(trazo-site.conf trazo-security-headers.conf)
for f in "${SNIPPETS[@]}" trazo.conf; do
  [[ -f "$REPO/deploy/nginx/$f" ]] || { echo "ERROR: falta deploy/nginx/$f; ejecuta desde un clon del repositorio." >&2; exit 1; }
done

# Un archivo con otros dominios no es uno que este script sepa regenerar.
if [[ -f "$CONF" ]] && grep -E '^[[:space:]]*server_name[[:space:]]' "$CONF" | grep -vqE "server_name[[:space:]]+${HOST//./\\.};"; then
  echo "ERROR: $CONF declara otros server_name; revísalo a mano." >&2
  exit 1
fi

BACKUP="$BACKUP_ROOT/$(date +%Y%m%d-%H%M%S)-$$"
mkdir -p "$BACKUP" "$NGINX_DIR/snippets" "$NGINX_DIR/conf.d"
if [[ -f "$CONF" ]]; then cp -a "$CONF" "$BACKUP/trazo.conf"; fi
for f in "${SNIPPETS[@]}"; do
  if [[ -f "$NGINX_DIR/snippets/$f" ]]; then cp -a "$NGINX_DIR/snippets/$f" "$BACKUP/$f"; fi
done

reload_nginx() { systemctl reload nginx; }
restore() {
  trap - ERR
  echo ">> Restaurando la configuración anterior desde $BACKUP" >&2
  if [[ -f "$BACKUP/trazo.conf" ]]; then cp -a "$BACKUP/trazo.conf" "$CONF"; else rm -f "$CONF"; fi
  for f in "${SNIPPETS[@]}"; do
    if [[ -f "$BACKUP/$f" ]]; then cp -a "$BACKUP/$f" "$NGINX_DIR/snippets/$f"; else rm -f "$NGINX_DIR/snippets/$f"; fi
  done
  nginx -t && reload_nginx
}

tls=""
if [[ -f "$CONF" ]]; then
  tls="$(grep -E '^[[:space:]]*(listen[[:space:]].*443|ssl_certificate(_key)?[[:space:]]|ssl_dhparam[[:space:]]|include[[:space:]]+/etc/letsencrypt/)' "$CONF" || true)"
fi

trap 'echo "ERROR: la instalación ha fallado." >&2; restore; exit 1' ERR
for f in "${SNIPPETS[@]}"; do
  install -m 0644 "$REPO/deploy/nginx/$f" "$NGINX_DIR/snippets/$f"
done
if [[ -n "$tls" ]]; then
  cat > "$CONF" <<EOF
# /etc/nginx/conf.d/trazo.conf
# Generado por deploy/scripts/install-trazo-nginx.sh. El sitio está en snippets/trazo-site.conf.

server {
    server_name $HOST;

    include snippets/trazo-site.conf;

$tls
}

server {
    if (\$host = $HOST) {
        return 301 https://\$host\$request_uri;
    } # managed by Certbot

    listen 80;
    listen [::]:80;
    server_name $HOST;
    return 404; # managed by Certbot
}
EOF
else
  install -m 0644 "$REPO/deploy/nginx/trazo.conf" "$CONF"
fi
chmod 0644 "$CONF"
if command -v restorecon >/dev/null 2>&1; then restorecon -R "$NGINX_DIR/snippets" "$CONF"; fi

nginx -t
reload_nginx
trap - ERR

# Sin la API en marcha (primer arranque) no hay nada que comprobar todavía.
if curl --silent --fail --max-time 5 -o /dev/null -H "Host: $HOST" -H "X-Forwarded-Proto: https" http://127.0.0.1:5001/health; then
  sleep 2
  if [[ -n "$tls" ]]; then
    check=("https://$HOST" --resolve "$HOST:443:127.0.0.1")
  else
    check=("http://$HOST" --resolve "$HOST:80:127.0.0.1")
  fi
  # CHECK_CURL_OPTS solo lo usa la prueba (certificado autofirmado).
  read -r -a extra <<< "${CHECK_CURL_OPTS:-}"
  if ! bash "$REPO/deploy/scripts/check-trazo-public-routes.sh" "${check[@]}" "${extra[@]}"; then
    restore
    exit 1
  fi
else
  echo ">> La API no responde en 127.0.0.1:5001: no se han comprobado las rutas."
fi
echo ">> Sitio nginx de TRAZO instalado. Copia anterior en $BACKUP"
