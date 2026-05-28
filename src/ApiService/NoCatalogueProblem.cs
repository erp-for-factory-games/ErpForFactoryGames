using Erp.Application.Common;
using Erp.Infrastructure;
using Microsoft.Extensions.Options;

namespace ApiService;

/// <summary>
/// Maps a player's missing-catalogue state to a structured ProblemDetails
/// response (ADR-0025 §4). Used by planner-side endpoints to short-circuit
/// when the request's player has no uploaded catalogue and the dev
/// server-local fallback is off.
///
/// <para>When <see cref="CatalogueOptions.AllowServerLocalFallback"/> is on
/// (dev), an empty catalogue is treated as legacy "no-op empty" instead of
/// 503 — the dev environment may genuinely have no Docs.json on disk yet,
/// and the planner UI handles empty arrays gracefully.</para>
/// </summary>
internal static class NoCatalogueProblem
{
    /// <summary>Code surfaced in ProblemDetails.extensions["code"] so the Web UI can branch on it.</summary>
    public const string Code = "no_catalogue";

    /// <summary>
    /// Returns <c>null</c> when the catalogue is loaded, or when the dev
    /// server-local fallback is enabled (legacy empty-arrays behavior).
    /// Otherwise a 503 ProblemDetails that the Web UI maps to "Pair an agent
    /// to load your catalogue".
    /// </summary>
    public static IResult? IfMissing(ICatalogProvider catalog, IOptions<CatalogueOptions> options)
    {
        if (catalog.IsLoaded) return null;
        if (options.Value.AllowServerLocalFallback) return null;
        return Results.Problem(
            title: "No catalogue available for this player.",
            detail: "Pair an agent and upload your Satisfactory Docs.json to enable planning.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = Code,
                ["source"] = catalog.Source,
            });
    }
}
