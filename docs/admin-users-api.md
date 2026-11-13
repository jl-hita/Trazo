# Administration: user management API

All routes live under `/api/v1/admin/users` and require the `superadmin` role. They are used by the
Users page of the administration console.

| Method | Route | Purpose |
|---|---|---|
| GET | `/` | List users. Query: `q` (name or email), `role`, `status` (`active`, `disabled`, `unverified`), `sort` (`created`; default is email), `page`, `pageSize` (max 100) |
| GET | `/{id}` | Detail with sign-in methods, failed attempts and active sessions |
| PATCH | `/{id}` | Change `displayName` and/or `role` (`user`, `admin`, `superadmin`) |
| POST | `/{id}/disable` | Disable the account and revoke all its sessions |
| POST | `/{id}/enable` | Enable a disabled account |
| POST | `/{id}/revoke-sessions` | Revoke every refresh token of the user |
| POST | `/{id}/unlock` | Clear a lockout caused by failed sign-ins |
| POST | `/{id}/verify-email` | Mark the email as verified |

## Rules

- A superadmin cannot change their own role or disable their own account, and the last active superadmin
  can never be demoted or disabled.
- A disabled account cannot sign in (password, Google or 2FA) or refresh a session.
- Access tokens last 60 minutes, so every authenticated request also checks the account in the database:
  disabling a user or changing a role takes effect immediately, not when the token expires.
- Every administrative action is written to the service log (`Trazo.Admin` category) with the acting
  superadmin and the target user.
