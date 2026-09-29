using EntityLibrary;

namespace DaoLibrary;

public interface IAuditDao
{
    (IReadOnlyList<AuditEvent> Items, long Total) Search(
        DateTime? fromUtc,
        DateTime? toUtc,
        int? userId,
        string? userEmail,
        string? entity,
        string? action,
        int page,
        int pageSize);
}
