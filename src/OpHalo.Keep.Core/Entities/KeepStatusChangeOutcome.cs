namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Wraps the result of KeepRequest.ChangeStatus so Result&lt;T&gt; always receives a
/// non-null value (Result&lt;T&gt;.Success guards against null regardless of T nullability).
/// IsNoOp is true when the call was a same-status/no-message no-op and no event was produced.
/// </summary>
public sealed record KeepStatusChangeOutcome(KeepRequestEvent? StatusChangedEvent)
{
    public bool IsNoOp => StatusChangedEvent is null;

    public static readonly KeepStatusChangeOutcome NoOp = new(StatusChangedEvent: null);

    public static KeepStatusChangeOutcome WithEvent(KeepRequestEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new KeepStatusChangeOutcome(e);
    }
}
