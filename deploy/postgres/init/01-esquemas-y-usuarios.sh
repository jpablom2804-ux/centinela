#!/bin/bash
# Crea un usuario por servicio, dueño de sus esquemas, y el usuario de solo lectura de Power BI.
# Lo ejecuta la imagen oficial de PostgreSQL una sola vez: cuando el volumen de datos está vacío.
# Las contraseñas llegan por variables de entorno y se pasan a psql como variables (:'...'),
# nunca concatenadas en el SQL.
set -eo pipefail

: "${SIMULADOR_DB_PASSWORD:?falta SIMULADOR_DB_PASSWORD}"
: "${DETECCION_DB_PASSWORD:?falta DETECCION_DB_PASSWORD}"
: "${AGENTE_DB_PASSWORD:?falta AGENTE_DB_PASSWORD}"
: "${POWERBI_RO_DB_PASSWORD:?falta POWERBI_RO_DB_PASSWORD}"

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v pass_simulador="$SIMULADOR_DB_PASSWORD" \
  -v pass_deteccion="$DETECCION_DB_PASSWORD" \
  -v pass_agente="$AGENTE_DB_PASSWORD" \
  -v pass_powerbi="$POWERBI_RO_DB_PASSWORD" <<'SQL'
-- Nadie se conecta ni crea objetos salvo lo que se otorga explícitamente abajo.
REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM PUBLIC;

CREATE ROLE simulador  LOGIN PASSWORD :'pass_simulador';
CREATE ROLE deteccion  LOGIN PASSWORD :'pass_deteccion';
CREATE ROLE agente     LOGIN PASSWORD :'pass_agente';
CREATE ROLE powerbi_ro LOGIN PASSWORD :'pass_powerbi';

GRANT CONNECT ON DATABASE :"db" TO simulador, deteccion, agente, powerbi_ro;

-- Cada servicio es dueño de sus esquemas y no tiene permisos sobre los de otros.
CREATE SCHEMA simulador AUTHORIZATION simulador;
CREATE SCHEMA deteccion AUTHORIZATION deteccion;
CREATE SCHEMA reportes  AUTHORIZATION deteccion;
CREATE SCHEMA agente    AUTHORIZATION agente;

ALTER ROLE simulador  SET search_path = simulador;
ALTER ROLE deteccion  SET search_path = deteccion;
ALTER ROLE agente     SET search_path = agente;
ALTER ROLE powerbi_ro SET search_path = reportes;

-- Power BI: solo SELECT sobre reportes, incluidas las vistas que deteccion cree después.
GRANT USAGE ON SCHEMA reportes TO powerbi_ro;
ALTER DEFAULT PRIVILEGES FOR ROLE deteccion IN SCHEMA reportes GRANT SELECT ON TABLES TO powerbi_ro;
ALTER ROLE powerbi_ro SET default_transaction_read_only = on;
SQL
