using System.Text;
using System.Text.Json;

namespace GoodGlam.IntegrationTests;

/// <summary>
/// Why the live reachability probe failed. <c>EorzeaCollectionClient</c> returns a bare
/// <c>null</c> for every one of these, which is fine for the plugin (it just means "no verdict")
/// but useless in CI: an edge block and a stale test fixture look identical. Classifying the raw
/// response separates "we never reached Eorzea Collection" from "we reached it and the data
/// changed", which are entirely different fixes.
/// </summary>
public enum EcProbeOutcome
{
    /// <summary>The search returned a record whose <c>XIVApiId</c> matches the probe item.</summary>
    Reachable,

    /// <summary>Both transports returned no body at all — blocked (e.g. Cloudflare 403) or unreachable.</summary>
    NoResponse,

    /// <summary>A body came back but it isn't a JSON array — typically a Cloudflare block/challenge page.</summary>
    NotJson,

    /// <summary>A well-formed but empty result set: EC answered, it just knows no such item.</summary>
    NoRecords,

    /// <summary>Records came back, but none carries the probe's game item ID — EC-side data drift.</summary>
    NoMatchingRecord,
}

/// <summary>The classified probe response: the verdict plus a human-readable detail for the CI log.</summary>
public sealed record EcProbeDiagnosis(EcProbeOutcome Outcome, string Detail)
{
    public override string ToString() => $"{this.Outcome} — {this.Detail}";

    /// <summary>
    /// Classifies a raw <c>/gear/{slot}/search</c> response body against the game item ID the probe
    /// expects to find. Pure and offline so it is unit-testable without EC connectivity.
    /// </summary>
    public static EcProbeDiagnosis Classify(string? body, uint gameItemId)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new EcProbeDiagnosis(
                EcProbeOutcome.NoResponse,
                "both transports returned no body: the request was blocked (e.g. a Cloudflare 403 on " +
                "the runner's egress IP) or the host was unreachable. See the transport log below for " +
                "the HTTP status / curl exit code.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            return new EcProbeDiagnosis(
                EcProbeOutcome.NotJson,
                $"Eorzea Collection answered with a non-JSON body ({ex.Message}), which usually means a " +
                $"Cloudflare block/challenge page rather than the search endpoint. Body starts: {Snippet(body)}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new EcProbeDiagnosis(
                    EcProbeOutcome.NotJson,
                    $"the search endpoint returned JSON that is not the expected array of gear records " +
                    $"(root was {document.RootElement.ValueKind}). Body starts: {Snippet(body)}");
            }

            var records = document.RootElement.EnumerateArray().ToArray();
            if (records.Length == 0)
            {
                return new EcProbeDiagnosis(
                    EcProbeOutcome.NoRecords,
                    "Eorzea Collection is reachable but the search returned no gear records at all — the " +
                    "item name in the fixture may no longer match anything on EC.");
            }

            foreach (var record in records)
            {
                if (record.TryGetProperty("XIVApiId", out var xivApiId) &&
                    xivApiId.TryGetInt64(out var value) &&
                    value == gameItemId)
                {
                    return new EcProbeDiagnosis(
                        EcProbeOutcome.Reachable,
                        $"Eorzea Collection is reachable and still knows game item {gameItemId}.");
                }
            }

            return new EcProbeDiagnosis(
                EcProbeOutcome.NoMatchingRecord,
                $"Eorzea Collection is reachable ({records.Length} record(s) returned) but none carries " +
                $"XIVApiId={gameItemId}, so the EcFixtures entry has drifted from EC's data. Returned: " +
                $"{Describe(records)}");
        }
    }

    /// <summary>Summarises the returned records as <c>id/XIVApiId/name</c> triples for the failure message.</summary>
    private static string Describe(IReadOnlyList<JsonElement> records)
    {
        var summary = new StringBuilder();
        for (var i = 0; i < Math.Min(records.Count, 5); i++)
        {
            if (i > 0)
                summary.Append("; ");

            var record = records[i];
            summary.Append(Property(record, "ID")).Append('/')
                   .Append(Property(record, "XIVApiId")).Append('/')
                   .Append(Property(record, "Name"));
        }

        if (records.Count > 5)
            summary.Append($"; … {records.Count - 5} more");

        return summary.ToString();

        static string Property(JsonElement record, string name)
            => record.TryGetProperty(name, out var value) ? value.ToString() : "?";
    }

    /// <summary>A single-line, length-bounded excerpt of a response body, safe to put in a test failure.</summary>
    private static string Snippet(string body)
    {
        // A null separator array is String.Split's "split on any whitespace" overload, which is how
        // the excerpt gets collapsed onto one line regardless of the markup it came from.
        const char[]? anyWhitespace = null;

        var collapsed = string.Join(' ', body.Split(anyWhitespace, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 300 ? collapsed : collapsed[..300] + "…";
    }
}
