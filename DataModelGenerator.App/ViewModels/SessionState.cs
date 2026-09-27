using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Pipeline;
using DataModelGenerator.Core.Providers;

namespace DataModelGenerator.App.ViewModels;

/// <summary>Adımlar arasında paylaşılan oturum durumu.</summary>
public class SessionState
{
    public ConnectionProfile? ActiveProfile { get; set; }
    public ProjectInput Input { get; set; } = new();
    public PipelineContext? Context { get; set; }
    public ModelPackage? Package { get; set; }

    public bool HasConnection => ActiveProfile is not null && Provider is not null;
    public bool HasModel => Package is not null && Context is not null;

    public IModelProvider? Provider =>
        ActiveProfile is null ? null : App.ProviderRegistry.GetByKey(ActiveProfile.ProviderKey);

    public ProviderConnectionSettings BuildSettings()
    {
        if (ActiveProfile is null) return new ProviderConnectionSettings();

        return new ProviderConnectionSettings
        {
            ApiKey = App.ApiKeyStore.Get(ActiveProfile.Id),
            BaseUrl = ActiveProfile.BaseUrl,
            DeploymentName = ActiveProfile.DeploymentName
        };
    }

    public ModelPipeline CreatePipeline()
    {
        if (ActiveProfile is null || Provider is null)
            throw new InvalidOperationException("Aktif bağlantı profili yok.");

        return new ModelPipeline(Provider, BuildSettings(), ActiveProfile.ModelId);
    }
}
