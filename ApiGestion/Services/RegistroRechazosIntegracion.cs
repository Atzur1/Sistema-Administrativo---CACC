using DaoLibrary;
using ServiceLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ApiGestion.Services;

public sealed class RegistroRechazosIntegracion : IRegistroRechazos
{
    private readonly IIntegracionRechazosDao _dao;
    private readonly ILogger<RegistroRechazosIntegracion> _logger;

    public RegistroRechazosIntegracion(IIntegracionRechazosDao dao, ILogger<RegistroRechazosIntegracion> logger)
    {
        _dao = dao;
        _logger = logger;
    }

    public void Registrar(string operacion, TipoResultado tipo, string motivo, string? correlacion, int? idPersona = null, int? idJugador = null)
    {
        string errorCode = tipo switch
        {
            TipoResultado.Conflicto => IntegrationErrorCodes.Conflict,
            TipoResultado.NoEncontrado => IntegrationErrorCodes.NotFound,
            TipoResultado.NoProcesable => IntegrationErrorCodes.Unprocessable,
            _ => IntegrationErrorCodes.InvalidInput
        };

        try
        {
            _dao.Registrar(new RechazoIntegracion(operacion, errorCode, motivo, correlacion, idPersona, idJugador));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("No se pudo guardar un rechazo de integración {Operacion} ({ErrorCode}); tipo de error: {TipoError}.",
                operacion, errorCode, ex.GetType().Name);
        }
    }

    public static string? OperacionDe(HttpContext context)
    {
        string? route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        if (context.Request.Method == HttpMethods.Post &&
            string.Equals(route, "api/integracion/v1/jugadores", StringComparison.OrdinalIgnoreCase))
        {
            return OperacionesDeIntegracion.AltaJugador;
        }
        if (context.Request.Method == HttpMethods.Put &&
            string.Equals(route, "api/integracion/v1/jugadores/{idJugador:int}/categoria", StringComparison.OrdinalIgnoreCase))
        {
            return OperacionesDeIntegracion.CambioCategoria;
        }
        return null;
    }

    public void RegistrarModeloInvalido(string operacion, string motivo, string? correlacion, int? idJugador = null)
        => Registrar(operacion, TipoResultado.Invalido, motivo, correlacion, idJugador: idJugador);
}
