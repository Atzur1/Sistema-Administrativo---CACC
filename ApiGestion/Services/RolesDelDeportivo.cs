namespace ApiGestion.Services;

// Roles del Portal Deportivo (tabla ROLES, ids fijos) que viajan en el token compartido como ClaimTypes.Role.
// El Administrador del Administrativo (rol_portal 2, "2") NO figura acá a propósito: trabaja solo en el Portal
// Administrativo y no consulta el estado deportivo.
public static class RolesDelDeportivo
{
    public const string SuperAdminAdministrativo = "1";
    public const string Administrador = "Administrador (Tesorero)";
    public const string DonQr = "Don QR";
    public const string Coordinador = "Coordinador";
    public const string Medico = "Médico";
    public const string DirectorTecnico = "Director Técnico";
    public const string PreparadorFisico = "Preparador Físico";

    // Quién puede consultar el estado de deuda de un jugador.
    public const string QuienConsulta = SuperAdminAdministrativo + "," + Administrador + "," + DonQr + "," + Coordinador + ","
        + Medico + "," + DirectorTecnico + "," + PreparadorFisico;

    // Ven a cualquier jugador. El Director Técnico y el Preparador Físico ven solo a los de sus categorías.
    public static readonly string[] VenATodos = { SuperAdminAdministrativo, Administrador, DonQr, Coordinador, Medico };
}
