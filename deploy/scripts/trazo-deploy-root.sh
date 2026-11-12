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
STAGE="/tmp/trazo-backend"
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
[[ -d "$STAGE" && ! -L "$STAGE" && -f "$STAGE/Trazo.Api.dll" ]] || fail "falta el backend publicado o el staging no es válido."
[[ "$(stat -c '%U' "$STAGE")" == "joso" ]] || fail "el staging debe pertenecer a joso."
[[ -z "$(find "$STAGE" -type l -print -quit)" ]] || fail "el staging no puede contener enlaces simbólicos."
[[ -f "$STAGE/Seeds/exercise-catalog-additions.json" ]] || fail "falta Seeds/exercise-catalog-additions.json en la publicación."
command -v flock >/dev/null
command -v curl >/dev/null
command -v rsync >/dev/null

# Prerrequisitos provisionados una sola vez (bootstrap-trazo-server.sh).
getent passwd "$SERVICE_USER" >/dev/null || fail "no existe el usuario de servicio $SERVICE_USER (ejecuta bootstrap-trazo-server.sh)."
[[ -f "$ENV_FILE" ]] || fail "falta $ENV_FILE."
[[ -d "$DP_DIR" ]] || fail "falta el directorio Data Protection $DP_DIR."
[[ -f /etc/systemd/system/$SERVICE ]] || fail "falta la unidad systemd $SERVICE (ejecuta bootstrap-trazo-server.sh)."

install -d -m 0755 /run/lock
exec 9>"$LOCK_FILE"
flock -n 9 || fail "ya hay otro despliegue activo."

TARGET="$RELEASES/$VERSION"
NEXT="$APP_ROOT/.current-$VERSION"
ACTIVATION_STARTED=0
SMOKE_DIR="$(mktemp -d /run/trazo-deploy-smoke.XXXXXX)"

cleanup() {
  local status=$?
  if (( status != 0 && ACTIVATION_STARTED == 0 )); then
    rm -rf -- "$TARGET" 2>/dev/null || true
    rm -f -- "$NEXT" 2>/dev/null || true
  fi
  rm -rf -- "$SMOKE_DIR" 2>/dev/null || true
  return "$status"
}
trap cleanup EXIT

PREVIOUS=""
if [[ -L "$CURRENT" ]]; then
  PREVIOUS="$(readlink -f "$CURRENT")"
  [[ "$PREVIOUS" == "$RELEASES/"* && -d "$PREVIOUS" ]] || fail "el enlace current apunta fuera de releases."
fi
[[ ! -e "$TARGET" && ! -L "$TARGET" ]] || fail "la versión $VERSION ya existe; no se sobrescriben releases."

# El runner (joso) nunca puede escribir en /opt/trazo.
install -d -o root -g root -m 0755 "$APP_ROOT" "$RELEASES" "$TARGET"
rsync -a --delete "$STAGE/" "$TARGET/"
chown -R root:root "$TARGET"
find "$TARGET" -type d -exec chmod 0755 {} +
find "$TARGET" -type f -exec chmod 0644 {} +
if command -v restorecon >/dev/null 2>&1; then restorecon -RF "$TARGET" || true; fi
runuser -u "$SERVICE_USER" -- test -r "$TARGET/Trazo.Api.dll" || fail "$SERVICE_USER no puede leer la publicación."

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
      return 0
    fi
    sleep 5
  done
  return 1
}

rollback() {
  echo "ERROR: $1. Iniciando rollback de código (el esquema de BD NO se revierte)." >&2
  if [[ -n "$PREVIOUS" && -d "$PREVIOUS" ]]; then
    rm -f -- "$NEXT"
    if ln -s "$PREVIOUS" "$NEXT" && mv -Tf "$NEXT" "$CURRENT"; then
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

rm -f -- "$NEXT"
ln -s "$TARGET" "$NEXT"
ACTIVATION_STARTED=1
mv -Tf "$NEXT" "$CURRENT" || rollback "no se pudo activar el enlace current"

systemctl enable "$SERVICE" >/dev/null 2>&1 || true
systemctl restart "$SERVICE" || rollback "no se pudo reiniciar $SERVICE"
smoke_test || { systemctl status "$SERVICE" --no-pager || true; rollback "falló el smoke test (health, readiness o catálogo)"; }

# La versión anterior se conserva siempre.
mapfile -t releases < <(find "$RELEASES" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %f\n' | sort -nr | cut -d' ' -f2-)
kept=0
for name in "${releases[@]}"; do
  path="$RELEASES/$name"
  [[ "$path" == "$TARGET" ]] && continue
  if (( kept < KEEP_RELEASES - 1 )); then ((kept+=1)); else rm -rf -- "$path"; fi
done

echo "Despliegue correcto: $VERSION"
echo "Backend activo: $(readlink -f "$CURRENT")"
