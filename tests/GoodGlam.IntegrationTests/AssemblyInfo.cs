using Xunit;

// The live tests share process-wide static state: GoodGlam.Services.Log (populated by
// LiveEc.EnsureLog) and the LiveEcDiagnostics capture buffer that the offline diagnostics tests
// reset and assert on. Running classes concurrently would let one class's Reset() wipe the log
// evidence another class is about to report. Serialize the whole assembly — these tests are also
// deliberately polite to Eorzea Collection, so there is nothing to gain from parallelism.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
