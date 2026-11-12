package cr.centinela.mesaservicio.tickets.api;

import com.fasterxml.jackson.annotation.JsonCreator;
import com.fasterxml.jackson.annotation.JsonValue;
import java.util.Arrays;

public enum TipoTicket {
    INCIDENTE("incidente"),
    SOLICITUD("solicitud"),
    PROBLEMA("problema"),
    CAMBIO("cambio");

    private final String codigo;

    TipoTicket(String codigo) {
        this.codigo = codigo;
    }

    @JsonValue
    public String codigo() {
        return codigo;
    }

    @JsonCreator
    public static TipoTicket desdeCodigo(String codigo) {
        return Arrays.stream(values())
                .filter(t -> t.codigo.equals(codigo))
                .findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Tipo desconocido"));
    }
}
