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
DataProtection__KeysPath=/var/lib/trazo/data-protection-keys
Cors__AllowedOrigins__0=https://trazostrength.duckdns.org
# Google, SMTP y el superadmin NO van aqui: se configuran desde el asistente de primer
# arranque del frontend (docs/configuration-and-deployment.md, seccion 3.1).
DietoExpressImport__ApiKey=$import_key
Logging__LogLevel__Default=Information
EOF
  chown root:trazo /etc/trazo/trazo.env
  chmod 0640 /etc/trazo/trazo.env
  echo ">> Creado /etc/trazo/trazo.env con JWT e import key aleatorias."
  echo ">> EDITALO: solo la contrasena de BD. Google, SMTP y el superadmin se configuran desde el asistente web."
  echo ">> IMPORTANTE: completa el asistente de primer arranque INMEDIATAMENTE; quien llegue primero a la URL publica se queda con la instalacion."
else
  echo ">> /etc/trazo/trazo.env ya existe; no se modifica."
fi

# 3.1. R8 Retrace para desofuscar los tickets Android en el servidor.
# El paquete oficial de Android Command-line Tools incluye retrace. Se fija versión y SHA-256
# para que el bootstrap sea reproducible y no descargue silenciosamente un binario distinto.
dnf install -y java-17-openjdk-headless unzip curl
SDK_ROOT="/opt/android-sdk"
RETRACE="$SDK_ROOT/cmdline-tools/latest/bin/retrace"
if [[ ! -x "$RETRACE" ]]; then
  SDK_VERSION="15859902"
  SDK_SHA256="4e4c464f145a7512b57d088ac6c278c03c9eea610886b35a5e0804e74eedf583"
  tmp="$(mktemp -d)"
  archive="$tmp/commandlinetools-linux.zip"
  curl --fail --location --silent --show-error --retry 3 \
    "https://dl.google.com/android/repository/commandlinetools-linux-${SDK_VERSION}_latest.zip" -o "$archive"
  echo "$SDK_SHA256  $archive" | sha256sum --check -
  unzip -q "$archive" -d "$tmp"
  [[ -x "$tmp/cmdline-tools/bin/retrace" ]] || { rm -rf "$tmp"; echo "ERROR: el paquete Android no contiene retrace." >&2; exit 1; }
  install -d -o root -g root -m 0755 "$SDK_ROOT/cmdline-tools/latest"
  cp -a "$tmp/cmdline-tools/." "$SDK_ROOT/cmdline-tools/latest/"
  rm -rf "$tmp"
  chown -R root:root "$SDK_ROOT"
  chmod -R a+rX "$SDK_ROOT"
fi
[[ -x "$RETRACE" ]] || { echo "ERROR: no se ha instalado R8 Retrace." >&2; exit 1; }
java -version

# Directorio privado de mappings. El servicio trazo solo necesita leerlos y escribirlos.
install -d -o trazo -g trazo -m 0700 /var/lib/trazo/r8-mappings

# Completa claves de diagnóstico sin sobrescribir valores que ya existan.
ensure_env_setting() {
  local key="$1" value="$2"
  if grep -q "^${key}=" /etc/trazo/trazo.env; then
    if ! grep -q "^${key}=." /etc/trazo/trazo.env; then
      sed -i "s|^${key}=.*$|${key}=${value}|" /etc/trazo/trazo.env
    fi
  else
    printf '%s=%s\n' "$key" "$value" >> /etc/trazo/trazo.env
  fi
}
ensure_env_setting "Diagnostics__MappingsPath" "/var/lib/trazo/r8-mappings"
ensure_env_setting "Diagnostics__RetraceExecutable" "$RETRACE"
ensure_env_setting "Diagnostics__MappingUploadKey" "$(openssl rand -hex 32)"
chown root:trazo /etc/trazo/trazo.env
chmod 0640 /etc/trazo/trazo.env
echo ">> R8 Retrace instalado y mappings privados configurados."
echo ">> Copia Diagnostics__MappingUploadKey de /etc/trazo/trazo.env al secret TRAZO_DIAGNOSTICS_UPLOAD_KEY de GitHub Actions."

# 4. Unidad systemd.
install -o root -g root -m 0644 "$REPO/deploy/systemd/trazo.service" /etc/systemd/system/trazo.service
systemctl daemon-reload

# 5. Nginx (solo HTTP; certbot añade HTTPS).
# El instalador conserva las líneas TLS que certbot haya añadido.
install -d -o root -g root -m 0755 /var/www/trazo-releases
bash "$REPO/deploy/scripts/install-trazo-nginx.sh"

# 6. Helper de despliegue + sudoers para el runner.
bash "$REPO/deploy/scripts/provision-trazo-deploy.sh" "$REPO/deploy/scripts/trazo-deploy-root.sh"

# 7. SELinux: permitir a nginx conectar con el upstream (ya activo si DietoExpress funciona).
if command -v getsebool >/dev/null 2>&1 && [[ "$(getsebool httpd_can_network_connect 2>/dev/null)" == *off* ]]; then
  setsebool -P httpd_can_network_connect 1
fi

echo
echo "Bootstrap completado. Siguientes pasos en docs/deployment-runbook.md."
