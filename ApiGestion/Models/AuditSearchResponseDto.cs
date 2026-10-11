namespace ApiGestion.Models;

public sealed class AuditSearchResponseDTO
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long Total { get; init; }
    public List<AuditEventDTO> Items { get; init; } = new();
}
