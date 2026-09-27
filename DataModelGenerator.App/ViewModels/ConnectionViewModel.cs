using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataModelGenerator.App.Dialogs;
using DataModelGenerator.Core.Models;
using DataModelGenerator.Core.Providers;
using DataModelGenerator.Core.Services;

namespace DataModelGenerator.App.ViewModels;

public partial class ConnectionViewModel : ObservableObject, IRefreshable
{
    private readonly SessionState _session;
    private readonly MainViewModel _main;

    [ObservableProperty] private ObservableCollection<ConnectionProfile> _profiles = new();
    [ObservableProperty] private ConnectionProfile? _selectedProfile;
    [ObservableProperty] private ObservableCollection<IModelProvider> _providers = new();

    [ObservableProperty] private string _profileName = string.Empty;
    [ObservableProperty] private IModelProvider? _selectedProvider;
    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _modelId = string.Empty;
    [ObservableProperty] private string _deploymentName = string.Empty;

    [ObservableProperty] private ObservableCollection<ModelInfo> _availableModels = new();
    [ObservableProperty] private ModelInfo? _selectedModel;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _testSucceeded;
    [ObservableProperty] private string _sampleCompletion = string.Empty;

    public bool ApiKeyRequired => SelectedProvider?.RequiresApiKey ?? false;
    public bool ShowBaseUrl => SelectedProvider?.RequiresBaseUrl ?? false;
    public bool ShowDeployment => SelectedProvider?.ProviderKey == "azure-openai";
    public string ApiKeyCaption => ApiKeyRequired
        ? "API Anahtarı (zorunlu)"
        : "API Anahtarı (offline model için opsiyonel)";

    public bool CanSave => TestSucceeded && !string.IsNullOrWhiteSpace(ProfileName);
    public bool CanUse => SelectedProfile is not null && !string.IsNullOrWhiteSpace(SelectedProfile.ModelId);

    public ConnectionViewModel(SessionState session, MainViewModel main)
    {
        _session = session;
        _main = main;

        foreach (var provider in App.ProviderRegistry.All)
            Providers.Add(provider);

        SelectedProvider = Providers.FirstOrDefault();
        ReloadProfiles();

        var activeId = App.SettingsStore.Settings.ActiveProfileId;
        if (!string.IsNullOrEmpty(activeId))
        {
            var active = Profiles.FirstOrDefault(p => p.Id == activeId);
            if (active is not null)
            {
                SelectedProfile = active;
                _session.ActiveProfile = active;
            }
        }

        _main.UpdateConnectionStatus();
    }

    public void Refresh() => _main.UpdateConnectionStatus();

    private void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var profile in App.ProfileStore.Profiles)
            Profiles.Add(profile);
    }

    partial void OnSelectedProviderChanged(IModelProvider? value)
    {
        OnPropertyChanged(nameof(ApiKeyRequired));
        OnPropertyChanged(nameof(ShowBaseUrl));
        OnPropertyChanged(nameof(ShowDeployment));
        OnPropertyChanged(nameof(ApiKeyCaption));

        TestSucceeded = false;
        OnPropertyChanged(nameof(CanSave));

        if (value is null) return;

        // Canlı listeleme API anahtarı ister; bilinen modeller anahtar girilmeden seçilebilsin.
        var previousModelId = ModelId;
        AvailableModels.Clear();
        foreach (var model in value.KnownModels)
            AvailableModels.Add(model);

        SelectedModel = AvailableModels.FirstOrDefault(m => m.Id == previousModelId)
                        ?? AvailableModels.FirstOrDefault();

        if (SelectedModel is null) ModelId = string.Empty;

        if (string.IsNullOrWhiteSpace(BaseUrl) && value.ProviderKey == "ollama")
            BaseUrl = "http://localhost:11434";
    }

    partial void OnProfileNameChanged(string value) => OnPropertyChanged(nameof(CanSave));

    partial void OnTestSucceededChanged(bool value) => OnPropertyChanged(nameof(CanSave));

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        OnPropertyChanged(nameof(CanUse));
        if (value is null) return;

        ProfileName = value.Name;
        SelectedProvider = Providers.FirstOrDefault(p => p.ProviderKey == value.ProviderKey) ?? SelectedProvider;
        BaseUrl = value.BaseUrl;
        ModelId = value.ModelId;
        DeploymentName = value.DeploymentName;
        ApiKey = App.ApiKeyStore.Get(value.Id);

        AvailableModels.Clear();
        foreach (var model in SelectedProvider?.KnownModels ?? [])
            AvailableModels.Add(model);

        if (!string.IsNullOrWhiteSpace(value.ModelId))
        {
            var known = AvailableModels.FirstOrDefault(m => m.Id == value.ModelId);
            if (known is null)
            {
                known = new ModelInfo(value.ModelId, value.ModelId);
                AvailableModels.Insert(0, known);
            }
            SelectedModel = known;
            ModelId = value.ModelId;
        }

        TestSucceeded = value.LastTestSucceeded == true;
        StatusMessage = value.LastTestMessage;
        StatusIsError = value.LastTestSucceeded == false;
    }

    partial void OnSelectedModelChanged(ModelInfo? value)
    {
        if (value is not null) ModelId = value.Id;
    }

    [RelayCommand]
    private void NewProfile()
    {
        SelectedProfile = null;
        ProfileName = string.Empty;
        BaseUrl = string.Empty;
        ApiKey = string.Empty;
        ModelId = string.Empty;
        DeploymentName = string.Empty;
        AvailableModels.Clear();
        SelectedModel = null;
        TestSucceeded = false;
        StatusMessage = string.Empty;
        SampleCompletion = string.Empty;
    }

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        if (SelectedProvider is null) return;

        // Canlı listeleme sağlayıcıda kimlik doğrulaması gerektirir.
        if (SelectedProvider.RequiresApiKey && string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = $"Canlı model listesi için {SelectedProvider.DisplayName} API anahtarı gerekir. " +
                            "Anahtar girmeden aşağıdaki hazır listeden seçim yapabilir veya model adını el ile yazabilirsiniz.";
            StatusIsError = true;
            return;
        }

        if (SelectedProvider.RequiresBaseUrl && string.IsNullOrWhiteSpace(BaseUrl))
        {
            StatusMessage = "Önce endpoint URL girin.";
            StatusIsError = true;
            return;
        }

        IsBusy = true;
        StatusMessage = "Model listesi alınıyor…";
        StatusIsError = false;

        try
        {
            var models = await SelectedProvider.ListModelsAsync(BuildSettings());
            AvailableModels.Clear();
            foreach (var model in models) AvailableModels.Add(model);

            if (models.Count == 0)
            {
                StatusMessage = "Model listesi boş döndü. Model adını el ile girebilirsiniz.";
                StatusIsError = true;
            }
            else
            {
                SelectedModel = AvailableModels.FirstOrDefault(m => m.Id == ModelId) ?? AvailableModels.First();
                StatusMessage = $"{models.Count} model yüklendi.";
            }
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"{ModelProviderBase.DescribeHttpError(ex)} Model adını el ile de girebilirsiniz.";
            StatusIsError = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Model listesi alınamadı: {ex.Message}. Model adını el ile girebilirsiniz.";
            StatusIsError = true;
            ApiLogDialog.Show("Model Listesi Alınamadı", ex, ApiLogger.LastEntry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (SelectedProvider is null) return;

        IsBusy = true;
        TestSucceeded = false;
        SampleCompletion = string.Empty;
        StatusMessage = "Bağlantı test ediliyor — modele ufak bir completion isteği gönderiliyor…";
        StatusIsError = false;

        try
        {
            var result = await SelectedProvider.TestConnectionAsync(BuildSettings(), ModelId.Trim());

            TestSucceeded = result.Success;
            StatusIsError = !result.Success;
            StatusMessage = result.Success
                ? $"{result.Message} Profili kaydedebilirsiniz."
                : result.Message;

            if (result.Success)
            {
                SampleCompletion = $"Model yanıtı: {Truncate(result.SampleCompletion ?? string.Empty, 200)}";
            }
            else
            {
                ApiLogDialog.Show("Bağlantı Testi Başarısız", result.Message, ApiLogger.LastEntry);
            }

            if (SelectedProfile is not null)
            {
                SelectedProfile.LastTestSucceeded = result.Success;
                SelectedProfile.LastTestedAt = DateTime.Now;
                SelectedProfile.LastTestMessage = result.Message;
                App.ProfileStore.Save(SelectedProfile);
                _main.UpdateConnectionStatus();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Beklenmedik hata: {ex.Message}";
            StatusIsError = true;
            ApiLogDialog.Show("Bağlantı Testi Başarısız", ex, ApiLogger.LastEntry);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProvider is null || !CanSave) return;

        var profile = SelectedProfile ?? new ConnectionProfile();
        profile.Name = ProfileName.Trim();
        profile.ProviderKey = SelectedProvider.ProviderKey;
        profile.BaseUrl = BaseUrl.Trim();
        profile.ModelId = ModelId.Trim();
        profile.DeploymentName = DeploymentName.Trim();
        profile.LastTestSucceeded = true;
        profile.LastTestedAt = DateTime.Now;
        profile.LastTestMessage = "Bağlantı testi başarılı.";

        App.ProfileStore.Save(profile);
        App.ApiKeyStore.Save(profile.Id, ApiKey);

        ReloadProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);

        StatusMessage = $"✓ '{profile.Name}' profili kaydedildi. API anahtarı şifreli olarak saklandı (DPAPI).";
        StatusIsError = false;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;

        var confirm = System.Windows.MessageBox.Show(
            $"'{SelectedProfile.Name}' profili silinsin mi?", "Profili Sil",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        var id = SelectedProfile.Id;
        App.ProfileStore.Delete(id);
        App.ApiKeyStore.Delete(id);

        if (_session.ActiveProfile?.Id == id)
        {
            _session.ActiveProfile = null;
            App.SettingsStore.Settings.ActiveProfileId = string.Empty;
            App.SettingsStore.Save();
            _main.UpdateConnectionStatus();
        }

        ReloadProfiles();
        NewProfile();
        StatusMessage = "Profil silindi.";
        StatusIsError = false;
    }

    [RelayCommand]
    private void UseProfile()
    {
        if (SelectedProfile is null) return;

        _session.ActiveProfile = SelectedProfile;
        App.SettingsStore.Settings.ActiveProfileId = SelectedProfile.Id;
        App.SettingsStore.Save();

        _main.UpdateConnectionStatus();
        _main.SetStatus($"Aktif profil: {SelectedProfile.Name}");
        _main.NavigateNext();
    }

    private ProviderConnectionSettings BuildSettings() => new()
    {
        ApiKey = ApiKey.Trim(),
        BaseUrl = BaseUrl.Trim(),
        DeploymentName = DeploymentName.Trim()
    };

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length] + "…";
}
