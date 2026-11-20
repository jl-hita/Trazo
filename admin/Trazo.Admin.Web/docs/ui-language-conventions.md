# Convenciones de idioma e interfaz

## Idioma por superficie

- El panel administrativo utiliza castellano de España (es-ES) como idioma único de interfaz.
- Las páginas públicas que ofrecen inglés deben seleccionar el idioma de forma explícita mediante ruta/diccionario. Actualmente Landing y las acciones de la lista de espera conservan sus variantes es y en; no se deben mezclar frases de ambos idiomas.
- Los nombres de producto y marcas (TRAZO, DietoExpress, Google Play) y los términos técnicos asentados (API, OAuth, SMTP, JSON, 2FA, RIR) conservan su grafía oficial. El texto explicativo alrededor se localiza.
- Los nombres de clases, funciones, propiedades, archivos, contratos API y valores de enum siguen en inglés.

## Formato regional

- LOCALE_ID: es-ES.
- Moneda predeterminada: EUR.
- Fechas visibles: dd/MM/yyyy.
- Horas: formato de 24 horas.
- Números y moneda: separadores y formato de es-ES.
- Para formatos creados con Intl, usar los helpers de core/ui-format.ts; para pipes Angular, respetar el locale global.

## Textos

- Los textos comunes de la interfaz se centralizan en core/ui-copy.ts.
- Los textos específicos de una funcionalidad se agrupan en diccionarios tipados por dominio cuando se migre esa pantalla.
- Evitar literales de presentación dispersos en templates y lógica de componentes.
- No traducir valores técnicos que formen parte de contratos o datos de catálogo; traducir sus etiquetas visibles.

## Terminología inicial

| Concepto | Texto de interfaz |
|---|---|
| Workspace | Espacio de trabajo |
| Overview | Resumen |
| Users | Usuarios |
| Exercises | Ejercicios |
| Subscriptions | Suscripciones |
| Logs | Registros |
| Error tickets | Incidencias |
| Waitlist | Lista de espera |
| Settings | Ajustes |
| Sign out | Cerrar sesión |
