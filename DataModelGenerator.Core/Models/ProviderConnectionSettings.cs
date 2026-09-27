namespace DataModelGenerator.Core.Models;

public class ProviderConnectionSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
}

public class ModelInfo
{
    public ModelInfo(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }
    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
