#!/usr/bin/env bash
set -Eeuo pipefail

# Punto de entrada no privilegiado del runner self-hosted de TRAZO.
# Toda operación privilegiada queda encapsulada en un helper root-owned.
[[ "$(id -un)" == "joso" ]] || {
  echo "ERROR: el despliegue debe ejecutarse como joso." >&2
  exit 1
}

VERSION="${TRAZO_VERSION:-}"
[[ "$VERSION" =~ ^[A-Za-z0-9._-]{7,80}$ ]] || {
  echo "ERROR: identificador de versión no válido." >&2
  exit 1
}

[[ -d /tmp/trazo-backend && -f /tmp/trazo-backend/Trazo.Api.dll ]] || {
  echo "ERROR: falta el backend publicado en /tmp/trazo-backend." >&2
  exit 1
}

command -v sudo >/dev/null 2>&1 || {
  echo "ERROR: sudo no está instalado." >&2
  exit 1
}

# -n evita esperar una contraseña en un runner sin terminal. La autorización
# se limita a este helper y a un identificador de versión validado.
sudo -n /usr/local/sbin/trazo-deploy "$VERSION"
