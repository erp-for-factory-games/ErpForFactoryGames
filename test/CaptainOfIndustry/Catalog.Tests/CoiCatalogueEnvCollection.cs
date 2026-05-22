namespace CaptainOfIndustry.Catalog.Tests;

/// <summary>
/// xUnit collection that serializes any test class touching the
/// <c>ERP_COI_CATALOGUE_PATH</c> process env var (see
/// <see cref="CoiCataloguePathResolver.EnvironmentVariable"/>).
///
/// xUnit's default parallelism runs test *classes* in parallel — so
/// when three classes each `SetEnvironmentVariable` for setup and `null`
/// for teardown, they race. Symptom: a test sets `C:\from-env.json`,
/// another concurrently-running test in a different class wipes it,
/// the first test reads back `C:\from-config.json`, assertion fails.
/// Reproduces under CI load; rarely on a fast dev box.
///
/// Tag every class that touches that env var with
/// <c>[Collection(nameof(CoiCatalogueEnvCollection))]</c> to make them
/// share this collection — xUnit then runs them sequentially.
/// </summary>
[CollectionDefinition(nameof(CoiCatalogueEnvCollection), DisableParallelization = true)]
public sealed class CoiCatalogueEnvCollection
{
}
