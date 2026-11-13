# Identidad de marca TRAZO

Logotipo definitivo: **wordmark «Trazo» con la T en azul eléctrico y un trazo turquesa que la subraya**.
El texto está convertido a trazados, así que los SVG no dependen de ninguna fuente instalada.

## Paleta

| Uso | Color | Hex |
|---|---|---|
| T sobre fondo claro | Azul principal | `#0B6BFF` |
| T sobre fondo oscuro | Azul claro | `#3D8BFF` |
| Trazo (acento) | Turquesa | `#00D4B4` |
| Texto sobre fondo claro / fondos de icono | Azul oscuro | `#0B1F44` |
| Texto sobre fondo oscuro | Blanco | `#FFFFFF` |
| Claim sobre fondo oscuro | Azul pálido | `#8FB0E8` |
| Gris claro (fondos) | Gris claro | `#E6EEF8` |

## Archivos

| Archivo | Uso |
|---|---|
| `trazo-logo-light.svg` | Logotipo para fondos claros |
| `trazo-logo-dark.svg` | Logotipo para fondos oscuros |
| `trazo-monogram-light.svg` / `-dark.svg` | T con trazo, sin texto (avatares, sellos) |
| `trazo-app-icon.svg` | Icono de aplicación (1024 px, fondo `#0B1F44`, sin esquinas redondeadas) |
| `generated/` | Salida completa del generador (no se versiona) |

El monograma usa una T más gruesa (Manrope 800) que el wordmark (Manrope 600) para que
aguante tamaños pequeños; el trazo se calcula sobre cada T con las mismas proporciones.

## Reglas de uso

- Zona de respeto: al menos la altura de la T alrededor del logotipo.
- Tamaño mínimo: logotipo 96 px de ancho; monograma 16 px.
- No cambiar los colores, no inclinar ni deformar, no añadir sombras ni degradados, no
  mover el trazo ni separarlo de la T, no usar la T azul sobre fondos azules oscuros
  (usar la versión `dark`).
- Claim: «ENTRENA · MEJORA · AVANZA», solo sobre fondo oscuro y separado del logotipo.

## Dónde se usa cada activo

- **Admin Angular:** `admin/Trazo.Admin.Web/public/` (favicon, iconos PWA, logotipos). Se copian al
  build mediante `assets` en `angular.json`.
- **Android:** repositorio `Trazo.Android`: icono adaptativo con capa monocromo (temas de
  Android 13), logotipos como `VectorDrawable` y `store/google-play/` (icono 512 y gráfico
  destacado 1024×500).

## Regenerar

```bash
cd brand/tools
npm install
npm run generate     # escribe en brand/generated
```

El generador crea todos los SVG/PNG/XML a partir de `generate.mjs`. Después se copian los
resultados a los destinos de la tabla anterior.

## Licencia de la tipografía

Manrope © The Manrope Project Authors, licencia SIL Open Font License 1.1
(<https://openfontlicense.org>). Se permite su uso en logotipos y la conversión a trazados.
El texto de la licencia se incluye en el paquete npm `@fontsource/manrope`.
