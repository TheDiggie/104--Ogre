using System;
using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// Where the player has decided their HUD goes.
///
/// WHY. A phone is held by two hands of a particular size, with a case
/// on it, in landscape, by somebody who may be left-handed. No single
/// layout is right for all of that, and every mobile game that is
/// actually played on a phone lets the player move its controls - PUBG
/// Mobile, Call of Duty Mobile and Mobile Legends all ship an editor
/// with the same four verbs, and the owner asked for one here after
/// playing on his own phone.
///
/// WHAT THE OTHERS DO, which is what this copies. Enter an edit mode
/// from settings; drag a control to move it; a slider for its size; a
/// slider for its transparency; hide the ones you do not use. More than
/// one saved layout, because the right HUD for a fight is not the right
/// HUD for walking around. And a reset, because the fastest way to
/// ruin a HUD is to edit it.
///
/// THE UNIT IS A GROUP, NOT A BUTTON, and that is the one place this
/// deliberately does less than a shooter. PUBG lets you move each of
/// forty buttons because its buttons are independent; this client's
/// are not - the combat cluster is an arc computed around the attack
/// control, the vitals are four bars that read as a column, the dial
/// has three satellites that belong to it. Letting those drift apart
/// would let a player break the arithmetic that makes them legible.
/// So a PIECE is a cluster, it moves and scales as one, and the layout
/// inside it stays the designer's. That also matches the advice every
/// one of those guides gives about the commonest mistake: overlapping
/// controls and an overcrowded screen.
///
/// HOW A PIECE ADOPTS THIS. It computes the rect it would like, exactly
/// as it does now, and hands it to <see cref="Place"/>; what comes back
/// is where it must actually draw. Everything inside it then lays out
/// relative to that rect and multiplied by <see cref="Piece.Scale"/>.
/// A piece that does not call Place is simply not movable, which is the
/// correct behaviour for anything that is not part of the HUD.
///
/// NOTHING HERE TOUCHES WHAT A CONTROL DOES. Moving a button does not
/// change what it sends; this is geometry and modulation only.
/// </summary>
public static class M59Hud
{
    /// <summary>One movable cluster.</summary>
    public sealed class Piece
    {
        /// <summary>Stable key in the saved file. Never shown to a player.</summary>
        public string Id;
        /// <summary>What the editor calls it.</summary>
        public string Name;
        /// <summary>
        /// What the editor fades and hides. A piece whose parts are
        /// several siblings passes the one that owns them all; a piece
        /// that has no single node passes null and handles Alpha and
        /// Hidden itself.
        /// </summary>
        public Control Node;

        /// <summary>The player's drag, in viewport units, from where the designer put it.</summary>
        public Vector2 Offset;
        /// <summary>0.7 to 1.6. Multiplies every size inside the piece.</summary>
        public float Scale = 1f;
        /// <summary>0.25 to 1. What the others call transparency.</summary>
        public float Alpha = 1f;
        /// <summary>Hidden by the player. Not the same as hidden by the client.</summary>
        public bool Hidden;

        /// <summary>
        /// Where the designer last wanted it, before the player's offset.
        /// The editor draws its handle here plus Offset, and Reset puts
        /// it back here.
        /// </summary>
        public Rect2 Natural;

        /// <summary>Whether the client is showing this at all right now.</summary>
        public bool Live = true;

        /// <summary>Where it actually ends up: Natural moved by Offset.</summary>
        public Rect2 Rect => new Rect2(Natural.Position + Offset, Natural.Size);

        internal bool Moved => Offset != Vector2.Zero || Scale != 1f || Alpha != 1f || Hidden;
    }

    static readonly Dictionary<string, Piece> Pieces = new Dictionary<string, Piece>();
    static readonly List<Piece> Order = new List<Piece>();

    /// <summary>Every registered piece, in the order they registered.</summary>
    public static IReadOnlyList<Piece> All => Order;

    /// <summary>
    /// True while the player is rearranging. The HUD keeps drawing and
    /// keeps its own state; what changes is that the editor is on top
    /// taking the touches, and pieces the client would normally hide
    /// are shown anyway so they can be moved.
    /// </summary>
    public static bool Editing { get; set; }

    /// <summary>Raised when a piece moves, so a laid-out piece can re-lay itself.</summary>
    public static event Action Changed;

    public const float MinScale = 0.7f, MaxScale = 1.6f;
    public const float MinAlpha = 0.25f, MaxAlpha = 1f;

    /// <summary>
    /// Declares a cluster. Safe to call more than once - a piece keeps
    /// the position the player gave it across a re-register, which
    /// happens whenever a panel rebuilds.
    /// </summary>
    public static Piece Register(string id, string name, Control node = null)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!Pieces.TryGetValue(id, out Piece p))
        {
            p = new Piece { Id = id };
            Pieces[id] = p;
            Order.Add(p);
            // A layout saved before this piece existed has nothing to
            // say about it, so a late register still picks up whatever
            // the file holds for it.
            ApplySaved(p);
            // A late register picks up the file's value, including a
            // hidden flag the editor would never have written. See Unbrick.
            if (id == "openers") p.Hidden = false;
        }
        p.Name = name ?? id;
        if (node != null) p.Node = node;
        return p;
    }

    public static Piece Get(string id)
        => id != null && Pieces.TryGetValue(id, out Piece p) ? p : null;

    /// <summary>
    /// Where a piece must actually draw, given where it would like to.
    ///
    /// Clamped so a piece can never be dragged off the glass: a control
    /// a player cannot see is a control they cannot drag back, and a
    /// phone has no window to resize. A quarter of the piece is kept on
    /// screen on each axis, which is enough to grab.
    /// </summary>
    public static Rect2 Place(string id, Rect2 natural, Vector2 v)
    {
        Piece p = Get(id);
        if (p == null) return natural;
        p.Natural = natural;

        Vector2 pos = natural.Position + p.Offset;
        Vector2 size = natural.Size;
        // A thumb's worth, not a pixel's. 24 left a 24x32 sliver of the
        // portrait on screen - grabbable with a mouse in the editor,
        // not with a finger on a phone, which is the only place this
        // runs. A piece smaller than a thumb is kept whole instead of
        // being allowed to hang further off than it is big.
        float keepX = Mathf.Min(size.X, Mathf.Max(44f, size.X * 0.25f));
        float keepY = Mathf.Min(size.Y, Mathf.Max(44f, size.Y * 0.25f));
        pos.X = Mathf.Clamp(pos.X, -(size.X - keepX), v.X - keepX);
        pos.Y = Mathf.Clamp(pos.Y, -(size.Y - keepY), v.Y - keepY);

        // Write the clamp back, so a drag that hit the edge does not
        // leave the saved offset pointing somewhere the piece will never
        // be drawn - otherwise the next launch snaps it somewhere else.
        p.Offset = pos - natural.Position;
        return new Rect2(pos, size);
    }

    /// <summary>
    /// The alpha and visibility part, for a piece that passed a node.
    /// Call it from the piece's own Layout, after Place.
    /// </summary>
    public static void Dress(string id)
    {
        Piece p = Get(id);
        if (p?.Node == null || !GodotObject.IsInstanceValid(p.Node)) return;
        var c = p.Node.Modulate;
        p.Node.Modulate = new Color(c.R, c.G, c.B, Editing ? 1f : p.Alpha);
        // While editing, a piece the player has hidden is shown faintly
        // so it can be found and brought back. Hiding it for real would
        // make the decision irreversible from inside the editor.
        if (p.Hidden && !Editing) p.Node.Visible = false;
    }

    /// <summary>Whether the client should draw this piece at all.</summary>
    public static bool Shows(string id)
    {
        Piece p = Get(id);
        if (p == null) return true;
        return Editing || !p.Hidden;
    }

    public static void Touch() => Changed?.Invoke();

    // ---- layouts ---------------------------------------------------

    /// <summary>
    /// How many saved layouts a player gets. Two is the number every
    /// guide recommends keeping - one for fighting, one for everything
    /// else - and a third costs nothing.
    /// </summary>
    public const int Slots = 3;

    /// <summary>Which slot is in use. Persisted with the layouts.</summary>
    public static int Slot { get; private set; }

    const string Path = "user://hud.cfg";

    /// <summary>
    /// The file. One section per slot, one line per piece. Kept as text
    /// rather than a Godot resource for the same reason every other
    /// store in this client is: it can be read and fixed by hand when
    /// something goes wrong on a device nobody can attach a debugger to.
    /// </summary>
    static readonly Dictionary<int, Dictionary<string, string>> Saved =
        new Dictionary<int, Dictionary<string, string>>();

    static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!FileAccess.FileExists(Path)) return;
            using FileAccess f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            if (f == null) return;
            int slot = 0;
            while (!f.EofReached())
            {
                string line = f.GetLine()?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    int.TryParse(line.Substring(1, line.Length - 2), out slot);
                    continue;
                }
                if (line.StartsWith("slot="))
                {
                    if (int.TryParse(line.Substring(5), out int s))
                        Slot = Mathf.Clamp(s, 0, Slots - 1);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (!Saved.TryGetValue(slot, out var d))
                    Saved[slot] = d = new Dictionary<string, string>();
                d[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
        }
        catch { /* a layout is a convenience; never let it stop the client */ }

        foreach (Piece p in Order) ApplySaved(p);
        Unbrick();
    }

    /// <summary>
    /// The one piece that may not come back hidden, whatever the file
    /// says.
    ///
    /// The editor refuses to hide the menu band because Menu is the
    /// only door into the editor, but that guard is in the editor and
    /// this file is plain text the store's own comment invites a player
    /// to edit. A hidden band loaded from disk would be a client with
    /// no menu, no settings and no way to reach the screen that could
    /// undo it - so the rule is kept here, where the value actually
    /// arrives, as well as there, where it is chosen.
    /// </summary>
    static void Unbrick()
    {
        Piece door = Get("openers");
        if (door != null) door.Hidden = false;
    }

    static void ApplySaved(Piece p)
    {
        if (p == null || !Saved.TryGetValue(Slot, out var d)) return;
        if (!d.TryGetValue(p.Id, out string v)) return;
        // x,y,scale,alpha,hidden
        string[] bits = v.Split(',');
        if (bits.Length < 5) return;
        float.TryParse(bits[0], out float x);
        float.TryParse(bits[1], out float y);
        float.TryParse(bits[2], out float s);
        float.TryParse(bits[3], out float a);
        p.Offset = new Vector2(x, y);
        p.Scale = s > 0f ? Mathf.Clamp(s, MinScale, MaxScale) : 1f;
        p.Alpha = a > 0f ? Mathf.Clamp(a, MinAlpha, MaxAlpha) : 1f;
        p.Hidden = bits[4] == "1";
    }

    public static void Save()
    {
        try
        {
            // The slot in memory is the truth for the current slot; the
            // others are whatever was read, so switching slots and back
            // does not wipe them.
            if (!Saved.TryGetValue(Slot, out var d))
                Saved[Slot] = d = new Dictionary<string, string>();
            foreach (Piece p in Order)
            {
                if (p.Moved)
                    d[p.Id] = $"{p.Offset.X:0.##},{p.Offset.Y:0.##},{p.Scale:0.###},{p.Alpha:0.###},{(p.Hidden ? 1 : 0)}";
                else
                    d.Remove(p.Id);   // back at the default: say nothing rather than saying "default"
            }

            var sb = new StringBuilder();
            sb.Append("# Where this player wants the HUD. One section per layout.\n");
            sb.Append("# piece=offsetX,offsetY,scale,alpha,hidden\n");
            sb.Append("slot=").Append(Slot).Append('\n');
            foreach (var kv in Saved)
            {
                if (kv.Value.Count == 0) continue;
                sb.Append('[').Append(kv.Key).Append("]\n");
                foreach (var e in kv.Value) sb.Append(e.Key).Append('=').Append(e.Value).Append('\n');
            }

            using FileAccess f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
            f?.StoreString(sb.ToString());
        }
        catch { }
    }

    /// <summary>Switches layout, keeping the one being left.</summary>
    public static void Use(int slot)
    {
        slot = Mathf.Clamp(slot, 0, Slots - 1);
        if (slot == Slot) return;
        Save();
        Slot = slot;
        foreach (Piece p in Order)
        {
            p.Offset = Vector2.Zero; p.Scale = 1f; p.Alpha = 1f; p.Hidden = false;
            ApplySaved(p);
        }
        // And again, now that Slot has moved. The first Save wrote the
        // layout being LEFT along with `slot=` still pointing at it, so
        // without this the choice of layout lived only in memory: a
        // player who switched to Layout 2 and backed out of the editor
        // with Cancel - which does not Save - came back after a restart
        // on Layout 1, with no sign that the switch had been dropped.
        // The pieces have just been reloaded from this slot's own saved
        // values, so writing them back changes nothing but the slot
        // number. See HudEditor.Pick.
        Save();
        Touch();
    }

    /// <summary>Puts one piece back where the designer put it.</summary>
    public static void Reset(Piece p)
    {
        if (p == null) return;
        p.Offset = Vector2.Zero; p.Scale = 1f; p.Alpha = 1f; p.Hidden = false;
        Touch();
    }

    /// <summary>Puts the whole layout back.</summary>
    public static void ResetAll()
    {
        foreach (Piece p in Order) Reset(p);
        Touch();
    }

    /// <summary>
    /// A snapshot, for the editor's Cancel. Taken on entry and put back
    /// if the player backs out - which they must be able to do, because
    /// the whole point of an editor is trying things.
    /// </summary>
    public static string Snapshot()
    {
        var sb = new StringBuilder();
        foreach (Piece p in Order)
            sb.Append(p.Id).Append('=')
              .Append(p.Offset.X).Append(',').Append(p.Offset.Y).Append(',')
              .Append(p.Scale).Append(',').Append(p.Alpha).Append(',')
              .Append(p.Hidden ? 1 : 0).Append(';');
        return sb.ToString();
    }

    public static void Restore(string snapshot)
    {
        if (string.IsNullOrEmpty(snapshot)) return;
        foreach (string row in snapshot.Split(';'))
        {
            int eq = row.IndexOf('=');
            if (eq <= 0) continue;
            Piece p = Get(row.Substring(0, eq));
            if (p == null) continue;
            string[] b = row.Substring(eq + 1).Split(',');
            if (b.Length < 5) continue;
            float.TryParse(b[0], out float x);
            float.TryParse(b[1], out float y);
            float.TryParse(b[2], out float s);
            float.TryParse(b[3], out float a);
            p.Offset = new Vector2(x, y);
            p.Scale = s > 0f ? s : 1f;
            p.Alpha = a > 0f ? a : 1f;
            p.Hidden = b[4] == "1";
        }
        Touch();
    }
}
