namespace GoodGlam.Diagnostics;

/// <summary>
/// A component-scoped diagnostic logger. The owning type <typeparamref name="T"/> identifies the
/// component every message is tagged with (see <see cref="TraceLogger{T}"/>), mirroring the
/// <c>ILogger&lt;T&gt;</c> pattern: a class that logs takes an <see cref="ITraceLogger{T}"/> of
/// itself, so its log tag follows the class name automatically and refactor-safely.
///
/// Exposed as an interface (rather than only the concrete <see cref="TraceLogger{T}"/>) so unit
/// tests for unrelated behaviour can inject a fake and, where relevant, assert what was logged
/// without going through the Dalamud <c>Services.Log</c> singleton.
/// </summary>
/// <typeparam name="T">The component that owns the logger; its name becomes the log tag.</typeparam>
public interface ITraceLogger<T>
{
    /// <summary>Finest-grained tracing (per-item/raw detail); off for normal users.</summary>
    void Verbose(string message);

    /// <summary>One line per meaningful decision or discrete user action - the primary debug tier.</summary>
    void Debug(string message);

    /// <summary>Sparse lifecycle / user-visible outcomes.</summary>
    void Information(string message);

    /// <summary>A recoverable problem worth surfacing.</summary>
    void Warning(string message);

    /// <summary>A recoverable problem, with the exception that caused it.</summary>
    void Warning(string message, Exception exception);

    /// <summary>A failure worth surfacing.</summary>
    void Error(string message);

    /// <summary>A failure, with the exception that caused it.</summary>
    void Error(string message, Exception exception);
}
