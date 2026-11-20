# Comunicación HTTP del panel Angular

## Transporte

Los servicios de dominio utilizan `ApiHttpClient` para comunicarse con la API. Mantienen sus rutas, tipos y contratos; el cliente común se ocupa del transporte. La autenticación y la renovación de sesión siguen siendo responsabilidad del interceptor.

`ApiHttpClient` conserva los errores originales de Angular (`HttpErrorResponse`): no los convierte en cadenas ni elimina el código HTTP. Así, los flujos que necesiten distinguir un estado pueden hacerlo sin duplicar la lógica de presentación.

## Errores visibles

Los componentes muestran los errores mediante `errorMessage(error, fallback?)`, definido en `core/api-error.ts`. La función aplica mensajes comunes en castellano a errores de conexión, validación, permisos, conflictos, límites de peticiones y fallos del servidor. No muestra al usuario detalles internos ni cuerpos de error sin normalizar.

El segundo argumento permite proporcionar un mensaje contextual de la pantalla. Los flujos que necesitan una reacción especial pueden inspeccionar el estado HTTP antes de mostrar el mensaje común.

## Carga y cancelación

- El estado de carga pertenece al componente o flujo que inicia la operación; se activa antes de la petición y se libera en `finally`.
- No se añade un estado global de carga: operaciones independientes pueden ejecutarse simultáneamente y no deben bloquear toda la interfaz.
- Para peticiones que pueden quedar obsoletas (búsquedas, filtros o cambios rápidos de periodo), se utiliza un identificador de petición y solo la respuesta más reciente puede actualizar el estado. Si en el futuro un flujo necesita cancelación real, debe exponer un Observable y cancelar su suscripción con RxJS (`switchMap` o `takeUntilDestroyed`); no se simula cancelación sobre las promesas de `ApiHttpClient`.
- Las acciones puntuales de guardar, eliminar o exportar mantienen su propio indicador de actividad y evitan envíos duplicados deshabilitando la acción mientras está en curso.

No se introduce una abstracción de loading/cancelación común mientras no exista un patrón compartido que aporte valor sin ocultar el ciclo de vida de cada operación.
