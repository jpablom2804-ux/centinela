package cr.centinela.mesaservicio.conocimiento.internal;

import cr.centinela.mesaservicio.conocimiento.api.IndiceSemantico;
import cr.centinela.mesaservicio.plataforma.Enmascarador;
import java.time.Clock;
import java.time.ZoneOffset;
import java.util.Collection;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

/**
 * Búsqueda híbrida: primero por significado (pgvector, similitud coseno con umbral) y, si no hay vectores, si el
 * proveedor falla o si nada supera el umbral, por texto (búsqueda de texto completo en español). Así la
 * herramienta del agente nunca falla por falta de embeddings.
 */
@Component
class IndiceSemanticoPostgres implements IndiceSemantico {

    private static final Logger log = LoggerFactory.getLogger(IndiceSemanticoPostgres.class);
    private static final int MAX_LIMITE = 10;
    private static final int MAX_PALABRAS = 20;
    private static final Pattern PALABRA = Pattern.compile("[\\p{L}\\p{N}]{3,}");

    private final JdbcTemplate jdbc;
    private final ProveedorEmbeddings proveedor;
    private final PropiedadesConocimiento propiedades;
    private final Clock reloj;

    IndiceSemanticoPostgres(JdbcTemplate jdbc, ProveedorEmbeddings proveedor, PropiedadesConocimiento propiedades, Clock reloj) {
        this.jdbc = jdbc;
        this.proveedor = proveedor;
        this.propiedades = propiedades;
        this.reloj = reloj;
    }

    @Override
    @Transactional(propagation = Propagation.MANDATORY)
    public void registrar(TipoRecurso tipo, String id, String textoEnmascarado) {
        // Defensa en profundidad: lo que se indexa termina en el proveedor de embeddings.
        var texto = Enmascarador.enmascarar(textoEnmascarado);
        var ahora = reloj.instant().atOffset(ZoneOffset.UTC);
        jdbc.update("""
                INSERT INTO mesa_servicio.indice_semantico (recurso_tipo, recurso_id, texto, proximo_intento, actualizado_en)
                VALUES (?, ?, ?, ?, ?)
                ON CONFLICT (recurso_tipo, recurso_id) DO UPDATE
                   SET texto = excluded.texto, embedding = NULL, modelo = NULL, intentos = 0, ultimo_error = NULL,
                       proximo_intento = excluded.proximo_intento, actualizado_en = excluded.actualizado_en
                """, codigo(tipo), id, texto, ahora, ahora);
    }

    @Override
    @Transactional(readOnly = true)
    public List<Coincidencia> buscar(TipoRecurso tipo, String consulta, String excluirId, int limite) {
        var texto = Enmascarador.enmascarar(consulta == null ? "" : consulta.strip());
        var n = Math.clamp(limite, 1, MAX_LIMITE);
        var excluir = excluirId == null ? "" : excluirId;
        if (texto.isEmpty()) {
            return List.of();
        }
        if (proveedor.habilitado()) {
            try {
                var vector = proveedor.vectorizar(List.of(texto), ProveedorEmbeddings.Uso.CONSULTA).getFirst();
                var porSignificado = porVector(tipo, vector, excluir, n);
                if (!porSignificado.isEmpty()) {
                    return porSignificado;
                }
            } catch (ProveedorEmbeddings.ErrorEmbeddings e) {
                log.warn("Búsqueda por significado no disponible ({}); se busca por texto", e.codigo());
            }
        }
        return porTexto(tipo, texto, excluir, n);
    }

    @Override
    @Transactional(readOnly = true)
    public Set<String> articulosExistentes(Collection<String> codigos) {
        if (codigos.isEmpty()) {
            return Set.of();
        }
        return new HashSet<>(jdbc.queryForList("SELECT codigo FROM mesa_servicio.articulo WHERE codigo = ANY (?)",
                String.class, (Object) codigos.toArray(String[]::new)));
    }

    private List<Coincidencia> porVector(TipoRecurso tipo, float[] vector, String excluir, int limite) {
        var literal = literalVector(vector);
        return jdbc.query("""
                        SELECT recurso_id, 1 - (embedding <=> ?::vector) AS similitud
                          FROM mesa_servicio.indice_semantico
                         WHERE recurso_tipo = ? AND modelo = ? AND embedding IS NOT NULL AND recurso_id <> ?
                         ORDER BY embedding <=> ?::vector
                         LIMIT ?
                        """,
                        (fila, i) -> new Coincidencia(fila.getString(1), fila.getDouble(2), Metodo.SIGNIFICADO),
                        literal, codigo(tipo), proveedor.modelo(), excluir, literal, limite)
                .stream()
                .filter(c -> c.similitud() >= propiedades.umbralSimilitud())
                .toList();
    }

    private List<Coincidencia> porTexto(TipoRecurso tipo, String texto, String excluir, int limite) {
        // Palabras sueltas unidas con OR: solo letras y dígitos, así el texto no puede alterar la consulta tsquery.
        var palabras = PALABRA.matcher(texto.toLowerCase(Locale.ROOT)).results()
                .map(m -> m.group())
                .distinct()
                .limit(MAX_PALABRAS)
                .collect(Collectors.joining(" | "));
        if (palabras.isEmpty()) {
            return List.of();
        }
        return jdbc.query("""
                        SELECT recurso_id, ts_rank(busqueda, consulta) AS similitud
                          FROM mesa_servicio.indice_semantico, to_tsquery('spanish', ?) AS consulta
                         WHERE recurso_tipo = ? AND recurso_id <> ? AND busqueda @@ consulta
                         ORDER BY similitud DESC
                         LIMIT ?
                        """,
                (fila, i) -> new Coincidencia(fila.getString(1), fila.getDouble(2), Metodo.TEXTO),
                palabras, codigo(tipo), excluir, limite);
    }

    static String codigo(TipoRecurso tipo) {
        return tipo.name().toLowerCase(Locale.ROOT);
    }

    /** pgvector acepta el vector como texto: '[0.1,0.2,…]'. */
    static String literalVector(float[] vector) {
        var texto = new StringBuilder(vector.length * 12).append('[');
        for (var i = 0; i < vector.length; i++) {
            if (i > 0) texto.append(',');
            texto.append(vector[i]);
        }
        return texto.append(']').toString();
    }

}
