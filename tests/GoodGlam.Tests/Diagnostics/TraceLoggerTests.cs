using FakeItEasy;
using FluentAssertions;
using GoodGlam.Diagnostics;
using Xunit;

namespace GoodGlam.Tests.Diagnostics;

public class TraceLoggerTests
{
    public TraceLoggerTests() => TestServices.EnsureLog();

    /// <summary>A stand-in owner type — deliberately unrelated to drops — to prove genericity.</summary>
    public sealed class SampleComponent
    {
    }

    [Fact]
    public void Component_is_the_owner_type_name()
    {
        TraceLogger<SampleComponent>.Component.Should().Be("SampleComponent");
        TraceLogger<TraceLoggerTests>.Component.Should().Be("TraceLoggerTests");
    }

    [Fact]
    public void Format_prefixes_the_message_with_the_component_tag()
        => new TraceLogger<SampleComponent>().Format("request failed")
            .Should().Be("GoodGlam[SampleComponent]: request failed");

    [Fact]
    public void Every_level_forwards_without_throwing()
    {
        // Services.Log is the no-op test log; exercise each level to prove the forwarders are wired.
        var logger = new TraceLogger<SampleComponent>();
        var act = () =>
        {
            logger.Verbose("v");
            logger.Debug("d");
            logger.Information("i");
            logger.Warning("w");
            logger.Warning("w", new InvalidOperationException());
            logger.Error("e");
            logger.Error("e", new InvalidOperationException());
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Interface_is_fakeable_for_unrelated_unit_tests()
    {
        // The point of exposing ITraceLogger<T>: a component under test can take a fake and have its
        // logging asserted without the Dalamud Services.Log singleton — and it is not drop-specific.
        var fake = A.Fake<ITraceLogger<SampleComponent>>();

        fake.Debug("hello");

        A.CallTo(() => fake.Debug("hello")).MustHaveHappenedOnceExactly();
    }
}
