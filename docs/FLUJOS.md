# Flujos

Cada paso tiene su comentario `// [FLUJO <NOMBRE> <N>]` en el código del backend. Se llena a medida que se implementa; renumerar al cambiar pasos.

Flujos previstos:

- `TRIAJE_AGENTE` (fase 1): ticket de entrada → verificación de enmascarado → ciclo del agente → herramientas → triaje validado.
- `CREAR_TICKET` (fase 2): `/api` → validación y control de acceso → ticket guardado → auditoría → reloj de SLA iniciado.
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
