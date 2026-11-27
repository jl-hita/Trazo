# Programas prediseñados (P3)

Un programa es **un perfil ya preparado para el mismo generador** (`ProgramLibrary`): no hay un segundo motor. El material,
las limitaciones y las exclusiones del usuario siguen decidiendo los ejercicios, y el plan resultante se personaliza como
cualquier plan generado ([plan-customisation.md](plan-customisation.md)). Niveles, duraciones y prioridades son
**decisiones de producto**, no recomendaciones demostradas.

| Id | Programa | Nivel mínimo | Reparto | Días | Min | Énfasis |
|---|---|---|---|---|---|---|
| `full-body-3` | Cuerpo completo · 3 días | Principiante | Cuerpo completo | 3 | 60 | |
| `full-body-4` | Cuerpo completo · 4 días | Intermedio | Cuerpo completo | 4 | 60 | |
| `upper-lower-4` | Torso/pierna · 4 días | Intermedio | Torso/pierna | 4 | 60 | |
| `upper-lower-5` | Torso/pierna · 5 días | Intermedio | Torso/pierna en rotación | 5 | 60 | |
| `push-pull-legs-3` | Empuje/tirón/pierna · 3 días | Principiante | PPL | 3 | 60 | |
| `push-pull-legs-6` | Empuje/tirón/pierna · 6 días | Intermedio | PPL | 6 | 60 | |
| `chest-focus` | Especialización en pecho | Intermedio | Torso/pierna | 4 | 75 | Pecho |
| `back-focus` | Especialización en espalda | Intermedio | Torso/pierna | 4 | 75 | Dorsales, espalda alta |
| `arms-shoulders-focus` | Especialización en brazos y hombros | Intermedio | Torso/pierna | 4 | 75 | Bíceps, tríceps, deltoides |
| `advanced-hybrid-5` | Hipertrofia avanzada · 5 días | Avanzado | Híbrido | 5 | 75 | |

Todos son de hipertrofia y de 12 semanas. El reparto híbrido (torso, pierna, empuje, pierna, tirón) no se ofrece como opción
suelta para no mostrar un valor sin nombre en apps anteriores.

## Premium (D7, opción B, aprobada)

Los diez son Premium, como todo plan generado; no cambian permisos ni suscripciones. Antes de comprar se puede ver la
**ficha**: material, series semanales por músculo de una semana normal y **una** sesión de ejemplo. No se sirven más sesiones.

## API

| Operación | Efecto |
|---|---|
| `GET /api/v1/plans/programs` | Los diez, traducidos; `locked` si falta Premium |
| `POST /api/v1/plans/programs/{id}/sheet` | Ficha para el material y limitaciones enviados (cuerpo como el de crear plan, todo opcional). No guarda nada |
| `POST /api/v1/plans/programs/{id}` | Crea el plan (Premium). El programa fija objetivo, reparto, modo y prioridades; el usuario puede indicar nivel (igual o superior al mínimo), días (tantos como sesiones), minutos, semanas, material, limitaciones, exclusiones y fecha |

El plan toma el nombre del programa.

## Límites conocidos

- Con poco material algunos programas pierden variedad o no se pueden generar: la respuesta explica por qué.
- En 3 días, los isquiotibiales quedan en torno a 4 series semanales en cuerpo completo y PPL; es lo que reparte hoy el motor.
- Sin revisión por un entrenador (D8) ni traducciones revisadas por hablantes nativos.
- La pantalla de la app no se ha probado en un dispositivo.
