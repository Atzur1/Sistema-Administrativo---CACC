namespace ApiGestion.Tests;

using System.ComponentModel.DataAnnotations;
using ApiGestion.Models;

// Automatic validations of the inbound benefit contract (HU-011). ASP.NET runs
// these same rules before the controller, answering 400, so every case here is a
// request that never reaches the database.
public class DiscountRequestDTOTests
{
    private static DiscountRequestDTO PercentageRequest()
    {
        return new DiscountRequestDTO
        {
            Reason = "Media Beca",
            ValueType = "%",
            Percentage = 50m,
            FixedAmount = null,
            StartDate = "2026-10-01",
            EndDate = "2026-12-31"
        };
    }

    private static DiscountRequestDTO FixedAmountRequest()
    {
        return new DiscountRequestDTO
        {
            Reason = "Descuento por Hermanos",
            ValueType = "$",
            Percentage = null,
            FixedAmount = 15000m,
            StartDate = "2026-10-01",
            EndDate = "2026-12-31"
        };
    }

    private static List<string> InvalidMembers(DiscountRequestDTO request)
    {
        List<ValidationResult> results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return results.SelectMany(result => result.MemberNames).ToList();
    }

    [Fact]
    public void PercentageRequest_IsValid()
    {
        Assert.Empty(InvalidMembers(PercentageRequest()));
    }

    [Fact]
    public void FixedAmountRequest_IsValid()
    {
        Assert.Empty(InvalidMembers(FixedAmountRequest()));
    }

    // Criterio 2: el motivo es obligatorio
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Reason_Missing_IsInvalid(string? reason)
    {
        DiscountRequestDTO request = PercentageRequest();
        request.Reason = reason!;

        Assert.Contains(nameof(DiscountRequestDTO.Reason), InvalidMembers(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("porcentaje")]
    [InlineData("EUR")]
    public void ValueType_NotAllowed_IsInvalid(string valueType)
    {
        DiscountRequestDTO request = PercentageRequest();
        request.ValueType = valueType;

        Assert.Contains(nameof(DiscountRequestDTO.ValueType), InvalidMembers(request));
    }

    // Caso 5 y 6 de las pruebas mínimas: 0 y más de 100 quedan fuera de rango
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(101)]
    [InlineData(1000)]
    public void Percentage_OutOfRange_IsInvalid(decimal percentage)
    {
        DiscountRequestDTO request = PercentageRequest();
        request.Percentage = percentage;

        Assert.Contains(nameof(DiscountRequestDTO.Percentage), InvalidMembers(request));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(33.33)]
    [InlineData(50)]
    [InlineData(100)]
    public void Percentage_InsideRange_IsValid(decimal percentage)
    {
        DiscountRequestDTO request = PercentageRequest();
        request.Percentage = percentage;

        Assert.Empty(InvalidMembers(request));
    }

    [Fact]
    public void Percentage_Missing_OnPercentageBenefit_IsInvalid()
    {
        DiscountRequestDTO request = PercentageRequest();
        request.Percentage = null;

        Assert.Contains(nameof(DiscountRequestDTO.Percentage), InvalidMembers(request));
    }

    // Caso 7: monto fijo en cero o negativo
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-15000)]
    public void FixedAmount_NotPositive_IsInvalid(decimal amount)
    {
        DiscountRequestDTO request = FixedAmountRequest();
        request.FixedAmount = amount;

        Assert.Contains(nameof(DiscountRequestDTO.FixedAmount), InvalidMembers(request));
    }

    [Fact]
    public void FixedAmount_Missing_OnFixedAmountBenefit_IsInvalid()
    {
        DiscountRequestDTO request = FixedAmountRequest();
        request.FixedAmount = null;

        Assert.Contains(nameof(DiscountRequestDTO.FixedAmount), InvalidMembers(request));
    }

    // Criterio 4: porcentual y monto fijo no se mezclan en una misma bonificación
    [Fact]
    public void PercentageBenefit_CarryingFixedAmount_IsInvalid()
    {
        DiscountRequestDTO request = PercentageRequest();
        request.FixedAmount = 15000m;

        Assert.Contains(nameof(DiscountRequestDTO.FixedAmount), InvalidMembers(request));
    }

    [Fact]
    public void FixedAmountBenefit_CarryingPercentage_IsInvalid()
    {
        DiscountRequestDTO request = FixedAmountRequest();
        request.Percentage = 50m;

        Assert.Contains(nameof(DiscountRequestDTO.Percentage), InvalidMembers(request));
    }

    // ===== HU-012: vigencia obligatoria =====

    // Hasta HU-011 una bonificación podía otorgarse sin fecha de corte y corría
    // hasta que alguien la diera de baja. HU-012 lo prohíbe: todo beneficio
    // opera dentro de un período autorizado.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StartDate_Missing_IsInvalid(string? startDate)
    {
        DiscountRequestDTO request = PercentageRequest();
        request.StartDate = startDate;

        Assert.Contains(nameof(DiscountRequestDTO.StartDate), InvalidMembers(request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EndDate_Missing_IsInvalid(string? endDate)
    {
        DiscountRequestDTO request = PercentageRequest();
        request.EndDate = endDate;

        Assert.Contains(nameof(DiscountRequestDTO.EndDate), InvalidMembers(request));
    }

    [Theory]
    [InlineData("15/09/2026")]
    [InlineData("2026-13-01")]
    [InlineData("ayer")]
    public void StartDate_Malformed_IsInvalid(string startDate)
    {
        DiscountRequestDTO request = PercentageRequest();
        request.StartDate = startDate;

        Assert.Contains(nameof(DiscountRequestDTO.StartDate), InvalidMembers(request));
    }

    [Theory]
    [InlineData("31/12/2026")]
    [InlineData("2026-02-30")]
    [InlineData("nunca")]
    public void EndDate_Malformed_IsInvalid(string endDate)
    {
        DiscountRequestDTO request = PercentageRequest();
        request.EndDate = endDate;

        Assert.Contains(nameof(DiscountRequestDTO.EndDate), InvalidMembers(request));
    }

    // El ejemplo explícito de la historia: 10/10 a 09/10 no se acepta
    [Fact]
    public void EndDate_BeforeStartDate_IsInvalid()
    {
        DiscountRequestDTO request = PercentageRequest();
        request.StartDate = "2026-10-10";
        request.EndDate = "2026-10-09";

        Assert.Contains(nameof(DiscountRequestDTO.EndDate), InvalidMembers(request));
    }

    // El otro ejemplo de la historia: 10/10 a 10/10 tampoco. La fecha de fin
    // tiene que ser ESTRICTAMENTE posterior, no "posterior o igual".
    [Fact]
    public void EndDate_SameAsStartDate_IsInvalid()
    {
        DiscountRequestDTO request = PercentageRequest();
        request.StartDate = "2026-10-10";
        request.EndDate = "2026-10-10";

        Assert.Contains(nameof(DiscountRequestDTO.EndDate), InvalidMembers(request));
    }

    [Fact]
    public void EndDate_OneDayAfterStartDate_IsValid()
    {
        DiscountRequestDTO request = PercentageRequest();
        request.StartDate = "2026-10-10";
        request.EndDate = "2026-10-11";

        Assert.Empty(InvalidMembers(request));
    }

    // Una vigencia que arranca en el futuro es válida: queda Programada
    [Fact]
    public void Range_EntirelyInTheFuture_IsValid()
    {
        DiscountRequestDTO request = PercentageRequest();
        request.StartDate = "2030-01-01";
        request.EndDate = "2030-12-31";

        Assert.Empty(InvalidMembers(request));
    }

    // Y una enteramente pasada también: sirve para cargar un beneficio histórico
    [Fact]
    public void Range_EntirelyInThePast_IsValid()
    {
        DiscountRequestDTO request = PercentageRequest();
        request.StartDate = "2020-01-01";
        request.EndDate = "2020-12-31";

        Assert.Empty(InvalidMembers(request));
    }
}
