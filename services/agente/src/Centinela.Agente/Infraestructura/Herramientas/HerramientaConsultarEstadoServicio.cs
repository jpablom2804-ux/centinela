using System.Text.Json;
using System.Text.Json.Nodes;
using Centinela.Agente.Dominio;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Infraestructura.Herramientas;

/// <summary>Dice si un sistema tiene incidentes abiertos: ayuda a detectar caídas generales y duplicados.</summary>
public sealed class HerramientaConsultarEstadoServicio(RepositorioDatosPrueba repositorio) : IHerramienta
{
    public const string Nombre = "consultar_estado_servicio";

    public DefinicionHerramienta Definicion { get; } = new(
        Nombre,
        "Consulta los incidentes abiertos de un sistema. Úsala cuando identifiques el sistema afectado, " +
        "para saber si ya hay una caída general reportada.",
        new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("sistema"),
            ["properties"] = new JsonObject
            {
                ["sistema"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(Catalogos.Sistemas.Select(s => (JsonNode)s).ToArray()),
                },
            },
        });

    // [FLUJO TRIAJE_AGENTE 6b] Valida la entrada del modelo y busca en los datos de prueba → resultado como dato no confiable
    public Task<ResultadoEjecucion> EjecutarAsync(JsonElement entrada, ContextoHerramienta contexto, CancellationToken ct)
    {
        var (sistema, error) = EntradaHerramienta.LeerTexto(entrada, "sistema", 2, 50);
        if (sistema is null) return Task.FromResult(ResultadoEjecucion.Error(error!));
        if (!Catalogos.Sistemas.Contains(sistema)) return Task.FromResult(ResultadoEjecucion.Error("Sistema fuera del catálogo."));

        var incidentes = repositorio.Datos.IncidentesAbiertos
            .Where(i => i.Sistema == sistema)
            .OrderByDescending(i => i.Desde)
            .Take(EntradaHerramienta.MaxResultados)
            .Select(i => new { id = i.Id, titulo = i.Titulo, estado = i.Estado, desde = i.Desde })
            .ToList();

        // Los incidentes no se pueden sugerir como duplicados: no son tickets, así que no se devuelven IDs.
        return Task.FromResult(new ResultadoEjecucion(EntradaHerramienta.Resultado(incidentes), EsError: false, []));
    }
}
