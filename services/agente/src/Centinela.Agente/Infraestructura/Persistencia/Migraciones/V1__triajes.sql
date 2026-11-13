-- Esquema propio del agente (ADR-003: ningún servicio toca tablas de otro).

-- Un triaje por ticket: si el ticket vuelve a llegar, se republica este evento sin llamar al modelo.
CREATE TABLE agente.triaje (
    ticket_id       uuid         PRIMARY KEY,
    evento_id       uuid         NOT NULL UNIQUE,
    correlation_id  varchar(64)  NOT NULL,
    estado          varchar(30)  NOT NULL,
    modelo          varchar(100) NOT NULL,
    tokens_entrada  integer      NOT NULL CHECK (tokens_entrada >= 0),
    tokens_salida   integer      NOT NULL CHECK (tokens_salida >= 0),
    cuerpo          text         NOT NULL,          -- sobre TicketTriado completo, tal como se publicó
    creado_en       timestamptz  NOT NULL
);

-- Para el tope diario de tokens: suma de lo gastado desde el inicio del día.
CREATE INDEX triaje_creado_en ON agente.triaje (creado_en);

-- Mensajes ya procesados (consumidor idempotente por id).
CREATE TABLE agente.mensaje_procesado (
    id            uuid        PRIMARY KEY,
    ticket_id     uuid        NOT NULL,
    procesado_en  timestamptz NOT NULL
);
