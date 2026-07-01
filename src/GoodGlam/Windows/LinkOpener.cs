using System.Diagnostics.CodeAnalysis;
using Dalamud.Utility;

namespace GoodGlam.Windows;

/// <summary>
/// Opens an external URL in the user's browser. The one seam over Dalamud's static
/// <see cref="Util.OpenLink"/>: production forwards to it (see <see cref="DalamudLinkOpener"/>),
/// while tests substitute a fake so the "which URL gets opened" decision is assertable without a
/// real browser. Mirrors the pure-logic split used elsewhere in the plugin.
/// </summary>
internal interface ILinkOpener
{
    void Open(string url);
}

/// <summary>
/// Production <see cref="ILinkOpener"/>: forwards to Dalamud's <see cref="Util.OpenLink"/>. A
/// one-line shim over a static Dalamud call that launches the OS browser, so it can't run in CI and
/// is excluded from coverage; the callers that decide <em>what</em> to open are tested against a fake.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "One-line forwarder over the static Util.OpenLink (launches the OS browser); can't run in CI.")]
internal sealed class DalamudLinkOpener : ILinkOpener
{
    public void Open(string url) => Util.OpenLink(url);
}
