-- Sugerencia del agente para un ticket (TicketTriado v1). Se guarda tal cual llegó y NUNCA se aplica sola:
-- en la fase 4 un técnico la acepta, la corrige o la descarta. Una sola por ticket.
CREATE TABLE mesa_servicio.sugerencia_triaje (
    ticket_id           uuid          PRIMARY KEY REFERENCES mesa_servicio.ticket (id),
    evento_id           uuid          NOT NULL UNIQUE,
    correlation_id      varchar(64)   NOT NULL,
    estado_analisis     varchar(30)   NOT NULL CHECK (estado_analisis IN ('completado', 'invalido', 'presupuesto_agotado', 'error')),
    categoria           varchar(60),
    sistema_afectado    varchar(60),
    prioridad_sugerida  varchar(2)    CHECK (prioridad_sugerida IN ('P1', 'P2', 'P3', 'P4')),
    justificacion       varchar(500),
    duplicados_posibles varchar(200)  NOT NULL DEFAULT '',   -- IDs de ticket separados por coma (máximo 5)
    articulos_sugeridos varchar(200)  NOT NULL DEFAULT '',   -- códigos de artículo separados por coma (máximo 5)
    borrador_respuesta  varchar(2000),
    modelo              varchar(100)  NOT NULL,
    tokens_entrada      integer       NOT NULL CHECK (tokens_entrada >= 0),
    tokens_salida       integer       NOT NULL CHECK (tokens_salida >= 0),
    iteraciones         integer       NOT NULL CHECK (iteraciones >= 0),
    duracion_ms         bigint        NOT NULL CHECK (duracion_ms >= 0),
    recibida_en         timestamptz   NOT NULL,
    -- Sin sugerencia (triaje manual) = todos los campos de la sugerencia vacíos.
    CHECK (estado_analisis = 'completado' OR (categoria IS NULL AND prioridad_sugerida IS NULL AND borrador_respuesta IS NULL))
);

-- Para los KPIs de costo y calidad del triaje (reportes).
CREATE INDEX sugerencia_triaje_recibida_en ON mesa_servicio.sugerencia_triaje (recibida_en);
