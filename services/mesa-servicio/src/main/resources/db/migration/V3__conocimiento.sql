-- Módulo conocimiento (ADR-007): artículos de solución e índice para buscar por significado (pgvector) o,
-- como respaldo, por texto (búsqueda de texto completo de PostgreSQL en español).

CREATE TABLE mesa_servicio.articulo (
    codigo       varchar(20)   PRIMARY KEY CHECK (codigo ~ '^KB-[0-9]{3,6}$'),
    titulo       varchar(200)  NOT NULL,
    contenido    varchar(5000) NOT NULL,
    sistemas     varchar(300)  NOT NULL DEFAULT '',   -- códigos del catálogo, separados por coma
    publicado_en timestamptz   NOT NULL
);

-- Lo que se indexa ya viene enmascarado: es lo único que sale hacia el proveedor de embeddings.
CREATE TABLE mesa_servicio.indice_semantico (
    recurso_tipo    varchar(20)  NOT NULL CHECK (recurso_tipo IN ('ticket', 'articulo')),
    recurso_id      varchar(64)  NOT NULL,
    texto           text         NOT NULL,
    busqueda        tsvector     GENERATED ALWAYS AS (to_tsvector('spanish', texto)) STORED,
    modelo          varchar(60),                 -- con qué modelo se calculó el vector: nunca se mezclan modelos
    embedding       vector(1024),                -- null = pendiente
    intentos        integer      NOT NULL DEFAULT 0 CHECK (intentos >= 0),
    proximo_intento timestamptz  NOT NULL,
    ultimo_error    varchar(40),
    actualizado_en  timestamptz  NOT NULL,
    PRIMARY KEY (recurso_tipo, recurso_id)
);

CREATE INDEX indice_semantico_pendientes ON mesa_servicio.indice_semantico (proximo_intento) WHERE embedding IS NULL;
CREATE INDEX indice_semantico_texto ON mesa_servicio.indice_semantico USING gin (busqueda);
CREATE INDEX indice_semantico_vector ON mesa_servicio.indice_semantico USING hnsw (embedding vector_cosine_ops);

-- Base de conocimiento inicial, 100 % sintética (la misma que usa el agente en sus pruebas).
INSERT INTO mesa_servicio.articulo (codigo, titulo, contenido, sistemas, publicado_en) VALUES
('KB-001', 'Cómo restablecer la contraseña del directorio activo',
 'Verificar la identidad del usuario con el procedimiento estándar, restablecer la contraseña desde la consola de administración, marcar cambio obligatorio al siguiente inicio y pedir que actualice las contraseñas guardadas en otros dispositivos.',
 'directorio_activo,vpn', now()),
('KB-002', 'Diagnóstico de la VPN corporativa',
 'Revisar que la contraseña no esté vencida, que la fecha y hora del equipo sean correctas, que el certificado corporativo esté instalado y que el token de doble factor esté sincronizado. Probar desde otra red para descartar el proveedor de internet.',
 'vpn', now()),
('KB-003', 'Recrear el perfil de Outlook',
 'Cerrar Outlook, crear un perfil nuevo desde el panel de correo, configurarlo como predeterminado y esperar la sincronización completa. Los datos están en el servidor, no se pierden.',
 'correo', now()),
('KB-004', 'Reiniciar la cola de impresión',
 'En el servidor de impresión, detener el servicio de cola, borrar los trabajos atascados y volver a iniciar el servicio. Si persiste, revisar el controlador de la impresora.',
 'impresion', now()),
('KB-005', 'Qué hacer ante un correo de phishing',
 'No abrir enlaces ni adjuntos. Usar el botón de reportar phishing, avisar a seguridad y, si se ingresaron credenciales, cambiarlas de inmediato y reportarlo como incidente de seguridad.',
 'correo', now()),
('KB-006', 'Escalamiento de incidentes del core bancario',
 'Confirmar si el problema afecta a una o varias agencias, revisar el tablero de estado, abrir caso con el proveedor del core y comunicar a las jefaturas de agencia. Es P1 si afecta la atención de clientes en varias agencias.',
 'core_bancario', now());

INSERT INTO mesa_servicio.indice_semantico (recurso_tipo, recurso_id, texto, proximo_intento, actualizado_en)
-- proximo_intento en el pasado: listos para indexar en cuanto haya proveedor de embeddings.
SELECT 'articulo', codigo, titulo || '. ' || contenido, timestamptz '1970-01-01 00:00:00+00', now() FROM mesa_servicio.articulo;
