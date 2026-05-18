using Xunit;

namespace AlGreenMES.Tests.Integration;

// Sprint 3.0 housekeeping — initialize the factory in the test class
// lifecycle, not via the collection fixture's IAsyncLifetime. xUnit 2.9.2
// + Testcontainers 4.0.0 + Apple Silicon macOS combo wasn't honoring
// IAsyncLifetime on the collection fixture (verified empirically:
// `docker ps` during a test run showed no Postgres container). Moving
// InitializeAsync onto IntegrationTestBase forces the container to
// start before any test in the suite runs, with Factory.InitializeAsync
// being idempotent (PostgreSqlContainer.StartAsync is a no-op on an
// already-started container). DisposeAsync is delegated to the factory
// so cleanup still happens once per collection.
[Collection("Integration")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly AlgreenWebApplicationFactory Factory;
    protected HttpClient Client = null!;

    protected IntegrationTestBase(AlgreenWebApplicationFactory factory)
    {
        Factory = factory;
    }

    public async Task InitializeAsync()
    {
        await Factory.InitializeAsync();
        Client = Factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<AlgreenWebApplicationFactory>
{
    // Shared fixture across all integration test classes — single Postgres container for the run.
}
