package cr.centinela.mesaservicio.plataforma;

/** Roles del perfil del proyecto (AGENTS.md). En la fase 4 vienen de Keycloak con los mismos nombres. */
public enum Rol {
    SOLICITANTE,
    TECNICO,
    COORDINADOR,
    APROBADOR_CAMBIOS,
    AUDITOR;

    /** Nombre de la authority de Spring Security (ROLE_TECNICO…). */
    public String authority() {
        return "ROLE_" + name();
    }
}
