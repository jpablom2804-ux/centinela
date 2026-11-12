# Contratos de eventos

Todos los eventos de RabbitMQ comparten este sobre:

```json
{
  "id": "uuid",
  "tipo": "TicketCreado",
  "version": 1,
  "correlationId": "uuid",
  "ocurridoEn": "2026-11-11T23:34:00Z",
  "datos": { }
}
```

Reglas: fechas ISO-8601 en UTC; montos, si aparecen, como texto decimal más moneda; un cambio incompatible sube `version` y el consumidor acepta ambas durante la transición.

- `correlationId` es texto: el `X-Request-ID` de la petición original (lo pone nginx: 32 hexadecimales) o un UUID si no venía o no era válido (`[A-Za-z0-9-]{8,64}`).
- El mensaje de AMQP repite `id` en `message_id`, `tipo` en `type` y el `correlationId` en `correlation_id`, con `content_type: application/json` y entrega persistente.
- `mesa-servicio` publica con outbox: un evento puede llegar **más de una vez** (por ejemplo, si una réplica cae entre el ack del broker y el commit). Por eso los consumidores deduplican por `id`.

## TicketCreado (v1)

Publica `mesa-servicio` (vía outbox) → consume `agente`.

```json
{
  "ticketId": "uuid",
  "tipo": "incidente | solicitud | problema | cambio",
  "titulo": "texto ENMASCARADO (NO CONFIABLE)",
  "descripcion": "texto ENMASCARADO (NO CONFIABLE)",
  "sistemaReportado": "string | null",
  "canal": "portal | correo | telefono"
}
```

`titulo` y `descripcion` salen ya enmascarados (cédulas, cuentas, tarjetas, correos y teléfonos reemplazados por marcadores como `[CEDULA]`). El `agente` vuelve a verificarlo antes de llamar al modelo; si encuentra algo sin enmascarar, no llama al modelo y publica `estadoAnalisis = error`.

## TicketTriado (v1)

Publica `agente` → consume `mesa-servicio`.

```json
{
  "ticketId": "uuid",
  "estadoAnalisis": "completado | invalido | presupuesto_agotado | error",
  "categoria": "string | null",
  "sistemaAfectado": "string | null",
  "prioridadSugerida": "P1 | P2 | P3 | P4 | null",
  "justificacion": "texto | null",
  "duplicadosPosibles": ["ticketId"],
  "articulosSugeridos": ["articuloId"],
  "borradorRespuesta": "texto | null",
  "metricas": {
    "modelo": "string",
    "tokensEntrada": 0,
    "tokensSalida": 0,
    "iteraciones": 0,
    "duracionMs": 0
  }
}
```

Si `estadoAnalisis` no es `completado`, los campos de sugerencia van en `null` (o listas vacías) y el ticket queda para triaje manual. `mesa-servicio` nunca aplica la sugerencia sola: la guarda para que el técnico la acepte, la corrija o la descarte.

Los textos los genera el modelo: son **no confiables**. Límites, validados por el `agente` al producirlos y por `mesa-servicio` al recibirlos:

| Campo | Límite |
|---|---|
| `justificacion` | hasta 500 caracteres |
| `borradorRespuesta` | hasta 2000 caracteres |
| `duplicadosPosibles` | hasta 5 IDs, que deben existir |
| `articulosSugeridos` | hasta 5 IDs, que deben existir |

Texto plano. Si no cumple, el `agente` publica `estadoAnalisis = invalido`. Si aun así llega a `mesa-servicio`, el mensaje se rechaza a la DLQ.

El `agente` deduplica por `ticketId` además de por `id`: si un ticket ya fue triado, no vuelve a llamar al modelo y republica el resultado guardado.

## Eventos internos de `mesa-servicio` (en memoria, no van por RabbitMQ)

| Evento | Publica → consume | Datos principales |
|---|---|---|
| `SlaPorVencer` | módulo `sla` → notificaciones | `ticketId`, `prioridad`, `porcentajeConsumido`, `venceEn` |
| `SlaVencido` | módulo `sla` → escalamiento | `ticketId`, `prioridad`, `vencioEn` |

Son eventos de aplicación de Spring. Si algún día los consume otro servicio, pasan al broker con el sobre de arriba y se documentan como contrato.

## Colas

Todo vive en el vhost `centinela` de RabbitMQ.

| Exchange / cola | Uso |
|---|---|
| `tickets` → `agente.tickets-creados` | Trabajo para el agente. Exchange `direct`, clave `ticket.creado`. El exchange lo declara `mesa-servicio`; la cola y su DLQ, el `agente`. Si no hay cola enlazada, el broker devuelve el mensaje y el outbox lo reintenta: nunca se pierde en silencio |
| `triajes` → `mesa-servicio.tickets-triados` | Resultados del agente |
| `*.dlq` | Mensajes muertos de cada cola |
