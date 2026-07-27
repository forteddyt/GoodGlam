using FluentAssertions;
using Xunit;

namespace GoodGlam.IntegrationTests;

/// <summary>
/// Offline coverage of the probe classifier. Deliberately *not* in the <see cref="LiveEc.Collection"/>
/// collection: these need no Eorzea Collection connectivity, so they still run (and still report) when
/// the live suite can't reach EC — which is exactly when the classification matters.
/// </summary>
public sealed class EcProbeDiagnosisTests
{
    private const uint GameItemId = 17492;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_body_is_reported_as_no_response(string? body)
    {
        var diagnosis = EcProbeDiagnosis.Classify(body, GameItemId);

        diagnosis.Outcome.Should().Be(EcProbeOutcome.NoResponse);
        diagnosis.Detail.Should().Contain("blocked");
    }

    [Fact]
    public void Cloudflare_challenge_page_is_reported_as_not_json_with_an_excerpt()
    {
        const string body = "<!DOCTYPE html>\n<html><head><title>Attention Required! | Cloudflare</title>";

        var diagnosis = EcProbeDiagnosis.Classify(body, GameItemId);

        diagnosis.Outcome.Should().Be(EcProbeOutcome.NotJson);
        diagnosis.Detail.Should().Contain("Cloudflare");
        diagnosis.Detail.Should().NotContain("\n", "the excerpt is collapsed onto a single line");
    }

    [Fact]
    public void Json_that_is_not_an_array_is_reported_as_not_json()
    {
        var diagnosis = EcProbeDiagnosis.Classify("""{"error":"nope"}""", GameItemId);

        diagnosis.Outcome.Should().Be(EcProbeOutcome.NotJson);
        diagnosis.Detail.Should().Contain("array");
    }

    [Fact]
    public void Empty_result_set_is_reported_as_no_records()
    {
        var diagnosis = EcProbeDiagnosis.Classify("[]", GameItemId);

        diagnosis.Outcome.Should().Be(EcProbeOutcome.NoRecords);
        diagnosis.Detail.Should().Contain("reachable");
    }

    [Fact]
    public void Records_without_the_probe_game_id_are_reported_as_data_drift()
    {
        const string body = """
            [{"ID":8912,"Name":"Scion Adventurer's Jacket","XIVApiId":99999}]
            """;

        var diagnosis = EcProbeDiagnosis.Classify(body, GameItemId);

        diagnosis.Outcome.Should().Be(EcProbeOutcome.NoMatchingRecord);
        diagnosis.Detail.Should().Contain("XIVApiId=17492");
        diagnosis.Detail.Should().Contain("99999", "the returned records are listed so the fixture can be corrected");
    }

    [Fact]
    public void Matching_record_is_reported_as_reachable()
    {
        const string body = """
            [{"ID":1,"Name":"Something Else","XIVApiId":1},
             {"ID":8912,"Name":"Scion Adventurer's Jacket","XIVApiId":17492}]
            """;

        var diagnosis = EcProbeDiagnosis.Classify(body, GameItemId);

        diagnosis.Outcome.Should().Be(EcProbeOutcome.Reachable);
    }

    [Fact]
    public void Long_bodies_are_truncated_so_the_failure_message_stays_readable()
    {
        var body = new string('x', 5000);

        var diagnosis = EcProbeDiagnosis.Classify(body, GameItemId);

        diagnosis.Outcome.Should().Be(EcProbeOutcome.NotJson);
        diagnosis.Detail.Length.Should().BeLessThan(800);
    }
}
