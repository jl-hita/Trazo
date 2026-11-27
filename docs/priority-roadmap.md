# Roadmap prioritario: producto y motor de hipertrofia

Documento de seguimiento del roadmap adicional aprobado el 2026-11-26. **Tiene precedencia** sobre las prioridades de producto del
issue [#48](https://github.com/jl-hita/Trazo/issues/48), que sigue siendo válido para lanzamiento, cobros y operación (ver §2).
Se actualiza en cada PR. Una tarea solo es «Hecha» si está integrada en `main`, probada de extremo a extremo y cumple sus
criterios; lo verificado solo en CI se indica así.

Estados: `Pendiente`, `En curso`, `Parcial`, `Hecho`, `Bloqueado (decisión)`.

- [1. Estado actual](#1-estado-actual-verificado-el-2026-11-26)
- [2. Convivencia con el roadmap anterior](#2-convivencia-con-el-roadmap-anterior)
- [3. Recomendaciones de arquitectura](#3-recomendaciones-de-arquitectura)
- [4. Decisiones que requieren aprobación](#4-decisiones-que-requieren-aprobación)
- [5. Riesgos](#5-riesgos)
- [6. Secuencia de PR y seguimiento](#6-secuencia-de-pr-y-seguimiento)
- [7. Evidencia consultada](#7-evidencia-consultada)

## 1. Estado actual (verificado el 2026-11-26)

Revisado sobre `main` de backend (`3bfb339`) y Android (`af6b847`) y sobre la instantánea de producción del catálogo (rama
`catalog-audit`, generada 2026-11-26 17:18 UTC). Revisión de código y datos, dirigida a los puntos del roadmap (no línea a
línea de todo el repositorio). No se ha ejecutado la app ni las pruebas en esta fase. La auditoría previa del catálogo está en
[exercise-catalog-audit.md](exercise-catalog-audit.md).

### P0 · Catálogo y metadatos — Parcial

| Hallazgo | Detalle |
|---|---|
| **Producción no coincide con el seed** | 150 ejercicios en ambos, pero 12 difieren en `minimumExperience` (press de banca, press con mancuernas, hack squat y otros siguen `Intermediate`: los principiantes no los reciben), 19 en `selectionPriority` y `standing-calf-raise` sigue activo; además nombres (40) e instrucciones (27). Músculos y equipamiento requerido coinciden. 148 filas tienen `UpdatedAt != CreatedAt`, así que el seeder las trata como editadas y no las actualiza. La causa de esas marcas no está verificada. |
| Estímulo muscular en código | `TrainingExerciseStimulus` (`TrainingPrescription.cs`) decide por id qué secundarios cuentan (77 con estímulo, 73 sin él). Un ejercicio nuevo cuenta solo sus primarios, sin aviso. |
| Inferencia por id en el motor | `GetDirectIsolationGap`: `ExerciseId.Contains("lateral-raise")`. |
| Inferencia por id en seguridad | `LimitationRules` excluye ejercicios por fragmentos del id (`dip`, `lunge`, `plank`, `skull-crusher`...) en las cinco zonas. Un ejercicio nuevo con otro id **no se excluye** para alguien con esa lesión. No estaba recogido en la auditoría previa. |
| Segunda fuente de equipamiento | `ExerciseEquipmentRequirements.Overrides` (33 ids). Hoy sus 33 valores son idénticos al seed: se puede retirar sin cambiar datos. |
| Validación desigual | Panel: solo exige texto no vacío (acepta cualquier patrón, músculo, objetivo o nivel). Importador DietoExpress: SQL directo con valores por defecto. Seeder: otra lista de reglas. El mapper descarta en silencio lo que el motor no entiende. |
| Revisión ficticia | `ExerciseCandidateMapper` marca todo ejercicio activo como `IsReviewed: true`. |
| Autoridad circular | `exercise-catalog-seeding.md` define el seed como copia de la base de datos de producción; el roadmap pide lo contrario. |
| Android | No se ha encontrado inferencia por id o nombre (búsqueda por patrones). Tiene traducciones del vocabulario de metadatos, no una copia de ellos. |

### P1 · Técnicas avanzadas — Pendiente (salvo superseries manuales)

- Prescripción por ejercicio: `Sets` + un único rango de repeticiones, RIR y descanso (`TrainingExercisePrescription`).
- Superseries: solo planes manuales, letra `supersetGroup` (A–F, 2–3 ejercicios) guardada junto al JSON; sin rondas ni descansos propios; el generador no las usa.
- Registro: `workout_set_logs` con índice único `(log, ejercicio, serie)`; no hay segmentos. Android registra peso, repeticiones y RIR por serie.
- Myo-reps, myo-rep match, drop sets, rest-pause y descendentes: no existen.

### P2 · Personalizar planes generados — Parcial

- Hay: sustituir ejercicio (sesión o resto del bloque), añadir ejercicio a **una** sesión, prioridades musculares, limitaciones, recolocar, omitir. Trazabilidad ya resuelta: `OriginalPrescriptionJson`, `PlanAdaptation.Source` (`engine`/`user`/`admin`) y antes/después.
- Falta: eliminar, reordenar, series, repeticiones, RIR, descansos, superseries, ajuste de tiempo. La edición completa solo existe para planes manuales.
- **El bloque siguiente se genera sin las decisiones del usuario**: recibe perfil, cargas, ejercicios del bloque anterior y objetivos aprendidos, no los cambios o añadidos manuales como intención. Un ejercicio sustituido puede volver.

### P3 · Programas prediseñados — Pendiente

No hay plantillas ni biblioteca de programas.

### P4 · Estadísticas — Parcial

- Hay: sesiones, racha, tonelaje, series por músculo de las últimas 8 semanas, e1RM e historial por ejercicio, carga semanal del plan, dosis real de 7 días, bloque actual, adaptaciones.
- Falta: prescrito frente a realizado por músculo, vista por mesociclo, comparación entre bloques, frecuencia y prioridades en pantalla.
- **Incoherencia**: `ProgressCalculator` cuenta cada secundario como 0,5; el motor y `LoadMetrics` cuentan como 1 solo el estímulo significativo. La misma semana muestra cifras distintas según la pantalla.

### P5 · Validación — Parcial

- Hay: golden de planes clásicos, pruebas de propiedades, perfiles de calidad, simulador y escenarios longitudinales, trazabilidad por mesociclo (versión del motor y huella del catálogo), exportación Trazo Intelligence y muestra anónima de investigación.
- Falta: batería de escenarios de decisión documentada, métricas definidas, comparación de políticas y el consentimiento de investigación en la app (abierto en #48).

### P6 · Recomendaciones — Parcial

Hay progresión doble, adaptación de volumen y frecuencia (con propuesta y aceptación), `RecoveryAdvisor`, descargas y
aprendizaje longitudinal (solo baja volumen). Falta un formato común de recomendación (qué, por qué, datos, cambio,
confianza, confirmación) y recomendaciones por ejercicio, estancamiento o molestias recurrentes.

### P7 · Cinco y seis días — Parcial

- Calendario: solo PPL admite 5 y 6 días. Automático y cuerpo completo llegan a 4; torso/pierna solo 2 o 4. **Torso/pierna de 5 días no existe en calendario.** Secuencia admite 2–6 en todos.
- Sesiones avanzadas largas quedan por debajo del tiempo disponible (límite de 20 series semanales), repiten accesorios y no rotan entre bloques fuera de fuerza (limitaciones ya documentadas en [planning-engine.md](planning-engine.md)).

### Contenido e idiomas

- El seed **no tiene instrucciones ni consejos en inglés**: hay 27 ficheros de traducción y ninguno `en`; las filas `en` nacen en borrador con solo el nombre y la API responde con el texto en español. No verificado si en producción alguien las completó desde el panel.
- No existen campos de errores frecuentes ni indicaciones de seguridad. No hay vídeos (sí imágenes, cobertura sin auditar).

## 2. Convivencia con el roadmap anterior

La **pista de lanzamiento** (Play Console, App Links, cobros, pruebas en dispositivo, restauración, legales) va **justo después**
de todo este roadmap, no en paralelo (decisión del titular, 2026-11-26).

| Del issue #48 | Destino |
|---|---|
| P12 revisión del catálogo, «corregir datos de producción» | Absorbido en P0 |
| Superseries automáticas | Absorbido en P1 |
| Flujo de generación con revisión y edición | Absorbido en P2 |
| Splits de 5–6 días, techo de 20 series, variedad | Absorbido en P7 |
| Recalibrar `RecoveryAdvisor`, validar con historiales | Absorbido en P5/P6 |
| Portar a DietoExpress / paquete compartido | Aplazado hasta cerrar P0 (decisión D3) |
| Play Console, App Links, cobros, pruebas en dispositivo, restauración, legales | Justo después de este roadmap |
| Analítica P7, fiscal P10, marketing P11, ecosistema | Sin cambios, detrás de este roadmap |

Cuando estén implementados este roadmap **y los bloqueantes de lanzamiento**, se revisarán las exclusiones por lesión (L-REV en §6;
decisión del titular, 2026-11-26).

## 3. Recomendaciones de arquitectura

Son propuestas. Las cifras de esta sección son **heurísticas de producto**, no recomendaciones científicas.

### R1 · Metadatos del catálogo

Añadir al ejercicio solo lo que algún consumidor lee:

| Campo | Para qué | Sustituye |
|---|---|---|
| `effectiveSecondaryMuscles` (subconjunto de los secundarios) | cuentan como una serie | tabla `TrainingExerciseStimulus` |
| `emphasis` (lista cerrada; hoy solo `LateralDeltoid`) | huecos de aislamiento directo | `Contains("lateral-raise")` |
| `limitationAreas` (`Knee`, `Shoulder`, `LowerBack`, `Wrist`, `Elbow`) | exclusiones por lesión | `LimitationRules` por id y reglas estructurales |
| `metadataReviewed` | solo un ejercicio válido y revisado entra en el motor | `IsReviewed: true` fijo |
| (en P1) aptitud para técnicas | qué técnicas admite | — |

Estímulo binario, como hoy: no hay base para calibrar fracciones. Se mantiene el grupo `Deltoids` (partirlo rompería
objetivos e historial). Los valores iniciales se derivan automáticamente de lo que hacen las tablas actuales, de modo que los
planes no cambien (lo protege el golden); las correcciones llegan después, en PR separados y visibles.

Un **validador único** (dominio) lo usan seeder, alta y edición del panel e importador: vocabularios cerrados, secundarios
efectivos ⊆ secundarios, sin solapes, equipamiento canónico, coherencia patrón/mecánica y `limitationAreas` declarado
(vacío es una respuesta, ausente no).

### R2 · Fuente de verdad (aprobada y ampliada el 2026-11-26)

El seed es la **línea base** de ejercicios y tiene prioridad sobre la base de datos:

- Contiene todos los metadatos correctos y las traducciones en **todos los idiomas**.
- Para los ejercicios que incluye, **sobrescribe** lo que haya en la base de datos: metadatos, textos, visibilidad y traducciones.
- Para cambiar uno de esos ejercicios se modifica el seed.
- La consola de administración **indica que el ejercicio es del seed** y no permite editar lo que el seed gobierna.
- El importador de DietoExpress no puede pisarlos. Los ejercicios fuera del seed siguen siendo editables, con el mismo validador.
- Un idioma que el seed aún no tenga (hoy, inglés) no borra lo que haya en la base de datos.

### R3 · Modelo de prescripción

Mantener ejercicio → series y añadir, sin jerarquías:

- `technique` opcional por ejercicio: tipo (`MyoReps`, `MyoRepMatch`, `DropSet`, `RestPause`), a qué series se aplica, objetivo y RIR de activación, repeticiones por segmento, descanso entre segmentos, máximo de segmentos, % de reducción de carga y criterio de parada. «Descendentes múltiples» es `DropSet` con dos o más descensos, con nombre propio en la interfaz.
- Grupo de superserie a nivel de sesión: orden, rondas, descanso entre ejercicios y entre rondas (lee la letra actual).
- Registro: `SegmentIndex` (0 = serie principal) en `workout_set_logs` y en el índice único. Los registros antiguos quedan en 0.
- Definiciones: *myo-reps*, activación cercana al fallo y miniseries cortas hasta no llegar al objetivo; *match*, serie inicial y miniseries hasta **sumar las repeticiones de la inicial**; *rest-pause*, reanudaciones con la misma carga hasta el criterio de parada. Se confirman con fuentes en el PR de diseño.
- Compatibilidad: la app declara la capacidad; a versiones anteriores no se les sirven técnicas. Las operaciones del motor deben conservar los campos nuevos al reescribir el JSON.

### R4 · Qué usa el generador

Primero solo manual y registro; el generador después. Justificación: **ahorrar tiempo**, no superioridad.

| Técnica | Nivel | Ejercicio | Cuándo |
|---|---|---|---|
| Superserie antagonista o no solapada | Intermedio+ (principiante no) | accesorios, máquinas | la sesión no cabe en el tiempo |
| Drop set (última serie) | Intermedio+ | aislamiento en máquina, polea o mancuerna | falta tiempo o músculo prioritario |
| Myo-reps / match | Avanzado | aislamiento estable | falta tiempo o prioridad |
| Rest-pause | Avanzado | máquina o aislamiento estable | falta tiempo o prioridad |

Nunca: sentadillas, bisagras o básicos libres pesados; levantamiento principal en fuerza; descargas, reentrada o preparación;
con dolor o fatiga repetida. Topes: una técnica de fatiga por músculo y dos por sesión.

### R5 · Volumen

No existe equivalencia validada. Propuesta conservadora y versionada: la serie inicial cuenta 1; el conjunto de segmentos
adicionales suma como máximo 0,5 (un bloque nunca pasa de 1,5); en superseries cada serie cuenta normal. La interfaz separa
«segmentos hechos» de «series efectivas estimadas». La progresión usa solo la serie principal (carga y repeticiones); los
segmentos informan, nunca declaran una regresión.

### R6 · Decisiones manuales

Precedencia: seguridad (dolor, limitaciones) > decisión del usuario > adaptación automática > propuesta del motor. Una capa
persistente de *ajustes del usuario* por plan (ejercicio sustituido, quitado o añadido en un hueco de sesión; parámetro
fijado) que se aplica a los bloques ya creados y a cada bloque nuevo. La adaptación no toca lo fijado: lo propone y explica.
Los cambios manuales quedan marcados y el aprendizaje longitudinal no los cuenta como decisiones propias.

### R7 · Diez programas

Recetas sobre el motor (reparto, huecos por patrón y músculo, prioridades, técnicas), resueltas con el catálogo según
equipamiento: ningún segundo generador. Lista: cuerpo completo 3 y 4; torso/pierna 4 y 5 (rotación); PPL 3 y 6;
especialización de pecho, espalda y brazos-deltoides; hipertrofia avanzada 5 días (híbrido torso/pierna + PPL). Las de 5 días
dependen de trabajo de P7, que se adelanta.

### R8 · Premium (propuesta; no se cambia nada sin aprobación)

Cuerpo completo 3 días gratis como plan manual editable, sin adaptación; las diez completas y su adaptación, Premium.
Hay que decidir qué se muestra antes de comprar, por la regla actual de no servir semanas futuras.

### R9 · Métricas útiles

Series efectivas por músculo prescritas y realizadas; exposiciones por semana; cumplimiento; repeticiones a igual carga y RIR;
tendencia de e1RM por ejercicio (hasta 12 repeticiones y solo comparable); comparación de bloques solo en ejercicios comunes.
Evitar tonelaje como indicador de hipertrofia, puntuaciones de fatiga y cualquier cifra presentada como medida fisiológica.

### R10 · Datos para validar

Sobre lo ya exportado: versión de motor y de cada política por prescripción, motivo codificado de cada cambio manual, técnica y
segmentos, capacidad del cliente. Al menos dos o tres mesociclos por usuario antes de comparar; el tamaño de muestra se
calcula cuando se fije la métrica principal. Solo datos anónimos o agregados, con consentimiento.

## 4. Decisiones que requieren aprobación

| ID | Decisión | Recomendación | Bloquea |
|---|---|---|---|
| D3 | DietoExpress mientras cambia el motor | Congelar el port hasta cerrar P0 | — |
| D6 | Precedencia manual | R6 | P2 |
| D8 | Revisión de los 150 ejercicios por un entrenador | Recomendable: la revisión asistida no sustituye a un experto | cierre de P0 |
| D9 | Origen y derechos de vídeos | Material propio o con licencia explícita | vídeos |

## 5. Riesgos

- Al pasar metadatos a datos, una migración incompleta cambia planes o deja de excluir ejercicios con lesión: golden y prueba de equivalencia de exclusiones antes de borrar código.
- Alinear producción con el seed cambia los planes nuevos (no los existentes) y puede pisar ediciones intencionadas.
- Técnicas con apps antiguas o registros en la cola offline: contrato aditivo, capacidad declarada y migración con índice nuevo.
- `TrainingPlanService` (más de 2.500 líneas) es punto de conflicto de P1, P2 y P6: extraer lo que toque cada PR, sin refactor previo.
- El motor diverge más de DietoExpress.
- El volumen de técnicas y la matriz son heurísticas: versionarlas y no presentarlas como ciencia.

## 6. Secuencia de PR y seguimiento

P0-4 se adelanta a P0-2: producción usa hoy metadatos antiguos y el titular ha fijado que el seed mande.

Cambio de orden respecto al roadmap, y motivo: la parte de P7 que habilita 5 días en torso/pierna e híbrido va antes de P3; la
batería de P5 empieza con P0 porque protege sus cambios.

| ID | PR | Objetivo y criterios de aceptación | Depende | Estado | PR | Pruebas |
|---|---|---|---|---|---|---|
| P0-0 | Roadmap | Este documento, enlazado desde #48 | — | Hecho | #238 | solo documentación |
| P0-1 | Validador único e informe de divergencias | Mismas reglas en seed, panel e importador; aviso al arrancar y `GET /admin/exercises/seed-report`; ningún dato cambia | — | Hecho | #239 | validador, seed, informe, panel e importación en CI; no comprobado en producción |
| P0-2 | Metadatos nuevos en datos | Migración y seed con R1; panel y API; valores equivalentes a las tablas | P0-1, D4 | Hecho | #242 | migración, validador, panel, seeder |
| P0-3 | Motor y limitaciones sin ids | Se borran tabla de estímulo, tokens y overrides; planes y exclusiones idénticos; prueba que impide literales de id; un ejercicio nuevo funciona sin código | P0-2 | Hecho | #242 | golden intacto; 1.890 planes del catálogo real idénticos a los de `main` (comprobación puntual) |
| P0-4 | Seed autoritativo | R2 completa: producción igual al seed en metadatos y traducciones, idempotente, instalación nueva equivalente; la consola marca los ejercicios del seed y bloquea su edición | P0-1 | Hecho (producción comprobada tras desplegar) | #240 | seeder en SQLite y PostgreSQL (CI), panel, importación, consola |
| P0-5 | Revisión ejercicio a ejercicio (3 PR por patrón) | Cada ejercicio con decisión registrada; cambios de plan explicados | P0-3, P0-4 | Hecho (150 revisados con asistencia; sin entrenador, D8) | #243, #244, #245 | comparación de 1.890 planes; ver `exercise-review.md` |
| P5-1 | Escenarios de decisión | Perfiles, fatiga, estancamiento, dolor, incumplimiento; en CI | — | Hecho | #247 | `DecisionScenarioTests`; carencias en `validation-scenarios.md` |
| X-1 | Una sola definición de serie efectiva en Progreso | Misma cifra en carga y progreso | P0-3 | Pendiente | | |
| C-1 | Inglés en el seed | `translations/en.json` completo, mismos pasos y consejos que el original; revisión nativa pendiente | P0-4 | Hecho (sin revisión nativa) | #241 | cobertura de todos los idiomas y correspondencia de pasos |
| C-2 | Errores frecuentes y seguridad (es/en) | Campos, contenido y pantalla Android | C-1 | Pendiente | | |
| P1-1 | Diseño y contrato de prescripción | Documento con definiciones y fuentes; esquema versionado; compatibilidad | P0, D5 | Hecho (D5 aprobada) | #248 | `prescription-techniques.md` |
| P1-2 | Registro por segmentos, volumen y progresión | Migración; offline; sin regresiones falsas | P1-1 | Hecho: registro, técnicas admitidas, prescripción y capacidad | #250, #251, #252 | `prescription-techniques.md` §5 |
| P1-3 | Técnicas en planes manuales (backend + Android) | Prescribir, ejecutar, recuperar borrador y sincronizar las cinco técnicas | P1-2 | Hecho; sin probar en un dispositivo | #253, #254, Trazo.Android#108 | `prescription-techniques.md` §5 |
| P1-4 | Superseries completas | Rondas y descansos, 2+ ejercicios, rotación intacta | P1-1 | Hecho; sin probar en un dispositivo | #255, Trazo.Android#109 | `prescription-techniques.md` |
| P1-5 | Técnicas en el generador | Matriz R4, con explicación | P1-3, P1-4 | Hecho; sin probar en un dispositivo | PR de esta rama | `prescription-techniques.md` §8 |
| P2-1 | Capa de ajustes del usuario | Sobreviven al bloque siguiente y a las adaptaciones | D6 | Pendiente | | |
| P2-2 | Edición completa de planes generados (API) | Todas las operaciones del roadmap, con origen | P2-1, P1-1 | Pendiente | | |
| P2-3 | Edición en Android | Pocas acciones; offline definido | P2-2 | Pendiente | | |
| P7-0 | Prioridades y volumen bajo objetivo (adelantado a petición del titular) | Enfatizar añade series; con tiempo libre los músculos llegan a su objetivo | P5-1 | Hecho (quedan casos con pocas sesiones) | #249 | 2.430 planes con énfasis comparados; `validation-scenarios.md` |
| P7-1 | Torso/pierna 5 e híbrido de 5 días | Volumen, frecuencia y recuperación probados | P0, P5-1 | Pendiente | | |
| P3-1 | Recetas de programa y primeros tres | Motor común, permisos, personalizables | P1-1, P2-2, D7 | Pendiente | | |
| P3-2 | Diez programas y pantalla Android | Revisados y documentados | P3-1, P7-1 | Pendiente | | |
| P4-1 | Estadísticas por músculo (API + Android) | Prescrito, realizado, frecuencia, tendencia | X-1 | Pendiente | | |
| P4-2 | Resumen por mesociclo | Comparación entre bloques honesta | P4-1 | Pendiente | | |
| P5-2 | Métricas y comparación de políticas | R9–R10; consentimiento en la app | P1-2, P2-1 | Pendiente | | |
| P6-1 | Recomendaciones explicables | Formato común, confianza y confirmación | P4, P5-2 | Pendiente | | |
| P7-2 | Calidad de 5 y 6 días | Variedad, rotación entre bloques, sin relleno | P7-1 | Pendiente | | |
| L-REV | Revisar las exclusiones por lesión (titular, idealmente con un entrenador o fisioterapeuta) | Cada zona revisada, en especial lo decidido con evidencia débil en `exercise-review.md` | Después de este roadmap y de los bloqueantes de lanzamiento | Pendiente | | |

### Registro de decisiones

| Fecha | Decisión |
|---|---|
| 2026-11-26 | Se adopta este roadmap con precedencia sobre las prioridades de producto de #48. Recomendaciones R1 y R3–R10 pendientes de aprobación. |
| 2026-11-26 | D1: el seed es la línea base, con traducciones en todos los idiomas, sobrescribe la base de datos en sus ejercicios y la consola los identifica (R2). |
| 2026-11-26 | D2: la pista de lanzamiento va justo después de este roadmap. |
| 2026-11-26 | D4: aprobado el modelo de metadatos R1. Los valores iniciales reproducen las reglas anteriores; corregirlos es P0-5. |
| 2026-11-26 | Cierre de P0: los 150 ejercicios revisados (asistido, sin entrenador) y con textos completos en los 29 idiomas del seed, con los mismos pasos y consejos que el original (lo exige una prueba). Pendiente: revisión nativa de las traducciones y comprobación en producción con `seed-report`. |
| 2026-11-26 | D5: aprobadas las cinco decisiones de `prescription-techniques.md` §11 (valores por defecto, volumen y progresión, `allowedTechniques` en el seed, capacidad `techniques-1`, sin combinar superserie y técnica de fatiga). La matriz del generador se concreta en P1-5. |
| 2026-11-27 | D7: los diez programas son Premium (ningún permiso ni condición de suscripción cambia; se descarta regalar uno, R8). Antes de pagar se ve la ficha (días, duración, material, series semanales por músculo) y una sesión de ejemplo, no las semanas completas. |
| 2026-11-27 | P5-2: la comparación de políticas con datos reales y la exportación de investigación usan **solo a quien lo consienta**: interruptor opcional en la app, apagado por defecto y revocable; los resultados se muestran agregados. El texto legal lo aprueba el titular. |

## 7. Evidencia consultada

Consulta preliminar por resúmenes y notas de prensa; **cada PR que cambie una política debe leer la fuente completa**.

- ACSM 2026, resumen oficial (no se ha leído el artículo): <https://acsm.org/resistance-training-guidelines-update-2026/>. Destaca constancia, frecuencia de al menos dos días y volumen; el fallo no mostró un efecto consistente en adultos sanos.
- Drop sets frente a series tradicionales, metaanálisis 2023: <https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10390395/>. Hipertrofia sin diferencia significativa, en menos tiempo.
- Superseries frente a series tradicionales, metaanálisis 2025: <https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12011898/>. Adaptaciones similares con sesiones más cortas y más fatiga percibida.
- Rest-pause: la evidencia hallada sugiere como mucho una ventaja pequeña; pendiente de revisar las fuentes primarias.
- Myo-reps y myo-rep match: no se ha encontrado un metaanálisis específico. Tratar como evidencia limitada.
