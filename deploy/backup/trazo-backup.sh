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
EXTRA_PATHS="${EXTRA_PATHS:-/var/lib/trazo/data-protection-keys /var/lib/trazo/r8-mappings /etc/trazo/trazo.env}"

for bin in pg_dump age rclone tar runuser; do
  command -v "$bin" >/dev/null || { echo "ERROR: falta $bin." >&2; exit 1; }
done

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
name="trazo-${stamp}.tar.age"
work="$(mktemp -d /var/tmp/trazo-backup.XXXXXX)"
trap 'rm -rf "$work"' EXIT

echo ">> Volcando la base '$DB_NAME'..."
mkdir -p "$work/payload"
runuser -u postgres -- pg_dump --format=custom --no-owner "$DB_NAME" > "$work/payload/database.dump"
[[ -s "$work/payload/database.dump" ]] || { echo "ERROR: el volcado de la BD esta vacio." >&2; exit 1; }

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

echo ">> Cifrando con age..."
tar -C "$work/payload" -cf - . | age --recipient "$AGE_RECIPIENT" --output "$work/$name"
[[ -s "$work/$name" ]] || { echo "ERROR: el fichero cifrado esta vacio." >&2; exit 1; }

echo ">> Subiendo a $RCLONE_REMOTE ($(du -h "$work/$name" | cut -f1))..."
# copyto compara el checksum remoto tras subir: si no coincide, falla.
rclone copyto "$work/$name" "$RCLONE_REMOTE/$name" --checksum --retries 3

# Retencion: las KEEP_DAILY mas recientes + los KEEP_WEEKLY domingos mas recientes. Borra el resto.
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
