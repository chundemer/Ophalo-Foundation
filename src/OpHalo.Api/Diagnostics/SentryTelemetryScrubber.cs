using System.Text.RegularExpressions;
using OpHalo.Api.Helpers;
using Sentry;
using Sentry.Protocol;

namespace OpHalo.Api.Diagnostics;

/// <summary>
/// Final telemetry boundary for Sentry events (GAP-039, ADR-495 D2). Runs as the SDK
/// <c>BeforeSend</c> hook: it does not redact the incoming event in place, it builds a fresh
/// event that carries only the ADR-495 allowlist —
///
/// <list type="bullet">
///   <item>environment and release;</item>
///   <item>the server-generated correlation ID, as a tag (validated 32-hex format);</item>
///   <item>a safe HTTP method and a route with query/fragment removed and every known
///         public-token family reduced to <c>[redacted]</c> via <see cref="PublicTokenPathRedactor"/>;</item>
///   <item>HTTP status code, as a tag (validated 100-599);</item>
///   <item>exception type and stack-frame metadata for grouping; and</item>
///   <item><c>account_id</c>, only when a prior processor set it for an authenticated request
///         (validated non-empty GUID).</item>
/// </list>
///
/// Everything else — messages, exception messages/data, locals, request/response bodies,
/// query strings, headers, cookies, sessions, client IP, breadcrumbs, user identity, contexts,
/// extras, modules, server name, thread dumps, fingerprints — is never copied across, so it
/// cannot leak by omission in a future SDK version. Grouping relies on the sanitized exception
/// type and stack frames, not a fingerprint. A final residual-token check discards the whole
/// event if an unredacted capability-token route still appears in any retained string (for
/// example embedded in a stack-frame path). <c>/health/live</c> and <c>/health/ready</c> events
/// are dropped.
/// </summary>
public static class SentryTelemetryScrubber
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal)
    {
        "correlation_id",
        "account_id",
        "http.status_code",
    };

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE", "CONNECT",
    };

    private static readonly HashSet<string> HealthRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health/live",
        "/health/ready",
    };

    private static readonly Regex CorrelationIdFormat = new("^[0-9a-fA-F]{32}$", RegexOptions.Compiled);

    // Any known public-token route family that still carries a raw token after redaction.
    private static readonly Regex UnredactedTokenRoute = new(
        @"(?ix) (?: public-intake/token/ | /keep/r/ | /keep/intake-sms/ | /keep/share-sms/ | /keep/share-call/ )
          (?! \[redacted\] ) \S",
        RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    /// <summary>
    /// Returns a new event containing only allowlisted data, or <c>null</c> to discard the event.
    /// </summary>
    public static SentryEvent? Scrub(SentryEvent original)
    {
        var route = SanitizePath(original.Request?.Url);
        if (route is not null && HealthRoutes.Contains(route))
            return null;

        // A fresh event is the structural allowlist: only fields explicitly copied below can
        // survive. The SDK-assigned EventId, Timestamp and Sdk metadata carry no request data.
        var safe = new SentryEvent
        {
            Platform = original.Platform,
            Level = original.Level,
            Release = original.Release,
            Environment = original.Environment,
            SentryExceptions = original.SentryExceptions?.Select(SanitizeException).ToList(),
        };

        if (route is not null)
        {
            safe.Request = new SentryRequest
            {
                Url = route,
                Method = SafeMethod(original.Request?.Method),
            };
        }

        foreach (var tag in original.Tags)
        {
            if (!AllowedTags.Contains(tag.Key))
                continue;
            var value = SanitizeTagValue(tag.Key, tag.Value);
            if (value is not null)
                safe.SetTag(tag.Key, value);
        }

        return IsProvablySafe(safe) ? safe : null;
    }

    private static SentryException SanitizeException(SentryException source) => new()
    {
        Type = source.Type,
        Module = source.Module,
        ThreadId = source.ThreadId,
        Value = null, // exception messages may embed customer text — never retained
        Mechanism = source.Mechanism is { } mechanism
            ? new Mechanism
            {
                Type = mechanism.Type,
                Handled = mechanism.Handled,
                Synthetic = mechanism.Synthetic,
                IsExceptionGroup = mechanism.IsExceptionGroup,
                ExceptionId = mechanism.ExceptionId,
                ParentId = mechanism.ParentId,
                // Description/HelpLink/Data/Meta are not copied.
            }
            : null,
        Stacktrace = SanitizeStacktrace(source.Stacktrace),
    };

    private static SentryStackTrace? SanitizeStacktrace(SentryStackTrace? source)
    {
        if (source?.Frames is not { Count: > 0 } frames)
            return null;

        var sanitized = new SentryStackTrace { AddressAdjustment = source.AddressAdjustment };
        foreach (var frame in frames)
        {
            sanitized.Frames.Add(new SentryStackFrame
            {
                Function = frame.Function,
                Module = frame.Module,
                Package = frame.Package,
                Platform = frame.Platform,
                LineNumber = frame.LineNumber,
                ColumnNumber = frame.ColumnNumber,
                InApp = frame.InApp,
                ImageAddress = frame.ImageAddress,
                SymbolAddress = frame.SymbolAddress,
                InstructionAddress = frame.InstructionAddress,
                AddressMode = frame.AddressMode,
                FunctionId = frame.FunctionId,
                // Paths are kept for diagnosis but sanitized the same way as the request route;
                // source context lines and locals are dropped.
                FileName = SanitizePath(frame.FileName),
                AbsolutePath = SanitizePath(frame.AbsolutePath),
            });
        }

        return sanitized;
    }

    /// <summary>
    /// Strips fragment and query string, then applies <see cref="PublicTokenPathRedactor"/>. Used
    /// for the request route and for every retained stack-frame path, so a path that happens to be
    /// an absolute URL (<c>https://host/p?email=...#frag</c>) cannot retain the query or fragment.
    /// </summary>
    private static string? SanitizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        string path;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            path = absolute.AbsolutePath;
        }
        else
        {
            path = value;
            var fragment = path.IndexOf('#');
            if (fragment >= 0)
                path = path[..fragment];
            var query = path.IndexOf('?');
            if (query >= 0)
                path = path[..query];
        }

        return PublicTokenPathRedactor.Redact(path);
    }

    private static string? SafeMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return null;
        var upper = method.Trim().ToUpperInvariant();
        return AllowedMethods.Contains(upper) ? upper : null;
    }

    /// <summary>
    /// Structural validation of an allowlisted tag: an untrusted or malformed value is dropped
    /// even though its key is allowed.
    /// </summary>
    private static string? SanitizeTagValue(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return key switch
        {
            "correlation_id" => CorrelationIdFormat.IsMatch(trimmed) ? trimmed.ToLowerInvariant() : null,
            "account_id" => Guid.TryParse(trimmed, out var account) && account != Guid.Empty
                ? account.ToString()
                : null,
            "http.status_code" => int.TryParse(trimmed, out var status) && status is >= 100 and <= 599
                ? status.ToString()
                : null,
            _ => null,
        };
    }

    /// <summary>
    /// The structural allowlist above is the guarantee; this is the residual guard that a
    /// capability token has not survived inside a free-form string that is legitimately kept
    /// (a stack-frame path). If one has, discard the whole event.
    /// </summary>
    private static bool IsProvablySafe(SentryEvent evt)
    {
        foreach (var value in RetainedStrings(evt))
        {
            if (value is not null && UnredactedTokenRoute.IsMatch(value))
                return false;
        }

        return true;
    }

    private static IEnumerable<string?> RetainedStrings(SentryEvent evt)
    {
        yield return evt.Request?.Url;

        foreach (var tag in evt.Tags)
            yield return tag.Value;

        if (evt.SentryExceptions is null)
            yield break;

        foreach (var exception in evt.SentryExceptions)
        {
            yield return exception.Type;
            yield return exception.Module;

            if (exception.Stacktrace?.Frames is not { } frames)
                continue;

            foreach (var frame in frames)
            {
                yield return frame.FileName;
                yield return frame.AbsolutePath;
                yield return frame.Function;
                yield return frame.Module;
            }
        }
    }
}
