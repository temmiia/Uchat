using System.Net;
using System.Text.Json;
using UChat.Integration.Tests.Infrastructure;

namespace UChat.Integration.Tests;

public class HealthCheckIntegrationTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsOk_WithSecurityHeaders()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new UChatWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").First());

        var content = await response.Content.ReadAsStringAsync(ct);
        using var json = JsonDocument.Parse(content);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }
}
