namespace WorldsBeneathSleep;

public sealed class GridTile
{
    public TileTerrain Terrain { get; set; } = TileTerrain.Wall;

    public bool Visible { get; set; }

    public bool Explored { get; set; }

    public GridActor? Occupant { get; set; }

    public bool IsOpaque => Terrain == TileTerrain.Wall;
}
