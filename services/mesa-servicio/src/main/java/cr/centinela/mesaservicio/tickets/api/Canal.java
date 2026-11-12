package cr.centinela.mesaservicio.tickets.api;

import com.fasterxml.jackson.annotation.JsonCreator;
import com.fasterxml.jackson.annotation.JsonValue;
import java.util.Arrays;

public enum Canal {
    PORTAL("portal"),
    CORREO("correo"),
    TELEFONO("telefono");

    private final String codigo;

    Canal(String codigo) {
        this.codigo = codigo;
    }

    @JsonValue
    public String codigo() {
        return codigo;
    }

    @JsonCreator
    public static Canal desdeCodigo(String codigo) {
        return Arrays.stream(values())
                .filter(c -> c.codigo.equals(codigo))
                .findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Canal desconocido"));
    }
}
