using System.Collections.ObjectModel;
using System.Windows.Input;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;

namespace DesktopPet.App.ViewModels;

public sealed record AiMessageItem(ChatRole Role, string Content, MessageStatus Status)
{ public string Header => $"{Role} · {Status}"; }

public sealed class AiViewModel : ObservableViewModel, IDisposable
{
    private readonly IAiChatService _chat; private readonly IAiProviderService _providers; private readonly IMemoryService _memories;
    private readonly PetHost _pets; private readonly ITextLocalizer _text; private readonly CancellationTokenSource _lifetime = new();
    private bool _initialized, _busy, _autoMemory, _saveKey = true, _loadingProvider; private string _input = "", _lastInput = "", _notice = "";
    private Conversation? _conversation; private AiProviderProfile? _profile; private MemoryItem? _memory;
    private string _providerName = "", _baseUrl = "", _model = "", _apiKey = "", _memoryContent = "", _memoryTags = "", _memoryKeywords = "";
    private AiProviderType _providerType; private MemoryCategory _memoryCategory; private int _timeoutSeconds = 30, _memoryImportance = 3;
    public AiViewModel(IAiChatService chat, IAiProviderService providers, IMemoryService memories, PetHost pets, ITextLocalizer text)
    {
        _chat = chat; _providers = providers; _memories = memories; _pets = pets; _text = text;
        SendCommand = Command(SendAsync, () => !Busy && !string.IsNullOrWhiteSpace(Input)); StopCommand = Command(() => _chat.StopAsync(_lifetime.Token), () => Busy);
        RetryCommand = Command(RetryAsync, () => !Busy && !string.IsNullOrWhiteSpace(_lastInput)); NewTemporaryCommand = Command(() => CreateConversationAsync(ConversationType.Temporary)); NewTopicCommand = Command(() => CreateConversationAsync(ConversationType.Topic));
        NewProviderCommand = Command(() => { ClearProviderEditor(); return Task.CompletedTask; }); FetchModelsCommand = Command(FetchModelsAsync, CanFetchModels); SaveProviderCommand = Command(SaveProviderAsync); TestProviderCommand = Command(TestProviderAsync, () => SelectedProvider is not null); SetActiveCommand = Command(SetActiveAsync, () => SelectedProvider is not null); DeleteProviderCommand = Command(DeleteProviderAsync, () => SelectedProvider is not null);
        SaveMemoryCommand = Command(SaveMemoryAsync); DeleteMemoryCommand = Command(DeleteMemoryAsync, () => SelectedMemory is not null); ClearCharacterMemoriesCommand = Command(ClearCharacterMemoriesAsync); ClearAllMemoriesCommand = Command(ClearAllMemoriesAsync);
        ApplyProviderDefaults();
        _text.CultureChanged += OnCultureChanged;
    }
    public ObservableCollection<Conversation> Conversations { get; } = []; public ObservableCollection<AiMessageItem> Messages { get; } = [];
    public ObservableCollection<AiProviderProfile> ProviderProfiles { get; } = []; public ObservableCollection<MemoryItem> Memories { get; } = [];
    public ObservableCollection<string> AvailableModels { get; } = [];
    public IReadOnlyList<AiProviderType> ProviderTypes { get; } = Enum.GetValues<AiProviderType>(); public IReadOnlyList<MemoryCategory> MemoryCategories { get; } = Enum.GetValues<MemoryCategory>();
    public string Title => _text.Get(TextKey.AiTitle); public string Subtitle => _text.Get(TextKey.AiSubtitle);
    public string SetupTitle => ProviderProfiles.Count == 0 ? _text.Get(TextKey.AiSetupRequired) : _text.Get(TextKey.AiProviderSettings);
    public string ConversationsText => _text.Get(TextKey.AiConversations); public string MessagesText => _text.Get(TextKey.AiMessages);
    public string InputText => _text.Get(TextKey.AiInput); public string SendText => _text.Get(TextKey.AiSend); public string StopText => _text.Get(TextKey.AiStop); public string RetryText => _text.Get(TextKey.AiRetry);
    public string NewTemporaryText => _text.Get(TextKey.AiNewTemporary); public string NewTopicText => _text.Get(TextKey.AiNewTopic); public string MemoryText => _text.Get(TextKey.AiMemory);
    public string ProviderText => _text.Get(TextKey.AiProvider); public string DisplayNameText => _text.Get(TextKey.AiDisplayName); public string BaseUrlText => _text.Get(TextKey.AiBaseUrl); public string ModelText => _text.Get(TextKey.AiModel); public string ApiKeyText => _text.Get(TextKey.AiApiKey); public string SaveKeyText => _text.Get(TextKey.AiSaveKey); public string TimeoutText => _text.Get(TextKey.AiTimeout);
    public string NewProviderText => _text.Get(TextKey.AiNewProvider); public string FetchModelsText => _text.Get(TextKey.AiFetchModels); public string SaveProviderText => _text.Get(TextKey.AiSaveProvider); public string TestConnectionText => _text.Get(TextKey.AiTestConnection); public string SetActiveText => _text.Get(TextKey.AiSetActive); public string DeleteProviderText => _text.Get(TextKey.AiDeleteProvider);
    public string AutoMemoryText => _text.Get(TextKey.AiAutoMemory); public string MemoryContentText => _text.Get(TextKey.AiMemoryContent); public string CategoryText => _text.Get(TextKey.AiCategory); public string ImportanceText => _text.Get(TextKey.AiImportance); public string TagsText => _text.Get(TextKey.AiTags); public string KeywordsText => _text.Get(TextKey.AiKeywords); public string SaveMemoryText => _text.Get(TextKey.AiSaveMemory); public string DeleteMemoryText => _text.Get(TextKey.AiDeleteMemory); public string ClearCharacterText => _text.Get(TextKey.AiClearCharacter); public string ClearAllText => _text.Get(TextKey.AiClearAll);
    public string Input { get => _input; set { _input = value; OnPropertyChanged(); NotifyCommands(); } }
    public string Notice { get => _notice; private set { _notice = value; OnPropertyChanged(); } }
    public bool Busy { get => _busy; private set { _busy = value; OnPropertyChanged(); NotifyCommands(); } }
    public Conversation? SelectedConversation { get => _conversation; set { if (_conversation == value) return; _conversation = value; OnPropertyChanged(); _ = SwitchConversationAsync(); } }
    public AiProviderProfile? SelectedProvider { get => _profile; set { if (_profile == value) return; _profile = value; OnPropertyChanged(); LoadProviderEditor(); NotifyCommands(); } }
    public MemoryItem? SelectedMemory { get => _memory; set { _memory = value; OnPropertyChanged(); if (value is not null) { MemoryContent = value.Content; MemoryCategory = value.Category; MemoryImportance = value.Importance; MemoryTags = string.Join(", ", value.Tags); MemoryKeywords = string.Join(", ", value.Keywords); } NotifyCommands(); } }
    public AiProviderType ProviderType { get => _providerType; set { if (_providerType == value && !_loadingProvider) return; _providerType = value; OnPropertyChanged(); if (!_loadingProvider) ApplyProviderDefaults(); } }
    public string ProviderName { get => _providerName; set { _providerName = value; OnPropertyChanged(); } } public string BaseUrl { get => _baseUrl; set { _baseUrl = value; OnPropertyChanged(); FetchModelsCommand.NotifyCanExecuteChanged(); } }
    public string Model { get => _model; set { _model = value; OnPropertyChanged(); } } public string ApiKey { get => _apiKey; set { _apiKey = value; OnPropertyChanged(); FetchModelsCommand.NotifyCanExecuteChanged(); } }
    public int TimeoutSeconds { get => _timeoutSeconds; set { _timeoutSeconds = value; OnPropertyChanged(); } } public bool SaveKey { get => _saveKey; set { _saveKey = value; OnPropertyChanged(); } }
    public bool AutoMemory { get => _autoMemory; set { if (_autoMemory == value) return; _autoMemory = value; OnPropertyChanged(); _ = SetAutoMemoryAsync(value); } }
    public string MemoryContent { get => _memoryContent; set { _memoryContent = value; OnPropertyChanged(); } } public MemoryCategory MemoryCategory { get => _memoryCategory; set { _memoryCategory = value; OnPropertyChanged(); } }
    public int MemoryImportance { get => _memoryImportance; set { _memoryImportance = value; OnPropertyChanged(); } } public string MemoryTags { get => _memoryTags; set { _memoryTags = value; OnPropertyChanged(); } } public string MemoryKeywords { get => _memoryKeywords; set { _memoryKeywords = value; OnPropertyChanged(); } }
    public ICommand SendCommand { get; } public ICommand StopCommand { get; } public ICommand RetryCommand { get; } public ICommand NewTemporaryCommand { get; } public ICommand NewTopicCommand { get; }
    public AsyncActionCommand FetchModelsCommand { get; } public ICommand NewProviderCommand { get; } public ICommand SaveProviderCommand { get; } public ICommand TestProviderCommand { get; } public ICommand SetActiveCommand { get; } public ICommand DeleteProviderCommand { get; }
    public ICommand SaveMemoryCommand { get; } public ICommand DeleteMemoryCommand { get; } public ICommand ClearCharacterMemoriesCommand { get; } public ICommand ClearAllMemoriesCommand { get; }
    public async Task InitializeAsync()
    {
        if (!_initialized) { _initialized = true; await RefreshProvidersAsync(); }
        await RefreshCharacterSpaceAsync();
    }
    public async Task RefreshCharacterSpaceAsync()
    {
        if (_pets.Runtime.Current?.Definition.Id is not { } character) return;
        await _chat.StopAsync(_lifetime.Token); var main = await _chat.GetMainAsync(character, _lifetime.Token);
        Replace(Conversations, await _chat.ListAsync(character, _lifetime.Token)); SelectedConversation = Conversations.FirstOrDefault(x => x.Id == main.Id) ?? main;
        Replace(Memories, await _memories.ListAsync(character, _lifetime.Token)); _autoMemory = await _memories.GetAutoEnabledAsync(character, _lifetime.Token); OnPropertyChanged(nameof(AutoMemory));
    }
    private async Task SwitchConversationAsync()
    { try { await _chat.StopAsync(_lifetime.Token); await LoadMessagesAsync(); } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { } catch (Exception e) { Notice = e.Message; } }
    private async Task LoadMessagesAsync() { Messages.Clear(); if (SelectedConversation is null) return; foreach (var item in await _chat.MessagesAsync(SelectedConversation.Id, _lifetime.Token)) Messages.Add(new(item.Role, item.Content, item.Status)); }
    private async Task SendAsync() { var value = Input.Trim(); Input = ""; _lastInput = value; await SendValueAsync(value); }
    private Task RetryAsync() => SendValueAsync(_lastInput);
    private async Task SendValueAsync(string value)
    {
        if (SelectedConversation is null) return; Busy = true; Notice = ""; Messages.Add(new(ChatRole.User, value, MessageStatus.Complete)); var partial = "";
        try { await foreach (var delta in _chat.SendAsync(SelectedConversation.Id, value, _lifetime.Token)) { partial += delta.Text; if (Messages.LastOrDefault()?.Role == ChatRole.Assistant) Messages.RemoveAt(Messages.Count - 1); Messages.Add(new(ChatRole.Assistant, partial, delta.Status)); } }
        catch (OperationCanceledException) { Notice = _text.Get(TextKey.AiGenerationStopped); }
        catch (Exception exception) { Notice = exception.Message; }
        finally { Busy = false; await LoadMessagesAsync(); }
    }
    private async Task CreateConversationAsync(ConversationType type)
    { var character = CurrentCharacter(); await _chat.StopAsync(_lifetime.Token); var item = await _chat.CreateAsync(character, type, type.ToString(), _lifetime.Token); await RefreshConversationsAsync(character); SelectedConversation = Conversations.First(x => x.Id == item.Id); }
    private async Task RefreshConversationsAsync(CharacterId character) => Replace(Conversations, await _chat.ListAsync(character, _lifetime.Token));
    private async Task RefreshProvidersAsync() { Replace(ProviderProfiles, await _providers.ListAsync(_lifetime.Token)); SelectedProvider = ProviderProfiles.FirstOrDefault(x => x.IsActive) ?? ProviderProfiles.FirstOrDefault(); OnPropertyChanged(nameof(SetupTitle)); }
    private async Task SaveProviderAsync()
    { Uri? url = string.IsNullOrWhiteSpace(BaseUrl) ? null : new Uri(BaseUrl, UriKind.Absolute); var now = DateTimeOffset.UtcNow; var item = new AiProviderProfile(SelectedProvider?.Id ?? Guid.NewGuid(), ProviderType, ProviderName.Trim(), url, Model.Trim(), TimeSpan.FromSeconds(TimeoutSeconds), SelectedProvider?.SecretReference, SelectedProvider?.IsActive ?? ProviderProfiles.Count == 0, SelectedProvider?.CreatedAtUtc ?? now, now); await _providers.SaveAsync(item, ApiKey.AsMemory(), SaveKey ? CredentialPersistence.Saved : CredentialPersistence.SessionOnly, _lifetime.Token); ApiKey = ""; await RefreshProvidersAsync(); Notice = _text.Get(TextKey.AiProviderSaved); }
    private async Task TestProviderAsync() { if (SelectedProvider is null) return; var result = await _providers.TestAsync(SelectedProvider.Id, _lifetime.Token); Notice = $"{_text.Get(TextKey.AiTestConnection)}: {result.Status}"; }
    private bool CanFetchModels() => (SelectedProvider?.SecretReference is not null || !string.IsNullOrWhiteSpace(ApiKey))
        && Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);
    private async Task FetchModelsAsync()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url)) { Notice = _text.Get(TextKey.AiModelsFailed); return; }
        var result = await _providers.DiscoverModelsAsync(SelectedProvider?.Id, ProviderType, url,
            TimeSpan.FromSeconds(TimeoutSeconds), ApiKey.AsMemory(), _lifetime.Token);
        if (!result.Succeeded) { Notice = $"{_text.Get(TextKey.AiModelsFailed)}: {result.Status}"; return; }
        var selected = Model; Replace(AvailableModels, result.Models);
        Model = result.Models.Contains(selected, StringComparer.Ordinal) ? selected : result.Models[0];
        Notice = $"{_text.Get(TextKey.AiModelsLoaded)}: {result.Models.Count}";
    }
    private async Task SetActiveAsync() { if (SelectedProvider is null) return; await _chat.StopAsync(_lifetime.Token); await _providers.SetActiveAsync(SelectedProvider.Id, _lifetime.Token); await RefreshProvidersAsync(); }
    private async Task DeleteProviderAsync() { if (SelectedProvider is null) return; await _chat.StopAsync(_lifetime.Token); await _providers.DeleteAsync(SelectedProvider.Id, _lifetime.Token); ClearProviderEditor(); await RefreshProvidersAsync(); }
    private async Task SaveMemoryAsync() { var character = CurrentCharacter(); var draft = new MemoryDraft(MemoryCategory, MemoryContent, MemoryImportance, Split(MemoryTags), Split(MemoryKeywords), Id: SelectedMemory?.Id); await _memories.SaveAsync(character, draft, _lifetime.Token); SelectedMemory = null; MemoryContent = ""; Replace(Memories, await _memories.ListAsync(character, _lifetime.Token)); }
    private async Task DeleteMemoryAsync() { if (SelectedMemory is null) return; await _memories.DeleteAsync(SelectedMemory.Id, _lifetime.Token); SelectedMemory = null; Replace(Memories, await _memories.ListAsync(CurrentCharacter(), _lifetime.Token)); }
    private async Task ClearCharacterMemoriesAsync() { var id = CurrentCharacter(); await _memories.ClearCharacterAsync(id, _lifetime.Token); Memories.Clear(); }
    private async Task ClearAllMemoriesAsync() { await _memories.ClearAllAsync(_lifetime.Token); Memories.Clear(); }
    private async Task SetAutoMemoryAsync(bool value) { if (!_initialized || _pets.Runtime.Current is null) return; try { await _memories.SetAutoEnabledAsync(CurrentCharacter(), value, _lifetime.Token); } catch (Exception e) { Notice = e.Message; } }
    private CharacterId CurrentCharacter() => _pets.Runtime.Current?.Definition.Id ?? throw new InvalidOperationException("No active character.");
    private void LoadProviderEditor() { if (SelectedProvider is null) return; _loadingProvider = true; try { ProviderType = SelectedProvider.ProviderType; ProviderName = SelectedProvider.DisplayName; BaseUrl = SelectedProvider.BaseUrl?.ToString() ?? AiProviderDefaults.SuggestedBaseUrl(ProviderType); Model = SelectedProvider.Model; Replace(AvailableModels, string.IsNullOrWhiteSpace(Model) ? [] : [Model]); TimeoutSeconds = (int)SelectedProvider.Timeout.TotalSeconds; ApiKey = ""; } finally { _loadingProvider = false; } }
    private void ClearProviderEditor() { SelectedProvider = null; AvailableModels.Clear(); ApiKey = ""; TimeoutSeconds = 30; ApplyProviderDefaults(); }
    private void ApplyProviderDefaults() { ProviderName = ProviderType.ToString(); BaseUrl = AiProviderDefaults.SuggestedBaseUrl(ProviderType); Model = ""; AvailableModels.Clear(); }
    private static string[] Split(string value) => value.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private AsyncActionCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(action, e => Notice = e.Message, enabled);
    private void NotifyCommands() { foreach (var command in new[] { SendCommand, StopCommand, RetryCommand, TestProviderCommand, SetActiveCommand, DeleteProviderCommand, DeleteMemoryCommand }.OfType<AsyncActionCommand>()) command.NotifyCanExecuteChanged(); }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source) { target.Clear(); foreach (var item in source) target.Add(item); }
    private void OnCultureChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
    public async Task StopAsync() { _lifetime.Cancel(); await _chat.StopAsync(CancellationToken.None); }
    public void Dispose() { _text.CultureChanged -= OnCultureChanged; _lifetime.Cancel(); _lifetime.Dispose(); }
}
