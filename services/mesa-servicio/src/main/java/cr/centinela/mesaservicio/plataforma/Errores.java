package cr.centinela.mesaservicio.plataforma;

/** Excepciones de negocio que el manejador global traduce a { codigo, mensaje }. */
public final class Errores {

    private Errores() {}

    /**
     * El recurso no existe o el usuario no puede verlo. Es la misma excepción en los dos casos a propósito:
     * la respuesta no revela qué existe (AGENTS.md: ticket ajeno e inexistente responden igual).
     */
    public static final class NoEncontrado extends RuntimeException {
        public NoEncontrado(String mensaje) {
            super(mensaje);
        }
    }

    /** El usuario puede ver el recurso, pero no hacer esta acción. */
    public static final class AccionNoPermitida extends RuntimeException {
        public AccionNoPermitida(String mensaje) {
            super(mensaje);
        }
    }

    /** La acción contradice una regla del dominio (transición inválida, falta justificación…). */
    public static final class ReglaDeNegocio extends RuntimeException {
        private final String codigo;

        public ReglaDeNegocio(String codigo, String mensaje) {
            super(mensaje);
            this.codigo = codigo;
        }

        public String codigo() {
            return codigo;
        }
    }
}
