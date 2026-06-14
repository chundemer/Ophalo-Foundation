namespace OpHalo.Foundation.Core.Helpers;

/// <summary>
/// Normalizes email addresses for consistent storage and lookup.
/// All write paths and all lookup paths must use this helper.
/// Never normalize inline in handlers — always call this.
/// </summary>
/// <remarks>
/// Lives in Foundation.Core, not SharedKernel: email is a business concept and the
/// SharedKernel must stay free of business concepts (build plan §8).
/// </remarks>
public static class EmailNormalizer
{
    public static string Normalize(string email) =>
        email.Trim().ToLowerInvariant();
}
