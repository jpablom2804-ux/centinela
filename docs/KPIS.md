# KPIs (la vista de gerente)

Objetivo: responder con datos si el agente vale lo que cuesta. Los precios por token y el costo por hora del analista son parámetros configurables (tabla `reportes.parametros`), nunca valores en el código.

## Indicadores

| KPI | Fórmula | Para qué sirve |
|---|---|---|
| Tasa de escalamiento | casos ÷ transacciones | Mide qué tanto filtran las reglas. Si sube, sube el costo del agente |
| Costo por caso analizado | tokens entrada × precio entrada + tokens salida × precio salida | Costo unitario de la IA |
| Costo diario de IA | Σ costo por caso del día | Control de gasto y alertas |
| Tasa de fallos del agente | casos con estado distinto de `completado` ÷ casos | Calidad técnica |
| Concordancia agente–analista | recomendaciones que coinciden con la decisión ÷ casos decididos | ¿El agente ayuda o confunde? |
| Fraudes detectados | casos confirmados como fraude por el analista | Valor de negocio |
| Falsos positivos | casos marcados legítimos por el analista ÷ casos decididos | Carga innecesaria para el analista |
| Tiempo medio de resolución | decisión del analista − creación del caso | Velocidad de respuesta |
| Ahorro estimado | casos × (min manual − min con agente) ÷ 60 × costo hora analista − costo de IA | Beneficio neto |
| ROI mensual | ahorro estimado ÷ costo de IA | El número que pediría un gerente |

"Minutos manuales" y "minutos con agente" son supuestos declarados en `reportes.parametros`, y el dashboard debe decir que son supuestos.

## Vistas para Power BI

| Vista | Contenido |
|---|---|
| `reportes.v_embudo_diario` | transacciones, casos, analizados por el agente, decididos, por día |
| `reportes.v_costo_agente` | costo, tokens y casos por día y por modelo |
| `reportes.v_calidad_agente` | concordancia, fallos y falsos positivos por semana |
| `reportes.v_roi_mensual` | ahorro estimado, costo de IA y ROI por mes |

El esquema `reportes` pertenece a `deteccion` (lo escribe su módulo `reportes`), y las vistas solo leen tablas de `deteccion`. Las métricas del agente llegan ahí dentro de `CasoAnalizado`; nunca se leen del esquema `agente`.

Power BI se conecta con el usuario `powerbi_ro`, que solo tiene `SELECT` sobre el esquema `reportes`.

## Alerta de costo diario

`reportes.v_costo_agente` compara el costo del día con el umbral de `reportes.parametros` y lo marca en el dashboard. No hay un tope en dinero dentro del agente porque el agente no conoce precios: su tope es de tokens por día (ver `docs/SEGURIDAD.md`).

## Experimento de optimización (fase 6)

Medir una semana simulada con la configuración base. Aplicar una optimización (caché de perfiles de cliente, o un modelo más barato para casos con puntaje bajo). Medir otra semana igual. Mostrar el antes y el después en el dashboard.
