#!/usr/bin/env bash
set -Eeuo pipefail

# Instala el backup cifrado a Drive (idempotente). Ejecutar como root desde la raiz del repo:
#   sudo bash deploy/scripts/install-trazo-backup.sh
# Despues sigue docs/backups.md: configurar rclone, generar la clave age y rellenar /etc/trazo/backup.env.
[[ "$(id -u)" -eq 0 ]] || { echo "ERROR: ejecutar como root." >&2; exit 1; }
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
[[ -f "$REPO/deploy/backup/trazo-backup.sh" ]] || { echo "ERROR: ejecutar desde un clon del repositorio." >&2; exit 1; }

dnf install -y age rclone
install -o root -g root -m 0755 "$REPO/deploy/backup/trazo-backup.sh" /usr/local/sbin/trazo-backup
install -o root -g root -m 0755 "$REPO/deploy/backup/trazo-restore.sh" /usr/local/sbin/trazo-restore
install -o root -g root -m 0644 "$REPO/deploy/systemd/trazo-backup.service" /etc/systemd/system/trazo-backup.service
install -o root -g root -m 0644 "$REPO/deploy/systemd/trazo-backup.timer" /etc/systemd/system/trazo-backup.timer

if [[ ! -f /etc/trazo/backup.env ]]; then
  umask 077
  cat > /etc/trazo/backup.env <<'ENV'
# Clave PUBLICA age (age1...). La privada NO va en el servidor.
AGE_RECIPIENT=
# remote:carpeta de rclone, p. ej. trazo-drive:trazo-backups
RCLONE_REMOTE=
DB_NAME=trazo
KEEP_DAILY=14
KEEP_WEEKLY=8
ENV
  echo ">> Creado /etc/trazo/backup.env: rellena AGE_RECIPIENT y RCLONE_REMOTE."
fi
systemctl daemon-reload
echo ">> Instalado. Cuando backup.env y rclone esten listos: sudo systemctl enable --now trazo-backup.timer"
echo ">> Primera prueba manual: sudo systemctl start trazo-backup.service && journalctl -u trazo-backup -n 50"
