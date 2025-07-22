namespace copilotIngestionEngine.Models;

public class AuthenticationRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
}