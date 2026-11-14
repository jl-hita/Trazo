# TRAZO Admin Web

Angular administration frontend for the TRAZO strength-training platform.

The application is maintained in this repository alongside the ASP.NET Core API. It provides authenticated, role-aware operational tools and consumes the backend API; it is not a standalone static UI.

## Features

The administration area includes workflows for:

- User accounts and role management.
- Subscription and entitlement management.
- Exercise catalog management and import/export.
- Training configuration.
- Manual plan-review queue and conversations.
- Operational logs and system settings.

Available actions depend on the authenticated user's role. The backend remains authoritative for authorization and data validation.

## Requirements

- Node.js and npm compatible with the Angular workspace.
- A running TRAZO API with a configured development environment.

## Development

```bash
cd admin/Trazo.Admin.Web
npm install
npm start
```

The Angular development server runs at http://localhost:4200.

Configure the API URL and any frontend environment settings using the configuration already defined by the workspace. Do not place API secrets, Google client secrets or server credentials in frontend code: browser-delivered configuration is public.

## Validation

Run the available frontend checks from this directory:

```bash
npm run build
```

The repository's GitHub Actions workflow also builds the administration frontend. A successful frontend build does not replace testing role-specific workflows against a running API.

## Related documentation

- [Main repository and backend setup](../../README.md)
- [Backend configuration and deployment](../../docs/configuration-and-deployment.md)
- [Backend architecture](../../docs/architecture.md)
