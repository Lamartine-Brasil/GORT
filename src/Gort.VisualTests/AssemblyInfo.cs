using Xunit;

// UI headless roda numa única thread de dispatcher — sem paralelismo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
