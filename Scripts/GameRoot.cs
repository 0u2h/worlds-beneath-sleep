using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace WorldsBeneathSleep;

public partial class GameRoot : Node2D
{
    private const int MapWidth = 36;
    private const int MapHeight = 22;
    private const int TileSize = 32;
    private const int SightRadius = 9;
    private const int EnemyCount = 8;
    private const double AutoWalkStepSeconds = 0.09;
    private const float SidebarWidth = 210f;

    private readonly Queue<Vector2I> _autoWalkSteps = new();
    private readonly List<string> _messageLog = new();

    private TileGrid _grid = null!;
    private Label _infoLabel = null!;
    private GridActor _player = null!;
    private bool _playerDead;
    private double _autoWalkTimer;
    private Vector2I? _previewTarget;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.02f, 0.03f, 0.05f));
        BuildScene();
        BuildLevel();
    }

    public override void _Process(double delta)
    {
        UpdateHoveredTile();

        if (_playerDead || _autoWalkSteps.Count == 0)
        {
            return;
        }

        _autoWalkTimer += delta;
        if (_autoWalkTimer >= AutoWalkStepSeconds)
        {
            _autoWalkTimer = 0;
            AdvanceAutoWalk();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                TryStartAutoWalk();
                return;
            }

            if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                StopAutoWalk();
                return;
            }
        }

        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.PhysicalKeycode == Key.R)
        {
            BuildLevel();
            return;
        }

        if (keyEvent.Keycode == Key.Escape)
        {
            StopAutoWalk();
            return;
        }

        if (_playerDead)
        {
            return;
        }

        if (TryResolveMoveInput(keyEvent, out Vector2I delta))
        {
            StopAutoWalk(false);
            TryTakePlayerTurn(delta);
            return;
        }

        if (keyEvent.Keycode == Key.Period || keyEvent.Keycode == Key.Kp5)
        {
            StopAutoWalk(false);
            TakeWaitTurn();
        }
    }

    private void BuildScene()
    {
        _grid = new TileGrid
        {
            Name = "TileGrid"
        };
        AddChild(_grid);

        CanvasLayer uiLayer = new()
        {
            Name = "Ui"
        };
        AddChild(uiLayer);

        _infoLabel = new Label
        {
            Name = "InfoLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.94f, 0.93f, 0.87f)
        };
        uiLayer.AddChild(_infoLabel);
        PositionInfoLabel();
    }

    private void BuildLevel()
    {
        DemoMapData map = DemoMapGenerator.Generate(MapWidth, MapHeight, EnemyCount);
        _grid.Initialize(MapWidth, MapHeight, TileSize);
        _grid.MapOffset = CalculateMapOffset();

        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                _grid.SetTerrain(new Vector2I(x, y), map.Terrain[x, y]);
            }
        }

        _player = new GridActor("Dreamer", "@", new Color(0.97f, 0.94f, 0.82f), map.PlayerStart, true, 14, 3);
        _grid.AddActor(_player);

        foreach (Vector2I spawn in map.EnemySpawns)
        {
            GridActor enemy = new(
                "Nightling",
                "n",
                new Color(0.78f, 0.43f, 0.43f),
                spawn,
                false,
                4,
                1);
            _grid.AddActor(enemy);
        }

        _playerDead = false;
        _autoWalkTimer = 0;
        _autoWalkSteps.Clear();
        _messageLog.Clear();
        AddMessage("Cross the ruins and bump enemies to strike.");
        AddMessage("Left click queues ToME-style auto-walk.");
        RefreshVisibility();
        RefreshInfoPanel();
        RefreshPreviewPath(force: true);
    }

    private void UpdateHoveredTile()
    {
        Vector2I? hoveredCell = _grid.CellAtGlobalPosition(GetGlobalMousePosition());
        if (_grid.HoveredCell == hoveredCell)
        {
            return;
        }

        _grid.HoveredCell = hoveredCell;
        RefreshPreviewPath(force: true);
        RefreshInfoPanel();
    }

    private void RefreshPreviewPath(bool force = false)
    {
        Vector2I? hoveredCell = _grid.HoveredCell;
        if (!force && _previewTarget == hoveredCell)
        {
            return;
        }

        _previewTarget = hoveredCell;

        if (hoveredCell is not Vector2I target
            || !_grid.IsVisible(target)
            || target == _player.Cell
            || _grid.GetActorAt(target) is not null)
        {
            _grid.ClearPreviewPath();
            return;
        }

        List<Vector2I> path = _grid.FindPath(_player.Cell, target, false, _player);
        if (path.Count == 0)
        {
            _grid.ClearPreviewPath();
            return;
        }

        _grid.SetPreviewPath(path);
    }

    private void TryStartAutoWalk()
    {
        if (_playerDead || _grid.HoveredCell is not Vector2I target)
        {
            return;
        }

        if (!_grid.IsVisible(target) || _grid.GetActorAt(target) is not null || target == _player.Cell)
        {
            return;
        }

        List<Vector2I> path = _grid.FindPath(_player.Cell, target, false, _player);
        if (path.Count == 0)
        {
            return;
        }

        _autoWalkSteps.Clear();
        foreach (Vector2I step in path)
        {
            _autoWalkSteps.Enqueue(step);
        }

        _autoWalkTimer = 0;
        AdvanceAutoWalk();
        RefreshInfoPanel();
    }

    private void AdvanceAutoWalk()
    {
        if (_autoWalkSteps.Count == 0)
        {
            return;
        }

        Vector2I nextStep = _autoWalkSteps.Peek();
        Vector2I delta = nextStep - _player.Cell;

        if (!_grid.CanStep(_player.Cell, nextStep, false, _player))
        {
            StopAutoWalk();
            return;
        }

        _autoWalkSteps.Dequeue();
        if (!TryTakePlayerTurn(delta))
        {
            StopAutoWalk();
        }
    }

    private bool TryTakePlayerTurn(Vector2I delta)
    {
        if (delta == Vector2I.Zero)
        {
            TakeWaitTurn();
            return true;
        }

        Vector2I destination = _player.Cell + delta;
        if (!_grid.IsInBounds(destination))
        {
            return false;
        }

        GridActor? target = _grid.GetActorAt(destination);
        if (target is not null && target.IsPlayer != _player.IsPlayer)
        {
            ResolveAttack(_player, target);
            EndPlayerTurn();
            return true;
        }

        if (!_grid.TryMoveActor(_player, destination))
        {
            return false;
        }

        EndPlayerTurn();
        return true;
    }

    private void TakeWaitTurn()
    {
        AddMessage("The Dreamer waits.");
        EndPlayerTurn();
    }

    private void EndPlayerTurn()
    {
        ProcessEnemies();
        RefreshVisibility();

        if (_autoWalkSteps.Count > 0 && AnyVisibleEnemies())
        {
            StopAutoWalk(false);
            AddMessage("Auto-walk interrupted by contact.");
        }

        RefreshPreviewPath(force: true);
        RefreshInfoPanel();
    }

    private void ProcessEnemies()
    {
        List<GridActor> enemies = _grid.Actors.Where(actor => !actor.IsPlayer).ToList();
        foreach (GridActor enemy in enemies)
        {
            if (_playerDead || !enemy.IsAlive)
            {
                break;
            }

            int distance = ChebyshevDistance(enemy.Cell, _player.Cell);
            if (distance <= 1)
            {
                ResolveAttack(enemy, _player);
                continue;
            }

            if (distance > 10 || !_grid.HasLineOfSight(enemy.Cell, _player.Cell))
            {
                continue;
            }

            List<Vector2I> path = _grid.FindPath(enemy.Cell, _player.Cell, false, enemy, allowOccupiedGoal: true);
            if (path.Count == 0)
            {
                continue;
            }

            Vector2I nextStep = path[0];
            if (nextStep == _player.Cell)
            {
                ResolveAttack(enemy, _player);
                continue;
            }

            _grid.TryMoveActor(enemy, nextStep);
        }
    }

    private void ResolveAttack(GridActor attacker, GridActor defender)
    {
        defender.TakeDamage(attacker.AttackPower);
        AddMessage($"{attacker.Name} hits {defender.Name} for {attacker.AttackPower}.");

        if (defender.IsAlive)
        {
            if (defender.IsPlayer)
            {
                RefreshInfoPanel();
            }
            return;
        }

        _grid.RemoveActor(defender);
        AddMessage($"{defender.Name} falls.");

        if (!defender.IsPlayer)
        {
            return;
        }

        _playerDead = true;
        _autoWalkSteps.Clear();
        AddMessage("The Dreamer dies. Press R to rebuild the dungeon.");
    }

    private void RefreshVisibility()
    {
        _grid.RecomputeVisibility(_player.Cell, SightRadius);
    }

    private bool AnyVisibleEnemies()
    {
        return _grid.Actors.Any(actor => !actor.IsPlayer && _grid.IsVisible(actor.Cell));
    }

    private void StopAutoWalk(bool refreshInfo = true)
    {
        _autoWalkSteps.Clear();
        _autoWalkTimer = 0;
        if (refreshInfo)
        {
            RefreshInfoPanel();
        }
    }

    private void PositionInfoLabel()
    {
        Vector2 viewportSize = GetViewportRect().Size;
        _infoLabel.Position = new Vector2(viewportSize.X - SidebarWidth - 24f, 20f);
        _infoLabel.Size = new Vector2(SidebarWidth, viewportSize.Y - 40f);
    }

    private Vector2 CalculateMapOffset()
    {
        Vector2 viewportSize = GetViewportRect().Size;
        float usableWidth = viewportSize.X - SidebarWidth - 64f;
        float x = Mathf.Max(24f, (usableWidth - _grid.MapPixelSize.X) * 0.5f);
        float y = Mathf.Max(24f, (viewportSize.Y - _grid.MapPixelSize.Y) * 0.5f);
        return new Vector2(x, y);
    }

    private void RefreshInfoPanel()
    {
        PositionInfoLabel();

        int livingEnemies = _grid.Actors.Count(actor => !actor.IsPlayer);
        string hoverInfo = DescribeHoveredCell();
        string mode = _autoWalkSteps.Count > 0 ? "Auto-walk" : "Manual";

        StringBuilder builder = new();
        builder.AppendLine("Worlds Beneath Sleep");
        builder.AppendLine();
        builder.AppendLine($"HP: {_player.HitPoints}/{_player.MaxHitPoints}");
        builder.AppendLine($"Enemies: {livingEnemies}");
        builder.AppendLine($"Mode: {mode}");
        builder.AppendLine();
        builder.AppendLine("Controls");
        builder.AppendLine("WASD move");
        builder.AppendLine("QEZC diagonal");
        builder.AppendLine(". wait");
        builder.AppendLine("LMB auto-walk");
        builder.AppendLine("R rebuild");
        builder.AppendLine();
        builder.AppendLine("Hover");
        builder.AppendLine(hoverInfo);
        builder.AppendLine();
        builder.AppendLine("Log");

        foreach (string message in _messageLog)
        {
            builder.AppendLine(message);
        }

        _infoLabel.Text = builder.ToString();
    }

    private string DescribeHoveredCell()
    {
        if (_grid.HoveredCell is not Vector2I cell)
        {
            return "Outside map";
        }

        if (!_grid.IsExplored(cell))
        {
            return $"{cell.X}, {cell.Y} Unknown";
        }

        GridActor? actor = _grid.GetActorAt(cell);
        if (actor is not null && _grid.IsVisible(cell))
        {
            return $"{cell.X}, {cell.Y} {actor.Name} ({actor.HitPoints} HP)";
        }

        string terrain = _grid.GetTerrain(cell) == TileTerrain.Wall ? "Wall" : "Floor";
        string visibility = _grid.IsVisible(cell) ? "Visible" : "Memory";
        return $"{cell.X}, {cell.Y} {terrain} {visibility}";
    }

    private void AddMessage(string message)
    {
        _messageLog.Add(message);
        while (_messageLog.Count > 7)
        {
            _messageLog.RemoveAt(0);
        }
    }

    private static int ChebyshevDistance(Vector2I a, Vector2I b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private static bool TryResolveMoveInput(InputEventKey keyEvent, out Vector2I delta)
    {
        delta = keyEvent.PhysicalKeycode switch
        {
            Key.W or Key.Kp8 => new Vector2I(0, -1),
            Key.S or Key.Kp2 => new Vector2I(0, 1),
            Key.A or Key.Kp4 => new Vector2I(-1, 0),
            Key.D or Key.Kp6 => new Vector2I(1, 0),
            Key.Q or Key.Kp7 => new Vector2I(-1, -1),
            Key.E or Key.Kp9 => new Vector2I(1, -1),
            Key.Z or Key.Kp1 => new Vector2I(-1, 1),
            Key.C or Key.Kp3 => new Vector2I(1, 1),
            _ => Vector2I.Zero
        };

        return delta != Vector2I.Zero;
    }
}
