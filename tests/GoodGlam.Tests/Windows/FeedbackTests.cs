using FluentAssertions;
using GoodGlam.Windows;
using Xunit;

namespace GoodGlam.Tests.Windows;

/// <summary>
/// Covers the URL behind the in-plugin <b>Report Bug</b> button (<see cref="Feedback"/>). The actual
/// browser-open path (<c>Util.OpenLink</c>) can't run in CI, so this guards the part that matters for
/// triage: the link must point at the repo's new-issue page and pre-select the <c>bug_report.yml</c>
/// issue form so reporters land on the structured form (with the Windows-vs-Wine/Linux field).
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
}
