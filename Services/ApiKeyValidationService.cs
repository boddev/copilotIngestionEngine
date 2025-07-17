using Azure.Identity;
using Azure.Core;
using Microsoft.Graph;
using copilotIngestionEngine.Configuration;

namespace copilotIngestionEngine.Services;

public interface IApiKeyValidationService
{
    Task<bool> ValidateApiKeyAsync(string? apiKey);
}

public class ApiKeyValidationService : IApiKeyValidationService
{
    private readonly MicrosoftGraphOptions _graphOptions;
    private readonly ILogger<ApiKeyValidationService> _logger;

    public ApiKeyValidationService(IConfiguration configuration, ILogger<ApiKeyValidationService> logger)
    {
        _graphOptions = new MicrosoftGraphOptions();
        configuration.GetSection(MicrosoftGraphOptions.SectionName).Bind(_graphOptions);
        _logger = logger;

        // Validate required configuration
        if (string.IsNullOrEmpty(_graphOptions.TenantId))
            throw new InvalidOperationException("Microsoft Graph TenantId not configured");
        if (string.IsNullOrEmpty(_graphOptions.ClientId))
            throw new InvalidOperationException("Microsoft Graph ClientId not configured");
    }

    public async Task<bool> ValidateApiKeyAsync(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("API key is null or empty");
            return false;
        }

        try
        {
            // Use the API key as the client secret
            var options = new ClientSecretCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
            };

            var clientSecretCredential = new ClientSecretCredential(
                _graphOptions.TenantId,
                _graphOptions.ClientId,
                apiKey, // Use the API key as client secret
                options);

            // Create GraphServiceClient to test authentication
            //var graphServiceClient = new GraphServiceClient(clientSecretCredential);

            // For application authentication, just try to get a token
            // This is lighter than making a Graph API call
            var tokenRequestContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
            var tokenResult = await clientSecretCredential.GetTokenAsync(tokenRequestContext);

            if (!string.IsNullOrEmpty(tokenResult.Token))
            {
                _logger.LogInformation("Successfully authenticated as application with tenant: {TenantId}", _graphOptions.TenantId);
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to obtain access token");
                return false;
            }
            // Try to make a simple request to validate the credentials
            // This will throw an exception if authentication fails
            //var me = await graphServiceClient.Me.GetAsync();

            //_logger.LogInformation("Successfully authenticated with Microsoft tenant");
            //return true;
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogWarning("Authentication failed: {Message}", ex.Message);
            return false;
        }
        catch (ServiceException ex)
        {
            _logger.LogWarning("Microsoft Graph service error: {Message}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during API key validation");
            return false;
        }
    }
}