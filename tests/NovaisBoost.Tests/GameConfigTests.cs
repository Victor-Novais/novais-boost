using System.Text;
using NovaisBoost.Core;

namespace NovaisBoost.Tests;

/// <summary>F15: game configuration files (ini, KeyValues, cfg, json), presets, frame cap. All on in-memory files.</summary>
public sealed partial class TweakEngineTests
{
    private const string Cs2Video = @"C:\Steam\userdata\123\730\local\cfg\cs2_video.txt";
    private const string FortniteIni = @"C:\Users\x\AppData\Local\FortniteGame\Saved\Config\WindowsClient\GameUserSettings.ini";

    private static byte[] Fixture(string game, string file) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "presets", game, file));

    private static GamePreset Preset(string id) => DataCatalog.Presets().Single(preset => preset.Id == id);

    private static TweakDefinition PresetTweak(string id, string path, int? frameCap = null) =>
        Assert.Single(GamePresets.Build(Preset(id), [new ResolvedPresetFile(Preset(id).Files[0], path)], frameCap));

    private string Text(string path) => ConfigFiles.Decode(_windows.Files[path]).Text;

    // ------------------------------------------------------------------ apply → revert, byte for byte

    [Fact]
    public void Cs2PresetAppliesAndRevertsByteForByte()
    {
        var original = Fixture("cs2", "cs2_video.txt");
        _windows.Files[Cs2Video] = original;
        var tweak = PresetTweak("cs2", Cs2Video);

        Assert.Equal(3, Engine.Apply(tweak, dryRun: false).Count);
        Assert.Contains("\t\"setting.msaa_samples\"\t\t\"2\"", Text(Cs2Video));
        Assert.Contains("\t\"setting.mat_vsync\"\t\t\"0\"", Text(Cs2Video));
        Assert.Contains("\"setting.r_texturefilteringquality\"\t\t\"5\"", Text(Cs2Video)); // untouched
        Assert.Empty(Engine.Detect(tweak));

        Engine.RevertAll(dryRun: false);
        Assert.Equal(original, _windows.Files[Cs2Video]);
    }

    [Fact]
    public void FortnitePresetChangesOnlyItsSectionAndRevertsByteForByte()
    {
        var original = Fixture("fortnite", "GameUserSettings.ini");
        _windows.Files[FortniteIni] = original;
        var tweak = PresetTweak("fortnite", FortniteIni, frameCap: 138);

        Engine.Apply(tweak, dryRun: false);
        var text = Text(FortniteIni);
        Assert.Contains("[/Script/FortniteGame.FortGameUserSettings]", text);
        Assert.Equal("138.000000", ConfigText.Get(ConfigFormat.Ini, text, "/Script/FortniteGame.FortGameUserSettings", "FrameRateLimit"));
        Assert.Equal("False", ConfigText.Get(ConfigFormat.Ini, text, "/Script/FortniteGame.FortGameUserSettings", "bUseVSync"));
        Assert.Equal("0.000000", ConfigText.Get(ConfigFormat.Ini, text, "/Script/Engine.GameUserSettings", "FrameRateLimit"));

        Engine.RevertAll(dryRun: false);
        Assert.Equal(original, _windows.Files[FortniteIni]);
    }

    [Fact]
    public void WithoutVrrTheFrameCapIsLeftAlone()
    {
        var tweak = PresetTweak("fortnite", FortniteIni, frameCap: null);
        var action = Assert.IsType<ConfigFileAction>(Assert.Single(tweak.Actions));
        Assert.Equal("bUseVSync", action.Key);
    }

    [Fact]
    public void EveryPresetAppliesAndRevertsOnItsFixture()
    {
        foreach (var preset in DataCatalog.Presets())
        {
            foreach (var file in preset.Files)
            {
                var name = Path.GetFileName(file.Path.Replace('/', '\\'));
                var original = Fixture(preset.Id, name);
                var path = $@"C:\fixture\{preset.Id}\{name}";
                _windows.Files[path] = original;
                var tweaks = GamePresets.Build(preset with { Files = [file] }, [new ResolvedPresetFile(file, path)], frameCap: 141);
                foreach (var tweak in tweaks)
                {
                    Assert.NotEmpty(Engine.Apply(tweak, dryRun: false));
                    Assert.StartsWith("Jogos: ", tweak.Category);
                }
                Engine.RevertAll(dryRun: false);
                Assert.Equal(original, _windows.Files[path]);
            }
            Assert.NotEmpty(preset.Sources);
            Assert.True(DateOnly.TryParse(preset.LastVerified, out _), $"{preset.Id}: lastVerified inválido.");
        }
    }

    [Fact]
    public void FileRewrittenByTheGameKeepsItsNewValuesOnRevert()
    {
        _windows.Files[FortniteIni] = Fixture("fortnite", "GameUserSettings.ini");
        Engine.Apply(PresetTweak("fortnite", FortniteIni, frameCap: 138), dryRun: false);

        // The game saved other settings after our change.
        _windows.Files[FortniteIni] = Encoding.UTF8.GetBytes(Text(FortniteIni).Replace("sg.ShadowQuality=3", "sg.ShadowQuality=0"));
        Engine.RevertAll(dryRun: false);

        var text = Text(FortniteIni);
        Assert.Equal("60.000000", ConfigText.Get(ConfigFormat.Ini, text, "/Script/FortniteGame.FortGameUserSettings", "FrameRateLimit"));
        Assert.Equal("True", ConfigText.Get(ConfigFormat.Ini, text, "/Script/FortniteGame.FortGameUserSettings", "bUseVSync"));
        Assert.Equal("0", ConfigText.Get(ConfigFormat.Ini, text, "ScalabilityGroups", "sg.ShadowQuality"));
    }

    // ------------------------------------------------------------------ safety rules

    [Fact]
    public void ApplyIsRefusedWhileTheGameIsOpen()
    {
        _windows.Files[Cs2Video] = Fixture("cs2", "cs2_video.txt");
        _windows.Running.Add("cs2");
        var error = Assert.Throws<InvalidOperationException>(() => Engine.Apply(PresetTweak("cs2", Cs2Video), dryRun: false));
        Assert.Contains("Feche o jogo", error.Message);
        Assert.Equal(0, _windows.Writes);
        Assert.Null(Snapshots.GetActive());
    }

    [Fact]
    public void RevertWaitsForTheGameToClose()
    {
        var original = Fixture("cs2", "cs2_video.txt");
        _windows.Files[Cs2Video] = original;
        Engine.Apply(PresetTweak("cs2", Cs2Video), dryRun: false);
        _windows.Running.Add("cs2");
        Assert.Throws<InvalidOperationException>(() => Engine.RevertAll(dryRun: false));
        Assert.False(Snapshots.GetActive()!.Pending);

        _windows.Running.Clear();
        Engine.RevertAll(dryRun: false);
        Assert.Equal(original, _windows.Files[Cs2Video]);
    }

    [Fact]
    public void MissingFileOrKeyFailsClosed()
    {
        var tweak = PresetTweak("cs2", Cs2Video);
        Assert.Contains("não existe", Assert.Throws<InvalidDataException>(() => Engine.Detect(tweak)).Message);

        _windows.Files[Cs2Video] = Encoding.UTF8.GetBytes("\"video.cfg\"\r\n{\r\n\t\"setting.mat_vsync\"\t\t\"1\"\r\n}\r\n");
        Assert.Contains("setting.msaa_samples", Assert.Throws<InvalidDataException>(() => Engine.Apply(tweak, dryRun: false)).Message);
        Assert.Equal(0, _windows.Writes);
    }

    [Fact]
    public void RepeatedKeyIsNeverGuessed()
    {
        const string text = "[A]\r\nKey=1\r\nKey=2\r\n";
        Assert.Throws<InvalidDataException>(() => ConfigText.Get(ConfigFormat.Ini, text, "A", "Key"));
        Assert.Throws<InvalidDataException>(() => ConfigText.Get(ConfigFormat.Ini, "[A]\r\nX=1\r\n[A]\r\nX=2\r\n", "A", "X"));
    }

    // ------------------------------------------------------------------ formats

    [Fact]
    public void IniKeysAreScopedToTheirSection()
    {
        const string text = "[One]\nLimit=1\n\n[Two]\nLimit=2\n";
        Assert.Equal("2", ConfigText.Get(ConfigFormat.Ini, text, "Two", "Limit"));
        Assert.Equal("[One]\nLimit=1\n\n[Two]\nLimit=9\n", ConfigText.Set(ConfigFormat.Ini, text, "Two", "Limit", "9"));
        Assert.Null(ConfigText.Get(ConfigFormat.Ini, text, "Three", "Limit"));
        Assert.Null(ConfigText.Get(ConfigFormat.Ini, text, "One", "Missing"));
    }

    [Fact]
    public void CfgCommandsAreReadQuotedOrNot()
    {
        const string text = "fps_max 0\r\ncl_showfps \"1\" // comment\r\n";
        Assert.Equal("0", ConfigText.Get(ConfigFormat.Cfg, text, null, "fps_max"));
        Assert.Equal("1", ConfigText.Get(ConfigFormat.Cfg, text, null, "cl_showfps"));
        Assert.Equal("fps_max 400\r\ncl_showfps \"1\" // comment\r\n", ConfigText.Set(ConfigFormat.Cfg, text, null, "fps_max", "400"));
    }

    [Fact]
    public void JsonKeepsTheValueType()
    {
        const string text = """{ "video": { "fpsCap": 60, "vsync": true, "mode": "full" } }""";
        Assert.Equal("60", ConfigText.Get(ConfigFormat.Json, text, null, "video.fpsCap"));
        var updated = ConfigText.Set(ConfigFormat.Json, ConfigText.Set(ConfigFormat.Json, text, null, "video.fpsCap", "141"), null, "video.vsync", "false");
        Assert.Equal("141", ConfigText.Get(ConfigFormat.Json, updated, null, "video.fpsCap"));
        Assert.Equal("false", ConfigText.Get(ConfigFormat.Json, updated, null, "video.vsync"));
        Assert.Throws<InvalidDataException>(() => ConfigText.Set(ConfigFormat.Json, text, null, "video.fpsCap", "abc"));
        Assert.Null(ConfigText.Get(ConfigFormat.Json, text, null, "video.missing"));
    }

    [Fact]
    public void Utf16FilesKeepTheirEncoding()
    {
        var original = ConfigFiles.Encode("[S]\r\nKey=1\r\n", Encoding.Unicode);
        var action = new ConfigFileAction(@"C:\x.ini", ConfigFormat.Ini, "S", "Key", "5", ["game"]);
        var changed = ConfigFiles.Compute(action, original, new ConfigFileState(true, "5"));
        Assert.Equal([0xFF, 0xFE], changed[..2]);
        Assert.Equal("[S]\r\nKey=5\r\n", ConfigFiles.Decode(changed).Text);
        Assert.Equal(original, ConfigFiles.Compute(action, changed, ConfigFiles.Read(action, original)));
    }

    // ------------------------------------------------------------------ frame cap and tokens

    [Theory]
    [InlineData(144, true, true, 138)]
    [InlineData(240, true, true, 224)]
    [InlineData(165, true, true, 157)]
    [InlineData(360, true, true, 324)]
    [InlineData(60, true, true, 59)]
    [InlineData(144, true, false, 141)]
    public void FrameCapKeepsTheGameInsideTheVrrRange(int hz, bool vrr, bool reflex, int expected) =>
        Assert.Equal(expected, FrameCap.For(hz, vrr, reflex));

    [Fact]
    public void WithoutVrrThereIsNoFrameCap() => Assert.Null(FrameCap.For(144, vrr: false, reflex: true));

    [Fact]
    public void PathTokensAreResolvedOrTheFileIsSkipped()
    {
        var tokens = new Dictionary<string, string> { ["%LOCALAPPDATA%"] = @"C:\Users\x\AppData\Local", ["{steamUserdata}"] = @"C:\Steam\userdata\123" };
        Assert.Equal(@"C:\Users\x\AppData\Local\FortniteGame\a.ini", GamePresets.Resolve(@"%LOCALAPPDATA%\FortniteGame\a.ini", tokens));
        Assert.Equal(@"C:\Steam\userdata\123\730\b.txt", GamePresets.Resolve(@"{steamUserdata}\730\b.txt", tokens));
        Assert.Null(GamePresets.Resolve(@"{gameDir}\cfg\autoexec.cfg", tokens));
    }

    [Fact]
    public void PresetsStayOutOfTheLevelsAndRepairs()
    {
        var ids = DataCatalog.Presets().Select(preset => preset.Id).ToArray();
        Assert.Equal(["cs2", "fortnite"], ids);
        Assert.All(DataCatalog.Presets(), preset => Assert.All(preset.Files, file => Assert.NotEmpty(file.Settings)));
        Assert.DoesNotContain(TweakCatalog.LoadEmbedded(), tweak => tweak.Actions.Any(action => action is ConfigFileAction));
        var games = DataCatalog.Games().Select(game => game.Id).ToHashSet();
        Assert.All(ids, id => Assert.Contains(id, games));
    }
}
