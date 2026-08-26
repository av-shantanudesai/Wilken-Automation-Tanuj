using WilkenAutomation.Application.Interfaces;

namespace WilkenAutomation.Worker.Wilken;

/// <summary>
/// Credentials come from configuration (user-secrets / environment), never from
/// source code: username via Wilken:Username, password via the WILKEN_PASSWORD
/// environment variable or Wilken:Password (user-secrets). A dedicated secret
/// store (e.g. Windows Credential Manager, Key Vault) can replace this later
/// behind the same interface.
/// </summary>
public class ConfigurationCredentialProvider : IWilkenCredentialProvider
{
    private readonly IConfiguration _configuration;

    public ConfigurationCredentialProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<WilkenCredentials?> GetCredentialsAsync(CancellationToken ct)
    {
        var username = _configuration["Wilken:Username"];
        var password = Environment.GetEnvironmentVariable("WILKEN_PASSWORD")
            ?? _configuration["Wilken:Password"];

        return Task.FromResult(
            string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)
                ? null
                : new WilkenCredentials(username, password));
    }
}
