using Xunit;

// GoodGlam wires its Dalamud dependencies through a single static holder (GoodGlam.Services), which
// tests populate with fakes via TestServices.Install. That static state is shared process-wide, so
// test classes that install services must not run concurrently. Disable xUnit's default per-class
// parallelization to keep the shared holder deterministic across the whole suite.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
