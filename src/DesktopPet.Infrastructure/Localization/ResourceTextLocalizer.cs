using System.Globalization;
using System.Resources;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Localization;

namespace DesktopPet.Infrastructure.Localization;

public sealed class ResourceTextLocalizer : ITextLocalizer
{
    private readonly ResourceManager _resources = new("DesktopPet.Infrastructure.Localization.Strings",
        typeof(ResourceTextLocalizer).Assembly);
    private readonly Func<string> _getCulture;
    public ResourceTextLocalizer(ISettingsService settings) => _getCulture = () => settings.Current.Culture;
    public ResourceTextLocalizer(string culture) => _getCulture = () => culture;
    public CultureInfo Culture => CultureInfo.GetCultureInfo(_getCulture());
    public string Get(TextKey key) => _resources.GetString(key.ToString(), Culture) ??
        throw new InvalidOperationException($"Missing resource: {key}.");
}
