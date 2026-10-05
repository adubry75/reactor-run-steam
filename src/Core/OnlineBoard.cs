using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace ReactorRun;

/// <summary>
/// Shared online top 10s (the same scores.php the web version uses). Plain .NET HttpClient, results are
/// handed back on the main thread. Everything is best-effort: offline just means the local boards show.
/// </summary>
public sealed class OnlineBoard
{
    public static readonly string Url = System.Environment.GetEnvironmentVariable("RR_SCORES_URL") ?? "https://dubry.com/games/ReactorRun/scores.php";

    public List<BoardEntry> Station { get; private set; }
    public List<BoardEntry> Total { get; private set; }
    public bool Online => Station != null && Total != null;

    static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    sealed class Lists { public List<BoardEntry> Station { get; set; } public List<BoardEntry> Total { get; set; } }
    sealed class Reply { public List<BoardEntry> Scores { get; set; } public int Rank { get; set; } public string Id { get; set; } }

    static void OnMain(Action a) => Callable.From(a).CallDeferred();

    bool _fetching;

    public void Fetch()
    {
        if (_fetching) return;
        _fetching = true;
        Task.Run(async () =>
        {
            try
            {
                var body = await Http.GetStringAsync($"{Url}?t={DateTime.UtcNow.Ticks}");
                var l = JsonSerializer.Deserialize<Lists>(body, Json);
                OnMain(() => { if (l?.Station != null) Station = l.Station; if (l?.Total != null) Total = l.Total; _fetching = false; });
            }
            catch (Exception) { OnMain(() => _fetching = false); }
        });
    }

    /// <summary>Posts a score. Calls done(rank, id) on the main thread: rank 0 = didn't place, -1 = failed.</summary>
    public void Submit(string board, string name, int score, int station, int crew, Action<int, string> done)
    {
        var payload = JsonSerializer.Serialize(new { board, name, score, station, crew });
        Task.Run(async () =>
        {
            try
            {
                using var resp = await Http.PostAsync(Url, new StringContent(payload, Encoding.UTF8, "application/json"));
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { OnMain(() => done(-1, "")); return; }
                var rep = JsonSerializer.Deserialize<Reply>(body, Json);
                OnMain(() =>
                {
                    if (rep?.Scores != null) { if (board == "total") Total = rep.Scores; else Station = rep.Scores; }
                    done(rep?.Rank ?? 0, rep?.Id ?? "");
                });
            }
            catch (Exception) { OnMain(() => done(-1, "")); }
        });
    }
}
