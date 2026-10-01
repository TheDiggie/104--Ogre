using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Meridian59.Data.Models;
using Meridian59.Common.Enums;
using Meridian59.Files.BGF;

/// <summary>
/// Screenshots a widget with made-up data, so the UI can be looked at
/// without a server. Not part of the game: it is attached to
/// <c>UiShot.tscn</c>, which nothing else loads.
///
/// Run it with
///     godot --path MobileClient UiShot.tscn -- --out shot.png --res /tmp/res
/// on a display - Xvfb is enough. Headless renders nothing, so the
/// picture comes out blank.
/// </summary>
public partial class UiShot : Node
{
    public override void _Ready()
    {
        string outPath = Arg("--out", "ui.png");
        string res = Arg("--res", "/tmp/res");

        var layer = new CanvasLayer();
        AddChild(layer);

        // --portrait shows the avatar-panel portrait instead of the bag:
        // an object composed from its HEAD hotspot beside the same object
        // composed whole, which is what makes a face out of a body.
        if (Arg("--portrait", null) != null) { Portrait(layer, res, outPath); return; }

        // --overlays shows the first-person player overlays at every
        // hotspot at once, so the placement arithmetic can be looked at
        // rather than reasoned about.
        if (Arg("--overlays", null) != null) { Overlays(res, outPath); return; }

        var bag = new InventoryPanel { FontSize = 18 };
        layer.AddChild(bag);

        // Open first: the panel ignores a sync while it is closed, which
        // is right for the live client - it syncs every frame - and a trap
        // for a harness that fills it once.
        bag.Open();
        List<InventoryObject> items = Fake(res);
        bag.Sync(items);

        // With --pick, select an item, which brings up the action row.
        string pick = Arg("--pick", null);
        if (pick != null && int.TryParse(pick, out int n) && n >= 0 && n < items.Count)
            bag.Choose(items[n]);

        // The bag only rearranges its own view; moving the item in the
        // list is the client's job, so the harness does here what
        // GameView.MoveInBag does against the real one.
        bag.MoveItem += (from, to) =>
        {
            int at = items.IndexOf(from), onto = items.IndexOf(to);
            if (at < 0 || onto < 0) return;
            items.RemoveAt(at);
            items.Insert(onto, from);
            bag.Sync(items);
            GD.Print($"[UiShot] moved {from.Name} onto {to.Name}");
            GD.Print("[UiShot] order now: " + string.Join(", ", items.ConvertAll(i => i.Name)));
        };

        // --drag i,j drags the i-th slot onto the j-th, as a finger does.
        string drag = Arg("--drag", null);
        if (drag != null)
        {
            GD.Print("[UiShot] order was: " + string.Join(", ", items.ConvertAll(i => i.Name)));
            DragSlots(drag, outPath);
            return;
        }

        Shoot(outPath);
    }

    /// <summary>
    /// The portrait path. The game's avatar panel composes your object
    /// from its HEAD hotspot downwards, which turns a body into a face.
    /// The art to hand has no player bodies - nothing here carries a head
    /// hotspot with a part on it - so this builds the case by hand: a main
    /// frame that does have hotspot 1, with a part pinned to it.
    /// </summary>
    void Portrait(CanvasLayer layer, string res, string outPath)
    {
        var o = new RoomObject();
        try
        {
            o.Resource = new BgfFile(Path.Combine(res, "flagpole.bgf"));
            var head = new SubOverlay(0, new AnimationNone(), (byte)KnownHotspot.HEAD, 0, 0);
            head.Resource = new BgfFile(Path.Combine(res, "ankh.bgf"));
            o.SubOverlays.Add(head);
            o.Tick(0, 1);
        }
        catch (Exception e) { GD.Print($"[UiShot] portrait art: {e.Message}"); }

        Tex tWhole = M59Compose.Icon(o, 160);
        Tex tHead = M59Compose.Icon(o, 160, (byte)KnownHotspot.HEAD);
        GD.Print($"[UiShot] whole {(tWhole == null ? "null" : tWhole.W + "x" + tWhole.H)}, " +
                 $"from head {(tHead == null ? "null" : tHead.W + "x" + tHead.H)}, " +
                 $"subs {o.SubOverlays.Count}, frame {(o.FrontFrame == null ? "null" : "ok")}");

        var whole = new TextureRect
        {
            Texture = M59Assets.FromTex(tWhole),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = new Vector2(20, 60), Size = new Vector2(160, 160),
        };
        var fromHead = new TextureRect
        {
            Texture = M59Assets.FromTex(tHead),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = new Vector2(220, 60), Size = new Vector2(160, 160),
        };
        layer.AddChild(whole);
        layer.AddChild(fromHead);

        foreach (var (text, x) in new[] { ("whole object", 20), ("from the HEAD hotspot", 220) })
        {
            var l = new Label { Text = text, Position = new Vector2(x, 28) };
            l.AddThemeColorOverride("font_color", new Color(1, 1, 1));
            layer.AddChild(l);
        }

        Shoot(outPath);
    }

    /// <summary>
    /// Every player-overlay hotspot at once, with whatever art is to
    /// hand standing in for a hand or a sword. The point is the geometry:
    /// nine pictures, one per compass point plus the centre, each flush
    /// against the edges its name says and scaled by the reference's
    /// width/800 factor.
    ///
    /// It drives the real widget through the real data layer - a
    /// DataController with overlays added to Data.PlayerOverlays - so
    /// what is being looked at is the client's own code path and not a
    /// drawing of it.
    /// </summary>
    void Overlays(string res, string outPath)
    {
        // Under the widget, so the pictures can be seen against
        // something rather than floating on grey.
        var layer = new CanvasLayer { Layer = 0 };
        AddChild(layer);
        var bg = new ColorRect { Color = new Color(0.10f, 0.12f, 0.16f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);

        var view = new PlayerOverlays { Verbose = true };
        layer.AddChild(view);

        var data = new Meridian59.Data.DataController();
        string[] files = System.IO.Directory.Exists(res)
            ? System.IO.Directory.GetFiles(res, "*.bgf")
            : new string[0];
        Array.Sort(files);

        uint id = 1;
        foreach (PlayerOverlayHotspot at in new[]
        {
            PlayerOverlayHotspot.HOTSPOT_NW, PlayerOverlayHotspot.HOTSPOT_N,
            PlayerOverlayHotspot.HOTSPOT_NE, PlayerOverlayHotspot.HOTSPOT_E,
            PlayerOverlayHotspot.HOTSPOT_SE, PlayerOverlayHotspot.HOTSPOT_S,
            PlayerOverlayHotspot.HOTSPOT_SW, PlayerOverlayHotspot.HOTSPOT_W,
            PlayerOverlayHotspot.HOTSPOT_CENTER,
        })
        {
            if (files.Length == 0) break;
            var ov = new PlayerOverlay();
            ov.ID = id;
            ov.RenderPosition = at;
            try { ov.Resource = new BgfFile(files[(int)(id - 1) % files.Length]); }
            catch (Exception e) { GD.Print($"[UiShot] overlay art: {e.Message}"); continue; }
            ov.Tick(0, 1);
            data.PlayerOverlays.Add(ov);
            id++;
        }
        GD.Print($"[UiShot] {data.PlayerOverlays.Count} overlays from {files.Length} bgf in {res}");

        view.Sync(data);
        Shoot(outPath);
    }

    /// <summary>A pack of things, built from whatever art is to hand.</summary>
    /// <summary>
    /// Drags one slot onto another with real mouse events, because that
    /// is the only way to run Godot's own drag and drop: a press, enough
    /// motion while held for it to decide a drag has begun, a release
    /// over the target.
    /// </summary>
    async void DragSlots(string spec, string outPath)
    {
        for (int i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        string[] p = spec.Split(',');
        var slots = new List<InventorySlot>();
        Collect(GetTree().Root, slots);

        if (p.Length != 2 || !int.TryParse(p[0], out int fromIdx) || !int.TryParse(p[1], out int toIdx) ||
            fromIdx < 0 || toIdx < 0 || fromIdx >= slots.Count || toIdx >= slots.Count)
        {
            GD.Print($"[UiShot] --drag wants i,j within 0..{slots.Count - 1}, got {spec}");
        }
        else
        {
            Vector2 a = slots[fromIdx].GetGlobalRect().GetCenter();
            Vector2 b = slots[toIdx].GetGlobalRect().GetCenter();

            Input.WarpMouse(a);
            Input.ParseInputEvent(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left,
              Position = a, GlobalPosition = a, Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            Vector2 last = a;
            for (int i = 1; i <= 12; i++)
            {
                Vector2 at = a.Lerp(b, i / 12f);
                Input.ParseInputEvent(new InputEventMouseMotion
                { Position = at, GlobalPosition = at, Relative = at - last, ButtonMask = MouseButtonMask.Left });
                last = at;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            Input.ParseInputEvent(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, Position = b, GlobalPosition = b, Pressed = false });
            GD.Print($"[UiShot] dragged slot {fromIdx} onto {toIdx}");
        }

        for (int i = 0; i < 30; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(outPath);
        GD.Print($"[UiShot] wrote {outPath}");
        GetTree().Quit();
    }

    static void Collect(Node from, List<InventorySlot> into)
    {
        if (from is InventorySlot s) into.Add(s);
        foreach (Node c in from.GetChildren()) Collect(c, into);
    }

    static List<InventoryObject> Fake(string res)
    {
        (string file, string name, uint count, bool used)[] wanted =
        {
            ("icraftlongsword.bgf", "long sword",       1, true),
            ("neruaxe.bgf",         "nerudite axe",     1, false),
            ("cookie.bgf",          "cookie",           4, false),
            ("corncob.bgf",         "ear of corn",      2, false),
            ("doubloon.bgf",        "gold doubloon",   17, false),
            ("dyebottle.bgf",       "bottle of dye",    1, false),
            ("book1.bgf",           "tattered book",    1, false),
            ("ankh.bgf",            "ankh",             1, true),
            ("carrot.bgf",          "carrot",           3, false),
        };

        var items = new List<InventoryObject>();
        uint id = 1;
        foreach (var w in wanted)
        {
            string path = Path.Combine(res, w.file);
            if (!System.IO.File.Exists(path)) { GD.Print($"[UiShot] no {w.file}"); continue; }

            var o = new InventoryObject();
            o.ID = id++;
            o.Name = w.name;
            o.NumOfSameName = w.count;
            o.Count = w.count;          // what the slot prints, as the game does
            o.IsInUse = w.used;
            try { o.Resource = new BgfFile(path); }
            catch (Exception e) { GD.Print($"[UiShot] {w.file}: {e.Message}"); continue; }
            o.Tick(0, 1);              // settles the frame the icon comes from
            items.Add(o);
        }
        return items;
    }

    /// <summary>
    /// Waits for the frame to be drawn before reading it back - the
    /// viewport's texture is empty until the renderer has been round
    /// once, and a shot taken in _Ready alone is a black rectangle.
    /// </summary>
    async void Shoot(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image img = GetViewport().GetTexture().GetImage();
        Error e = img.SavePng(path);
        GD.Print(e == Error.Ok ? $"[UiShot] wrote {path}" : $"[UiShot] save failed: {e}");
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
