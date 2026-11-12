using System.Text.Json;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Infraestructura.Herramientas;

/// <summary>Busca artículos de la base de conocimiento que ayuden a resolver el ticket.</summary>
public sealed class HerramientaBuscarArticulos(RepositorioDatosPrueba repositorio) : IHerramienta
{
    public const string Nombre = "buscar_articulos_conocimiento";

    public DefinicionHerramienta Definicion { get; } = new(
        Nombre,
        "Busca artículos de solución en la base de conocimiento. Úsala para sugerir al técnico pasos " +
        $"documentados. Devuelve como máximo {EntradaHerramienta.MaxResultados} artículos.",
        EntradaHerramienta.EsquemaTexto("consulta", "Palabras clave del problema, por ejemplo: restablecer contraseña directorio activo"));

    // [FLUJO TRIAJE_AGENTE 6b] Valida la entrada del modelo y busca en los datos de prueba → resultado como dato no confiable
    public Task<ResultadoEjecucion> EjecutarAsync(JsonElement entrada, ContextoHerramienta contexto, CancellationToken ct)
    {
        var (consulta, error) = EntradaHerramienta.LeerTexto(entrada, "consulta", 3, 300);
        if (consulta is null) return Task.FromResult(ResultadoEjecucion.Error(error!));

        var encontrados = RepositorioDatosPrueba.Buscar(
                repositorio.Datos.Articulos,
                consulta,
                a => $"{a.Titulo} {a.Contenido} {string.Join(' ', a.Sistemas)}",
                EntradaHerramienta.MaxResultados)
            .Select(x => new
            {
                id = x.Elemento.Id,
                titulo = x.Elemento.Titulo,
                extracto = EntradaHerramienta.Recortar(x.Elemento.Contenido, 600),
                similitud = Math.Round(x.Puntaje, 2),
            })
            .ToList();

        return Task.FromResult(new ResultadoEjecucion(
            EntradaHerramienta.Resultado(encontrados), EsError: false, encontrados.Select(e => e.id).ToList(), TipoIdDevuelto.Articulo));
    }
}
