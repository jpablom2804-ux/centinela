# mesa-servicio

Núcleo de la mesa de servicio, en Java 25 y Spring Boot 4: tickets, SLA, conocimiento y auditoría por ahora. Más adelante se suman tablero, cambios y reportes. Es un monolito modular (ADR-003): cada módulo expone solo su paquete `api/`, y `ModulosTest` (Spring Modulith) hace fallar el build si alguno usa el `internal/` de otro.

| Módulo | Qué hace hoy |
|---|---|
| `tickets` | Crear, listar, ver, asignar, cambiar estado y prioridad, comentar. Transiciones válidas; un ticket cerrado solo se reabre con justificación |
| `sla` | Reloj de resolución por ticket en tiempo hábil (jornada, fines de semana y feriados en hora de Costa Rica). Se pausa en `en_espera`. Avisos en memoria al 75 % y al vencer, con ShedLock para varias réplicas |
| `auditoria` | Bitácora de solo inserción, encadenada con hash: un registro alterado o insertado a mano se detecta |
| `conocimiento` | Artículos de solución y búsqueda por significado (ADR-007): embeddings de Voyage AI en pgvector, calculados aparte en lotes. Sin key de Voyage, o si Voyage falla, busca por texto (PostgreSQL en español) |
| `plataforma` | Seguridad, usuario actual, errores `{ codigo, mensaje }`, ID de correlación, enmascarado de datos sensibles y outbox hacia RabbitMQ |

## Seguridad que ya está en el código

- **Dos roles de base de datos:** las migraciones corren aparte con el rol dueño (perfil `migracion`) y la app con `mesa_servicio_app`, que nunca recibe el secreto del dueño (A14). Sobre la bitácora, la app solo tiene `SELECT` e `INSERT`: lo prueba `BitacoraTest` contra PostgreSQL real.
- **Ticket ajeno e inexistente responden igual:** el mismo `404`, el mismo cuerpo y una sola consulta.
- **Un DTO por acción:** un campo de más (por ejemplo `prioridad` al crear) es un `400` (A6). Hay largos máximos en todo el texto (A4).
- **Límite por persona (A5):** 20 tickets por hora, contados en la base para que valga con varias réplicas; el siguiente recibe `429`.
- **Toda acción va a la bitácora en la misma transacción,** incluidas las pausas del SLA.
- **Nada sale sin enmascarar:** `TicketCreado` lleva cédulas, cuentas, tarjetas, correos, teléfonos, credenciales, códigos e IPs reemplazados por marcadores (`[CEDULA]`…). Las reglas son las mismas que verifica el agente, y las dos partes prueban el mismo archivo de casos ([tests/contratos/enmascarado.json](../../tests/contratos/enmascarado.json)).
- **Outbox transaccional:** el evento se guarda con el ticket y se publica después, con confirmación de RabbitMQ. Si el broker está caído, el ticket se crea igual y el evento sale cuando vuelve.

## Cómo correrlo

Requisitos: JDK 25, Docker y el compose de la raíz levantado (`docker compose up -d` y `./scripts/secretos.sh generar`). La key de Voyage AI es opcional (`./scripts/secretos.sh guardar voyage_api_key`): sin ella, la búsqueda es por texto.

```bash
cd services/mesa-servicio
# Ruta ABSOLUTA de la carpeta de secretos (con una relativa, la importación opcional se ignora sin avisar)
export CENTINELA_SECRETOS="$(cd ../../secrets && pwd)"

./mvnw -B -DskipTests package
java -jar target/mesa-servicio-0.0.1-SNAPSHOT.jar --spring.profiles.active=migracion   # migra con el rol dueño y termina
java -jar target/mesa-servicio-0.0.1-SNAPSHOT.jar --spring.profiles.active=desarrollo  # API en http://127.0.0.1:8080
```

En Windows con Git Bash: `export CENTINELA_SECRETOS="$(cygpath -m "$(cd ../../secrets && pwd)")"`.

Con el perfil `desarrollo` hay usuarios de prueba (`solicitante1`, `solicitante2`, `tecnico1`, `tecnico2`, `coordinador1`, `auditor1`), todos con la contraseña de `secrets/usuarios_prueba_password`. Sin ese perfil no hay usuarios, hasta que llegue Keycloak en la fase 4.

```bash
P="$(cat ../../secrets/usuarios_prueba_password)"
curl -u "solicitante1:$P" -H "Content-Type: application/json" \
  -d '{"tipo":"incidente","titulo":"No puedo entrar a la VPN","descripcion":"Se queda en conectando.","sistemaReportado":"vpn","canal":"portal"}' \
  http://127.0.0.1:8080/api/tickets
```

| Método y ruta | Quién |
|---|---|
| `POST /api/tickets` | solicitante, técnico, coordinador |
| `GET /api/tickets`, `GET /api/tickets/{id}` | el solicitante ve los suyos; soporte y auditoría, todos |
| `POST /api/tickets/{id}/estado` | soporte: cualquier transición válida. Solicitante: confirmar el cierre o reabrir con justificación |
| `POST /api/tickets/{id}/prioridad` | técnico o coordinador, con justificación |
| `POST /api/tickets/{id}/asignacion` | el coordinador asigna a cualquiera; un técnico solo se lo toma a sí mismo |
| `POST/GET /api/tickets/{id}/comentarios` | el solicitante en los suyos y soporte; nunca en un ticket cerrado |
| `GET /api/tickets/{id}/sugerencia` | soporte y auditoría: la sugerencia del agente, siempre con `aplicada: false` |

La API interna de solo lectura (`/interna/tickets/{id}/similares`, `/interna/articulos`, `/interna/sistemas/{sistema}/incidentes`) es solo para las herramientas del agente: acepta únicamente su cuenta de servicio (`secrets/api_interna_agente_password`), solo `GET`, y todo lo que devuelve va enmascarado. Fuera de desarrollo, nginx no la publica.

## Pruebas

```bash
./mvnw -B verify
```

Son 78 pruebas, entre ellas:
- **Unitarias:** transiciones, calendario laboral (fines de semana, feriados y cambio de día en UTC) y enmascarado (el contrato compartido con el agente más evasiones como ancho completo, caracteres invisibles o dígitos de otros alfabetos).
- **Límites entre módulos:** la verificación de Spring Modulith.
- **Integración con PostgreSQL y RabbitMQ reales (Testcontainers):** usa el mismo script de init del proyecto, Flyway corre con el rol dueño y la app con el suyo. Cubren la API, la bitácora, el vigilante de SLA (con un reloj que se puede mover), el outbox (mensaje enmascarado, `correlationId` de punta a punta, reintento cuando no hay cola), el consumo de `TicketTriado` (guardado sin aplicarse, duplicados, DLQ, A3), la API interna y la búsqueda por texto y por significado. Esta última usa un proveedor de embeddings falso: el CI nunca llama a Voyage.
