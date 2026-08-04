using Xunit;

// liblouis keeps global state (compiled table cache, log callback, data path) and is explicitly
// not thread safe - LibLouis serialises its own calls behind a lock for exactly that reason.
// xunit parallelises across test classes by default, which lets unsynchronised native calls race
// and produce spurious "could not be compiled" failures. Run the whole assembly serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
