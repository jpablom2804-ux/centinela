# mesa-servicio

Núcleo de la mesa de servicio, en Java 25 y Spring Boot 4: tickets, SLA y auditoría por ahora. Más adelante se suman tablero, cambios, conocimiento y reportes. Es un monolito modular (ADR-003): cada módulo expone solo su paquete `api/`, y `ModulosTest` (Spring Modulith) hace fallar el build si alguno usa el `internal/` de otro.

| Módulo | Qué hace hoy |
|---|---|
| `tickets` | Crear, listar, ver, asignar, cambiar estado y prioridad, comentar. Transiciones válidas; un ticket cerrado solo se reabre con justificación |
| `sla` | Reloj de resolución por ticket en tiempo hábil (jornada, fines de semana y feriados en hora de Costa Rica). Se pausa en `en_espera`. Avisos en memoria al 75 % y al vencer, con ShedLock para varias réplicas |
| `auditoria` | Bitácora de solo inserción, encadenada con hash: un registro alterado o insertado a mano se detecta |
| `plataforma` | Seguridad, usuario actual y errores `{ codigo, mensaje }` |

## Seguridad que ya está en el código

- **Dos roles de base de datos:** las migraciones corren aparte con el rol dueño (perfil `migracion`) y la app con `mesa_servicio_app`, que nunca recibe el secreto del dueño (A14). Sobre la bitácora, la app solo tiene `SELECT` e `INSERT`: lo prueba `BitacoraTest` contra PostgreSQL real.
- **Ticket ajeno e inexistente responden igual:** el mismo `404`, el mismo cuerpo y una sola consulta.
- **Un DTO por acción:** un campo de más (por ejemplo `prioridad` al crear) es un `400` (A6). Hay largos máximos en todo el texto (A4).
- **Toda acción va a la bitácora en la misma transacción,** incluidas las pausas del SLA.

## Cómo correrlo

Requisitos: JDK 25, Docker y el compose de la raíz levantado (`docker compose up -d` y `./scripts/secretos.sh generar`).

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

## Pruebas

```bash
./mvnw -B verify
```

Son 35 pruebas, entre ellas:
- **Unitarias:** transiciones y calendario laboral (fines de semana, feriados y cambio de día en UTC).
- **Límites entre módulos:** la verificación de Spring Modulith.
- **Integración con PostgreSQL real (Testcontainers):** usa el mismo script de init del proyecto, Flyway corre con el rol dueño y la app con el suyo. Cubren la API, la bitácora y el vigilante de SLA, este último con un reloj que se puede mover.
