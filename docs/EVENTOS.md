# Contratos de eventos

Todos los eventos comparten este sobre:

```json
{
  "id": "uuid",
  "tipo": "TransaccionRecibida",
  "version": 1,
  "correlationId": "uuid",
  "ocurridoEn": "2026-11-11T23:34:00Z",
  "datos": { }
}
```

Reglas: montos como texto decimal más moneda; fechas ISO-8601 en UTC; un cambio incompatible sube `version` y el consumidor acepta ambas durante la transición.

## TransaccionRecibida (v1)

Publica `simulador` → consume `deteccion`.

```json
{
  "transaccionId": "uuid",
  "clienteId": "uuid",
  "monto": "1250.00",
  "moneda": "CRC",
  "comercio": "texto libre (NO CONFIABLE)",
  "categoriaComercio": "string",
  "pais": "CR",
  "canal": "tarjeta | transferencia | sinpe_movil",
  "fecha": "2026-11-11T23:30:00Z"
}
```

## CasoSospechosoCreado (v1)

Publica `deteccion` (vía outbox) → consume `agente`.

```json
{
  "casoId": "uuid",
  "transaccionId": "uuid",
  "clienteId": "uuid",
  "puntajeReglas": 82,
  "reglasActivadas": ["MONTO_INUSUAL", "PAIS_DISTINTO"]
}
```

## CasoAnalizado (v1)

Publica `agente` → consume `deteccion`.

```json
{
  "casoId": "uuid",
  "estadoAnalisis": "completado | invalido | presupuesto_agotado | error",
  "recomendacion": "revisar_urgente | revisar | probablemente_legitimo | null",
  "nivelRiesgo": "alto | medio | bajo | null",
  "hallazgos": ["texto corto", "..."],
  "resumen": "explicación para el analista",
  "metricas": {
    "modelo": "string",
    "tokensEntrada": 0,
    "tokensSalida": 0,
    "iteraciones": 0,
    "duracionMs": 0
  }
}
```

Si `estadoAnalisis` no es `completado`, `recomendacion` es `null` y el caso queda para revisión manual completa.

`hallazgos` y `resumen` los genera el modelo: son **no confiables**. Límites, validados por el `agente` al producirlos y por `deteccion` al recibirlos:

| Campo | Límite |
|---|---|
| `hallazgos` | máximo 10 elementos, cada uno de hasta 300 caracteres |
| `resumen` | hasta 2000 caracteres |

Texto plano. Si no cumple, el `agente` publica `estadoAnalisis = invalido` sin recomendación. Si aun así llega a `deteccion`, el mensaje se rechaza a la DLQ.

El `agente` deduplica por `casoId` además de por `id`: si un caso ya fue analizado, no vuelve a llamar al modelo y republica el resultado guardado.

## Colas

Todo vive en el vhost `centinela` de RabbitMQ.

| Exchange / cola | Uso |
|---|---|
| `transacciones` → `deteccion.transacciones` | Ingesta |
| `casos` → `agente.casos-sospechosos` | Trabajo para el agente |
| `analisis` → `deteccion.casos-analizados` | Resultados del agente |
| `*.dlq` | Mensajes muertos de cada cola |
