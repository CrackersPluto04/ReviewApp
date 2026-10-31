namespace ReviewApp.Api.Tests.Support;

// The expiry and cleanup jobs work on every row of the shared database, not just a test's own data.
// Classes in this collection run after the parallel ones, one at a time.
[CollectionDefinition(Name, DisableParallelization = true)]
public class GlobalJobsCollection
{
    public const string Name = "Global jobs";
}
