-- Outbox transaccional (AGENTS.md: mesa-servicio publica con outbox). Cada evento para otro servicio se
-- guarda aquí en la misma transacción que su cambio; RelevoOutbox lo publica en RabbitMQ con SKIP LOCKED.
CREATE TABLE mesa_servicio.outbox (
    id              uuid         PRIMARY KEY,
    exchange        varchar(100) NOT NULL,
    routing_key     varchar(100) NOT NULL,
    tipo            varchar(60)  NOT NULL,
    correlation_id  varchar(64)  NOT NULL,
    cuerpo          text         NOT NULL,                  -- sobre completo en JSON, con los datos ya enmascarados
    creado_en       timestamptz  NOT NULL,
    publicado_en    timestamptz,
    intentos        integer      NOT NULL DEFAULT 0 CHECK (intentos >= 0),
    proximo_intento timestamptz  NOT NULL,
    ultimo_error    varchar(40)                              -- un código (SIN_COLA, SIN_CONFIRMACION…), nunca datos
);

-- Solo los pendientes: lo que busca el relevo en cada vuelta.
CREATE INDEX outbox_pendientes ON mesa_servicio.outbox (proximo_intento) WHERE publicado_en IS NULL;
