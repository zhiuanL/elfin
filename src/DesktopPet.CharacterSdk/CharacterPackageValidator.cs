using System.Collections.ObjectModel;
using System.Text.Json;

namespace DesktopPet.CharacterSdk;

public sealed class CharacterPackageValidator : ICharacterPackageValidator
{
    public ValidationResult Validate(CharacterPackageContent content, CharacterValidationLimits limits)
    {
        CharacterManifest manifest;
        try
        {
            using var envelope = JsonDocument.Parse(content.ManifestJson, new JsonDocumentOptions { MaxDepth = 32 });
            if (envelope.RootElement.TryGetProperty("schemaVersion", out var schema) &&
                schema.TryGetInt32(out var number) && number != CharacterSchema.CurrentVersion)
                return ValidationResult.Reject(CharacterErrorCode.UnsupportedSchema, "manifest.json", "Unsupported character schema; no implicit downgrade.");
            manifest = CharacterSchema.Read<CharacterManifest>(content.ManifestJson);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { return ValidationResult.Reject(CharacterErrorCode.InvalidJson, "manifest.json", "Invalid, duplicate or unknown manifest fields."); }

        var issues = new List<ValidationIssue>();
        void Issue(CharacterErrorCode code, ValidationSeverity severity, string location, string expected, string actual, string message) =>
            issues.Add(new(code, severity, location.StartsWith('$') ? location : null,
                location.StartsWith('$') ? null : location, expected, actual, message, "Correct the field/resource or use a supported fallback."));
        void Fatal(CharacterErrorCode code, string path, string message) => Issue(code, ValidationSeverity.Fatal, path, "Valid value", "Invalid", message);
        if (!CharacterSchema.IsValidId(manifest.CharacterId) || !PackagePath.IsSafe(manifest.CharacterId)) Fatal(CharacterErrorCode.InvalidId, "$.id", "Use a lowercase dotted/hyphenated identifier, not a reserved device name.");
        if (manifest.SchemaVersion != CharacterSchema.CurrentVersion) Fatal(CharacterErrorCode.UnsupportedSchema, "$.schemaVersion", "Unsupported schema.");
        if (!CharacterSchema.IsVersion(manifest.PackageVersion, out _)) Fatal(CharacterErrorCode.InvalidVersion, "$.packageVersion", "Expected major.minor.patch.");
        if (!CharacterSchema.IsVersion(manifest.MinimumAppVersion, out var minimum)) Fatal(CharacterErrorCode.InvalidVersion, "$.minimumAppVersion", "Expected major.minor.patch.");
        else if (minimum > CharacterSchema.AppVersion) Fatal(CharacterErrorCode.AppTooOld, "$.minimumAppVersion", "This package requires a newer application.");
        if (manifest.Assets is null || manifest.Animations is null || manifest.Capabilities is null || manifest.Locales is null ||
            manifest.Profiles is null || manifest.Profiles.Persona is null || manifest.Profiles.Dialogue is null)
            return ValidationResult.Reject(CharacterErrorCode.InvalidJson, "manifest.json", "Required sections cannot be null.");
        manifest = manifest with { Profiles = ResolveProfiles(manifest.Profiles, content.Resources) };
        if (manifest.DefaultLocale is not ("zh-CN" or "en-US")) Fatal(CharacterErrorCode.InvalidJson, "$.defaultLocale", "Unsupported default locale.");
        foreach (var (locale, text) in manifest.Locales)
            if (locale is not ("zh-CN" or "en-US") || text is null || string.IsNullOrWhiteSpace(text.Name) ||
                text.Name.Length > 100 || text.Description is null || text.Description.Length > 4000)
                Fatal(CharacterErrorCode.InvalidJson, "$.locales", "Invalid localized text.");
        if (manifest.DefaultLocale is null || !manifest.Locales.ContainsKey(manifest.DefaultLocale))
            Issue(CharacterErrorCode.MissingLocalization, ValidationSeverity.Warning, "$.locales", manifest.DefaultLocale ?? "Supported locale", "Missing", "Name falls back to a supported locale or identifier.");

        bool Safe(string? path)
        {
            if (PackagePath.IsSafe(path)) return true;
            Fatal(CharacterErrorCode.InvalidPath, path ?? "$.resource", "Resource path is unsafe.");
            return false;
        }
        bool Image(string? path, bool required)
        {
            if (!Safe(path)) return false;
            var resource = content.Resources.GetValueOrDefault(path!);
            var severity = required ? ValidationSeverity.Fatal : ValidationSeverity.Error;
            if (resource is null) { Issue(CharacterErrorCode.MissingResource, severity, path!, "Existing PNG", "Missing", "Resource is absent."); return false; }
            if (!path!.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || resource.Image is not { IsValid: true } image ||
                image.Width <= 0 || image.Height <= 0 || image.Width > limits.MaxImageDimension || image.Height > limits.MaxImageDimension)
            { Issue(CharacterErrorCode.InvalidPng, severity, path!, "Valid bounded PNG", "Invalid", "Resource is not a valid supported PNG."); return false; }
            return true;
        }
        Image(manifest.Assets.Preview, true);
        Image(manifest.Assets.Fallback, true);
        var animations = new Dictionary<string, AnimationDefinition>(StringComparer.Ordinal);
        var frameTotal = 0;
        foreach (var (semantic, animation) in manifest.Animations)
        {
            var required = semantic == "idle";
            var location = "$.animations." + semantic;
            void AnimationError(string message) => Issue(CharacterErrorCode.InvalidAnimation, required ? ValidationSeverity.Fatal : ValidationSeverity.Error,
                location, "Valid supported frames", "Disabled", message);
            if (!CharacterSchema.IsSemantic(semantic) || animation is null) { AnimationError("Invalid semantic or null animation."); continue; }
            if (animation.Fallback is not null && !CharacterSchema.IsSemantic(animation.Fallback)) { AnimationError("Invalid fallback semantic."); continue; }
            if (animation.Path is not null && !Safe(animation.Path)) continue;
            if (animation.Fps is < 1 or > 60) { AnimationError("FPS must be between 1 and 60."); continue; }
            if (animation.Type is AnimationFormat.Layered2D or AnimationFormat.Live2D)
            {
                Issue(CharacterErrorCode.UnsupportedRenderer, required ? ValidationSeverity.Fatal : ValidationSeverity.Error,
                    location, "static/sequence", animation.Type.ToString(), "Renderer is reserved; animation is disabled.");
                continue;
            }
            IReadOnlyList<AnimationFrameDefinition> frames;
            if (animation.Type == AnimationFormat.StaticPng)
            {
                if (animation.Path is null || animation.Frames is not null) { AnimationError("Static animations require a PNG path and no frame list."); continue; }
                frames = [new(animation.Path, 1000)];
            }
            else if (animation.Frames is not null)
            {
                if (animation.Path is not null) { AnimationError("Choose explicit frames or a sequence directory, not both."); continue; }
                frames = animation.Frames;
            }
            else if (animation.Path is not null)
            {
                var prefix = animation.Path.TrimEnd('/') + "/";
                frames = content.Resources.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal) &&
                    !path[prefix.Length..].Contains('/') && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal).Select(path => new AnimationFrameDefinition(path)).ToArray();
            }
            else { AnimationError("Sequence has no frames or directory."); continue; }
            if (frames.Count == 0 || frames.Count > limits.MaxAnimationFrames) { AnimationError("Invalid sequence frame count."); continue; }
            var valid = true;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalized = new List<AnimationFrameDefinition>();
            foreach (var frame in frames)
            {
                if (frame is null) { valid = false; AnimationError("Null frame."); continue; }
                if (!seen.Add(frame.Path ?? string.Empty)) { valid = false; AnimationError("Duplicate frame reference; use a duration to hold a frame."); }
                if (!Image(frame.Path, required)) valid = false;
                var duration = frame.DurationMs ?? (int)Math.Round(1000.0 / animation.Fps);
                if (duration is < 1 or > 60000) { valid = false; AnimationError("Frame duration must be 1..60000 ms."); }
                normalized.Add(new(frame.Path!, duration));
            }
            if (!valid) continue;
            frameTotal += normalized.Count;
            animations.Add(semantic, animation with { Frames = normalized.AsReadOnly() });
        }
        if (!animations.ContainsKey("idle")) Fatal(CharacterErrorCode.MissingResource, "$.animations.idle", "At least one renderable idle is required.");
        if (frameTotal > limits.MaxAnimationFrames) Fatal(CharacterErrorCode.ResourceLimit, "$.animations", "Total normalized frames exceed the configured limit.");
        foreach (var resource in content.Resources.Values.Where(resource => resource.Image is { IsValid: false }))
            if (!issues.Any(issue => issue.ResourcePath == resource.Path))
                Issue(CharacterErrorCode.InvalidPng, ValidationSeverity.Warning, resource.Path, "Valid PNG", "Unused invalid image", "Unreferenced invalid image will not be rendered.");

        var present = new HashSet<CharacterCapability>();
        foreach (var (semantic, capability) in new[] { ("idle", CharacterCapability.Idle), ("blink", CharacterCapability.Blink),
            ("happy", CharacterCapability.Happy), ("rest", CharacterCapability.Rest), ("talking", CharacterCapability.Talking) })
            if (animations.ContainsKey(semantic)) present.Add(capability);

        bool Profile<T>(string? path, Func<T, bool> validate)
        {
            if (path is null) return false;
            if (!Safe(path)) return false;
            try
            {
                var json = content.Resources.GetValueOrDefault(path)?.Json;
                if (json is not null && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && validate(CharacterSchema.Read<T>(json))) return true;
            }
            catch (JsonException) { /* Optional malformed profile is diagnosed and disabled below. */ }
            Issue(CharacterErrorCode.InvalidProfile, ValidationSeverity.Error, path, "Valid typed profile", "Disabled", "Optional profile is missing, malformed or out of range.");
            return false;
        }
        bool LocalizedProfiles<T>(IReadOnlyDictionary<string, string> paths, Func<T, bool> validate)
        {
            var valid = false;
            foreach (var (locale, path) in paths)
            {
                if (locale is not ("zh-CN" or "en-US")) { Fatal(CharacterErrorCode.InvalidProfile, "$.profiles", "Unsupported profile locale."); continue; }
                valid |= Profile(path, validate);
            }
            return valid;
        }
        static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000;
        bool Semantic(string? value) => value is not null && animations.ContainsKey(value);
        if (LocalizedProfiles<PersonaProfile>(manifest.Profiles.Persona, p => Text(p.Summary) && p.Traits is not null && p.Traits.Count <= 50 && p.Traits.All(Text))) present.Add(CharacterCapability.Persona);
        if (LocalizedProfiles<DialogueProfile>(manifest.Profiles.Dialogue, p => p.Lines is { Count: > 0 and <= 100 } && p.Lines.All(pair =>
            CharacterSchema.IsSemantic(pair.Key) && pair.Value is { Count: > 0 and <= 100 } && pair.Value.All(Text)))) present.Add(CharacterCapability.Dialogue);
        if (Profile<EmotionProfile>(manifest.Profiles.EmotionMap, p => p.Animations is { Count: > 0 and <= 50 } &&
            p.Animations.All(pair => CharacterSchema.IsSemantic(pair.Key) && Semantic(pair.Value)))) present.Add(CharacterCapability.EmotionMap);
        if (Profile<HitAreaProfile>(manifest.Profiles.HitAreas, p => p.Areas is { Count: > 0 and <= 32 } && p.Areas.All(a => a is not null &&
            CharacterSchema.IsSemantic(a.Id) && Finite(a.X, 0, 1) && Finite(a.Y, 0, 1) && Finite(a.Width, .001, 1) &&
            Finite(a.Height, .001, 1) && a.X + a.Width <= 1 && a.Y + a.Height <= 1))) present.Add(CharacterCapability.HitAreas);
        if (Profile<BehaviorProfile>(manifest.Profiles.BehaviorProfile, p => p.Behaviors is { Count: > 0 and <= 100 } &&
            p.Behaviors.All(b => b is not null && Semantic(b.Animation) && Finite(b.Weight, 0, 10) && Finite(b.CooldownSeconds, 0, 3600)))) present.Add(CharacterCapability.BehaviorProfile);
        if (Profile<CharacterVoiceProfile>(manifest.Profiles.Voice, p => Text(p.Provider) && Text(p.Voice) && Finite(p.Speed, .5, 2) && Finite(p.Volume, 0, 1))) present.Add(CharacterCapability.TtsProfile);
        if (animations.ContainsKey("talking") && animations.ContainsKey("mouth-open") && animations.ContainsKey("mouth-closed")) present.Add(CharacterCapability.LipSync);

        var declared = manifest.Capabilities;
        foreach (var (claimed, capability) in new[] { (declared.Persona, CharacterCapability.Persona), (declared.Dialogue, CharacterCapability.Dialogue),
            (declared.EmotionMap, CharacterCapability.EmotionMap), (declared.HitAreas, CharacterCapability.HitAreas), (declared.BehaviorProfile, CharacterCapability.BehaviorProfile),
            (declared.TtsProfile, CharacterCapability.TtsProfile), (declared.LipSync, CharacterCapability.LipSync) })
            if (claimed != present.Contains(capability))
                Issue(CharacterErrorCode.CapabilityMismatch, claimed ? ValidationSeverity.Error : ValidationSeverity.Warning, "$.capabilities",
                    capability.ToString(), claimed ? "Missing/disabled" : "Undeclared", "Capability is derived from validated content, not the declaration.");
        if (declared.Layered2D || declared.Live2D)
            Issue(CharacterErrorCode.UnsupportedRenderer, ValidationSeverity.Warning, "$.capabilities", "PNG renderer", "Future renderer", "Reserved renderer capabilities are not enabled.");

        CharacterCapability[] standard = [CharacterCapability.Idle, CharacterCapability.Blink, CharacterCapability.Happy, CharacterCapability.Rest,
            CharacterCapability.Persona, CharacterCapability.Dialogue, CharacterCapability.EmotionMap];
        var all = Enum.GetValues<CharacterCapability>();
        var tier = all.All(present.Contains) ? CharacterTier.Full : standard.All(present.Contains) ? CharacterTier.Standard : CharacterTier.Basic;
        if (tier != manifest.TargetTier) Issue(CharacterErrorCode.TierMismatch, ValidationSeverity.Warning, "$.targetTier",
            tier.ToString(), manifest.TargetTier.ToString(), "Declared tier differs from verified content tier.");
        if (issues.Any(i => i.Severity == ValidationSeverity.Fatal)) return new(false, null, issues.AsReadOnly());
        var metadata = new CharacterPackageMetadata(manifest.TargetTier, tier, (3 + present.Count) * 100 / (3 + all.Length),
            Array.AsReadOnly(all.Except(present).ToArray()), Array.AsReadOnly(issues.Where(i => i.Severity == ValidationSeverity.Warning).ToArray()));
        return new(true, new(manifest, new ReadOnlyDictionary<string, AnimationDefinition>(animations), metadata), issues.AsReadOnly());
    }
    private static bool Finite(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    private static CharacterProfileReferences ResolveProfiles(CharacterProfileReferences profiles, IReadOnlyDictionary<string, CharacterResource> resources)
    {
        IReadOnlyDictionary<string, string> Localized(IReadOnlyDictionary<string, string> supplied, string directory)
        {
            var result = new Dictionary<string, string>(supplied, StringComparer.Ordinal);
            foreach (var locale in new[] { "zh-CN", "en-US" })
            {
                var path = $"{directory}/{locale}.json";
                if (!result.ContainsKey(locale) && resources.ContainsKey(path)) result.Add(locale, path);
            }
            return new ReadOnlyDictionary<string, string>(result);
        }
        string? Conventional(string? supplied, string path) => supplied ?? (resources.ContainsKey(path) ? path : null);
        return profiles with
        {
            Persona = Localized(profiles.Persona, "persona"), Dialogue = Localized(profiles.Dialogue, "locales"),
            EmotionMap = Conventional(profiles.EmotionMap, "emotion.json"), HitAreas = Conventional(profiles.HitAreas, "hitareas.json"),
            BehaviorProfile = Conventional(profiles.BehaviorProfile, "behavior.json"), Voice = Conventional(profiles.Voice, "voice.json")
        };
    }
}
