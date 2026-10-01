using System;
using Godot;
using Meridian59.Data.Models;

/// <summary>
/// Photographs the two guild flows, and prints what pressing their
/// buttons would put on the wire. Not part of the game: it is attached to
/// <c>GuildFlowsShot.tscn</c>, which nothing else loads.
///
/// The panels are driven through the same models the live client drives
/// them with - a real <see cref="GuildHallsInfo"/> with real
/// <see cref="GuildHall"/> rows, a real <see cref="GuildAskData"/> - and
/// the visibility flag is set the way DataController sets it
/// (`Meridian59/Data/DataController.cs:2803-2804`, `:2821-2823`), so what
/// is being looked at is the client's own code path.
///
/// The wire half matters more than the picture. A screen saying the right
/// thing is not evidence that the right thing was sent, so the button
/// handlers here build the real UserCommand objects - the same ones
/// `BaseClient.SendUserCommandGuildRent` and
/// `SendUserCommandGuildCreate` build (`Meridian59/Client/BaseClient.cs:1241-1249`,
/// `:1342-1360`) - and print their serialized bytes.
///
///     godot --path MobileClient GuildFlowsShot.tscn -- --out guild.png
///
/// on a display; Xvfb is enough. Switches: <c>--create</c> for the
/// founding panel instead of the hall list, <c>--press &lt;NodeName&gt;</c>
/// to press named controls first, comma-separated (hall rows are <c>hall&lt;id&gt;</c>,
/// the buttons are <c>hallbuy</c>, <c>hallcancel</c>, <c>guildcreate</c>,
/// <c>guildclose</c>, <c>secret</c>), and <c>--password &lt;text&gt;</c>
/// to fill the guild password box before pressing, and <c>--name
/// &lt;text&gt;</c> the guild name box.
/// </summary>
public partial class GuildFlowsShot : Node
{
    public override void _Ready()
    {
        string outPath = Arg("--out", "guild.png");

        var layer = new CanvasLayer();
        AddChild(layer);

        if (Arg("--create", null) != null) Create(layer, outPath);
        else Halls(layer, outPath);
    }

    /// <summary>
    /// The hall list. Three halls with different costs and rents, which is
    /// what the reference's three columns are for (`UIGuildHallBuy.cpp:20-22`).
    /// </summary>
    void Halls(CanvasLayer layer, string outPath)
    {
        var info = new GuildHallsInfo();
        var panel = new GuildHallBuyPanel();
        layer.AddChild(panel);

        panel.Buy += (id, password) =>
        {
            // Exactly what BaseClient would build
            // (`BaseClient.cs:1241-1249`).
            var command = new UserCommandGuildRent(id, password);
            GD.Print($"[GuildFlows] rent hall {id} password \"{password}\" " +
                     $"-> type {(byte)command.CommandType}, {command.ByteLength} bytes: " +
                     Hex(command.Bytes));
            info.Clear(true);
            info.IsVisible = false;
        };
        // What GameView's handler does, so the close is observable here
        // rather than only on a real client (`UIGuildHallBuy.cpp:260-262`).
        panel.Cancelled += () =>
        {
            GD.Print("[GuildFlows] cancelled: clear + IsVisible false");
            info.Clear(true);
            info.IsVisible = false;
        };

        info.GuildHalls.Add(Hall("Hall of the Broken Wheel", 11, 15000, 250));
        info.GuildHalls.Add(Hall("Marion Guild House", 12, 40000, 900));
        info.GuildHalls.Add(Hall("Tos Guild Tower", 13, 125000, 2400));
        // The order DataController uses: fill, then raise the flag
        // (`DataController.cs:2821-2823`).
        panel.Sync(info);
        info.IsVisible = true;
        panel.Sync(info);

        // --invalidate reproduces a server save: DataController.Invalidate
        // empties GuildHallsInfo and leaves IsVisible set
        // (`DataController.cs:1049`), which is the state the reference
        // closes on (`UIGuildHallBuy.cpp:139-144`).
        if (Arg("--invalidate", null) != null)
        {
            info.Clear(true);
            GD.Print($"[GuildFlows] invalidated: {info.GuildHalls.Count} halls, IsVisible={info.IsVisible}");
        }

        string password = Arg("--password", null);
        if (password != null)
        {
            var box = panel.FindChild("hallpassword", true, false) as LineEdit;
            if (box == null) GD.Print("[GuildFlows] no password box");
            else box.Text = password;
        }

        Finish(panel, info, outPath);
    }

    /// <summary>
    /// The founding panel, with the two prices the server quotes
    /// (`GuildAskData.cs:112-141`).
    /// </summary>
    void Create(CanvasLayer layer, string outPath)
    {
        var ask = new GuildAskData(25000, 75000);
        var panel = new GuildCreatePanel();
        layer.AddChild(panel);

        panel.Found += f =>
        {
            // Argument order is BaseClient's: all five male ranks, then
            // all five female (`BaseClient.cs:1342-1353`).
            var command = new UserCommandGuildCreate(
                f.Name,
                f.Male[0], f.Male[1], f.Male[2], f.Male[3], f.Male[4],
                f.Female[0], f.Female[1], f.Female[2], f.Female[3], f.Female[4],
                f.Secret);
            GD.Print($"[GuildFlows] found \"{f.Name}\" secret={f.Secret} quoted={f.Cost} " +
                     $"-> type {(byte)command.CommandType}, {command.ByteLength} bytes: " +
                     Hex(command.Bytes));
            ask.IsVisible = false;
        };
        panel.Closed += () =>
        {
            GD.Print("[GuildFlows] closed: IsVisible false");
            ask.IsVisible = false;
        };

        panel.Sync(ask);
        ask.IsVisible = true;
        panel.Sync(ask);

        // --name fills the guild name box, which is the one thing with
        // no default and the one thing a founding cannot do without.
        string name = Arg("--name", null);
        if (name != null)
        {
            var box = panel.FindChild("guildname", true, false) as LineEdit;
            if (box == null) GD.Print("[GuildFlows] no guild name box");
            else box.Text = name;
        }

        Finish(panel, ask, outPath);
    }

    /// <summary>
    /// Presses one named control if asked, re-syncs, and shoots. The
    /// re-sync is what the live client does every frame, so a press that
    /// should have closed the panel is seen to close it.
    /// </summary>
    void Finish(Control panel, object model, string outPath)
    {
        // Comma-separated, because most of these flows take two presses:
        // pick a row, then Buy.
        string press = Arg("--press", null);
        if (press != null)
            foreach (string step in press.Split(','))
            {
                Node found = panel.FindChild(step, true, false);
                if (found is CheckBox c) { c.ButtonPressed = !c.ButtonPressed; GD.Print($"[GuildFlows] toggled {step}"); }
                else if (found is Button b) { b.EmitSignal(BaseButton.SignalName.Pressed); GD.Print($"[GuildFlows] pressed {step}"); }
                else GD.Print($"[GuildFlows] no control named {step}");
            }

        if (model is GuildHallsInfo halls) ((GuildHallBuyPanel)panel).Sync(halls);
        else if (model is GuildAskData ask) ((GuildCreatePanel)panel).Sync(ask);

        GD.Print($"[GuildFlows] open={(panel is GuildHallBuyPanel h ? h.IsOpen : ((GuildCreatePanel)panel).IsOpen)}");
        Shoot(outPath);
    }

    static GuildHall Hall(string name, uint id, uint cost, uint rent)
    {
        // The name is normally a string-resource id resolved on the way in
        // (`GuildHall.cs:232-248`); there is no string table here, so it
        // is set directly.
        var h = new GuildHall(name, id) { Cost = cost, Rent = rent };
        return h;
    }

    static string Hex(byte[] b)
    {
        var sb = new System.Text.StringBuilder();
        foreach (byte x in b) sb.Append(x.ToString("x2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    async void Shoot(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image img = GetViewport().GetTexture().GetImage();
        Error e = img.SavePng(path);
        GD.Print(e == Error.Ok ? $"[GuildFlows] wrote {path}" : $"[GuildFlows] save failed: {e}");
        GetTree().Quit();
    }

    static string Arg(string name, string fallback)
    {
        string[] a = OS.GetCmdlineUserArgs();
        for (int i = 0; i < a.Length; i++)
            if (a[i] == name) return i + 1 < a.Length ? a[i + 1] : "";
        return fallback;
    }
}
