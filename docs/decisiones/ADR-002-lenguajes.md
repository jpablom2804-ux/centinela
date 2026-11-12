# ADR-002 — Lenguajes por servicio

- **Estado:** Aceptado (aprobado por Juan Pablo el 2026-11-11)
- **Fecha:** 2026-11-11
- **Actualizado:** 2026-11-11 por ADR-003. La decisión no cambia; solo los nombres de los servicios Java (ahora `mesa-servicio` y el `generador` opcional).

## Contexto

- El profesor recomendó construir el agente desde cero "con el lenguaje que quieras, C# o Rust".
- Juan Pablo ya domina Java + Spring Boot + React (TFG) y tiene el skill `buenas-practicas-java-react` con todas sus reglas para ese stack.
- Objetivo: empleo como desarrollador backend con integración de IA en Heredia/San José, donde Java y .NET tienen mucha demanda.
- Tiempo limitado: trabajo con turnos rotativos y último año de licenciatura.

## Opciones evaluadas

| Opción | Encaje | Ventajas aquí | Desventajas aquí |
|---|---|---|---|
| Todo Java | Medio | Un solo toolchain. El skill aplica completo. Lo más rápido | No sigue la sugerencia del profesor. No aprovecha la ventaja típica de microservicios (cada uno con su stack) |
| Todo C# | Medio | Sigue al profesor. Un solo toolchain nuevo | Hay que reaprender todo lo que ya sale bien en Java. El skill no aplica directo en ningún servicio |
| **Java en el núcleo (`mesa-servicio`), C# en `agente`** | **Alto** | El grueso del sistema usa lo que ya domina (y el skill aplica tal cual). El agente "desde cero", el aprendizaje nuevo, queda en el servicio más pequeño. Demuestra en la práctica que una frontera de microservicio permite otro stack. Suma .NET al CV | Dos toolchains, dos frameworks de pruebas, dos pipelines |
| Rust en `agente` | Bajo | Rendimiento y seguridad de memoria | Curva muy fuerte para la parte que ya es la más difícil conceptualmente. Pocas vacantes junior de Rust en el país |

## Recomendación

**Java 21+ con Spring Boot en `mesa-servicio` (y en el `generador`). C# con .NET 10 (LTS) en `agente`. React + TypeScript en `frontend`.**

El agente en C# se programa sin frameworks de agentes: el ciclo (llamar al modelo → ejecutar herramienta → devolver resultado → repetir) se escribe a mano con `HttpClient` contra la API del modelo. Ese ciclo es justamente lo que se quiere aprender y mostrar.

Verificar las versiones LTS vigentes al empezar cada servicio.

## Qué tendría que cambiar para que otra opción fuera mejor

Si el tiempo se pone muy apretado, conviene **todo Java**, con el agente también hecho a mano (sin Spring AI) para no perder el "desde cero". Se pierde .NET en el CV, pero se gana velocidad.

## Consecuencias

- Las reglas del skill se aplican a los servicios Java y al frontend. En C# se usan las equivalencias de `AGENTS.md`.
- Los contratos entre servicios (eventos y API interna) son JSON documentado, nunca clases compartidas entre lenguajes.
