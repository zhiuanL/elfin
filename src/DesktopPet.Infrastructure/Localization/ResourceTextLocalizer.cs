using System.Globalization;
using System.Resources;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Localization;

namespace DesktopPet.Infrastructure.Localization;

public sealed class ResourceTextLocalizer : ITextLocalizer
{
    private readonly ResourceManager _resources = new("DesktopPet.Infrastructure.Localization.Strings",
        typeof(ResourceTextLocalizer).Assembly);
    private readonly ISettingsService? _settings;
    private string _standaloneCulture;
    public ResourceTextLocalizer(ISettingsService settings)
    {
        _settings = settings;
        _standaloneCulture = settings.Current.Culture;
    }
    public ResourceTextLocalizer(string culture) => _standaloneCulture = Validate(culture);
    public event EventHandler? CultureChanged;
    private string CultureName => _settings?.Current.Culture ?? _standaloneCulture;
    public CultureInfo Culture => CultureInfo.GetCultureInfo(CultureName);
    public string Get(TextKey key) => _resources.GetString(key.ToString(), Culture) ??
        _resources.GetString(key.ToString(), CultureInfo.GetCultureInfo("en-US")) ?? key.ToString();
    public async Task SetCultureAsync(string culture, CancellationToken ct)
    {
        culture = Validate(culture);
        if (_settings is not null) await _settings.UpdateAsync(current => current with { Culture = culture }, ct);
        else _standaloneCulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
    private static string Validate(string culture) => culture is "zh-CN" or "en-US"
        ? culture : throw new ArgumentOutOfRangeException(nameof(culture));
}
