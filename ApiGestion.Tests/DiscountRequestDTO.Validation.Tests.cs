using System.ComponentModel.DataAnnotations;
using ApiGestion.Models;
using Xunit;

namespace ApiGestion.Tests;

// HU-090: los mensajes de validacion de las bonificaciones salen en castellano.
public class DiscountRequestDTOValidationTests
{
    private static List<ValidationResult> Validar(DiscountRequestDTO dto) =>
        dto.Validate(new ValidationContext(dto)).ToList();

    [Fact]
    public void BonificacionPorPorcentaje_SinPorcentaje_PideElPorcentajeEnCastellano()
    {
        var errores = Validar(new DiscountRequestDTO
        {
            ValueType = DiscountRequestDTO.PercentageValue,
            StartDate = "2026-01-01",
            EndDate = "2026-12-31"
        });

        Assert.Contains(errores, e => e.ErrorMessage == "Indicá el porcentaje de la bonificación.");
    }

    [Fact]
    public void BonificacionPorMontoFijo_SinMonto_PideElMontoEnCastellano()
    {
        var errores = Validar(new DiscountRequestDTO
        {
            ValueType = DiscountRequestDTO.FixedAmountValue,
            StartDate = "2026-01-01",
            EndDate = "2026-12-31"
        });

        Assert.Contains(errores, e => e.ErrorMessage == "Indicá el monto fijo de la bonificación.");
    }

    [Fact]
    public void NingunMensajeDeValidacionQuedaEnIngles()
    {
        var casos = new[]
        {
            new DiscountRequestDTO { ValueType = DiscountRequestDTO.PercentageValue },
            new DiscountRequestDTO { ValueType = DiscountRequestDTO.FixedAmountValue },
            new DiscountRequestDTO { ValueType = DiscountRequestDTO.PercentageValue, Percentage = 150, FixedAmount = 5 },
            new DiscountRequestDTO { ValueType = DiscountRequestDTO.FixedAmountValue, FixedAmount = -1, Percentage = 5 },
        };

        foreach (var caso in casos)
            foreach (var error in Validar(caso))
                Assert.DoesNotMatch(@"\b(requires|must|cannot|benefit)\b", error.ErrorMessage ?? "");
    }
}
