using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace ReactorRun;

/// <summary>Persistent progress and settings, stored as JSON in user://save.json.</summary>
public sealed class SaveData
{
    const string Path = "user://save.json";

    [System.Text.Json.Serialization.JsonIgnore] public bool NoPersist { get; set; }

    public int Salvage { get; set; }
    public Dictionary<string, int> Up { get; set; } = new();
    public int Runs { get; set; }
    public int Escapes { get; set; }
    public int Best { get; set; }
    public float? BestLeft { get; set; }
    public int CrewTotal { get; set; }
    public HashSet<string> Hints { get; set; } = new();
    public HashSet<string> Achievements { get; set; } = new();
    public Dictionary<string, int> Stats { get; set; } = new();
    public List<ScoreEntry> Scores { get; set; } = new();
    public string DailyDate { get; set; } = "";
    public int DailyBest { get; set; }
    public int DailyTries { get; set; }

    // settings
    public float MusicVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 0.9f;
    public bool Glow { get; set; } = true;
    public bool Crt { get; set; } = true;
    public bool Fullscreen { get; set; }
    public bool ScreenShake { get; set; } = true;

    public int Level(string key) => Up.TryGetValue(key, out var v) ? v : 0;
    public int Stat(string key) => Stats.TryGetValue(key, out var v) ? v : 0;

    /// <summary>Adds a run to the Hall of Fame (top 10 by banked salvage). Returns its rank (1-based) or 0 if it didn't place.</summary>
    public int AddScore(ScoreEntry e)
    {
        Scores.Add(e);
        Scores.Sort((a, b) => b.Banked.CompareTo(a.Banked));
        if (Scores.Count > 10) Scores.RemoveRange(10, Scores.Count - 10);
        return Scores.IndexOf(e) + 1;
    }

    public bool HintOnce(string key)
    {
        if (!Hints.Add(key)) return false;
        Save();
        return true;
    }

    public static SaveData Load()
    {
        try
        {
            if (FileAccess.FileExists(Path))
            {
                var json = FileAccess.GetFileAsString(Path);
                var data = JsonSerializer.Deserialize<SaveData>(json);
                if (data != null) return data;
            }
        }
        catch (JsonException e)
        {
            GD.PushWarning($"Save file unreadable, starting fresh: {e.Message}");
        }
        return new SaveData();
    }

    public void Save()
    {
        if (NoPersist) return;
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this));
    }

    /// <summary>Wipes progress but keeps the player's settings.</summary>
    public void ResetProgress()
    {
        Salvage = 0; Up.Clear(); Runs = 0; Escapes = 0; Best = 0; BestLeft = null; CrewTotal = 0; Hints.Clear();
        Achievements.Clear(); Stats.Clear(); Scores.Clear(); DailyDate = ""; DailyBest = 0; DailyTries = 0;
        Save();
    }
}

public sealed class ScoreEntry
{
    public int Banked { get; set; }
    public int Station { get; set; }
    public bool Won { get; set; }
    public float TimeLeft { get; set; }
    public int Crew { get; set; }
    public bool Daily { get; set; }
    public string Date { get; set; } = "";
}
