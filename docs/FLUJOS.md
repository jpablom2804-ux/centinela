# Flujos

Cada paso tiene su comentario `// [FLUJO <NOMBRE> <N>]` en el código. Se llena a medida que se implementa; renumerar al cambiar pasos.

Flujos previstos:

- `ANALISIS_AGENTE` (fase 1): caso de entrada → ciclo del agente → herramientas → recomendación.
- `INGESTA` (fase 2): evento `TransaccionRecibida` → validación → reglas → caso.
- `CASO_SOSPECHOSO` (fase 3): caso creado → outbox → `agente` → `CasoAnalizado` → guardado en `deteccion`. La numeración continúa entre servicios.
- `DECISION_ANALISTA` (fase 4): panel → `/api` → registro de la decisión.

## ANALISIS_AGENTE

| Paso | Archivo | Método | Qué hace |
|---|---|---|---|
| | | | |
