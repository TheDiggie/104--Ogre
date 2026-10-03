using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// Whether the piece STARTS hidden - an optional piece the player
        /// unhides in the editor, rather than one they can take away.
        /// The hotkey box is the first: a second copy of the hotbar on
        /// the glass is a thing to opt into, not a default. Set once, at
        /// the first Register, before the file is read, so a saved line
        /// still wins; and it is what "default" means for this piece -
        /// Reset and a layout switch go back to it, Moved compares
        /// against it, so an untouched optional piece writes no line
        /// (Save) and an unhidden one writes hidden=0, which ApplySaved
        /// reads as the player's choice.
        /// </summary>
        public bool DefaultHidden;

        /// <summary>
        /// How many across, for a piece that is a GRID of things and
        /// wraps. Zero means the piece's own default. Only a piece that
        /// sets <see cref="MaxColumns"/> above <see cref="MinColumns"/>
        /// gets the verb in the editor; every other piece ignores it.
        ///
        /// WHY THIS AND NOT A FREE RESIZE. The player's word was
        /// "resize", and the honest answer for a cluster is Scale -
        /// every size inside it grows together. But a grid has a second
        /// honest axis: the same slots at the same size can be laid out
        /// wide or tall, and that is a column count, not a rectangle.
        /// A free-dragged rectangle would have to be reconciled with
        /// the slot size on every edge and would lie about what it
        /// holds the moment the bag changed; a number of columns cannot.
        /// Saved as the sixth field on the piece's line so a file
        /// written before it existed still reads (ApplySaved).
        /// </summary>
        public int Columns;
        /// <summary>The range, and what zero means. Set by the piece at Register.</summary>
        public int MinColumns, MaxColumns, DefaultColumns;

        /// <summary>
        /// How many DOWN, for the same grid pieces: the height of the
        /// box the grid is seen through, in rows. Columns says how wide
        /// the grid is; this says how tall the WINDOW onto it is, and a
        /// pack with more rows than this scrolls inside it rather than
        /// growing the piece (InventoryDock).
        ///
        /// WHY A SECOND COUNT AND NOT "AS MANY AS IT NEEDS". The
        /// player's words: "add an adjustment for vertical as well. If a
        /// player has more than the slots can show just add a slider."
        /// His pack is ninety items; at seven across that is thirteen
        /// rows, and a piece whose height is its contents ran off the
        /// bottom of the glass through the chat. A piece's size has to
        /// be the player's decision, not the pack's. Saved as the
        /// OPTIONAL seventh field, after columns, so a five- or six-field
        /// line from before still reads (ApplySaved); zero means the
        /// piece's default, as with Columns.
        /// </summary>
        public int Rows;
        public int MinRows, MaxRows, DefaultRows;

        /// <summary>
        /// What the editor CALLS the two counts, and what one step of
        /// each is worth on the card. "Across" and "Down" are a grid's
        /// words and the dock keeps them; the chat is not a grid - its
        /// two honest axes are a width in points and a number of lines
        /// of text (ChatOverlay) - and a player reading "Across 25" over
        /// a chat box would not know what was being counted. So the
        /// piece names its own axes, and says what one count is worth:
        /// the card shows Columns x ColumnsUnit, so the chat's 25 steps
        /// of 40 read as "Width 1000", while the dock's unit of 1 reads
        /// "Across 8" as before. The SAVED number is always the count
        /// (the sixth and seventh fields are unchanged); the unit is
        /// display only.
        /// </summary>
        public string ColumnsLabel = "Across", RowsLabel = "Down";
        public int ColumnsUnit = 1, RowsUnit = 1;

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

        internal bool Moved => Offset != Vector2.Zero || Scale != 1f || Alpha != 1f || Hidden != DefaultHidden || Columns != 0 || Rows != 0;

        /// <summary>The columns in force: the player's, clamped, or the piece's default.</summary>
        public int ColumnsNow
            => Columns > 0 && MaxColumns > MinColumns
               ? Mathf.Clamp(Columns, MinColumns, MaxColumns) : DefaultColumns;

        /// <summary>The rows in force, by the same rule as ColumnsNow.</summary>
        public int RowsNow
            => Rows > 0 && MaxRows > MinRows
               ? Mathf.Clamp(Rows, MinRows, MaxRows) : DefaultRows;
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
    public static Piece Register(string id, string name, Control node = null, bool hidden = false)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!Pieces.TryGetValue(id, out Piece p))
        {
            // An optional piece starts hidden (Piece.DefaultHidden); the
            // file read just below overrides it when the player has said.
            p = new Piece { Id = id, DefaultHidden = hidden, Hidden = hidden };
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
    /// Everything the store says about a piece that a laid-out piece
    /// cares about, as one comparable value. The pieces compare it every
    /// frame against the one they laid out with, because a layout LOADED
    /// after _Ready, or an editor that moved a piece without raising
    /// Changed, says nothing - and the failure is silent.
    ///
    /// A struct, not a string: nine pieces built an interpolated string
    /// of six floats every frame each, which was about a kilobyte and a
    /// half of garbage per frame for a comparison that is almost always
    /// "same". <c>default</c> is what a missing piece stamps as, and is
    /// what every piece starts with, so a piece that never registered
    /// never relays - as the empty string did.
    /// </summary>
    public readonly struct Stamp : IEquatable<Stamp>
    {
        public readonly bool Present;
        public readonly Vector2 Offset;
        public readonly float Scale, Alpha;
        public readonly int Columns, Rows;
        /// <summary>Hidden, Editing and any caller-supplied bits, packed.</summary>
        public readonly int Flags;

        public Stamp(Piece p, int extra)
        {
            Present = true;
            Offset = p.Offset; Scale = p.Scale; Alpha = p.Alpha;
            Columns = p.Columns; Rows = p.Rows;
            Flags = (p.Hidden ? 1 : 0) | (Editing ? 2 : 0) | (extra << 2);
        }

        public bool Equals(Stamp o)
            => Present == o.Present && Offset == o.Offset && Scale == o.Scale && Alpha == o.Alpha
               && Columns == o.Columns && Rows == o.Rows && Flags == o.Flags;
        public override bool Equals(object obj) => obj is Stamp s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(Present, Offset, Scale, Alpha, Columns, Rows, Flags);
        public static bool operator ==(Stamp a, Stamp b) => a.Equals(b);
        public static bool operator !=(Stamp a, Stamp b) => !a.Equals(b);
    }

    /// <summary>The stamp of a piece; <c>default</c> for one that is not registered.</summary>
    public static Stamp StampOf(string id, int extra = 0)
    {
        Piece p = Get(id);
        return p == null ? default : new Stamp(p, extra);
    }

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
        // NO CLAMP. There was one - a quarter of the piece, then a
        // thumb's 44 points - and Ashton hit it twice: "Your ui element
        // border still stops me from putting stuff further left. Remove
        // this restriction system." So a piece goes where the finger
        // puts it, off the glass included; Reset in the editor is the
        // way home for one that is lost, and Reset all for the lot.
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

    // ---- the controls ----------------------------------------------

    /// <summary>
    /// How the thumbs drive the avatar: TWO choices, not one scheme.
    ///
    /// Moving is either the floating stick that appears under the left
    /// thumb (TouchControls) or a d-pad drawn on the glass
    /// (FixedControls); looking is either a drag across the glass or a
    /// look stick drawn on it. They used to come as a pair - "touch
    /// anywhere" or "fixed pad + stick" - and the player's words broke
    /// the pair: "some people don't like using the joystick to look
    /// around, they said it's sluggish. Let people use the joystick to
    /// move but touch anywhere to look", and the other way about for
    /// whoever wants it. So each is its own switch and the four
    /// combinations all work; both off is what the client has always
    /// done. PUBG Mobile and Call of Duty Mobile make the same two
    /// choices separately.
    /// </summary>
    public static bool MovePad { get; set; }

    /// <summary>Looking with a stick drawn on the glass instead of a drag over it. See MovePad.</summary>
    public static bool LookStick { get; set; }

    /// <summary>
    /// Both are PART OF THE LAYOUT, deliberately: chosen in the arrange
    /// screen, saved in the same file under the same slot, and they
    /// switch with the slot - a layout that puts a d-pad on the glass
    /// and a layout that does not are different HUDs, and keeping the
    /// choice anywhere else would let a slot switch bring back a pad
    /// the player had removed, or remove one they had placed. Snapshot
    /// and Restore carry them too, so Cancel backs out of the switch as
    /// it backs out of a drag.
    ///
    /// The keys. `move=pad` and `look=stick`, each written only when
    /// on, so a layout nobody touched writes nothing. The old single
    /// key `controls=fixed` is still read as both on - a player who had
    /// chosen the pad and the stick together keeps both - and is never
    /// written again. Never a piece id.
    /// </summary>
    const string MoveKey = "move", LookKey = "look", LegacyControlsKey = "controls";
    const string PadValue = "pad", StickValue = "stick", LegacyFixed = "fixed";

    /// <summary>Both switches from a slot's saved lines; off when the file says nothing.</summary>
    static void ApplyControls(Dictionary<string, string> d)
    {
        bool legacy = d != null && d.TryGetValue(LegacyControlsKey, out string c) && c == LegacyFixed;
        MovePad = legacy || (d != null && d.TryGetValue(MoveKey, out string m) && m == PadValue);
        LookStick = legacy || (d != null && d.TryGetValue(LookKey, out string l) && l == StickValue);
    }

    /// <summary>Both switches into a slot's lines, by the say-nothing-for-the-default rule.</summary>
    static void StoreControls(Dictionary<string, string> d)
    {
        if (MovePad) d[MoveKey] = PadValue; else d.Remove(MoveKey);
        if (LookStick) d[LookKey] = StickValue; else d.Remove(LookKey);
        // The old pair key has been read and split; writing it back
        // would re-pair the two choices on the next load.
        d.Remove(LegacyControlsKey);
    }

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
        ApplyScheme();
        Unbrick();
    }

    /// <summary>The slot's two control switches, or off when the file says nothing.</summary>
    static void ApplyScheme() => ApplyControls(Saved.TryGetValue(Slot, out var d) ? d : null);

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
        // The control lines share the section and are not pieces. A
        // piece registered under one of their keys would read "pad" as
        // a position; no piece is, and this keeps it that way.
        if (p.Id == MoveKey || p.Id == LookKey || p.Id == LegacyControlsKey) return;
        // x,y,scale,alpha,hidden[,columns[,rows]] - the sixth and the
        // seventh are optional, so a file from before grids had a column
        // count, or from before they had a row count, still reads.
        string[] bits = v.Split(',');
        if (bits.Length < 5) return;
        Read(p, bits, true);
    }

    /// <summary>
    /// The five-to-seven fields of a piece line into the piece. One
    /// reader for the file and the snapshot, because the two had drifted:
    /// the file clamped the scale and the snapshot did not.
    ///
    /// INVARIANT CULTURE on every number, read and written. float.Parse
    /// follows the phone's locale, and on a German or French device
    /// "1.5" does not parse and "1,5" is two fields - a layout written
    /// there would come back as a different layout, with no error.
    /// NaN and the infinities are refused too: "NaN" parses as a float,
    /// and an offset of NaN put a Control's size through Godot's
    /// set_size guard once a frame for the life of the client.
    /// </summary>
    static void Read(Piece p, string[] bits, bool clamp)
    {
        float x = Num(bits[0]), y = Num(bits[1]), s = Num(bits[2]), a = Num(bits[3]);
        p.Offset = new Vector2(x, y);
        p.Scale = s > 0f ? (clamp ? Mathf.Clamp(s, MinScale, MaxScale) : s) : 1f;
        p.Alpha = a > 0f ? (clamp ? Mathf.Clamp(a, MinAlpha, MaxAlpha) : a) : 1f;
        p.Hidden = bits[4] == "1";
        p.Columns = bits.Length > 5 && int.TryParse(bits[5], NumberStyles.Integer, Inv, out int c) && c > 0 ? c : 0;
        p.Rows = bits.Length > 6 && int.TryParse(bits[6], NumberStyles.Integer, Inv, out int r) && r > 0 ? r : 0;
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A finite float from the file, or zero.</summary>
    static float Num(string s)
        => float.TryParse(s, NumberStyles.Float, Inv, out float f) && float.IsFinite(f) ? f : 0f;

    /// <summary>A float into the file, culture-free, to the given places.</summary>
    static string Fmt(float f, string format) => f.ToString(format, Inv);

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
                    // The seventh field needs the sixth in front of it,
                    // so a row count with default columns writes a 0
                    // there - which ApplySaved reads as "default".
                    d[p.Id] = $"{Fmt(p.Offset.X, "0.##")},{Fmt(p.Offset.Y, "0.##")},{Fmt(p.Scale, "0.###")},{Fmt(p.Alpha, "0.###")},{(p.Hidden ? 1 : 0)}"
                            + (p.Rows > 0 ? $",{p.Columns},{p.Rows}" : p.Columns > 0 ? $",{p.Columns}" : "");
                else
                    d.Remove(p.Id);   // back at the default: say nothing rather than saying "default"
            }
            // The two control switches, by the same rule: only a choice
            // that is not the default is written.
            StoreControls(d);

            var sb = new StringBuilder();
            sb.Append("# Where this player wants the HUD. One section per layout.\n");
            sb.Append("# piece=offsetX,offsetY,scale,alpha,hidden[,columns[,rows]]\n");
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
            p.Offset = Vector2.Zero; p.Scale = 1f; p.Alpha = 1f; p.Hidden = p.DefaultHidden; p.Columns = 0; p.Rows = 0;
            ApplySaved(p);
        }
        ApplyScheme();
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
        p.Offset = Vector2.Zero; p.Scale = 1f; p.Alpha = 1f; p.Hidden = p.DefaultHidden; p.Columns = 0; p.Rows = 0;
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
        // The two control switches first, in the piece line's shape with
        // a word where the numbers go; Restore tells them apart by key.
        sb.Append(MoveKey).Append('=').Append(MovePad ? PadValue : "touch").Append(';');
        sb.Append(LookKey).Append('=').Append(LookStick ? StickValue : "touch").Append(';');
        foreach (Piece p in Order)
            sb.Append(p.Id).Append('=')
              .Append(Fmt(p.Offset.X, "R")).Append(',').Append(Fmt(p.Offset.Y, "R")).Append(',')
              .Append(Fmt(p.Scale, "R")).Append(',').Append(Fmt(p.Alpha, "R")).Append(',')
              .Append(p.Hidden ? 1 : 0).Append(',').Append(p.Columns).Append(',').Append(p.Rows).Append(';');
        return sb.ToString();
    }

    public static void Restore(string snapshot)
    {
        if (string.IsNullOrEmpty(snapshot)) return;
        foreach (string row in snapshot.Split(';'))
        {
            int eq = row.IndexOf('=');
            if (eq <= 0) continue;
            string key = row.Substring(0, eq);
            if (key == MoveKey) { MovePad = row.Substring(eq + 1) == PadValue; continue; }
            if (key == LookKey) { LookStick = row.Substring(eq + 1) == StickValue; continue; }
            Piece p = Get(row.Substring(0, eq));
            if (p == null) continue;
            string[] b = row.Substring(eq + 1).Split(',');
            if (b.Length < 5) continue;
            Read(p, b, false);
        }
        Touch();
    }
}
