# KPIs (la vista de gerente)

Objetivo: responder con datos si la mesa de servicio cumple sus SLA y si el agente vale lo que cuesta. Los precios por token, el costo por hora del técnico y los umbrales son parámetros configurables (tabla `reportes.parametros`), nunca valores en el código.

## Indicadores

| KPI | Fórmula | Para qué sirve |
|---|---|---|
| Tiempo medio de respuesta y de resolución (MTTR) | promedio por prioridad, descontando pausas | Velocidad del equipo frente a lo comprometido |
| Cumplimiento de SLA | tickets dentro de SLA ÷ tickets cerrados, por prioridad | El número que se reporta al negocio |
| Antigüedad del backlog | tickets abiertos por rango de días | Detecta lo que se está quedando atrás |
| Resolución en el primer contacto | resueltos sin reasignación ÷ resueltos | Calidad de la asignación inicial |
| Precisión del triaje | sugerencias aceptadas sin cambios ÷ sugerencias, por campo (categoría, prioridad) | ¿El agente ayuda o confunde? |
| Tasa de fallos del agente | triajes con estado distinto de `completado` ÷ triajes | Calidad técnica |
| Duplicados detectados | tickets agrupados por sugerencia del agente y confirmados por un técnico | Ahorro de trabajo repetido |
| Uso de la base de conocimiento | tickets resueltos con un artículo sugerido ÷ resueltos | Valor de la búsqueda por significado |
| Costo de IA por ticket | tokens entrada × precio entrada + tokens salida × precio salida, por modelo (más embeddings) | Costo unitario de la IA |
| Costo diario de IA | Σ costo por ticket del día | Control de gasto y alerta |
| Ahorro estimado | tickets × (min de triaje manual − min con agente) ÷ 60 × costo hora técnico − costo de IA | Beneficio neto |
| ROI mensual | ahorro estimado ÷ costo de IA | El número que pediría un gerente |

"Minutos de triaje manual" y "minutos con agente" son supuestos declarados en `reportes.parametros`, y el dashboard debe decir que son supuestos.

## Vistas para Power BI

| Vista | Contenido |
|---|---|
| `reportes.v_sla_diario` | tickets abiertos, cerrados, dentro y fuera de SLA, por día y prioridad |
| `reportes.v_backlog_antiguedad` | tickets abiertos por rango de días, prioridad y sistema |
| `reportes.v_calidad_triaje` | precisión por campo, fallos del agente y duplicados confirmados, por semana |
| `reportes.v_costo_agente` | costo, tokens y tickets por día y por modelo, con la marca de alerta |
| `reportes.v_roi_mensual` | ahorro estimado, costo de IA y ROI por mes |

El esquema `reportes` pertenece a `mesa-servicio`, y las vistas solo leen sus tablas. Las métricas del agente llegan ahí dentro de `TicketTriado`; nunca se leen del esquema `agente`.

Power BI se conecta con el usuario `powerbi_ro`, que solo tiene `SELECT` sobre el esquema `reportes`.

## Alerta de costo diario

`reportes.v_costo_agente` compara el costo del día con el umbral de `reportes.parametros` y lo marca en el dashboard. No hay un tope en dinero dentro del agente porque el agente no conoce precios: su tope es de tokens por día (ver `docs/SEGURIDAD.md`).

## Experimento de optimización (fase 7)

Medir una semana simulada (tickets del `generador`) con la configuración base. Aplicar una optimización: caché de búsquedas de similares, un modelo más barato para tickets simples, o descartar duplicados obvios antes de llamar al modelo. Medir otra semana igual. Mostrar el antes y el después en el dashboard.
