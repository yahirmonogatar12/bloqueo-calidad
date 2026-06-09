using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QualityLock.Api.IntegrationTests;

/// <summary>
/// WebApplicationFactory that supplies the minimal configuration the API now
/// requires at startup (a connection string and a JWT signing key) so the host
/// can boot. The health endpoint itself does not hit the database.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySQL"] = "Server=localhost;Port=3306;Database=test;Uid=test;Pwd=test;",
                ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-bytes-long!!",
                ["Jwt:Issuer"] = "QualityLock.Api",
                ["Jwt:Audience"] = "QualityLock.Clients",
                ["Auth:ClientApiKey"] = "integration-test-client-key"
            });
        });
    }
}

public class HealthCheckTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task HealthEndpoint_Returns200()
    {
        // /health is AllowAnonymous and does not touch the database.
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
