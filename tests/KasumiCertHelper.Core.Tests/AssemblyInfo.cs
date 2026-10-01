// Loc is process wide state, so tests that switch the language must not run side by side.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
