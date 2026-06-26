using Microsoft.AspNetCore.Http;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.Requests;

namespace OpHalo.Api.Accounts;

public static class BadgeEndpoints
{
    public static void MapBadgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me/badge", GetBadge).RequireAuthorization();
    }

    private static async Task<IResult> GetBadge(
        GetBadgeCountService service,
        CancellationToken ct)
    {
        var result = await service.ExecuteAsync(ct);

        if (result.IsFailure)
            return ErrorHttpMapper.ToHttpResult(result.Error);

        var r = result.Value;
        return Results.Ok(new
        {
            count = r.Count,
            computedAtUtc = r.ComputedAtUtc
        });
    }
}
