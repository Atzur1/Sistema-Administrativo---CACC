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
}
