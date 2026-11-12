# Flujos

Cada paso tiene su comentario `// [FLUJO <NOMBRE> <N>]` en el código del backend. Se llena a medida que se implementa; renumerar al cambiar pasos.

Flujos previstos:

- `TRIAJE_AGENTE` (fase 1): ticket de entrada → verificación de enmascarado → ciclo del agente → herramientas → triaje validado.
- `CREAR_TICKET` (fase 2): `/api` → validación y control de acceso → ticket guardado → auditoría → reloj de SLA iniciado.
- `CAMBIAR_ESTADO` (fase 2): `/api` → ticket visible → permiso y transición válida → auditoría → evento → el SLA pausa, reanuda o detiene.
- `RELOJ_SLA` (fase 2): tarea programada → cálculo con calendario laboral y pausas → `SlaPorVencer` / `SlaVencido` en memoria → notificación o escalamiento.
- `TRIAJE_ASINCRONO` (fase 3): ticket creado → enmascarado → outbox → `agente` → `TicketTriado` → sugerencia guardada en `mesa-servicio`. La numeración continúa entre servicios.
- `APROBAR_CAMBIO` (fase 5): `/api` → segregación de funciones → aprobación → auditoría.

## TRIAJE_AGENTE

Servicio `agente`, rutas bajo `services/agente/src/Centinela.Agente/`.

| Paso | Archivo | Método | Qué hace |
|---|---|---|---|
| 1 | `Program.cs` | (nivel superior) | Lee el ticket de un archivo JSON, lo valida y arma los servicios con la configuración validada |
| 2 | `Aplicacion/CicloAgente.cs` | `TriarAsync` | Abre el presupuesto del ticket (tiempo, iteraciones, tokens) y arma el primer mensaje con el ticket delimitado |
| 3 | `Aplicacion/CicloAgente.cs` | `EjecutarAsync` | Antes de cada llamada revisa que nada sin enmascarar vaya al modelo; si encuentra algo, corta con `error` sin llamarlo |
| 4 | `Aplicacion/CicloAgente.cs` | `LlamarModeloAsync` | Llama al modelo; reintenta con backoff solo los errores reintentables, hasta un máximo |
| 5 | `Infraestructura/Llm/ClienteAnthropicHttp.cs` | `EnviarAsync` | `POST /v1/messages` hecho a mano, clasifica los errores e interpreta `stop_reason` |
| 6 | `Aplicacion/CicloAgente.cs` | `EjecutarHerramientasAsync` | Ejecuta las herramientas pedidas con el contexto inyectado y devuelve todos los resultados juntos; vuelve al paso 3 |
| 6b | `Infraestructura/Herramientas/Herramienta*.cs` | `EjecutarAsync` | Valida la entrada que eligió el modelo y busca en los datos de prueba; el resultado va marcado como dato no confiable |
| 7 | `Aplicacion/ValidadorTriaje.cs` | `Validar` | Valida el JSON final contra catálogos, largos e IDs vistos en las herramientas: `completado` o `invalido` |
| 7b | `Aplicacion/CicloAgente.cs` | `EjecutarAsync` | Si no valida y quedan correcciones (`Agente:MaxReparaciones`, 1 por defecto), le dice al modelo qué falló con mensajes propios, sin repetir su texto, y vuelve al paso 3 dentro del mismo presupuesto |
| 8 | `Aplicacion/RegistroMetricas.cs` | `Registrar` | Registra estado, modelo, tokens, iteraciones y duración (solo IDs y números); `Program` imprime el resultado |

## CREAR_TICKET

Servicio `mesa-servicio`, rutas bajo `services/mesa-servicio/src/main/java/cr/centinela/mesaservicio/`.

| Paso | Archivo | Método | Qué hace |
|---|---|---|---|
| 1 | `tickets/internal/TicketsControlador.java` | `crear` | Recibe el ticket del usuario autenticado y valida el cuerpo: solo los campos de esa acción (A6) y con largo máximo (A4) |
| 2 | `tickets/internal/ServicioTickets.java` | `crear` | Valida el rol y crea el ticket en `nuevo` con la prioridad inicial (la ajusta un técnico, nunca el agente solo) |
| 3 | `tickets/internal/ServicioTickets.java` | `crear` → `auditoria/RegistroAuditoria.registrar` | Registra la creación en la bitácora de solo inserción, encadenada con hash, en la misma transacción |
| 4 | `tickets/internal/ServicioTickets.java` | `crear` | Publica `TicketCreado` en memoria |
| 5 | `sla/internal/ServicioSla.java` | `alCrearTicket` | Arranca el reloj de resolución |
| 6 | `tickets/internal/ServicioTickets.java` | `crear` → `PublicadorTicketCreado.registrar` | Deja `TicketCreado` en el outbox, en la misma transacción (sigue en `TRIAJE_ASINCRONO` 1); el controlador responde `201` |

## TRIAJE_ASINCRONO

Empieza en `mesa-servicio` (rutas bajo `services/mesa-servicio/src/main/java/cr/centinela/mesaservicio/`) y sigue en el `agente`.

| Paso | Archivo | Método | Qué hace |
|---|---|---|---|
| 1 | `tickets/internal/PublicadorTicketCreado.java` | `registrar` | Enmascara título, descripción y sistema reportado (`plataforma/Enmascarador`); el ticket guardado no cambia |
| 2 | `plataforma/mensajeria/Outbox.java` | `registrar` | Arma el sobre (`id`, `tipo`, `version`, `correlationId`, `ocurridoEn`) y lo guarda sin publicar; exige estar dentro de la transacción del cambio |
| 3 | `plataforma/mensajeria/RelevoOutbox.java` | `publicarPendientes` | Cada 2 s toma un lote con `FOR UPDATE SKIP LOCKED` (varias réplicas sin pisarse), publica en el exchange `tickets` y marca publicado solo con el ack del broker y si llegó a una cola; si no, reintenta con backoff hasta un máximo |
| 4… | `agente` | — | Fase 3, siguiente punto: el worker consume `agente.tickets-creados` |

## CAMBIAR_ESTADO

| Paso | Archivo | Método | Qué hace |
|---|---|---|---|
| 1 | `tickets/internal/TicketsControlador.java` | `cambiarEstado` | Recibe el estado destino y la justificación |
| 2 | `tickets/internal/ServicioTickets.java` | `cambiarEstado` | Carga el ticket visible (ajeno e inexistente: el mismo `404`), valida rol y transición; reabrir exige justificación; registra en la bitácora |
| 3 | `tickets/internal/ServicioTickets.java` → `sla/internal/ServicioSla.java` | `cambiarEstado` → `alCambiarEstado` | Publica `EstadoTicketCambiado`; el SLA se pausa en `en_espera`, se detiene en `resuelto`/`cerrado` y se reanuda al volver a trabajar, y cada pausa queda en la bitácora |

## RELOJ_SLA

| Paso | Archivo | Método | Qué hace |
|---|---|---|---|
| 1 | `sla/internal/VigilanteSla.java` | `revisar` | Tarea programada con ShedLock: una sola réplica revisa a la vez |
| 2 | `sla/internal/VigilanteSla.java` | `revisar` | Calcula el tiempo hábil consumido (`CalendarioLaboral`: jornada, fines de semana y feriados en la zona local) contra el objetivo de la prioridad |
| 3 | `sla/internal/VigilanteSla.java` → `sla/internal/AvisosSla.java` | `revisar` → `porVencer` / `vencido` | Al 75 % publica `SlaPorVencer` y al 100 % `SlaVencido`, una sola vez cada uno, y los registra en la bitácora |
