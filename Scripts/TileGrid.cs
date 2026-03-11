using System;
using System.Collections.Generic;
using Godot;

namespace WorldsBeneathSleep;

public partial class TileGrid : Node2D
{
    private static readonly Vector2I[] NeighborOffsets =
    {
        new(-1, -1),
        new(0, -1),
        new(1, -1),
        new(-1, 0),
        new(1, 0),
        new(-1, 1),
        new(0, 1),
        new(1, 1)
    };

    private GridTile[,] _tiles = new GridTile[1, 1];
    private readonly List<GridActor> _actors = new();
    private readonly Dictionary<Vector2I, GridActor> _actorsByCell = new();
    private HashSet<Vector2I> _previewPath = new();
    private Vector2I? _hoveredCell;
    private Vector2 _mapOffset = Vector2.Zero;

    public int Width { get; private set; } = 1;

    public int Height { get; private set; } = 1;

    public int TileSize { get; private set; } = 32;

    public Vector2 MapPixelSize => new(Width * TileSize, Height * TileSize);

    public IReadOnlyList<GridActor> Actors => _actors;

    public Vector2 MapOffset
    {
        get => _mapOffset;
        set
        {
            _mapOffset = value;
            QueueRedraw();
        }
    }

    public Vector2I? HoveredCell
    {
        get => _hoveredCell;
        set
        {
            if (_hoveredCell == value)
            {
                return;
            }

            _hoveredCell = value;
            QueueRedraw();
        }
    }

    public void Initialize(int width, int height, int tileSize)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        _tiles = new GridTile[width, height];
        _actors.Clear();
        _actorsByCell.Clear();
        _previewPath.Clear();
        _hoveredCell = null;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                _tiles[x, y] = new GridTile();
            }
        }

        QueueRedraw();
    }

    public bool IsInBounds(Vector2I cell)
    {
        return cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;
    }

    public TileTerrain GetTerrain(Vector2I cell)
    {
        return IsInBounds(cell) ? _tiles[cell.X, cell.Y].Terrain : TileTerrain.Wall;
    }

    public bool IsVisible(Vector2I cell)
    {
        return IsInBounds(cell) && _tiles[cell.X, cell.Y].Visible;
    }

    public bool IsExplored(Vector2I cell)
    {
        return IsInBounds(cell) && _tiles[cell.X, cell.Y].Explored;
    }

    public GridActor? GetActorAt(Vector2I cell)
    {
        return IsInBounds(cell) ? _tiles[cell.X, cell.Y].Occupant : null;
    }

    public void SetTerrain(Vector2I cell, TileTerrain terrain)
    {
        if (!IsInBounds(cell))
        {
            return;
        }

        _tiles[cell.X, cell.Y].Terrain = terrain;
    }

    public bool CanStep(
        Vector2I from,
        Vector2I to,
        bool ignoreActors = false,
        GridActor? movingActor = null,
        bool allowOccupiedGoal = false)
    {
        if (!IsInBounds(from) || !IsInBounds(to))
        {
            return false;
        }

        Vector2I delta = to - from;
        if (delta == Vector2I.Zero || Math.Abs(delta.X) > 1 || Math.Abs(delta.Y) > 1)
        {
            return false;
        }

        if (delta.X != 0 && delta.Y != 0 && CutsCorner(from, delta))
        {
            return false;
        }

        return IsWalkable(to, ignoreActors, movingActor, allowOccupiedGoal);
    }

    public bool AddActor(GridActor actor)
    {
        if (!IsWalkable(actor.Cell))
        {
            return false;
        }

        _actors.Add(actor);
        _actorsByCell[actor.Cell] = actor;
        _tiles[actor.Cell.X, actor.Cell.Y].Occupant = actor;
        QueueRedraw();
        return true;
    }

    public bool TryMoveActor(GridActor actor, Vector2I target)
    {
        if (!_actorsByCell.ContainsKey(actor.Cell) || !CanStep(actor.Cell, target, false, actor))
        {
            return false;
        }

        _tiles[actor.Cell.X, actor.Cell.Y].Occupant = null;
        _actorsByCell.Remove(actor.Cell);
        actor.Cell = target;
        _tiles[target.X, target.Y].Occupant = actor;
        _actorsByCell[target] = actor;
        QueueRedraw();
        return true;
    }

    public void RemoveActor(GridActor actor)
    {
        if (_actorsByCell.TryGetValue(actor.Cell, out GridActor? current) && current == actor)
        {
            _tiles[actor.Cell.X, actor.Cell.Y].Occupant = null;
            _actorsByCell.Remove(actor.Cell);
        }

        _actors.Remove(actor);
        QueueRedraw();
    }

    public Vector2I? CellAtGlobalPosition(Vector2 globalPosition)
    {
        Vector2 local = ToLocal(globalPosition) - MapOffset;
        if (local.X < 0 || local.Y < 0)
        {
            return null;
        }

        Vector2I cell = new((int)(local.X / TileSize), (int)(local.Y / TileSize));
        return IsInBounds(cell) ? cell : null;
    }

    public List<Vector2I> FindPath(
        Vector2I start,
        Vector2I goal,
        bool ignoreActors = false,
        GridActor? movingActor = null,
        bool allowOccupiedGoal = false)
    {
        List<Vector2I> path = new();
        if (!IsInBounds(start) || !IsInBounds(goal) || start == goal)
        {
            return path;
        }

        if (!IsWalkable(goal, ignoreActors, movingActor, allowOccupiedGoal))
        {
            return path;
        }

        PriorityQueue<Vector2I, int> frontier = new();
        frontier.Enqueue(start, 0);

        Dictionary<Vector2I, Vector2I> cameFrom = new();
        Dictionary<Vector2I, int> costSoFar = new() { [start] = 0 };

        while (frontier.Count > 0)
        {
            Vector2I current = frontier.Dequeue();
            if (current == goal)
            {
                break;
            }

            foreach (Vector2I next in GetPathNeighbors(current, ignoreActors, movingActor, goal, allowOccupiedGoal))
            {
                int stepCost = current.X != next.X && current.Y != next.Y ? 14 : 10;
                int newCost = costSoFar[current] + stepCost;

                if (costSoFar.TryGetValue(next, out int knownCost) && newCost >= knownCost)
                {
                    continue;
                }

                costSoFar[next] = newCost;
                frontier.Enqueue(next, newCost + OctileDistance(next, goal));
                cameFrom[next] = current;
            }
        }

        if (!cameFrom.ContainsKey(goal))
        {
            return path;
        }

        Vector2I cursor = goal;
        while (cursor != start)
        {
            path.Add(cursor);
            cursor = cameFrom[cursor];
        }

        path.Reverse();
        return path;
    }

    public bool HasLineOfSight(Vector2I start, Vector2I goal)
    {
        if (!IsInBounds(start) || !IsInBounds(goal))
        {
            return false;
        }

        foreach (Vector2I cell in TraceLine(start, goal))
        {
            if (cell == start)
            {
                continue;
            }

            if (cell == goal)
            {
                return true;
            }

            if (_tiles[cell.X, cell.Y].IsOpaque)
            {
                return false;
            }
        }

        return true;
    }

    public void RecomputeVisibility(Vector2I origin, int radius)
    {
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                _tiles[x, y].Visible = false;
            }
        }

        if (!IsInBounds(origin))
        {
            QueueRedraw();
            return;
        }

        int radiusSquared = radius * radius;
        for (int x = origin.X - radius; x <= origin.X + radius; x++)
        {
            for (int y = origin.Y - radius; y <= origin.Y + radius; y++)
            {
                Vector2I cell = new(x, y);
                if (!IsInBounds(cell))
                {
                    continue;
                }

                int dx = x - origin.X;
                int dy = y - origin.Y;
                if ((dx * dx) + (dy * dy) > radiusSquared)
                {
                    continue;
                }

                if (!HasLineOfSight(origin, cell))
                {
                    continue;
                }

                _tiles[x, y].Visible = true;
                _tiles[x, y].Explored = true;
            }
        }

        QueueRedraw();
    }

    public void SetPreviewPath(IEnumerable<Vector2I> path)
    {
        _previewPath = new HashSet<Vector2I>(path);
        QueueRedraw();
    }

    public void ClearPreviewPath()
    {
        if (_previewPath.Count == 0)
        {
            return;
        }

        _previewPath.Clear();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(
            new Rect2(MapOffset - new Vector2(12, 12), MapPixelSize + new Vector2(24, 24)),
            new Color(0.03f, 0.04f, 0.06f),
            true);

        Font font = ThemeDB.FallbackFont;
        int terrainFontSize = Math.Max(10, TileSize / 2);
        int actorFontSize = Math.Max(16, TileSize - 8);

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector2I cell = new(x, y);
                GridTile tile = _tiles[x, y];
                Rect2 rect = CellRect(cell);

                DrawRect(rect, ResolveTileColor(cell, tile), true);

                if (tile.Explored)
                {
                    DrawString(
                        font,
                        rect.Position + new Vector2(0f, TileSize * 0.70f),
                        tile.Terrain == TileTerrain.Wall ? "#" : ".",
                        HorizontalAlignment.Center,
                        TileSize,
                        terrainFontSize,
                        ResolveTerrainGlyphColor(tile));
                }

                if (_previewPath.Contains(cell) && tile.Visible)
                {
                    DrawRect(rect.Grow(-5f), new Color(0.54f, 0.81f, 0.35f, 0.22f), true);
                }

                if (_hoveredCell == cell)
                {
                    DrawRect(rect.Grow(-3f), new Color(0.96f, 0.80f, 0.35f, 0.24f), true);
                }

                DrawRect(rect, new Color(0f, 0f, 0f, 0.28f), false, 1f);
            }
        }

        foreach (GridActor actor in _actors)
        {
            if (!IsVisible(actor.Cell))
            {
                continue;
            }

            Rect2 rect = CellRect(actor.Cell);
            DrawString(
                font,
                rect.Position + new Vector2(0f, TileSize * 0.76f),
                actor.Glyph,
                HorizontalAlignment.Center,
                TileSize,
                actorFontSize,
                actor.GlyphColor);
        }
    }

    private IEnumerable<Vector2I> GetPathNeighbors(
        Vector2I cell,
        bool ignoreActors,
        GridActor? movingActor,
        Vector2I goal,
        bool allowOccupiedGoal)
    {
        foreach (Vector2I offset in NeighborOffsets)
        {
            Vector2I next = cell + offset;
            bool allowGoal = allowOccupiedGoal && next == goal;
            if (CanStep(cell, next, ignoreActors, movingActor, allowGoal))
            {
                yield return next;
            }
        }
    }

    private bool IsWalkable(
        Vector2I cell,
        bool ignoreActors = false,
        GridActor? movingActor = null,
        bool allowOccupiedGoal = false)
    {
        if (!IsInBounds(cell))
        {
            return false;
        }

        GridTile tile = _tiles[cell.X, cell.Y];
        if (tile.Terrain == TileTerrain.Wall)
        {
            return false;
        }

        if (ignoreActors || tile.Occupant is null)
        {
            return true;
        }

        if (movingActor is not null && tile.Occupant == movingActor)
        {
            return true;
        }

        return allowOccupiedGoal;
    }

    private bool CutsCorner(Vector2I from, Vector2I delta)
    {
        Vector2I horizontal = new(from.X + delta.X, from.Y);
        Vector2I vertical = new(from.X, from.Y + delta.Y);
        return !IsWalkable(horizontal, ignoreActors: true) || !IsWalkable(vertical, ignoreActors: true);
    }

    private Rect2 CellRect(Vector2I cell)
    {
        return new Rect2(MapOffset + new Vector2(cell.X * TileSize, cell.Y * TileSize), new Vector2(TileSize, TileSize));
    }

    private static int OctileDistance(Vector2I a, Vector2I b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        int diagonal = Math.Min(dx, dy);
        int straight = Math.Max(dx, dy) - diagonal;
        return (diagonal * 14) + (straight * 10);
    }

    private static IEnumerable<Vector2I> TraceLine(Vector2I from, Vector2I to)
    {
        int x0 = from.X;
        int y0 = from.Y;
        int x1 = to.X;
        int y1 = to.Y;
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            yield return new Vector2I(x0, y0);
            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }

            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static Color ResolveTileColor(Vector2I cell, GridTile tile)
    {
        if (!tile.Explored)
        {
            return new Color(0.03f, 0.04f, 0.06f);
        }

        bool checker = ((cell.X + cell.Y) & 1) == 0;
        if (tile.Terrain == TileTerrain.Wall)
        {
            return tile.Visible
                ? (checker ? new Color(0.35f, 0.31f, 0.27f) : new Color(0.32f, 0.28f, 0.24f))
                : new Color(0.14f, 0.13f, 0.15f);
        }

        return tile.Visible
            ? (checker ? new Color(0.14f, 0.18f, 0.24f) : new Color(0.12f, 0.16f, 0.22f))
            : new Color(0.08f, 0.10f, 0.14f);
    }

    private static Color ResolveTerrainGlyphColor(GridTile tile)
    {
        if (!tile.Explored)
        {
            return new Color(0, 0, 0, 0);
        }

        return tile.Visible
            ? new Color(0.86f, 0.83f, 0.72f, 0.45f)
            : new Color(0.56f, 0.56f, 0.60f, 0.25f);
    }
}
