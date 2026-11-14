#!/usr/bin/env bash
set -Eeuo pipefail
umask 022
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH

# Helper privilegiado instalado como root en /usr/local/sbin/trazo-deploy.
# Solo acepta una versión; todas las rutas y opciones son fijas.
[[ "$(id -u)" -eq 0 && "${SUDO_USER:-}" == "joso" ]] || {
  echo "ERROR: este helper solo puede invocarlo joso mediante sudo." >&2
  exit 1
}
[[ "$#" -eq 1 ]] || { echo "ERROR: uso: trazo-deploy VERSION" >&2; exit 2; }

APP_ROOT="/opt/trazo"
RELEASES="$APP_ROOT/releases"
CURRENT="$APP_ROOT/current"
WEB_ROOT="/var/www/trazo"
WEB_RELEASES="/var/www/trazo-releases"
STAGE="/tmp/trazo-backend"
WEB_STAGE="/tmp/trazo-frontend"
SERVICE="trazo.service"
SERVICE_USER="trazo"
ENV_FILE="/etc/trazo/trazo.env"
DP_DIR="/var/lib/trazo/data-protection-keys"
PUBLIC_HOST="trazostrength.duckdns.org"      # Host que acepta AllowedHosts
LOCAL_URL="http://127.0.0.1:5001"
KEEP_RELEASES=5
LOCK_FILE="/run/lock/trazo-deploy.lock"
VERSION="$1"

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ "$VERSION" =~ ^[A-Za-z0-9._-]{7,80}$ ]] || fail "identificador de versión no válido."
for s in "$STAGE" "$WEB_STAGE"; do
  [[ -d "$s" && ! -L "$s" ]] || fail "falta el staging $s o es un enlace."
  [[ "$(stat -c '%U' "$s")" == "joso" ]] || fail "el staging $s debe pertenecer a joso."
  [[ -z "$(find "$s" -type l -print -quit)" ]] || fail "el staging $s no puede contener enlaces simbólicos."
done
[[ -f "$STAGE/Trazo.Api.dll" ]] || fail "falta Trazo.Api.dll en la publicación."
[[ -f "$STAGE/Seeds/exercise-catalog-additions.json" ]] || fail "falta Seeds/exercise-catalog-additions.json en la publicación."
[[ -f "$WEB_STAGE/index.html" ]] || fail "falta index.html en el frontend compilado."
command -v flock >/dev/null
command -v curl >/dev/null
command -v rsync >/dev/null

# Prerrequisitos provisionados una sola vez (bootstrap-trazo-server.sh).
getent passwd "$SERVICE_USER" >/dev/null || fail "no existe el usuario de servicio $SERVICE_USER (ejecuta bootstrap-trazo-server.sh)."
getent passwd nginx >/dev/null || fail "no existe el usuario nginx."
[[ -f "$ENV_FILE" ]] || fail "falta $ENV_FILE."
[[ -d "$DP_DIR" ]] || fail "falta el directorio Data Protection $DP_DIR."
[[ -f /etc/systemd/system/$SERVICE ]] || fail "falta la unidad systemd $SERVICE (ejecuta bootstrap-trazo-server.sh)."
[[ ! -L "$RELEASES" && ! -L "$WEB_RELEASES" ]] || fail "los directorios de releases no pueden ser enlaces."
[[ ! -e "$WEB_ROOT" || -L "$WEB_ROOT" ]] || fail "$WEB_ROOT existe y no es un enlace simbólico; muévelo antes de desplegar."

install -d -m 0755 /run/lock
exec 9>"$LOCK_FILE"
flock -n 9 || fail "ya hay otro despliegue activo."

TARGET="$RELEASES/$VERSION"
WEB_TARGET="$WEB_RELEASES/$VERSION"
NEXT="$APP_ROOT/.current-$VERSION"
WEB_NEXT="/var/www/.trazo-$VERSION"
ACTIVATION_STARTED=0
SMOKE_DIR="$(mktemp -d /run/trazo-deploy-smoke.XXXXXX)"

cleanup() {
  local status=$?
  if (( status != 0 && ACTIVATION_STARTED == 0 )); then
    rm -rf -- "$TARGET" "$WEB_TARGET" 2>/dev/null || true
    rm -f -- "$NEXT" "$WEB_NEXT" 2>/dev/null || true
  fi
  rm -rf -- "$SMOKE_DIR" 2>/dev/null || true
  return "$status"
}
trap cleanup EXIT

PREVIOUS=""
PREVIOUS_WEB=""
if [[ -L "$CURRENT" ]]; then
  PREVIOUS="$(readlink -f "$CURRENT")"
  [[ "$PREVIOUS" == "$RELEASES/"* && -d "$PREVIOUS" ]] || fail "el enlace current apunta fuera de releases."
fi
if [[ -L "$WEB_ROOT" ]]; then
  PREVIOUS_WEB="$(readlink -f "$WEB_ROOT")"
  [[ "$PREVIOUS_WEB" == "$WEB_RELEASES/"* && -d "$PREVIOUS_WEB" ]] || PREVIOUS_WEB=""
fi
[[ ! -e "$TARGET" && ! -L "$TARGET" && ! -e "$WEB_TARGET" && ! -L "$WEB_TARGET" ]] || fail "la versión $VERSION ya existe; no se sobrescriben releases."

# El runner (joso) nunca puede escribir en /opt/trazo ni en /var/www/trazo*.
install -d -o root -g root -m 0755 "$APP_ROOT" "$RELEASES" "$WEB_RELEASES" "$TARGET" "$WEB_TARGET"
rsync -a --delete "$STAGE/" "$TARGET/"
rsync -a --delete "$WEB_STAGE/" "$WEB_TARGET/"
chown -R root:root "$TARGET" "$WEB_TARGET"
find "$TARGET" "$WEB_TARGET" -type d -exec chmod 0755 {} +
find "$TARGET" "$WEB_TARGET" -type f -exec chmod 0644 {} +
if command -v restorecon >/dev/null 2>&1; then restorecon -RF "$TARGET" "$WEB_TARGET" || true; fi
runuser -u "$SERVICE_USER" -- test -r "$TARGET/Trazo.Api.dll" || fail "$SERVICE_USER no puede leer la publicación."
runuser -u nginx -- test -x /var/www || fail "nginx no puede atravesar /var/www."
runuser -u nginx -- test -x "$WEB_RELEASES" || fail "nginx no puede atravesar $WEB_RELEASES."
runuser -u nginx -- test -r "$WEB_TARGET/index.html" || fail "nginx no puede leer el frontend publicado."

# Peticiones locales a Nginx: --resolve evita depender del DNS/NAT y -L sigue la
# redirección a HTTPS que certbot añade, con el certificado real del dominio.
web_curl() {
  curl --fail --silent --show-error --location --max-time 8 \
    --resolve "$PUBLIC_HOST:80:127.0.0.1" --resolve "$PUBLIC_HOST:443:127.0.0.1" "$@"
}

smoke_test() {
  local attempt
  for attempt in $(seq 1 12); do
    if systemctl is-active --quiet "$SERVICE" &&
       curl --fail --silent --show-error --max-time 5 \
         -H "Host: $PUBLIC_HOST" -H "X-Forwarded-Proto: https" \
         "$LOCAL_URL/health" -o "$SMOKE_DIR/health.json" 2>/dev/null &&
       grep -Fq "trazo-api" "$SMOKE_DIR/health.json" &&
       curl --fail --silent --show-error --max-time 5 \
         -H "Host: $PUBLIC_HOST" -H "X-Forwarded-Proto: https" \
         "$LOCAL_URL/health/ready" -o "$SMOKE_DIR/ready.json" 2>/dev/null &&
       grep -Fq "ready" "$SMOKE_DIR/ready.json" &&
       curl --fail --silent --show-error --max-time 5 \
         -H "Host: $PUBLIC_HOST" -H "X-Forwarded-Proto: https" \
         "$LOCAL_URL/api/v1/exercises?pageSize=1" -o "$SMOKE_DIR/exercises.json" 2>/dev/null &&
       grep -Fq "totalCount" "$SMOKE_DIR/exercises.json"; then
      break
    fi
    [[ "$attempt" -lt 12 ]] || return 1
    sleep 5
  done
  # Frontend servido por Nginx, fallback SPA en rutas profundas y proxy al API.
  web_curl "http://$PUBLIC_HOST/" -o "$SMOKE_DIR/index.html" 2>/dev/null || return 1
  grep -Fqi "<trazo-root" "$SMOKE_DIR/index.html" || return 1
  web_curl "http://$PUBLIC_HOST/setup" -o /dev/null 2>/dev/null || return 1
  web_curl "http://$PUBLIC_HOST/api/v1/setup/status" -o "$SMOKE_DIR/setup.json" 2>/dev/null || return 1
  grep -Fq "setupRequired" "$SMOKE_DIR/setup.json" || return 1
  return 0
}

rollback() {
  echo "ERROR: $1. Iniciando rollback de código (el esquema de BD NO se revierte)." >&2
  if [[ -n "$PREVIOUS" && -d "$PREVIOUS" ]]; then
    rm -f -- "$NEXT" "$WEB_NEXT"
    if ln -s "$PREVIOUS" "$NEXT" && mv -Tf "$NEXT" "$CURRENT" &&
       { [[ -z "$PREVIOUS_WEB" ]] || { ln -s "$PREVIOUS_WEB" "$WEB_NEXT" && mv -Tf "$WEB_NEXT" "$WEB_ROOT"; }; }; then
      systemctl restart "$SERVICE" || true
      if smoke_test; then
        echo "Rollback completado; vuelve a estar activa la versión anterior."
      else
        echo "CRITICAL: la versión anterior tampoco supera el smoke test; se detiene el servicio." >&2
        systemctl stop "$SERVICE" || true
      fi
    else
      echo "CRITICAL: no se pudo restaurar el enlace; se detiene el servicio." >&2
      systemctl stop "$SERVICE" || true
    fi
  else
    echo "CRITICAL: no hay versión anterior (primer despliegue). Se detiene el servicio." >&2
    systemctl stop "$SERVICE" || true
  fi
  journalctl -u "$SERVICE" -n 80 --no-pager || true
  exit 1
}

rm -f -- "$NEXT" "$WEB_NEXT"
ln -s "$TARGET" "$NEXT"
ln -s "$WEB_TARGET" "$WEB_NEXT"
ACTIVATION_STARTED=1
mv -Tf "$NEXT" "$CURRENT" || rollback "no se pudo activar el enlace current"
mv -Tf "$WEB_NEXT" "$WEB_ROOT" || rollback "no se pudo activar el enlace del frontend"

systemctl enable "$SERVICE" >/dev/null 2>&1 || true
systemctl restart "$SERVICE" || rollback "no se pudo reiniciar $SERVICE"
smoke_test || { systemctl status "$SERVICE" --no-pager || true; rollback "falló el smoke test (health, readiness, catálogo o frontend)"; }

# La versión anterior se conserva siempre.
prune() {
  local dir="$1" keep_path="$2" name path kept=0
  mapfile -t releases < <(find "$dir" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %f\n' | sort -nr | cut -d' ' -f2-)
  for name in "${releases[@]}"; do
    path="$dir/$name"
    [[ "$path" == "$keep_path" ]] && continue
    if (( kept < KEEP_RELEASES - 1 )); then ((kept+=1)); else rm -rf -- "$path"; fi
  done
}
prune "$RELEASES" "$TARGET"
prune "$WEB_RELEASES" "$WEB_TARGET"

echo "Despliegue correcto: $VERSION"
echo "Backend activo:  $(readlink -f "$CURRENT")"
echo "Frontend activo: $(readlink -f "$WEB_ROOT")"
