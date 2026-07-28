using FluentAssertions;
using Xunit;

namespace GoodGlam.IntegrationTests.Harness;

/// <summary>
/// Harness tests for <see cref="EcReachabilityReport"/>, the post-mortem the fixture attaches when
/// the live-EC probe fails. Like <see cref="RetryPolicyTests"/> these sit outside the live-EC
/// collection and touch no network — they assert the *classification and wording* of a report built
/// from captured response metadata, so they keep passing (and stay meaningful) during an outage.
/// </summary>
public sealed class EcReachabilityReportTests
{
    private const string ChallengeBody =
        "<!DOCTYPE html><html lang=\"en-US\"><head><title>Just a moment...</title>" +
        "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\">";

    /// <summary>
    /// The signature seen on GitHub-hosted runners: Cloudflare answers the datacenter IP with a
    /// managed challenge instead of proxying to the origin.
    /// </summary>
    [Fact]
    public void Recognises_a_cloudflare_challenge_from_the_mitigation_header()
    {
        var report = new EcReachabilityReport(
            StatusCode: 403,
            CfRay: "a22083600fa52a9f-LAX",
            CfMitigated: "challenge",
            Server: "cloudflare",
            BodySnippet: ChallengeBody);

        report.IsCloudflareChallenge.Should().BeTrue();
        report.Describe().Should().Contain("Cloudflare bot challenge");
    }

    /// <summary>The header isn't always present; the interstitial body alone is enough to classify.</summary>
    [Fact]
    public void Recognises_a_cloudflare_challenge_from_the_interstitial_body()
    {
        var report = new EcReachabilityReport(
            StatusCode: 403,
            CfRay: null,
            CfMitigated: null,
            Server: "cloudflare",
            BodySnippet: ChallengeBody);

        report.IsCloudflareChallenge.Should().BeTrue(
            "the 'Just a moment...' interstitial is the challenge page");
    }

    /// <summary>
    /// A plain 403 is a different problem (a genuinely forbidden endpoint), and conflating the two
    /// would send the next person down the wrong path.
    /// </summary>
    [Fact]
    public void Does_not_mistake_a_plain_403_for_a_challenge()
    {
        var report = new EcReachabilityReport(
            StatusCode: 403,
            CfRay: null,
            CfMitigated: null,
            Server: "nginx",
            BodySnippet: "{\"error\":\"forbidden\"}");

        report.IsCloudflareChallenge.Should().BeFalse();
        report.Describe().Should().NotContain("Cloudflare bot challenge");
    }

    [Fact]
    public void Describes_the_status_ray_and_mitigation_so_the_log_is_self_contained()
    {
        var report = new EcReachabilityReport(
            StatusCode: 403,
            CfRay: "a22083600fa52a9f-LAX",
            CfMitigated: "challenge",
            Server: "cloudflare",
            BodySnippet: ChallengeBody);

        var text = report.Describe();

        text.Should().Contain("403");
        text.Should().Contain("a22083600fa52a9f-LAX", "the CF-Ray is what EC's operator would need");
        text.Should().Contain("challenge");
        text.Should().Contain("Just a moment", "a body snippet proves what was actually served");
    }

    /// <summary>A challenge is environmental, so the report should say so rather than implicate the code.</summary>
    [Fact]
    public void Explains_that_a_challenge_is_a_runner_ip_problem_not_a_code_problem()
    {
        var report = new EcReachabilityReport(
            StatusCode: 403,
            CfRay: "a22083600fa52a9f-LAX",
            CfMitigated: "challenge",
            Server: "cloudflare",
            BodySnippet: ChallengeBody);

        report.Describe().Should().Contain("runner");
    }

    /// <summary>When the probe never got a response at all, say that instead of inventing a status.</summary>
    [Fact]
    public void Reports_a_transport_level_failure_when_no_response_arrived()
    {
        var report = new EcReachabilityReport(TransportError: "TaskCanceledException: timed out after 20s");

        report.IsCloudflareChallenge.Should().BeFalse();

        var text = report.Describe();
        text.Should().Contain("no HTTP response");
        text.Should().Contain("timed out after 20s");
    }

    [Fact]
    public void Reports_an_unexpected_success_when_the_probe_itself_succeeds()
    {
        var report = new EcReachabilityReport(
            StatusCode: 200,
            CfRay: "a22083600fa52a9f-LAX",
            CfMitigated: null,
            Server: "cloudflare",
            BodySnippet: "[{\"ID\":8912}]");

        report.IsCloudflareChallenge.Should().BeFalse();
        report.Describe().Should().Contain("200");
    }
}
