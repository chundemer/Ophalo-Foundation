using OpHalo.Keep.Application.Notifications;

namespace OpHalo.UnitTests.Keep;

public class KeepPushDisplayMapperTests
{
    [Theory]
    [InlineData(KeepPushEventKind.CallRequested)]
    [InlineData(KeepPushEventKind.CancellationRequested)]
    [InlineData(KeepPushEventKind.TimingChangeRequested)]
    [InlineData(KeepPushEventKind.Assignment)]
    [InlineData(KeepPushEventKind.CustomerMessage)]
    [InlineData(KeepPushEventKind.UnresolvedFeedback)]
    public void GetDisplay_AllKinds_ReturnNonEmptyTitleAndBody(KeepPushEventKind kind)
    {
        var display = KeepPushDisplayMapper.GetDisplay(kind);

        Assert.NotEmpty(display.Title);
        Assert.NotEmpty(display.Body);
    }

    [Theory]
    [InlineData(KeepPushEventKind.CallRequested)]
    [InlineData(KeepPushEventKind.CancellationRequested)]
    [InlineData(KeepPushEventKind.TimingChangeRequested)]
    [InlineData(KeepPushEventKind.Assignment)]
    [InlineData(KeepPushEventKind.CustomerMessage)]
    [InlineData(KeepPushEventKind.UnresolvedFeedback)]
    public void ToPayloadString_AllKinds_ReturnSnakeCaseNonEmpty(KeepPushEventKind kind)
    {
        var s = KeepPushDisplayMapper.ToPayloadString(kind);

        Assert.NotEmpty(s);
        Assert.DoesNotContain(" ", s);
        Assert.Equal(s, s.ToLowerInvariant());
    }

    [Theory]
    [InlineData(KeepPushEventKind.CallRequested)]
    [InlineData(KeepPushEventKind.CancellationRequested)]
    [InlineData(KeepPushEventKind.TimingChangeRequested)]
    [InlineData(KeepPushEventKind.Assignment)]
    [InlineData(KeepPushEventKind.CustomerMessage)]
    [InlineData(KeepPushEventKind.UnresolvedFeedback)]
    public void GetDisplay_ForbiddenDataAbsent(KeepPushEventKind kind)
    {
        var display = KeepPushDisplayMapper.GetDisplay(kind);
        var combined = display.Title + display.Body;

        // No dynamic customer/request data in static strings (ADR-358).
        Assert.DoesNotContain("{", combined);
        Assert.DoesNotContain("}", combined);
    }

    [Fact]
    public void GetDisplay_UnknownKind_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KeepPushDisplayMapper.GetDisplay((KeepPushEventKind)999));
    }

    [Fact]
    public void AllDefinedKinds_HaveMappings()
    {
        var allKinds = Enum.GetValues<KeepPushEventKind>();

        foreach (var kind in allKinds)
        {
            var display = KeepPushDisplayMapper.GetDisplay(kind);
            Assert.NotEmpty(display.Title);
            Assert.NotEmpty(display.Body);
            Assert.NotEmpty(KeepPushDisplayMapper.ToPayloadString(kind));
        }
    }
}
