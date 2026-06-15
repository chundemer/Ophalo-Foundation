namespace OpHalo.Keep.Application.Requests;

public sealed record GetKeepRequestListResult(IReadOnlyList<KeepRequestSummary> Requests);
