# Escenarios de decisión del motor (P5-1)

Batería reproducible que comprueba que las decisiones del motor son razonables, no solo que el plan es válido. Se ejecuta en
CI. **Es una simulación sobre reglas propias: no es evidencia clínica ni dice que los planes produzcan mejores resultados.**
Las cotas son expectativas de producto, no umbrales fisiológicos.

## Qué cubre cada dimensión

| Dimensión | Pruebas |
|---|---|
| Nivel, objetivo, frecuencia, material y duración (450 perfiles sobre el catálogo real) | `DecisionScenarioTests` |
| Prioridades musculares | `DecisionScenarioTests`, `MusclePriorityTests`, `LongitudinalLimitsTests` |
| Restricciones y lesiones | `DecisionScenarioTests`, `EngineCoachingQualityTests`, `UpdateLimitationsTests` |
| Invariantes del plan (series, repeticiones, RIR, descansos, determinismo) | `MesocycleGeneratorPropertyTests`, `ClassicalPlanGoldenTests` |
| Progresión, estancamiento, fatiga, recuperación, molestias, incumplimiento | `PlanLogicTests` (reglas de adaptación e historiales aleatorios) |
| Bloques completos: dosis aprendida, dolor, datos insuficientes | `Longitudinal/LongitudinalScenarioTests` y `LongitudinalSimulator` |
| Cambios de disponibilidad y ausencias | `FrequencyReorganizationTests`, `RescheduleAndReentryTests`, `SessionsNotDoneTests` |
| Modificaciones manuales | `PrescriptionHistoryTests`; su efecto sobre bloques futuros no existe todavía (P2) |

## Resultados (2026-11-26)

La dosis semanal se mide en la **semana 3** del bloque. Las dos primeras semanas de un bloque de hipertrofia son
introductorias (una serie menos en muchos ejercicios).

Se cumple en los perfiles con plan: ninguna sesión supera el tiempo ofrecido; con gimnasio completo en hipertrofia los músculos
grandes reciben al menos 6 series; un usuario avanzado no recibe claramente menos trabajo que un principiante; enfatizar sube la
dosis y mantener la baja; un músculo enfatizado no pasa de 20 series; toda limitación sigue dando plan sin nada de lo que
excluye.

> **Corrección.** La primera pasada midió la semana 1 y dio por carencias dos cosas que no lo eran: que enfatizar dorsales o
> cuádriceps no añadía series, y que pecho y deltoides quedaban bajo su objetivo. En la semana 3 sí suben y llegan.

### Arreglado a raíz de la medición correcta (motor 0.12.0)

Enfatizar un músculo podía no añadir nada y dejarlo por debajo de su objetivo, porque los ejercicios disponibles ya estaban en su
tope de series. Ahora un músculo enfatizado tiene un hueco de ejercicio propio mientras esté bajo objetivo y quepa en la sesión.
Además, un objetivo elevado dejaba sin techo a los ejercicios que trabajan ese músculo de forma indirecta y podía llegar a 24
series; ahora el límite es 20 (o el propio rango de un objetivo profesional mayor).

Comparación con la versión anterior en 2.430 planes con un énfasis (nivel, frecuencia, material, duración y semilla), semana 3:

| Medida | Antes | Después |
|---|---|---|
| Gimnasio completo: músculo enfatizado más de una serie bajo su objetivo (de 1.215) | 237 | 126 |
| Músculo enfatizado por encima de 20 series | 54 | 0 |
| Músculo enfatizado: planes en que sube / baja | — | 507 / 83 |
| Resto de músculos en esos planes: suben / bajan (de 19.440) | — | 380 / 883, media -0,03 series |

Los planes sin prioridades no cambian. Las bajadas del músculo enfatizado vienen sobre todo del nuevo techo; en el resto, el
peor caso pierde 4 series: el hueco nuevo ocupa tiempo de la sesión.

## Carencias abiertas

| # | Carencia | Dónde se trata |
|---|---|---|
| 1 | 130 de 450 perfiles no generan plan: sin material no hay tirón horizontal; en fuerza con banda o sin material faltan empuje y sentadilla; un principiante de fuerza con barra y rack no tiene sentadilla ni remo (se abren en intermedio) | Decisión de producto: plan parcial con aviso, ejercicios nuevos o nivel de entrada |
| 2 | PPL de 5 días en calendario entrena la pierna una sola vez por semana | P7 |
| 3 | Con pocas sesiones o sesiones de 60 minutos, parte de los énfasis no llega a su objetivo (sobre todo espalda alta e isquiotibiales) | P7 |
| 4 | Un principiante con 2 días no recibe ninguna serie de gemelo; las sesiones de PPL de 6 días de un principiante duran unos 30 de 75 minutos | P7 |
| 5 | Fuerza avanzada con solo mancuernas: los glúteos llegan a 21 series en las semanas altas | P7 |

Aún no cubierto: métricas con datos reales y comparación de políticas (P5-2).
