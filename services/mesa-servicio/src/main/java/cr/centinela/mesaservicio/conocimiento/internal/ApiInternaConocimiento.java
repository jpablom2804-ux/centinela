package cr.centinela.mesaservicio.conocimiento.internal;

import cr.centinela.mesaservicio.conocimiento.api.IndiceSemantico;
import cr.centinela.mesaservicio.plataforma.Enmascarador;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;
import java.util.Arrays;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/**
 * API interna de solo lectura para la herramienta buscar_articulos_conocimiento del agente. No pasa por nginx
 * (solo red interna) y solo la usa la cuenta de servicio del agente. Todo lo que devuelve va enmascarado.
 */
@RestController
@RequestMapping("/interna/articulos")
class ApiInternaConocimiento {

    private static final int MAX_EXTRACTO = 600;

    private final IndiceSemantico indice;
    private final JdbcTemplate jdbc;

    ApiInternaConocimiento(IndiceSemantico indice, JdbcTemplate jdbc) {
        this.indice = indice;
        this.jdbc = jdbc;
    }

    record ArticuloEncontrado(String id, String titulo, String extracto, List<String> sistemas, double similitud, String metodo) {}

    // [FLUJO HERRAMIENTAS_AGENTE 2b] Busca artículos por significado o texto y los devuelve enmascarados → agente (resultado no confiable)
    @GetMapping
    List<ArticuloEncontrado> buscar(@RequestParam @NotBlank @Size(max = 300) String consulta,
            @RequestParam(defaultValue = "5") @Min(1) @Max(10) int limite) {
        var coincidencias = indice.buscar(IndiceSemantico.TipoRecurso.ARTICULO, consulta, null, limite);
        if (coincidencias.isEmpty()) {
            return List.of();
        }
        var codigos = coincidencias.stream().map(IndiceSemantico.Coincidencia::id).toArray(String[]::new);
        Map<String, Map<String, Object>> articulos = jdbc.queryForList(
                        "SELECT codigo, titulo, contenido, sistemas FROM mesa_servicio.articulo WHERE codigo = ANY (?)",
                        (Object) codigos)
                .stream()
                .collect(Collectors.toMap(f -> (String) f.get("codigo"), Function.identity()));
        return coincidencias.stream()
                .filter(c -> articulos.containsKey(c.id()))
                .map(c -> {
                    var a = articulos.get(c.id());
                    var contenido = Enmascarador.enmascarar((String) a.get("contenido"));
                    var sistemas = ((String) a.get("sistemas")).isBlank() ? List.<String>of()
                            : Arrays.asList(((String) a.get("sistemas")).split(","));
                    return new ArticuloEncontrado(c.id(), Enmascarador.enmascarar((String) a.get("titulo")),
                            contenido.length() > MAX_EXTRACTO ? contenido.substring(0, MAX_EXTRACTO) + "…" : contenido,
                            sistemas, Math.round(c.similitud() * 100) / 100.0, c.metodo().name().toLowerCase());
                })
                .toList();
    }
}
