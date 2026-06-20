namespace OpHalo.Keep.Application.IntakeSetup;

public sealed record KeepIntakeSetupStatusResult(bool HasActiveLink, string? PublicSlug, DateTime? CreatedAtUtc);

public sealed record KeepIntakeSetupEnsureResult(bool Created, string? RawToken, string? PublicSlug);

public sealed record KeepIntakeSetupReplaceResult(string RawToken, string PublicSlug, bool StaleLinksWarning);
