using Centinela.Agente.Aplicacion;
using Centinela.Agente.Infraestructura.ApiInterna;
using Centinela.Agente.Infraestructura.Herramientas;
using Centinela.Agente.Infraestructura.Llm;
using Centinela.Agente.Infraestructura.Mensajeria;
using Centinela.Agente.Infraestructura.Persistencia;
using Npgsql;
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
            .ValidateOnStart();
        servicios.AddSingleton<IValidateOptions<OpcionesLlm>, ValidadorOpcionesLlm>();

        // Dos adaptadores reales del mismo puerto (ADR-006); el proveedor se elige por configuración.
        servicios.AddHttpClient<ClienteAnthropicHttp>();
        servicios.AddHttpClient<ClienteOpenAiCompatibleHttp>();
        servicios.AddTransient<IClienteLlm>(sp =>
            sp.GetRequiredService<IOptions<OpcionesLlm>>().Value.Proveedor == OpcionesLlm.ProveedorCompatibleOpenAi
                ? sp.GetRequiredService<ClienteOpenAiCompatibleHttp>()
                : sp.GetRequiredService<ClienteAnthropicHttp>());
        servicios.AddSingleton(TimeProvider.System);
        // Por defecto, los datos sintéticos (modo archivo); el worker los reemplaza por la API interna de mesa-servicio.
        servicios.AddSingleton(RepositorioDatosPrueba.Cargar(rutaDatosPrueba));
        servicios.AddSingleton<IConsultasMesaServicio, ConsultasDatosPrueba>();
        // Transitorias: el cliente HTTP de la API interna se maneja con IHttpClientFactory.
        servicios.AddTransient<IHerramienta, HerramientaBuscarTicketsSimilares>();
        servicios.AddTransient<IHerramienta, HerramientaBuscarArticulos>();
        servicios.AddTransient<IHerramienta, HerramientaConsultarEstadoServicio>();
        servicios.AddTransient<CicloAgente>();
        return servicios;
    }

    /// <summary>Lo que suma el modo worker: base de datos propia, RabbitMQ y el consumidor (fase 3).</summary>
    public static IServiceCollection AgregarWorker(this IServiceCollection servicios, IConfiguration configuracion)
    {
        // Las contraseñas llegan como archivos (ADR-005); el nombre del archivo es la clave de configuración.
        servicios.AddOptions<OpcionesBaseDatos>()
            .Bind(configuracion.GetSection(OpcionesBaseDatos.Seccion))
            .PostConfigure(o => o.Password = configuracion[OpcionesBaseDatos.ClaveSecreto] ?? o.Password)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        servicios.AddOptions<OpcionesMensajeria>()
            .Bind(configuracion.GetSection(OpcionesMensajeria.Seccion))
            .PostConfigure(o => o.Password = configuracion[OpcionesMensajeria.ClaveSecreto] ?? o.Password)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicios.AddSingleton(sp => NpgsqlDataSource.Create(sp.GetRequiredService<IOptions<OpcionesBaseDatos>>().Value.CadenaConexion()));
        servicios.AddOptions<OpcionesApiInterna>()
            .Bind(configuracion.GetSection(OpcionesApiInterna.Seccion))
            .PostConfigure(o => o.Password = configuracion[OpcionesApiInterna.ClaveSecreto] ?? o.Password)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // Las herramientas consultan datos reales (fase 3): reemplaza a los datos sintéticos.
        servicios.AddHttpClient<IConsultasMesaServicio, ClienteApiInterna>();

        servicios.AddSingleton<IAlmacenTriajes, AlmacenTriajesPostgres>();
        servicios.AddSingleton<ConexionRabbit>();
        servicios.AddSingleton<IPublicadorTriajes, PublicadorTriajesRabbit>();
        servicios.AddTransient<ProcesadorTicketCreado>();
        // En este orden: las migraciones terminan antes de que el consumidor reciba el primer mensaje.
        servicios.AddHostedService<MigradorEsquema>();
        servicios.AddHostedService<ConsumidorTicketsCreados>();
        return servicios;
    }

    /// <summary>Fuerza la validación de opciones sin arrancar un host (es una app de consola).</summary>
    public static void ValidarOpciones(IServiceProvider proveedor)
    {
        _ = proveedor.GetRequiredService<IOptions<OpcionesAgente>>().Value;
        _ = proveedor.GetRequiredService<IOptions<OpcionesLlm>>().Value;
    }
}
