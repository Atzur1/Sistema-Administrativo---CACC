using EntityLibrary;

namespace ApiGestion.Models;

// One row of the audit log (GET api/auditoria/cambios): who changed what, when, and the before/after snapshot.
public class AuditEventDTO
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserName { get; set; }
    public string? UserSurname { get; set; }
    public string? UserDni { get; set; }
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTime TimestampUtc { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    // The names behind the numbers the snapshots carry, as "kind:number" -> text (for example "jugador:3" ->
    // "PRUEBA, NICOLAS (DNI 99000001)"). What no longer exists in the database is simply not in the map.
    public Dictionary<string, string> References { get; set; } = new();

    public static AuditEventDTO FromEntity(AuditEvent auditEvent)
    {
        return new AuditEventDTO
        {
            Id = auditEvent.Id,
            UserId = auditEvent.UserId,
            UserEmail = auditEvent.UserEmail,
            UserName = auditEvent.UserName,
            UserSurname = auditEvent.UserSurname,
            UserDni = auditEvent.UserDni,
            Entity = auditEvent.Entity,
            EntityId = auditEvent.EntityId,
            Action = auditEvent.Action,
            TimestampUtc = auditEvent.TimestampUtc,
            BeforeJson = auditEvent.BeforeJson,
            AfterJson = auditEvent.AfterJson,
            References = new Dictionary<string, string>(auditEvent.Referencias)
        };
    }
}
