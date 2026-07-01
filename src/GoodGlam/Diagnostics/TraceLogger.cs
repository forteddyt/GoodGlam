namespace GoodGlam.Diagnostics;

/// <summary>
/// Default <see cref="ITraceLogger{T}"/>: prefixes every message with the owning component's name
/// (<c>GoodGlam[Owner]: ...</c>) and forwards it to the Dalamud <c>Services.Log</c> at the matching
/// level. The tag is derived from <c>typeof(T).Name</c>, so a component's log lines stay greppable
/// and follow the class name if it is ever renamed.
///
/// It is stateless and holds no Dalamud references of its own, so constructing one never touches the
/// framework - it is cheap to <c>new</c> at any composition point (including UI/entry-point fields)
/// and harmless in unit tests, where <c>Services.Log</c> is a no-op. Debug/Verbose output is gated
/// by Dalamud's log level (<c>/xllog</c>), so those tiers are inert for normal users.
/// </summary>
/// <typeparam name="T">The component that owns the logger; its name becomes the log tag.</typeparam>
public sealed class TraceLogger<T> : ITraceLogger<T>
{
    /// <summary>The component tag stamped on every message, derived from the owning type name.</summary>
    internal static string Component => typeof(T).Name;

    /// <summary>Builds the tagged message body, e.g. <c>GoodGlam[EcTransport]: request failed</c>.</summary>
    internal string Format(string message) => $"GoodGlam[{Component}]: {message}";

    public void Verbose(string message) => Services.Log.Verbose(this.Format(message));

    public void Debug(string message) => Services.Log.Debug(this.Format(message));

    public void Information(string message) => Services.Log.Information(this.Format(message));

    public void Warning(string message) => Services.Log.Warning(this.Format(message));

    public void Warning(string message, Exception exception) => Services.Log.Warning(exception, this.Format(message));

    public void Error(string message) => Services.Log.Error(this.Format(message));

    public void Error(string message, Exception exception) => Services.Log.Error(exception, this.Format(message));
}
