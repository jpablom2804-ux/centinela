using System.Text.Json;
using System.Text.Json.Nodes;
using Centinela.Agente.Dominio;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Infraestructura.Herramientas;

/// <summary>Dice si un sistema tiene incidentes abiertos: ayuda a detectar caídas generales y duplicados.</summary>
public sealed class HerramientaConsultarEstadoServicio(IConsultasMesaServicio consultas) : IHerramienta
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

    // [FLUJO TRIAJE_AGENTE 6b] Valida la entrada del modelo y consulta (API interna o datos de prueba) → resultado como dato no confiable
    public async Task<ResultadoEjecucion> EjecutarAsync(JsonElement entrada, ContextoHerramienta contexto, CancellationToken ct)
    {
        var (sistema, error) = EntradaHerramienta.LeerTexto(entrada, "sistema", 2, 50);
        if (sistema is null) return ResultadoEjecucion.Error(error!);
        if (!Catalogos.Sistemas.Contains(sistema)) return ResultadoEjecucion.Error("Sistema fuera del catálogo.");

        IReadOnlyList<IncidenteServicio> abiertos;
        try
        {
            abiertos = await consultas.IncidentesAbiertosAsync(sistema, EntradaHerramienta.MaxResultados, ct);
        }
        catch (ConsultaNoDisponibleException)
        {
            return ResultadoEjecucion.Error(EntradaHerramienta.FuenteNoDisponible);
        }

        var incidentes = abiertos
            .Take(EntradaHerramienta.MaxResultados)
            .Select(i => new { id = i.Id, titulo = EntradaHerramienta.Recortar(i.Titulo, 200), estado = i.Estado, desde = i.Desde })
            .ToList();

        // Los incidentes no se pueden sugerir como duplicados desde aquí: no se devuelven IDs.
        return new ResultadoEjecucion(EntradaHerramienta.Resultado(incidentes), EsError: false, []);
    }
}
