using OpHalo.SharedKernel.Results;
using Xunit;

namespace OpHalo.UnitTests.SharedKernel;

/// <summary>
/// Behavior tests for the Result/Error primitives ported into SharedKernel in
/// Phase 3. These lock the proven invariants from the reference implementation.
/// </summary>
public class ResultTests
{
    static readonly Error SampleError = Error.Create("sample.code", "Sample message");

    [Fact]
    public void Success_is_successful_and_carries_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_is_not_successful_and_carries_the_error()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Failure_with_Error_None_throws()
        => Assert.Throws<InvalidOperationException>(() => Result.Failure(Error.None));

    [Fact]
    public void Generic_success_exposes_its_value()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Generic_failure_throws_on_value_access()
    {
        var result = Result<int>.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Success_with_null_value_throws()
        => Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));

    [Fact]
    public void Map_transforms_a_success_and_passes_through_a_failure()
    {
        Assert.Equal(10, Result<int>.Success(5).Map(x => x * 2).Value);
        Assert.True(Result<int>.Failure(SampleError).Map(x => x * 2).IsFailure);
    }

    [Fact]
    public void Bind_chains_success_and_short_circuits_on_failure()
    {
        var chained = Result<int>.Success(5).Bind(x => Result<string>.Success($"v{x}"));
        Assert.Equal("v5", chained.Value);

        var shortCircuited = Result<int>.Failure(SampleError).Bind(x => Result<string>.Success($"v{x}"));
        Assert.True(shortCircuited.IsFailure);
        Assert.Equal(SampleError, shortCircuited.Error);
    }

    [Fact]
    public void Match_selects_the_correct_branch()
    {
        Assert.Equal("ok", Result.Success().Match(() => "ok", _ => "err"));
        Assert.Equal("err", Result.Failure(SampleError).Match(() => "ok", _ => "err"));
        Assert.Equal(7, Result<int>.Success(7).Match(v => v, _ => -1));
    }

    [Fact]
    public void TryGetValue_reflects_success_state()
    {
        Assert.True(Result<int>.Success(3).TryGetValue(out var ok));
        Assert.Equal(3, ok);

        Assert.False(Result<int>.Failure(SampleError).TryGetValue(out var bad));
        Assert.Equal(default, bad);
    }
}
