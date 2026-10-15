namespace EntityLibrary;

public sealed class AuditEvent
{
    public long Id { get; init; }
    public int? UserId { get; init; }
    public string? UserEmail { get; init; }
    public string? UserName { get; init; }
    public string? UserSurname { get; init; }
    public string? UserDni { get; init; }
    public string Entity { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; }
    public string? BeforeJson { get; init; }
    public string? AfterJson { get; init; }

    // Los nombres de lo que el evento menciona solo por número (el jugador 3, la división 5...), listos para
    // mostrar. La clave es "tipo:número" (ver DaoLibrary.AuditReferences), por ejemplo "jugador:3" ->
    // "PRUEBA, NICOLAS (DNI 99000001)". Lo que ya no existe en la base no aparece: quien lo muestre cae al número.
    public IReadOnlyDictionary<string, string> Referencias { get; set; } = new Dictionary<string, string>();
}
