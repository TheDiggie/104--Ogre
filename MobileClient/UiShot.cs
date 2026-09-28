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

    /// <summary>A pack of things, built from whatever art is to hand.</summary>
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
