package cr.centinela.mesaservicio.conocimiento.internal;

import java.util.List;

/** Frontera real con un servicio externo (ADR-007): Voyage AI hoy; otro proveedor sería otro adaptador. */
interface ProveedorEmbeddings {

    /** Voyage recomienda distinguir lo que se guarda de lo que se busca. */
    enum Uso { DOCUMENTO, CONSULTA }

    boolean habilitado();

    String modelo();

    /** Un vector por texto, en el mismo orden. Los textos deben venir enmascarados. */
    List<float[]> vectorizar(List<String> textos, Uso uso);

    /**
     * Falla del proveedor. Las permanentes (key inválida, sin saldo, petición inválida) no se reintentan; las
     * transitorias (límite de peticiones, 5xx, timeouts) sí, con backoff.
     */
    final class ErrorEmbeddings extends RuntimeException {

        private final boolean reintentable;
        private final String codigo;

        ErrorEmbeddings(String codigo, boolean reintentable) {
            super(codigo);
            this.codigo = codigo;
            this.reintentable = reintentable;
        }

        boolean reintentable() {
            return reintentable;
        }

        String codigo() {
            return codigo;
        }
    }
}
