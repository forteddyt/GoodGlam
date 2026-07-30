using GoodGlam.Glam;

namespace GoodGlam.Tests;

/// <summary>
/// Loads the recorded Eorzea Collection responses under <c>Fixtures/Ec/</c> and serves them through
/// an <see cref="IEcTransport"/>, so tests can drive the real client and pipeline without a network.
/// See <c>Fixtures/Ec/README.md</c> for provenance and how to refresh a capture.
/// </summary>
internal static class EcFixtures
{
    /// <summary>Response to <c>POST /gear/body/search</c> for "Scion Adventurer's Jacket".</summary>
    public static string SearchBodyScionJacket => Read("search-body-scion-jacket.json");

    /// <summary>Response to the loves-ordered glamour listing for EC body piece 8912.</summary>
    public static string ListingBody8912 => Read("listing-body-8912.html");

    /// <summary>A transport that replays the recorded pair, and records the URLs it was asked for.</summary>
    public static ReplayEcTransport ReplayTransport() =>
        new(postResult: SearchBodyScionJacket, getResult: ListingBody8912);

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ec", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Recorded Eorzea Collection fixture '{fileName}' was not found at '{path}'. " +
                "It should be copied to the output directory by GoodGlam.Tests.csproj.",
                path);
        }

        var content = File.ReadAllText(path);

        // A capture can silently be a Cloudflare interstitial: curl exits 0 on a 403, so a blocked
        // refresh lands in the file looking like a successful download. Left unchecked the suite
        // would go green against a block page, quietly testing nothing.
        if (content.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || content.Contains("cf-mitigated", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Recorded fixture '{fileName}' looks like a Cloudflare challenge page rather than a " +
                "real Eorzea Collection response. Re-capture it (see Fixtures/Ec/README.md) - a " +
                "challenge page would make these tests pass while exercising nothing.");
        }

        return content;
    }
}

/// <summary>
/// Serves recorded response bodies and records the requests made, so tests can assert both what the
/// client parsed and the URL it asked for.
/// </summary>
internal sealed class ReplayEcTransport(string? postResult, string? getResult) : IEcTransport
{
    public readonly List<string> PostUrls = new();
    public readonly List<string> PostBodies = new();
    public readonly List<string> GetUrls = new();

    public Task<string?> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        this.PostUrls.Add(url);
        this.PostBodies.Add(jsonBody);
        return Task.FromResult(postResult);
    }

    public Task<string?> GetAsync(string url, CancellationToken ct)
    {
        this.GetUrls.Add(url);
        return Task.FromResult(getResult);
    }
}
