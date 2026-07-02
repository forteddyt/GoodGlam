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
    public void OpenBugReport_opens_exactly_the_bug_report_url()
    {
        var opener = new FakeLinkOpener();

        Feedback.OpenBugReport(opener);

        opener.Opened.Should().ContainSingle().Which.Should().Be(Feedback.BugReportUrl);
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
