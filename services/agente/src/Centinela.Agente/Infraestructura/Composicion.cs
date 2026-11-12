using Centinela.Agente.Aplicacion;
using Centinela.Agente.Infraestructura.Herramientas;
using Centinela.Agente.Infraestructura.Llm;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Infraestructura;

/// <summary>Registro de dependencias, compartido por Program y la suite adversaria con el modelo real.</summary>
public static class Composicion
{
    public static IServiceCollection AgregarAgente(this IServiceCollection servicios, IConfiguration configuracion, string rutaDatosPrueba)
    {
        servicios.AddOptions<OpcionesAgente>()
            .Bind(configuracion.GetSection(OpcionesAgente.Seccion))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicios.AddOptions<OpcionesLlm>()
            .Bind(configuracion.GetSection(OpcionesLlm.Seccion))
            .ValidateDataAnnotations()
            .Validate(o => OpcionesLlm.EsfuerzosValidos.Contains(o.Esfuerzo), "Llm:Esfuerzo debe ser low, medium, high, xhigh o max.")
            .ValidateOnStart();

        servicios.AddHttpClient<IClienteLlm, ClienteAnthropicHttp>();
        servicios.AddSingleton(TimeProvider.System);
        servicios.AddSingleton(RepositorioDatosPrueba.Cargar(rutaDatosPrueba));
        servicios.AddSingleton<IHerramienta, HerramientaBuscarTicketsSimilares>();
        servicios.AddSingleton<IHerramienta, HerramientaBuscarArticulos>();
        servicios.AddSingleton<IHerramienta, HerramientaConsultarEstadoServicio>();
        servicios.AddTransient<CicloAgente>();
        return servicios;
    }

    /// <summary>Fuerza la validación de opciones sin arrancar un host (es una app de consola).</summary>
    public static void ValidarOpciones(IServiceProvider proveedor)
    {
        _ = proveedor.GetRequiredService<IOptions<OpcionesAgente>>().Value;
        _ = proveedor.GetRequiredService<IOptions<OpcionesLlm>>().Value;
    }
}
