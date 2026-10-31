using EntityLibrary;

namespace ApiGestion.Models;

// HU-106: resumen de impacto de un cambio de cuota mensual (POST api/aranceles/simular, y el 409 de programar).
// Todos los números salen del backend; el portal solo los muestra.
public class TariffImpactResponseDTO
{
    public string Tipo { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string? Genero { get; set; }
    public int? IdCategoria { get; set; }
    public string? NombreCategoria { get; set; }
    public DateTime VigenteDesde { get; set; }
    public decimal? MontoBase { get; set; }
    public decimal MontoNuevo { get; set; }
    public DateTime MesDeAplicacion { get; set; }
    public DateTime? ReemplazadoDesde { get; set; }
    public List<TariffImpactLineDTO> Lineas { get; set; } = new();
    public List<TariffImpactExcludedDTO> Excluidos { get; set; } = new();
    public int Jugadores { get; set; }
    public int Categorias { get; set; }
    public decimal DiferenciaMensual { get; set; }
    public bool MesEnCurso { get; set; }
    public int CuotasDelMesPendientesSinPagos { get; set; }
    public int CuotasDelMesConPagos { get; set; }
    public DateTime? SeEmiteElDia { get; set; }
    public bool RequiereConfirmacionReforzada { get; set; }
    public string Huella { get; set; } = string.Empty;

    public static TariffImpactResponseDTO FromEntity(ImpactoArancel impacto) => new()
    {
        Tipo = TariffChangeRequestDTO.Nombre(impacto.Tipo),
        Valor = impacto.Valor,
        Genero = impacto.Genero,
        IdCategoria = impacto.IdCategoria,
        NombreCategoria = impacto.NombreCategoria,
        VigenteDesde = impacto.VigenteDesde,
        MontoBase = impacto.MontoBase,
        MontoNuevo = impacto.MontoNuevo,
        MesDeAplicacion = impacto.MesDeAplicacion,
        ReemplazadoDesde = impacto.ReemplazadoDesde,
        Lineas = impacto.Lineas.Select(l => new TariffImpactLineDTO
        {
            IdCategoria = l.IdCategoria,
            NombreCategoria = l.NombreCategoria,
            Genero = l.Genero,
            MontoActual = l.MontoActual,
            MontoNuevo = l.MontoNuevo,
            Jugadores = l.Jugadores,
            DiferenciaPorJugador = l.DiferenciaPorJugador,
            DiferenciaTotal = l.DiferenciaTotal
        }).ToList(),
        Excluidos = impacto.Excluidos.Select(e => new TariffImpactExcludedDTO
        {
            IdCategoria = e.IdCategoria,
            NombreCategoria = e.NombreCategoria,
            Jugadores = e.Jugadores
        }).ToList(),
        Jugadores = impacto.Jugadores,
        Categorias = impacto.Categorias,
        DiferenciaMensual = impacto.DiferenciaMensual,
        MesEnCurso = impacto.MesEnCurso,
        CuotasDelMesPendientesSinPagos = impacto.CuotasDelMesPendientesSinPagos,
        CuotasDelMesConPagos = impacto.CuotasDelMesConPagos,
        SeEmiteElDia = impacto.SeEmiteElDia,
        RequiereConfirmacionReforzada = impacto.RequiereConfirmacionReforzada,
        Huella = impacto.Huella
    };
}

public class TariffImpactLineDTO
{
    public int? IdCategoria { get; set; }
    public string? NombreCategoria { get; set; }
    public string Genero { get; set; } = string.Empty;
    public decimal? MontoActual { get; set; }
    public decimal MontoNuevo { get; set; }
    public int Jugadores { get; set; }
    public decimal DiferenciaPorJugador { get; set; }
    public decimal DiferenciaTotal { get; set; }
}

public class TariffImpactExcludedDTO
{
    public int? IdCategoria { get; set; }
    public string? NombreCategoria { get; set; }
    public int Jugadores { get; set; }
}
