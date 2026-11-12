# AGENTS.md — Centinela

Reglas para cualquier agente que trabaje en este repositorio (Claude Code, Cursor, Copilot). Responder siempre en español.

La base es el skill `buenas-practicas-java-react`. En Claude Code se usa la versión sincronizada, `anthropic-skills:buenas-practicas-java-react`, no la copia antigua de `~/.claude/skills/`. Este archivo manda sobre el skill: agrega el perfil del proyecto, las equivalencias para C# y las reglas específicas del dominio, del agente de IA y de mensajería.

## Antes de cualquier tarea

1. **Graphify primero.** Si existe `graphify-out/`, leer `graphify-out/GRAPH_REPORT.md` y navegar con el grafo. Nunca explorar todo el repo con lecturas de carpetas o grep masivo. Si el grafo no existe, avisar y correr `/graphify .`. **El grafo se actualiza una vez al cerrar cada fase** (o antes, si cambia la arquitectura o un ADR), con `/graphify . --update`, no después de cada cambio: el código se extrae gratis, pero cada pasada sobre documentos cuesta unos 100.000 tokens. Entre actualizaciones, el grafo da la estructura y lo reciente se ve con `git log`/`git diff` desde la última actualización, abriendo solo los archivos que cambiaron. `graphify-out/` no se versiona: es local y se regenera.
2. **Revisar decisiones pendientes.** Si algún ADR en `docs/decisiones/` está en estado **Propuesto**, presentárselo al usuario y esperar su aprobación antes de crear carpetas o código de servicios. Al aprobarse, cambiar el estado a **Aceptado** y actualizar el perfil.
3. **Ubicar la tarea en `docs/ROADMAP.md`.** No adelantar fases sin que el usuario lo pida. Marcar los ítems al terminarlos.
4. En la primera sesión, leer `docs/CONTEXTO.md` (de dónde salió el proyecto y por qué se tomaron las decisiones).

## Perfil del proyecto

| Campo | Valor |
|---|---|
| Qué es | Mesa de servicio de TI para entidades financieras: tickets, SLA, tablero Kanban, gestión de cambios y auditoría, con triaje asistido por IA. El agente recomienda; el técnico decide |
| Propósito | Portafolio para un puesto de desarrollador backend con integración de IA |
| Datos | 100 % sintéticos. Inspirados en tipos de casos reales de soporte, nunca copiados. Nunca datos reales de personas ni de clientes |
| Tamaño | Mediano |
| Equipo | 1 persona (Juan Pablo) |
| Arquitectura | Monolito modular + agente como servicio aparte con RabbitMQ — ADR-003 |
| Lenguajes | Java + Spring Boot (`mesa-servicio`, `generador`), C# .NET (`agente`), React + TypeScript (`frontend`) — ADR-002 |
| Base de datos | PostgreSQL + pgvector. `mesa-servicio` tiene dos roles: `mesa_servicio` (dueño de los esquemas, solo para Flyway) y `mesa_servicio_app` (con el que corre). `agente` tiene su propio usuario y esquema. `powerbi_ro` solo lee `reportes` |
| Mensajería | RabbitMQ, solo para lo que cruza servicios |
| Despliegue | Contenedores: compose en desarrollo → servidor Rocky Linux con nginx como reverse proxy → Kubernetes (k3s o kind) |
| Sistema destino | Rocky Linux (compatible con RHEL: aplican las reglas [RHEL] del skill) |
| Autenticación | OIDC con Keycloak (fase 4) y BFF: `mesa-servicio` es el cliente OIDC y el navegador solo tiene una cookie de sesión — ADR-004. Roles: `solicitante`, `tecnico`, `coordinador`, `aprobador_cambios`, `auditor`. Sin login hecho a mano |
| Maneja dinero | No directamente. Si aparecen montos (por ejemplo, el costo de un cambio o del agente), aplican las reglas de dinero del skill |
| Usuarios | Empleados que reportan, técnicos de soporte, coordinadores, comité de cambios, auditoría (simulados) |
| Integraciones | API de LLM configurable: Anthropic o compatible con OpenAI (Qwen para desarrollo) — ADR-006; proveedor de embeddings (a decidir en la fase 3), RabbitMQ, Power BI (solo lectura) |

## Arquitectura (resumen de ADR-003)

- Servicios: `services/mesa-servicio`, `services/agente`, `frontend` y la herramienta de desarrollo `tools/generador` (no es parte del producto). Detalle en `docs/ARQUITECTURA.md`.
- Cada servicio es dueño de sus esquemas. **Prohibido** leer o escribir tablas de otro servicio. El `generador` crea tickets por la API de `mesa-servicio` y no tiene usuario de BD.
- Entre servicios: eventos de RabbitMQ (`docs/EVENTOS.md`). Única llamada síncrona permitida: `agente` → API interna de solo lectura de `mesa-servicio`.
- Dentro de `mesa-servicio`, los eventos entre módulos (por ejemplo, los de SLA) son eventos de aplicación en memoria, no mensajes del broker.
- `mesa-servicio` es un monolito modular (`tickets`, `sla`, `tablero`, `cambios`, `conocimiento`, `auditoria`, `reportes`). Un módulo solo usa el `api/` de otro, nunca su `internal/`.
- `agente` es hexagonal ligero: el dominio no importa nada de HTTP, RabbitMQ ni del proveedor de LLM.
- `generador` usa capas clásicas.
- Cambiar o mezclar estilos requiere un ADR nuevo aprobado.

## Reglas de dominio

- **Auditoría de solo inserción.** Toda acción queda en la bitácora de `auditoria` (quién, qué, cuándo, antes y después). `mesa_servicio_app` solo tiene `SELECT` e `INSERT` sobre ese esquema; nunca `UPDATE`, `DELETE` ni `TRUNCATE`.
- **Segregación de funciones** validada en el backend: en un cambio, solicitante ≠ aprobador ≠ ejecutor.
- **Relojes de SLA** en UTC, con calendario laboral configurable. Se pausan en `en_espera` y cada pausa queda auditada.
- **Un ticket cerrado no se edita:** se reabre con justificación.
- **Control de acceso por rol y por recurso,** verificado en el backend: un solicitante solo ve sus tickets. Ticket ajeno e inexistente responden igual (`404`, mismo cuerpo y mismo camino de código), para no revelar qué existe ni por el mensaje ni por el tiempo de respuesta.

## Reglas de login y sesión (ADR-004)

- Ningún token en el navegador: ni en `localStorage`, ni en `sessionStorage`, ni en cookies legibles por JavaScript.
- Cookie de sesión `__Host-SESSION` con `HttpOnly`, `Secure` y `SameSite=Lax`; CSRF activo en todo lo que modifica; nuevo ID de sesión al iniciar sesión; inactividad 15 min y absoluta 8 h.
- Fuerza bruta: bloqueo **temporal** en Keycloak (nunca permanente) más rate limiting por IP en nginx.
- Mensajes de error de login idénticos para usuario inexistente y contraseña incorrecta, también en la recuperación de contraseña.
- MFA obligatorio para `coordinador`, `aprobador_cambios` y `auditor`.

## Reglas de despliegue y réplicas

Aprendidas en el proyecto anterior en Rocky Linux (scoopy) y en `docs/ARQUITECTURA.md`:

- **Compatible con Podman:** imágenes con nombre completo (`docker.io/...`) y montajes con etiqueta SELinux `:z`/`:Z`. SELinux nunca se desactiva.
- **Todo servicio debe aguantar varias réplicas:** sin estado en memoria entre peticiones, sin sesiones pegajosas, tareas programadas con bloqueo en la base (ShedLock), outbox con `SKIP LOCKED`.
- **Secretos como archivos, uno por servicio (ADR-005):** cada contenedor recibe solo sus secretos en `/run/secrets/` (Spring: `configtree:`; .NET: `AddKeyPerFile`). Nunca en variables de entorno, en la imagen, en logs ni en mensajes de error. `.env` solo lleva configuración no secreta. En desarrollo se generan con `./scripts/secretos.sh` y se guardan cifrados con SOPS + age en `secrets.enc.env`.
- **nginx es la única puerta:** TLS, rate limiting, headers de seguridad, `X-Request-ID` y `/actuator/` bloqueado. Ningún servicio publica puertos hacia afuera; en desarrollo solo en `127.0.0.1`.

## Principios y anti‑sobreingeniería

Se aplican tal cual las del skill: SOLID, DRY, KISS, YAGNI, composición sobre herencia, fail fast; la arquitectura más simple que cubra los requisitos reales; nada de interfaces de una sola implementación salvo en fronteras reales (BD, LLM, embeddings, broker, APIs externas) o cuando un test lo necesite; no generalizar antes de tres casos parecidos; cada clase, capa o dependencia nueva lleva una justificación de una línea; avisar si un cambio simple obliga a tocar más de 4 o 5 archivos.

## Equivalencias para C# (servicio `agente`)

| Regla del skill (Java) | En C# / .NET |
|---|---|
| `BigDecimal` desde String, `HALF_UP`, `compareTo` | `decimal` (parsear con `CultureInfo.InvariantCulture`), `Math.Round(x, 2, MidpointRounding.AwayFromZero)` |
| Bean Validation | DataAnnotations o FluentValidation, validando al recibir cada mensaje |
| `@RestControllerAdvice` con `{ codigo, mensaje }` | Middleware de excepciones / `ProblemDetails` con el mismo formato `{ codigo, mensaje }` |
| Perfiles de Spring y variables de entorno | `appsettings.json` + variables de entorno + `IOptions<T>` validado al arrancar |
| Logger en vez de `println` | `ILogger<T>` con logs estructurados en JSON a stdout |
| MDC con `X-Request-ID` | `ILogger.BeginScope` con `correlationId` |
| `/actuator/health` | Health checks de ASP.NET (`/health/live`, `/health/ready`) |
| JUnit + Testcontainers | xUnit + Testcontainers para .NET |
| Spotless / Checkstyle | `dotnet format` + analizadores con advertencias como error |
| Inyección por constructor | Inyección por constructor con el contenedor de .NET |
| OWASP Dependency-Check / npm audit | `dotnet list package --vulnerable` |

## Reglas del agente de IA (OWASP LLM y Agentic Top 10)

- **El agente nunca actúa, solo recomienda.** No asigna, no cambia prioridad, no cierra tickets ni envía respuestas. Todo lo aplica un humano.
- **La prioridad sugerida nunca se aplica sola,** aunque el agente diga P1.
- **Herramientas mínimas y de solo lectura:** `buscar_tickets_similares`, `buscar_articulos_conocimiento`, `consultar_estado_servicio` (incidentes abiertos por sistema). Cada una con propósito, parámetros validados y límite de resultados. Agregar una herramienta requiere justificarla y actualizar `docs/SEGURIDAD.md`.
- **El modelo nunca elige el ticket en curso.** Las herramientas no reciben `ticketId` del modelo: el ciclo lo toma del ticket que se está triando.
- **Enmascarar antes del LLM:** cédulas, números de cuenta y de tarjeta, correos y teléfonos. `mesa-servicio` enmascara antes de publicar el evento y en todo lo que devuelve la API interna; el agente vuelve a verificar antes de cada llamada al modelo. Lo mismo aplica al proveedor de embeddings.
- **El texto del ticket es no confiable,** igual que los tickets similares y los artículos de conocimiento. Va delimitado como datos, nunca concatenado a las instrucciones del sistema.
- **Salida estructurada y validada.** JSON validado contra un esquema: categoría, sistema afectado, prioridad sugerida con justificación, posibles duplicados, artículos sugeridos y borrador de respuesta. Si no valida, el ticket queda sin sugerencia para triaje manual.
- **Salida acotada:** `justificacion` y `borradorRespuesta` los genera el modelo, así que son texto no confiable. Tienen largo máximo (`docs/EVENTOS.md`), se validan al recibirse y se muestran como texto plano. El borrador solo se envía si un técnico lo aprueba.
- **Límites por ticket:** máximo de iteraciones, tiempo y presupuesto de tokens. Al superarlos, triaje manual.
- **Errores de la API del modelo clasificados:** saldo insuficiente, key inválida o petición inválida no se reintentan. El ticket va a triaje manual, el agente pausa las llamadas y se alerta, para no gastar reintentos ni llenar la DLQ. `429`, saturación, `5xx` y timeouts se reintentan con backoff, respetando `retry-after`, hasta un máximo.
- **Tope diario de tokens** en `agente` (desde la fase 3): al alcanzarlo, los tickets pasan a triaje manual sin llamar al modelo. La alerta de **costo** diario (en dinero) la calcula `reportes`, que es quien conoce los precios.
- **Medir siempre:** tokens de entrada y salida, modelo, duración e iteraciones de cada ticket (ver `docs/KPIS.md`).
- **Modelo, umbrales y precios por configuración**, nunca en el código.
- **La API key solo vive como secreto** (`dotnet user-secrets` en desarrollo, archivo montado en contenedor; ADR-005), nunca en variables de entorno, en `.env` ni en el código. No se registran en logs los prompts completos, los textos de tickets ni las respuestas crudas; solo IDs y métricas.
- **Todo cambio de prompt, herramienta o modelo obliga a correr la suite adversaria** (`tests/adversarial/`) antes de darlo por terminado.

## Reglas de mensajería

- Todo mensaje lleva `id`, `tipo`, `version`, `correlationId` y `ocurridoEn` (ISO-8601 UTC). Contratos en `docs/EVENTOS.md`; un cambio incompatible sube la versión.
- Consumidores idempotentes: registrar el `id` procesado y descartar duplicados. El `agente` además deduplica por `ticketId`: un ticket ya triado no vuelve a llamar al modelo (evita pagar dos veces si se cae antes del ack o si hay varias réplicas).
- Reintentos con backoff y un máximo; después, a la DLQ. Nunca reintentos infinitos.
- `mesa-servicio` publica con outbox transaccional.
- Si un mensaje lleva montos: texto decimal (`"1250.00"`) más la moneda, nunca número de punto flotante.

## Comentarios de flujo — obligatorios siempre en el backend

Formato del skill: `// [FLUJO <NOMBRE> <N>] <qué hace> → <a dónde sigue>` en Java y en C#. Como en el skill, son del backend: el frontend no los lleva, y su flujo en `docs/FLUJOS.md` empieza en el endpoint de `/api`. Cuando un flujo cruza servicios por un evento, la numeración continúa en el servicio que lo consume. Mantener `docs/FLUJOS.md` al día.

## Frontend

Bulletproof React y el flujo de diseño del skill: `/impeccable init` una vez, `/impeccable shape` con apoyo de ui-ux-pro-max (es un plugin: habilitarlo con `/plugin` antes de la fase 4, hoy está en el marketplace pero apagado), `/impeccable audit` por pantalla y critique/polish en las principales (proyecto mediano). Es una herramienta interna: **no** usar `/design-taste-frontend`. Prioridad: legibilidad, accesibilidad y poca animación.

## Revisión adversaria

- Las revisiones de seguridad las hace un subagente o una sesión aparte (por ejemplo, un panel de herdr) que **arranca sin el contexto de la implementación** y con el rol explícito de atacante.
- El revisor trabaja contra la lista de `docs/SEGURIDAD.md` y reporta hallazgos; no corrige.

## Al terminar una tarea, verificar

Además de la lista del skill:

- [ ] Compila y pasan los tests de los servicios tocados (`mvn verify` / `dotnet test` / `npm test`).
- [ ] Sin `double`/`float` para dinero en Java, C#, eventos ni BD.
- [ ] Ningún servicio accede a tablas de otro.
- [ ] Nada sin enmascarar llega al LLM ni a los logs.
- [ ] Acciones nuevas registradas en la bitácora de `auditoria`.
- [ ] Endpoints nuevos: `404` sin revelar existencia, CSRF activo y aguantan varias réplicas.
- [ ] Mensajes nuevos o cambiados documentados en `docs/EVENTOS.md`.
- [ ] Si se tocó el agente: suite adversaria en verde y métricas de tokens registradas.
- [ ] Comentarios de flujo y `docs/FLUJOS.md` al día.
- [ ] ROADMAP marcado. Si la tarea cierra una fase (o cambia la arquitectura), grafo de Graphify actualizado.

## Prohibido

Todo lo prohibido en el skill, más:

- Darle al agente herramientas de escritura o que ejecuten acciones.
- Aplicar automáticamente una sugerencia del agente (prioridad, asignación, cierre o respuesta).
- Mandar al LLM o al proveedor de embeddings texto sin enmascarar.
- Dar `UPDATE`, `DELETE` o `TRUNCATE` sobre `auditoria` al rol de la aplicación.
- Compartir tablas o clases entre servicios.
- Usar datos reales de personas.
- Escribir la API key, el modelo, los umbrales o los precios en el código.
- Pasar secretos como variables de entorno, guardarlos en `.env` o versionarlos sin cifrar.
- Agregar un servicio nuevo sin un ADR aprobado.
