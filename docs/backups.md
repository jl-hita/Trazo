# Backups cifrados a Google Drive (recuperación ante desastres)

Copia diaria de **toda** la instalación de TRAZO, no por usuario. Si el servidor se pierde, con la copia y la clave privada
se reconstruye todo.

## Qué contiene cada copia

Un `trazo-AAAAMMDDTHHMMSSZ.tar.age` con:

- `database.dump`: `pg_dump -Fc` de la base `trazo` (incluye usuarios, planes, logs, tickets y los ajustes del asistente).
- `files/var/lib/trazo/data-protection-keys`: sin ellas se pierden los secretos 2FA.
- `files/etc/trazo/trazo.env`: JWT, conexión a BD y demás secretos del servidor.
- `MANIFEST.txt`.

`/var/lib/trazo/r8-mappings` **no** se incluye por defecto: son cientos de MB que casi no cambian y inflarían cada copia
diaria. Sin ellos solo se pierde poder desofuscar tickets Android antiguos. Para incluirlos, define en `backup.env`
`EXTRA_PATHS="/var/lib/trazo/data-protection-keys /var/lib/trazo/r8-mappings /etc/trazo/trazo.env"`.

## Cómo está protegido

- Cifrado con [age](https://age-encryption.org) usando **solo la clave pública** (`AGE_RECIPIENT`). El servidor no puede
  descifrar sus propias copias: si roban el servidor o la cuenta de Drive, las copias siguen siendo ilegibles.
- La **clave privada** vive fuera del servidor (gestor de contraseñas + copia en papel en otro sitio). **Sin ella las copias
  no sirven y no hay forma de recuperarlas.**
- Drive: cuenta dedicada solo a copias, con verificación en dos pasos, y `rclone` con el alcance `drive.file` (solo ve lo
  que él mismo sube). Credenciales en `/etc/trazo/rclone.conf` (0600, root).
- Retención: las 14 últimas copias diarias más los 8 últimos domingos (`KEEP_DAILY`, `KEEP_WEEKLY`).

## Puesta en marcha (una sola vez, en weirdfish)

1. Instala y copia los ficheros:

   ```bash
   sudo bash deploy/scripts/install-trazo-backup.sh
   ```

2. Genera el par de claves **en tu PC** (no en el servidor) y guarda la privada:

   ```bash
   age-keygen -o trazo-backup.key        # imprime "Public key: age1..."
   ```

3. Configura rclone contra la cuenta dedicada. En el servidor no hay navegador: usa `rclone authorize "drive" "{...}"`
   desde tu PC o `ssh -L 53682:localhost:53682`.

   ```bash
   sudo RCLONE_CONFIG=/etc/trazo/rclone.conf rclone config   # n) nuevo, nombre trazo-drive, tipo drive, scope drive.file
   sudo chmod 600 /etc/trazo/rclone.conf
   ```

4. Rellena `/etc/trazo/backup.env`: `AGE_RECIPIENT=age1...` y `RCLONE_REMOTE=trazo-drive:trazo-backups`.

5. Prueba manual y activa los timers (copia diaria ~03:30 con `Persistent=true`, y chequeo diario a las 09:00):

   ```bash
   sudo systemctl start trazo-backup.service && journalctl -u trazo-backup -n 50
   sudo systemctl enable --now trazo-backup.timer trazo-backup-check.timer
   ```

## Restaurar

Prueba mensual recomendada, en una base de prueba (no toca producción):

```bash
sudo trazo-restore --identity ./trazo-backup.key --from-drive latest
```

Desastre real en un servidor nuevo (tras `bootstrap-trazo-server.sh` y crear el rol `trazo_app`, con el servicio parado):

```bash
sudo systemctl stop trazo
sudo trazo-restore --identity ./trazo-backup.key --from-drive latest --workdir /var/tmp/r --db trazo --production
sudo cp -a /var/tmp/r/payload/files/var/lib/trazo/. /var/lib/trazo/ && sudo chown -R trazo:trazo /var/lib/trazo
sudo install -o root -g trazo -m 0640 /var/tmp/r/payload/files/etc/trazo/trazo.env /etc/trazo/trazo.env
sudo systemctl start trazo
```

Si el servidor nuevo aún no tiene `backup.env`/`rclone.conf`, descarga el `.tar.age` a mano desde Drive y usa `--file`.
Borra `/var/tmp/r` al terminar: contiene secretos en claro.

## Vigilancia

- El backup falla (exit ≠ 0) si el volcado, el cifrado o la subida fallan; entonces `trazo-backup-failed.service` avisa.
- `trazo-backup-check.timer` (diario, 09:00) comprueba en Drive que la última copia tenga menos de `MAX_AGE_HOURS`
  (36 por defecto). Si no la hay o es más antigua, la unidad queda en `failed` y se avisa. Cubre también el caso en que el
  timer no llegue a ejecutarse o Drive deje de aceptar subidas.
- Un aviso siempre escribe en el journal con prioridad error (`journalctl -t trazo-backup-check -p err`). Para recibirlo
  fuera del servidor, define en `backup.env` `NOTIFY_URL` con una URL a la que se haga POST con el mensaje; sirve
  [ntfy.sh](https://ntfy.sh) con un tema largo y secreto (instala la app y suscríbete a ese tema).
- **Notificación en el móvil (la app de Trazo).** En «Copias de seguridad» → «Avisos por notificación» se elige, en un
  desplegable, qué cuenta de administración recibe el aviso (se guarda su id en la base de datos, no en el `.env`). Cada
  30 minutos la API lee los mismos ficheros de estado que el panel y, si la salud es **error**, envía un push a esa
  cuenta: una vez, y otra cada 24 h mientras dure; cuando vuelve a estar bien envía un último aviso. El texto solo
  dice el estado de las copias. Requisitos: Firebase configurado (Ajustes → Notificaciones) y que esa cuenta haya abierto
  la app con su sesión y concedido el permiso de notificaciones (el desplegable muestra cuántos móviles tiene cada
  cuenta; hay un botón de prueba). Solo se pueden elegir cuentas `admin`/`superadmin` activas. Depende de que la API
  esté en marcha: si el servidor entero cae, no hay aviso, y para eso sirve `NOTIFY_URL`.
- Activación del chequeo (también en instalaciones anteriores a esta versión): vuelve a ejecutar
  `sudo bash deploy/scripts/install-trazo-backup.sh` y luego `sudo systemctl enable --now trazo-backup-check.timer`.
- Estado: `systemctl list-timers 'trazo-backup*'` y `sudo trazo-backup-check`.

## Panel en la consola de administración

**Copias de seguridad** (solo superadmin, solo lectura) muestra la salud (correcto, atención, error o sin datos), la
última copia con su antigüedad y tamaño, cómo terminó la última ejecución (con su error si falló) y la lista de copias
en Drive, marcando las de domingo como semanales.

La API no habla con Drive ni tiene credenciales. Lee dos ficheros pequeños que dejan los scripts en `/var/lib/trazo`
(0640, `trazo:trazo`; ruta cambiable con `Backups__StatusPath`):

| Fichero | Lo escribe | Contenido |
|---|---|---|
| `backup-last-run.json` | `trazo-backup` (éxito o fallo, con el paso en que falló) | `ok`, `startedAt`, `finishedAt`, `copy`, `error` |
| `backup-status.json` | `trazo-backup-check` (tras cada copia y a diario) | `checkedAt`, `remote`, `maxAgeHours`, lista de copias (`rclone lsjson`) |

`GET /api/v1/admin/backups` calcula la salud al responder: **error** si la última ejecución falló después de la última
copia, si no hay copias o si la última supera `maxAgeHours`; **atención** si la comprobación diaria lleva más de ~50 h
sin ejecutarse; **sin datos** mientras no exista ninguno de los dos ficheros (instalación nueva o sin copias aún).
Tras actualizar los scripts, el panel se rellena con la siguiente copia o con `sudo trazo-backup-check`.
