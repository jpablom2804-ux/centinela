package cr.centinela.mesaservicio.plataforma;

import java.util.EnumSet;
import java.util.Set;
import org.springframework.security.core.Authentication;

/**
 * Quién hace la petición. El id es el nombre de usuario en la fase 2; en la fase 4 será el {@code sub} de
 * Keycloak (A7: la identidad se compara por id, nunca por nombre o correo).
 */
public record UsuarioActual(String id, Set<Rol> roles) {

    public static UsuarioActual de(Authentication autenticacion) {
        var roles = EnumSet.noneOf(Rol.class);
        for (var authority : autenticacion.getAuthorities()) {
            for (var rol : Rol.values()) {
                if (rol.authority().equals(authority.getAuthority())) {
                    roles.add(rol);
                }
            }
        }
        return new UsuarioActual(autenticacion.getName(), Set.copyOf(roles));
    }

    public boolean tiene(Rol rol) {
        return roles.contains(rol);
    }

    /** Técnicos y coordinadores atienden tickets de cualquier solicitante. */
    public boolean esPersonalDeSoporte() {
        return tiene(Rol.TECNICO) || tiene(Rol.COORDINADOR);
    }

    /** Puede ver tickets de otros: soporte y auditoría (esta última solo lectura). */
    public boolean veTodosLosTickets() {
        return esPersonalDeSoporte() || tiene(Rol.AUDITOR);
    }
}
