# Consentimiento de investigación (P5-2)

Decisión del titular (2026-11-27): la comparación entre versiones del motor y la exportación de investigación usan **solo** los
datos de quien lo consienta; el interruptor es opcional, apagado por defecto y revocable; los resultados se muestran
agregados; **el texto legal lo aprueba el titular antes de publicarlo**.

## Estado

| Pieza | Estado |
|---|---|
| Exportación de Trazo Intelligence filtrada por consentimiento | Hecho |
| Comparación por versión del motor (solo con consentimiento, mínimo 5 personas por grupo) en `GET /admin/intelligence/summary` | Hecho; sin pantalla en la consola |
| Interruptor en la app | **No hecho**: se escribirá con el texto aprobado, en los 29 idiomas |
| Política de privacidad | Pendiente de actualizar con el texto aprobado |

El consentimiento es `users.ResearchConsent` (`PUT /api/v1/account/research-consent`). Antes solo decidía si las filas
anónimas conservadas al borrar la cuenta podían agruparse ([account-deletion.md](account-deletion.md)); el texto debe cubrir
ambas cosas. Hasta que exista el interruptor nadie lo tiene activado desde la app, así que la exportación y la comparación
salen vacías.

El resto del resumen de Intelligence (recuentos de planes, entrenos y adaptaciones) sigue siendo de todos los usuarios,
agregado, como antes: es seguimiento del servicio, no la comparación de la que trata esta decisión. Conviene que la revisión
legal lo confirme.

## Texto propuesto (pendiente de aprobación)

**Español**

> **Ayudar a mejorar TRAZO (opcional)**
> Si lo activas, TRAZO usará tus datos de entrenamiento (planes, series, cargas, repeticiones, esfuerzo y molestias que
> registres, sin tu nombre, correo ni notas) para comparar versiones del motor y estudiar cómo mejorar los planes. Los datos
> se sustituyen por claves que no identifican a nadie fuera de cada análisis y los resultados se miran agrupados. No se
> venden ni se ceden. Si borras tu cuenta, las cifras anónimas por músculo que se conservan podrán agruparse. Puedes
> desactivarlo cuando quieras: dejarás de incluirse en los análisis posteriores. No cambia nada de lo que puedes usar.

**English**

> **Help improve TRAZO (optional)**
> If you turn this on, TRAZO will use your training data (plans, sets, loads, repetitions, effort and discomfort you log,
> without your name, email or notes) to compare engine versions and study how to make plans better. The data is replaced with
> keys that identify nobody outside each analysis, and results are read in groups. It is not sold or shared. If you delete
> your account, the anonymous per-muscle figures that are kept may be grouped. You can turn it off at any time: you will be
> left out of later analyses. It changes nothing you can use.

Puntos a confirmar con quien revise la parte legal: que las molestias son datos de salud (el texto o el consentimiento de
salud deben cubrir su uso para investigación), la base jurídica y el plazo de conservación de las exportaciones.
