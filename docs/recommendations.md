# Recomendaciones explicables (P6-1)

`GET /api/v1/plans/current/recommendations` (Premium, como el resto de la ayuda automática; sin Premium responde `locked`).
Cada elemento tiene siempre: tipo, título, **por qué** (con sus cifras), sugerencia, **confianza** y la acción que el usuario
puede elegir. **Nada se aplica solo**: cambiar el ejercicio o el plan usa las operaciones ya existentes.

| Tipo | Regla (heurística de producto) | Confianza | Acción |
|---|---|---|---|
| `recurring_pain` | Dolor anotado en 2 o más de las últimas 4 veces del ejercicio | media | `swap` |
| `stalled_exercise` | 6 o más veces en bloques de entrenamiento a lo largo de 21 días o más, y las 3 últimas no superan la mejor estimada anterior; como mucho 3 | baja | `swap` |
| `low_adherence` | Menos del 60 % de las series efectivas previstas en 14 días (mínimo 20 previstas), entrenando ya desde antes | media | `adjust_plan` |

Solo se recomienda sobre ejercicios que siguen en sesiones pendientes. Ningún umbral está validado: señalan algo que mirar, no
diagnostican. Las reorganizaciones de días y las vueltas tras una ausencia siguen en sus propias pantallas.
