# Flujos

Cada paso tiene su comentario `// [FLUJO <NOMBRE> <N>]` en el código del backend. Se llena a medida que se implementa; renumerar al cambiar pasos.

Flujos previstos:

- `TRIAJE_AGENTE` (fase 1): ticket de entrada → verificación de enmascarado → ciclo del agente → herramientas → triaje validado.
- `CREAR_TICKET` (fase 2): `/api` → validación y control de acceso → ticket guardado → auditoría → reloj de SLA iniciado.
- `RELOJ_SLA` (fase 2): tarea programada → cálculo con calendario laboral y pausas → `SlaPorVencer` / `SlaVencido` en memoria → notificación o escalamiento.
- `TRIAJE_ASINCRONO` (fase 3): ticket creado → enmascarado → outbox → `agente` → `TicketTriado` → sugerencia guardada en `mesa-servicio`. La numeración continúa entre servicios.
- `APROBAR_CAMBIO` (fase 5): `/api` → segregación de funciones → aprobación → auditoría.

## TRIAJE_AGENTE

| Paso | Archivo | Método | Qué hace |
|---|---|---|---|
| | | | |
