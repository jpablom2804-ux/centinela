# ADR-003 — Cambio de dominio: mesa de servicio con IA para entidades financieras

- **Estado:** Propuesto (pendiente de aprobación de Juan Pablo)
- **Fecha:** 2026-11-11
- **Reemplaza:** el dominio y la arquitectura de ADR-001. ADR-002 (lenguajes) se mantiene.

## Contexto

El dominio cambia de antifraude a una **herramienta de tickets y su resolución para entidades financieras**. Las razones:

- Juan Pablo trabaja en soporte de TI y conoce el flujo de tickets de primera mano. Puede defender cada decisión con experiencia real.
- El triaje con IA (clasificar, priorizar, detectar duplicados y sugerir soluciones desde una base de conocimiento) es uno de los casos de uso empresariales de IA más comunes, así que es muy empleable.
- El principio se mantiene: **la IA recomienda y el humano decide.**

Lo que pesa en la arquitectura:

- El núcleo es mayormente gestión de datos con reglas de proceso (estados, SLA, aprobaciones, auditoría). No hay un flujo masivo de eventos como en el antifraude.
- El agente sigue siendo lento, costoso y el componente más expuesto (el texto de los tickets lo escribe cualquier usuario).
- Sigue vigente el requisito de aprendizaje: broker, contenedores y Kubernetes.
- Equipo de 1 persona.

## Opciones evaluadas

| Arquitectura | Encaje | Ventajas aquí | Desventajas aquí | Complejidad que agrega |
|---|---|---|---|---|
| Capas clásicas | Bajo | Rápida para el CRUD de tickets | Tickets, SLA, cambios, conocimiento y auditoría crecerían enredados en un solo paquete por capa | Baja |
| Monolito modular (agente como módulo) | Medio | Lo más simple. Un despliegue | El agente comparte proceso con el núcleo: un ticket malicioso o una llamada lenta al LLM afecta todo. No aísla permisos. Broker casi sin uso | Baja–media |
| Hexagonal en todo | Bajo | Dominio aislado | Ceremonial para la mayoría de módulos, que son CRUD con reglas | Media |
| **Monolito modular + agente como servicio aparte, comunicados por RabbitMQ** | **Alto** | El núcleo queda simple. El agente se aísla, escala y se limita aparte. El broker tiene uso real (trabajo del agente, alertas de SLA, notificaciones). Kubernetes sigue teniendo sentido (escalar el agente) | Consistencia eventual entre los dos servicios: outbox, idempotencia y DLQ | Media |
| Microservicios por módulo (tickets, SLA, cambios, conocimiento…) | Bajo | — | Sobreingeniería: los módulos comparten transacciones y datos todo el tiempo | Muy alta |

## Recomendación

**Dos servicios más el frontend:**

| Servicio | Lenguaje | Arquitectura interna | Responsabilidad |
|---|---|---|---|
| `mesa-servicio` | Java + Spring Boot | Monolito modular | Tickets, SLA, tablero, cambios, conocimiento, auditoría, reportes |
| `agente` | C# .NET | Hexagonal ligero | Triaje con IA: clasificación, prioridad sugerida, duplicados, solución sugerida, borrador de respuesta |
| `frontend` | React + TypeScript | Bulletproof React | Portal para reportar, bandeja y tablero para técnicos, aprobaciones de cambios |
| `generador` (opcional, herramienta de desarrollo) | Java | Capas | Crea tickets sintéticos para pruebas y para alimentar los KPIs. No es parte del producto |

**Qué tendría que cambiar para que otra opción fuera mejor:** si se abandona el objetivo de aprender broker y K8s, conviene un monolito modular con el agente como módulo y una cola en la base de datos.

## Consecuencias

- Se eliminan `simulador` y `deteccion`, y con ellos los eventos de transacciones.
- PostgreSQL con **pgvector** para buscar tickets y artículos similares por significado.
- El agente nunca recibe datos sensibles sin enmascarar (cédulas, números de cuenta o tarjeta, correos y teléfonos de clientes).
- La bitácora de auditoría es de solo inserción: la aplicación no tiene permiso de `UPDATE` ni `DELETE` sobre esa tabla.
- Segregación de funciones: quien solicita un cambio no puede aprobarlo y quien lo aprueba no lo ejecuta, validado en el backend.
