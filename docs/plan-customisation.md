# Personalizar un plan generado (P2)

Estado: servidor y edición de sesión en la app implementados (sin probar en un dispositivo). La precedencia es la recomendación R6 del roadmap, **adoptada sin
aprobación expresa** (D6): conviene que el titular la confirme.

**Precedencia:** seguridad (dolor, limitaciones, ejercicios excluidos) > decisión del usuario > adaptación automática >
propuesta del motor.

## API

| Operación | Efecto |
|---|---|
| `PUT /api/v1/plans/current/sessions/{id}/exercises` | Reescribe una sesión pendiente de un plan generado: quitar, añadir, sustituir, reordenar, series, repeticiones, RIR, descanso, técnica y superseries. Mismo formato y validación que una rutina manual. `scope`: `session` (por defecto) o `plan` |
| `PUT /api/v1/plans/current/session-minutes` | Cambia el tiempo por sesión (35-180) desde el próximo bloque |
| `DELETE /api/v1/plans/current/adjustments` | Olvida los cambios duraderos. Las sesiones ya escritas no cambian |

- Premium, como el resto del plan generado; los permisos no cambian. Los planes manuales usan su editor.
- Un ejercicio nuevo tiene que ser compatible con el perfil. Con técnicas o superseries hace falta `techniques-1`.
- La vista de sesión indica `customised` cuando tiene valores fijados por el usuario.

## Qué se guarda

- La sesión se edita sobre su JSON: cargas objetivo y marcas de otras operaciones se conservan. `OriginalPrescriptionJson`
  sigue siendo la propuesta del motor y cada cambio queda en `plan_adaptations` con origen `user`.
- Solo se fija **lo que el usuario cambió**; cada ejercicio anota esos campos en `userFixed`.
- Con `scope: plan`, las decisiones se guardan por plan y título de sesión (`plan_session_adjustments`) y se aplican a las
  repeticiones pendientes y a cada bloque nuevo.

## Bloques nuevos

- Un ejercicio quitado no vuelve a esa sesión (salvo que la dejara vacía).
- Un ejercicio añadido o elegido ocupa el sitio de uno que haga su trabajo (mismo patrón y músculo principal) si el bloque
  eligió otro; si no lo hay, se añade. Una dosis fijada en un ejercicio que el bloque cambió lo recupera solo si puede
  sustituir a otro equivalente.
- En descargas y preparación se respeta qué ejercicios y en qué orden, **no** series, RIR, técnicas ni superseries.
- Una limitación o exclusión posterior gana: ese ejercicio no se recupera.
- Si cambian los días o el reparto y la sesión deja de existir con ese título, sus decisiones no se aplican.

## Adaptación automática

No cambia los valores fijados al reducir o devolver series, subir la reserva o con el feedback muscular; lo avisa con «Se han
respetado los valores que fijaste tú». Sí se aplican con **dolor pendiente** en el ejercicio, en las **descargas** y en la
vuelta tras una ausencia, que son protecciones.

## Pendiente

- En la app falta cambiar el tiempo por sesión.
- El aprendizaje longitudinal aún no distingue las dosis fijadas por el usuario de las del motor (solo puede bajar volumen).
- No se puede editar una sesión ya registrada ni crear sesiones nuevas.
