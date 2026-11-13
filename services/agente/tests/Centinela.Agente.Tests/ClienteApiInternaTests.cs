using System.Net;
using System.Text;
using Centinela.Agente.Infraestructura.ApiInterna;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Tests;

/// <summary>El cliente de la API interna: solo GET, con la cuenta de servicio, y cualquier falla es "no disponible".</summary>
public class ClienteApiInternaTests
{
    private sealed class ServidorFalso(HttpStatusCode estado, string cuerpo) : HttpMessageHandler
    {
        public HttpRequestMessage? Recibida { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recibida = request;
            return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") });
        }
    }

    private static ClienteApiInterna Cliente(ServidorFalso servidor) => new(new HttpClient(servidor), Options.Create(new OpcionesApiInterna
    {
        UrlBase = new Uri("http://mesa-servicio:8080/"),
        Usuario = "agente",
        Password = "clave",
    }));

    [Fact]
    public async Task PideSimilaresDelTicketEnCursoConLaCuentaDeServicio()
    {
        var servidor = new ServidorFalso(HttpStatusCode.OK,
            """[{"id":"6f1c0d55-0000-0000-0000-000000000001","titulo":"VPN no conecta","estado":"resuelto","sistema":"vpn","solucion":"Se renovó la contraseña.","similitud":0.81,"metodo":"significado"}]""");
        var enCurso = Guid.NewGuid();

        var similares = await Cliente(servidor).BuscarTicketsSimilaresAsync(enCurso, "vpn no conecta & más", 5, CancellationToken.None);

        var t = Assert.Single(similares);
        Assert.Equal("Se renovó la contraseña.", t.Solucion);
        var peticion = servidor.Recibida!;
        Assert.Equal(HttpMethod.Get, peticion.Method);
        Assert.Equal($"/interna/tickets/{enCurso}/similares", peticion.RequestUri!.AbsolutePath);
        Assert.Contains("consulta=vpn%20no%20conecta%20%26%20m%C3%A1s", peticion.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("Basic", peticion.Headers.Authorization!.Scheme);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "[]")]
    [InlineData(HttpStatusCode.InternalServerError, "[]")]
    [InlineData(HttpStatusCode.OK, "esto no es JSON")]
    public async Task CualquierFallaEsConsultaNoDisponible(HttpStatusCode estado, string cuerpo)
    {
        var cliente = Cliente(new ServidorFalso(estado, cuerpo));

        await Assert.ThrowsAsync<ConsultaNoDisponibleException>(() => cliente.BuscarArticulosAsync("vpn", 5, CancellationToken.None));
    }

    [Fact]
    public async Task ElSistemaVaEscapadoEnLaRuta()
    {
        var servidor = new ServidorFalso(HttpStatusCode.OK, "[]");

        await Cliente(servidor).IncidentesAbiertosAsync("../admin", 5, CancellationToken.None);

        Assert.DoesNotContain("/admin", servidor.Recibida!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }
}
