namespace copilotIngestionEngine.Configuration;

public class MicrosoftGraphOptions
{
    public const string SectionName = "MicrosoftGraph";
    
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
}
