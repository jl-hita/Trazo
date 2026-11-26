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

## Resultado de la primera pasada (2026-11-26)

Se cumple en los perfiles con plan: ninguna sesión supera el tiempo ofrecido; ningún músculo pasa de 20 series semanales; con
gimnasio completo en hipertrofia los músculos grandes reciben al menos 6; un usuario avanzado no recibe claramente menos
trabajo que un principiante; las prioridades nunca mueven la dosis en sentido contrario; toda limitación sigue dando plan sin
nada de lo que excluye.

## Carencias encontradas

Las pruebas fijan las tres primeras tal como están: arreglarlas (o empeorarlas) obliga a cambiar la prueba.

| # | Carencia | Dónde se trata |
|---|---|---|
| 1 | 130 de 450 perfiles no generan plan: sin material no hay tirón horizontal; en fuerza con banda o sin material faltan empuje y sentadilla; un principiante de fuerza con barra y rack no tiene sentadilla ni remo (se abren en intermedio) | Decisión de producto: plan parcial con aviso, ejercicios nuevos o nivel de entrada |
| 2 | PPL de 5 días en calendario entrena la pierna una sola vez por semana | P7 |
| 3 | Enfatizar dorsales o cuádriceps (intermedio, 4 días, 75 min) no añade ninguna serie: se queda en 14 de un objetivo de 16 con tiempo libre en la sesión | P7 |
| 4 | En el mismo perfil, pecho y deltoides quedan por debajo de su objetivo sin prioridad (10 y 9 de 13) aunque las sesiones terminan antes del tiempo ofrecido | P7 |
| 5 | Un principiante con 2 días no recibe ninguna serie de gemelo; las sesiones de PPL de 6 días de un principiante duran unos 30 de 75 minutos | P7 |

Aún no cubierto: métricas con datos reales y comparación de políticas (P5-2).
