using System.Collections.ObjectModel;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Runtime;
using DesktopPet.Application.Voice;

namespace DesktopPet.App.ViewModels;

public sealed class VoiceSettingsViewModel : ObservableViewModel, IDisposable
{
    private readonly ISpeechService _speech;
    private readonly ISettingsService _settings;
    private readonly ITextLocalizer _text;
    private readonly PetHost _pets;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _enabled, _autoRead, _silent, _focusSuppression, _onlineFallback;
    private string _providerId = VoiceProviderIds.Windows, _voiceId = string.Empty, _notice = string.Empty;
    private string _openAiModel = "gpt-4o-mini-tts";
    private double _speed = 1, _volume = 1;
    private bool _loading;

    public VoiceSettingsViewModel(ISpeechService speech, ISettingsService settings, ITextLocalizer text, PetHost pets)
    {
        _speech = speech; _settings = settings; _text = text; _pets = pets;
        SaveCommand = new(SaveAsync, exception => Notice = exception.Message);
        TestCommand = new(TestAsync, exception => Notice = exception.Message, () => Enabled && Voices.Count > 0);
        StopCommand = new(() => _speech.StopAsync(_lifetime.Token), exception => Notice = exception.Message, () => _speech.IsSpeaking);
        _speech.StateChanged += OnSpeechStateChanged;
        _text.CultureChanged += OnCultureChanged;
    }
    public ObservableCollection<TtsVoice> Voices { get; } = [];
    public IReadOnlyList<SettingOption<string>> Providers => _speech.ProviderIds.Select(id => new SettingOption<string>(id,
        id == VoiceProviderIds.Windows ? _text.Get(TextKey.VoiceProviderWindows) : _text.Get(TextKey.VoiceProviderOpenAI))).ToArray();
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); TestCommand.NotifyCanExecuteChanged(); } }
    public bool AutoRead { get => _autoRead; set { _autoRead = value; OnPropertyChanged(); } }
    public bool Silent { get => _silent; set { _silent = value; OnPropertyChanged(); } }
    public bool FocusSuppression { get => _focusSuppression; set { _focusSuppression = value; OnPropertyChanged(); } }
    public bool OnlineFallback { get => _onlineFallback; set { _onlineFallback = value; OnPropertyChanged(); } }
    public string ProviderId { get => _providerId; set { if (_providerId == value) return; _providerId = value; OnPropertyChanged(); if (!_loading) _ = RefreshVoicesSafeAsync(); } }
    public string VoiceId { get => _voiceId; set { _voiceId = value; OnPropertyChanged(); } }
    public double Speed { get => _speed; set { _speed = value; OnPropertyChanged(); } }
    public double Volume { get => _volume; set { _volume = value; OnPropertyChanged(); } }
    public string OpenAiModel { get => _openAiModel; set { _openAiModel = value; OnPropertyChanged(); } }
    public string Notice { get => _notice; private set { _notice = value; OnPropertyChanged(); } }
    public string Title => _text.Get(TextKey.VoiceSettings);
    public string EnableText => _text.Get(TextKey.VoiceEnable);
    public string AutoReadText => _text.Get(TextKey.VoiceAutoRead);
    public string SilentText => _text.Get(TextKey.VoiceSilent);
    public string FocusText => _text.Get(TextKey.VoiceFocusSuppression);
    public string FallbackText => _text.Get(TextKey.VoiceOnlineFallback);
    public string ProviderText => _text.Get(TextKey.VoiceProvider);
    public string VoiceText => _text.Get(TextKey.VoiceVoice);
    public string ModelText => _text.Get(TextKey.VoiceModel);
    public string SpeedText => _text.Get(TextKey.VoiceSpeed);
    public string VolumeText => _text.Get(TextKey.VoiceVolume);
    public string SaveText => _text.Get(TextKey.VoiceSave);
    public string TestText => _text.Get(TextKey.VoiceTest);
    public string StopText => _text.Get(TextKey.VoiceStop);
    public AsyncActionCommand SaveCommand { get; }
    public AsyncActionCommand TestCommand { get; }
    public AsyncActionCommand StopCommand { get; }

    public async Task InitializeAsync()
    {
        var value = _settings.Current.Voice;
        _loading = true;
        Enabled = value.Enabled; AutoRead = value.AutoReadAi; Silent = value.SilentMode;
        FocusSuppression = value.SuppressDuringFocus; OnlineFallback = value.OnlineFallback;
        ProviderId = VoiceProfilePolicy.ToId(value.Provider); VoiceId = value.VoiceId;
        Speed = value.Speed; Volume = value.Volume;
        OpenAiModel = value.OpenAiModel;
        _loading = false;
        await RefreshVoicesSafeAsync();
    }
    private async Task RefreshVoicesSafeAsync()
    {
        try
        {
            var selected = VoiceId;
            var voices = await _speech.GetVoicesAsync(ProviderId, _lifetime.Token);
            Voices.Clear(); foreach (var voice in voices) Voices.Add(voice);
            VoiceId = voices.Any(item => item.Id.Equals(selected, StringComparison.OrdinalIgnoreCase)) ? selected :
                voices.FirstOrDefault(item => item.IsDefault)?.Id ?? voices.FirstOrDefault()?.Id ?? string.Empty;
            TestCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { Notice = exception.Message; }
    }
    private async Task SaveAsync()
    {
        var provider = ProviderId == VoiceProviderIds.OpenAI ? TtsProviderKind.OpenAI : TtsProviderKind.Windows;
        await _settings.UpdateAsync(current => current with { Voice = current.Voice with
        {
            Enabled = Enabled, AutoReadAi = AutoRead, SilentMode = Silent, SuppressDuringFocus = FocusSuppression,
            OnlineFallback = OnlineFallback, Provider = provider, VoiceId = VoiceId, Speed = Speed, Volume = Volume, OpenAiModel = OpenAiModel,
            ProviderUserOverride = true, VoiceUserOverride = true, SpeedUserOverride = true, VolumeUserOverride = true
        } }, _lifetime.Token);
        Notice = _text.Get(TextKey.SettingsSaved);
    }
    private async Task TestAsync()
    {
        await SaveAsync();
        var current = _settings.Current.Voice;
        var profile = new VoiceProfile(ProviderId, VoiceId, Speed, Volume, current.OpenAiModel);
        var sample = _text.Get(TextKey.VoiceTestSample);
        var result = await _speech.SpeakAsync(new(_pets.Runtime.InstanceId, sample, _text.Culture.Name,
            SpeechOrigin.Manual, ExplicitVoice: profile), _lifetime.Token);
        Notice = _text.Get(result.Status == SpeechStatus.Completed ? TextKey.VoiceTestSucceeded : TextKey.VoiceTestFailed);
    }
    private void OnSpeechStateChanged(object? sender, EventArgs e) => StopCommand.NotifyCanExecuteChanged();
    private void OnCultureChanged(object? sender, EventArgs e) { OnPropertyChanged(string.Empty); OnPropertyChanged(nameof(Providers)); }
    public async Task StopAsync()
    {
        _lifetime.Cancel();
        await _speech.StopAsync(CancellationToken.None);
        await Task.WhenAll(SaveCommand.Completion, TestCommand.Completion, StopCommand.Completion);
    }
    public void Dispose()
    {
        _speech.StateChanged -= OnSpeechStateChanged; _text.CultureChanged -= OnCultureChanged;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}
