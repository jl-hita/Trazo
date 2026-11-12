# Configuración completa de TRAZO (API y administración web)

Esta guía describe la configuración que consume el código actual del repositorio. TRAZO es independiente de DietoExpress: debe usar su propia base PostgreSQL, claves JWT, claves de Data Protection, servicio systemd y copias de seguridad.

## 1. Arquitectura prevista

En el servidor compartido `weirdfish`:

| Componente | Dirección de escucha | Uso |
|---|---|---|
| DietoExpress API | `127.0.0.1:5000` | API existente |
| TRAZO API | `127.0.0.1:5001` | API para Android y administración |
| Nginx | TCP 80 y 443 | Entrada pública y proxy inverso |
| PostgreSQL | Local/privado | Bases de datos separadas |

Hostname público previsto: `trazo.duckdns.org`. Es una elección pendiente de reservar y configurar; esta documentación no presupone que el registro DNS exista todavía.

La idea es que Nginx atienda `https://trazo.duckdns.org` y reenvíe al API en `http://127.0.0.1:5001`. El puerto 5001 no se abre en el router. En el NAT solo se reenvían TCP 80 y 443 al servidor. DietoExpress conserva su hostname y upstream actuales.

## 2. Requisitos previos

- .NET 10 SDK para desarrollo; runtime ASP.NET Core 10 para ejecución publicada.
- PostgreSQL compatible con la versión de Npgsql usada por el proyecto.
- Node.js compatible con Angular 21 y npm para la administración web.
- Una cuenta SMTP y un remitente verificado para correos de verificación y recuperación.
- Un proyecto de Google Cloud con clientes OAuth Web y Android si se habilita Google Sign-In.
- Un hostname DuckDNS y un certificado TLS válido antes de habilitar el acceso público.

Crea una base de datos y un usuario exclusivos para TRAZO. Ejemplo orientativo ejecutado como administrador de PostgreSQL:

```sql
CREATE ROLE trazo_app LOGIN PASSWORD 'REPLACE_WITH_A_LONG_RANDOM_PASSWORD';
CREATE DATABASE trazo OWNER trazo_app;
```

El usuario configurado debe poder crear y modificar objetos en su propia base porque la API aplica automáticamente las migraciones EF Core al arrancar. No reutilices el usuario ni la base de DietoExpress.

## 3. Archivo de entorno de la API

En Linux, crea por ejemplo `/etc/trazo/trazo.env`, con permisos de lectura restringidos al usuario del servicio. El siguiente ejemplo recoge las claves de configuración utilizadas por la API, incluidas autenticación, correo, CORS, persistencia y el importador privado opcional.

```ini
# ASP.NET Core
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5001

# PostgreSQL exclusivo de TRAZO
ConnectionStrings__Trazo=Host=127.0.0.1;Port=5432;Database=trazo;Username=trazo_app;Password=REPLACE_WITH_DATABASE_PASSWORD

# Hostnames que acepta ASP.NET Core
AllowedHosts=trazo.duckdns.org;localhost

# CORS: añade el origen real desde el que se servirá Angular.
# Si Angular se sirve desde el mismo origen que la API, no necesita CORS.
Cors__AllowedOrigins__0=https://trazo.duckdns.org
Cors__AllowedOrigins__1=http://localhost:4200

# JWT: genera una clave independiente de DietoExpress, de al menos 32 bytes UTF-8.
Authentication__Jwt__Issuer=TRAZO
Authentication__Jwt__Audience=TRAZO-Clients
Authentication__Jwt__SigningKey=REPLACE_WITH_A_RANDOM_SECRET_OF_AT_LEAST_32_BYTES

# Google Sign-In: ID del cliente OAuth de tipo Web (no un client secret).
Authentication__Google__ClientId=YOUR_WEB_CLIENT_ID.apps.googleusercontent.com

# Data Protection: directorio persistente, fuera de la carpeta de despliegue.
DataProtection__KeysPath=/var/lib/trazo/data-protection-keys

# Correo transaccional (SMTP)
Email__Smtp__Host=smtp.example.com
Email__Smtp__Port=587
Email__Smtp__EnableSsl=true
Email__Smtp__Username=YOUR_SMTP_USERNAME
Email__Smtp__Password=YOUR_SMTP_PASSWORD
Email__From=no-reply@example.com
Email__ActionBaseUrl=trazo://account

# Bootstrap inicial: solo para crear el primer superadmin.
# Elimina estas tres variables tras el primer arranque correcto.
BootstrapAdmin__Email=admin@example.com
BootstrapAdmin__Password=REPLACE_WITH_A_UNIQUE_LONG_INITIAL_PASSWORD
BootstrapAdmin__DisplayName=TRAZO Administrator

# Importador privado DietoExpress -> TRAZO.
# Debe coincidir con TrazoTransfer__ApiKey en el backend de DietoExpress.
# Déjalo sin configurar si no vas a realizar la importación.
DietoExpressImport__ApiKey=REPLACE_WITH_A_LONG_RANDOM_SHARED_SECRET

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
```

### Generar secretos

Genera valores aleatorios distintos para la contraseña PostgreSQL, la clave JWT, la contraseña SMTP si aplica, la clave de importación y la contraseña inicial del superadmin:

```bash
openssl rand -base64 48
```

La clave JWT debe tener al menos 32 bytes UTF-8. No reutilices la clave JWT de DietoExpress. La clave de importación debe ser larga y aleatoria y debe coincidir exactamente en ambos sistemas. No guardes el archivo `trazo.env` en Git ni lo incluyas en artefactos públicos.

### Data Protection

El directorio de claves debe existir o poder crearlo el usuario del servicio. Ejemplo:

```bash
sudo install -d -o trazo -g trazo -m 700 /var/lib/trazo/data-protection-keys
sudo chown root:trazo /etc/trazo/trazo.env
sudo chmod 640 /etc/trazo/trazo.env
```

Las claves deben persistir entre reinicios y despliegues y deben incluirse en una copia de seguridad protegida. Si se pierden, los secretos TOTP cifrados con Data Protection pueden dejar de ser recuperables. No compartas este key ring con DietoExpress.

### Bootstrap del primer superadmin

La API puede crear el primer superadmin cuando la tabla de usuarios está vacía. Tras confirmar que el usuario puede iniciar sesión, elimina `BootstrapAdmin__Email`, `BootstrapAdmin__Password` y `BootstrapAdmin__DisplayName` del archivo de entorno y reinicia el servicio. No mantengas una contraseña de bootstrap activa indefinidamente.

## 4. Servicio systemd de ejemplo

Ejemplo de unidad, suponiendo publicación en `/opt/trazo` y DLL `Trazo.Api.dll`. Ajusta las rutas y el nombre de usuario al despliegue real:

```ini
[Unit]
Description=TRAZO API
After=network-online.target postgresql.service
Wants=network-online.target

[Service]
Type= простой
User=trazo
Group=trazo
WorkingDirectory=/opt/trazo
EnvironmentFile=/etc/trazo/trazo.env
ExecStart=/usr/bin/dotnet /opt/trazo/Trazo.Api.dll
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=trazo-api
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
```

En la unidad real, el valor de `Type` debe ser `simple` (en minúsculas). Después de instalarla:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now trazo
sudo systemctl status trazo
journalctl -u trazo -f
```

## 5. Nginx, DNS y NAT

Cuando hayas reservado el hostname y apuntado DuckDNS a la IP pública:

1. Reenvía en el router únicamente TCP 80 y TCP 443 hacia el servidor.
2. Emite un certificado TLS para `trazo.duckdns.org`.
3. Crea un virtual host Nginx para ese hostname y reenvía al upstream `http://127.0.0.1:5001`.
4. Conserva los encabezados `Host`, `X-Real-IP`, `X-Forwarded-For` y `X-Forwarded-Proto`.
5. Comprueba que el API solo escucha en loopback y que el puerto 5001 no está publicado en el router.

La API ya procesa encabezados forwarded de un proxy local y aplica HTTPS redirection fuera de Development. No habilites el puerto 5001 en Internet ni configures Kestrel en `0.0.0.0` para este diseño.

Comprobaciones locales después del despliegue:

```bash
curl -fsS http://127.0.0.1:5001/health
curl -fsS http://127.0.0.1:5001/health/ready
```

El segundo endpoint comprueba conectividad con PostgreSQL. La configuración DNS, Nginx, certificados y router no se modifica mediante estos ejemplos.

## 6. Configurar Google Sign-In

En Google Cloud Console crea:

- Un OAuth Client ID de tipo **Web application**. Su ID va en `Authentication__Google__ClientId` y en la compilación Android como `googleWebClientId`.
- Un OAuth Client ID de tipo **Android** con el package `com.trazo.android` y la huella SHA-1 del certificado que firma la aplicación instalada. Registra por separado la firma debug y la de Play App Signing.

No pongas el Google client secret en Android. El backend valida el ID token dirigido al cliente Web.

## 7. Administración Angular

La aplicación Angular está en `admin/Trazo.Admin.Web`. En el estado actual es una **estructura visual inicial**: todavía no implementa login administrativo ni consume endpoints reales de la API. Por tanto, no existe aún una variable `apiUrl` ni un archivo de entorno Angular que tengas que rellenar. No añadas una URL ficticia esperando que conecte el frontend.

### Desarrollo local

```bash
cd admin/Trazo.Admin.Web
npm install
npm start
```

Angular sirve la interfaz en `http://localhost:4200`. La API se ejecuta por separado en `http://localhost:5001`. Cuando se implemente la integración, habrá que definir explícitamente la URL/base de API y el flujo de autenticación; entonces se documentarán las variables frontend correspondientes.

### Compilación y publicación

```bash
cd admin/Trazo.Admin.Web
npm run build
```

La salida se genera en `dist/trazo-admin-web`. Puede servirse como contenido estático mediante Nginx cuando la interfaz esté lista. Configura fallback de rutas SPA a `index.html`. Si el panel se sirve desde el mismo origen `https://trazo.duckdns.org`, las peticiones futuras a rutas `/api` podrán compartir origen; si se publica en otro hostname o puerto, añade ese origen exacto a `Cors__AllowedOrigins`.

## 8. Importación privada desde DietoExpress

El endpoint interno de TRAZO es `POST /api/internal/exercise-catalog/import`. Requiere:

- `Content-Type: application/zip`.
- Cabecera `X-Trazo-Import-Key`.
- Valor configurado en TRAZO como `DietoExpressImport__ApiKey`.
- El mismo secreto configurado en DietoExpress como `TrazoTransfer__ApiKey`.

En DietoExpress, `TrazoTransfer__Endpoint` debe apuntar al endpoint público HTTPS de TRAZO, por ejemplo `https://trazo.duckdns.org/api/internal/exercise-catalog/import`. No ejecutes la transferencia hasta confirmar que la licencia de los datos de RepDB permite este uso entre aplicaciones.

## 9. Configuración de desarrollo

El perfil `Trazo.Api` de `launchSettings.json` escucha en `http://localhost:5001` y `https://localhost:7001`. Para una base local, configura las variables necesarias en el entorno del proceso o en secretos de usuario. No guardes credenciales reales en `appsettings.Development.json`.

Ejemplo mínimo para desarrollo (sustituye los valores):

```bash
export ConnectionStrings__Trazo='Host=localhost;Port=5432;Database=trazo_dev;Username=trazo_dev;Password=...'
export Authentication__Jwt__SigningKey='LOCAL_ONLY_RANDOM_SECRET_AT_LEAST_32_BYTES'
export Authentication__Google__ClientId='YOUR_WEB_CLIENT_ID.apps.googleusercontent.com'
export DataProtection__KeysPath="$HOME/.local/share/trazo/dp-keys"
dotnet run --project src/Trazo.Api
```

Si pruebas flujos de correo, añade las variables SMTP de la sección 3. En desarrollo la API no fuerza redirección HTTPS, para facilitar las pruebas desde el emulador Android.

## 10. Lista de verificación antes de abrir el servicio

- [ ] Hostname DuckDNS reservado y apuntando a la IP pública correcta.
- [ ] Certificado TLS válido para `trazo.duckdns.org`.
- [ ] PostgreSQL con base y usuario exclusivos.
- [ ] Archivo `/etc/trazo/trazo.env` protegido y completo.
- [ ] JWT signing key aleatoria y distinta de DietoExpress.
- [ ] Data Protection key ring persistente, protegido y respaldado.
- [ ] SMTP probado: verificación de correo y recuperación de contraseña.
- [ ] OAuth Web y Android configurados con las huellas correctas.
- [ ] Bootstrap superadmin eliminado después de la primera creación.
- [ ] API escuchando solo en `127.0.0.1:5001`.
- [ ] Nginx proxy a 5001; NAT limitado a 80/443.
- [ ] Health y readiness correctos desde localhost y desde HTTPS público.
- [ ] Copias de seguridad de PostgreSQL y Data Protection probadas.
- [ ] Importación RepDB aplazada hasta verificar la licencia.

Esta guía documenta la configuración; no implica que TRAZO esté listo para producción ni que se haya desplegado en el servidor.
