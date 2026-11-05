using EntityLibrary;

namespace ApiGestion.Models
{
    // HU-106: el cambio de cuota mensual que arma el form "Programar nueva cuota mensual": género O categoría (uno
    // solo), tipo, valor y vigencia. Es el body de POST api/aranceles/simular.
    //   tipo: "absoluto" (valor = monto final), "porcentaje" (valor = %) o "montoFijo" (valor = $ a sumar), HU-069.
    public class TariffChangeRequestDTO
    {
        public string? Genero { get; set; }
        public int? IdCategoria { get; set; }
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Indicá el tipo de cambio del arancel.")]
        public string Tipo { get; set; } = "absoluto";
        public decimal Valor { get; set; }
        public DateTime VigenteDesde { get; set; }

        public static readonly IReadOnlyDictionary<string, TipoCambioArancel> Tipos = new Dictionary<string, TipoCambioArancel>(StringComparer.OrdinalIgnoreCase)
        {
            ["absoluto"] = TipoCambioArancel.Absoluto,
            ["porcentaje"] = TipoCambioArancel.Porcentaje,
            ["montoFijo"] = TipoCambioArancel.MontoFijo
        };

        public static string Nombre(TipoCambioArancel tipo) => tipo switch
        {
            TipoCambioArancel.Porcentaje => "porcentaje",
            TipoCambioArancel.MontoFijo => "montoFijo",
            _ => "absoluto"
        };
    }

    // POST api/aranceles/programar: el mismo cambio más lo que el SuperAdmin aprobó en la simulación.
    public class ScheduleTariffRequestDTO : TariffChangeRequestDTO
    {
        // Huella de la simulación que se revisó.
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Revisá el impacto del cambio antes de confirmarlo.")]
        public string Huella { get; set; } = string.Empty;

        // La cantidad de jugadores escrita para confirmar (obligatoria si alcanza más de una categoría).
        public int? JugadoresConfirmados { get; set; }
    }
}
