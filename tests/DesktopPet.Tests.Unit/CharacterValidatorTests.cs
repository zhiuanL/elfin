using System.Text.Json;
using DesktopPet.CharacterSdk;

namespace DesktopPet.Tests.Unit;

public sealed class CharacterValidatorTests
{
    internal static CharacterManifest Basic => new()
    {
        SchemaVersion = 1, PackageVersion = "1.0.0", MinimumAppVersion = "0.2.0", CharacterId = "tests.basic",
        Assets = new("preview.png", "fallback.png"),
        Animations = new Dictionary<string, AnimationDefinition> { ["idle"] = new() { Type = AnimationFormat.StaticPng, Path = "idle.png" } }
    };
    internal static Dictionary<string, CharacterResource> Resources => new(StringComparer.Ordinal)
    {
        ["preview.png"] = new("preview.png", 100, new(256, 256, true)),
        ["fallback.png"] = new("fallback.png", 100, new(256, 256, true)),
        ["idle.png"] = new("idle.png", 100, new(256, 256, true))
    };
    internal static ValidationResult Validate(CharacterManifest manifest, Dictionary<string, CharacterResource>? resources = null) =>
        new CharacterPackageValidator().Validate(new(JsonSerializer.Serialize(manifest, CharacterSchema.JsonOptions()), resources ?? Resources), new());

    [Fact]
    public void BasicIsRunnableRegardlessOfClaimedFullTier()
    {
        var result = Validate(Basic with { TargetTier = CharacterTier.Full, Capabilities = new() { Persona = true, LipSync = true } });
        Assert.True(result.CanInstall);
        Assert.Equal(CharacterTier.Basic, result.ActualLevel);
        Assert.Equal(26, result.CompletenessPercentage);
        Assert.Contains(CharacterCapability.Persona, result.MissingCapabilities);
        Assert.Contains(result.Issues, i => i.ErrorCode == CharacterErrorCode.CapabilityMismatch && i.Severity == ValidationSeverity.Error);
        Assert.Contains(result.Warnings, i => i.ErrorCode == CharacterErrorCode.TierMismatch);
    }
    [Theory]
    [InlineData("preview.png")]
    [InlineData("fallback.png")]
    [InlineData("idle.png")]
    public void RequiredMissingOrBrokenPngRefusesInstallation(string path)
    {
        var resources = Resources;
        resources.Remove(path);
        Assert.False(Validate(Basic, resources).CanInstall);
        resources[path] = new(path, 20, new(0, 0, false));
        Assert.Contains(Validate(Basic, resources).Issues, i => i.Severity == ValidationSeverity.Fatal);
    }
    [Theory]
    [InlineData("../bad")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("a..b")]
    [InlineData("")]
    public void UnsafeIdentifiersAreRejected(string id) => Assert.False(Validate(Basic with { CharacterId = id }).CanInstall);
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"unknown\":true}")]
    public void MalformedDuplicateOrUnknownJsonIsDiagnosticNotAnException(string json)
    {
        var result = new CharacterPackageValidator().Validate(new(json, Resources), new());
        Assert.False(result.CanInstall);
        Assert.All(result.Issues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Message)));
    }
    [Fact]
    public void NullSectionsAndLocaleCannotCrashValidator()
    {
        Assert.False(Validate(Basic with { DefaultLocale = null! }).CanInstall);
        Assert.False(Validate(Basic with { Assets = null! }).CanInstall);
        Assert.False(Validate(Basic with { Animations = null! }).CanInstall);
        Assert.False(Validate(Basic with { Profiles = null! }).CanInstall);
    }
    [Fact]
    public void MissingTypedProfileFieldsAndOutOfRangeRecommendationsAreDisabled()
    {
        var resources = Resources;
        resources["voice.json"] = new("voice.json", 100, Json: "{\"provider\":\"offline\"}");
        resources["behavior.json"] = new("behavior.json", 100, Json: "{\"behaviors\":[{\"animation\":\"idle\",\"weight\":11,\"cooldownSeconds\":-1}]}");
        resources["hitareas.json"] = new("hitareas.json", 100, Json: "{\"areas\":[{\"id\":\"body\",\"x\":0.8,\"y\":0,\"width\":0.8,\"height\":1}]}");
        var result = Validate(Basic, resources);
        Assert.True(result.CanInstall);
        Assert.Equal(3, result.Issues.Count(issue => issue.ErrorCode == CharacterErrorCode.InvalidProfile));
        Assert.Contains(CharacterCapability.TtsProfile, result.MissingCapabilities);
    }
    [Fact]
    public void CategoryFallbackAndExplicitCompatibilityPrecedeIdle()
    {
        var animations = new Dictionary<string, AnimationDefinition>(Basic.Animations)
        {
            ["talking"] = Basic.Animations["idle"],
            ["rest"] = Basic.Animations["idle"],
            ["special"] = new() { Type = AnimationFormat.StaticPng, Path = "missing.png", Fallback = "rest" }
        };
        var definition = Validate(Basic with { Animations = animations }).Definition!;
        Assert.Equal(new[] { "talking", "idle", "fallback" }, new AnimationResolver().Candidates(definition, new("mouth-open")).Select(c => c.Semantic.Value));
        Assert.Equal(new[] { "rest", "idle", "fallback" }, new AnimationResolver().Candidates(definition, new("special")).Select(c => c.Semantic.Value));
    }
    [Fact]
    public void UnusedBrokenImageIsReportedWithoutBlockingRequiredValidResources()
    {
        var resources = Resources;
        resources["unused.png"] = new("unused.png", 50, new(0, 0, false));
        var result = Validate(Basic, resources);
        Assert.True(result.CanInstall);
        Assert.Contains(result.Warnings, issue => issue.ErrorCode == CharacterErrorCode.InvalidPng);
    }
    [Theory]
    [InlineData(2, "0.2.0", CharacterErrorCode.UnsupportedSchema)]
    [InlineData(1, "999.0.0", CharacterErrorCode.AppTooOld)]
    [InlineData(1, "bad", CharacterErrorCode.InvalidVersion)]
    public void VersionEnvelopeIsEnforced(int schema, string minimum, CharacterErrorCode error)
    {
        var result = Validate(Basic with { SchemaVersion = schema, MinimumAppVersion = minimum });
        Assert.False(result.CanInstall);
        Assert.Contains(result.Issues, i => i.ErrorCode == error);
    }
    [Fact]
    public void OptionalBrokenAnimationDegradesAndFallbackIsCentralized()
    {
        var manifest = Basic with { Animations = new Dictionary<string, AnimationDefinition>(Basic.Animations)
        {
            ["happy"] = new() { Type = AnimationFormat.StaticPng, Path = "missing.png", Fallback = "idle" }
        }};
        var result = Validate(manifest);
        Assert.True(result.CanInstall);
        Assert.DoesNotContain("happy", result.Definition!.Animations.Keys);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error);
        var candidates = new AnimationResolver().Candidates(result.Definition, new("happy"));
        Assert.Equal(new[] { "idle", "fallback" }, candidates.Select(c => c.Semantic.Value));
    }
    [Fact]
    public void ExplicitFramesPreserveOrderDurationAndLoopWhileDirectoryFramesSortOrdinally()
    {
        var frames = new[] { new AnimationFrameDefinition("idle.png", 47), new AnimationFrameDefinition("preview.png", 139) };
        var result = Validate(Basic with { Animations = new Dictionary<string, AnimationDefinition>
        { ["idle"] = new() { Type = AnimationFormat.PngSequence, Frames = frames, Fps = 17, Loop = false } } });
        Assert.True(result.CanInstall);
        var animation = result.Definition!.Animations["idle"];
        Assert.Equal(frames, animation.Frames);
        Assert.False(animation.Loop);
        var resources = Resources;
        resources["seq/002.png"] = new("seq/002.png", 100, new(256, 256, true));
        resources["seq/001.png"] = new("seq/001.png", 100, new(256, 256, true));
        result = Validate(Basic with { Animations = new Dictionary<string, AnimationDefinition>
        { ["idle"] = new() { Type = AnimationFormat.PngSequence, Path = "seq", Fps = 20 } } }, resources);
        Assert.Equal(new[] { "seq/001.png", "seq/002.png" }, result.Definition!.Animations["idle"].Frames!.Select(f => f.Path));
        Assert.All(result.Definition.Animations["idle"].Frames!, f => Assert.Equal(50, f.DurationMs));
    }
    [Theory]
    [InlineData(0, 20)]
    [InlineData(61, 20)]
    [InlineData(12, -1)]
    [InlineData(12, 60001)]
    public void InvalidTimingAndDuplicateFramesAreRejected(int fps, int duration)
    {
        var animation = new AnimationDefinition { Type = AnimationFormat.PngSequence, Fps = fps, Frames = [new("idle.png", duration)] };
        Assert.False(Validate(Basic with { Animations = new Dictionary<string, AnimationDefinition> { ["idle"] = animation } }).CanInstall);
        Assert.False(Validate(Basic with { Animations = new Dictionary<string, AnimationDefinition>
        { ["idle"] = animation with { Fps = 12, Frames = [new("idle.png", 50), new("idle.png", 50)] } } }).CanInstall);
    }
    [Fact]
    public void StandardAndFullRequireActualTypedProfilesAndAnimations()
    {
        var resources = Resources;
        var animations = new Dictionary<string, AnimationDefinition>(Basic.Animations);
        foreach (var semantic in new[] { "blink", "happy", "rest" }) animations[semantic] = Basic.Animations["idle"];
        void Profile(string path, object typed) => resources.Add(path, new(path, 100, Json: JsonSerializer.Serialize(typed, CharacterSchema.JsonOptions())));
        Profile("persona.json", new PersonaProfile("Test", ["calm"]));
        Profile("dialogue.json", new DialogueProfile(new Dictionary<string, IReadOnlyList<string>> { ["idle"] = ["hello"] }));
        Profile("emotion.json", new EmotionProfile(new Dictionary<string, string> { ["tired"] = "rest" }));
        var profiles = new CharacterProfileReferences
        {
            Persona = new Dictionary<string, string> { ["en-US"] = "persona.json" },
            Dialogue = new Dictionary<string, string> { ["en-US"] = "dialogue.json" }, EmotionMap = "emotion.json"
        };
        var manifest = Basic with { Animations = animations, Profiles = profiles };
        Assert.Equal(CharacterTier.Standard, Validate(manifest, resources).ActualLevel);
        foreach (var semantic in new[] { "talking", "mouth-open", "mouth-closed" }) animations[semantic] = Basic.Animations["idle"];
        Profile("hit.json", new HitAreaProfile([new("body", .1, .1, .8, .8)]));
        Profile("behavior.json", new BehaviorProfile([new("idle", 1, 60)]));
        Profile("voice.json", new CharacterVoiceProfile("future-provider", "future-voice"));
        manifest = manifest with { Profiles = profiles with { HitAreas = "hit.json", BehaviorProfile = "behavior.json", Voice = "voice.json" } };
        var full = Validate(manifest, resources);
        Assert.Equal(CharacterTier.Full, full.ActualLevel);
        Assert.Equal(100, full.CompletenessPercentage);
        Assert.Empty(full.MissingCapabilities);
        resources["voice.json"] = new("voice.json", 100, Json: "{\"provider\":\"x\",\"voice\":\"x\",\"apiKey\":\"not-a-real-key\"}");
        Assert.Equal(CharacterTier.Standard, Validate(manifest, resources).ActualLevel);
    }
    [Fact]
    public void CyclicSemanticFallbackTerminatesAndLocaleFallsBackToEnglish()
    {
        var manifest = Basic with
        {
            Locales = new Dictionary<string, LocalizedCharacterText> { ["en-US"] = new("English", "Description") },
            Animations = new Dictionary<string, AnimationDefinition>(Basic.Animations)
            {
                ["happy"] = new() { Type = AnimationFormat.Live2D, Fallback = "rest" },
                ["rest"] = new() { Type = AnimationFormat.Live2D, Fallback = "happy" }
            }
        };
        var definition = Validate(manifest).Definition!;
        Assert.Equal("English", definition.Localize("zh-CN").Name);
        Assert.Equal(new[] { "idle", "fallback" }, new AnimationResolver().Candidates(definition, new("happy")).Select(c => c.Semantic.Value));
    }
    [Theory]
    [InlineData("../out.png")]
    [InlineData("C:/out.png")]
    [InlineData("/out.png")]
    [InlineData("dir\\out.png")]
    [InlineData("file.png:stream")]
    [InlineData("NUL.png")]
    [InlineData("COM¹.png")]
    [InlineData("LPT².txt")]
    [InlineData("CONOUT$.txt")]
    [InlineData("NUL .png")]
    [InlineData("dir/../out.png")]
    [InlineData("a//out.png")]
    [InlineData("a./out.png")]
    public void UnsafePathsAreNeverOptionalDowngrades(string path)
    {
        Assert.False(PackagePath.IsSafe(path));
        Assert.Throws<ArgumentException>(() => PackagePath.Resolve(Path.GetTempPath(), path));
        Assert.False(Validate(Basic with { Assets = new(path, "fallback.png") }).CanInstall);
    }
    [Fact]
    public void LongAuthorFallbackChainsDoNotUseRecursiveStackGrowth()
    {
        var animations = new Dictionary<string, AnimationDefinition>(Basic.Animations);
        for (var i = 0; i < 3000; i++) animations[$"clip-{i}"] = new() { Type = AnimationFormat.Live2D, Fallback = i == 2999 ? "idle" : $"clip-{i + 1}" };
        var definition = Validate(Basic with { Animations = animations }).Definition!;
        Assert.Equal(new[] { "idle", "fallback" }, new AnimationResolver().Candidates(definition, new("clip-0")).Select(c => c.Semantic.Value));
    }
}
