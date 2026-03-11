using System.Collections.Generic;
using Godot;

namespace WorldsBeneathSleep;

public sealed class DemoMapData
{
    public DemoMapData(int width, int height)
    {
        Width = width;
        Height = height;
        Terrain = new TileTerrain[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Terrain[x, y] = TileTerrain.Wall;
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public TileTerrain[,] Terrain { get; }

    public Vector2I PlayerStart { get; set; } = new(1, 1);

    public List<Vector2I> EnemySpawns { get; } = new();
}

public static class DemoMapGenerator
{
    private sealed class Room
    {
        public Room(Rect2I bounds)
        {
            Bounds = bounds;
            Center = new Vector2I(bounds.Position.X + (bounds.Size.X / 2), bounds.Position.Y + (bounds.Size.Y / 2));
        }

        public Rect2I Bounds { get; }

        public Vector2I Center { get; }
    }

    public static DemoMapData Generate(int width, int height, int enemyCount)
    {
        DemoMapData data = new(width, height);
        RandomNumberGenerator rng = new();
        rng.Randomize();

        List<Room> rooms = new();
        const int maxRooms = 10;
        const int attempts = 64;

        for (int i = 0; i < attempts && rooms.Count < maxRooms; i++)
        {
            int roomWidth = rng.RandiRange(5, 9);
            int roomHeight = rng.RandiRange(5, 8);
            int x = rng.RandiRange(2, width - roomWidth - 3);
            int y = rng.RandiRange(2, height - roomHeight - 3);
            Rect2I candidate = new(x, y, roomWidth, roomHeight);
            Rect2I padded = new(x - 1, y - 1, roomWidth + 2, roomHeight + 2);

            bool overlaps = false;
            foreach (Room room in rooms)
            {
                if (padded.Intersects(room.Bounds))
                {
                    overlaps = true;
                    break;
                }
            }

            if (overlaps)
            {
                continue;
            }

            CarveRoom(data, candidate);
            Room newRoom = new(candidate);

            if (rooms.Count > 0)
            {
                ConnectRooms(data, rooms[^1].Center, newRoom.Center, rng);
            }

            rooms.Add(newRoom);
        }

        if (rooms.Count == 0)
        {
            Rect2I fallback = new(width / 2 - 3, height / 2 - 3, 7, 7);
            CarveRoom(data, fallback);
            rooms.Add(new Room(fallback));
        }

        data.PlayerStart = rooms[0].Center;

        for (int i = 1; i < rooms.Count && data.EnemySpawns.Count < enemyCount; i++)
        {
            data.EnemySpawns.Add(rooms[i].Center);
        }

        while (data.EnemySpawns.Count < enemyCount)
        {
            Vector2I randomFloor = FindRandomFloor(data, rng, rooms);
            if (randomFloor != data.PlayerStart && !data.EnemySpawns.Contains(randomFloor))
            {
                data.EnemySpawns.Add(randomFloor);
            }
        }

        return data;
    }

    private static void CarveRoom(DemoMapData data, Rect2I room)
    {
        for (int x = room.Position.X; x < room.End.X; x++)
        {
            for (int y = room.Position.Y; y < room.End.Y; y++)
            {
                data.Terrain[x, y] = TileTerrain.Floor;
            }
        }
    }

    private static void ConnectRooms(
        DemoMapData data,
        Vector2I start,
        Vector2I end,
        RandomNumberGenerator rng)
    {
        if (rng.Randf() < 0.5f)
        {
            CarveHorizontalTunnel(data, start.X, end.X, start.Y);
            CarveVerticalTunnel(data, start.Y, end.Y, end.X);
            return;
        }

        CarveVerticalTunnel(data, start.Y, end.Y, start.X);
        CarveHorizontalTunnel(data, start.X, end.X, end.Y);
    }

    private static void CarveHorizontalTunnel(DemoMapData data, int x1, int x2, int y)
    {
        int from = Mathf.Min(x1, x2);
        int to = Mathf.Max(x1, x2);

        for (int x = from; x <= to; x++)
        {
            data.Terrain[x, y] = TileTerrain.Floor;
        }
    }

    private static void CarveVerticalTunnel(DemoMapData data, int y1, int y2, int x)
    {
        int from = Mathf.Min(y1, y2);
        int to = Mathf.Max(y1, y2);

        for (int y = from; y <= to; y++)
        {
            data.Terrain[x, y] = TileTerrain.Floor;
        }
    }

    private static Vector2I FindRandomFloor(
        DemoMapData data,
        RandomNumberGenerator rng,
        IReadOnlyList<Room> rooms)
    {
        if (rooms.Count > 0)
        {
            Room room = rooms[rng.RandiRange(0, rooms.Count - 1)];
            return new Vector2I(
                rng.RandiRange(room.Bounds.Position.X, room.Bounds.End.X - 1),
                rng.RandiRange(room.Bounds.Position.Y, room.Bounds.End.Y - 1));
        }

        while (true)
        {
            int x = rng.RandiRange(1, data.Width - 2);
            int y = rng.RandiRange(1, data.Height - 2);
            if (data.Terrain[x, y] == TileTerrain.Floor)
            {
                return new Vector2I(x, y);
            }
        }
    }
}
