# AGENTS.md — Centinela

Reglas para cualquier agente que trabaje en este repositorio (Claude Code, Cursor, Copilot). Responder siempre en español.

La base es el skill `buenas-practicas-java-react`. En Claude Code se usa la versión sincronizada, `anthropic-skills:buenas-practicas-java-react`, no la copia antigua de `~/.claude/skills/`. Este archivo manda sobre el skill: agrega el perfil del proyecto, las equivalencias para C# y las reglas específicas de agentes de IA y mensajería.

## Antes de cualquier tarea

1. **Graphify primero.** Si existe `graphify-out/`, leer `graphify-out/GRAPH_REPORT.md` y navegar con el grafo. Nunca explorar todo el repo con lecturas de carpetas o grep masivo. Si el grafo no existe o está desactualizado, avisar y correr `/graphify .`. Después de cambios, `/graphify . --update`. `graphify-out/` no se versiona: es local y se regenera.
2. **Revisar decisiones pendientes.** Si algún ADR en `docs/decisiones/` está en estado **Propuesto**, presentárselo al usuario y esperar su aprobación antes de crear carpetas o código de servicios. Al aprobarse, cambiar el estado a **Aceptado** y actualizar el perfil.
3. **Ubicar la tarea en `docs/ROADMAP.md`.** No adelantar fases sin que el usuario lo pida. Marcar los ítems al terminarlos.
4. En la primera sesión, leer `docs/CONTEXTO.md` (de dónde salió el proyecto y por qué se tomaron las decisiones).

## Perfil del proyecto

| Campo | Valor |
|---|---|
| Qué es | Análisis antifraude asistido: reglas filtran, un agente de IA investiga y recomienda, un analista humano decide |
| Propósito | Portafolio para un puesto de desarrollador backend con integración de IA |
| Datos | 100 % sintéticos (generador propio o PaySim). Nunca datos reales de personas ni de clientes |
| Tamaño | Mediano |
| Equipo | 1 persona (Juan Pablo) |
| Arquitectura | Microservicios mínimos orientados a eventos — ADR-001 |
| Lenguajes | Java + Spring Boot (`simulador`, `deteccion`), C# .NET (`agente`), React + TypeScript (`frontend`) — ADR-002 |
| Base de datos | PostgreSQL. Un usuario por servicio, dueño de sus esquemas: `simulador`, `deteccion` (más `reportes`), `agente`. Usuario `powerbi_ro` de solo lectura sobre `reportes` |
| Mensajería | RabbitMQ |
| Despliegue | Contenedores: compose en desarrollo → servidor Rocky Linux con nginx como reverse proxy → Kubernetes (k3s o kind) |
| Sistema destino | Rocky Linux (compatible con RHEL: aplican las reglas [RHEL] del skill) |
| Autenticación | OIDC con Keycloak para analistas (fase 4). Sin login hecho a mano |
| Maneja dinero | Sí |
| Usuarios | Analistas de fraude (simulados) |
| Integraciones | API de LLM (Anthropic), RabbitMQ, Power BI (solo lectura) |

## Arquitectura (resumen de ADR-001)

- Servicios: `services/simulador`, `services/deteccion`, `services/agente`, `frontend`. Detalle en `docs/ARQUITECTURA.md`.
- Cada servicio es dueño de sus esquemas (`deteccion` es dueño de `deteccion` y `reportes`). **Prohibido** leer o escribir tablas de otro servicio.
- Entre servicios: eventos de RabbitMQ (`docs/EVENTOS.md`). Única llamada síncrona permitida: `agente` → API interna de solo lectura de `deteccion`.
- `deteccion` es un monolito modular (`ingesta`, `reglas`, `casos`, `reportes`). Un módulo solo usa el `api/` de otro, nunca su `internal/`.
- `agente` es hexagonal ligero: el dominio no importa nada de HTTP, RabbitMQ ni del proveedor de LLM.
- `simulador` usa capas clásicas.
- Cambiar o mezclar estilos requiere un ADR nuevo aprobado.

## Principios y anti‑sobreingeniería

Se aplican tal cual las del skill: SOLID, DRY, KISS, YAGNI, composición sobre herencia, fail fast; la arquitectura más simple que cubra los requisitos reales; nada de interfaces de una sola implementación salvo en fronteras reales (BD, LLM, broker, APIs externas) o cuando un test lo necesite; no generalizar antes de tres casos parecidos; cada clase, capa o dependencia nueva lleva una justificación de una línea; avisar si un cambio simple obliga a tocar más de 4 o 5 archivos.

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

- **El agente nunca actúa, solo recomienda.** No tiene herramientas que escriban, bloqueen cuentas ni muevan dinero. La decisión final siempre es de un humano.
- **Herramientas mínimas y de solo lectura.** Cada herramienta tiene un propósito, parámetros validados y límites (por ejemplo, máximo de transacciones devueltas). Agregar una herramienta requiere justificarla y actualizar `docs/SEGURIDAD.md`.
- **El modelo nunca elige de quién son los datos.** Las herramientas no reciben `clienteId` ni `casoId` como parámetro del modelo: el ciclo los toma del caso en curso, y la API interna de `deteccion` se consulta por `casoId` y deduce el cliente.
- **Todo dato de una transacción es no confiable.** Descripciones, nombres de comercio y cualquier texto van delimitados como datos, nunca concatenados en las instrucciones del sistema.
- **Salida estructurada y validada.** El agente responde JSON que se valida contra un esquema. Si no valida, el caso pasa a revisión manual sin recomendación. Nunca se aprueba por defecto.
- **Límites por caso:** máximo de iteraciones del ciclo, tiempo máximo y presupuesto de tokens. Al superarlos, el caso va a revisión manual.
- **Tope diario de tokens** en `agente` (desde la fase 3): al alcanzarlo, los casos pasan a revisión manual sin llamar al modelo. La alerta de **costo** diario (en dinero) la calcula `reportes`, que es quien conoce los precios.
- **Salida acotada:** `hallazgos` y `resumen` son texto generado por el modelo, o sea no confiable. Tienen largo máximo (`docs/EVENTOS.md`), se validan al recibirse y se muestran como texto plano.
- **Medir siempre:** tokens de entrada y salida, modelo, duración e iteraciones de cada caso (ver `docs/KPIS.md`).
- **Modelo y precios por configuración**, nunca en el código.
- **La API key solo vive en variables de entorno o Secrets.** No se registran en logs los prompts completos ni las respuestas crudas; solo IDs y métricas.
- **Todo cambio de prompt, herramienta o modelo obliga a correr la suite adversaria** (`tests/adversarial/`) antes de darlo por terminado.

## Reglas de mensajería

- Todo mensaje lleva `id`, `tipo`, `version`, `correlationId` y `ocurridoEn` (ISO-8601 UTC). Contratos en `docs/EVENTOS.md`; un cambio incompatible sube la versión.
- Consumidores idempotentes: registrar el `id` procesado y descartar duplicados. El `agente` además deduplica por `casoId`: un caso ya analizado no vuelve a llamar al modelo (evita pagar dos veces si se cae antes del ack o si hay varias réplicas).
- Reintentos con backoff y un máximo; después, a la DLQ. Nunca reintentos infinitos.
- `deteccion` publica con outbox transaccional.
- Montos en los mensajes como texto decimal (`"1250.00"`) más la moneda, nunca como número de punto flotante.

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
- [ ] Mensajes nuevos o cambiados documentados en `docs/EVENTOS.md`.
- [ ] Si se tocó el agente: suite adversaria en verde y métricas de tokens registradas.
- [ ] Comentarios de flujo y `docs/FLUJOS.md` al día.
- [ ] ROADMAP marcado y grafo de Graphify actualizado.

## Prohibido

Todo lo prohibido en el skill, más:

- Darle al agente herramientas de escritura o que ejecuten acciones.
- Aprobar un caso automáticamente cuando el agente falla, se agota o devuelve algo inválido.
- Compartir tablas o clases entre servicios.
- Usar datos reales de personas.
- Escribir la API key, el modelo o los precios en el código.
- Agregar un servicio nuevo sin un ADR aprobado.
