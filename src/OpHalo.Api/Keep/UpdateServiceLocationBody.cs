namespace OpHalo.Api.Keep;

public sealed record UpdateServiceLocationBody(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string? Zip);
