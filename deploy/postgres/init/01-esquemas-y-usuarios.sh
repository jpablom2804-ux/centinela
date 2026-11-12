#!/bin/bash
# Crea los roles y esquemas de cada servicio, la extensión pgvector y el usuario de Power BI.
# Lo ejecuta la imagen de PostgreSQL una sola vez: cuando el volumen de datos está vacío.
# Las contraseñas llegan por variables de entorno y se pasan a psql como variables (:'...'),
# nunca concatenadas en el SQL.
set -eo pipefail

: "${MESA_SERVICIO_DB_PASSWORD:?falta MESA_SERVICIO_DB_PASSWORD}"
: "${MESA_SERVICIO_APP_DB_PASSWORD:?falta MESA_SERVICIO_APP_DB_PASSWORD}"
: "${AGENTE_DB_PASSWORD:?falta AGENTE_DB_PASSWORD}"
: "${POWERBI_RO_DB_PASSWORD:?falta POWERBI_RO_DB_PASSWORD}"

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v pass_mesa="$MESA_SERVICIO_DB_PASSWORD" \
  -v pass_mesa_app="$MESA_SERVICIO_APP_DB_PASSWORD" \
  -v pass_agente="$AGENTE_DB_PASSWORD" \
  -v pass_powerbi="$POWERBI_RO_DB_PASSWORD" <<'SQL'
-- Nadie se conecta ni crea objetos salvo lo que se otorga explícitamente abajo.
REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM PUBLIC;

-- mesa_servicio es dueño de los esquemas y solo lo usa Flyway.
-- mesa_servicio_app es el rol con el que corre la aplicación: al no ser dueño,
-- los permisos que no se le otorgan (UPDATE/DELETE en auditoria) no los puede recuperar.
CREATE ROLE mesa_servicio     LOGIN PASSWORD :'pass_mesa';
CREATE ROLE mesa_servicio_app LOGIN PASSWORD :'pass_mesa_app';
CREATE ROLE agente            LOGIN PASSWORD :'pass_agente';
CREATE ROLE powerbi_ro        LOGIN PASSWORD :'pass_powerbi';

GRANT CONNECT ON DATABASE :"db" TO mesa_servicio, mesa_servicio_app, agente, powerbi_ro;

CREATE SCHEMA mesa_servicio AUTHORIZATION mesa_servicio;
CREATE SCHEMA auditoria     AUTHORIZATION mesa_servicio;
CREATE SCHEMA reportes      AUTHORIZATION mesa_servicio;
CREATE SCHEMA agente        AUTHORIZATION agente;

-- pgvector requiere superusuario para instalarse; vive en el esquema de mesa_servicio.
CREATE EXTENSION IF NOT EXISTS vector SCHEMA mesa_servicio;

ALTER ROLE mesa_servicio     SET search_path = mesa_servicio;
ALTER ROLE mesa_servicio_app SET search_path = mesa_servicio;
ALTER ROLE agente            SET search_path = agente;
ALTER ROLE powerbi_ro        SET search_path = reportes;

-- Rol de la aplicación: lectura y escritura en mesa_servicio.
GRANT USAGE ON SCHEMA mesa_servicio TO mesa_servicio_app;
ALTER DEFAULT PRIVILEGES FOR ROLE mesa_servicio IN SCHEMA mesa_servicio
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO mesa_servicio_app;
ALTER DEFAULT PRIVILEGES FOR ROLE mesa_servicio IN SCHEMA mesa_servicio
  GRANT USAGE, SELECT ON SEQUENCES TO mesa_servicio_app;

-- Bitácora de auditoría: solo SELECT e INSERT. Nunca UPDATE, DELETE ni TRUNCATE.
GRANT USAGE ON SCHEMA auditoria TO mesa_servicio_app;
ALTER DEFAULT PRIVILEGES FOR ROLE mesa_servicio IN SCHEMA auditoria
  GRANT SELECT, INSERT ON TABLES TO mesa_servicio_app;
ALTER DEFAULT PRIVILEGES FOR ROLE mesa_servicio IN SCHEMA auditoria
  GRANT USAGE, SELECT ON SEQUENCES TO mesa_servicio_app;

-- Reportes: la app lee parámetros y vistas; Power BI solo lee.
GRANT USAGE ON SCHEMA reportes TO mesa_servicio_app, powerbi_ro;
ALTER DEFAULT PRIVILEGES FOR ROLE mesa_servicio IN SCHEMA reportes
  GRANT SELECT ON TABLES TO mesa_servicio_app, powerbi_ro;
ALTER ROLE powerbi_ro SET default_transaction_read_only = on;
SQL
