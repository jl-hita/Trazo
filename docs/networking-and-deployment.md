# TRAZO API networking and deployment

## Ports and process binding

The API listener is configured independently from Nginx and the router. ASP.NET Core uses `ASPNETCORE_URLS`, so its port can be changed without changing application code.

Recommended same-host layout on weirdfish:

| Component | Listener | Exposure |
|---|---|---|
| DietoExpress API | `127.0.0.1:5000` | Loopback only |
| TRAZO API | `127.0.0.1:5001` | Loopback only |
| Nginx | `:80`, `:443` | Router forwards TCP 80/443 |
| PostgreSQL | Local/private listener | Never expose publicly |

TRAZO's planned port **5001 is internal, not a router/NAT port**. Nginx proxies requests to it locally.

Set this in TRAZO's eventual systemd environment file:

```ini
ASPNETCORE_URLS=http://127.0.0.1:5001
```

Change the port if required, then update the Nginx upstream to match. Do not bind to `0.0.0.0` unless direct LAN access is explicitly required.

For local Development, `launchSettings.json` uses HTTP 5001 and HTTPS 7001. Override the listener with `ASPNETCORE_URLS` when necessary.

## Router / NAT

For the recommended reverse-proxy architecture, forward only:

- TCP 80 -> weirdfish TCP 80.
- TCP 443 -> weirdfish TCP 443.

Do **not** forward TCP 5001. Nginx and TRAZO communicate over loopback.

Only if Nginx were deliberately bypassed would a direct API port need a NAT rule. That is not recommended: it exposes Kestrel directly and requires a separately managed HTTPS endpoint, certificate, firewall rule and hardening.

## Coexistence with DietoExpress

DietoExpress keeps its existing API listener on 5000; TRAZO uses 5001. Nginx receives public HTTPS and routes to each loopback upstream.

Preferred routing is by distinct hostnames if DNS and certificate issuance support them. If only `jlhitap.duckdns.org` is available, a path prefix such as `/trazo-api/` is an alternative, but requires coordinated Nginx routing and application path-base handling. Do not configure the final Nginx route until the public URL is selected.

## Android API address

Android accepts scheme, host and port as separate build properties:

```bash
gradle --no-daemon :app:assembleDebug \
  -PapiScheme=https \
  -PapiHost=jlhitap.duckdns.org \
  -PapiPort=443
```

For a non-standard HTTPS port, set e.g. `-PapiPort=8443`. For HTTP on a private LAN during debug, use `-PapiScheme=http` and the server's reachable LAN IP/port; HTTP is permitted only in debug builds. Release builds require HTTPS.

Alternatively, pass `-PapiBaseUrl=https://host:port/`, which takes precedence. Include the trailing slash. Do not use emulator-only `10.0.2.2` on a physical device.

## Deployment checklist

1. Provision TRAZO's separate PostgreSQL database and service account.
2. Set `ASPNETCORE_URLS=http://127.0.0.1:5001` in the TRAZO service environment.
3. Verify `curl http://127.0.0.1:5001/health` and `/health/ready`.
4. Configure Nginx to proxy the selected public hostname/path to `127.0.0.1:5001`.
5. Keep router forwarding TCP 80/443 only.
6. Build Android with the matching public HTTPS URL and port.
7. Verify login from a real device before making the endpoint the production default.

This guide does not change server, firewall, router, DNS or Nginx configuration.
