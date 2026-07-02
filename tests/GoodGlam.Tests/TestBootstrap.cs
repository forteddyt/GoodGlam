using System.Runtime.CompilerServices;

namespace GoodGlam.Tests;

/// <summary>
/// Assembly bootstrap. Seeds the no-op Dalamud log into the process-global <c>Services</c> holder
/// exactly once, before any test runs, via a <see cref="ModuleInitializerAttribute"/> (module
/// initializers execute when the runtime loads the assembly — ahead of test discovery and every test
/// constructor).
/// </summary>
/// <remarks>
/// GoodGlam components log through <c>Services.Log</c> (see <c>TraceLogger</c>), which is null until
/// seeded. Any test that exercises a logging code path would throw a <see cref="NullReferenceException"/>
/// if it were the first to run. xUnit does not guarantee the order in which test collections execute
/// (parallelization is disabled, but ordering across classes is still unspecified), so relying on some
/// <em>other</em> class's constructor to call <see cref="TestServices.EnsureLog"/> first made such
/// tests order-dependent and intermittently flaky — e.g. <c>MainWindowTests.OnOpen_...</c>, whose
/// <c>OnOpen</c> logs a debug line. Seeding here removes that ordering dependency for the entire
/// assembly; the per-class <see cref="TestServices.EnsureLog"/> calls remain a harmless, idempotent
/// safety net.
/// </remarks>
internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Init() => TestServices.EnsureLog();
}
