# ADR-001 — Arquitectura del sistema

- **Estado:** Reemplazado por [ADR-003](ADR-003-cambio-dominio-tickets.md) el 2026-11-11. Se conserva como historia: el dominio antifraude ya no se construye.
- **Fecha:** 2026-11-11

## Contexto

Centinela analiza transacciones financieras sintéticas. Un motor de reglas filtra la mayoría y solo los casos sospechosos pasan a un agente de IA. El agente investiga y recomienda. Un analista humano decide.

Lo que pesa en la decisión:

- **Requisito explícito de aprendizaje y portafolio.** El profesor recomendó microservicios, un broker con arquitectura asíncrona y Kubernetes. Es un requisito declarado, no un "por si crece".
- **Hay una frontera asíncrona real.** El agente tarda segundos por caso y cuesta dinero por llamada. No puede bloquear la ingesta de transacciones.
- **Escalado distinto.** El simulador y la detección manejan mucho volumen barato. El agente maneja poco volumen caro. Escalan por razones diferentes.
- **Aislamiento por seguridad.** El componente con LLM es el más expuesto (prompt injection, abuso de herramientas). Separarlo permite darle solo los permisos mínimos.
- **Equipo de 1 persona** con tiempo limitado (trabajo más licenciatura). Cada servicio extra es carga real de mantenimiento.

## Opciones evaluadas

| Arquitectura | Encaje | Ventajas aquí | Desventajas aquí | Complejidad que agrega |
|---|---|---|---|---|
| Capas clásicas | Bajo | Rápida de arrancar | Hay reglas, casos, agente e ingesta: demasiadas áreas para un solo paquete por capa. No cubre el requisito de broker ni de servicios separados | Baja |
| Monolito modular | Medio | Lo más simple que cubre los módulos reales. Un solo despliegue. Transacciones locales sin outbox | El agente bloquearía hilos o habría que simular asincronía dentro. No cumple el objetivo de aprender broker, microservicios y K8s. No aísla el componente LLM | Baja–media |
| Hexagonal en todo | Medio | Dominio testeable, proveedor de LLM intercambiable | Ceremonial para el simulador y para la parte CRUD de casos. No resuelve la asincronía por sí sola | Media |
| Monolito modular + hexagonal por módulo | Medio | Buen equilibrio interno | Mismo problema que el monolito: sin broker, sin aislamiento del agente, sin K8s significativo | Media |
| Microservicios completos (un servicio por cada concepto: ingesta, reglas, casos, agente, notificaciones, reportes…) | Bajo | Se ve "empresarial" | Sobreingeniería clara para 1 persona: 6+ despliegues, datos repartidos sin razón, consistencia eventual en todos lados | Muy alta |
| **Microservicios mínimos orientados a eventos** (3 servicios + frontend, partidos solo donde hay una razón real) | **Alto** | Cumple el requisito del profesor. Cada frontera tiene justificación: el simulador imita un sistema externo, la detección es el núcleo, el agente escala y se aísla aparte. RabbitMQ resuelve la asincronía | Consistencia eventual entre servicios; hace falta outbox, idempotencia y DLQ. Más infraestructura local (Postgres, RabbitMQ, 3 servicios) | Media–alta, pero justificada |

## Recomendación

**Microservicios mínimos orientados a eventos, con la arquitectura interna de cada servicio elegida por su complejidad:**

| Servicio | Por qué es un servicio aparte | Arquitectura interna |
|---|---|---|
| `simulador` | Representa el core bancario, un sistema externo. Nunca debería compartir código con la detección | Capas clásicas (es pequeño y casi sin lógica) |
| `deteccion` | Núcleo del negocio: ingesta, reglas, casos, decisiones del analista y vistas de reporte | Monolito modular (`ingesta`, `reglas`, `casos`, `reportes`, cada uno con `api/` e `internal/`) |
| `agente` | Lento, costoso y el más expuesto a ataques. Escala aparte y corre con permisos mínimos | Hexagonal ligero: dominio sin dependencias, puertos solo en fronteras reales (LLM, herramientas, mensajería) |
| `frontend` | Panel del analista | Bulletproof React |

Por qué: es la opción más simple que cumple los requisitos reales, incluido el de aprendizaje. Las tres fronteras se pueden defender en una entrevista una por una. La alternativa "microservicios completos" agrega servicios que no tienen razón propia.

## Qué tendría que cambiar para que otra opción fuera mejor

- Si el objetivo de aprender broker y K8s desapareciera, o el tiempo se volviera muy corto, lo correcto sería un **monolito modular** con el agente como módulo y una cola en base de datos. Es más simple y suficiente para el problema.
- Si en algún momento el motor de reglas necesitara escalar o desplegarse independiente de los casos (no hay evidencia hoy), recién ahí se separaría de `deteccion`.

## Consecuencias

- Cada servicio es dueño de su esquema en PostgreSQL. Ningún servicio lee tablas de otro.
- Comunicación por eventos en RabbitMQ (contratos en `docs/EVENTOS.md`). La única llamada síncrona permitida es la del agente a la API interna de solo lectura de `deteccion` para sus herramientas.
- `deteccion` publica eventos con **outbox transaccional** para no perder eventos si se cae entre guardar y publicar.
- Consumidores idempotentes, reintentos con backoff y colas de mensajes muertos (DLQ).
- ID de correlación obligatorio en logs y mensajes (en un proyecto mediano es "recomendado", pero con flujos que cruzan servicios por un broker es la única forma de seguir un caso).
