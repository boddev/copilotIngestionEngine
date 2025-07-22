using Azure.Identity;
using Azure.Core;
using Microsoft.Graph;
using copilotIngestionEngine.Configuration;
using copilotIngestionEngine.Models;

namespace copilotIngestionEngine.Services;

public interface IApiKeyValidationService
{
    Task<bool> ValidateAuthenticationAsync(AuthenticationRequest? authRequest);
}

public class ApiKeyValidationService : IApiKeyValidationService
{
    private readonly ILogger<ApiKeyValidationService> _logger;

    public ApiKeyValidationService(ILogger<ApiKeyValidationService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ValidateAuthenticationAsync(AuthenticationRequest? authRequest)
    {
        if (authRequest == null)
        {
            _logger.LogWarning("Authentication request is null");
            return false;
        }

        if (string.IsNullOrEmpty(authRequest.ClientId) ||
            string.IsNullOrEmpty(authRequest.ClientSecret) ||
            string.IsNullOrEmpty(authRequest.TenantId) ||
            string.IsNullOrEmpty(authRequest.ConnectionId))
        {
            _logger.LogWarning("One or more required authentication parameters are missing");
            return false;
        }

        try
        {
            var options = new ClientSecretCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
            };

            var clientSecretCredential = new ClientSecretCredential(
                authRequest.TenantId,
                authRequest.ClientId,
                authRequest.ClientSecret,
                options);

            // For application authentication, just try to get a token
            // This is lighter than making a Graph API call
            var tokenRequestContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
            var tokenResult = await clientSecretCredential.GetTokenAsync(tokenRequestContext);

            if (!string.IsNullOrEmpty(tokenResult.Token))
            {
                _logger.LogInformation("Successfully authenticated as application with tenant: {TenantId} and connection: {ConnectionId}",
                    authRequest.TenantId, authRequest.ConnectionId);
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to obtain access token");
                return false;
            }
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
            _logger.LogError(ex, "Unexpected error during authentication validation");
            return false;
        }
    }
}