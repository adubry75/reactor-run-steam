using System;
using System.Collections.Generic;
using Godot;
using static ReactorRun.Config;

namespace ReactorRun;

/// <summary>A selectable button drawn on the canvas. Works with keyboard, gamepad and mouse.</summary>
public sealed class MenuItem
{
    public string Label = "", Sub = "", Right = "";
    public bool Enabled = true;
    public Color Accent = Line;
    public Action Activate;
    public Action<int> Adjust;      // left/right on sliders and toggles
    public int Pips, PipMax;         // upgrade level pips drawn after the label
    public Rect2 Rect;
}

public sealed class Menu
{
    public readonly List<MenuItem> Items = new();
    public int Selected;
    public bool Horizontal;

    public MenuItem Add(string label, Action act, string sub = "", string right = "", bool enabled = true, Color? accent = null, Action<int> adjust = null)
    {
        var it = new MenuItem { Label = label, Activate = act, Sub = sub, Right = right, Enabled = enabled, Accent = accent ?? Line, Adjust = adjust };
        Items.Add(it);
        return it;
    }

    public void Clear() { Items.Clear(); Selected = 0; }

    /// <summary>Handles nav + confirm. Returns true if an item was activated.</summary>
    public bool HandleInput(Sfx sfx)
    {
        if (Items.Count == 0) return false;
        int prev = Selected;
        bool back = Horizontal ? GameInput.Hit(GameInput.NavLeft) : GameInput.Hit(GameInput.NavUp);
        bool fwd = Horizontal ? GameInput.Hit(GameInput.NavRight) : GameInput.Hit(GameInput.NavDown);
        if (back) Selected = (Selected - 1 + Items.Count) % Items.Count;
        if (fwd) Selected = (Selected + 1) % Items.Count;
        if (Selected != prev) sfx.Play("tick", 0.35f);
        var cur = Items[Selected];
        if (!Horizontal && cur.Adjust != null)
        {
            if (GameInput.Hit(GameInput.NavLeft)) { cur.Adjust(-1); sfx.Play("tick", 0.4f); }
            if (GameInput.Hit(GameInput.NavRight)) { cur.Adjust(1); sfx.Play("tick", 0.4f); }
        }
        if (GameInput.Hit(GameInput.Confirm) && cur.Enabled && cur.Activate != null) { sfx.Play("pick", 0.6f); cur.Activate(); return true; }
        return false;
    }

    public void Hover(Vector2 mouse)
    {
        for (int i = 0; i < Items.Count; i++) if (Items[i].Rect.HasPoint(mouse)) Selected = i;
    }

    public bool Click(Vector2 mouse, Sfx sfx)
    {
        foreach (var it in Items)
        {
            if (!it.Rect.HasPoint(mouse)) continue;
            if (it.Adjust != null) { it.Adjust(mouse.X > it.Rect.GetCenter().X ? 1 : -1); sfx.Play("tick", 0.4f); return true; }
            if (it.Enabled && it.Activate != null) { sfx.Play("pick", 0.6f); it.Activate(); return true; }
        }
        return false;
    }

    /// <summary>Draws one item as a neon button. Call after setting Rect.</summary>
    public void DrawItem(CanvasItem ci, MenuItem it, int index, float clock)
    {
        bool sel = index == Selected;
        var r = it.Rect;
        var col = it.Enabled ? it.Accent : Dim;
        ci.DrawRect(r, new Color(0.02f, 0.035f, 0.07f, 0.94f)); // near-opaque base so background lines don't show through
        ci.DrawRect(r, Alpha(col, sel ? 0.18f : 0.08f));
        ci.DrawRect(r, sel ? Neon(col, 1.6f) : Alpha(col, 0.5f), false, sel ? 2f : 1f);
        if (sel) ci.DrawRect(new Rect2(r.Position.X, r.Position.Y, 3f, r.Size.Y), Neon(col, 1.8f));
        float ty = r.Position.Y + (string.IsNullOrEmpty(it.Sub) ? r.Size.Y / 2f + 6f : 22f);
        Draw.Text(ci, it.Label, new Vector2(r.Position.X + 14f, ty), 16, it.Enabled ? Ink : Dim);
        if (it.PipMax > 0)
        {
            float px = r.Position.X + 24f + Draw.TextWidth(it.Label, 16);
            for (int p = 0; p < it.PipMax; p++)
            {
                var pr = new Rect2(px + p * 12f, ty - 10f, 8f, 8f);
                if (p < it.Pips) ci.DrawRect(pr, Neon(Core, 1.5f)); else ci.DrawRect(pr, Alpha(Core, 0.6f), false, 1f);
            }
        }
        if (!string.IsNullOrEmpty(it.Sub)) Draw.Text(ci, it.Sub, new Vector2(r.Position.X + 14f, ty + 18f), 13, Dim);
        if (!string.IsNullOrEmpty(it.Right))
            Draw.Text(ci, it.Right, new Vector2(r.End.X - 14f, r.Position.Y + r.Size.Y / 2f + 6f), 15, it.Enabled ? Neon(col, 1.3f) : Dim, HorizontalAlignment.Right);
    }
}

/// <summary>The hangar backdrop: deck, launch pad, idling ship and every survivor you've rescued.</summary>
public static class HangarScene
{
    public static Vector2 ShipPos(Vector2 size, float panelW) => new(Center(size, panelW), size.Y * 0.74f);
    static float Center(Vector2 size, float panelW) => size.X >= 980f ? MathF.Max(size.X * 0.28f, (size.X - panelW) / 2f) : size.X / 2f;

    public static void DrawLander(CanvasItem ci, Vector2 at, float s, Color col)
    {
        Vector2 P(float x, float y) => at + new Vector2(x, y) * s;
        var c = Neon(col);
        ci.DrawPolyline(new[] { P(-38, 0), P(-28, -16), P(-12, -34), P(20, -34), P(38, -16), P(30, 0) }, c, 2f, true);
        ci.DrawMultiline(new[] { P(-28, -16), P(38, -16), P(-2, -34), P(4, -46), P(-6, -24), P(14, -24) }, c, 2f);
    }

    public static void Draw(CanvasItem ci, Ctx c, float panelW)
    {
        var size = c.Size; float clock = c.Clock;
        float cx = Center(size, panelW), fy = size.Y * 0.74f, s = Mathf.Clamp(size.Y / 260f, 1.6f, 3.2f);
        c.Stars.Draw(ci, size, clock * 8f, 0f);
        var wall = new List<Vector2>();
        for (int i = -5; i <= 5; i++) { wall.Add(new Vector2(cx + i * 90f, fy - size.Y * 0.5f)); wall.Add(new Vector2(cx + i * 90f, fy)); }
        wall.Add(new Vector2(cx - 470f, fy - size.Y * 0.5f)); wall.Add(new Vector2(cx + 470f, fy - size.Y * 0.5f));
        ci.DrawMultiline(wall.ToArray(), Alpha(Line, 0.18f), 1f);
        for (int i = -4; i <= 4; i++)
        {
            bool on = MathF.Sin(clock * 2f + i) > -0.6f;
            ci.DrawRect(new Rect2(cx + i * 90f - 8f, fy - size.Y * 0.5f - 2f, 16f, 3f), on ? Neon(Core, 1.6f) : Alpha(Dim, 0.3f));
        }
        ci.DrawLine(new Vector2(0, fy), new Vector2(size.X, fy), Neon(Line, 1.6f), 1.5f);
        var floor = new List<Vector2>();
        for (int k = -10; k <= 10; k++) { floor.Add(new Vector2(cx + k * 50f, fy)); floor.Add(new Vector2(cx + k * 190f, size.Y)); }
        for (int j = 1; j < 5; j++) { float y = fy + (size.Y - fy) * MathF.Pow(j / 5f, 1.6f); floor.Add(new Vector2(0, y)); floor.Add(new Vector2(size.X, y)); }
        ci.DrawMultiline(floor.ToArray(), Alpha(Line, 0.22f), 1f);
        float padR = 70f * s / 2.2f;
        var pad = new Vector2[33];
        for (int i = 0; i <= 32; i++) { float a = i / 32f * Mathf.Tau; pad[i] = new Vector2(cx + MathF.Cos(a) * padR, fy + MathF.Sin(a) * 9f); }
        ci.DrawPolyline(pad, Neon(Core, 1.5f), 1.5f, true);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.Tau + clock; bool on = (int)(clock * 4f + i) % 3 == 0;
            ci.DrawRect(new Rect2(cx + MathF.Cos(a) * padR - 2f, fy + MathF.Sin(a) * 9f - 2f, 4f, 4f), on ? Neon(Core, 1.8f) : Alpha(Core, 0.25f));
        }
        DrawLander(ci, new Vector2(cx, fy - 4f + MathF.Sin(clock * 2f) * 1.5f), s, Line);
        int n = Math.Min(c.Save.CrewTotal, 28);
        for (int i = 0; i < n; i++)
        {
            int side = i % 2 == 1 ? 1 : -1, k = i / 2;
            float x = cx + side * (padR + 30f + k * 26f), bob = MathF.Abs(MathF.Sin(clock * 3f + i)) * 2f;
            if (x < 10f || x > size.X - 10f) continue;
            ci.DrawSetTransform(new Vector2(x, fy - 22f * 1.4f - bob), 0f, new Vector2(1.4f, 1.4f));
            StationPhase.DrawAstronaut(ci, new Vector2(-6f, 0f), 12f, -side, Loot);
            ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        ReactorRun.Draw.Text(ci, n > 0 ? $"CREW RESCUED · {c.Save.CrewTotal}" : "NO CREW YET · RESCUE SURVIVORS TO FILL THE HANGAR", new Vector2(cx, fy + 34f), 12, Dim, HorizontalAlignment.Center);
    }
}
