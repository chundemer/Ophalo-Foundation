using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks <see cref="ActualWorkLineFinancialResolution"/> (ADR-493 / build-log/129, build-log/135
/// §4 Batch 1): Create validation, reason trim + 2,000-char cap, empty-GUID guards, and retained
/// audit authorship. Snapshot/review-state rules are enforced later (Batch 3a-ii), not here.
/// </summary>
public class ActualWorkLineFinancialResolutionTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid ActualWorkId = Guid.CreateVersion7();
    static readonly Guid ActualWorkLineId = Guid.CreateVersion7();
    static readonly Guid Resolver = Guid.CreateVersion7();
    static readonly DateTime ResolvedAtUtc = DateTime.UtcNow;

    static Result<ActualWorkLineFinancialResolution> Resolution(
        decimal? sellPrice = 120m,
        decimal? directCost = 70m,
        FinancialResolutionBasis basis = FinancialResolutionBasis.SupplierReceipt,
        string reason = "Vendor receipt attached") =>
        ActualWorkLineFinancialResolution.Create(
            AccountId, ActualWorkId, ActualWorkLineId, sellPrice, directCost, basis, reason, Resolver, ResolvedAtUtc);

    [Fact]
    public void Create_with_valid_fields_succeeds()
    {
        var result = Resolution(reason: "  Vendor receipt attached  ");

        Assert.True(result.IsSuccess);
        var row = result.Value;
        Assert.Equal(AccountId, row.AccountId);
        Assert.Equal(ActualWorkId, row.ActualWorkId);
        Assert.Equal(ActualWorkLineId, row.ActualWorkLineId);
        Assert.Equal(120m, row.ResolvedUnitSellPrice);
        Assert.Equal(70m, row.ResolvedUnitStandardExpectedDirectCost);
        Assert.Equal(FinancialResolutionBasis.SupplierReceipt, row.Basis);
        Assert.Equal("Vendor receipt attached", row.Reason);
        Assert.Equal(Resolver, row.ResolvedByAccountUserId);
        Assert.Equal(ResolvedAtUtc, row.ResolvedAtUtc);
    }

    [Fact]
    public void Create_retains_resolver_as_audit_author()
    {
        var row = Resolution().Value;

        Assert.Equal(Resolver, row.CreatedByUserId);
    }

    [Fact]
    public void Create_allows_a_single_component()
    {
        Assert.True(Resolution(sellPrice: 120m, directCost: null).IsSuccess);
        Assert.True(Resolution(sellPrice: null, directCost: 70m).IsSuccess);
    }

    [Fact]
    public void Create_allows_zero_valued_component()
    {
        Assert.True(Resolution(sellPrice: 0m, directCost: null).IsSuccess);
    }

    [Fact]
    public void Create_with_no_component_fails()
    {
        var result = Resolution(sellPrice: null, directCost: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.FinancialResolutionValueRequired, result.Error);
    }

    [Fact]
    public void Create_with_negative_sell_price_fails()
    {
        var result = Resolution(sellPrice: -1m, directCost: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.FinancialResolutionValueNegative, result.Error);
    }

    [Fact]
    public void Create_with_negative_direct_cost_fails()
    {
        var result = Resolution(sellPrice: null, directCost: -0.01m);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.FinancialResolutionValueNegative, result.Error);
    }

    [Fact]
    public void Create_with_undefined_basis_fails()
    {
        var result = Resolution(basis: (FinancialResolutionBasis)999);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.FinancialResolutionInvalidBasis, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_reason_fails(string reason)
    {
        var result = Resolution(reason: reason);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.FinancialResolutionReasonRequired, result.Error);
    }

    [Fact]
    public void Create_with_reason_over_2000_chars_fails()
    {
        var result = Resolution(reason: new string('x', 2001));

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.FinancialResolutionReasonTooLong, result.Error);
    }

    [Fact]
    public void Create_trims_reason_before_length_check()
    {
        var result = Resolution(reason: "  " + new string('x', 2000) + "  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(2000, result.Value.Reason.Length);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Create_with_empty_required_guid_throws(bool acct, bool visit, bool line, bool resolver)
    {
        Assert.Throws<ArgumentException>(() =>
            ActualWorkLineFinancialResolution.Create(
                acct ? Guid.Empty : AccountId,
                visit ? Guid.Empty : ActualWorkId,
                line ? Guid.Empty : ActualWorkLineId,
                120m,
                70m,
                FinancialResolutionBasis.SupplierReceipt,
                "Vendor receipt attached",
                resolver ? Guid.Empty : Resolver,
                ResolvedAtUtc));
    }
}
