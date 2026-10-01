using System;
using Godot;
using Meridian59.Data;

/// <summary>
/// Screenshots the weather overlay and the drink blur over a real
/// picture of a room, so both can be looked at rather than reasoned
/// about. Not part of the game: it is attached to
/// <c>WeatherShot.tscn</c>, which nothing else loads.
///
/// The room picture is whatever PNG you hand it - in practice one
/// written by <c>Tools/Meridian59.Net8Fpv</c>, which is this client's
/// own renderer run without an engine, so the bytes going into
/// <see cref="ScreenEffects.Blur"/> here are the same shape and the same
/// content as the ones <c>GameView.RenderFrame</c> passes it.
///
///     godot --path MobileClient WeatherShot.tscn -- \
///        --world /tmp/world.png --out shot.png --rain --wait 90
///
/// A virtual display is needed; `--headless` renders nothing.
/// Flags: --rain, --snow, --blur, --clear (stop emitting partway, to
/// watch the fade-out that ClearWeather asks for), --wait frames.
/// </summary>
public partial class WeatherShot : Node
{
    string _out;
    int _wait, _clearAt;
    DataController _data;
    ScreenEffects _fx;
    TextureRect _view;
    ImageTexture _tex;
    Image _img;
    byte[] _rgba, _clean;
    int _w, _h, _frame;
    double _blurMs;

    public override void _Ready()
    {
        _out = Arg("--out", "weather.png");
        _wait = int.TryParse(Arg("--wait", "90"), out int n) ? n : 90;
        _clearAt = int.TryParse(Arg("--clear", "-1"), out int c) ? c : -1;

        // The world, as a picture. Kept twice: `_clean` is what the
        // renderer produced and `_rgba` is what the effects have had a
        // go at, because Blur works in place and a harness that blurred
        // its own output every frame would end up with fog.
        string world = Arg("--world", "/tmp/world.png");
        _img = Image.LoadFromFile(world);
        if (_img == null) { GD.PrintErr($"[WeatherShot] no {world}"); GetTree().Quit(); return; }
        _img.Convert(Image.Format.Rgba8);
        _w = _img.GetWidth(); _h = _img.GetHeight();
        _clean = _img.GetData();
        _rgba = (byte[])_clean.Clone();
        _tex = ImageTexture.CreateFromImage(_img);

        _view = new TextureRect
        {
            Texture = _tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var under = new CanvasLayer { Layer = -1 };
        AddChild(under);
        under.AddChild(_view);

        // The effects, on their own layer over the world, which is where
        // GameView puts them (GameView.cs:1069-1074).
        var layer = new CanvasLayer { Layer = 0 };
        AddChild(layer);
        _fx = new ScreenEffects { Verbose = Arg("--quiet", null) == null };
        layer.AddChild(_fx);

        // A data layer with nothing in it but the flags the server would
        // have set. `Effects.HandleEffect` is what normally sets these
        // (Effects.cs:324-334); setting them directly is the same state
        // without a socket.
        _data = new DataController();
        if (Arg("--rain", null) != null) _data.Effects.Raining.IsActive = true;
        if (Arg("--snow", null) != null) _data.Effects.Snowing.IsActive = true;
        if (Arg("--blur", null) != null) _data.Effects.Blur.StartOrExtend(600000);

        GD.Print($"[WeatherShot] {_w}x{_h} rain={_data.Effects.Raining.IsActive} " +
                 $"snow={_data.Effects.Snowing.IsActive} blur={_data.Effects.Blur.IsActive}");
    }

    public override void _Process(double delta)
    {
        if (_fx == null) return;

        // ClearWeather, partway through, so the fade-out can be seen:
        // emission stops and what is already falling finishes falling.
        if (_clearAt >= 0 && _frame == _clearAt)
        {
            _data.Effects.Raining.IsActive = false;
            _data.Effects.Snowing.IsActive = false;
            GD.Print("[WeatherShot] ClearWeather");
        }

        _data.Tick(0, (long)(delta * 1000));
        _fx.Sync(_data);

        // Exactly what GameView.RenderFrame does with the buffer, minus
        // the renderer: start from the clean frame, let the blur have it,
        // upload.
        Array.Copy(_clean, _rgba, _rgba.Length);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _fx.Blur(_rgba, _w, _h);
        sw.Stop();
        _blurMs += sw.Elapsed.TotalMilliseconds;
        _img.SetData(_w, _h, false, Image.Format.Rgba8, _rgba);
        _tex.Update(_img);

        if (++_frame >= _wait) Shoot();
    }

    async void Shoot()
    {
        SetProcess(false);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        Error e = img.SavePng(_out);
        GD.Print($"[WeatherShot] blur {_blurMs / Math.Max(1, _frame):0.00} ms/frame over {_frame} frames");
        GD.Print(e == Error.Ok ? $"[WeatherShot] wrote {_out}" : $"[WeatherShot] save failed: {e}");
        GetTree().Quit();
    }

    static string Arg(string name, string fallback)
    {
        string[] a = OS.GetCmdlineUserArgs();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != name) continue;
            if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) return a[i + 1];
            return "";
        }
        return fallback;
    }
}
