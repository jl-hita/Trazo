# Estadísticas por músculo y por bloque (X-1, P4)

## Una sola definición de serie efectiva

Progreso, carga del plan y generador cuentan igual (`LoadMetrics`): una serie cuenta **1** para cada músculo principal y para
cada secundario que el catálogo marca como efectivo; los segmentos de una serie suman juntos **0,5**. Antes Progreso daba 0,5 a
cualquier secundario, así que sus cifras cambian (también las de semanas pasadas: se recalculan al leer, no se reescribe nada).
Es una convención de planificación, no una medida del estímulo.

## `GET /api/v1/plans/progress` (campos nuevos por músculo)

| Campo | Qué es |
|---|---|
| `setsByWeek` | Series efectivas hechas, 8 semanas |
| `sessionsByWeek` | Entrenos que de verdad lo trabajaron (al menos 2 series efectivas) |
| `prescribedByWeek` | Lo que pedía el plan activo cada semana; solo planes con fechas, `null` si no pedía nada |
| `prescribedPerSevenDays` | Lo que pide el bloque actual por cada siete días (también en planes sin fechas) |
| `priority` | Prioridad elegida por el usuario |

Un músculo pedido y no trabajado aparece con cero.

## `GET /api/v1/plans/current/blocks/summary`

Por cada bloque ya empezado: sesiones previstas y hechas, series efectivas pedidas y hechas por músculo, y el cambio de la
repetición máxima **estimada** en los ejercicios que comparte con el bloque de entrenamiento anterior. No se comparan descargas
ni preparaciones, ni ejercicios que solo están en uno de los bloques, ni series de más de 12 repeticiones. El tonelaje no se
usa como indicador de hipertrofia. Un cambio estimado no demuestra ganancia de músculo ni distingue técnica, sueño o error
de medida.
