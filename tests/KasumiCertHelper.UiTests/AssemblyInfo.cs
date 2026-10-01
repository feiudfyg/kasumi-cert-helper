// The tests share one application instance and its active language, so they must run one at a time.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
