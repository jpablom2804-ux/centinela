package cr.centinela.mesaservicio.tickets.api;

import com.fasterxml.jackson.annotation.JsonCreator;
import com.fasterxml.jackson.annotation.JsonValue;
import java.util.Arrays;
import java.util.EnumSet;
import java.util.Set;

/**
 * Estados del ticket y sus transiciones válidas (docs/ARQUITECTURA.md):
 * nuevo → triado → asignado → en_progreso ⇄ en_espera → resuelto → cerrado.
 * Un ticket resuelto o cerrado solo vuelve a en_progreso reabriéndolo con justificación.
 */
public enum EstadoTicket {
    NUEVO("nuevo"),
    TRIADO("triado"),
    ASIGNADO("asignado"),
    EN_PROGRESO("en_progreso"),
    EN_ESPERA("en_espera"),
    RESUELTO("resuelto"),
    CERRADO("cerrado");

    private final String codigo;

    EstadoTicket(String codigo) {
        this.codigo = codigo;
    }

    @JsonValue
    public String codigo() {
        return codigo;
    }

    @JsonCreator
    public static EstadoTicket desdeCodigo(String codigo) {
        return Arrays.stream(values())
                .filter(e -> e.codigo.equals(codigo))
                .findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Estado desconocido"));
    }

    public Set<EstadoTicket> siguientes() {
        return switch (this) {
            case NUEVO -> EnumSet.of(TRIADO, ASIGNADO);
            case TRIADO -> EnumSet.of(ASIGNADO);
            case ASIGNADO -> EnumSet.of(EN_PROGRESO);
            case EN_PROGRESO -> EnumSet.of(EN_ESPERA, RESUELTO);
            case EN_ESPERA -> EnumSet.of(EN_PROGRESO);
            case RESUELTO -> EnumSet.of(CERRADO, EN_PROGRESO);
            case CERRADO -> EnumSet.of(EN_PROGRESO);
        };
    }

    public boolean puedePasarA(EstadoTicket destino) {
        return siguientes().contains(destino);
    }

    /** Volver a trabajar un ticket que ya estaba resuelto o cerrado: exige justificación. */
    public boolean esReapertura(EstadoTicket destino) {
        return (this == RESUELTO || this == CERRADO) && destino == EN_PROGRESO;
    }

    /** Estados en los que el ticket no se edita (AGENTS.md: un ticket cerrado se reabre, no se edita). */
    public boolean esFinal() {
        return this == CERRADO;
    }
}
