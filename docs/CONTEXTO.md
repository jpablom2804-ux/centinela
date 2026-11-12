# Contexto del proyecto

Resumen de la conversación en la que nació Centinela (noviembre de 2026). Sirve para que cualquier sesión de Claude Code entienda el porqué de las decisiones sin tener ese chat.

## De dónde viene la idea

La idea original era montar con Docker un sistema de agentes de IA coordinados por **n8n**: automatizaciones, aprender contenedores en el camino, verlo "con mentalidad de gerente" y medir el valor con un análisis financiero en **Power BI**.

El entorno de Juan Pablo:

- Laptop con Docker dentro de una VM de VirtualBox.
- Un servidor local con **Rocky Linux** donde ya levantó una página con Docker Compose y un reverse proxy.
- PC de escritorio con GTX 1650 Super (4 GB de VRAM): solo alcanza para modelos locales pequeños.
- Usa Claude Code con skills propias (buenas prácticas, impeccable, ui-ux-pro-max), Graphify para ahorrar tokens y subagentes para auditarse.

## Lo que aconsejó el profesor

1. **No usar n8n si se busca control.** Lo que se describe son microservicios. n8n puede "amarrar", sobre todo en el costo de tokens.
2. **Seguridad:** como mínimo el OWASP Top 10 para IA, pero también los OWASP de los demás ámbitos (web, APIs, y móvil si hubiera agentes en apps móviles).
3. **KPIs:** el problema actual son los tokens. Con más control se reducen mucho.
4. **Arquitectura:** microservicios con los agentes, Kubernetes, y un broker con arquitectura asíncrona de bus. "Hay mucha tela que cortar, depende de la necesidad."
5. **Perfil:** lo orientó hacia ingeniería de IA y le dijo que un proyecto así lo pone "en otro nivel" aunque sea junior.
6. **Primer paso:** "monta tu agente desde cero, con el lenguaje que quieras, C# o Rust".
7. **Análisis adversarios** son críticos; recomendó usar **herdr** (multiplexor de terminal para correr varios agentes de programación en paralelo, con su estado visible).
8. Quedó en que le cuente cuando tenga el agente funcionando.

## Decisiones tomadas en el chat

- **Se deja n8n.** El agente se programa a mano: el ciclo de llamada al modelo y uso de herramientas es lo que se quiere aprender y mostrar.
- **Dominio: antifraude asistido** (sector financiero, con "peso" para el portafolio). Principio central: la IA analiza y recomienda; un humano decide. Ningún banco aceptaría un agente que mueva dinero solo.
- **Control de tokens por arquitectura:** un motor de reglas barato filtra la gran mayoría y solo el 2–5 % sospechoso llega al agente. Además: caché, modelos baratos para tareas simples y límites por caso. Los tokens no desaparecen, se controlan.
- **Arquitectura y lenguajes:** aprobados en ADR-001 y ADR-002 (Aceptados el 2026-11-11). ADR-001 quedó reemplazado ese mismo día por ADR-003 (ver abajo).
- **Alcance:** se construye por fases y cada fase es un entregable presentable. Un proyecto terminado vale más que uno enorme a medias.
- **Título objetivo:** desarrollador backend con integración de IA (hay pocas vacantes de "AI Engineer" junior); este proyecto sirve para ambos.

## Cambio de dominio (11 nov 2026)

El mismo día se cambió el dominio de antifraude a una **mesa de servicio de TI para entidades financieras** con triaje asistido por IA ([ADR-003](decisiones/ADR-003-cambio-dominio-tickets.md)). Razones:

- Juan Pablo trabaja en soporte de TI: conoce el flujo de tickets de primera mano y puede defender cada decisión con experiencia real.
- El triaje con IA (clasificar, priorizar, detectar duplicados, sugerir soluciones desde una base de conocimiento) es uno de los usos empresariales de IA más comunes, así que es muy empleable.
- El principio no cambia: **la IA recomienda y el humano decide.**

Qué cambió en la arquitectura: `simulador` y `deteccion` se reemplazan por `mesa-servicio` (Java, monolito modular). El `agente` (C#) sigue como servicio aparte con RabbitMQ porque es lento, costoso y el más expuesto: el texto de los tickets lo escribe cualquier usuario. Se suma pgvector para buscar tickets y artículos similares, y el enmascarado de datos sensibles antes de llamar al LLM.

Ajustes acordados al aprobar: eventos de SLA en memoria (no por el broker), dos roles de BD para que la auditoría sea realmente de solo inserción, tablero solo Kanban y proveedor de embeddings a decidir en la fase 3. Se mantuvo el nombre **Centinela**.

## Dos niveles de agentes (no confundirlos)

| | Agentes que ayudan a programar | El agente que se construye |
|---|---|---|
| Qué es | Claude Code + skills + Graphify + herdr + subagentes | `services/agente` en C# |
| Para qué | Producir más rápido y con menos tokens | Es el producto del portafolio |

El análisis adversario aplica a los dos: un revisor atacante sobre el código que se genera, y una suite de ataques contra el agente del producto.

## Próximo paso acordado

Terminar la **fase 1** del roadmap (agente de triaje de consola en C# con 3 herramientas y pruebas de prompt injection), publicarla en GitHub y mandársela al profesor.
