using System.Text.Json;
using Centinela.Agente;
using Centinela.Agente.Aplicacion;
using Centinela.Agente.Dominio;
using Centinela.Agente.Infraestructura;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

// Dos modos:
//   sin argumentos        → worker: consume agente.tickets-creados (fase 3)
//   <ticket.json>         → triaje de un solo ticket desde un archivo (fase 1; útil para probar el modelo)
if (args.Length > 1)
{
    await Console.Error.WriteLineAsync("Uso: dotnet run --project src/Centinela.Agente [-- <ticket.json>]");
    return 64;
}

var modoArchivo = args.Length == 1;

// appsettings.json se lee junto al ejecutable, no desde la carpeta donde se lanzó el comando.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
// Orden (el último gana): appsettings → user-secrets → archivos de secretos → variables de entorno.
// Las variables van al final para poder ajustar la configuración (no secretos) sin tocar archivos.
builder.Configuration
    .AddUserSecrets<CicloAgente>(optional: true)
    // Cada secreto es un archivo (ADR-005): /run/secrets en contenedor, o la carpeta de CENTINELA_SECRETOS en desarrollo.
    .AddKeyPerFile(Environment.GetEnvironmentVariable("CENTINELA_SECRETOS") ?? "/run/secrets", optional: true)
    .AddEnvironmentVariables();

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    // El correlationId va en el scope de cada mensaje: así un ticket se sigue entre servicios.
    o.IncludeScopes = true;
});
if (modoArchivo)
{
    // Los logs van a stderr; stdout queda solo para el resultado del triaje.
    builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
}

builder.Services.AgregarAgente(builder.Configuration, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Herramientas", "datos-prueba.json"));
if (!modoArchivo)
{
    builder.Services.AgregarWorker(builder.Configuration);
}

using var host = builder.Build();
try
{
    Composicion.ValidarOpciones(host.Services);
}
catch (OptionsValidationException e)
{
    await Console.Error.WriteLineAsync("Configuración inválida:\n- " + string.Join("\n- ", e.Failures));
    return 78;
}

if (!modoArchivo)
{
    // [FLUJO TRIAJE_ASINCRONO 4] (arranque) Migra el esquema propio y empieza a consumir → ConsumidorTicketsCreados.AtenderAsync
    try
    {
        await host.RunAsync();
    }
    catch (OptionsValidationException e)
    {
        await Console.Error.WriteLineAsync("Configuración inválida:\n- " + string.Join("\n- ", e.Failures));
        return 78;
    }

    return 0;
}

// [FLUJO TRIAJE_AGENTE 1] Lee el ticket de un archivo JSON, lo valida y arma los servicios → CicloAgente.TriarAsync (paso 2)
Ticket ticket;
try
{
    var entrada = JsonSerializer.Deserialize<EntradaTicket>(await File.ReadAllTextAsync(args[0]), FormatoSalida.Opciones)
        ?? throw new JsonException("Archivo vacío.");
    ticket = entrada.ATicket();
}
catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
{
    await Console.Error.WriteLineAsync($"No se pudo leer el ticket: {e.Message}");
    return 65;
}

var errores = ticket.Validar();
if (errores.Count > 0)
{
    await Console.Error.WriteLineAsync("Ticket inválido:\n- " + string.Join("\n- ", errores));
    return 65;
}

var ciclo = host.Services.GetRequiredService<CicloAgente>();
var resultado = await ciclo.TriarAsync(ticket, CancellationToken.None);
Console.WriteLine(JsonSerializer.Serialize(FormatoSalida.TicketTriado(resultado), FormatoSalida.OpcionesIndentadas));
return 0;
