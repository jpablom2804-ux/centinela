using Centinela.Agente.Aplicacion;
using Centinela.Agente.Dominio;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Logging;

namespace Centinela.Agente.Tests;

public class CicloAgenteTests
{
    private const string BuscarVpn = """{"consulta":"vpn no conecta desde casa"}""";

    [Fact]
    public async Task Usa_herramientas_y_completa_con_metricas_sumadas()
    {
        var cliente = new ClienteLlmFalso()
            .Responde(Respuestas.Herramienta("buscar_tickets_similares", BuscarVpn, tokensEntrada: 1_000, tokensSalida: 50))
            .Responde(Respuestas.Final(Respuestas.TriajeVpn(duplicados: ["TK-1001"]), tokensEntrada: 1_500, tokensSalida: 300));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Completado, resultado.Estado);
        Assert.Equal("P3", resultado.Sugerencia!.PrioridadSugerida);
        Assert.Equal(["TK-1001"], resultado.Sugerencia.DuplicadosPosibles);
        Assert.Equal(2, resultado.Metricas.Iteraciones);
        Assert.Equal(2_500, resultado.Metricas.TokensEntrada);
        Assert.Equal(350, resultado.Metricas.TokensSalida);
        Assert.Equal("modelo-falso", resultado.Metricas.Modelo);

        // La segunda llamada lleva la respuesta del modelo y el resultado de la herramienta.
        var segunda = cliente.Solicitudes[1].Mensajes;
        Assert.IsType<MensajeAsistente>(segunda[1]);
        var resultados = Assert.IsType<ResultadosHerramientas>(segunda[2]);
        Assert.Contains("TK-1001", resultados.Resultados[0].Contenido, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Respuesta_que_no_es_json_queda_invalida_y_sin_sugerencia()
    {
        var cliente = new ClienteLlmFalso().Responde(Respuestas.Final("Le pondría P2, creo."));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Invalido, resultado.Estado);
        Assert.Null(resultado.Sugerencia);
    }

    [Theory]
    [InlineData("P0")]
    [InlineData("urgente")]
    public async Task Prioridad_fuera_del_catalogo_queda_invalida(string prioridad)
    {
        var cliente = new ClienteLlmFalso().Responde(Respuestas.Final(Respuestas.TriajeVpn(prioridad)));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Invalido, resultado.Estado);
    }

    [Fact]
    public async Task Duplicado_que_no_devolvio_ninguna_herramienta_queda_invalido()
    {
        // El modelo "inventa" un ID sin haber buscado: no se acepta.
        var cliente = new ClienteLlmFalso().Responde(Respuestas.Final(Respuestas.TriajeVpn(duplicados: ["TK-1001"])));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Invalido, resultado.Estado);
    }

    [Fact]
    public async Task Corta_por_iteraciones_sin_pasarse_del_maximo()
    {
        var cliente = new ClienteLlmFalso().RespondeSiempre(_ => Respuestas.Herramienta("buscar_tickets_similares", BuscarVpn));
        var opciones = new OpcionesAgente { MaxIteraciones = 3, BackoffBaseMs = 1 };

        var resultado = await Fabrica.Ciclo(cliente, opciones).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.PresupuestoAgotado, resultado.Estado);
        Assert.Equal(3, cliente.Solicitudes.Count);
        Assert.Null(resultado.Sugerencia);
    }

    [Fact]
    public async Task Corta_por_tokens()
    {
        var cliente = new ClienteLlmFalso().RespondeSiempre(_ =>
            Respuestas.Herramienta("buscar_tickets_similares", BuscarVpn, tokensEntrada: 3_000, tokensSalida: 100));
        var opciones = new OpcionesAgente { MaxTokensTicket = 5_000, BackoffBaseMs = 1 };

        var resultado = await Fabrica.Ciclo(cliente, opciones).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.PresupuestoAgotado, resultado.Estado);
        Assert.Equal(2, cliente.Solicitudes.Count);
    }

    [Fact]
    public async Task Corta_por_tiempo()
    {
        var cliente = new ClienteLlmFalso { Demora = TimeSpan.FromSeconds(30) }.Responde(Respuestas.Final(Respuestas.TriajeVpn()));
        var opciones = new OpcionesAgente { TimeoutSegundos = 5, BackoffBaseMs = 1 };

        var resultado = await Fabrica.Ciclo(cliente, opciones).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.PresupuestoAgotado, resultado.Estado);
    }

    [Theory]
    [InlineData(TipoErrorLlm.SaldoInsuficiente, 402)]
    [InlineData(TipoErrorLlm.Autenticacion, 401)]
    public async Task Saldo_o_key_invalida_no_se_reintenta_y_dispara_alerta(TipoErrorLlm tipo, int codigo)
    {
        var cliente = new ClienteLlmFalso().Falla(new ErrorLlmException(tipo, reintentable: false, codigo));
        var logger = new LoggerCapturador<CicloAgente>();

        var resultado = await Fabrica.Ciclo(cliente, logger: logger).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Error, resultado.Estado);
        Assert.Single(cliente.Solicitudes);
        Assert.Contains(logger.Registros, r => r.Nivel == LogLevel.Critical && r.Mensaje.Contains("ALERTA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Errores_reintentables_se_reintentan_y_luego_completa()
    {
        var cliente = new ClienteLlmFalso()
            .Falla(new ErrorLlmException(TipoErrorLlm.Saturado, reintentable: true, 529))
            .Falla(new ErrorLlmException(TipoErrorLlm.LimiteTasa, reintentable: true, 429, TimeSpan.FromMilliseconds(5)))
            .Responde(Respuestas.Final(Respuestas.TriajeVpn()));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Completado, resultado.Estado);
        Assert.Equal(3, cliente.Solicitudes.Count);
    }

    [Fact]
    public async Task Reintentos_tienen_maximo()
    {
        var cliente = new ClienteLlmFalso().RespondeSiempre(_ => throw new ErrorLlmException(TipoErrorLlm.ErrorServidor, reintentable: true, 500));
        var opciones = new OpcionesAgente { MaxReintentos = 2, BackoffBaseMs = 1 };

        var resultado = await Fabrica.Ciclo(cliente, opciones).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Error, resultado.Estado);
        Assert.Equal(3, cliente.Solicitudes.Count);
    }

    [Fact]
    public async Task Rechazo_del_modelo_deja_el_ticket_sin_sugerencia()
    {
        var cliente = new ClienteLlmFalso().Responde(Respuestas.ConMotivo(MotivoParada.Rechazo, "cyber"));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Error, resultado.Estado);
        Assert.Null(resultado.Sugerencia);
    }

    [Fact]
    public async Task Respuesta_cortada_por_max_tokens_queda_invalida()
    {
        var cliente = new ClienteLlmFalso().Responde(Respuestas.ConMotivo(MotivoParada.LimiteTokens));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Invalido, resultado.Estado);
    }

    [Fact]
    public async Task Ticket_con_datos_sin_enmascarar_no_llega_al_modelo()
    {
        var cliente = new ClienteLlmFalso().Responde(Respuestas.Final(Respuestas.TriajeVpn()));
        var ticket = Fabrica.TicketVpn("No conecta la VPN. Mi cédula es 1-0987-0654, escríbanme a persona@ejemplo.com.");

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(ticket, CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Error, resultado.Estado);
        Assert.Empty(cliente.Solicitudes);
    }

    [Fact]
    public async Task Parametro_no_permitido_o_herramienta_inexistente_vuelven_como_error_al_modelo()
    {
        var cliente = new ClienteLlmFalso()
            .Responde(Respuestas.Herramienta("buscar_tickets_similares", """{"consulta":"vpn","ticketId":"otro"}"""))
            .Responde(Respuestas.Herramienta("borrar_ticket", """{"id":"TK-1001"}""", id: "toolu_2"))
            .Responde(Respuestas.Final(Respuestas.TriajeVpn()));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Completado, resultado.Estado);
        var primero = Assert.IsType<ResultadosHerramientas>(cliente.Solicitudes[1].Mensajes[2]).Resultados[0];
        Assert.True(primero.EsError);
        Assert.Contains("ticketId", primero.Contenido, StringComparison.Ordinal);
        var segundo = Assert.IsType<ResultadosHerramientas>(cliente.Solicitudes[2].Mensajes[4]).Resultados[0];
        Assert.True(segundo.EsError);
    }

    [Fact]
    public async Task Los_logs_no_contienen_el_texto_del_ticket()
    {
        var cliente = new ClienteLlmFalso()
            .Responde(Respuestas.Herramienta("buscar_tickets_similares", BuscarVpn))
            .Responde(Respuestas.Final("no es json"));
        var logger = new LoggerCapturador<CicloAgente>();
        var ticket = Fabrica.TicketVpn("Texto distintivo zxq-ticket-privado que no debe aparecer en los logs.");

        await Fabrica.Ciclo(cliente, logger: logger).TriarAsync(ticket, CancellationToken.None);

        Assert.NotEmpty(logger.Registros);
        Assert.DoesNotContain(logger.Registros, r => r.Mensaje.Contains("zxq-ticket-privado", StringComparison.Ordinal));
    }
}
