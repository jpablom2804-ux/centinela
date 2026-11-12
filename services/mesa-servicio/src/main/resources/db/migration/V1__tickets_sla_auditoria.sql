-- Fase 2: tickets, relojes de SLA y bitácora de auditoría.
-- Corre con el rol dueño (mesa_servicio). Los permisos del rol de la app salen de los privilegios por
-- defecto del script de init: lectura y escritura en mesa_servicio; solo SELECT e INSERT en auditoria.

create table mesa_servicio.ticket (
    id                   uuid primary key,
    tipo                 varchar(20)   not null check (tipo in ('INCIDENTE', 'SOLICITUD', 'PROBLEMA', 'CAMBIO')),
    titulo               varchar(200)  not null,
    descripcion          varchar(5000) not null,
    sistema_reportado    varchar(100),
    canal                varchar(20)   not null check (canal in ('PORTAL', 'CORREO', 'TELEFONO')),
    estado               varchar(20)   not null check (estado in ('NUEVO', 'TRIADO', 'ASIGNADO', 'EN_PROGRESO', 'EN_ESPERA', 'RESUELTO', 'CERRADO')),
    prioridad            varchar(2)    not null check (prioridad in ('P1', 'P2', 'P3', 'P4')),
    solicitante          varchar(100)  not null,
    asignado_a           varchar(100),
    primera_respuesta_en timestamptz,
    creado_en            timestamptz   not null,
    actualizado_en       timestamptz   not null,
    version              bigint        not null
);
create index ix_ticket_solicitante on mesa_servicio.ticket (solicitante, creado_en desc);
create index ix_ticket_estado on mesa_servicio.ticket (estado);

create table mesa_servicio.comentario (
    id        uuid primary key,
    ticket_id uuid          not null references mesa_servicio.ticket (id),
    autor     varchar(100)  not null,
    texto     varchar(5000) not null,
    creado_en timestamptz   not null
);
create index ix_comentario_ticket on mesa_servicio.comentario (ticket_id, creado_en);

-- Del módulo sla: guarda el id del ticket, pero el módulo no lee la tabla ticket.
create table mesa_servicio.sla_reloj (
    ticket_id      uuid primary key references mesa_servicio.ticket (id),
    prioridad      varchar(2)  not null check (prioridad in ('P1', 'P2', 'P3', 'P4')),
    consumido_ms   bigint      not null check (consumido_ms >= 0),
    corriendo      boolean     not null,
    ultimo_corte   timestamptz not null,
    alerta_enviada boolean     not null,
    vencido        boolean     not null,
    version        bigint      not null
);
create index ix_sla_reloj_corriendo on mesa_servicio.sla_reloj (corriendo) where corriendo and not vencido;

-- ShedLock: una sola réplica ejecuta cada tarea programada a la vez.
create table mesa_servicio.shedlock (
    name       varchar(64)  primary key,
    lock_until timestamptz  not null,
    locked_at  timestamptz  not null,
    locked_by  varchar(255) not null
);

-- Bitácora de solo inserción, encadenada con hash (A8). "antes" y "despues" son text y no jsonb: jsonb
-- normaliza el JSON (orden, espacios) y el texto leído ya no coincidiría con el usado para el hash.
create table auditoria.bitacora (
    id            bigint generated always as identity primary key,
    ocurrido_en   timestamptz  not null,
    actor         varchar(100) not null,
    accion        varchar(60)  not null,
    recurso_tipo  varchar(40)  not null,
    recurso_id    varchar(64)  not null,
    antes         text,
    despues       text,
    hash_anterior varchar(64)  not null,
    hash          varchar(64)  not null unique
);
create index ix_bitacora_recurso on auditoria.bitacora (recurso_tipo, recurso_id);
