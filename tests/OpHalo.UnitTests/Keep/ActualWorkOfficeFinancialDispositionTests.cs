using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks <see cref="ActualWorkOfficeFinancialDisposition"/> (ADR-493 / build-log/129,
/// build-log/135 §4 Batch 1): visit-level Create validation, reason trim + 2,000-char cap,
/// empty-GUID guards, and retained audit authorship. The zero-line-only / lined-visit rejection is
/// enforced later against the loaded visit (Batch 3b-i), not here.
/// </summary>
public class ActualWorkOfficeFinancialDispositionTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid ActualWorkId = Guid.CreateVersion7();
    static readonly Guid Disposer = Guid.CreateVersion7();
    static readonly DateTime DisposedAtUtc = DateTime.UtcNow;

    static Result<ActualWorkOfficeFinancialDisposition> Disposition(
        OfficeFinancialDispositionKind kind = OfficeFinancialDispositionKind.NoCharge,
        string reason = "Warranty visit, no billable work") =>
        ActualWorkOfficeFinancialDisposition.Create(
            AccountId, ActualWorkId, kind, reason, Disposer, DisposedAtUtc);

    [Fact]
    public void Create_with_valid_fields_succeeds()
    {
        var result = Disposition(reason: "  Warranty visit, no billable work  ");

        Assert.True(result.IsSuccess);
        var row = result.Value;
        Assert.Equal(AccountId, row.AccountId);
        Assert.Equal(ActualWorkId, row.ActualWorkId);
        Assert.Equal(OfficeFinancialDispositionKind.NoCharge, row.Kind);
        Assert.Equal("Warranty visit, no billable work", row.Reason);
        Assert.Equal(Disposer, row.DisposedByAccountUserId);
        Assert.Equal(DisposedAtUtc, row.DisposedAtUtc);
    }

    [Fact]
    public void Create_retains_disposer_as_audit_author()
    {
        var row = Disposition().Value;

        Assert.Equal(Disposer, row.CreatedByUserId);
    }

    [Fact]
    public void Create_with_undefined_kind_fails()
    {
        var result = Disposition(kind: (OfficeFinancialDispositionKind)999);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.DispositionInvalidKind, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_reason_fails(string reason)
    {
        var result = Disposition(reason: reason);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.DispositionReasonRequired, result.Error);
    }

    [Fact]
    public void Create_with_reason_over_2000_chars_fails()
    {
        var result = Disposition(reason: new string('x', 2001));

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkFinancialResolutionErrors.DispositionReasonTooLong, result.Error);
    }

    [Fact]
    public void Create_trims_reason_before_length_check()
    {
        var result = Disposition(reason: "  " + new string('x', 2000) + "  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(2000, result.Value.Reason.Length);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Create_with_empty_required_guid_throws(bool acct, bool visit, bool disposer)
    {
        Assert.Throws<ArgumentException>(() =>
            ActualWorkOfficeFinancialDisposition.Create(
                acct ? Guid.Empty : AccountId,
                visit ? Guid.Empty : ActualWorkId,
                OfficeFinancialDispositionKind.NoCharge,
                "Warranty visit, no billable work",
                disposer ? Guid.Empty : Disposer,
                DisposedAtUtc));
    }
}
