namespace ApiGestion.Tests;

using System.ComponentModel.DataAnnotations;
using ApiGestion.Models;

// Automatic validations of the inbound payment contract (HU-015 / HU-016).
// ASP.NET runs these same attributes before the controller, answering 400.
public class PaymentRequestDTOTests
{
    private static PaymentRequestDTO ValidRequest()
    {
        return new PaymentRequestDTO
        {
            PlayerId = 577,
            PeriodYear = 2026,
            PeriodMonth = 7,
            Amount = 85000,
            Method = "efectivo",
            Reference = null
        };
    }

    private static List<string> InvalidMembers(PaymentRequestDTO request)
    {
        List<ValidationResult> results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return results.SelectMany(result => result.MemberNames).ToList();
    }

    [Fact]
    public void ValidRequest_HasNoValidationErrors()
    {
        Assert.Empty(InvalidMembers(ValidRequest()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PlayerId_NotPositive_IsInvalid(long playerId)
    {
        PaymentRequestDTO request = ValidRequest();
        request.PlayerId = playerId;

        Assert.Contains(nameof(PaymentRequestDTO.PlayerId), InvalidMembers(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void PeriodMonth_OutOfRange_IsInvalid(int month)
    {
        PaymentRequestDTO request = ValidRequest();
        request.PeriodMonth = month;

        Assert.Contains(nameof(PaymentRequestDTO.PeriodMonth), InvalidMembers(request));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void PeriodMonth_Boundaries_AreValid(int month)
    {
        PaymentRequestDTO request = ValidRequest();
        request.PeriodMonth = month;

        Assert.DoesNotContain(nameof(PaymentRequestDTO.PeriodMonth), InvalidMembers(request));
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void PeriodYear_OutOfRange_IsInvalid(int year)
    {
        PaymentRequestDTO request = ValidRequest();
        request.PeriodYear = year;

        Assert.Contains(nameof(PaymentRequestDTO.PeriodYear), InvalidMembers(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Amount_NotPositive_IsInvalid(int amount)
    {
        PaymentRequestDTO request = ValidRequest();
        request.Amount = amount;

        Assert.Contains(nameof(PaymentRequestDTO.Amount), InvalidMembers(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Method_Missing_IsInvalid(string? method)
    {
        PaymentRequestDTO request = ValidRequest();
        request.Method = method!;

        Assert.Contains(nameof(PaymentRequestDTO.Method), InvalidMembers(request));
    }

    [Fact]
    public void Reference_LongerThanColumn_IsInvalid()
    {
        PaymentRequestDTO request = ValidRequest();
        request.Method = "transferencia";
        request.Reference = new string('9', 51);

        Assert.Contains(nameof(PaymentRequestDTO.Reference), InvalidMembers(request));
    }

    [Fact]
    public void Reference_AtColumnLength_IsValid()
    {
        PaymentRequestDTO request = ValidRequest();
        request.Method = "transferencia";
        request.Reference = new string('9', 50);

        Assert.Empty(InvalidMembers(request));
    }
}
