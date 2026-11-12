# Roadmap

Cada fase termina en algo presentable. No se adelanta una fase sin pedido explícito. Marcar `[x]` al terminar cada ítem.

## Fase 0 — Preparación
- [x] Aprobar ADR-002 y ADR-003 (cambiar a **Aceptado**)
- [x] `git init`, `.gitignore`, `.env.example`, primer commit
- [x] `compose.yml` con PostgreSQL + pgvector (roles y esquemas por servicio, más `powerbi_ro`) y RabbitMQ (con panel de administración)
- [x] `/graphify .`

**Hecho cuando:** `docker compose up` levanta Postgres y RabbitMQ sanos, y no hay secretos versionados.

## Fase 1 — El agente desde cero (C#, consola)  ← se manda al profesor
- [ ] Proyecto .NET con la estructura de `docs/ARQUITECTURA.md`
- [ ] Cliente HTTP a la API del modelo, hecho a mano con `HttpClient` (modelo y API key por configuración)
- [ ] Ciclo del agente: mensaje → el modelo pide una herramienta → se ejecuta → se devuelve el resultado → repetir hasta respuesta final
- [ ] 3 herramientas de solo lectura sobre datos en memoria: `buscar_tickets_similares`, `buscar_articulos_conocimiento`, `consultar_estado_servicio`. Búsqueda simple por palabras clave (los embeddings llegan en la fase 3). Sin `ticketId` elegido por el modelo
- [ ] Verificación de enmascarado antes de cada llamada al modelo
- [ ] Salida JSON validada contra esquema, con los límites de largo de `docs/EVENTOS.md`; si es inválida, `estadoAnalisis = invalido`
- [ ] Límites: iteraciones, tiempo y tokens por ticket
- [ ] Registro de tokens, iteraciones y duración
- [ ] Tests unitarios del ciclo con un `IClienteLlm` falso (sin gastar tokens)
- [ ] Primeros 5 casos adversarios (`docs/SEGURIDAD.md`) pasando
- [ ] README de la fase con un ejemplo de entrada y salida, y qué se aprendió

**Hecho cuando:** `dotnet run -- ticket.json` devuelve un triaje válido, los tests pasan y la suite adversaria básica está en verde.

## Fase 2 — Núcleo de la mesa de servicio (Java)
- [ ] Usar la LTS vigente de Java y de Spring Boot (verificar al empezar, como dice ADR-002)
- [ ] `mesa-servicio` como monolito modular, Flyway con el rol `mesa_servicio` y la app con `mesa_servicio_app`
- [ ] Módulo `tickets`: tipos, estados y transiciones válidas; un ticket cerrado solo se reabre con justificación
- [ ] Módulo `sla`: políticas P1–P4, calendario laboral, pausas, eventos en memoria al 75 % y al vencer
- [ ] Módulo `auditoria`: bitácora de solo inserción, con un test que pruebe que la app no puede hacer `UPDATE` ni `DELETE`
- [ ] Roles y control de acceso por recurso (todavía sin Keycloak: usuarios de prueba)
- [ ] Tests de transiciones de estado y de relojes de SLA con casos límite (pausas, fines de semana, cambio de día en UTC)
- [ ] `generador`: crea tickets sintéticos por la API

**Hecho cuando:** se crean tickets por la API, los relojes de SLA corren y cada acción aparece en la bitácora.

## Fase 3 — Conexión con el agente
- [ ] ADR del proveedor de embeddings (externo o modelo local)
- [ ] Enmascarado en `mesa-servicio` y outbox para publicar `TicketCreado`
- [ ] El agente pasa de consola a worker que consume la cola
- [ ] API interna de solo lectura de `mesa-servicio` para las herramientas, siempre enmascarada
- [ ] Módulo `conocimiento` con pgvector y embeddings; herramientas con búsqueda por significado
- [ ] `TicketTriado` guardado en `mesa-servicio`, sin aplicarse solo
- [ ] Idempotencia, reintentos con backoff y DLQ en todos los consumidores; en el agente, deduplicación también por `ticketId`
- [ ] Tope diario de tokens en el agente (al alcanzarlo, triaje manual sin llamar al modelo)
- [ ] `correlationId` de punta a punta en logs JSON
- [ ] Testcontainers para la integración con Postgres y RabbitMQ

**Hecho cuando:** se puede seguir un ticket por su `correlationId` desde su creación hasta el triaje, y tumbar el agente a mitad no pierde tickets.

## Fase 4 — Frontend
- [ ] Habilitar el plugin ui-ux-pro-max (`/plugin`)
- [ ] React + TypeScript con estructura Bulletproof
- [ ] `/impeccable init` y `/impeccable shape` (con ui-ux-pro-max)
- [ ] Portal del solicitante: crear ticket y ver los suyos
- [ ] Bandeja del técnico con la sugerencia del agente: aceptar, corregir o descartar
- [ ] Tablero Kanban con límite de trabajo en curso (módulo `tablero`)
- [ ] Keycloak (OIDC) con los roles del perfil
- [ ] `/impeccable audit` por pantalla; critique y polish en bandeja y tablero

**Hecho cuando:** un solicitante reporta, un técnico ve la sugerencia del agente, la corrige y mueve el ticket en el tablero.

## Fase 5 — Gestión de cambios
- [ ] Módulo `cambios`: aprobación con segregación de funciones, ventana de ejecución y plan de reversa obligatorio
- [ ] Vistas para el auditor (bitácora filtrable, solo lectura)

**Hecho cuando:** un cambio pasa por solicitud → aprobación → ejecución con tres personas distintas, y el sistema rechaza que una misma persona haga dos de esos pasos.

## Fase 6 — Seguridad y revisión adversaria completa
- [ ] Suite adversaria completa (todos los casos de `docs/SEGURIDAD.md`)
- [ ] Revisión con un agente atacante sin contexto (subagente o panel de herdr) contra las listas OWASP
- [ ] Hallazgos corregidos, cada uno con su test
- [ ] Sección de seguridad en el README con los ataques probados y cómo se defienden

## Fase 7 — KPIs y Power BI
- [ ] Vistas SQL de `docs/KPIS.md` en el módulo `reportes`
- [ ] Alerta de costo diario contra el umbral de `reportes.parametros`
- [ ] Dashboard: SLA, backlog, calidad del triaje, costo contra ahorro
- [ ] Una optimización de costo con el antes y el después medidos

## Fase 8 — Despliegue en Rocky Linux
- [ ] Containerfiles multi-stage, usuario no root
- [ ] nginx: frontend y proxy de `/api`, TLS, headers de seguridad
- [ ] SELinux en enforcing, firewalld solo 80/443
- [ ] `docs/DESPLIEGUE.md`

## Fase 9 — Kubernetes
- [ ] Manifiestos en `deploy/k8s/` (k3s o kind): Deployments, Services, ConfigMaps, Secrets, Ingress
- [ ] Probes a los health checks, requests y limits
- [ ] Contenedores con UID arbitrario (compatibles con OpenShift)
- [ ] Escalar el agente a 2 réplicas y comprobar que no se duplican triajes

## Si sobra tiempo
- [ ] Backlog y sprints para problemas, cambios y mejoras, con velocidad del equipo (fuera del alcance por ADR-003)
