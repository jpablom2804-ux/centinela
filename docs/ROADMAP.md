# Roadmap

Cada fase termina en algo presentable. No se adelanta una fase sin pedido explícito. Marcar `[x]` al terminar cada ítem.

## Fase 0 — Preparación
- [x] Aprobar ADR-002 y ADR-003 (cambiar a **Aceptado**)
- [x] `git init`, `.gitignore`, `.env.example`, primer commit
- [x] `compose.yml` con PostgreSQL + pgvector (roles y esquemas por servicio, más `powerbi_ro`) y RabbitMQ (con panel de administración)
- [x] `/graphify .`
- [x] Secretos como archivos montados por servicio, sin variables de entorno, y cifrados con SOPS + age (ADR-005)

**Hecho cuando:** `docker compose up` levanta Postgres y RabbitMQ sanos, y no hay secretos versionados.

## Fase 1 — El agente desde cero (C#, consola)  ← se manda al profesor
- [x] Proyecto .NET con la estructura de `docs/ARQUITECTURA.md`
- [x] Cliente HTTP a la API del modelo, hecho a mano con `HttpClient`. Modelo por configuración; API key con `dotnet user-secrets` en desarrollo y desde archivo (`AddKeyPerFile`) en contenedor, nunca en variables de entorno
- [x] Ciclo del agente: mensaje → el modelo pide una herramienta → se ejecuta → se devuelve el resultado → repetir hasta respuesta final
- [x] 3 herramientas de solo lectura sobre datos en memoria: `buscar_tickets_similares`, `buscar_articulos_conocimiento`, `consultar_estado_servicio`. Búsqueda simple por palabras clave (los embeddings llegan en la fase 3). Sin `ticketId` elegido por el modelo
- [x] Verificación de enmascarado antes de cada llamada al modelo
- [x] Salida JSON validada contra esquema, con los límites de largo de `docs/EVENTOS.md`; si es inválida, `estadoAnalisis = invalido`
- [x] Límites: iteraciones, tiempo y tokens por ticket
- [x] Errores de la API clasificados: saldo insuficiente o key inválida → sin reintentos, triaje manual y alerta; `429`, saturación y `5xx` → backoff con máximo
- [x] Registro de tokens, iteraciones y duración
- [x] Tests unitarios del ciclo con un `IClienteLlm` falso (sin gastar tokens)
- [x] Primeros 5 casos adversarios (`docs/SEGURIDAD.md`) pasando con el modelo falso
- [x] Proveedor configurable: Anthropic o compatible con OpenAI, para usar Qwen gratis en desarrollo (ADR-006)
- [x] Hallazgos del modelado de amenazas en el código del agente (`docs/SEGURIDAD.md`), cada uno con su caso: A1 (enmascarado normalizado y categorías nuevas), A15 (URLs y caracteres invisibles en la salida), A16 (tope de herramientas y presupuesto antes de enviar), A17 (falso positivo que aborta), A18 (errores por `type`/`code`), A26, A27 y A28
- [x] Casos 1 y 2 de la suite adversaria contra el modelo real (Qwen en Groq: 9 de 9 seguros; ver `services/agente/README.md`)
- [x] Primera corrida real: `dotnet run -- ejemplos/ticket.json` con Qwen en Groq
- [x] README de la fase con un ejemplo de entrada y salida, y qué se aprendió

**Hecho cuando:** `dotnet run -- ticket.json` devuelve un triaje válido, los tests pasan y la suite adversaria básica está en verde.

## Fase 2 — Núcleo de la mesa de servicio (Java)
- [x] Usar la LTS vigente de Java y de Spring Boot (Java 25 y Spring Boot 4.1.2, verificados el 2026-11-12)
- [x] `mesa-servicio` como monolito modular (límites verificados con Spring Modulith), Flyway con el rol `mesa_servicio` en un proceso aparte (perfil `migracion`, A14) y la app con `mesa_servicio_app`
- [x] Módulo `tickets`: tipos, estados y transiciones válidas; un ticket cerrado solo se reabre con justificación; un DTO por acción (A6) y largos máximos (A4)
- [x] Módulo `sla`: políticas P1–P4, calendario laboral, pausas, eventos en memoria al 75 % y al vencer, con ShedLock
- [x] Módulo `auditoria`: bitácora de solo inserción y encadenada con hash (A8), con un test que prueba que la app no puede hacer `UPDATE`, `DELETE` ni `TRUNCATE`
- [x] Roles y control de acceso por recurso (todavía sin Keycloak: usuarios de prueba); ticket ajeno e inexistente con el mismo `404`
- [x] Tests de transiciones de estado y de relojes de SLA con casos límite (pausas, fines de semana, feriados, cambio de día en UTC)
- [x] `generador` (`tools/generador`): crea tickets sintéticos por la API, reproducibles con una semilla, y respeta el límite por usuario
- [x] Límite de tickets por usuario en `mesa-servicio` (A5): 20 por hora, contado en la base

**Cerrada el 2026-11-12:** el generador creó 39 tickets por la API, cada uno con su reloj de SLA corriendo y su registro en la bitácora, y el límite cortó a cada usuario en 20.

**Hecho cuando:** se crean tickets por la API, los relojes de SLA corren y cada acción aparece en la bitácora.

## Fase 3 — Conexión con el agente
- [x] ADR del proveedor de embeddings: Voyage AI ([ADR-007](decisiones/ADR-007-proveedor-de-embeddings.md))
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
- [ ] Keycloak (OIDC) con los roles del perfil, en su propia base de datos
- [ ] BFF en `mesa-servicio` (ADR-004): `oauth2Login`, cookie `__Host-SESSION` (`HttpOnly`, `Secure`, `SameSite=Lax`), CSRF para la SPA, Spring Session JDBC, inactividad 15 min y absoluta 8 h
- [ ] Keycloak: fuerza bruta con bloqueo temporal, política de contraseñas, MFA para roles con poder, registro abierto apagado
- [ ] `404` igual para ticket ajeno e inexistente
- [ ] `/impeccable audit` por pantalla; critique y polish en bandeja y tablero

### Calidad de la interfaz (se revisa en cada pantalla)
- [ ] **Tokens de diseño** en `DESIGN.md` (`/impeccable init`): colores semánticos para prioridad P1–P4, SLA (en tiempo, al 75 %, vencido) y estados del ticket; espaciado, tipografía, radios y sombras. Nada de valores sueltos en los componentes
- [ ] **Jerarquía visual:** en la bandeja manda el SLA por vencer y la prioridad; la sugerencia del agente se distingue como *sugerencia*, nunca como un dato ya aplicado
- [ ] **Consistencia:** un solo componente compartido para la insignia de prioridad, el reloj de SLA, la tarjeta de ticket y el panel de sugerencia (`src/components`)
- [ ] **Estados de carga:** skeletons en bandeja y tablero; el triaje es asíncrono, así que el ticket muestra "analizando…" y, si el análisis queda `invalido` o `presupuesto_agotado`, "sin sugerencia: triaje manual" (nunca un spinner eterno)
- [ ] **Estados de la interfaz:** vacío, cargando, error, sin permiso y sesión expirada (inactividad de 15 min). "No encontrado" se ve igual para un ticket ajeno y uno inexistente
- [ ] **Estados de interacción:** hover, `focus-visible`, activo, deshabilitado y enviando; sin doble envío. En el Kanban, soltar en una columna que llegó a su límite de trabajo en curso se rechaza y se explica por qué
- [ ] **Accesibilidad (WCAG 2.2 AA):** Kanban operable con teclado (acción "mover a…", no solo arrastrar); prioridad y SLA nunca solo por color; avisos con `aria-live`; `prefers-reduced-motion`; contraste verificado. El texto del modelo se muestra como texto plano
- [ ] **Responsive:** el portal del solicitante se diseña primero para móvil; la bandeja y el tablero, primero para escritorio, y en móvil el Kanban pasa a pestañas por columna. Sin scroll horizontal de la página
- [ ] Los borradores de tickets no se guardan en `localStorage` (pueden llevar datos sensibles antes de enmascararse)

**Hecho cuando:** un solicitante reporta, un técnico ve la sugerencia del agente, la corrige y mueve el ticket en el tablero.

## Fase 5 — Gestión de cambios
- [ ] Módulo `cambios`: aprobación con segregación de funciones, ventana de ejecución y plan de reversa obligatorio
- [ ] Vistas para el auditor (bitácora filtrable, solo lectura)

**Hecho cuando:** un cambio pasa por solicitud → aprobación → ejecución con tres personas distintas, y el sistema rechaza que una misma persona haga dos de esos pasos.

## Fase 6 — Seguridad y revisión adversaria completa
- [ ] Suite adversaria completa (todos los casos de `docs/SEGURIDAD.md`, incluidos los de login, tiempos de respuesta y cookies)
- [ ] Revisión con un agente atacante sin contexto (subagente o panel de herdr) contra las listas OWASP
- [ ] Hallazgos corregidos, cada uno con su test
- [ ] Sección de seguridad en el README con los ataques probados y cómo se defienden

## Fase 7 — KPIs y Power BI
- [ ] Vistas SQL de `docs/KPIS.md` en el módulo `reportes`
- [ ] Alerta de costo diario contra el umbral de `reportes.parametros`
- [ ] Dashboard: SLA, backlog, calidad del triaje, costo contra ahorro
- [ ] Una optimización de costo con el antes y el después medidos

## Fase 8 — Despliegue en Rocky Linux
- [ ] Containerfiles multi-stage, usuario no root, imágenes con nombre completo (`docker.io/...`) para Podman; JVM con `-XX:MaxRAMPercentage=75`
- [ ] nginx sin root (`nginx-unprivileged`): frontend, proxy de `/api` y de Keycloak, TLS con redirección 80 → 443, `server_tokens off`, headers de seguridad con CSP y HSTS, `/actuator/` bloqueado, `X-Request-ID`, caché larga para `/assets/`
- [ ] Rate limiting en nginx sobre el login de Keycloak y `/api` (`429`)
- [ ] `mesa-servicio` con 2 réplicas detrás de `upstream` (sin sesiones pegajosas), ShedLock en el reloj de SLA, outbox con `SKIP LOCKED`; probar que tumbar una réplica no corta sesiones
- [ ] SELinux en enforcing (montajes `:z`/`:Z`, `httpd_can_network_connect`); firewalld solo SSH, 80 y 443
- [ ] SSH solo con llave, sin root; usuario de despliegue dedicado
- [ ] Secretos con `podman secret` o `systemd-creds` (cifrados y atados a la máquina); ningún `.env` con secretos en el servidor; procedimiento de rotación documentado
- [ ] Servicios como unidades de systemd (Podman Quadlet)
- [ ] Respaldos con `pg_dump` programado, copia fuera del servidor y restauración de prueba
- [ ] `docs/DESPLIEGUE.md`

## Fase 9 — Kubernetes
- [ ] Manifiestos en `deploy/k8s/` (k3s o kind): Deployments, Services, ConfigMaps, Secrets, Ingress
- [ ] Probes a los health checks, requests y limits
- [ ] Contenedores con UID arbitrario (compatibles con OpenShift)
- [ ] Escalar el agente a 2 réplicas y comprobar que no se duplican triajes
- [ ] `mesa-servicio` con 2 réplicas detrás del Ingress (rate limiting en el Ingress), migraciones como Job
- [ ] Secrets de Kubernetes con cifrado en reposo, generados desde SOPS
- [ ] Opcional, con su propio ADR: OpenBao con credenciales dinámicas de PostgreSQL

## Si sobra tiempo
- [ ] Backlog y sprints para problemas, cambios y mejoras, con velocidad del equipo (fuera del alcance por ADR-003)
