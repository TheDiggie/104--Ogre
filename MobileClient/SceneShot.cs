using System;
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

        var view = new FirstPersonView
        {
            ResourceDir = res,
            RoomFile = room,
            RenderWidth = int.TryParse(Arg("--width", "480"), out int rw) ? rw : 480,
        };
        AddChild(view);

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

    static string Arg(string name, string fallback)
    {
        string[] a = OS.GetCmdlineUserArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == name) return a[i + 1];
        return fallback;
    }
}
