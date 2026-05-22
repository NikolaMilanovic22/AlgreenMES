using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

public class SmokeTests : IntegrationTestBase
{
    public SmokeTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Api_Boots_And_Responds_To_Anonymous_Endpoint()
    {
        var response = await Client.GetAsync("/");

        // 200, 401, 404, 405 all prove the API booted and is serving requests.
        ((int)response.StatusCode).Should().BeInRange(200, 499);
    }
}
