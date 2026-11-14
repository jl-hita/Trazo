# Configuración del backend: qué vive en el `.env` y qué en la base de datos

Este documento inventaría **toda** la configuración que lee el backend de TRAZO y decide dónde vive. Complementa a [configuration-and-deployment.md](configuration-and-deployment.md) (cómo montar el servidor).

## Regla de reparto

> Solo pasa a la base de datos lo que **no** hace falta para arrancar el backend ni para iniciar sesión.

Si una configuración crítica se pudiera cambiar desde Ajustes y se guardara un valor erróneo, el superadmin no podría volver a entrar a corregirlo. Por eso esas claves siguen en `/etc/trazo/trazo.env` (cargado por systemd). Todo lo demás se edita en **Ajustes** (solo superadmin), con efecto inmediato y sin reiniciar.

### Prioridad de resolución

```
valor válido en la base de datos  >  variable de entorno / appsettings  >  valor por defecto
```

- **Retrocompatible**: una variable de entorno existente sigue funcionando mientras la base de datos no tenga valor. Nada del `.env` actual deja de aplicarse al desplegar esta versión.
- Un valor **inválido** en la base de datos (fuera de rango, URL mal formada, secreto que no se puede descifrar…) **nunca impide arrancar**: se ignora, se aplica el valor siguiente de la cadena y se registra un aviso (una vez, no cada refresco) en los logs.
- Guardar un campo vacío elimina el valor de la base de datos y devuelve el control al entorno/defecto.
- Los secretos se guardan **cifrados** con Data Protection (misma `IsSecret` que la contraseña SMTP), no se devuelven nunca por la API y el formulario solo indica si hay uno guardado y de dónde viene (`database` / `environment` / `none`).
- Cada guardado queda en el log de auditoría (categoría `Trazo.Admin`, visible en la sección Logs): quién (id del superadmin) y qué claves cambiaron, con valor antiguo → nuevo para los no secretos y solo «replaced/cleared» para los secretos.

### Cómo se aplica sin reiniciar

| Mecanismo | Qué lo usa |
| --- | --- |
| Lectura de la base de datos en cada petición (`IRuntimeSettings`) | Google ClientId, SMTP, Play Billing, DietoExpress (endpoint y clave) |
| Caché en memoria `IRuntimeOptions`: se recarga tras cada guardado y cada 30 s | Orígenes CORS adicionales, límites de peticiones, retención de logs |
| Capa de configuración reactiva (`RuntimeOverridesConfigurationSource`), añadida la última | Nivel de log por defecto (el filtro de `ILogger` observa los cambios) |

Si la base de datos no responde, se conservan los últimos valores buenos (al arrancar: entorno/defectos) y se avisa en el log. Los ajustes nunca son necesarios para arrancar ni para iniciar sesión.

## Inventario

Leyenda de «Crítica»: **Sí** = sin ella el backend no arranca o nadie puede iniciar sesión (o un error equivale a un bloqueo).

### Se quedan en el `.env` (o en el código) — críticas

| Clave | Para qué sirve | Crítica arranque/login | Hoy | Pasa a |
| --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Entorno (`Production`) | Sí: cambia validaciones, HTTPS y OpenAPI | `.env` | Se queda en `.env` |
| `ASPNETCORE_URLS` | Dirección de escucha (`127.0.0.1:5001`) | Sí: Nginx apunta ahí | `.env` | Se queda en `.env` |
| `AllowedHosts` | Hosts HTTP aceptados | Sí: un valor erróneo devuelve 400 a todas las peticiones, login incluido | `.env` / `appsettings.json` (`*`) | Se queda en `.env` |
| `ConnectionStrings__Trazo` | Conexión a PostgreSQL | Sí: sin ella no arranca; además es donde viven los ajustes | `.env` | Se queda en `.env` |
| `DataProtection__KeysPath` | Directorio persistente de claves de Data Protection | Sí: la API se niega a arrancar sin él; sin las claves no se descifran secretos ni TOTP | `.env` | Se queda en `.env` |
| `Authentication__Jwt__Issuer` / `Audience` | Emisor y audiencia del JWT | Sí: la API valida cada token contra ellos | `.env` / `appsettings.json` | Se quedan en `.env` |
| `Authentication__Jwt__SigningKey` | Clave HMAC del JWT (≥ 32 bytes) | Sí: arranque y todo el login | `.env` | Se queda en `.env` |
| `Cors__AllowedOrigins__N` | Orígenes CORS base (el del admin web) | Sí en la práctica: si el admin se sirve desde otro origen, un error cierra la pantalla de Ajustes | `.env` / `appsettings.json` | Se queda en `.env`; en Ajustes solo se **añaden** más |
| `BootstrapAdmin__Email/Password/DisplayName` | Crea el primer superadmin al arrancar (alternativa al asistente) | Sí: es el acceso inicial | `.env` (opcional) | Se queda en `.env` (se retiran tras el primer arranque) |
| `Logging__Database__Enabled` | Activa/desactiva el guardado de logs en BD | No, pero se lee al construir el contenedor: exige reiniciar | `.env` | Se queda en `.env` (aviso de reinicio en Ajustes) |
| `Logging__LogLevel__<categoría>` (p. ej. `Microsoft.AspNetCore`) | Niveles por categoría | No | `appsettings.json` / `.env` | Se quedan en appsettings/`.env`: solo se expone el nivel por defecto |
| Tiempo de vida del token de acceso (60 min) y de refresco (30 días), `ClockSkew` 30 s | Validez de sesiones | Sí (login) | Constantes en código (`AuthTokenService`) | Se quedan en código: parámetros de validez del JWT |
| Bloqueo tras 5 intentos fallidos (15 min) | Protección de fuerza bruta | Sí (login) | Constante en código | Se queda en código |
| Límite de peticiones `auth` (10/min por IP) | Protege el inicio de sesión | Sí (login) | Constante en código | Se queda en código, **no** editable: un valor demasiado bajo bloquearía el acceso |
| Caducidad de los enlaces de verificación (24 h) y de recuperación de contraseña (30 min), reto 2FA (5 min) | Flujos de cuenta | Sí (recuperación de acceso) | Constantes en código | Se quedan en código |
| `ForwardedHeaders` (proxies conocidos: loopback) | IP real tras Nginx; los límites por IP dependen de ello | Sí: un error rompe el rate limiting y HTTPS | Código | Se queda en código |
| Tamaño máximo de importación (512 MB), timeouts HTTP (10 s / 5 min) | Salvaguardas internas | No | Constantes en código | Se quedan en código: son límites de seguridad, no ajustes de operación |
| `TRAZO_VERSION`, rutas de `/opt/trazo`, `/etc/trazo`, `PUBLIC_HOST` | Solo los scripts de despliegue (`trazo-deploy-root.sh`) | — | Scripts | Fuera del alcance del backend; no se tocan |

### Ya estaban en Ajustes (la base de datos gana al `.env`)

| Clave | Para qué sirve | Crítica | Dónde |
| --- | --- | --- | --- |
| `Authentication:Google:ClientId` | Inicio de sesión con Google | No: el acceso con contraseña funciona sin él | Asistente + Ajustes |
| `Email:Smtp:*`, `Email:From`, `Email:ActionBaseUrl` | Correo (verificación, recuperación) | No para arrancar; sin SMTP no hay correos de cuenta | Asistente + Ajustes (con prueba de envío) |
| `Billing:Enforced`, `Billing:Play:*` | Suscripciones y Google Play (secretos cifrados) | No | Ajustes (con prueba de conexión) |

### Nuevos en Ajustes (esta versión)

| Clave en BD (y en entorno) | Para qué sirve | Crítica | Hoy | Validación | Defecto |
| --- | --- | --- | --- | --- | --- |
| `Cors:AdditionalOrigins` (no tiene variable equivalente de uso normal) | Orígenes CORS **adicionales** | No: se suman a los de `Cors__AllowedOrigins__N`, que siempre siguen permitidos | `.env` (solo los base) | Cada origen `https://host[:puerto]`, sin ruta/usuario/query; `http` solo `localhost`; máx. 20; una entrada mala invalida la lista | vacío |
| `DietoExpressTransfer:Endpoint` (`DietoExpressTransfer__Endpoint`) | Destino del envío de ejercicios a DietoExpress | No | `.env` | URL absoluta `https` (o `http` loopback), sin usuario/query/fragmento, ≤ 500 | vacío (transferencia desactivada) |
| `DietoExpressImport:ApiKey` (`DietoExpressImport__ApiKey`) | Secreto compartido: autentica la importación entrante (`X-Trazo-Import-Key`) y el envío saliente | No | `.env` (generada por `bootstrap-trazo-server.sh`) | 16–200 caracteres, sin espacios. **Cifrada** en BD | sin clave: importación y envío desactivados |
| `RateLimits:Setup:PerMinute` | Límite del asistente y de las pantallas de Ajustes, por IP | No | Código (30) | 10–600 | 30 |
| `RateLimits:SetupTest:PerMinute` | Límite de las pruebas de conexión (SMTP, Google, Play, DietoExpress) | No | Código (5) | 3–60 | 5 |
| `RateLimits:Subscription:PerMinute` | Límite de los endpoints de suscripción, por usuario | No | Código (30) | 5–600 | 30 |
| `Logging:LogLevel:Default` | Nivel mínimo global de log | No | `.env` / `appsettings.json` (`Information`) | `Debug`, `Information`, `Warning`, `Error`, `Critical` | `Information` |
| `Logging:Database:RetentionDays` | Días que se conservan los logs en BD (0 = siempre) | No | `.env` | 0–3650 | 30 |

Los mínimos de los límites impiden dejar el sistema inutilizable (p. ej. un `Setup` de 1/min haría inservible la propia pantalla de Ajustes).

## Decisiones dudosas

- **CORS: aditivo, no sustitutivo.** Si la base de datos pudiera sustituir a `Cors__AllowedOrigins__N` y el admin se sirviera desde otro origen, un valor erróneo cerraría Ajustes. Por eso el `.env` fija la base y Ajustes solo añade. (Detrás de Nginx en el mismo origen, el admin ni siquiera usa CORS; esto sirve para otros orígenes, p. ej. un entorno de pruebas.)
- **Límites: se edita todo menos `auth`.** `setup`, `setup-test` y `subscription` no afectan al inicio de sesión y tienen mínimos seguros. `auth` protege el login y queda fijo.
- **Nivel de log: no se permite `None` ni `Trace`.** `None` dejaría la API muda (incluidos los diagnósticos para recuperarse de un error) y `Trace` llenaría la base de datos. Al elegir `Warning` o superior se mantiene `Information` solo para la categoría `Trazo.Admin`, para no perder la auditoría.
- **La clave de DietoExpress se comparte entre entrada y salida** (como ya hacía el código). Si la guardas en BD, tiene prioridad sobre la variable de entorno en ambos sentidos; debe coincidir con `TrazoTransfer__ApiKey` en DietoExpress.
- **La prueba de DietoExpress solo comprueba alcanzabilidad** (HTTP GET a la dirección, 10 s). No envía la clave a ningún sitio: el receptor solo la valida al recibir una biblioteca. Una URL con ruta (`https://host/sub`) ahora se respeta al construir la ruta de importación; antes una URL sin barra final perdía el último segmento.
- **Tokens, bloqueos y caducidades siguen en código.** El encargo los lista como candidatos, pero son parámetros de validez de sesión/recuperación de acceso y la regla clave los deja fuera.
- **Asistente de primer arranque: sin cambios.** Ninguno de los nuevos ajustes es necesario en el primer arranque (el `.env` generado ya trae CORS, la clave de importación y el nivel de log), así que no se añaden pasos ni obligatoriedad. Se editan después en Ajustes.
- **Pantalla de Ajustes:** la sección «Solo en el archivo de entorno» recuerda qué exige editar `/etc/trazo/trazo.env` y reiniciar.

## Limpieza opcional del `.env` (paso manual, no automático)

Nada de esto es obligatorio: las variables actuales siguen valiendo. Cuando hayas guardado el valor en Ajustes y comprobado que funciona, puedes retirar del `.env`:
`DietoExpressImport__ApiKey`, `DietoExpressTransfer__Endpoint`, `Logging__LogLevel__Default`, `Logging__Database__RetentionDays`. **No** retires nada de la tabla «Se quedan en el `.env`». Tras editar el `.env` hay que reiniciar el servicio. Si quitas una variable sin haber guardado antes el valor en Ajustes, se pierde esa función (volverá al valor por defecto).

`deploy/scripts/bootstrap-trazo-server.sh` sigue generando `DietoExpressImport__ApiKey` y `Logging__LogLevel__Default` en instalaciones nuevas; seguirá funcionando como valor de respaldo. Si se prefiere que las instalaciones nuevas no lo escriban, hay que editar el script (no se ha tocado).

## API (superadmin)

| Método y ruta | Descripción |
| --- | --- |
| `GET /api/v1/admin/settings/operations` | Valores efectivos, orígenes CORS del entorno (solo lectura) y origen de la clave DietoExpress. Nunca devuelve la clave |
| `PUT /api/v1/admin/settings/operations` | Valida todo y guarda atómicamente; `dietoExpressApiKey`: `null` conserva, `""` borra. 400 con los errores por campo si algo es inválido |
| `POST /api/v1/admin/settings/operations/test/dietoexpress` | Comprueba que la dirección responde |
