# Comments from the app

Account → **Send feedback** in the Android app. Each comment is linked to the account, so it can be answered.

| Stored | Notes |
|---|---|
| Category (`bug`, `idea`, `other`) and message (up to 2,000 characters) | The app asks not to write health data |
| Screen the person came from, app version, phone model and Android version, language | Sent by the app; the screen text says so |

- `POST /api/v1/feedback` (signed in, 10 per hour and account). `clientRequestId` makes a retry store the comment once.
- `GET /api/v1/admin/feedback`, `PUT .../{id}/status`, `DELETE .../{id}`: **superadmin only**. Console: *Comentarios*.
- Comments are part of the data export and are deleted with the account. Withdrawing the health consent does not rewrite
  them, as with other free text. There is no automatic expiry; delete them from the console when they are dealt with.
