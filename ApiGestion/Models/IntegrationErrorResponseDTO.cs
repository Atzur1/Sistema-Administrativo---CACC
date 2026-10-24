using System.Text.Json.Serialization;

namespace ApiGestion.Models;

// Contrato de error de la API de integración (HU-058): { errorCode, message, field? }.
// exito y mensaje se mantienen porque el Portal Deportivo ya los lee en la v1: quitarlos rompería el contrato
// (ver docs/integracion/README.md). mensaje siempre es igual a message.
public sealed class IntegrationErrorResponseDTO
{
    [JsonPropertyName("errorCode")]
    public required string ErrorCode { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    // Solo en los 400: el dato de entrada que no es válido.
    [JsonPropertyName("field")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Field { get; init; }

    [JsonPropertyName("exito")]
    public bool Exito => false;

    [JsonPropertyName("mensaje")]
    public string Mensaje => Message;
}
