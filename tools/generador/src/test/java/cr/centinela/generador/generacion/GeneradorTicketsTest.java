package cr.centinela.generador.generacion;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import java.util.Set;
import java.util.regex.Pattern;
import java.util.stream.IntStream;
import org.junit.jupiter.api.Test;

class GeneradorTicketsTest {

    private static final Set<String> TIPOS = Set.of("incidente", "solicitud", "problema", "cambio");
    private static final Set<String> CANALES = Set.of("portal", "correo", "telefono");
    // Lo que el enmascarado marcaría: 8 o más dígitos seguidos (con separadores) o un "@".
    private static final Pattern PARECE_DATO_PERSONAL = Pattern.compile("\\d(?:[\\s.\\-/]?\\d){7,}|@");

    private static List<TicketSintetico> generar(long semilla, int cantidad) {
        var generador = new GeneradorTickets(semilla);
        return IntStream.range(0, cantidad).mapToObj(i -> generador.siguiente()).toList();
    }

    @Test
    void la_misma_semilla_genera_la_misma_secuencia() {
        assertThat(generar(42, 50)).isEqualTo(generar(42, 50));
        assertThat(generar(42, 50)).isNotEqualTo(generar(43, 50));
    }

    @Test
    void los_tickets_cumplen_el_contrato_de_la_api() {
        assertThat(generar(7, 500)).allSatisfy(t -> {
            assertThat(TIPOS).contains(t.tipo());
            assertThat(CANALES).contains(t.canal());
            assertThat(t.titulo()).isNotBlank().hasSizeLessThanOrEqualTo(200);
            assertThat(t.descripcion()).isNotBlank().hasSizeLessThanOrEqualTo(5000);
            if (t.sistemaReportado() != null) {
                assertThat(t.sistemaReportado()).hasSizeLessThanOrEqualTo(100);
            }
        });
    }

    @Test
    void ningun_texto_parece_un_dato_personal() {
        // Datos 100 % sintéticos: nada que el enmascarado tenga que tapar.
        for (var caso : CatalogoCasos.CASOS) {
            for (var texto : concatenar(caso.titulos(), caso.descripciones())) {
                assertThat(PARECE_DATO_PERSONAL.matcher(texto).find()).as(texto).isFalse();
            }
        }
    }

    @Test
    void usa_variedad_de_casos() {
        assertThat(generar(1, 300).stream().map(TicketSintetico::titulo).distinct().count()).isGreaterThan(15);
    }

    private static List<String> concatenar(List<String> a, List<String> b) {
        return java.util.stream.Stream.concat(a.stream(), b.stream()).toList();
    }
}
