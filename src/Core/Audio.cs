using System.Collections.Generic;
using Godot;

namespace ReactorRun;

public enum MusicState { None, Hangar, Fight, Explore, Lockdown, Countdown }

/// <summary>Suno soundtrack player: one track set per state, A/B takes alternate, crossfades between states.</summary>
public partial class Music : Node
{
    const float Xfade = 1.4f;

    static readonly Dictionary<string, string[]> Tracks = new()
    {
        ["hangar"] = new[] { "res://music/hangar-a.mp3", "res://music/hangar-b.mp3" },
        ["explore"] = new[] { "res://music/explore-a.mp3", "res://music/explore-b.mp3" },
        ["lockdown"] = new[] { "res://music/lockdown-a.mp3", "res://music/lockdown-b.mp3" },
        ["countdown"] = new[] { "res://music/countdown-a.mp3" },
    };

    static string KeyFor(MusicState s) => s switch
    {
        MusicState.Hangar => "hangar",
        MusicState.Fight => "lockdown",
        MusicState.Explore => "explore",
        MusicState.Lockdown => "lockdown",
        MusicState.Countdown => "countdown",
        _ => null,
    };

    sealed class Fade { public AudioStreamPlayer P; public float From, To, T, Secs; }

    readonly List<AudioStreamPlayer> _pool = new();
    readonly List<Fade> _fades = new();
    readonly Dictionary<string, int> _take = new();
    readonly Dictionary<AudioStreamPlayer, float> _level = new();
    AudioStreamPlayer _cur;
    string _key;
    public float Volume { get; set; } = 0.8f;

    public override void _Ready()
    {
        for (int i = 0; i < 3; i++)
        {
            var p = new AudioStreamPlayer { VolumeDb = -80f };
            AddChild(p);
            _pool.Add(p);
        }
    }

    public void Set(MusicState state)
    {
        var key = KeyFor(state);
        if (key == _key) return;
        if (_cur != null) FadeTo(_cur, 0f, key == null ? 0.6f : Xfade);
        _key = key;
        _cur = null;
        if (key != null) StartNext();
    }

    void StartNext()
    {
        var list = Tracks[_key];
        int t = (_take.TryGetValue(_key, out var v) ? v + 1 : 0) % list.Length;
        _take[_key] = t;
        var stream = GD.Load<AudioStream>(list[t]);
        if (stream == null) return;
        var p = FreePlayer();
        p.Stream = stream;
        p.VolumeDb = -80f;
        _level[p] = 0f;
        p.Play();
        _cur = p;
        FadeTo(p, 1f, Xfade);
    }

    AudioStreamPlayer FreePlayer()
    {
        foreach (var p in _pool)
            if (!p.Playing) return p;
        var extra = new AudioStreamPlayer();
        AddChild(extra);
        _pool.Add(extra);
        return extra;
    }

    void FadeTo(AudioStreamPlayer p, float to, float secs)
    {
        _fades.RemoveAll(f => f.P == p);
        _fades.Add(new Fade { P = p, From = _level.TryGetValue(p, out var l) ? l : 0f, To = to, Secs = secs });
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _fades.Count - 1; i >= 0; i--)
        {
            var f = _fades[i];
            f.T += dt;
            float k = Mathf.Min(1f, f.T / f.Secs);
            float lin = Mathf.Lerp(f.From, f.To, k);
            _level[f.P] = lin;
            f.P.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, lin * Volume));
            if (k >= 1f)
            {
                _fades.RemoveAt(i);
                if (f.To <= 0f) f.P.Stop();
            }
        }
        // near the end of a take, crossfade into the next take of the same state
        if (_cur != null && _key != null && _cur.Stream != null)
        {
            double len = _cur.Stream.GetLength();
            if (len > 0 && _cur.GetPlaybackPosition() > len - Xfade - 0.2)
            {
                FadeTo(_cur, 0f, Xfade);
                _cur = null;
                StartNext();
            }
        }
    }

    /// <summary>Re-applies the volume setting to the playing track.</summary>
    public void ApplyVolume()
    {
        foreach (var (p, lin) in _level) p.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, lin * Volume));
    }
}

/// <summary>Pooled one-shot sound effects (pre-rendered WAVs in res://sfx).</summary>
public partial class Sfx : Node
{
    readonly Dictionary<string, AudioStream> _cache = new();
    readonly List<AudioStreamPlayer> _pool = new();
    int _next;
    public float Volume { get; set; } = 0.9f;

    public override void _Ready()
    {
        for (int i = 0; i < 16; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            _pool.Add(p);
        }
    }

    public void Play(string name, float vol = 1f, float pitchJitter = 0f)
    {
        if (Volume <= 0.001f) return;
        if (!_cache.TryGetValue(name, out var stream))
        {
            stream = GD.Load<AudioStream>($"res://sfx/{name}.wav");
            _cache[name] = stream;
        }
        if (stream == null) return;
        var p = _pool[_next];
        _next = (_next + 1) % _pool.Count;
        p.Stream = stream;
        p.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, vol * Volume * 0.7f));
        p.PitchScale = 1f + (pitchJitter > 0 ? Rng.Range(-pitchJitter, pitchJitter) : 0f);
        p.Play();
    }
}
