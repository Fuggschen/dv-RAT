using DVRAT;
using Newtonsoft.Json.Linq;
using Xunit;

public class SpeakerSettingsTests
{
    [Fact]
    public void SavesAndLoadsEveryValue()
    {
        var settings = new SpeakerSettings { Volume = 35, LowsDb = 9, MidsDb = -3, HighsDb = 1.5f };

        var loaded = SpeakerSettings.FromJson(JObject.Parse(settings.ToJson().ToString()));
        Assert.Equal(settings.Id, loaded.Id);

        Assert.Equal(35, loaded.Volume);
        Assert.Equal(9f, loaded.LowsDb);
        Assert.Equal(-3f, loaded.MidsDb);
        Assert.Equal(1.5f, loaded.HighsDb);
    }

    [Fact]
    public void MissingOrBrokenStateGivesDefaults()
    {
        Assert.Equal(SpeakerSettings.DefaultVolume, SpeakerSettings.FromJson(null).Volume);
        Assert.Equal(SpeakerSettings.DefaultVolume, SpeakerSettings.FromJson(new JValue("broken")).Volume);
        Assert.Equal(0f, SpeakerSettings.FromJson(new JObject { ["volume"] = 50 }).LowsDb);
    }

    [Fact]
    public void KeepsValuesInRange()
    {
        var settings = new SpeakerSettings { Volume = 140, LowsDb = 40, HighsDb = -40 };

        Assert.Equal(100, settings.Volume);
        Assert.Equal(ToneSettings.MaxEqDb, settings.LowsDb);
        Assert.Equal(-ToneSettings.MaxEqDb, settings.HighsDb);
    }

    [Fact]
    public void SubwooferPlaysOnlyBass()
    {
        var sub = SpeakerSettings.BaseTone(SpeakerKind.Subwoofer);
        var speaker = SpeakerSettings.BaseTone(SpeakerKind.Speaker);

        Assert.True(sub.HighCutHz > 0 && sub.HighCutHz <= 150);
        Assert.Equal(0f, speaker.HighCutHz);
    }

    [Fact]
    public void TakingOverAnotherCopysSettingsKeepsTheId()
    {
        var here = new SpeakerSettings();
        string id = here.Id;
        here.CopyFrom(new SpeakerSettings { Volume = 40, LowsDb = 6, MidsDb = -2, HighsDb = 4 });
        Assert.Equal(id, here.Id);
        Assert.Equal((40, 6f, -2f, 4f), (here.Volume, here.LowsDb, here.MidsDb, here.HighsDb));
    }
}
