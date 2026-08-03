using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Settings;
using System.Reflection;
using Xunit;

namespace RensaioBackend.Tests;

public sealed class SettingsServiceSerializationTests
{
    [Fact]
    public void StringArraySettings_RoundTrip_PreservesPipeCharacters()
    {
        var original = new EditableSettingsDto
        {
            ContributionSourceAllowlist = ["pkg.example|123456789", "pkg.other|42"],
            ContributionPackageAllowlist = ["pkg.example", "pkg.other"],
            MihonRepositories = ["https://repo.example/index.min.json"],
            PreferredLanguages = ["en", "es"]
        };

        List<SettingEntity> persisted = Serialize(original);
        (_, EditableSettingsDto restored) = Deserialize(persisted, new EditableSettingsDto());

        Assert.Equal(original.ContributionSourceAllowlist, restored.ContributionSourceAllowlist);
        Assert.Equal(original.ContributionPackageAllowlist, restored.ContributionPackageAllowlist);
        Assert.Equal(original.MihonRepositories, restored.MihonRepositories);
        Assert.Equal(original.PreferredLanguages, restored.PreferredLanguages);
    }

    [Fact]
    public void StringArraySettings_LegacyPipeJoinedValues_StillDeserialize()
    {
        // Existing databases hold '|'-joined values written before the JSON format; they must
        // keep deserializing after the switch to JSON persistence.
        var persisted = new List<SettingEntity>
        {
            new()
            {
                Name = nameof(EditableSettingsDto.MihonRepositories),
                Value = "https://a.example/index.json|https://b.example/index.json"
            },
            new() { Name = nameof(EditableSettingsDto.PreferredLanguages), Value = "en" }
        };

        (_, EditableSettingsDto restored) = Deserialize(persisted, new EditableSettingsDto());

        Assert.Equal(
            new[] { "https://a.example/index.json", "https://b.example/index.json" },
            restored.MihonRepositories);
        Assert.Equal(new[] { "en" }, restored.PreferredLanguages);
    }

    private static List<SettingEntity> Serialize(EditableSettingsDto settings) =>
        (List<SettingEntity>)typeof(SettingsService)
            .GetMethod("Serialize", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [settings])!;

    private static (bool NeedSave, EditableSettingsDto Settings) Deserialize(
        List<SettingEntity> settings, EditableSettingsDto defaults) =>
        ((bool, EditableSettingsDto))typeof(SettingsService)
            .GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [settings, defaults])!;
}
