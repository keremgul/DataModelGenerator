namespace DataModelGenerator.Core.Models;

/// <summary>
/// Kullanıcının tanımladığı bağlantı profili. API anahtarı bu nesnede tutulmaz —
/// yalnızca <see cref="Id"/> ile <see cref="Security.ApiKeyStore"/> içinde şifreli saklanır.
/// </summary>
public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;

    public bool? LastTestSucceeded { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public string LastTestMessage { get; set; } = string.Empty;

    public ConnectionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        ProviderKey = ProviderKey,
        BaseUrl = BaseUrl,
        ModelId = ModelId,
        DeploymentName = DeploymentName,
        LastTestSucceeded = LastTestSucceeded,
        LastTestedAt = LastTestedAt,
        LastTestMessage = LastTestMessage
    };

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name) ? ProviderKey : Name;
}

public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SampleCompletion { get; set; }

    public static ConnectionTestResult Ok(string completion) =>
        new() { Success = true, Message = "Bağlantı başarılı — model yanıt üretti.", SampleCompletion = completion };

    public static ConnectionTestResult Fail(string message) =>
        new() { Success = false, Message = message };
}
