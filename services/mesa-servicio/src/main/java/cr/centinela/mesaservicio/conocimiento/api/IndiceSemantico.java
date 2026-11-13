package cr.centinela.mesaservicio.conocimiento.api;

import java.util.Collection;
import java.util.List;
import java.util.Set;

/**
 * Búsqueda por significado (ADR-007). Lo que se registra debe venir ya enmascarado: es lo que sale hacia el
 * proveedor de embeddings. Si el proveedor no está disponible (o no hay API key), busca por texto.
 */
public interface IndiceSemantico {

    enum TipoRecurso { TICKET, ARTICULO }

    /** Cómo se encontró: por significado (vectores) o por texto (respaldo). */
    enum Metodo { SIGNIFICADO, TEXTO }

    record Coincidencia(String id, double similitud, Metodo metodo) {}

    /** Deja el recurso pendiente de indexar, en la misma transacción que lo crea; el vector se calcula después. */
    void registrar(TipoRecurso tipo, String id, String textoEnmascarado);

    /** Los más parecidos a la consulta, sin incluir {@code excluirId} (por ejemplo, el ticket que se está triando). */
    List<Coincidencia> buscar(TipoRecurso tipo, String consulta, String excluirId, int limite);

    /** De los códigos de artículo dados, los que existen. */
    Set<String> articulosExistentes(Collection<String> codigos);
}
