using Xunit;

namespace AlGreenMES.Tests.Integration;

public class SignalRTests : IntegrationTestBase
{
    public SignalRTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact(Skip = "SignalR client over WebApplicationFactory needs WebSocket bridging via TestServer.CreateWebSocketClient — defer to Sprint 2.3 with cross-tenant tests.")]
    public Task Connect_WithoutAuth_Fails() => Task.CompletedTask;

    [Fact(Skip = "SignalR client over WebApplicationFactory needs WebSocket bridging via TestServer.CreateWebSocketClient — defer to Sprint 2.3 with cross-tenant tests.")]
    public Task Connect_WithAuth_Succeeds() => Task.CompletedTask;
}
