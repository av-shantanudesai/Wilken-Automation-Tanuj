using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WilkenAutomation.Application.Models;
using WilkenAutomation.Infrastructure.Configuration;

namespace WilkenAutomation.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"wilken-api-{Guid.NewGuid():N}.db");

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
        Environment.SetEnvironmentVariable(DatabaseConnection.ConnectionEnvironmentVariable, $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("Jwt__Key", "WilkenAutomation-TestKey-MustBe32chars!!");
        Environment.SetEnvironmentVariable("Jwt__WorkerKey", "WilkenAutomation-TestWorkerKey-32ch!!");
        Environment.SetEnvironmentVariable("Auth__AllowRegistration", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable(DatabaseConnection.ConnectionEnvironmentVariable, null);
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}

public class ApiIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Runs_WithoutToken_IsUnauthorized()
    {
        var response = await _client.GetAsync("/api/runs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_ThenListRuns_ReturnsEmptyForNewUser()
    {
        var token = await RegisterAsync($"user{Guid.NewGuid():N}@example.com");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/runs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var runs = await response.Content.ReadFromJsonAsync<List<RunSummaryDto>>();
        Assert.NotNull(runs);
        Assert.Empty(runs!);
    }

    [Fact]
    public async Task UserCannotReadAnotherUsersRun()
    {
        var ownerToken = await RegisterAsync($"owner{Guid.NewGuid():N}@example.com");
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new CreateRunRequestDto
            {
                ClientCount = 1,
                YearFrom = 2024,
                YearTo = 2024,
                AutoStart = false
            })
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var created = await _client.SendAsync(create);
        created.EnsureSuccessStatusCode();
        using var createdJson = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        var runId = createdJson!.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));

        var otherToken = await RegisterAsync($"other{Guid.NewGuid():N}@example.com");
        using var peek = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}");
        peek.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var hidden = await _client.SendAsync(peek);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_WithUserToken_IsForbidden()
    {
        var token = await RegisterAsync($"workerprobe{Guid.NewGuid():N}@example.com");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/worker/heartbeat")
        {
            Content = JsonContent.Create(new WorkerStatusDto { WorkerState = "IDLE" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> RegisterAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto
        {
            Email = email,
            Password = "Secret123",
            DisplayName = "Tester"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
        return body!.Token;
    }
}
