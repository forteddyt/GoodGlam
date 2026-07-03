using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers the URLs behind the in-plugin <b>Report Bug</b> and <b>Suggest Feature</b> buttons
/// (<see cref="Feedback"/>). The actual browser-open path (<c>Util.OpenLink</c>) can't run in CI, so
/// this guards the part that matters for triage: each link must point at the repo's new-issue page
/// and pre-select the matching issue form (<c>bug_report.yml</c> / <c>feature_request.yml</c>) so
/// reporters land on the structured form.
/// </summary>
public class FeedbackTests
{
    [Fact]
    public void Bug_report_url_targets_the_repo_new_issue_page()
    {
        Feedback.BugReportUrl.Should().StartWith("https://github.com/forteddyt/GoodGlam/issues/new");
    }

    [Fact]
    public void Bug_report_url_preselects_the_bug_report_form()
    {
        Feedback.BugReportUrl.Should().Contain("template=bug_report.yml");
    }

    [Fact]
    public void BuildBugReportUrl_appends_the_encoded_version_to_the_base_url()
    {
        var url = Feedback.BuildBugReportUrl(new Version(0, 1, 0, 0));

        url.Should().Be($"{Feedback.BugReportUrl}&goodglam-version=v0.1.0.0");
    }

    [Fact]
    public void BuildBugReportUrl_uses_the_about_tab_version_format()
    {
        var version = new Version(1, 2, 3, 4);

        Feedback.BuildBugReportUrl(version)
            .Should().EndWith("&goodglam-version=" + Uri.EscapeDataString(AboutInfo.FormatVersion(version)));
    }

    [Fact]
    public void BuildBugReportUrl_fills_the_field_even_when_the_version_is_null()
    {
        // v(unknown) contains parentheses, so the value must be URL-encoded.
        Feedback.BuildBugReportUrl(null)
            .Should().Be($"{Feedback.BugReportUrl}&goodglam-version=v%28unknown%29");
    }

    [Fact]
    public void OpenBugReport_opens_exactly_the_built_bug_report_url()
    {
        var opener = new FakeLinkOpener();
        var version = new Version(0, 1, 0, 0);

        Feedback.OpenBugReport(opener, version);

        opener.Opened.Should().ContainSingle().Which.Should().Be(Feedback.BuildBugReportUrl(version));
    }

    [Fact]
    public void Suggest_feature_url_targets_the_repo_new_issue_page()
    {
        Feedback.SuggestFeatureUrl.Should().StartWith("https://github.com/forteddyt/GoodGlam/issues/new");
    }

    [Fact]
    public void Suggest_feature_url_preselects_the_feature_request_form()
    {
        Feedback.SuggestFeatureUrl.Should().Contain("template=feature_request.yml");
    }

    [Fact]
    public void OpenSuggestFeature_opens_exactly_the_suggest_feature_url()
    {
        var opener = new FakeLinkOpener();

        Feedback.OpenSuggestFeature(opener);

        opener.Opened.Should().ContainSingle().Which.Should().Be(Feedback.SuggestFeatureUrl);
    }
}
