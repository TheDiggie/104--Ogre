using System;
using Godot;

/// <summary>
/// Finds out how far the app gets before it dies.
///
/// The client crashed on a phone with "PlatformNotSupportedException:
/// Operation is not supported on this platform" and no stack, and the
/// handlers that would have printed one never ran - so whatever throws
/// happens before GameView._Ready, and nothing inside GameView can
/// report it. This scene touches one thing at a time and writes each
/// step to the screen as it passes, so the last line standing names the
/// step that failed.
///
/// A blank screen means it did not even get here, which would put the
/// fault in loading the assembly rather than in anything it does.
/// </summary>
public partial class Probe : Node2D
{
    Label _out;
    string _text = "";

    public override void _Ready()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        var bg = new ColorRect { Color = new Color(0.03f, 0.03f, 0.05f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);

        _out = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _out.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _out.OffsetLeft = 16; _out.OffsetTop = 48;
        _out.OffsetRight = -16; _out.OffsetBottom = -16;
        _out.AddThemeFontSizeOverride("font_size", 22);
        layer.AddChild(_out);

        Say("probe running");
        Say($"platform: {OS.GetName()}  model: {OS.GetModelName()}");

        Step("touch the library", () =>
        {
            string enc = Meridian59.Common.Util.Encoding.WebName;
            Say($"  encoding: {enc}");
        });

        Step("read the paths", () =>
        {
            Say($"  user dir: {OS.GetUserDataDir()}");
            Say($"  needs unpack: {M59Paths.NeedsUnpack()}");
        });

        Step("resolve the resource folder", () =>
        {
            string dir = M59Paths.Resolve("");
            Say($"  resources: {dir ?? "(none found)"}");
        });

        Step("build a client", () =>
        {
            var c = new M59Client();
            Say($"  client: {c.GetType().Name}");
        });

        Step("open a socket", () =>
        {
            Say($"  IPv6 available: {System.Net.Sockets.Socket.OSSupportsIPv6}");
            using var s = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetworkV6,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);
            s.SetSocketOption(System.Net.Sockets.SocketOptionLevel.IPv6,
                              System.Net.Sockets.SocketOptionName.IPv6Only, false);
            Say("  dual-stack socket ok");
        });

        // The game app never got past this: the probe found the data
        // still packed, and it shares the app's storage - so if the game
        // had unpacked it once, this would say False.
        Step("unpack the game data", () =>
        {
            if (!M59Paths.NeedsUnpack()) { Say("  already unpacked"); return; }
            int n = M59Paths.UnpackIfNeeded(m => Say("  " + m));
            Say($"  wrote {n} files");
        });

        Step("resolve again after unpacking", () =>
        {
            Say($"  resources: {M59Paths.Resolve("") ?? "(none found)"}");
        });

        Step("compose a frame", () =>
        {
            // The composer is the one thing here that touches the
            // library's drawing code, which is where a desktop-only API
            // would most likely be hiding.
            string dir = M59Paths.Resolve("");
            if (dir == null) { Say("  skipped, no resources"); return; }
            var assets = new M59Assets();
            Say($"  assets init: {assets.Init(dir)}");
        });

        Say("");
        Say("done - everything above passed");
    }

    void Step(string what, Action body)
    {
        Say($"> {what}");
        try { body(); }
        catch (Exception e)
        {
            Say($"  FAILED {e.GetType().Name}: {e.Message}");
            foreach (string line in (e.StackTrace ?? "").Split('\n'))
                if (line.Trim().Length > 0) Say("   " + line.Trim());
        }
    }

    void Say(string line)
    {
        _text += line + "\n";
        if (_out != null) _out.Text = _text;
        GD.Print("[Probe] " + line);
    }
}
