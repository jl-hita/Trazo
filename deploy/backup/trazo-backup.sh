#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

# Backup de recuperacion ante desastres de TRAZO: toda la BD + secretos del servidor,
# cifrado con age (solo clave PUBLICA en el servidor) y subido a Google Drive con rclone.
# Lo lanza trazo-backup.timer como root. Config: /etc/trazo/backup.env (ver docs/backups.md).

CONFIG="${TRAZO_BACKUP_CONFIG:-/etc/trazo/backup.env}"
[[ -f "$CONFIG" ]] || { echo "ERROR: falta $CONFIG." >&2; exit 1; }
# shellcheck disable=SC1090
source "$CONFIG"

: "${AGE_RECIPIENT:?AGE_RECIPIENT sin definir en $CONFIG}"
: "${RCLONE_REMOTE:?RCLONE_REMOTE sin definir en $CONFIG (p. ej. trazo-drive:trazo-backups)}"
DB_NAME="${DB_NAME:-trazo}"
KEEP_DAILY="${KEEP_DAILY:-14}"
KEEP_WEEKLY="${KEEP_WEEKLY:-8}"
export RCLONE_CONFIG="${RCLONE_CONFIG:-/etc/trazo/rclone.conf}"
# Rutas incluidas ademas de la BD: sin las claves de Data Protection se pierden los secretos 2FA.
# r8-mappings queda fuera a proposito (cientos de MB que casi no cambian); anade la ruta en EXTRA_PATHS si la quieres.
EXTRA_PATHS="${EXTRA_PATHS:-/var/lib/trazo/data-protection-keys /etc/trazo/trazo.env}"

for bin in pg_dump age rclone tar runuser; do
  command -v "$bin" >/dev/null || { echo "ERROR: falta $bin." >&2; exit 1; }
done

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
name="trazo-${stamp}.tar.age"
started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
work="$(mktemp -d /var/tmp/trazo-backup.XXXXXX)"

# Como termino la ultima ejecucion, para el panel de la consola de administracion (la API lo lee; docs/backups.md).
STATUS_DIR="${BACKUP_STATUS_DIR:-/var/lib/trazo}"
step="preparacion"
finished_ok=0
json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr '\n\t' '  '; }
write_last_run() { # ok(true|false) copia error
  local target="$STATUS_DIR/backup-last-run.json" tmp error="null"
  [[ -n "$3" ]] && error="\"$(json_escape "$3")\""
  [[ -d "$STATUS_DIR" ]] || return 0
  tmp="$(mktemp "$target.XXXXXX")" || return 0
  printf '{"ok":%s,"startedAt":"%s","finishedAt":"%s","copy":"%s","error":%s}\n' \
    "$1" "$started_at" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$2" "$error" > "$tmp"
  chmod 0640 "$tmp"; chown trazo:trazo "$tmp" 2>/dev/null || true
  mv -f "$tmp" "$target"
}
finish() {
  local rc=$?
  rm -rf "$work"
  if [[ $finished_ok -ne 1 ]]; then write_last_run false "" "Fallo en el paso '$step' (codigo $rc)" || true; fi
}
trap finish EXIT

step="volcado de la base"
echo ">> Volcando la base '$DB_NAME'..."
mkdir -p "$work/payload"
runuser -u postgres -- pg_dump --format=custom --no-owner "$DB_NAME" > "$work/payload/database.dump"
[[ -s "$work/payload/database.dump" ]] || { echo "ERROR: el volcado de la BD esta vacio." >&2; exit 1; }

step="copia de secretos"
echo ">> Copiando secretos y ficheros de estado..."
manifest="$work/payload/MANIFEST.txt"
{ echo "trazo-backup $stamp"; echo "db=$DB_NAME"; echo "host=$(hostname)"; } > "$manifest"
for path in $EXTRA_PATHS; do
  if [[ -e "$path" ]]; then
    dest="$work/payload/files$(dirname "$path")"
    mkdir -p "$dest"
    cp -a "$path" "$dest/"
    echo "file=$path" >> "$manifest"
  else
    echo "AVISO: $path no existe; se omite." >&2
  fi
done

step="cifrado"
echo ">> Cifrando con age..."
tar -C "$work/payload" -cf - . | age --recipient "$AGE_RECIPIENT" --output "$work/$name"
[[ -s "$work/$name" ]] || { echo "ERROR: el fichero cifrado esta vacio." >&2; exit 1; }

step="subida a Drive"
echo ">> Subiendo a $RCLONE_REMOTE ($(du -h "$work/$name" | cut -f1))..."
# copyto compara el checksum remoto tras subir: si no coincide, falla.
rclone copyto "$work/$name" "$RCLONE_REMOTE/$name" --checksum --retries 3

# Retencion: las KEEP_DAILY mas recientes + los KEEP_WEEKLY domingos mas recientes. Borra el resto.
step="retencion"
echo ">> Aplicando retencion ($KEEP_DAILY diarias, $KEEP_WEEKLY semanales)..."
mapfile -t all < <(rclone lsf "$RCLONE_REMOTE" --files-only | grep -E '^trazo-[0-9]{8}T[0-9]{6}Z\.tar\.age$' | sort -r)
declare -A keep=()
for ((i = 0; i < ${#all[@]} && i < KEEP_DAILY; i++)); do keep["${all[$i]}"]=1; done
weekly=0
for f in "${all[@]}"; do
  ((weekly >= KEEP_WEEKLY)) && break
  day="${f:6:8}"
  if [[ "$(date -u -d "$day" +%u)" == 7 ]]; then
    keep["$f"]=1
    weekly=$((weekly + 1))
  fi
done
# Nunca borra la copia recien subida, pase lo que pase.
keep["$name"]=1
for f in "${all[@]}"; do
  if [[ -z "${keep[$f]:-}" ]]; then
    echo "   borrando $f"
    rclone deletefile "$RCLONE_REMOTE/$f"
  fi
done

echo ">> Backup $name completado."
finished_ok=1
write_last_run true "$name" ""
# Refresca la lista de copias que lee el panel (y comprueba que la recien subida es visible en Drive).
if [[ -x /usr/local/sbin/trazo-backup-check ]]; then /usr/local/sbin/trazo-backup-check || true; fi
