#!/usr/bin/env bash
set -Eeuo pipefail

# Vigila que haya una copia reciente en Drive. Lo lanza trazo-backup-check.timer (diario) y, con --failed,
# trazo-backup-failed.service cuando el propio backup falla.
# Si falla: sale con codigo 1 (la unidad queda "failed"), escribe en el journal con prioridad error y, si NOTIFY_URL
# esta definida en backup.env, hace un POST con el mensaje (compatible con ntfy.sh y servicios parecidos).

CONFIG="${TRAZO_BACKUP_CONFIG:-/etc/trazo/backup.env}"
[[ -f "$CONFIG" ]] || { echo "ERROR: falta $CONFIG." >&2; exit 1; }
# shellcheck disable=SC1090
source "$CONFIG"
export RCLONE_CONFIG="${RCLONE_CONFIG:-/etc/trazo/rclone.conf}"
MAX_AGE_HOURS="${MAX_AGE_HOURS:-36}"

alert() {
  local msg="$1"
  echo "ALERTA: $msg" >&2
  logger -t trazo-backup-check -p user.err "$msg" || true
  if [[ -n "${NOTIFY_URL:-}" ]]; then
    curl --fail --silent --show-error --max-time 20 --header "Title: TRAZO backup" --data "$msg" "$NOTIFY_URL" \
      || echo "AVISO: no se pudo enviar la notificacion a NOTIFY_URL." >&2
  fi
}

if [[ "${1:-}" == "--failed" ]]; then
  alert "El backup de $(hostname) ha fallado. Revisa: journalctl -u trazo-backup -n 50"
  exit 1
fi

: "${RCLONE_REMOTE:?RCLONE_REMOTE sin definir en $CONFIG}"
latest="$(rclone lsf "$RCLONE_REMOTE" --files-only 2>/dev/null | grep -E '^trazo-[0-9]{8}T[0-9]{6}Z\.tar\.age$' | sort | tail -n 1 || true)"
if [[ -z "$latest" ]]; then
  alert "No hay ninguna copia en $RCLONE_REMOTE (o Drive no responde)."
  exit 1
fi

stamp="${latest:6:15}" # AAAAMMDDTHHMMSSZ
iso="${stamp:0:4}-${stamp:4:2}-${stamp:6:2}T${stamp:9:2}:${stamp:11:2}:${stamp:13:2}Z"
age_hours=$(( ( $(date -u +%s) - $(date -u -d "$iso" +%s) ) / 3600 ))
if (( age_hours > MAX_AGE_HOURS )); then
  alert "La ultima copia ($latest) tiene $age_hours h; el maximo es $MAX_AGE_HOURS h."
  exit 1
fi
echo ">> OK: ultima copia $latest ($age_hours h)."
