#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

# Atiende la peticion de restauracion que deja el panel de la consola de administracion (docs/backups.md).
# Lo lanza trazo-restore-request.service (root) cuando la API crea /run/trazo/restore-request junto a /run/trazo/restore-key.
#   mode=test        descarga, descifra y restaura en la base de PRUEBA para comprobar que la copia sirve; despues la borra.
#   mode=production  primero verifica igual; si va bien, guarda un volcado previo, para trazo, restaura la base y las claves de
#                    Data Protection (NO trazo.env: se conserva el actual) y arranca trazo. Si la restauracion falla, vuelve atras.
# La clave privada age llega solo por /run (tmpfs) y se destruye al terminar.

REQ_DIR="${TRAZO_REQUEST_DIR:-/run/trazo}"
STATUS_DIR="${BACKUP_STATUS_DIR:-/var/lib/trazo}"
SAFE_DIR="${TRAZO_PRE_RESTORE_DIR:-/var/backups/trazo-pre-restore}"
TEST_DB="trazo_restore_test"
RESTORE_BIN="${TRAZO_RESTORE_BIN:-/usr/local/sbin/trazo-restore}"

work="$(mktemp -d /run/trazo-restore-req.XXXXXX)"   # tmpfs, root 0700
mode="" copy="" started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)" finished_ok=0 step="preparacion" service_stopped=0 log="$work/log"

json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr '\n\t' '  '; }
write_status() { # state message
  local target="$STATUS_DIR/backup-restore-status.json" tmp
  [[ -d "$STATUS_DIR" ]] || return 0
  tmp="$(mktemp "$target.XXXXXX")" || return 0
  printf '{"state":"%s","mode":"%s","copy":"%s","startedAt":"%s","finishedAt":%s,"message":"%s"}\n' \
    "$1" "$mode" "$copy" "$started_at" \
    "$([[ "$1" == running ]] && echo null || echo "\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"")" "$(json_escape "$2")" > "$tmp"
  chmod 0640 "$tmp"; chown trazo:trazo "$tmp" 2>/dev/null || true
  mv -f "$tmp" "$target"
}
finish() {
  local rc=$? last=""
  [[ -f "$log" ]] && last="$(tail -n 3 "$log" | tr '\n' ' ')"
  { command -v shred >/dev/null && shred -u "$work/key" 2>/dev/null; } || rm -f "$work/key"
  rm -rf "$work" "${restore_dir:-/nonexistent}"
  # Si paramos trazo y algo fallo antes de arrancarlo de nuevo, no lo dejamos caido.
  if [[ $service_stopped -eq 1 && $finished_ok -ne 1 ]]; then systemctl start trazo || true; fi
  if [[ $finished_ok -ne 1 ]]; then write_status failed "Fallo en el paso '$step' (codigo $rc). $last" || true; fi
}
trap finish EXIT

# Toma posesion de la peticion y de la clave ya: la API (usuario trazo) no puede cambiarlas despues de moverlas.
[[ -e "$REQ_DIR/restore-request" && -e "$REQ_DIR/restore-key" ]] || { echo "ERROR: falta la peticion o la clave." >&2; exit 1; }
mv -f "$REQ_DIR/restore-request" "$work/request"
mv -f "$REQ_DIR/restore-key" "$work/key"
for f in request key; do
  [[ -f "$work/$f" && ! -L "$work/$f" ]] || { echo "ERROR: $f no es un fichero normal." >&2; exit 1; }
done
chmod 0600 "$work/key"
while IFS='=' read -r k v; do
  case "$k" in copy) copy="$v" ;; mode) mode="$v" ;; esac
done < "$work/request"
[[ "$copy" =~ ^trazo-[0-9]{8}T[0-9]{6}Z\.tar\.age$ ]] || { echo "ERROR: nombre de copia no valido." >&2; exit 1; }
[[ "$mode" == test || "$mode" == production ]] || { echo "ERROR: modo no valido." >&2; exit 1; }
[[ "$(wc -l < "$work/key")" -eq 1 ]] && grep -qE '^AGE-SECRET-KEY-1[A-Z0-9]+$' "$work/key" || { echo "ERROR: clave no valida." >&2; exit 1; }

write_status running "Descargando y descifrando la copia..."
exec > >(tee -a "$log") 2>&1

restore_dir="$(mktemp -d /var/tmp/trazo-restore-req.XXXXXX)"
step="verificacion en la base de prueba"
echo ">> Verificando $copy en $TEST_DB..."
"$RESTORE_BIN" --identity "$work/key" --from-drive "$copy" --workdir "$restore_dir" --db "$TEST_DB"
verified="$(grep -E '^>> Base ' "$log" | tail -n 1 | sed 's/^>> //')"
runuser -u postgres -- psql -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$TEST_DB\""
rm -rf "$restore_dir"; restore_dir=""

if [[ "$mode" == test ]]; then
  finished_ok=1
  write_status ok "Copia verificada: la clave la abre y la base se restaura sin errores. ${verified:-} Producción no se ha tocado."
  exit 0
fi

step="volcado previo de seguridad"
write_status running "Guardando un volcado previo de la base actual..."
mkdir -p "$SAFE_DIR"; chmod 0700 "$SAFE_DIR"
pre="$SAFE_DIR/pre-restore-$(date -u +%Y%m%dT%H%M%SZ).dump"
runuser -u postgres -- pg_dump --format=custom --no-owner trazo > "$pre"
[[ -s "$pre" ]] || { echo "ERROR: el volcado previo esta vacio." >&2; exit 1; }
# Conserva solo los 3 volcados previos mas recientes.
mapfile -t olds < <(ls -1t "$SAFE_DIR"/pre-restore-*.dump 2>/dev/null | tail -n +4)
((${#olds[@]})) && rm -f "${olds[@]}"

step="restauracion en produccion"
restore_dir="$(mktemp -d /var/tmp/trazo-restore-req.XXXXXX)"
write_status running "Restaurando en producción (el servidor está parado)..."
systemctl stop trazo
service_stopped=1
if ! "$RESTORE_BIN" --identity "$work/key" --from-drive "$copy" --workdir "$restore_dir" --db trazo --production; then
  echo "ERROR: la restauracion fallo; recuperando el volcado previo." >&2
  runuser -u postgres -- psql -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"trazo\"" -c "CREATE DATABASE \"trazo\""
  chmod a+rx "$SAFE_DIR"; chmod a+r "$pre"
  runuser -u postgres -- pg_restore --no-owner --dbname=trazo "$pre"
  runuser -u postgres -- psql -v ON_ERROR_STOP=1 -d trazo \
    -c "GRANT ALL ON ALL TABLES IN SCHEMA public TO trazo_app" -c "GRANT ALL ON ALL SEQUENCES IN SCHEMA public TO trazo_app" \
    -c "ALTER DATABASE \"trazo\" OWNER TO trazo_app"
  chmod a-rx "$SAFE_DIR"
  systemctl start trazo; service_stopped=0
  step="restauracion en produccion (se ha vuelto atras: la base queda como estaba)"
  exit 1
fi

step="claves de Data Protection"
keys_src="$restore_dir/payload/files/var/lib/trazo/data-protection-keys"
if [[ -d "$keys_src" ]]; then
  rm -rf /var/lib/trazo/data-protection-keys
  cp -a "$keys_src" /var/lib/trazo/data-protection-keys
  chown -R trazo:trazo /var/lib/trazo/data-protection-keys
fi

step="arranque de trazo"
systemctl start trazo
service_stopped=0
sleep 3
systemctl is-active --quiet trazo || { echo "ERROR: trazo no arranca tras la restauracion." >&2; exit 1; }
finished_ok=1
write_status ok "Restaurado ${copy} en producción. ${verified:-} Volcado previo guardado en $pre (root). trazo.env no se ha tocado."
