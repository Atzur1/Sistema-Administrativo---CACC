using EntityLibrary;

namespace ApiGestion.Models;

public sealed class AuditSearchResponseDto
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long Total { get; init; }
    public IReadOnlyList<AuditEvent> Items { get; init; } = Array.Empty<AuditEvent>();
}
