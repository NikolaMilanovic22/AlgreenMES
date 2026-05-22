using Xunit;

namespace AlGreenMES.Tests.Integration;

public class HealthTests : IntegrationTestBase
{
    public HealthTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact(Skip = "No /health endpoint exists in API — pending design decision (revisit when health checks are added).")]
    public Task HealthCheck_ReturnsSuccess() => Task.CompletedTask;
}
