using Centinela.Agente.Dominio;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Aplicacion;

/// <summary>
/// El ciclo del agente, escrito a mano: modelo → herramienta → resultado → repetir, hasta la
/// respuesta final o hasta agotar el presupuesto. Cualquier falla deja el ticket sin sugerencia.
/// </summary>
public sealed partial class CicloAgente(
    IClienteLlm cliente,
    IEnumerable<IHerramienta> herramientas,
    IOptions<OpcionesAgente> opciones,
    TimeProvider reloj,
    ILogger<CicloAgente> logger)
{
    private readonly Dictionary<string, IHerramienta> _herramientas =
        herramientas.ToDictionary(h => h.Definicion.Nombre, StringComparer.Ordinal);

    private readonly OpcionesAgente _opciones = opciones.Value;

    // [FLUJO TRIAJE_AGENTE 2] Abre el presupuesto del ticket (tiempo, iteraciones, tokens) y arma el primer mensaje → bucle del paso 3
    public async Task<ResultadoTriaje> TriarAsync(Ticket ticket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var presupuesto = new PresupuestoTriaje(_opciones.MaxIteraciones, _opciones.MaxTokensTicket, TimeSpan.FromSeconds(_opciones.TimeoutSegundos));
        var estado = new EstadoCiclo(reloj.GetTimestamp());

        using var porTiempo = new CancellationTokenSource(presupuesto.TiempoMaximo, reloj);
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct, porTiempo.Token);

        ResultadoTriaje resultado;
        try
        {
            resultado = await EjecutarAsync(ticket, presupuesto, estado, limite.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (porTiempo.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            PresupuestoAgotado(logger, ticket.TicketId, "tiempo");
            resultado = SinSugerencia(ticket, EstadoAnalisis.PresupuestoAgotado, estado);
        }
        catch (ErrorLlmException e)
        {
            if (e.RequiereAlerta)
            {
                // Saldo, key o permisos: ningún reintento lo arregla y alguien tiene que actuar.
                AlertaApiModelo(logger, ticket.TicketId, e.Tipo, e.CodigoHttp, e.CodigoApi ?? "-");
            }
            else
            {
                ErrorApiModelo(logger, ticket.TicketId, e.Tipo, e.CodigoHttp, e.CodigoApi ?? "-");
            }

            resultado = SinSugerencia(ticket, EstadoAnalisis.Error, estado);
        }

        RegistroMetricas.Registrar(logger, resultado);
        return resultado;
    }

    private async Task<ResultadoTriaje> EjecutarAsync(Ticket ticket, PresupuestoTriaje presupuesto, EstadoCiclo estado, CancellationToken ct)
    {
        var mensajes = new List<MensajeConversacion> { new MensajeUsuario(Prompts.MensajeTicket(ticket)) };
        var definiciones = _herramientas.Values.Select(h => h.Definicion).ToList();
        var contexto = new ContextoHerramienta(ticket.TicketId);

        while (true)
        {
            if (!presupuesto.QuedanIteraciones(estado.Iteraciones) || !presupuesto.QuedanTokens(estado.TokensTotales))
            {
                PresupuestoAgotado(logger, ticket.TicketId, estado.Iteraciones >= presupuesto.MaxIteraciones ? "iteraciones" : "tokens");
                return SinSugerencia(ticket, EstadoAnalisis.PresupuestoAgotado, estado);
            }

            // [FLUJO TRIAJE_AGENTE 3] Revisa que nada sin enmascarar vaya al modelo; si encuentra algo, corta sin llamarlo → paso 4
            var sensibles = DetectarDatosSensibles(mensajes);
            if (sensibles.Count > 0)
            {
                DatosSinEnmascarar(logger, ticket.TicketId, string.Join(",", sensibles.Order(StringComparer.Ordinal)));
                return SinSugerencia(ticket, EstadoAnalisis.Error, estado);
            }

            var solicitud = new SolicitudLlm(Prompts.Sistema, definiciones, ValidadorTriaje.Esquema(), mensajes.ToList());
            var respuesta = await LlamarModeloAsync(solicitud, ct).ConfigureAwait(false);
            estado.Registrar(respuesta);

            switch (respuesta.Motivo)
            {
                case MotivoParada.Final:
                    var validacion = ValidadorTriaje.Validar(respuesta.Texto, estado.IdsVistos);
                    if (validacion.EsValido)
                    {
                        return ResultadoTriaje.Completado(ticket.TicketId, validacion.Sugerencia!, estado.Metricas(reloj));
                    }

                    RespuestaDescartada(logger, ticket.TicketId, string.Join("; ", validacion.Errores));
                    if (estado.Reparaciones >= _opciones.MaxReparaciones)
                    {
                        return SinSugerencia(ticket, EstadoAnalisis.Invalido, estado);
                    }

                    // [FLUJO TRIAJE_AGENTE 7b] Turno de corrección: le dice al modelo qué falló (con mensajes propios,
                    // sin repetir su texto) y le da otra oportunidad dentro del mismo presupuesto → vuelve al paso 3
                    estado.Reparaciones++;
                    mensajes.Add(new MensajeAsistente(respuesta.ContenidoProveedor));
                    mensajes.Add(new MensajeUsuario(Prompts.MensajeCorreccion(validacion.Errores)));
                    break;

                case MotivoParada.UsoHerramientas when respuesta.Llamadas.Count > 0:
                    mensajes.Add(new MensajeAsistente(respuesta.ContenidoProveedor));
                    var resultados = await EjecutarHerramientasAsync(ticket.TicketId, respuesta.Llamadas, contexto, estado, ct).ConfigureAwait(false);
                    mensajes.Add(new ResultadosHerramientas(resultados));
                    break;

                case MotivoParada.LimiteTokens:
                    RespuestaDescartada(logger, ticket.TicketId, "se cortó por max_tokens");
                    return SinSugerencia(ticket, EstadoAnalisis.Invalido, estado);

                case MotivoParada.Rechazo:
                    ModeloRechazo(logger, ticket.TicketId, respuesta.DetalleRechazo ?? "sin categoría");
                    return SinSugerencia(ticket, EstadoAnalisis.Error, estado);

                default:
                    RespuestaDescartada(logger, ticket.TicketId, $"motivo de parada no esperado: {respuesta.Motivo}");
                    return SinSugerencia(ticket, EstadoAnalisis.Error, estado);
            }
        }
    }

    // [FLUJO TRIAJE_AGENTE 4] Llama al modelo; reintenta con backoff solo los errores reintentables → ClienteAnthropicHttp.EnviarAsync (paso 5)
    private async Task<RespuestaLlm> LlamarModeloAsync(SolicitudLlm solicitud, CancellationToken ct)
    {
        for (var intento = 0; ; intento++)
        {
            try
            {
                return await cliente.EnviarAsync(solicitud, ct).ConfigureAwait(false);
            }
            catch (ErrorLlmException e) when (e.Reintentable && intento < _opciones.MaxReintentos)
            {
                var espera = e.EsperaSugerida ?? TimeSpan.FromMilliseconds(_opciones.BackoffBaseMs * Math.Pow(2, intento));

                // Si el proveedor pide esperar minutos (cuota agotada), esperar solo gasta el tiempo del ticket: se corta ya.
                if (espera > TimeSpan.FromSeconds(_opciones.MaxEsperaReintentoSegundos))
                {
                    EsperaDemasiadoLarga(logger, e.Tipo, (long)espera.TotalSeconds, _opciones.MaxEsperaReintentoSegundos);
                    throw;
                }

                Reintento(logger, e.Tipo, e.CodigoApi ?? "-", intento + 1, (long)espera.TotalMilliseconds);
                await Task.Delay(espera, reloj, ct).ConfigureAwait(false);
            }
        }
    }

    // [FLUJO TRIAJE_AGENTE 6] Ejecuta las herramientas pedidas con el contexto inyectado y devuelve todos los resultados juntos → vuelve al paso 3
    private async Task<List<ResultadoHerramienta>> EjecutarHerramientasAsync(
        Guid ticketId, IReadOnlyList<LlamadaHerramienta> llamadas, ContextoHerramienta contexto, EstadoCiclo estado, CancellationToken ct)
    {
        var resultados = new List<ResultadoHerramienta>(llamadas.Count);
        foreach (var llamada in llamadas)
        {
            ResultadoEjecucion ejecucion;
            if (!_herramientas.TryGetValue(llamada.Nombre, out var herramienta))
            {
                ejecucion = ResultadoEjecucion.Error($"La herramienta '{llamada.Nombre}' no existe.");
            }
            else
            {
                try
                {
                    ejecucion = await herramienta.EjecutarAsync(llamada.Entrada, contexto, ct).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Una herramienta rota no debe tumbar el triaje: el modelo recibe el error y sigue o se agota el presupuesto.
                catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    HerramientaFallo(logger, ticketId, llamada.Nombre, e.GetType().Name);
                    ejecucion = ResultadoEjecucion.Error("La herramienta falló. Continúa sin ese dato.");
                }
            }

            if (ejecucion.EsError) HerramientaConError(logger, ticketId, llamada.Nombre);
            estado.IdsVistos.UnionWith(ejecucion.IdsDevueltos);
            resultados.Add(new ResultadoHerramienta(llamada.Id, ejecucion.Contenido, ejecucion.EsError));
        }

        return resultados;
    }


    private static HashSet<string> DetectarDatosSensibles(IEnumerable<MensajeConversacion> mensajes)
    {
        var encontrados = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mensaje in mensajes)
        {
            switch (mensaje)
            {
                case MensajeUsuario usuario:
                    encontrados.UnionWith(VerificadorEnmascarado.Detectar(usuario.Texto));
                    break;
                case ResultadosHerramientas herramientas:
                    foreach (var r in herramientas.Resultados) encontrados.UnionWith(VerificadorEnmascarado.Detectar(r.Contenido));
                    break;
            }
        }

        return encontrados;
    }

    private ResultadoTriaje SinSugerencia(Ticket ticket, EstadoAnalisis estadoAnalisis, EstadoCiclo estado) =>
        ResultadoTriaje.SinSugerencia(ticket.TicketId, estadoAnalisis, estado.Metricas(reloj));

    /// <summary>Lo que se acumula durante un triaje.</summary>
    private sealed class EstadoCiclo(long inicio)
    {
        public int Iteraciones { get; private set; }
        public int TokensEntrada { get; private set; }
        public int TokensSalida { get; private set; }
        public string Modelo { get; private set; } = "sin llamada";
        public HashSet<string> IdsVistos { get; } = new(StringComparer.Ordinal);
        public int Reparaciones { get; set; }
        public int TokensTotales => TokensEntrada + TokensSalida;

        public void Registrar(RespuestaLlm respuesta)
        {
            Iteraciones++;
            TokensEntrada += respuesta.Uso.Entrada;
            TokensSalida += respuesta.Uso.Salida;
            Modelo = respuesta.Modelo;
        }

        public MetricasTriaje Metricas(TimeProvider reloj) =>
            new(Modelo, TokensEntrada, TokensSalida, Iteraciones, (long)reloj.GetElapsedTime(inicio).TotalMilliseconds);
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Warning, Message = "Ticket {TicketId}: presupuesto agotado por {Motivo}; pasa a triaje manual")]
    private static partial void PresupuestoAgotado(ILogger logger, Guid ticketId, string motivo);

    [LoggerMessage(EventId = 201, Level = LogLevel.Warning, Message = "Ticket {TicketId}: datos sin enmascarar ({Tipos}); no se llamó al modelo")]
    private static partial void DatosSinEnmascarar(ILogger logger, Guid ticketId, string tipos);

    [LoggerMessage(EventId = 202, Level = LogLevel.Warning, Message = "Ticket {TicketId}: respuesta del modelo descartada: {Motivo}")]
    private static partial void RespuestaDescartada(ILogger logger, Guid ticketId, string motivo);

    [LoggerMessage(EventId = 203, Level = LogLevel.Warning, Message = "Ticket {TicketId}: el modelo rechazó la solicitud ({Categoria})")]
    private static partial void ModeloRechazo(ILogger logger, Guid ticketId, string categoria);

    [LoggerMessage(EventId = 204, Level = LogLevel.Warning, Message = "Ticket {TicketId}: la herramienta {Herramienta} devolvió error")]
    private static partial void HerramientaConError(ILogger logger, Guid ticketId, string herramienta);

    [LoggerMessage(EventId = 208, Level = LogLevel.Error, Message = "Ticket {TicketId}: la herramienta {Herramienta} lanzó {TipoExcepcion}")]
    private static partial void HerramientaFallo(ILogger logger, Guid ticketId, string herramienta, string tipoExcepcion);

    [LoggerMessage(EventId = 209, Level = LogLevel.Warning, Message = "La API del modelo pide esperar {EsperaSegundos} s ({Tipo}), más que el máximo de {MaximoSegundos} s: no se reintenta")]
    private static partial void EsperaDemasiadoLarga(ILogger logger, TipoErrorLlm tipo, long esperaSegundos, int maximoSegundos);

    [LoggerMessage(EventId = 205, Level = LogLevel.Warning, Message = "Error reintentable de la API del modelo ({Tipo}, {CodigoApi}); reintento {Intento} en {EsperaMs} ms")]
    private static partial void Reintento(ILogger logger, TipoErrorLlm tipo, string codigoApi, int intento, long esperaMs);

    [LoggerMessage(EventId = 206, Level = LogLevel.Error, Message = "Ticket {TicketId}: error de la API del modelo ({Tipo}, HTTP {CodigoHttp}, {CodigoApi}); pasa a triaje manual")]
    private static partial void ErrorApiModelo(ILogger logger, Guid ticketId, TipoErrorLlm tipo, int? codigoHttp, string codigoApi);

    [LoggerMessage(EventId = 207, Level = LogLevel.Critical, Message = "ALERTA ticket {TicketId}: la API del modelo no se puede usar ({Tipo}, HTTP {CodigoHttp}, {CodigoApi}). Revisar saldo, API key o permisos")]
    private static partial void AlertaApiModelo(ILogger logger, Guid ticketId, TipoErrorLlm tipo, int? codigoHttp, string codigoApi);
}
