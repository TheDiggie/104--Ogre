using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Meridian59.Data.Models;
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
