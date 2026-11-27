# Consentimiento de investigación (P5-2)

Decisión del titular (2026-11-27): la comparación entre versiones del motor y la exportación de investigación usan **solo** los
datos de quien lo consienta; el interruptor es opcional, apagado por defecto y revocable; los resultados se muestran
agregados; **el texto legal lo aprueba el titular antes de publicarlo**.

## Estado

| Pieza | Estado |
|---|---|
| Exportación de Trazo Intelligence filtrada por consentimiento | Hecho |
| Comparación por versión del motor (solo con consentimiento, mínimo 5 personas por grupo) en `GET /admin/intelligence/summary` | Hecho; sin pantalla en la consola |
| Interruptor en la app (Cuenta, apagado por defecto) | Hecho (Trazo.Android#117); sin probar en un dispositivo |
| Política de privacidad y consentimiento de salud de la app | Actualizados con la redacción aprobada por el titular (2026-11-27) |

El consentimiento es `users.ResearchConsent` (`PUT /api/v1/account/research-consent`). Antes solo decidía si las filas
anónimas conservadas al borrar la cuenta podían agruparse ([account-deletion.md](account-deletion.md)); el texto debe cubrir
ambas cosas.

El resto del resumen de Intelligence (recuentos de planes, entrenos y adaptaciones) sigue siendo de todos los usuarios,
agregado, como antes: es seguimiento del servicio, no la comparación de la que trata esta decisión. Conviene que la revisión
legal lo confirme.

## Texto aprobado por el titular (2026-11-27, con el motivo de archivado)

**Español**

> **Ayudar a mejorar TRAZO (opcional)**
> Si lo activas, TRAZO usará tus datos de entrenamiento (planes, series, cargas, repeticiones, esfuerzo, dolor y molestias que registres y, si lo indicas, el motivo por el que archivas un plan; sin tu nombre, correo ni notas) para comparar
> versiones del motor y estudiar cómo mejorar los planes. Los datos
> se sustituyen por claves que no identifican a nadie fuera de cada análisis y los resultados se miran agrupados. No se
> venden ni se ceden. Si borras tu cuenta, las cifras anónimas por músculo que se conservan podrán agruparse. Puedes
> desactivarlo cuando quieras: dejarás de incluirte en los análisis posteriores. Los motivos de archivado que hubieras indicado se borran. No cambia nada de lo que puedes usar.

**English**

> **Help improve TRAZO (optional)**
> If you turn this on, TRAZO will use your training data (plans, sets, loads, repetitions, effort, pain and discomfort you log and, if you say it, why you archive a plan;
> without your name, email or notes) to compare engine versions and study how to make plans better. The data is replaced with
> keys that identify nobody outside each analysis, and results are read in groups. It is not sold or shared. If you delete
> your account, the anonymous per-muscle figures that are kept may be grouped. You can turn it off at any time: you will be
> left out of later analyses. Any reasons you gave for archiving plans are erased. It changes nothing you can use.

Los textos de los otros 27 idiomas de la app son traducciones de estos, sin revisión nativa.

El texto cubre el **dolor** y las **molestias** registrados, no la enfermedad: la exportación sustituye ese
motivo (de una sesión o de una serie) por «otro».

El consentimiento de salud de la app dice que el dolor y las molestias se usan además para investigar si se activa este.
El titular aprobó nombrar el dolor el 2026-11-27, porque la exportación lo incluye.

## Motivo de archivado de un plan

Al archivar un plan, la app pregunta el motivo **solo** a quien tiene este consentimiento; el servidor lo vuelve a comprobar y no
guarda nada sin él. Es opcional, de lista cerrada y sin texto libre: `PlanCompleted`, `WantNewPlan`, `NoTime`, `TooDemanding`,
`TooEasy`, `Discomfort`, `GoalsChanged`, `NotConvinced`, `Other`. Se guarda en `training_plans."ArchiveReason"` (nulo en los planes
anteriores y cuando no se indica) y sale en la tabla de planes de la exportación (`research.json` y `plans.csv`), con los planes
creados en el periodo y con los archivados en él con motivo. Es una opinión de la persona, no una evaluación del motor.

- `Discomfort` es dato de salud: además exige el consentimiento de salud. La app no ofrece esa opción sin él, el servidor la guarda
  como `Other` y al retirarlo se convierte en `Other`.
- Al retirar el consentimiento de investigación, los motivos se borran de sus planes: solo se recogen para investigar. También se
  borran al eliminar la cuenta, aparecen en la exportación de datos de la propia persona y no pasan a las cifras anónimas.
- El titular aprobó el 2026-11-27 nombrar el motivo en el texto del consentimiento y en la política, y borrarlo al retirarlo. Quien
  aceptó el texto anterior no ha aceptado esa frase: queda por confirmar en la revisión legal si hace falta pedirlo de nuevo.

Pendiente: el plazo de conservación de las exportaciones (la política no lo dice) y confirmar la base jurídica con quien revise
la parte legal.
