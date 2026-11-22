#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

# Restaura un backup de TRAZO. Por defecto restaura en una base de PRUEBA (trazo_restore_test)
# para comprobar que la copia sirve; no toca produccion salvo con --production.
#
#   trazo-restore --identity ~/trazo-backup.key --from-drive latest
#   trazo-restore --identity ~/trazo-backup.key --file trazo-20261122T033000Z.tar.age --workdir /tmp/r
#
# Opciones: --identity FICHERO (clave privada age, obligatoria) · --from-drive latest|NOMBRE · --file RUTA
#           --workdir DIR (donde se extrae todo; por defecto un directorio temporal que se conserva)
#           --db NOMBRE (por defecto trazo_restore_test) · --production (permite restaurar sobre la base 'trazo')

identity="" source_drive="" source_file="" workdir="" db="trazo_restore_test" production=0
while (($#)); do
  case "$1" in
    --identity) identity="${2:?falta valor}"; shift 2 ;;
    --from-drive) source_drive="${2:?falta valor}"; shift 2 ;;
    --file) source_file="${2:?falta valor}"; shift 2 ;;
    --workdir) workdir="${2:?falta valor}"; shift 2 ;;
    --db) db="${2:?falta valor}"; shift 2 ;;
    --production) production=1; shift ;;
    *) echo "Opcion desconocida: $1" >&2; exit 2 ;;
  esac
done
[[ -f "$identity" ]] || { echo "ERROR: --identity debe apuntar a la clave privada age." >&2; exit 2; }
[[ -n "$source_drive$source_file" ]] || { echo "ERROR: indica --from-drive o --file." >&2; exit 2; }
[[ "$db" =~ ^[A-Za-z0-9_]+$ ]] || { echo "ERROR: nombre de base no valido." >&2; exit 2; }
if [[ "$db" == "trazo" && $production -ne 1 ]]; then
  echo "ERROR: restaurar sobre 'trazo' exige --production (y parar el servicio antes)." >&2
  exit 2
fi
for bin in age tar pg_restore psql runuser; do
  command -v "$bin" >/dev/null || { echo "ERROR: falta $bin." >&2; exit 1; }
done

[[ -n "$workdir" ]] || workdir="$(mktemp -d /var/tmp/trazo-restore.XXXXXX)"
mkdir -p "$workdir"

if [[ -n "$source_drive" ]]; then
  CONFIG="${TRAZO_BACKUP_CONFIG:-/etc/trazo/backup.env}"
  # shellcheck disable=SC1090
  source "$CONFIG"
  export RCLONE_CONFIG="${RCLONE_CONFIG:-/etc/trazo/rclone.conf}"
  if [[ "$source_drive" == latest ]]; then
    source_drive="$(rclone lsf "$RCLONE_REMOTE" --files-only | grep -E '^trazo-.*\.tar\.age$' | sort | tail -n 1)"
    [[ -n "$source_drive" ]] || { echo "ERROR: no hay backups en $RCLONE_REMOTE." >&2; exit 1; }
  fi
  echo ">> Descargando $source_drive..."
  rclone copyto "$RCLONE_REMOTE/$source_drive" "$workdir/$source_drive"
  source_file="$workdir/$source_drive"
fi

echo ">> Descifrando y extrayendo en $workdir/payload..."
mkdir -p "$workdir/payload"
age --decrypt --identity "$identity" "$source_file" | tar -C "$workdir/payload" -xf -
cat "$workdir/payload/MANIFEST.txt"

echo ">> Restaurando la base '$db'..."
runuser -u postgres -- psql -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$db\"" -c "CREATE DATABASE \"$db\""
# El usuario postgres debe poder leer el volcado.
chmod a+rx "$workdir" "$workdir/payload"
chmod a+r "$workdir/payload/database.dump"
runuser -u postgres -- pg_restore --no-owner --dbname="$db" "$workdir/payload/database.dump"
tables="$(runuser -u postgres -- psql -tA -d "$db" -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'")"
echo ">> Base '$db' restaurada: $tables tablas en public."
if [[ "$db" == "trazo" ]]; then
  runuser -u postgres -- psql -v ON_ERROR_STOP=1 -d "$db" \
    -c "GRANT ALL ON ALL TABLES IN SCHEMA public TO trazo_app" -c "GRANT ALL ON ALL SEQUENCES IN SCHEMA public TO trazo_app" \
    -c "ALTER DATABASE \"$db\" OWNER TO trazo_app"
fi

echo ">> Ficheros de estado extraidos en $workdir/payload/files (data-protection-keys, r8-mappings, trazo.env)."
echo "   En un desastre real, copialos a su ruta original con el mismo propietario y permisos antes de arrancar el servicio."
