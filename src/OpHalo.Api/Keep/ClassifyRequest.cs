// Confirmation of the classify action is enforced in the UI (confirmation dialog before submit).
// The backend treats any authenticated, authorized POST as intentional.
public sealed record ClassifyRequestBody(string? TargetStatus, string? Reason);
