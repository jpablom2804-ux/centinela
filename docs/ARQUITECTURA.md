# Arquitectura

Decisión y alternativas en `docs/decisiones/ADR-003-cambio-dominio-tickets.md` (estado: Aceptado). ADR-001 queda como historia del dominio anterior.

## Vista general

```mermaid
flowchart LR
    FE[frontend<br/>React] -- /api --> MS[mesa-servicio<br/>Java · monolito modular]
    MS -- TicketCreado<br/>vía outbox, enmascarado --> MQ[(RabbitMQ)]
    MQ --> AGE[agente<br/>C# · worker]
    AGE -- HTTP solo lectura<br/>API interna --> MS
    AGE -- llamadas --> LLM[API del modelo]
    AGE -- TicketTriado --> MQ
    MQ --> MS
    MS --> PG[(PostgreSQL + pgvector<br/>esquemas por servicio)]
    BI[Power BI] -- powerbi_ro --> PG
    KC[Keycloak] -. OIDC .- FE
    GEN[generador<br/>herramienta de desarrollo] -. /api .-> MS
```

## Servicios

| Servicio | Responsabilidad | Escribe en | Lee de | Arquitectura interna |
|---|---|---|---|---|
| `mesa-servicio` | Tickets, SLA, tablero Kanban, cambios, base de conocimiento, auditoría y vistas de reporte. Enmascara datos sensibles antes de que salgan hacia el agente | Esquemas `mesa_servicio`, `auditoria` y `reportes` | Eventos | Monolito modular |
| `agente` | Triaje con IA: categoría, sistema afectado, prioridad sugerida, posibles duplicados, artículos sugeridos y borrador de respuesta. Solo recomienda | Esquema `agente` (mensajes y tickets procesados, métricas, tokens del día) | API interna de `mesa-servicio`, eventos | Hexagonal ligero |
| `frontend` | Portal del solicitante, bandeja y tablero del técnico, aprobación de cambios, vistas del auditor | — | `/api` de `mesa-servicio` | Bulletproof React |
| `generador` | Herramienta de desarrollo: crea tickets sintéticos para pruebas y KPIs. No es parte del producto | — (usa la API de `mesa-servicio`) | — | Capas clásicas |

## Módulos de `mesa-servicio`

| Módulo | Qué hace |
|---|---|
| `tickets` | Crear, asignar, cambiar estado, comentar. Tipos: incidente, solicitud, problema, cambio. Estados: `nuevo → triado → asignado → en_progreso → en_espera → resuelto → cerrado`. Guarda la sugerencia del agente (`TicketTriado`) y lo que el técnico hizo con ella. Expone la API interna de solo lectura para el agente, siempre con texto enmascarado |
| `sla` | Políticas por prioridad (P1–P4) con tiempo de respuesta y de resolución, en UTC y con calendario laboral. Pausa en `en_espera`. Alerta al 75 % y escala al vencer, con eventos en memoria (`SlaPorVencer`, `SlaVencido`) |
| `tablero` | Kanban de incidentes y solicitudes con límite de trabajo en curso por columna |
| `cambios` | Flujo de aprobación con segregación de funciones (solicitante ≠ aprobador ≠ ejecutor), ventana de ejecución y plan de reversa obligatorio |
| `conocimiento` | Artículos de solución con embeddings en pgvector. Los tickets resueltos alimentan sugerencias de artículos nuevos |
| `auditoria` | Bitácora de solo inserción de toda acción: quién, qué, cuándo, antes y después |
| `reportes` | Vistas SQL de KPIs para Power BI y `reportes.parametros` (precios, umbrales, supuestos) |

Los módulos se comunican por su `api/` y por eventos de aplicación en memoria; RabbitMQ solo se usa con el `agente`.

## Base de datos

Una base `centinela` en PostgreSQL con pgvector. Los roles y esquemas los crea `deploy/postgres/init/01-esquemas-y-usuarios.sh` al levantar el contenedor por primera vez.

| Rol | Para qué | Permisos |
|---|---|---|
| `mesa_servicio` | Dueño de `mesa_servicio`, `auditoria` y `reportes`. Solo lo usa Flyway | DDL en sus esquemas |
| `mesa_servicio_app` | Con este corre `mesa-servicio` | Lectura y escritura en `mesa_servicio`; `SELECT` e `INSERT` en `auditoria`; `SELECT` en `reportes` |
| `agente` | Dueño y usuario del esquema `agente` | Solo su esquema |
| `powerbi_ro` | Power BI | `SELECT` sobre `reportes`, sesiones de solo lectura |

Por qué dos roles en `mesa-servicio`: el dueño de una tabla siempre puede modificarla. Separar al dueño (migraciones) del rol con el que corre la aplicación es lo que hace que la bitácora sea realmente de solo inserción.

La extensión `vector` vive en el esquema `mesa_servicio`.

Keycloak (fase 4) usa su propia base de datos, `keycloak`, con su propio usuario, en la misma instancia de PostgreSQL (ADR-004).

## Varias réplicas y balanceo de carga

Desde la fase 8, `mesa-servicio` corre en al menos 2 réplicas detrás de nginx (en Rocky Linux) o de un Service con Ingress (en Kubernetes). Para que funcione sin sorpresas:

| Tema | Decisión |
|---|---|
| Entrada | nginx es la única puerta: termina TLS, aplica rate limiting y reparte con `upstream` (round robin). Detecta réplicas caídas de forma pasiva con `max_fails` y `fail_timeout`; en Kubernetes eso lo hacen las readiness probes |
| Sesiones | Sin sesiones pegajosas. La sesión del BFF vive en PostgreSQL (Spring Session JDBC), así que cualquier réplica atiende cualquier petición (ADR-004) |
| Tareas programadas | El reloj de SLA corre en una sola réplica a la vez: bloqueo en la base con ShedLock |
| Outbox | El publicador toma filas con `SELECT … FOR UPDATE SKIP LOCKED`, así que dos réplicas no publican el mismo evento |
| Migraciones | Flyway toma un bloqueo en la base; aun así, las migraciones se corren una vez antes de levantar las réplicas (en Kubernetes, como Job) |
| Rate limiting | En nginx, que ve todas las peticiones. Si cada réplica limitara por su cuenta, el límite real se multiplicaría por el número de réplicas |
| IP del cliente | nginx manda `X-Forwarded-For` y `X-Forwarded-Proto`; Spring usa `server.forward-headers-strategy: framework` y solo confía en el proxy |
| Trazabilidad | nginx genera `X-Request-ID` y se propaga como `correlationId` |
| `agente` | Escala como consumidores que compiten por la misma cola, con `prefetch` 1 y deduplicación por `ticketId` (`docs/EVENTOS.md`) |

## Interior del `agente` (hexagonal ligero)

```
services/agente/
├── src/Centinela.Agente/
│   ├── Dominio/          Ticket, Catalogos, Triaje (ResultadoTriaje, PresupuestoTriaje); sin dependencias externas
│   ├── Aplicacion/       CicloAgente (el loop), VerificadorEnmascarado, ValidadorTriaje, Prompts, RegistroMetricas
│   │   └── Prompts/      sistema.md (instrucciones fijas, recurso embebido)
│   ├── Puertos/          IClienteLlm, IHerramienta (IPublicadorEventos llega en la fase 3)
│   ├── Infraestructura/
│   │   ├── Llm/          ClienteAnthropicHttp (HttpClient a mano), OpcionesLlm
│   │   ├── Herramientas/ las 3 herramientas + datos-prueba.json (fase 1; en la fase 3, API interna de mesa-servicio)
│   │   └── Mensajeria/   consumidor y publicador de RabbitMQ (fase 3)
│   └── Program.cs        consola: dotnet run -- ticket.json
├── ejemplos/ticket.json
└── tests/
    ├── Centinela.Agente.Tests/       unitarios del ciclo con un IClienteLlm falso
    └── Centinela.Agente.Adversarial/ suite de ataques (datos en tests/adversarial/ de la raíz)
```

Justificación de las interfaces: `IClienteLlm` es una frontera real (API externa) y permite probar el ciclo sin gastar tokens; `IHerramienta` tiene varias implementaciones reales; `IPublicadorEventos` separará el dominio de RabbitMQ en la fase 3. `VerificadorEnmascarado` y `ValidadorTriaje` son estáticos: no tienen estado ni otra implementación posible.

## Estructura del repositorio

```
centinela/
├── AGENTS.md · CLAUDE.md · README.md · .env.example · compose.yml
├── services/
│   ├── mesa-servicio/
│   └── agente/
├── frontend/
├── tools/generador/          tickets sintéticos (solo desarrollo)
├── tests/adversarial/        casos de ataque compartidos (datos de entrada)
├── deploy/                   postgres/ (init de desarrollo), nginx/ y k8s/ (fases 8 y 9)
└── docs/
    ├── CONTEXTO.md · ARQUITECTURA.md · ROADMAP.md · SEGURIDAD.md
    ├── KPIS.md · EVENTOS.md · FLUJOS.md · DESPLIEGUE.md (fase 8)
    └── decisiones/
```
