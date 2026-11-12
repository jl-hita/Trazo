#!/usr/bin/env bash
set -Eeuo pipefail

# Provisioning ÚNICO de weirdfish para TRAZO (Fedora). Ejecutar como root desde
# la raíz del repositorio clonado:
#   sudo bash deploy/scripts/bootstrap-trazo-server.sh
#
# Es idempotente: no sobrescribe /etc/trazo/trazo.env si ya existe y no toca
# DietoExpress. NO crea la base de datos ni el rol PostgreSQL (ver runbook).
[[ "$(id -u)" -eq 0 ]] || { echo "ERROR: ejecutar como root." >&2; exit 1; }
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
[[ -f "$REPO/deploy/systemd/trazo.service" ]] || { echo "ERROR: ejecutar desde un clon del repositorio." >&2; exit 1; }

# 1. Usuario de servicio dedicado, sin shell ni login.
id trazo >/dev/null 2>&1 || useradd --system --home-dir /var/lib/trazo --shell /usr/sbin/nologin trazo

# 2. Directorios: datos persistentes (Data Protection) y configuración.
install -d -o trazo -g trazo -m 0750 /var/lib/trazo
install -d -o trazo -g trazo -m 0700 /var/lib/trazo/data-protection-keys
install -d -o root -g trazo -m 0750 /etc/trazo
install -d -o root -g root -m 0755 /opt/trazo /opt/trazo/releases

# 3. Plantilla de entorno con secretos generados. Solo si no existe.
if [[ ! -f /etc/trazo/trazo.env ]]; then
  jwt="$(openssl rand -base64 48 | tr -d '\n')"
  import_key="$(openssl rand -base64 48 | tr -d '\n')"
  umask 027
  cat > /etc/trazo/trazo.env <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5001
AllowedHosts=trazostrength.duckdns.org;localhost
ConnectionStrings__Trazo=Host=127.0.0.1;Port=5432;Database=trazo;Username=trazo_app;Password=CAMBIAR_PASSWORD_BD
Authentication__Jwt__Issuer=TRAZO
Authentication__Jwt__Audience=TRAZO-Clients
Authentication__Jwt__SigningKey=$jwt
Authentication__Google__ClientId=
DataProtection__KeysPath=/var/lib/trazo/data-protection-keys
Cors__AllowedOrigins__0=https://trazostrength.duckdns.org
# SMTP: completar antes de abrir el registro público.
Email__Smtp__Host=
Email__Smtp__Port=587
Email__Smtp__EnableSsl=true
Email__Smtp__Username=
Email__Smtp__Password=
Email__From=
Email__ActionBaseUrl=trazo://account
# Bootstrap del primer superadmin: rellenar, arrancar una vez y BORRAR estas 3 líneas.
BootstrapAdmin__Email=
BootstrapAdmin__Password=
BootstrapAdmin__DisplayName=TRAZO Administrator
DietoExpressImport__ApiKey=$import_key
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
EOF
  chown root:trazo /etc/trazo/trazo.env
  chmod 0640 /etc/trazo/trazo.env
  echo ">> Creado /etc/trazo/trazo.env con JWT e import key aleatorias."
  echo ">> EDÍTALO: contraseña de BD, SMTP, Google y BootstrapAdmin."
else
  echo ">> /etc/trazo/trazo.env ya existe; no se modifica."
fi

# 4. Unidad systemd.
install -o root -g root -m 0644 "$REPO/deploy/systemd/trazo.service" /etc/systemd/system/trazo.service
systemctl daemon-reload

# 5. Nginx (solo HTTP; certbot añade HTTPS).
install -o root -g root -m 0644 "$REPO/deploy/nginx/trazo.conf" /etc/nginx/conf.d/trazo.conf
nginx -t
systemctl reload nginx

# 6. Helper de despliegue + sudoers para el runner.
bash "$REPO/deploy/scripts/provision-trazo-deploy.sh" "$REPO/deploy/scripts/trazo-deploy-root.sh"

# 7. SELinux: permitir a nginx conectar con el upstream (ya activo si DietoExpress funciona).
if command -v getsebool >/dev/null 2>&1 && [[ "$(getsebool httpd_can_network_connect 2>/dev/null)" == *off* ]]; then
  setsebool -P httpd_can_network_connect 1
fi

echo
echo "Bootstrap completado. Siguientes pasos en docs/deployment-runbook.md."
