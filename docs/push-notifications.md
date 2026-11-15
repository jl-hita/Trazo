# Notificaciones push

Las notificaciones usan Firebase Cloud Messaging (FCM, API HTTP v1). Toda la configuración está en la base de datos
(tabla `app_settings`) y la edita el superadmin en **Settings → Push notifications**; nada de esto va en el `.env`.

## Configuración (una sola vez)

1. En la consola de Firebase, crear un proyecto y añadir una app Android con el paquete `com.trazo.android`.
2. En el JSON `google-services.json` que descarga Firebase están los valores del cliente:
   `project_info.project_id`, `client_info.mobilesdk_app_id`, `api_key.current_key` y (opcional) `project_info.project_number`.
3. En Project settings → Service accounts → *Generate new private key*: el JSON se pega en Settings (se guarda cifrado y
   no se vuelve a mostrar). Debe estar activa la API *Firebase Cloud Messaging API (V1)*.
4. Guardar y pulsar **Check connection** (autentica sin enviar nada) y **Send test to my devices**.

Vacío todo = push apagado. Los cambios se aplican al instante.

## Cómo funciona

- La app no lleva `google-services.json`: tras iniciar sesión pide `GET /api/v1/push/config` (valores públicos del cliente),
  arranca Firebase con ellos, y registra su token con `PUT /api/v1/devices`. Al cerrar sesión llama a
  `POST /api/v1/devices/unregister`.
- Un token pertenece a un solo usuario (si otra cuenta inicia sesión en el mismo móvil, lo hereda); máximo 10 por usuario.
  Se borran con la cuenta (`device_tokens`, ver `account-deletion.md`) y cuando FCM responde `UNREGISTERED`.
- Los mensajes son **de datos** (`title`, `body`, `kind`), no de notificación: la app dibuja la notificación, también con la
  app cerrada, y traduce el texto según `kind` (`review-reply`, `review-closed`, `review-new`, `review-message`,
  `announcement`, `test`).
- **Privacidad:** el texto nunca incluye datos de salud ni el contenido de los mensajes, solo avisa de que hay algo que leer.

## Eventos que envían push

| Evento | Destinatario | `kind` |
| --- | --- | --- |
| El equipo responde en una revisión manual | El usuario | `review-reply` |
| El equipo cierra la revisión | El usuario | `review-closed` |
| Petición nueva / mensaje del usuario | Admins (el asignado, si lo hay) | `review-new` / `review-message` |
| Anuncio desde Settings | Todos los dispositivos | `announcement` |

Es un canal adicional al email y a la app: si push no está configurado o falla, solo se registra y lo demás sigue funcionando.
