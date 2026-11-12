# Centinela

Mesa de servicio de TI para entidades financieras, con triaje asistido por IA. Proyecto de portafolio.

Cuando alguien reporta un ticket, un agente de IA programado desde cero lo analiza con herramientas de solo lectura: sugiere categoría, sistema afectado y prioridad, busca duplicados y artículos de la base de conocimiento, y redacta un borrador de respuesta. **El agente recomienda; el técnico decide.** Ninguna sugerencia se aplica sola.

> **En construcción.** Fase actual: 0 (preparación). Avance en [docs/ROADMAP.md](docs/ROADMAP.md).

## Cómo funciona

```mermaid
flowchart LR
    FE[portal y tablero<br/>React] -- /api --> MS[mesa-servicio<br/>Java · tickets, SLA, cambios, auditoría]
    MS -- TicketCreado<br/>enmascarado --> MQ[(RabbitMQ)]
    MQ --> AGE[agente<br/>C# · ciclo hecho a mano]
    AGE -- HTTP solo lectura --> MS
    AGE -- TicketTriado --> MQ
    MQ --> MS
```

## Objetivos del proyecto

- Monolito modular para el núcleo y el agente como servicio aparte con RabbitMQ: separado solo donde hay una razón real ([ADR-003](docs/decisiones/ADR-003-cambio-dominio-tickets.md))
- Java + Spring Boot, C# .NET, React + TypeScript, PostgreSQL + pgvector ([ADR-002](docs/decisiones/ADR-002-lenguajes.md))
- Un agente de IA sin frameworks: el ciclo modelo → herramienta → resultado escrito a mano, con límites de iteraciones, tiempo y tokens
- Datos sensibles (cédulas, cuentas, tarjetas, correos, teléfonos) enmascarados antes de llegar al modelo
- Reglas de una entidad financiera: SLA con calendario laboral, segregación de funciones en los cambios y bitácora de auditoría de solo inserción
- Seguridad basada en OWASP (web, APIs, LLM y agentes), con una suite de ataques contra el agente ([docs/SEGURIDAD.md](docs/SEGURIDAD.md))
- SLA, calidad del triaje y costo de la IA medidos en Power BI ([docs/KPIS.md](docs/KPIS.md))
- Datos 100 % sintéticos

## Entorno de desarrollo

Requisitos: Docker con Compose.

```bash
cp .env.example .env      # completar las contraseñas
docker compose up -d      # PostgreSQL (con pgvector) y RabbitMQ
docker compose ps         # ambos deben quedar "healthy"
```

- PostgreSQL en `127.0.0.1:5432`, con roles y esquemas por servicio ([docs/ARQUITECTURA.md](docs/ARQUITECTURA.md#base-de-datos)).
- Panel de RabbitMQ en http://127.0.0.1:15672.

Los roles y esquemas se crean solo la primera vez, cuando el volumen está vacío. Para recrearlos: `docker compose down -v` (borra los datos).

## Documentación

| Documento | Contenido |
|---|---|
| [ARQUITECTURA](docs/ARQUITECTURA.md) | Servicios, módulos, base de datos y estructura del repositorio |
| [EVENTOS](docs/EVENTOS.md) | Contratos de los mensajes |
| [SEGURIDAD](docs/SEGURIDAD.md) | Riesgos, controles y suite adversaria |
| [KPIS](docs/KPIS.md) | Indicadores de servicio, calidad del triaje y costo |
| [CONTEXTO](docs/CONTEXTO.md) | De dónde salió el proyecto y por qué |
