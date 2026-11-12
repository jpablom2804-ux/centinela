# Roadmap

Cada fase termina en algo presentable. No se adelanta una fase sin pedido explícito. Marcar `[x]` al terminar cada ítem.

## Fase 0 — Preparación
- [x] Aprobar ADR-001 y ADR-002 (cambiar a **Aceptado**)
- [ ] `git init`, `.gitignore`, `.env.example`, primer commit
- [ ] `compose.yml` solo con PostgreSQL (esquemas y usuarios por servicio, más `powerbi_ro`) y RabbitMQ (con panel de administración)
- [ ] `/graphify .`

**Hecho cuando:** `docker compose up` levanta Postgres y RabbitMQ sanos, y no hay secretos versionados.

## Fase 1 — El agente desde cero (C#, consola)  ← se manda al profesor
- [ ] Proyecto .NET con la estructura de `docs/ARQUITECTURA.md`
- [ ] Cliente HTTP a la API del modelo, hecho a mano con `HttpClient` (modelo y API key por configuración)
- [ ] Ciclo del agente: mensaje → el modelo pide una herramienta → se ejecuta → se devuelve el resultado → repetir hasta respuesta final
- [ ] 3 herramientas de solo lectura sobre datos de prueba en memoria: `historial_cliente`, `perfil_gasto`, `transacciones_recientes`. Sin parámetro de cliente: el ciclo inyecta el caso en curso
- [ ] Límite de largo de `hallazgos` y `resumen` (`docs/EVENTOS.md`)
- [ ] Salida JSON validada contra esquema; si es inválida, `estadoAnalisis = invalido`
- [ ] Límites: iteraciones, tiempo y tokens por caso
- [ ] Registro de tokens, iteraciones y duración
- [ ] Tests unitarios del ciclo con un `IClienteLlm` falso (sin gastar tokens)
- [ ] Primeros 5 casos adversarios (`docs/SEGURIDAD.md`) pasando
- [ ] README de la fase con un ejemplo de entrada y salida, y qué se aprendió

**Hecho cuando:** `dotnet run -- caso.json` produce un análisis válido, los tests pasan y la suite adversaria básica está en verde.

## Fase 2 — Simulador y detección (Java)
- [ ] Usar la LTS vigente de Java y de Spring Boot (verificar al empezar, como dice ADR-002)
- [ ] `simulador`: genera transacciones normales y con patrones de fraude, publica `TransaccionRecibida`
- [ ] `deteccion`: monolito modular, Flyway, módulo `ingesta` consumiendo el evento
- [ ] Módulo `reglas` con 4 reglas y puntaje; tests de cada regla con casos límite
- [ ] Módulo `casos`: crea casos sobre el umbral
- [ ] Montos con `BigDecimal` y `NUMERIC`

**Hecho cuando:** el simulador corre y en la BD aparecen casos solo para las transacciones sospechosas.

## Fase 3 — Conexión asíncrona y confiabilidad
- [ ] Outbox en `deteccion` para publicar `CasoSospechosoCreado`
- [ ] El agente pasa de consola a worker que consume la cola
- [ ] Herramientas del agente apuntando a la API interna de solo lectura de `deteccion`
- [ ] `CasoAnalizado` guardado en `deteccion`
- [ ] Idempotencia, reintentos con backoff y DLQ en todos los consumidores; en el agente, deduplicación también por `casoId`
- [ ] Tope diario de tokens en el agente (al alcanzarlo, revisión manual sin llamar al modelo)
- [ ] API interna de `deteccion` consultada por `casoId`
- [ ] `correlationId` de punta a punta en logs JSON
- [ ] Testcontainers para la integración con Postgres y RabbitMQ

**Hecho cuando:** se puede seguir un caso por su `correlationId` desde la transacción hasta el análisis, y tumbar el agente a mitad no pierde casos.

## Fase 4 — Panel del analista
- [ ] Habilitar el plugin ui-ux-pro-max (`/plugin`)
- [ ] React + TypeScript con estructura Bulletproof
- [ ] `/impeccable init` y `/impeccable shape` (con ui-ux-pro-max)
- [ ] Bandeja de casos, detalle con hallazgos del agente, aprobar o rechazar con comentario
- [ ] Keycloak (OIDC) para el login de analistas
- [ ] `/impeccable audit` por pantalla; critique y polish en bandeja y detalle

**Hecho cuando:** un analista entra, revisa un caso con el análisis del agente y registra su decisión.

## Fase 5 — Seguridad y revisión adversaria completa
- [ ] Suite adversaria completa (todos los casos de `docs/SEGURIDAD.md`)
- [ ] Revisión con un agente atacante sin contexto (subagente o panel de herdr) contra las listas OWASP
- [ ] Hallazgos corregidos, cada uno con su test
- [ ] Sección de seguridad en el README con los ataques probados y cómo se defienden

## Fase 6 — KPIs y Power BI
- [ ] Vistas SQL de `docs/KPIS.md` en el módulo `reportes`
- [ ] Usuario `powerbi_ro` de solo lectura
- [ ] Alerta de costo diario contra el umbral de `reportes.parametros`
- [ ] Dashboard: costo contra ahorro, embudo transacciones → casos → agente → decisiones, concordancia agente vs. analista
- [ ] Una optimización de costo (caché o modelo más barato para casos simples) con el antes y el después medidos

## Fase 7 — Despliegue en Rocky Linux
- [ ] Containerfiles multi-stage, usuario no root
- [ ] nginx: frontend y proxy de `/api`, TLS, headers de seguridad
- [ ] SELinux en enforcing, firewalld solo 80/443
- [ ] `docs/DESPLIEGUE.md`

## Fase 8 — Kubernetes
- [ ] Manifiestos en `deploy/k8s/` (k3s o kind): Deployments, Services, ConfigMaps, Secrets, Ingress
- [ ] Probes a los health checks, requests y limits
- [ ] Contenedores con UID arbitrario (compatibles con OpenShift)
- [ ] Escalar el agente a 2 réplicas y comprobar que no se duplican análisis
