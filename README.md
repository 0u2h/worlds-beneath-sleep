# Worlds Beneath Sleep

Minimal Godot 4 C# prototype for a Tales of Maj'Eyal-style tile system.

## Features

- Square-tile dungeon grid with terrain and occupancy state
- Turn-based 8-direction movement
- Mouse hover highlighting and click-to-auto-walk
- A* pathfinding with diagonal corner blocking
- Simple field of view and fog of war
- Basic enemy turns and bump attacks

## Requirements

- Godot 4.2 or newer with the .NET build
- .NET 8 SDK

## Run

1. Open the project in Godot.
2. Let Godot restore NuGet packages for the C# project if prompted.
3. Run `Scenes/Main.tscn`.

## Controls

- `W`, `A`, `S`, `D`: move
- `Q`, `E`, `Z`, `C`: diagonal move
- `.` or numpad `5`: wait
- Left click: auto-walk to the hovered tile
- Right click or `Esc`: stop auto-walk
- `R`: rebuild the dungeon

