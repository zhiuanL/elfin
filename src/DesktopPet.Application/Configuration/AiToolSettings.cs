namespace DesktopPet.Application.Configuration;

public enum AiMediumConfirmationPreference { AlwaysAsk, AllowReversibleWithoutPrompt }

public sealed record AiToolSettings
{
    public bool Enabled { get; init; } = true;
    public AiMediumConfirmationPreference MediumConfirmation { get; init; } = AiMediumConfirmationPreference.AlwaysAsk;
    public IReadOnlyList<string> DisabledToolIds { get; init; } = [];
    public bool IsValid => Enum.IsDefined(MediumConfirmation) && DisabledToolIds is { Count: <= 64 } &&
        DisabledToolIds.All(IsValidToolId) && DisabledToolIds.Distinct(StringComparer.Ordinal).Count() == DisabledToolIds.Count;
    public bool Equals(AiToolSettings? other) => ReferenceEquals(this, other) || other is not null &&
        Enabled == other.Enabled && MediumConfirmation == other.MediumConfirmation &&
        (DisabledToolIds is null ? other.DisabledToolIds is null : other.DisabledToolIds is not null && DisabledToolIds.SequenceEqual(other.DisabledToolIds));
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Enabled); hash.Add(MediumConfirmation);
        if (DisabledToolIds is not null) foreach (var id in DisabledToolIds) hash.Add(id);
        return hash.ToHashCode();
    }
    private static bool IsValidToolId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsLetter(character) || char.IsDigit(character) || character is '.' or '-' or '_');
}
