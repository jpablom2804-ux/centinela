package cr.centinela.mesaservicio.plataforma;

import static org.assertj.core.api.Assertions.assertThat;

import java.nio.file.Path;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;
import tools.jackson.databind.json.JsonMapper;

class EnmascaradorTest {

    /** El mismo archivo lo prueba el agente: si las dos barreras se separan, falla uno de los dos lados. */
    private static final Path CONTRATO = Path.of("../../tests/contratos/enmascarado.json");

    record Caso(String nombre, String entrada, String esperado) {}

    record Contrato(String descripcion, List<Caso> casos) {}

    static Stream<Arguments> casosDelContrato() {
        var contrato = JsonMapper.builder().build().readValue(CONTRATO.toFile(), Contrato.class);
        return contrato.casos().stream().map(c -> Arguments.of(c.nombre(), c));
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("casosDelContrato")
    void cumple_el_contrato_compartido_con_el_agente(String nombre, Caso caso) {
        var resultado = Enmascarador.enmascarar(caso.entrada());

        assertThat(resultado).isEqualTo(caso.esperado());
        assertThat(Enmascarador.detectar(resultado)).isEmpty();
    }

    @Test
    void varios_datos_en_el_mismo_texto() {
        var texto = "Soy 1-2345-0678, mi correo es ana@banco.cr y mi celular 8888 1234; la tarjeta 4111111111111111";

        assertThat(Enmascarador.enmascarar(texto))
                .isEqualTo("Soy [CEDULA], mi correo es [CORREO] y mi celular [TELEFONO]; la tarjeta [TARJETA]");
    }

    @Test
    void trece_a_diecinueve_digitos_sin_luhn_es_cuenta() {
        assertThat(Enmascarador.enmascarar("cuenta 1234567890123")).isEqualTo("cuenta [CUENTA]");
    }

    @Test
    void null_sigue_siendo_null() {
        assertThat(Enmascarador.enmascarar(null)).isNull();
    }

    @Test
    void es_idempotente() {
        var una = Enmascarador.enmascarar("Llamar al 8888-1234 o a ana@banco.cr");

        assertThat(Enmascarador.enmascarar(una)).isEqualTo(una);
    }

    @Test
    void los_digitos_de_otros_alfabetos_tambien_cuentan() {
        // Dígitos arábigo-índicos: el número no se esconde cambiando de alfabeto.
        assertThat(Enmascarador.enmascarar("tel ٨٨٨٨١٢٣٤")).isEqualTo("tel [TELEFONO]");
    }
}
