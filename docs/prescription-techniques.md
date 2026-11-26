# Técnicas avanzadas: modelo de prescripción y registro (P1-1)

Estado: **aprobado el 2026-11-26 (D5)**; la matriz del generador (sección 8) se concreta en P1-5. Implementado: solo el registro por segmentos y sus reglas de conteo (P1-2a, secciones 4 y 5). Las cifras son **valores por defecto de
producto**, no recomendaciones demostradas; la sección 9 dice en qué se apoya cada una.

## 1. Punto de partida

- Cada ejercicio prescrito (`TrainingExercisePrescription`) tiene un número de series con un único rango, RIR y descanso.
- Superseries: solo en planes manuales, una letra (`supersetGroup`) junto al JSON; 2-3 ejercicios consecutivos.
- Registro (`workout_set_logs`): una fila por serie, única por (registro, ejercicio, serie); máximo 12 series por ejercicio y
  60 por sesión.
- La app envía `X-App-Version`; no declara capacidades.

## 2. Definiciones

| Técnica | Qué es | Parámetros por defecto | Fin |
|---|---|---|---|
| **Myo-reps** | Serie de activación cercana al fallo y miniseries con la misma carga y descansos muy breves | Activación 12-20 reps, RIR 1; miniseries de 3-5 reps; descanso 15 s | Miniserie por debajo de 3 reps, o 5 miniseries |
| **Myo-rep match** | La primera serie fija el objetivo; cada serie posterior suma miniseries **hasta igualar sus repeticiones** | Primera serie normal con RIR 1; descanso 15 s | Objetivo igualado, o 5 miniseries (no igualarlo no es un fallo) |
| **Rest-pause** | Serie cercana al fallo y reanudaciones con la misma carga | RIR 0-1; descanso 20 s; 2 reanudaciones | Reanudación por debajo de 2 reps, o las previstas |
| **Drop set** | Serie y un descenso de carga sin apenas descanso | 20 % menos; hasta 15 s para cambiar la carga; RIR 0-1 | Los descensos previstos |
| **Descendentes múltiples** | Drop set con dos o tres descensos, con nombre propio en la app | 20 % cada vez | Los descensos previstos |
| **Superserie** | Dos o más ejercicios alternados por rondas | 0-30 s entre ejercicios, 90-120 s tras la ronda | Rondas previstas |

Diferencia entre myo-reps y rest-pause: las miniseries tienen un objetivo corto y fijo y se para al no alcanzarlo; en
rest-pause cada reanudación se lleva al criterio de esfuerzo.

## 3. Modelo de prescripción

Sin jerarquías: el ejercicio sigue teniendo sus series y gana un campo opcional.

```json
{
  "prescriptionVersion": 2,
  "exercises": [
    {
      "exerciseId": "cable-curl", "sets": 3, "minRepetitions": 10, "maxRepetitions": 15,
      "technique": {
        "type": "DropSet", "appliesTo": "LastSet",
        "maxSegments": 1, "restSeconds": 10, "loadDropPercent": 20,
        "segmentMinRepetitions": null, "segmentMaxRepetitions": null, "targetRir": 1
      }
    }
  ],
  "supersets": [ { "group": "A", "rounds": 3, "restBetweenSeconds": 15, "restAfterRoundSeconds": 90 } ]
}
```

- `type`: `MyoReps`, `MyoRepMatch`, `DropSet`, `RestPause`. «Descendentes múltiples» es `DropSet` con `maxSegments` ≥ 2.
- `appliesTo`: `LastSet` (por defecto) o `AllSets`. En myo-reps match, las series 2 en adelante.
- `maxSegments`: segmentos **después** de la serie principal (0 no es válido).
- Los ejercicios de una superserie siguen marcados con `supersetGroup`; la entrada de `supersets` añade rondas y descansos.
  Sin ella se aplica el comportamiento actual, así que lo guardado sigue valiendo.
- `technique` es un campo del registro del motor, no una anotación aparte: las operaciones que reescriben la prescripción
  (cambio, descarga, ajuste) deben conservarlo o quitarlo a propósito.

### Validación (un único validador para planes manuales y generados)

- Parámetros dentro de rango; `loadDropPercent` solo y obligatorio en `DropSet` (5-50).
- El ejercicio admite la técnica: nuevo campo de catálogo `allowedTechniques`, **declarado y revisado en el seed**, con el
  mismo criterio que `limitationAreas`. No se deduce del identificador ni del equipamiento.
- En manual se avisa, pero no se impide, salvo en técnicas que el catálogo no admite en ese ejercicio.
- Una superserie tiene 2-4 ejercicios consecutivos; no se combina con otra técnica en el mismo ejercicio en la primera versión.

### Qué hace el motor con ellas

- Descarga, reentrada, preparación y dolor en el ejercicio: se quita la técnica de las sesiones afectadas.
- Cambio de ejercicio: se conserva solo si el sustituto la admite.
- La duración estimada incluye segmentos y descansos.

## 4. Registro

- `workout_set_logs` gana `SegmentIndex` (0 = serie principal; 1…n, segmentos) y la clave única pasa a
  (registro, ejercicio, serie, segmento). Las filas antiguas quedan en 0: no se reescribe nada.
- `LogSetRequest` gana `segment` opcional (0 por defecto). Máximo 10 segmentos por serie; el tope por sesión sube de 60 a 200
  filas.
- Cada segmento guarda su carga, repeticiones, RIR y si se completó. La superserie no necesita datos nuevos.
- La idempotencia (`ClientRequestId`) y la cola sin conexión no cambian.
- El borrador de la app guarda el segmento, para recuperar un entreno interrumpido.

## 5. Volumen, progresión y recuperación

Todos los consumidores deben leer los segmentos; ninguno puede contar filas como series. Se concentra en una sola función.

| Consumidor | Regla propuesta |
|---|---|
| Volumen prescrito y realizado (`LoadMetrics`, informe de calidad, observaciones longitudinales) | La serie principal cuenta 1; los segmentos de una serie suman juntos como máximo 0,5 (un bloque nunca pasa de 1,5). Superseries: cada serie cuenta normal |
| Progresión, referencias de carga, último rendimiento, propuesta de carga | **Solo la serie principal** |
| Estancamiento y regresión | Los segmentos nunca declaran una regresión |
| Progreso (1RM estimado, récords) | Solo la serie principal; el tonelaje incluye todo |
| Recuperación | La serie con técnica de fatiga pesa como cercana al fallo (heurística) |
| Exportaciones (Intelligence, investigación) | Incluyen el segmento y la técnica |

La interfaz muestra por separado «segmentos hechos» y «series efectivas estimadas».

### Estado tras P1-2a

- `POST .../log` acepta `segment` (0 o ausente, la serie principal; 1-10). Un segmento sin su serie principal, repetido o
  fuera de rango se rechaza; máximo 60 series y 200 filas por sesión. Las vistas del registro y la exportación de cuenta e
  Intelligence incluyen el segmento.
- La lectura común está en `LoggedSets`: serie principal para el rendimiento; 1 + 0,5 para el volumen; dolor en cualquier
  fila (el de un segmento cuenta para su serie). El motivo por el que se para un segmento no se trata como serie omitida.
- La recuperación usa las series efectivas y el RIR de las principales. **Falta** que una serie con técnica de fatiga pese
  como cercana al fallo: necesita saber la técnica prescrita, que llega en P1-2b.
- **Falta en P1-2b** ocultar los segmentos a las apps sin la capacidad `techniques-1`. Ninguna app publicada los envía.

## 6. Compatibilidad

- La app declara `X-Client-Capabilities: techniques-1`.
- Sin esa capacidad, la API entrega las series normales (sin `technique` ni rondas) y los registros llegan como hoy. No se
  rechaza nada.
- Crear o editar un plan manual con técnicas exige la capacidad, para que una app antigua no las borre al guardar: editar desde
  una app antigua un plan que ya las tiene responde con un error que pide actualizar.
- El generador no añade técnicas a un usuario cuya app no las ha declarado.
- Orden de despliegue: backend primero.

## 7. App Android

Modelos y borrador con segmentos; pantalla de entreno con la técnica identificada, instrucciones, registro de carga y
repeticiones por segmento, temporizador de descansos breves y de ronda; editor de planes manuales; textos en todos los
idiomas de la app.

## 8. Generador (P1-5, después de que registro y app estén en producción)

Matriz R4 del roadmap: solo para ahorrar tiempo o atender un músculo prioritario; superseries antagonistas y drop sets desde
intermedio, myo-reps y rest-pause en avanzado; solo en ejercicios que lo admiten; nunca en sentadillas, bisagras ni básicos
libres, en descargas ni con dolor; una técnica de fatiga por músculo y dos por sesión.

## 9. En qué se apoya

Búsqueda preliminar; antes de P1-5 hay que leer las fuentes completas.

| Afirmación | Respaldo |
|---|---|
| Drop sets: hipertrofia similar a series normales en menos tiempo | Metaanálisis 2023: <https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10390395/> |
| Superseries: adaptaciones similares con sesiones más cortas y más fatiga percibida | Metaanálisis 2025: <https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12011898/> |
| Descansos largos entre series pueden favorecer algo la hipertrofia | Metaanálisis 2024: <https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11349676/> |
| Rest-pause: como mucho una ventaja pequeña | Evidencia limitada |
| Estructura de myo-reps y match, repeticiones y descansos por defecto | **Solo fuentes de entrenadores**, sin cifras coincidentes (<https://www.barbellmedicine.com/blog/myo-reps/>, <https://www.strengthlog.com/myo-reps/>). No se encontró metaanálisis |
| Descanso de 20 s y reducción del 20 % | Práctica habitual, sin ensayo que lo fije |
| Peso 0,5 de los segmentos en el volumen | **Heurística de TRAZO**: no existe equivalencia validada |

## 10. Plan de entrega

| PR | Contenido | Criterios |
|---|---|---|
| P1-2a | `SegmentIndex`, registro, función única de volumen y progresión | Migración en PostgreSQL; registros antiguos idénticos; sin regresiones falsas |
| P1-2b | `technique` y `supersets` en prescripción, `allowedTechniques` en seed y validador, capacidad del cliente | Lo guardado se lee igual; planes generados sin cambios (golden) |
| P1-3 | Planes manuales y entreno en Android | Prescribir, ejecutar, recuperar y sincronizar las cinco técnicas |
| P1-4 | Superseries completas | Rondas y descansos, 2-4 ejercicios, rotación intacta |
| P1-5 | Generador | Matriz con explicación y pruebas de escenarios |

## 11. Decisiones aprobadas

1. Los valores por defecto de la sección 2.
2. El peso del volumen (1 + como máximo 0,5) y progresar solo con la serie principal.
3. `allowedTechniques` declarado en el seed, revisado ejercicio a ejercicio.
4. La capacidad `techniques-1` y no poder editar desde una app antigua un plan con técnicas.
5. No combinar superserie y técnica de fatiga en la primera versión.
