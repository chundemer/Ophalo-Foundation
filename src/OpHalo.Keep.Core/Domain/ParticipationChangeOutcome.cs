using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// Wraps the result of a KeepRequestParticipationService operation.
/// IsNoOp is true when the action was a safe idempotent no-op and no event was produced.
/// NewParticipants contains rows created during this change that the caller must persist.
/// Existing participant rows may have been mutated in place by the domain operation.
/// </summary>
public sealed record ParticipationChangeOutcome
{
    private ParticipationChangeOutcome() { }

    public bool IsNoOp => Event is null;
    public KeepRequestEvent? Event { get; private init; }
    public IReadOnlyList<KeepRequestParticipant> NewParticipants { get; private init; } = [];

    public static readonly ParticipationChangeOutcome NoOp = new();

    public static ParticipationChangeOutcome WithEvent(
        KeepRequestEvent @event,
        IReadOnlyList<KeepRequestParticipant>? newParticipants = null)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return new ParticipationChangeOutcome
        {
            Event = @event,
            NewParticipants = newParticipants ?? []
        };
    }
}
