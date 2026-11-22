# Backups cifrados a Google Drive (recuperación ante desastres)

Copia diaria de **toda** la instalación de TRAZO, no por usuario. Si el servidor se pierde, con la copia y la clave privada
se reconstruye todo.

## Qué contiene cada copia

Un `trazo-AAAAMMDDTHHMMSSZ.tar.age` con:

- `database.dump`: `pg_dump -Fc` de la base `trazo` (incluye usuarios, planes, logs, tickets y los ajustes del asistente).
- `files/var/lib/trazo/data-protection-keys`: sin ellas se pierden los secretos 2FA.
- `files/var/lib/trazo/r8-mappings`: mappings para desofuscar tickets Android.
- `files/etc/trazo/trazo.env`: JWT, conexión a BD y demás secretos del servidor.
- `MANIFEST.txt`.

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

5. Prueba manual y activa el timer (diario ~03:30, con `Persistent=true`):

   ```bash
   sudo systemctl start trazo-backup.service && journalctl -u trazo-backup -n 50
   sudo systemctl enable --now trazo-backup.timer
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

El servicio falla (exit ≠ 0) si el volcado, el cifrado o la subida fallan; mira `systemctl status trazo-backup` o
`systemctl list-timers trazo-backup.timer`. Pendiente: aviso automático cuando no haya copia reciente en Drive.
