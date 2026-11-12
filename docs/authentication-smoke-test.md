# Authentication smoke test

This checklist prepares a local/test API for the TRAZO Android client. It does not require the Angular administration shell and does not deploy anything.

## 1. Provision PostgreSQL

Create a dedicated database and login for TRAZO. The API applies EF Core migrations automatically during startup, so the configured database role needs permission to create and alter schema objects. Do not reuse the DietoExpress database or credentials.

Set the connection string through the environment:

- `ConnectionStrings__Trazo`

## 2. Configure required API security settings

Generate a unique signing key:

```bash
openssl rand -base64 48
```

Set these values in the API environment:

```ini
Authentication__Jwt__Issuer=TRAZO
Authentication__Jwt__Audience=TRAZO-Clients
Authentication__Jwt__SigningKey=REPLACE_WITH_RANDOM_SECRET
DataProtection__KeysPath=/var/lib/trazo/data-protection-keys
```

For non-Development environments, create the Data Protection directory before starting the service and grant read/write access only to the TRAZO service account. Keep it persistent across deployments and include it in secure backups.

For local Development, the Data Protection path may be omitted. Never use a short or reused JWT signing key.

## 3. Create a first test account

The easiest first login test is the one-time bootstrap superadmin. Before the first API startup, set:

```ini
BootstrapAdmin__Email=trazo-test@example.com
BootstrapAdmin__Password=REPLACE_WITH_A_LONG_RANDOM_PASSWORD
BootstrapAdmin__DisplayName=TRAZO Test Admin
```

The password must contain at least 12 characters, including uppercase, lowercase and a number. The account is created with a verified email and the `superadmin` role only when the users table is empty. Bootstrap settings never promote an existing account.

After the first successful startup, remove `BootstrapAdmin__Password` (and preferably the other bootstrap settings) from the environment. Do not enable public registration before the first administrator has been provisioned.

## 4. Optional email registration and password recovery

Email/password registration requires a working SMTP relay because accounts cannot sign in until their email is verified. Configure:

```ini
Email__Smtp__Host=smtp.example.com
Email__Smtp__Port=587
Email__Smtp__EnableSsl=true
Email__Smtp__Username=SMTP_USERNAME
Email__Smtp__Password=SMTP_PASSWORD
Email__From=no-reply@example.com
Email__ActionBaseUrl=trazo://account
```

The Android app handles the `trazo://account/account/verify-email` and `trazo://account/account/reset-password` links. If SMTP is not configured, use the verified bootstrap account for the first login smoke test; do not treat a failed email send as a successful registration.

## 5. Optional Google Sign-In

Create two OAuth clients in Google Cloud Console:

1. A Web application client. Set its client ID as `Authentication__Google__ClientId` on the API and pass the same ID to Android as `googleWebClientId`.
2. An Android client for package `com.trazo.android`, registered with the SHA-1 fingerprint of the certificate used to sign the installed APK.

The API validates the Google ID token against the Web client ID. Never put a Google client secret in the Android app. For a locally installed debug APK, register the debug signing fingerprint; Play App Signing has a different fingerprint.

## 6. Start and verify the API

Start the API with the environment above. On startup it applies migrations and seeds the curated exercise additions. Verify:

- `GET /health` returns HTTP 200.
- `GET /health/ready` returns HTTP 200 and reports the database as available.
- `POST /api/v1/auth/login` accepts the bootstrap email/password and returns access and refresh tokens.
- `GET /api/v1/auth/me` with the access token returns the account summary.

Example login request:

```bash
curl -i -X POST http://localhost:5000/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"trazo-test@example.com","password":"REPLACE_WITH_TEST_PASSWORD","deviceName":"Android smoke test"}'
```

Do not paste real passwords, access tokens, refresh tokens, SMTP credentials or Google tokens into issue trackers or logs.

## 7. Build and run Android

The Android debug build defaults to `http://10.0.2.2:5000/`, the development computer's localhost as seen from the Android emulator. Start the API on port 5000 on that computer.

For another test API, pass the URL when building:

```bash
gradle --no-daemon :app:assembleDebug -PapiBaseUrl=https://YOUR_TEST_API_HOST/ \
  -PgoogleWebClientId=YOUR_WEB_CLIENT_ID.apps.googleusercontent.com
```

The URL must end in a slash. A physical device cannot use `10.0.2.2`; use a reachable LAN address for temporary debug HTTP or a valid HTTPS host. Only the debug manifest permits cleartext HTTP. Release builds should use HTTPS.

## 8. End-to-end test order

1. Sign in with the bootstrap account.
2. Confirm the app shows the email, display name and server-assigned role.
3. Close and reopen the app; confirm refresh-token rotation restores the session.
4. Sign out; confirm the app returns to the login screen.
5. Sign in again, configure TOTP, add the provisioning URI/secret to an authenticator, and confirm with a current six-digit code.
6. Sign out and sign in again; confirm the app requests the TOTP challenge and accepts the current code or a one-time recovery code.
7. If Google OAuth is configured, test Google sign-in on a real device with Google Play services and the matching Android signing fingerprint.
8. If SMTP is configured, test registration, verification deep link, resend and password recovery.

The CI build is not a substitute for tests with a real Google OAuth project, SMTP relay, emulator/device and deployed API.
