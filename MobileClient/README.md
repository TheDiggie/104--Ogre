# MobileClient

Godot 4 (.NET) front end for Meridian 59, built on the ported
`Meridian59` core library. Android and iOS are the targets; the desktop
editor is just where it gets developed.

## Requirements

- Godot **.NET** build (the download labelled "Windows - .NET", file
  name `Godot_v<version>-stable_mono_win64.zip`). The standard build
  cannot run C# at all.
- .NET 8 SDK.

## Running

Open this folder as a project in the Godot .NET editor and press play.
It builds `MobileClient.csproj`, which references
`../Meridian59/net8.csproj`.

On first run it looks for rooms in
`%LOCALAPPDATA%\Meridian-104\resource`. To point somewhere else, select
the RoomView node and set **Resource Dir** in the inspector.

## Current state

One milestone only: load a ROO and draw its walls top-down. Dark lines
are one-sided walls, blue lines have a sector on both sides. Drag to
pan, wheel or pinch to zoom.

The output should look **identical** to the PNG from

    dotnet run --project Tools/Meridian59.Net8Room -- <resource dir> <out> 900

for the same room. That PNG was checked against the wiki map, so it is
the reference - if Godot draws something different, the bug is in this
project, not the library.

Nothing here talks to a server yet.
