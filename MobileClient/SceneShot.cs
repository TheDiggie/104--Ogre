using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Screenshots the offline first-person view: the renderer, the textures,
/// the minimap and the touch controls, all of it running inside Godot
/// rather than in a test harness. Nothing else loads this scene.
///
///     godot --path MobileClient SceneShot.tscn -- \
///       --out room.png --res /tmp/res --room barinn.roo --wait 30
///
/// A virtual display is enough; `--headless` renders nothing.
/// </summary>
public partial class SceneShot : Node
{
    public override void _Ready()
    {
        string outPath = Arg("--out", "room.png");
        string res = Arg("--res", "/tmp/res");
        string room = Arg("--room", "barinn.roo");
        int wait = int.TryParse(Arg("--wait", "30"), out int w) ? w : 30;

        int width = int.TryParse(Arg("--width", "480"), out int rw) ? rw : 480;

        // --host turns this into a shot of the live view instead of the
        // offline one, pointed wherever you say - which in practice means
        // Tools/Meridian59.Net8FakeServer on loopback.
        string host = Arg("--host", null);
        if (host != null)
        {
            var live = new GameView
            {
                ResourceDir = res,
                Host = host,
                Port = int.TryParse(Arg("--port", "15999"), out int pt) ? pt : 15999,
                RenderWidth = width,
                AutoConnect = true,
            };
            AddChild(live);
        }
        else
        {
            var view = new FirstPersonView
            {
                ResourceDir = res,
                RoomFile = room,
                RenderWidth = width,
            };
            AddChild(view);
        }

        // --walk <frames> holds the forward key down and photographs the
        // result every few frames, which is how the movement path gets
        // exercised at all: it runs through the same input handling a
        // thumb on the stick does.
        if (int.TryParse(Arg("--walk", "0"), out int walk) && walk > 0)
            Walk(outPath, wait, walk, Arg("--keys", "W"));
        else
            Shoot(outPath, wait);
    }

    /// <summary>
    /// Waits a set number of frames before reading the screen back. The
    /// view loads its room and builds its textures over several frames,
    /// and a shot taken too early catches a black screen with a status
    /// line on it - which looks exactly like a broken renderer.
    /// </summary>
    async void Shoot(string path, int frames)
    {
        for (int i = 0; i < Math.Max(1, frames); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image img = GetViewport().GetTexture().GetImage();
        Error e = img.SavePng(path);
        GD.Print(e == Error.Ok ? $"[SceneShot] wrote {path}" : $"[SceneShot] save failed: {e}");
        GetTree().Quit();
    }

    /// <summary>
    /// Holds keys down and takes a numbered shot every few frames. The
    /// keys go in through Godot's own input queue rather than by poking
    /// the view, so what is tested is the path a real press takes.
    /// </summary>
    async void Walk(string path, int settle, int frames, string keys)
    {
        for (int i = 0; i < Math.Max(1, settle); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var held = new List<Key>();
        foreach (char k in keys.ToUpperInvariant())
            if (Enum.TryParse("Key" + k, out Key parsed) || Enum.TryParse(k.ToString(), out parsed))
                held.Add(parsed);

        foreach (Key k in held)
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });

        int shot = 0;
        for (int i = 0; i <= frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (i % Math.Max(1, frames / 8) != 0) continue;

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image img = GetViewport().GetTexture().GetImage();
            string numbered = path.Replace(".png", $"-{shot:D2}.png");
            img.SavePng(numbered);
            GD.Print($"[SceneShot] wrote {numbered}");
            shot++;
        }

        foreach (Key k in held)
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });

        GetTree().Quit();
    }

    static string Arg(string name, string fallback)
    {
        string[] a = OS.GetCmdlineUserArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == name) return a[i + 1];
        return fallback;
    }
}
