using Centinela.Agente.Puertos;

namespace Centinela.Agente.Aplicacion;

/// <summary>
/// Estimación conservadora del tamaño de entrada de una petición, para no enviar una que ya no entra en
/// el presupuesto del ticket (A16). Cuenta unos 3 caracteres por token: sobreestima un poco a propósito.
/// </summary>
public static class EstimadorTokens
{
    private const int CaracteresPorToken = 3;

    public static int Entrada(SolicitudLlm solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        long caracteres = solicitud.Instrucciones.Length + solicitud.EsquemaSalida.ToJsonString().Length;
        caracteres += solicitud.Herramientas.Sum(h => (long)h.Nombre.Length + h.Descripcion.Length + h.EsquemaEntrada.ToJsonString().Length);
        caracteres += solicitud.Mensajes.Sum(m => m switch
        {
            MensajeUsuario u => (long)u.Texto.Length,
            MensajeAsistente a => a.ContenidoProveedor.ToString()?.Length ?? 0,
            ResultadosHerramientas r => r.Resultados.Sum(x => (long)x.Contenido.Length),
            _ => 0,
        });
        return (int)Math.Min(int.MaxValue, caracteres / CaracteresPorToken);
    }
}
