# Runbook de despliegue de TRAZO en weirdfish

Despliegue continuo del backend con el mismo patrón que DietoExpress: un runner self-hosted de GitHub Actions compila, prueba y publica; un helper root con sudoers acotado activa la release de forma atómica y hace rollback automático si falla el smoke test.

```
git push main → Actions (runner en weirdfish) → /tmp/trazo-backend
   → sudo trazo-deploy <sha> → /opt/trazo/releases/<sha> ← /opt/trazo/current
   → systemd trazo.service (127.0.0.1:5001) ← Nginx :80/:443 ← Internet
```

Alcance actual: solo el API. El Angular de administración aún es una maqueta y no se despliega.

## Tareas únicas (las haces tú en weirdfish y en el router)

### 1. DNS
Crea el subdominio `trazo` en DuckDNS apuntando a la misma IP pública que `jlhitap`. El router ya reenvía 80/443 a weirdfish; **no** abras el 5001. Comprueba: `nslookup trazostrength.duckdns.org`.

Si prefieres otro hostname, cámbialo en `deploy/scripts/trazo-deploy-root.sh` (`PUBLIC_HOST`), `deploy/nginx/trazo.conf` y `AllowedHosts` del env.

### 2. PostgreSQL (rol y base exclusivos)
```bash
sudo -u postgres psql -c "CREATE ROLE trazo_app LOGIN PASSWORD 'UNA_CONTRASEÑA_LARGA'"
sudo -u postgres psql -c "CREATE DATABASE trazo OWNER trazo_app"
```

### 3. Runtime .NET
`/usr/bin/dotnet --list-runtimes` debe listar `Microsoft.AspNetCore.App 10.x` (ya requerido por DietoExpress si usa .NET 10).

### 4. Bootstrap del servidor
```bash
git clone https://github.com/jl-hita/Trazo.git ~/trazo-bootstrap
cd ~/trazo-bootstrap
sudo bash deploy/scripts/bootstrap-trazo-server.sh
```
Crea el usuario `trazo`, `/opt/trazo`, `/var/lib/trazo/data-protection-keys`, `/etc/trazo/trazo.env` (con JWT e import key aleatorios), la unidad systemd, el vhost Nginx y el helper `/usr/local/sbin/trazo-deploy` con su regla sudoers. Revisa los scripts antes de ejecutarlos como root.

Después edita `/etc/trazo/trazo.env` solo para la contraseña de BD. Google, SMTP y la cuenta superadmin se configuran desde el asistente de primer arranque del frontend (ver `docs/configuration-and-deployment.md`, sección 3.1): complétalo **inmediatamente** tras el primer despliegue, porque quien llegue primero a la URL pública se queda con la instalación.

### 5. Runner de GitHub Actions
Es un runner **por repositorio**: el de DietoExpress no sirve para TRAZO. Instala otro, en su propio directorio y con su propio servicio, como `joso`:

1. GitHub → `jl-hita/Trazo` → Settings → Actions → Runners → **New self-hosted runner** → Linux x64. GitHub muestra los comandos y el token de registro (caduca en 1 hora).
2. ```bash
   mkdir -p ~/actions-runner-trazo && cd ~/actions-runner-trazo
   # descargar y extraer según las instrucciones de GitHub, luego:
   ./config.sh --url https://github.com/jl-hita/Trazo --token <TOKEN> --name weirdfish-trazo
   sudo ./svc.sh install joso && sudo ./svc.sh start
   ```
   Las etiquetas por defecto (`self-hosted`, `Linux`, `X64`) coinciden con el workflow.
3. Requisitos: `git`, `curl`, `rsync`, `python3` y salida a Internet (descarga el SDK .NET).

### 6. Primer despliegue
Con todo lo anterior, cualquier push a `main` que toque `src/`, `tests/` o `deploy/` despliega, o ejecuta manualmente **Actions → Deploy TRAZO API → Run workflow**. El despliegue aplica las migraciones EF y el seed al arrancar y solo se da por bueno si responden `/health`, `/health/ready` y `/api/v1/exercises`.

### 7. HTTPS
Con el DNS resuelto y el 80 abierto:
```bash
sudo certbot --nginx -d trazostrength.duckdns.org
```
Verifica desde fuera de tu red (p. ej. datos móviles):
```bash
curl https://trazostrength.duckdns.org/health
curl "https://trazostrength.duckdns.org/api/v1/exercises?pageSize=1"
```

### 8. Tras el primer arranque
- Si usaste `BootstrapAdmin__*` en lugar del asistente, comprueba que el superadmin inicia sesión y **borra** esas tres variables de `trazo.env`; `sudo systemctl restart trazo`.
- Haz copia de seguridad de la base `trazo` y de `/var/lib/trazo/data-protection-keys` (sin esas claves se pierden los secretos 2FA).
- Compila Android con `-PapiScheme=https -PapiHost=trazostrength.duckdns.org -PapiPort=443`.

## Operación

| Acción | Comando |
|---|---|
| Estado / logs | `systemctl status trazo` · `journalctl -u trazo -f` |
| Release activa | `readlink -f /opt/trazo/current` |
| Rollback manual | `sudo ln -sfn /opt/trazo/releases/<sha-anterior> /opt/trazo/current && sudo systemctl restart trazo` |
| Cambiar configuración | Google/SMTP: pantalla Settings del admin (sin reinicio). Resto: editar `/etc/trazo/trazo.env` y `sudo systemctl restart trazo` |

Se conservan las 5 últimas releases. El rollback revierte el código, **no** el esquema: las migraciones deben ser compatibles hacia atrás con la release anterior, y conviene respaldar la BD antes de desplegar migraciones destructivas.

## Seguridad del diseño
- El runner (`joso`) no escribe en `/opt/trazo`; solo puede invocar `trazo-deploy` con un SHA validado.
- El servicio corre como `trazo`, sin shell, con `ProtectSystem=strict` y escritura solo en `/var/lib/trazo`.
- Los secretos viven únicamente en `/etc/trazo/trazo.env` (0640 root:trazo); nada en el repositorio ni en el runner.
- Kestrel escucha solo en `127.0.0.1:5001`; Nginx es la única entrada pública.
- Los workflows se ejecutan en tu servidor: no permitas que PRs de terceros disparen jobs en el runner (el workflow solo se activa con push a `main` y manualmente).
