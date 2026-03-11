using System;
using Godot;

namespace WorldsBeneathSleep;

public sealed class GridActor
{
    public GridActor(
        string name,
        string glyph,
        Color glyphColor,
        Vector2I cell,
        bool isPlayer,
        int maxHitPoints,
        int attackPower)
    {
        Name = name;
        Glyph = glyph;
        GlyphColor = glyphColor;
        Cell = cell;
        IsPlayer = isPlayer;
        MaxHitPoints = maxHitPoints;
        HitPoints = maxHitPoints;
        AttackPower = attackPower;
    }

    public string Name { get; }

    public string Glyph { get; }

    public Color GlyphColor { get; }

    public Vector2I Cell { get; set; }

    public bool IsPlayer { get; }

    public int MaxHitPoints { get; }

    public int HitPoints { get; private set; }

    public int AttackPower { get; }

    public bool IsAlive => HitPoints > 0;

    public void TakeDamage(int amount)
    {
        HitPoints = Math.Max(0, HitPoints - Math.Max(0, amount));
    }
}

