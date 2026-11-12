# TRAZO authentication

## Implemented authentication methods

- Email and password registration/login, with email verification required before password login.
- One-time email verification and password reset links; token values are stored only as SHA-256 hashes and are single-use.
- Google Sign-In through a Google OpenID Connect ID token. The API validates the token signature, issuer, audience and verified email with Google's published signing keys.
- TOTP second factor compatible with Google Authenticator, Aegis, 1Password and other RFC 6238 apps.
- Ten one-time recovery codes generated when TOTP is enabled.
- Short-lived JWT access tokens (15 minutes) and rotating opaque refresh tokens (30 days).
- Refresh-token revocation on logout and logout-all.
- Server-side roles: `user`, `admin`, `superadmin`.
- Authentication endpoints are rate-limited to 10 requests per minute per client IP.

Google accounts are not silently attached to an existing password account just because the email matches. The user must authenticate to the existing TRAZO account and explicitly link the Google identity. The Google email must be verified and match the TRAZO account email.

## API endpoints

| Method | Endpoint | Access |
| --- | --- | --- |
| POST | `/api/v1/auth/register` | Public |
| POST | `/api/v1/auth/login` | Public |
| POST | `/api/v1/auth/google` | Public |
| POST | `/api/v1/auth/email/verify` | Public, one-time token |
| POST | `/api/v1/auth/email/resend` | Public, rate-limited |
| POST | `/api/v1/auth/password/forgot` | Public, rate-limited |
| POST | `/api/v1/auth/password/reset` | Public, one-time token |
| POST | `/api/v1/auth/2fa/verify` | Public, short-lived challenge |
| POST | `/api/v1/auth/refresh` | Public, refresh token |
| POST | `/api/v1/auth/logout` | Authenticated |
| POST | `/api/v1/auth/logout-all` | Authenticated |
| GET | `/api/v1/auth/me` | Authenticated |
| POST | `/api/v1/auth/google/link` | Authenticated |
| POST | `/api/v1/auth/2fa/setup` | Authenticated |
| POST | `/api/v1/auth/2fa/enable` | Authenticated |
| POST | `/api/v1/auth/2fa/disable` | Authenticated |

### TOTP enrollment

1. Call `POST /2fa/setup` with a bearer access token. The API returns a Base32 secret and an `otpauth://` provisioning URI.
2. Add the account to an authenticator app.
3. Call `POST /2fa/enable` with the current six-digit code.
4. Store the ten recovery codes returned by the API. They are shown only once and each can be used once.

The authenticator secret is encrypted at rest using ASP.NET Core Data Protection. The API must keep its Data Protection key ring across deployments.

### Google OAuth configuration

Create OAuth 2.0 clients in Google Cloud Console:

- A **Web application** OAuth client. Its client ID is configured on the API and is the expected audience of the ID token sent by Android.
- An **Android** OAuth client registered with the Android package name and signing-certificate SHA-1 fingerprints (debug and Play App Signing as appropriate).

The Android app should use Credential Manager / Google Identity Services to obtain an ID token for the Web application client ID, then send that ID token to `POST /api/v1/auth/google`. Do not send Google access tokens as proof of identity and never embed a Google client secret in Android.

## Transactional email configuration

Configure an SMTP relay and the public account-action URL before allowing registration. Keep credentials in the service environment, never in source control:

```ini
Email__Smtp__Host=smtp.example.com
Email__Smtp__Port=587
Email__Smtp__EnableSsl=true
Email__Smtp__Username=YOUR_SMTP_USER
Email__Smtp__Password=YOUR_SMTP_PASSWORD
Email__From=no-reply@example.com
Email__ActionBaseUrl=trazo://account
```

The configured action base URL receives `/account/verify-email?token=...` and `/account/reset-password?token=...`. The Android client registers the `trazo://account` deep links and calls the API to consume the token. A web client must provide equivalent routes if it is also used.

## Server configuration

Supply these values through the TRAZO service environment file or secret manager. Do not commit real secrets.

```ini
Authentication__Jwt__Issuer=TRAZO
Authentication__Jwt__Audience=TRAZO-Clients
Authentication__Jwt__SigningKey=GENERATE_A_RANDOM_SECRET_AT_LEAST_32_BYTES
Authentication__Google__ClientId=YOUR_GOOGLE_WEB_CLIENT_ID.apps.googleusercontent.com
DataProtection__KeysPath=/var/lib/trazo/data-protection-keys
```

Generate a dedicated signing key, separate from DietoExpress:

```bash
openssl rand -base64 48
```

Create the Data Protection directory for the TRAZO service account and restrict access so only that account can read and write the key ring. Do not put this directory inside the application release directory or a temporary directory. Back it up securely; losing it makes existing encrypted authenticator secrets unreadable.

### Initial superadmin

For the first startup only, optionally supply:

```ini
BootstrapAdmin__Email=admin@example.com
BootstrapAdmin__Password=A_LONG_RANDOM_PASSWORD
BootstrapAdmin__DisplayName=TRAZO Administrator
```

If the users table is empty, startup creates this account with the `superadmin` role. Remove the bootstrap password from the service environment immediately after the first successful startup. The bootstrap values never change an existing user or promote an existing account.

## Security and launch checklist

- Passwords are hashed with ASP.NET Core's PBKDF2-based PasswordHasher; plaintext passwords are never persisted.
- Failed password attempts trigger a 15-minute account lockout after five consecutive failures.
- Refresh tokens are random, stored only as SHA-256 hashes, rotated on use and revocable.
- Two-factor login challenges expire after five minutes and are limited to five attempts.
- Recovery codes are stored as hashes and consumed once.
- JWT signing keys and Google client IDs are server configuration, not source code.
- The initial role is always assigned by the server; clients cannot choose or modify their role.
- The exercise catalog remains read-only and public. User-specific training endpoints must require authentication and enforce ownership checks.

**Not yet production-complete:** account deletion/export workflows, administrator account management UI, automated security integration tests, and complete Android deep-link / password-recovery screens still need to be added. Verify the SMTP relay and public action URLs before opening public registration. Do not treat the current authentication slice as a launch-ready identity product until these gaps and the Android end-to-end flows have been tested.
