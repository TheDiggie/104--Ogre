using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Meridian59.Common;
using Meridian59.Common.Enums;
using Meridian59.Data.Lists;
using Meridian59.Data.Models;
using Meridian59.Files.ROO;
using Meridian59.Files.RSB;
using Meridian59.Protocol;
using Meridian59.Protocol.Enums;
using Meridian59.Protocol.GameMessages;
using Meridian59.Protocol.Structs;

/// <summary>
/// A Meridian 59 server that is not one: just enough of the protocol to
/// walk a real client from its first byte to standing in a room with
/// things in it.
///
/// Why this exists: the live half of this client - login, character
/// select, room entry, the object list, chat - could not be exercised at
/// all. The real server is not reachable from here, so that whole path
/// was written from reading the library and never once run. This makes it
/// runnable, on loopback, with no account and nothing at stake.
///
/// It is deliberately not a server. It answers the handshake, puts you in
/// one room, and says things. Anything it does not understand it ignores.
///
///     dotnet run -c Release -- [port] [resourceDir] [room.roo]
///
/// The client verifies no CRC on what it receives - it only signs what it
/// sends - so nothing here needs the packet crypto. It does need to send
/// GetChoice and GameState, because that pair is what moves the client's
/// own parser from login mode into game mode.
/// </summary>
static class FakeServer
{
    // Resource ids this server hands out. They have to resolve through the
    // client's string dictionary, so EnsureStrings writes one.
    const uint RID_ROOMFILE = 60001;
    const uint RID_ROOMNAME = 60002;
    // The room on the other side of a Go. Two rooms is the smallest
    // number that proves a room CHANGE rather than a room.
    const uint RID_ROOMFILE2 = 60005;
    const uint RID_ROOMNAME2 = 60006;
    const uint RID_ARROWBGF = 60007;
    // M59_BADROOM: a room file the client does not have, and its name.
    const uint RID_ROOMFILE3 = 60008;
    const uint RID_ROOMNAME3 = 60009;
    // M59_LOOKSKILL: the words on a skill's description.
    const uint RID_SKILLDESC = 60115;
    const uint RID_SKILLSCHOOL = 60116;
    const uint RID_SKILLLEVEL = 60117;
    const uint RID_PLAYERNAME = 60003;
    const uint RID_RATNAME = 60004;
    const uint RID_PLAYERBGF = 60010;
    const uint RID_RATBGF = 60011;
    const uint RID_GREETING = 60020;
    const uint RID_ECHO = 60021;
    const uint RID_COIN = 60030;
    const uint RID_COINBGF = 60031;
    const uint RID_BOOK = 60032;
    const uint RID_BOOKBGF = 60033;
    const uint RID_AXE = 60034;
    const uint RID_AXEBGF = 60035;
    const uint RID_ALICE = 60040;
    const uint RID_BORIS = 60041;
    const uint RID_RATLOOK = 60050;
    const uint RID_SPELL1 = 60060;
    const uint RID_SPELL2 = 60061;
    const uint RID_SKILL1 = 60062;
    const uint RID_SKILL2 = 60063;
    const uint RID_COND1 = 60160;
    const uint RID_COND2 = 60161;
    const uint RID_COND3 = 60162;
    const uint RID_COND4 = 60163;
    const uint RID_GLOBE = 60150;
    const uint RID_HEADLINE = 60151;
    const uint RID_HEADBGF = 60140;
    const uint RID_HAIRBGF = 60141;
    const uint RID_SPELLA = 60142;
    const uint RID_SPELLB = 60143;
    const uint RID_SPELLC = 60144;
    const uint RID_SKILLA = 60145;
    const uint RID_SKILLB = 60146;
    const uint RID_MAIL1 = 60130;
    const uint RID_MAIL2 = 60131;
    const uint RID_ROOMBUFF1 = 60072;
    const uint RID_ROOMBUFF2 = 60073;
    const uint RID_BUFF1 = 60070;
    const uint RID_BUFF2 = 60071;
    static int stopAfter;
    // M59_PARALYZE=1 / M59_WAIT=1: the other two states that stop you
    // moving, so the notifier's other two words can be photographed.
    static int paralyzeAfter, releaseAfter, waitAfter, unwaitAfter;
    static int blindAfter, painAfter, whiteAfter, invertAfter;
    // M59_SHOOT=1: an arrow from the rat to you, over and over.
    static int shootAfter;

    // ===================================================================
    // THE TEST SURFACE. Everything from here to the end of this block is
    // OFF unless its M59_* variable is set, so a run that sets none of
    // them is byte-for-byte the run it was before. Each switch names the
    // real-server behaviour it stands in for; notes/fake-server.md has the
    // recipes and what was verified. The messages are the library's own
    // classes (Meridian59/Protocol/GameMessages/GameMode/), and where the
    // server's own behaviour decides the content it is cited to Server-104.
    // ===================================================================
    static int EnvInt(string name, int dflt)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out int v) ? v : dflt;
    static bool EnvOn(string name) => Environment.GetEnvironmentVariable(name) == "1";
    static string EnvStr(string name) => Environment.GetEnvironmentVariable(name);

    // --- Doors and animating geometry -------------------------------------
    // One opening at M59_GEOM_AFTER messages after every room entry (default
    // 6, counted in client messages like M59_SHOOT), and one closing
    // M59_GEOM_PERIOD messages after that (default 24). Targets are the
    // SERVER ids the .roo carries, not array indices: RooFile matches on
    // RooSector.ServerID / RooSideDef.ServerID (`RooFile.cs:2250-2284`), so
    // id 0 - what every untagged sector has, and every sector of barinn -
    // would move ALL of them. The recipes in fake-server.md name rooms
    // whose doors are tagged.
    //
    // M59_SECTOR=<id>  SectorMoveMessage (PI 223): a door or a lift. The
    //   server's `User.SectorSendUser` (`kod/object/active/holder/nomoveon/battler/player/user.kod:4680-4689`) writes
    //   [type 1][sector 2][height 2][speed 1]; the room calls it for every
    //   player in the room when a sector moves (`kod/object/active/holder/room.kod:2988-3010`, `SetSector`), and replays
    //   every recorded move to a player entering the room at speed 0 (`room.kod:2196-2204`).
    //   M59_SECTOR_PLANE=ceiling|floor  which plane moves: ANIMATE_CEILING_LIFT
    //     / ANIMATE_FLOOR_LIFT (`include/proto.h:309-310`), default ceiling
    //   M59_SECTOR_TO=<h>     target height, in the .roo's own units (default 400)
    //   M59_SECTOR_BACK=<h>   height to go back to at the closing step
    //   M59_SECTOR_SPEED=<n>  0 is an instant jump (`RooSector.Tick`), default 16
    static readonly int geomAfter = EnvInt("M59_GEOM_AFTER", 6);
    static readonly int geomPeriod = EnvInt("M59_GEOM_PERIOD", 24);
    static readonly int sectorId = EnvInt("M59_SECTOR", -1);
    static readonly bool sectorFloor = EnvStr("M59_SECTOR_PLANE") == "floor";
    static readonly int sectorTo = EnvInt("M59_SECTOR_TO", 400);
    static readonly int sectorBack = EnvInt("M59_SECTOR_BACK", -1);
    static readonly int sectorSpeed = EnvInt("M59_SECTOR_SPEED", 16);
    // M59_SECTOR_CHANGE=<id>  SectorChangeMessage (PI 239): depth and
    //   texture scroll of a sector, the server's `SectorChangeSendUser`
    //   (`user.kod:4757-4766`: [sector 2][depth 1][scrollSpeed 1]).
    //   M59_SECTOR_DEPTH=0..3   (default 4 = "leave it": ChangeOverride,
    //     `RooSectorFlags.cs:40`), M59_SECTOR_SCROLL=0..3 (default 4 =
    //     "leave it": `TextureScrollSpeed.CHANGE_OVERRIDE`). The closing
    //     step sends M59_SECTOR_DEPTH_BACK / M59_SECTOR_SCROLL_BACK (default
    //     0 and 0) for whichever of the two was changed.
    static readonly int sectorChangeId = EnvInt("M59_SECTOR_CHANGE", -1);
    static readonly int sectorDepth = EnvInt("M59_SECTOR_DEPTH", 4);
    static readonly int sectorScroll = EnvInt("M59_SECTOR_SCROLL", 4);
    static readonly int sectorDepthBack = EnvInt("M59_SECTOR_DEPTH_BACK", 0);
    static readonly int sectorScrollBack = EnvInt("M59_SECTOR_SCROLL_BACK", 0);
    // M59_WALL=<sidedef id>  WallAnimateMessage (PI 225): a wall whose
    //   texture animates, the server's `WallSendUser` (`user.kod:4704-4742`:
    //   [wall 2][animation 1] then, per animation, none: [group 2], once:
    //   [period 4][low 2][high 2][final 2], cycle: [period 4][low 2][high 2],
    //   then [passable 1] - 0 leave alone, 1 passable, 2 impassable).
    //   M59_WALL_ANIM=cycle|once|none   default cycle
    //   M59_WALL_PERIOD=<ms>            default 500
    //   M59_WALL_GROUPS=<low>,<high>[,<final>]   bitmap groups, default 1,2
    //   M59_WALL_ACTION=none|passable|impassable  the trailing byte; the
    //     closing step sends the opposite of passable/impassable.
    //   The closing step stops the animation (`none`, first group).
    static readonly int wallId = EnvInt("M59_WALL", -1);
    static readonly string wallAnim = EnvStr("M59_WALL_ANIM") ?? "cycle";
    static readonly int wallPeriod = EnvInt("M59_WALL_PERIOD", 500);
    static readonly string wallGroups = EnvStr("M59_WALL_GROUPS") ?? "1,2";
    static readonly string wallAction = EnvStr("M59_WALL_ACTION") ?? "none";
    // M59_WALLTEX=<server id>  ChangeTextureMessage (PI 227): a texture
    //   swap, the server's `TextureSendUser` (`user.kod:4744-4753`:
    //   [id 2][new texture 2][flags 1]); flags are `CTF_*`
    //   (`include/proto.h:557-565`). The id names a SIDEDEF for the wall
    //   parts and a SECTOR for floor/ceiling.
    //   M59_WALLTEX_PART=above|normal|below|floor|ceiling   default normal
    //   M59_WALLTEX_TO=<n>     the new texture, a grdNNNNN.bgf number
    //   M59_WALLTEX_BACK=<n>   texture to restore at the closing step
    //     (CTF_RESET, 0x20, exists on the wire but RooFile ignores it -
    //     see fake-server.md - so the original is sent by number instead)
    static readonly int texId = EnvInt("M59_WALLTEX", -1);
    static readonly string texPart = EnvStr("M59_WALLTEX_PART") ?? "normal";
    static readonly int texTo = EnvInt("M59_WALLTEX_TO", 1018);
    static readonly int texBack = EnvInt("M59_WALLTEX_BACK", -1);
    static int geomCount = -1;       // -1: not running

    // --- A server verdict on the avatar's move ---------------------------
    // M59_YANK=<n>: every nth ReqMove is refused and the avatar is sent
    //   back. The server's own refusal is `UtilGoNearSquare` with the
    //   last legal square (`User.UserMove`, `user.kod:3982`: no-move flag
    //   or a destination outside the map `:4020-4027`, running with no
    //   vigor `:4154`, a room's own `IsMoveOK` `:4202`), which goes
    //   through `UtilGoToSquare` (`kod/util.kod:124-141`) to room
    //   `SomethingMoved` with no cause, and `User.SomethingMoved`
    //   answers EVERY move whose cause is not CAUSE_USER_INPUT - your
    //   own included - with BP_MOVE for yourself (`user.kod:8286-8311`,
    //   the packet at `:8302`), then `SomethingTurned` for the angle. So: a MoveMessage for the
    //   avatar, then a TurnMessage. Accepted moves are not answered at
    //   all (`user.kod:8300`), which is why the client predicts.
    //   M59_YANK_TO=<x>,<y>  always send it there instead of the last
    //     accepted position (a teleport-style yank)
    //   M59_YANK_SPEED=<n>   MovementSpeed byte, default 0 (what
    //     `UtilGoToSquare` sends: SomethingMoved's speed default)
    static readonly int yankEvery = EnvInt("M59_YANK", 0);
    static readonly string yankTo = EnvStr("M59_YANK_TO");
    static readonly int yankSpeed = EnvInt("M59_YANK_SPEED", 0);
    static int reqMoves;
    static ushort okX, okY, okAngle;      // last move the server "accepted"

    // --- A room change with a gap ---------------------------------------
    // M59_ROOMGAP_MS=<ms>: on a Go (not on first entry), PlayerMessage goes
    //   out at once and everything after it - RoomContents, and with it the
    //   sounds and enchantments that follow the contents - is held back
    //   that long. The real server sends BP_PLAYER and BP_ROOM_CONTENTS as
    //   two packets (`clientd3d/server.c:81-82`) and the contents have to be
    //   collected and encoded; over a real link the client sits in the gap
    //   with a new room and no avatar. The library has no avatar until
    //   RoomContents lands, which is the window this opens.
    static readonly int roomGapMs = EnvInt("M59_ROOMGAP_MS", 0);
    static int roomsEntered;
    static readonly List<Timer> gapTimers = new List<Timer>();
    static readonly object sendLock = new object();

    // --- Room light ----------------------------------------------------
    // M59_AMBIENT=<0-255>: RoomInfo.AmbientLight for both rooms (default
    //   160, what the fixture always sent). Not a stand-in for anything the
    //   server does differently - it is `PlayerMessage`'s own field - but
    //   most .roo files other than barinn and barlmarket render near-black
    //   at 160, and a door that opens in the dark proves nothing to the eye.
    static readonly byte ambient = (byte)EnvInt("M59_AMBIENT", 160);

    // --- Another room, another spawn, a room the client lacks ----------
    // M59_ROOM2=<file.roo>   the room a Go leads to (default barlmarket.roo)
    // M59_SPAWN=<x>,<y>[,<angle>]   room 1's avatar, in server units (kod =
    //   room/16 + 64), angle in whole radians as `Obj` takes it. Default:
    //   the computed spawn (barinn gives 752,672, the coordinate this
    //   fixture always used). M59_SPAWN2= the same for the second room.
    // M59_BADROOM=1   the second room names "zzmissing.roo", which the
    //   client's resource folder does not have: a client on an old resource
    //   set or a server with a newer map. PlayerMessage and RoomContents go
    //   out exactly as for a good room, so the failed load is the client's
    //   alone. (The real server cannot know what files a client has.)
    static readonly string room2Env = EnvStr("M59_ROOM2");
    static readonly bool badRoom = EnvOn("M59_BADROOM");

    // --- Icons on the spell and skill rows ------------------------------
    // M59_STATICONS=1: StatList rows carry a ResourceIconID. The server
    //   sends `4,Send(oSpell,@GetIcon)` / `4,Send(oSkill,@GetIcon)` as the
    //   last field of every row (`user.kod:9409-9420`, `:9436-9447`). The
    //   fixture sent 0. The icons here are deliberately NOT the ones the
    //   spell/skill objects carry, so a window that drew the object's art
    //   instead of the row's would show the wrong picture.
    static readonly bool wantStatIcons = EnvOn("M59_STATICONS");

    // --- ReqLook on a skill ---------------------------------------------
    // M59_LOOKSKILL=1: ReqLook with a skill id (5101, 5102) is answered
    //   with LookSkillMessage (PI 192) instead of the rat's Look. Content
    //   follows `Skill.ShowDesc` (`kod/object/passive/skill.kod:141-163`):
    //   a description, the school and the level, as server strings.
    static readonly bool wantLookSkill = EnvOn("M59_LOOKSKILL");

    // --- The second room's enchantments ----------------------------------
    // M59_ROOM2BUFFS=1: the second room sends one room enchantment. Room
    //   buffs are the room's, re-sent on every entry - the library clears
    //   RoomBuffs on both RoomContents and Player for that reason
    //   (`DataController.cs:2225`, `:2346`); `EnterSecondRoom` used to
    //   return before any were sent, so the list could only ever be
    //   emptied, never refilled, after a Go. It sends ONE where room 1
    //   sends two, so a stale list shows as the wrong count.
    static readonly bool wantRoom2Buffs = EnvOn("M59_ROOM2BUFFS");

    // --- A stack that changes under the client -------------------------
    // M59_STACK=shrink|replace: the coins (8003, 25 of them) change
    //   M59_STACK_AFTER messages into the session (default 12), to
    //   M59_STACK_TO (default 5).
    //   shrink:  ChangeMessage (PI 219) for the stack. This is what the
    //     server does when a NumberItem's count changes:
    //     `SubtractNumber` -> `NewNumber` -> `User.SomethingChanged`
    //     (`kod/object/item/passitem/numbitem.kod:259-297`,
    //     `user.kod:8678-8706`), which sends BP_CHANGE with the object in
    //     its inventory form, count included (`ToCliObject`, `user.kod:3095-3108`).
    //   replace: InventoryRemove of the stack and InventoryAdd of a new
    //     one (id 8004) - what a stack running out and another arriving
    //     looks like (`numbitem.kod:265-271` deletes at <= 0).
    // The fixture's own bag (ReqInventory) follows the change, so a later
    // reopen agrees with the wire.
    static readonly string stackMode = EnvStr("M59_STACK");
    static readonly int stackAfter = EnvInt("M59_STACK_AFTER", 12);
    static readonly int stackTo = EnvInt("M59_STACK_TO", 5);
    static int stackCount = -1;      // -1: not running
    static uint coinId = 8003;
    static uint coinNow = 25;
    // ===================================================================
    const uint RID_SPELLDESC = 60110;
    const uint RID_SCHOOL = 60111;
    const uint RID_LEVEL = 60112;
    const uint RID_MANA = 60113;
    const uint RID_VIGOR = 60114;
    const uint RID_QHEAD1 = 60100;
    const uint RID_QHEAD2 = 60101;
    const uint RID_QUEST1 = 60102;
    const uint RID_QUEST2 = 60103;
    const uint RID_QUEST3 = 60104;
    const uint RID_MIGHT = 60090;
    const uint RID_INTELLECT = 60091;
    const uint RID_STAMINA = 60092;
    const uint RID_AGILITY = 60093;
    const uint RID_MYSTICISM = 60094;
    const uint RID_AIM = 60095;
    const uint RID_QDESC1 = 60120;
    const uint RID_QREQ1 = 60121;
    const uint RID_QDESC2 = 60122;
    const uint RID_QREQ2 = 60123;
    const uint RID_QDESC3 = 60124;
    const uint RID_QREQ3 = 60125;
    const uint RID_RATSOUND = 60080;
    const uint RID_MUSIC = 60081;

    /// <summary>The server's own copy of what it wrote to the string file.</summary>
    /// <summary>How many times each object has been attacked this run.</summary>
    static readonly Dictionary<uint, int> hitsOn = new Dictionary<uint, int>();

    static readonly StringDictionary strings = new StringDictionary();

    static string room = "barinn.roo";
    /// <summary>Where a Go takes you, and a second Go brings you back.</summary>
    static string room2 = "barlmarket.roo";
    static bool inRoom2;
    /// <summary>Session-scoped things are sent once, not once per room.</summary>
    static bool sessionExtrasSent;
    static string dir = "/tmp/res";

    static int Main(string[] args)
    {
        int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 15999;
        if (args.Length > 1) dir = args[1];
        if (args.Length > 2) room = args[2];
        if (room2Env != null) room2 = room2Env;

        EnsureStrings();

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Console.WriteLine($"fake server on 127.0.0.1:{port}, room {room}, resources {dir}");

        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            Console.WriteLine("client connected");
            try { Serve(client); }
            catch (Exception e) { Console.WriteLine($"client gone: {e}"); }
            finally { client.Close(); }
        }
    }

    /// <summary>
    /// Writes a string dictionary the client can resolve our ids through,
    /// unless the resource folder already has one. Without it the client
    /// cannot turn a room file id into "barinn.roo" and lands nowhere.
    /// </summary>
    static void EnsureStrings()
    {
        string path = Path.Combine(dir, "rsc0000.rsb");

        var stringList = new List<RsbResourceID>
        {
            new RsbResourceID(RID_ROOMFILE,   room,          4),
            new RsbResourceID(RID_ROOMNAME,   "Somewhere",   4),
            new RsbResourceID(RID_ROOMFILE2,  room2,         4),
            new RsbResourceID(RID_ROOMNAME2,  "Elsewhere",   4),
            new RsbResourceID(RID_ARROWBGF,   "arrowsil.bgf", 4),
            new RsbResourceID(RID_ROOMFILE3,  "zzmissing.roo", 4),
            new RsbResourceID(RID_ROOMNAME3,  "Nowhere",     4),
            new RsbResourceID(RID_SKILLDESC,
                "A quick cut that opens a guard, at the price of your footing.", 4),
            new RsbResourceID(RID_SKILLSCHOOL, "School: Fencing", 4),
            new RsbResourceID(RID_SKILLLEVEL,  "Level 1", 4),
            new RsbResourceID(RID_PLAYERNAME, "Tester",      4),
            new RsbResourceID(RID_RATNAME,    "a duskrat",   4),
            new RsbResourceID(RID_PLAYERBGF,  "bri.bgf",     4),
            new RsbResourceID(RID_RATBGF,     "duskrat.bgf", 4),
            // Inline styles, the way the server really sends them: ~B is
            // bold, ~n back to normal, and a colour letter picks one of
            // vanilla's six - r red, g green, b blue, q purple, k black,
            // w white. The client parses these out of the string and
            // hands the view a list of styled runs.
            new RsbResourceID(RID_GREETING,   "~BThe duskrat~n regards you with ~rmild contempt~w.", 4),
            new RsbResourceID(RID_ECHO,       "The duskrat has nothing to say about that.", 4),
            new RsbResourceID(RID_COIN,       "a gold doubloon", 4),
            new RsbResourceID(RID_COINBGF,    "doubloon.bgf",    4),
            new RsbResourceID(RID_BOOK,       "a tattered book", 4),
            new RsbResourceID(RID_BOOKBGF,    "book1.bgf",       4),
            new RsbResourceID(RID_AXE,        "a nerudite axe",  4),
            new RsbResourceID(RID_AXEBGF,     "neruaxe.bgf",     4),
            new RsbResourceID(RID_ALICE,      "Alice",           4),
            new RsbResourceID(RID_BORIS,      "Boris the Outlaw", 4),
            new RsbResourceID(RID_SPELL1,     "shalille's touch", 4),
            new RsbResourceID(RID_SPELL2,     "kraanan's blessing", 4),
            new RsbResourceID(RID_SKILL1,     "slash",            4),
            new RsbResourceID(RID_SKILL2,     "bandaging",        4),
            // A mail's subject is not a field on the wire: it is the
            // first line of the body, after a "Subject: " that the
            // client strips back off. So these strings carry it.
            new RsbResourceID(RID_MAIL1,
                "Subject: The cellar again\nAlice says the rats are back. She is not wrong.", 4),
            new RsbResourceID(RID_MAIL2,
                "Subject: Re: your axe\nI have it. Come and get it, or do not.", 4),
            // Stand-in art. This fixture's resource folder has no face
            // parts in it at all - no hair, eye, nose or mouth BGFs -
            // so the palette points at a body and a hat. The compose
            // path is the same one a real head goes through; only the
            // pictures are wrong.
            // Conditions carry a string id like everything else, and
            // the client resolves it into the bar's name. Sending zero
            // leaves every bar anonymous, which is what this fixture
            // did while the client had nowhere to show a name anyway.
            new RsbResourceID(RID_COND1,      "health",           4),
            new RsbResourceID(RID_COND2,      "mana",             4),
            new RsbResourceID(RID_COND3,      "vigor",            4),
            new RsbResourceID(RID_COND4,      "toughness",        4),
            new RsbResourceID(RID_GLOBE,      "a notice board",   4),
            new RsbResourceID(RID_HEADLINE,   "Nothing here is true, and this is the board that says so.", 4),
            new RsbResourceID(RID_HEADBGF,    "bri.bgf",          4),
            new RsbResourceID(RID_HAIRBGF,    "book1.bgf",        4),
            new RsbResourceID(RID_SPELLA,     "blink",            4),
            new RsbResourceID(RID_SPELLB,     "shal'ille's touch", 4),
            new RsbResourceID(RID_SPELLC,     "qor's curse",      4),
            new RsbResourceID(RID_SKILLA,     "slash",            4),
            new RsbResourceID(RID_SKILLB,     "bandaging",        4),
            new RsbResourceID(RID_ROOMBUFF1,  "shal'ille's grace", 4),
            new RsbResourceID(RID_ROOMBUFF2,  "a lingering fog",   4),
            new RsbResourceID(RID_BUFF1,      "shielding",        4),
            new RsbResourceID(RID_BUFF2,      "haste",            4),
            // Sound files are named as .wav in the string table and the
            // library swaps the extension to .ogg, which is what is
            // actually on disk.
            new RsbResourceID(RID_SPELLDESC,
                "Lays a hand on a wound and closes it, at some cost to the caster.", 4),
            new RsbResourceID(RID_SCHOOL,     "School: Shal'ille", 4),
            new RsbResourceID(RID_LEVEL,      "Level 2",           4),
            new RsbResourceID(RID_MANA,       "Mana 8",            4),
            new RsbResourceID(RID_VIGOR,      "Vigor 3",           4),
            new RsbResourceID(RID_QHEAD1,     "Available",        4),
            new RsbResourceID(RID_QHEAD2,     "In progress",      4),
            new RsbResourceID(RID_QUEST1,     "Clear the cellar", 4),
            new RsbResourceID(RID_QUEST2,     "Deliver the ledger", 4),
            new RsbResourceID(RID_QUEST3,     "Find Alice's ring", 4),
            new RsbResourceID(RID_MIGHT,      "might",            4),
            new RsbResourceID(RID_INTELLECT,  "intellect",        4),
            new RsbResourceID(RID_STAMINA,    "stamina",          4),
            new RsbResourceID(RID_AGILITY,    "agility",          4),
            new RsbResourceID(RID_MYSTICISM,  "mysticism",        4),
            new RsbResourceID(RID_AIM,        "aim",              4),
            new RsbResourceID(RID_QDESC1,
                "The cellar under the inn has gone to rats, and the innkeeper has gone to pieces.", 4),
            new RsbResourceID(RID_QREQ1,
                "~gEight killed~n of the twenty asked for. Return to Alice when it is done.", 4),
            new RsbResourceID(RID_QDESC2,
                "A ledger owed to the guild across the water, and nobody willing to carry it.", 4),
            new RsbResourceID(RID_QREQ2,
                "Requires ~bfive levels of stealth~n and a free hand.", 4),
            new RsbResourceID(RID_QDESC3,
                "Alice lost a ring in the dark and would rather not say how.", 4),
            new RsbResourceID(RID_QREQ3,
                "~rRequires a lantern you do not have.~n", 4),
            new RsbResourceID(RID_RATSOUND,   "Rat_awr.wav",      4),
            new RsbResourceID(RID_MUSIC,      "AMBCave.wav",      4),
            new RsbResourceID(RID_RATLOOK,
                "A duskrat, grey-brown and unbothered. Its tail is longer than the rest of it.", 4),
        };

        foreach (RsbResourceID r in stringList)
            FakeServer.strings.TryAdd(r.ID, r.Text, r.Language);

        // A file left over from an older run is worse than no file: the
        // client resolves the ids it knows and comes up empty for every
        // id added since, which looks like a broken message rather than a
        // stale dictionary. So it is rewritten unless it already has all
        // of them.
        if (File.Exists(path) && HasAll(path, stringList))
        {
            Console.WriteLine($"using the string file already in {dir}");
            return;
        }

        try
        {
            new RsbFile(stringList).Save(path);
            Console.WriteLine($"wrote {path} with {stringList.Count} strings");
        }
        catch (Exception e) { Console.WriteLine($"could not write {path}: {e.Message}"); }
    }

    /// <summary>True when every id we hand out is already in that file.</summary>
    /// <summary>
    /// True when the file on disk already says everything this run
    /// intends to say. Ids alone are not enough: an id whose TEXT
    /// changed - the second room's filename, say - would keep the old
    /// value, and the client would then load a room the server never
    /// named while both logs looked right. That cost a run.
    /// </summary>
    static bool HasAll(string path, List<RsbResourceID> wanted)
    {
        try
        {
            var have = new RsbFile(); have.Load(path);
            var text = new Dictionary<uint, string>();
            foreach (RsbResourceID r in have.StringResources) text[r.ID] = r.Text;

            foreach (RsbResourceID r in wanted)
                if (!text.TryGetValue(r.ID, out string was) || was != r.Text)
                    return false;

            return true;
        }
        catch { return false; }
    }

    static void Serve(TcpClient client)
    {
        NetworkStream ns = client.GetStream();
        var ctrl = new MessageControllerClient(new StringDictionary());

        Send(ns, ctrl, new GetLoginMessage());

        bool inGame = false;
        var deadline = DateTime.UtcNow.AddMinutes(10);

        while (client.Connected && DateTime.UtcNow < deadline)
        {
            byte pi = ReadMessage(ns, out byte[] body);
            if (body == null) break;

            if (!inGame)
            {
                switch ((MessageTypeLoginMode)pi)
                {
                    case MessageTypeLoginMode.Login:
                        Console.WriteLine("  <- Login");
                        Send(ns, ctrl, new LoginOKMessage(AccountType.USER, 4242));
                        // GetChoice carries the hash table; the client
                        // answers it with ReqGameState.
                        Send(ns, ctrl, new GetChoiceMessage(new HashTable()));
                        break;

                    case MessageTypeLoginMode.ReqGame:
                        Console.WriteLine("  <- ReqGameState");
                        Send(ns, ctrl, new GameStateMessage());
                        inGame = true;
                        Console.WriteLine("  (client is in game mode now)");
                        SendCharacters(ns, ctrl);
                        break;

                    default:
                        Console.WriteLine($"  <- login-mode {pi}, ignored");
                        break;
                }
                continue;
            }

            // The looping sound is stopped a few messages in, so the stop
            // path gets exercised the way it happens in the game: some
            // time after the loop started, by name.
            if (stopAfter > 0 && --stopAfter == 0)
            {
                Send(ns, ctrl, new StopWaveMessage(
                    new StopSound(RID_RATSOUND, 0)));
            }

            // Paralyze holds you still until a Release; Wait is the
            // server saying "saving, stand by". Both stop movement in
            // BaseClient, and both are server-driven with nothing to
            // press, so they go out on a count like the sound stop.
            if (paralyzeAfter > 0 && --paralyzeAfter == 0)
            {
                Console.WriteLine("  -> Paralyze");
                Send(ns, ctrl, new EffectMessage(new EffectParalyze()));
            }
            if (releaseAfter > 0 && --releaseAfter == 0)
            {
                Console.WriteLine("  -> Release");
                Send(ns, ctrl, new EffectMessage(new EffectRelease()));
            }
            if (waitAfter > 0 && --waitAfter == 0)
            {
                Console.WriteLine("  -> Wait");
                Send(ns, ctrl, new WaitMessage());
            }

            // The things that happen to your eyes. M59_BLIND=1 blinds
            // you outright, M59_PAIN=1 flashes red and M59_WHITEOUT=1
            // flashes white, both over three seconds; M59_INVERT=1 is
            // the bonk on the head. There is nothing to press for any
            // of them, so they go out on a count like the sound stop.
            if (blindAfter > 0 && --blindAfter == 0)
            {
                Console.WriteLine("  -> Blind");
                Send(ns, ctrl, new EffectMessage(new EffectBlind()));
            }
            if (painAfter > 0 && --painAfter == 0)
            {
                Console.WriteLine("  -> Pain");
                Send(ns, ctrl, new EffectMessage(new EffectPain(3000)));
            }
            if (whiteAfter > 0 && --whiteAfter == 0)
            {
                Console.WriteLine("  -> Whiteout");
                Send(ns, ctrl, new EffectMessage(new EffectWhiteOut(3000)));
            }
            if (invertAfter > 0 && --invertAfter == 0)
            {
                Console.WriteLine("  -> Invert");
                Send(ns, ctrl, new EffectMessage(new EffectInvert()));
            }
            if (unwaitAfter > 0 && --unwaitAfter == 0)
            {
                Console.WriteLine("  -> Unwait");
                Send(ns, ctrl, new UnwaitMessage());
            }

            // An arrow in flight. The library resolves the source and
            // the target against the room, refuses the projectile if
            // either is missing, then moves it itself every tick - so
            // one message is the whole feature from here, and whether
            // anything appears is entirely the client's half.
            if (shootAfter > 0 && --shootAfter == 0)
            {
                Console.WriteLine("  -> Shoot 2003 -> avatar");
                Send(ns, ctrl, new ShootMessage(new Projectile(
                    RID_ARROWBGF, AnimationType.NONE, 0, 0, new AnimationNone(),
                    new ObjectID(2003), new ObjectID(1001), 8, 0, new LightingInfo())));
                shootAfter = 12;  // and again, so a shot is easy to catch
            }

            // The stat change wizard has no request behind it - the
            // server offers one and the client puts it up. There is
            // nothing for a test to press, so it goes out a few
            // messages in, and only when asked for: it covers the
            // screen, and every other fixture here wants a clear one.
            if (statChangeAfter > 0 && --statChangeAfter == 0)
                SendStatChange(ns, ctrl);

            // The test-surface switches that run on a message count.
            Geometry(ns, ctrl);
            StackChange(ns, ctrl);

            switch ((MessageTypeGameMode)pi)
            {
                case MessageTypeGameMode.SendCharacters:
                    Console.WriteLine("  <- SendCharacters");
                    SendCharacters(ns, ctrl);
                    break;

                case MessageTypeGameMode.UseCharacter:
                    Console.WriteLine("  <- UseCharacter");
                    EnterRoom(ns, ctrl);
                    Say(ns, ctrl, RID_GREETING);
                    break;

                case MessageTypeGameMode.ReqAttack:
                    // Attacking was the one common thing the fixture did
                    // not answer: the client sent 103 and the server
                    // ignored it, so nothing downstream of a kill - the
                    // target clearing, the action row emptying, the red
                    // outline going away - had ever been exercised.
                    //
                    // Three hits kill. The count is per object so two
                    // rats can be fought in one session, and the reply
                    // is the same RemoveMessage a real server sends when
                    // a creature dies and leaves the room.
                    // The body is [PI=103][01][ObjectID], so the id starts at
                    // TWO. Read at one - the offset ReqLook uses - it comes
                    // back as 0x7D101 instead of 0x7D1: the id shifted up a
                    // byte with that 01 pulled into the bottom. The removal
                    // then names an object that does not exist, the client
                    // correctly ignores it, and the rat looks unkillable.
                    // This cost an hour of reading client code that was right.
                    uint hit = body.Length >= 6 ? BitConverter.ToUInt32(body, 2) : 0;
                    hitsOn.TryGetValue(hit, out int sofar);
                    hitsOn[hit] = ++sofar;
                    Console.WriteLine($"  <- ReqAttack {hit} (hit {sofar} of 3)");
                    if (sofar >= 3)
                    {
                        Console.WriteLine($"  -> Remove {hit} (it dies)");
                        Send(ns, ctrl, new RemoveMessage(hit));
                    }
                    break;

                case MessageTypeGameMode.ReqLook:
                    // The look reply: the object, what kind of look it is,
                    // the description, and an inscription if it has one.
                    // The client's own LookObject is what goes up on
                    // screen, so this is all the window needs.
                    // There is no separate request for a spell's
                    // description: the client sends ReqLook with the
                    // spell's id and the server decides to answer with
                    // LookSpell rather than Look. The ids the spell list
                    // hands out are the ones sent in the Spells message.
                    uint lookAt = body.Length >= 5 ? BitConverter.ToUInt32(body, 1) : 0;
                    Console.WriteLine($"  <- ReqLook {lookAt}");
                    if (lookAt == 5001 || lookAt == 5002) SendLookSpell(ns, ctrl);
                    else if (wantLookSkill && (lookAt == 5101 || lookAt == 5102))
                        SendLookSkill(ns, ctrl, lookAt);
                    // A news globe answers with LookNewsGroup rather
                    // than Look - the same request, a different reply,
                    // exactly as with a spell.
                    //
                    // M59_NEWS=1 makes the book on the floor answer that
                    // way too. Placing a separate globe and hitting it
                    // with a screen tap turned out to be a fight with
                    // the room's geometry rather than a test of the
                    // window, and the container fixture wants that book
                    // to stay a container the rest of the time.
                    else if (lookAt == 3104 || (wantNews && lookAt == 3101))
                        SendNewsGroup(ns, ctrl);
                    else SendLook(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqGet:
                {
                    // Which object is being taken decides what happens,
                    // which it did not use to: every get opened the
                    // container, so taking something off the floor - by
                    // far the commoner thing - had no fixture at all,
                    // and neither did InventoryAdd, a message the real
                    // server sends constantly. It is also the path that
                    // binds a hotbar button waiting for an item of that
                    // name, so that could not be tested either.
                    // Through ObjectID, not a raw uint read: the top
                    // four bits are a flag saying a count follows, and
                    // the client sets it now that a get carries how
                    // many. Compared raw, no id ever matches again.
                    uint getting = 0;
                    uint gettingCount = 0;
                    if (body.Length >= 5)
                    {
                        var asked = new ObjectID(body, 1);
                        getting = asked.ID;
                        gettingCount = asked.Count;
                    }
                    Console.WriteLine($"  <- ReqGet {getting} x{gettingCount}");

                    if (getting == 3101)
                    {
                        // The container. The first get opens it; each one
                        // after takes something out, and this server
                        // tracks nothing, so it sends a shorter list.
                        if (!lootOpen) { lootOpen = true; SendLoot(ns, ctrl, lootLeft); }
                        else { SendLoot(ns, ctrl, --lootLeft); Say(ns, ctrl, RID_ECHO); }
                    }
                    else
                    {
                        // Something off the floor goes into your pack,
                        // and stays there - the next ReqInventory sends
                        // it back with the rest.
                        InventoryObject got = Carry(
                            8100 + getting % 100, RID_COINBGF, RID_COIN,
                            gettingCount > 0 ? gettingCount : 17, false);
                        takenSoFar.Add(got);
                        Send(ns, ctrl, new InventoryAddMessage(got));
                        // And it leaves the floor. Without this the thing
                        // went into the pack and stayed lying in the room
                        // as well, so nothing downstream of a successful
                        // get - the loot window emptying, the object
                        // going from the room and from the minimap - was
                        // being tested. A real server sends this.
                        Send(ns, ctrl, new RemoveMessage(getting));
                        Say(ns, ctrl, RID_ECHO);
                    }
                    break;
                }

                case MessageTypeGameMode.ReqGo:
                    // The Open key's request: "take me through whatever
                    // I am standing at". Its body is the PI and nothing
                    // else - there is no argument to read. A real server
                    // answers with a room change, and so does this one:
                    // Go takes you to the second room, Go again brings
                    // you back, which is the whole room-change path -
                    // new walls, new object list, a rebuilt map - with
                    // one button and no door to stand on.
                    Console.WriteLine("  <- ReqGo");
                    inRoom2 = !inRoom2;
                    EnterRoom(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqMove when yankEvery > 0:
                    // M59_YANK: see the block of statics. Everything else
                    // falls to the default below and is logged by name.
                    ServerVerdict(ns, ctrl, body);
                    break;

                case MessageTypeGameMode.ReqCast:
                case MessageTypeGameMode.ReqPerform:
                    // Named rather than left as "game-mode 105,
                    // ignored". Whether a cast reached the wire at all
                    // is the first question every spell bug asks, and
                    // an anonymous number is a poor answer. The body is
                    // the PI, the id and then a target list whose shape
                    // depends on the spell, so only the id is read.
                    Console.WriteLine(
                        $"  <- {(MessageTypeGameMode)pi} id {(body.Length > 2 ? body[1] | (body[2] << 8) : -1)}");
                    break;

                case MessageTypeGameMode.UserCommand:
                {
                    // The body is the PI and then the user-command type,
                    // so one byte says which of two dozen commands this
                    // is without parsing the rest.
                    byte cmd = body.Length > 1 ? body[1] : (byte)0;
                    Console.WriteLine($"  <- UserCommand {(UserCommandType)cmd}");
                    if (cmd == (byte)UserCommandType.ReqGuildInfo) SendGuild(ns, ctrl);
                    if (cmd == (byte)UserCommandType.ReqGuildList) SendGuildList(ns, ctrl);
                    break;
                }

                case MessageTypeGameMode.System:
                {
                    // A system message wraps a sub-message, and the byte
                    // after the PI says which. SendCharInfo asks for the
                    // character-creation palette; NewCharInfo is the
                    // finished character coming back.
                    byte sub = body.Length > 1 ? body[1] : (byte)0;
                    Console.WriteLine($"  <- SystemMessage sub {sub}");
                    if (sub == (byte)MessageTypeGameMode.SendCharInfo)
                        SendCharInfo(ns, ctrl);
                    else if (sub == (byte)MessageTypeGameMode.NewCharInfo)
                    {
                        // A real server makes the character and answers
                        // with its id; the client logs it in on that.
                        //
                        // M59_NAMETAKEN=1 refuses it instead, which is
                        // the case the client had no answer for: the
                        // wizard used to close on hope and leave an
                        // empty screen and no message behind.
                        if (Environment.GetEnvironmentVariable("M59_NAMETAKEN") == "1")
                        {
                            Console.WriteLine("     (new character refused: name in use)");
                            Send(ns, ctrl, new CharInfoNotOkMessage(
                                CharInfoNotOkError.NameInUse));
                        }
                        else
                        {
                            Console.WriteLine("     (new character accepted)");
                            Send(ns, ctrl, new CharInfoOkMessage(1001));
                        }
                    }
                    break;
                }

                case MessageTypeGameMode.ReqArticles:
                    Console.WriteLine("  <- ReqArticles");
                    SendArticles(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqArticle:
                {
                    // The globe id comes first as a short, then the
                    // article number as an int - reading the number
                    // straight off the PI gives the two glued together
                    // (article 1 on globe 7 arrives as 65543).
                    uint num = body.Length >= 7 ? BitConverter.ToUInt32(body, 3) : 0;
                    Console.WriteLine($"  <- ReqArticle {num}");
                    Send(ns, ctrl, new ArticleMessage(Body(num)));
                    break;
                }

                case MessageTypeGameMode.PostArticle:
                    Console.WriteLine("  <- PostArticle");
                    posted++;
                    break;

                case MessageTypeGameMode.DeleteNews:
                    Console.WriteLine("  <- DeleteNews");
                    deleted++;
                    break;

                case MessageTypeGameMode.ReqGetMail:
                    Console.WriteLine("  <- ReqGetMail");
                    SendMail(ns, ctrl);
                    break;

                case MessageTypeGameMode.DeleteMail:
                    // The client deletes the server's copy the moment it
                    // has one, which is how a mailbox that lives on disk
                    // avoids downloading the same mail twice.
                    Console.WriteLine("  <- DeleteMail");
                    break;

                case MessageTypeGameMode.ReqLookupNames:
                    Console.WriteLine("  <- ReqLookupNames");
                    SendLookup(ns, ctrl, body);
                    break;

                case MessageTypeGameMode.SendMail:
                    Console.WriteLine("  <- SendMail");
                    Say(ns, ctrl, RID_ECHO);
                    break;

                case MessageTypeGameMode.ReqNPCQuests:
                    Console.WriteLine("  <- ReqNPCQuests");
                    SendNPCQuests(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqTriggerQuest:
                    // A real server starts the quest and says so. This
                    // just answers, so the accept is visible from the
                    // client's side.
                    Console.WriteLine("  <- ReqTriggerQuest");
                    Say(ns, ctrl, RID_ECHO);
                    break;

                case MessageTypeGameMode.ReqBuy:
                    Console.WriteLine("  <- ReqBuy");
                    SendStock(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqBuyItems:
                {
                    // A real server takes the money and hands over the
                    // goods. This used to say something and hand over
                    // nothing, so the half of the shop that matters -
                    // does what I bought turn up in my pack - could not
                    // be tested at all.
                    Console.WriteLine("  <- ReqBuyItems");
                    try
                    {
                        // Parsed by hand rather than with
                        // ReqBuyItemsMessage: that reads a whole TCP
                        // message and checks its length, and what is in
                        // hand here is the body. Handing it the body
                        // gives "WrongLEN - input:256 expected:8061".
                        // The shape is the PI, the seller, a count, then
                        // that many ObjectIDs - which do know how to
                        // read themselves, and how long they are.
                        int cursor = 1 + 4;
                        ushort lines = BitConverter.ToUInt16(body, cursor);
                        cursor += 2;
                        for (int i = 0; i < lines; i++)
                        {
                            var line = new ObjectID(body, cursor);
                            cursor += line.ByteLength;
                            // The shop's own lines, by id, so buying the
                            // book does not hand over an axe.
                            (uint bgf, uint name) = line.ID switch
                            {
                                7001 => (RID_AXEBGF, RID_AXE),
                                7002 => (RID_BOOKBGF, RID_BOOK),
                                _    => (RID_COINBGF, RID_COIN),
                            };
                            InventoryObject bought = Carry(
                                8200 + line.ID % 100, bgf, name,
                                line.Count > 0 ? line.Count : 0u, false);
                            takenSoFar.Add(bought);
                            Send(ns, ctrl, new InventoryAddMessage(bought));
                            Console.WriteLine($"  -> sold {line.ID} x{line.Count}");
                        }
                    }
                    catch (Exception e) { Console.WriteLine($"  !! buy: {e.Message}"); }
                    Say(ns, ctrl, RID_ECHO);
                    break;
                }

                case MessageTypeGameMode.ReqOffer:
                case MessageTypeGameMode.ReqCounterOffer:
                {
                    // The lines are logged, not just the message name:
                    // offering part of a stack is a thing the client can
                    // now do, and "it said 23 on screen" is not evidence
                    // that 23 went out. Same shape as a buy - the PI,
                    // the partner, a count, then that many ObjectIDs.
                    Console.WriteLine($"  <- {(MessageTypeGameMode)pi}");
                    try
                    {
                        // An offer names the partner first; a counter
                        // offer does not - it is a reply, so the server
                        // already knows who to. Reading one as the other
                        // silently gives a nonsense count and prints
                        // nothing, which is how this first went wrong.
                        int cursor = 1;
                        if ((MessageTypeGameMode)pi == MessageTypeGameMode.ReqOffer)
                        {
                            var partner = new ObjectID(body, cursor);
                            cursor += partner.ByteLength;
                        }
                        ushort lines = BitConverter.ToUInt16(body, cursor);
                        cursor += 2;
                        for (int i = 0; i < lines; i++)
                        {
                            var line = new ObjectID(body, cursor);
                            cursor += line.ByteLength;
                            Console.WriteLine($"     offered {line.ID} x{line.Count}");
                        }

                        // And the server confirms YOUR side back to
                        // you. Until this existed the offer went out and
                        // nothing came back, so TradeInfo.IsItemsYouSet
                        // stayed false and the window sat on "Offer"
                        // forever - the half of the trade that belongs
                        // to the server was never tested at all.
                        // Offered answers an offer, CounterOffered a
                        // counter: the client files them in the same
                        // place but only the matching one arrives in a
                        // real session.
                        var mine = new ObjectBase[]
                        {
                            Item(9201, RID_BOOKBGF, RID_BOOK, 0),
                        };
                        if ((MessageTypeGameMode)pi == MessageTypeGameMode.ReqOffer)
                            Send(ns, ctrl, new OfferedMessage(mine));
                        else
                            Send(ns, ctrl, new CounterOfferedMessage(mine));
                        Console.WriteLine("  -> confirmed your side");
                    }
                    catch (Exception e) { Console.WriteLine($"  !! offer: {e.Message}"); }
                    Say(ns, ctrl, RID_ECHO);
                    break;
                }

                case MessageTypeGameMode.AcceptOffer:
                    Console.WriteLine("  <- AcceptOffer");
                    Say(ns, ctrl, RID_ECHO);
                    break;

                case MessageTypeGameMode.CancelOffer:
                    Console.WriteLine("  <- CancelOffer");
                    break;

                case MessageTypeGameMode.SendPlayers:
                    // Alice says something every time the Who list is
                    // asked for, so muting her can be seen to work: ask
                    // twice with a mute in between and the second line
                    // never arrives.
                    //
                    // 4002 because that is Alice in the *online players*
                    // list, which is the list HandleSaid looks the
                    // speaker up in. 4001 is Alice as a room object and
                    // Tester in the who list - this fixture's id spaces
                    // do not line up, and speaking as 4001 muted
                    // nothing at all.
                    SayAs(ns, ctrl, 4002, RID_PLAYERBGF, RID_GREETING);
                    Console.WriteLine("  <- SendPlayers");
                    SendWho(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqInventory:
                    Console.WriteLine("  <- ReqInventory");
                    SendBag(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqDrop:
                    // A count of zero means the whole thing; anything
                    // else came from the amount prompt.
                    // PI, then the ObjectID: four bytes of id and four
                    // of count. Zero count means the whole thing.
                    if (body.Length >= 9)
                        Console.WriteLine($"  <- ReqDrop id {BitConverter.ToUInt32(body, 1)} count {BitConverter.ToUInt32(body, 5)}");
                    else
                        Console.WriteLine($"  <- ReqDrop ({body.Length} bytes)");
                    Say(ns, ctrl, RID_ECHO);
                    break;

                case MessageTypeGameMode.ReqPut:
                    // The item's id and count, then the container's id
                    // with a count of zero.
                    if (body.Length >= 13)
                        Console.WriteLine($"  <- ReqPut item {BitConverter.ToUInt32(body, 1)} into {BitConverter.ToUInt32(body, 9)}");
                    else
                        Console.WriteLine($"  <- ReqPut ({body.Length} bytes)");
                    break;

                case MessageTypeGameMode.SendObjectContents:
                    // Opening a container: the client asks for what is
                    // inside, and the answer is the same kind of list the
                    // loot pile is made of.
                    Console.WriteLine("  <- SendObjectContents");
                    lootOpen = true;
                    SendLoot(ns, ctrl, lootLeft);
                    break;

                case MessageTypeGameMode.SendSpells:
                    Console.WriteLine("  <- SendSpells");
                    SendSpells(ns, ctrl);
                    break;

                case MessageTypeGameMode.SendSkills:
                    Console.WriteLine("  <- SendSkills");
                    SendSkills(ns, ctrl);
                    break;

                case MessageTypeGameMode.SendStats:
                    // The client asks for a named group, and the group is
                    // the byte after the header - answering with the wrong
                    // one is how a window ends up permanently empty.
                    // Condition is the one behind the bars: hit points,
                    // mana, vigor and vanilla's fourth, the chance of
                    // getting tougher. Attributes is the character sheet.
                    // The group is the byte after the PI - on the wire
                    // this message is just 29-01 for Condition, 29-02 for
                    // Attributes. Parsing it with SendStatsMessage does
                    // not work here: body still carries the PI that the
                    // message's own ReadFrom expects to have been handled
                    // already, so it reads the group from the wrong
                    // offset and every request looks like Condition.
                    StatGroup want = body.Length > 1
                        ? (StatGroup)body[1] : StatGroup.Condition;
                    Console.WriteLine($"  <- SendStats {want}");
                    if (want == StatGroup.Attributes) SendAttributes(ns, ctrl);
                    else if (want == StatGroup.Quests) SendQuests(ns, ctrl);
                    else SendConditions(ns, ctrl);
                    break;

                case MessageTypeGameMode.SayTo:
                    // Whatever the player typed. The text is a string the
                    // client composed, and this server has no vocabulary
                    // to answer it with, so it answers with the one line
                    // it does have.
                    Console.WriteLine("  <- SayTo");
                    Say(ns, ctrl, RID_ECHO);
                    break;

                default:
                    // By name where the enum knows one. "game-mode 100,
                    // ignored" has been read as a ping more than once,
                    // and miscounting pings for moves is how an evening
                    // goes to believing the client cannot walk.
                    Console.WriteLine(
                        Enum.IsDefined(typeof(MessageTypeGameMode), (int)pi)
                            ? $"  <- {(MessageTypeGameMode)pi} ({pi}), ignored"
                            : $"  <- game-mode {pi}, ignored");
                    break;
            }
        }
    }

    /// <summary>
    /// The condition bars. Deliberately not all full: a low vigor is what
    /// shows the client blinks a bar under a third, and the render
    /// minimum and maximum are what the fill is measured against for
    /// vigor and tougher-chance rather than the plain maximum.
    /// </summary>
    static void SendConditions(NetworkStream ns, MessageControllerClient ctrl)
    {
        var stats = new Stat[]
        {
            //            num, rid, tag, current, renderMin, renderMax, maximum
            new StatNumeric(1, RID_COND1, 0,  74,  0, 120, 120),   // hit points
            new StatNumeric(2, RID_COND2, 0,  38,  0,  90,  90),   // mana
            new StatNumeric(3, RID_COND3, 0,  22,  0, 100, 100),   // vigor, low enough to blink
            new StatNumeric(4, RID_COND4, 0,  61,  0, 100, 100),   // tougher chance
        };

        Send(ns, ctrl, new StatGroupMessage(StatGroup.Condition, stats));
    }

    /// <summary>
    /// The quest log. A StatList with no skill points is a heading
    /// rather than a quest - UIQuests.cpp draws those in bold and takes
    /// the click away - so this sends two of them, to prove the window
    /// tells them apart.
    /// </summary>
    static void SendQuests(NetworkStream ns, MessageControllerClient ctrl)
    {
        var quests = new Stat[]
        {
            Quest(1, RID_QHEAD1, 0,    0),
            Quest(2, RID_QUEST1, 9001, 25, RID_BOOKBGF),
            Quest(3, RID_QUEST2, 9002, 40, RID_AXEBGF),
            Quest(4, RID_QHEAD2, 0,    0),
            Quest(5, RID_QUEST3, 9003, 10, RID_COINBGF),
        };

        Send(ns, ctrl, new StatGroupMessage(StatGroup.Quests, quests));
    }

    /// <summary>
    /// The last argument is the icon resource. It used to be zero for
    /// every quest, so the quest log had no art to draw and the icon
    /// path could not be tested - and a missing resource looks exactly
    /// like a client that forgot to compose one. Headings keep zero,
    /// because a heading is not a quest and has no icon.
    /// </summary>
    static StatList Quest(byte num, uint nameRid, uint objectId, uint points, uint iconRid = 0)
        => new StatList(num, nameRid, objectId, points, iconRid);

    /// <summary>
    /// Who is online. The window draws each name in
    /// NameColors.GetColorFor(flags), so these deliberately carry
    /// different player types - the colours are the point.
    /// </summary>
    static void SendWho(NetworkStream ns, MessageControllerClient ctrl)
    {
        var who = new[]
        {
            Online(4001, RID_PLAYERNAME, "Tester", ObjectFlags.PlayerType.None, NC_PLAYER),
            Online(4002, RID_ALICE,      "Alice",  ObjectFlags.PlayerType.None, NC_PLAYER),
            Online(4003, RID_BORIS,      "Boris the Outlaw", ObjectFlags.PlayerType.Outlaw, NC_OUTLAW),
            Online(4004, RID_ALICE,      "Cordelia the DM", ObjectFlags.PlayerType.DM, 0x00FFFF),
        };

        Send(ns, ctrl, new PlayersMessage(who));
    }

    /// <summary>
    /// In this flavour NameColors.GetColorFor reads Flags.NameColor -
    /// a colour the server puts in the flags - and ignores the player
    /// type entirely; only vanilla decides the colour from the type.
    /// The type still decides the tooltip, so both are set here and
    /// they are deliberately independent.
    /// </summary>
    static OnlinePlayer Online(uint id, uint nameRid, string name,
                               ObjectFlags.PlayerType type, uint nameColor)
    {
        var f = new ObjectFlags();
        f.Player = type;
        f.NameColor = nameColor;
        return new OnlinePlayer(id, nameRid, name, f);
    }

    /// <summary>
    /// What the player is carrying. One of them is a stack, because the
    /// drop path forks on that: a single thing goes straight out with a
    /// count of zero and a stack asks how many first.
    /// </summary>
    static void SendBag(NetworkStream ns, MessageControllerClient ctrl)
    {
        var bag = new[]
        {
            Carry(8001, RID_AXEBGF,  RID_AXE,   0, true),
            Carry(8002, RID_BOOKBGF, RID_BOOK,  0, false),
            Carry(coinId, RID_COINBGF, RID_COIN, coinNow, false),
        };

        var all = new List<InventoryObject>(bag);
        all.AddRange(takenSoFar);

        // M59_BIGBAG=1 fills the pack to a hundred, which is what
        // Ashton's character carries on the live server. Three items
        // is not a test of an inventory window: the bug that showed up
        // there - five slots visible out of a hundred - could not
        // happen with three.
        if (Environment.GetEnvironmentVariable("M59_BIGBAG") == "1")
            for (uint i = 0; all.Count < 100; i++)
                all.Add(Carry(8200 + i,
                    (i % 3) switch { 0u => RID_AXEBGF, 1u => RID_BOOKBGF, _ => RID_COINBGF },
                    (i % 3) switch { 0u => RID_AXE, 1u => RID_BOOK, _ => RID_COIN },
                    (i % 3) == 2 ? i + 1 : 0, false));

        Send(ns, ctrl, new InventoryMessage(all.ToArray()));
    }

    /// <summary>
    /// inUse is the inventory window's own "this is worn" state; the
    /// EQUIPPED flag is the wire bit the loot and trade lists read to
    /// put " (in use)" after a name. They are different things and the
    /// fixture used to set only the first, so the suffix could not be
    /// tested - and its absence looked exactly like a client that never
    /// wrote it.
    /// </summary>
    const uint OF_EQUIPPED = 0x00008000;

    static InventoryObject Carry(uint id, uint bgfRid, uint nameRid, uint count, bool inUse)
    {
        return new InventoryObject(
            id, count, bgfRid, nameRid, inUse ? OF_EQUIPPED : 0u,
            new LightingInfo(),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new BaseList<SubOverlay>(),
            inUse);
    }

    /// <summary>
    /// The character sheet. Same StatNumeric shape as the condition bars,
    /// and the window fills each bar between ValueRenderMin and
    /// ValueRenderMax rather than against the maximum - so these are
    /// deliberately not all on the same scale, to prove it.
    /// </summary>
    static void SendAttributes(NetworkStream ns, MessageControllerClient ctrl)
    {
        var stats = new Stat[]
        {
            new StatNumeric(1, RID_MIGHT,     0, 42, 0, 100, 100),
            new StatNumeric(2, RID_INTELLECT, 0, 17, 0,  50,  50),   // half scale
            new StatNumeric(3, RID_STAMINA,   0, 68, 0, 100, 100),
            new StatNumeric(4, RID_AGILITY,   0,  9, 0,  20,  20),   // fifth scale
            new StatNumeric(5, RID_MYSTICISM, 0, 55, 0, 100, 100),
            new StatNumeric(6, RID_AIM,       0, 30, 0, 100, 100)
        };

        Send(ns, ctrl, new StatGroupMessage(StatGroup.Attributes, stats));
    }

    /// <summary>
    /// Spells arrive as two things, and the client needs both: the objects
    /// themselves, which is what a cast is resolved against, and a stat
    /// group, which is the list the window shows with a percentage beside
    /// each name.
    /// </summary>
    static void SendSpells(NetworkStream ns, MessageControllerClient ctrl)
    {
        var objects = new[]
        {
            Spell(5001, RID_SPELL1, RID_BOOKBGF),
            Spell(5002, RID_SPELL2, RID_AXEBGF),
        };
        Send(ns, ctrl, new SpellsMessage(objects));

        var stats = new Stat[]
        {
            new StatList(1, RID_SPELL1, 5001, 63, wantStatIcons ? RID_COINBGF : 0),
            new StatList(2, RID_SPELL2, 5002, 21, wantStatIcons ? RID_BOOKBGF : 0),
        };
        Send(ns, ctrl, new StatGroupMessage(StatGroup.Spells, stats));
    }

    static void SendSkills(NetworkStream ns, MessageControllerClient ctrl)
    {
        var objects = new[]
        {
            Skill(5101, RID_SKILL1, RID_COINBGF),
            Skill(5102, RID_SKILL2, RID_BOOKBGF),
        };
        Send(ns, ctrl, new SkillsMessage(objects));

        var stats = new Stat[]
        {
            new StatList(1, RID_SKILL1, 5101, 88, wantStatIcons ? RID_AXEBGF : 0),
            new StatList(2, RID_SKILL2, 5102, 40, wantStatIcons ? RID_COINBGF : 0),
        };
        Send(ns, ctrl, new StatGroupMessage(StatGroup.Skills, stats));
    }

    /// <summary>
    /// The third argument is the overlay file - the art. It used to be
    /// zero, so every spell arrived with no resource and the client had
    /// nothing to draw: the hotbar's spell icons could not be tested at
    /// all, and a null there looks exactly like a client that forgot to
    /// compose one. Real spells have art, so these do too.
    /// </summary>
    static SpellObject Spell(uint id, uint nameRid, uint bgfRid)
    {
        return new SpellObject(
            id, 1, bgfRid, nameRid, 0,
            new LightingInfo(),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new List<SubOverlay>(),
            1, 0);
    }

    static SkillObject Skill(uint id, uint nameRid, uint bgfRid)
    {
        return new SkillObject(
            id, 1, bgfRid, nameRid, 0,
            new LightingInfo(),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new List<SubOverlay>(),
            1, SchoolType.Kraanan, true);
    }

    static void SendLook(NetworkStream ns, MessageControllerClient ctrl)
    {
        // An ObjectBase, not a RoomObject. ObjectInfo reads one back with
        // `new ObjectBase(...)`, so writing the bigger thing - which also
        // carries a position, an angle and a motion animation - leaves the
        // reader mid-object and everything after it garbage. The first
        // attempt did that and the description came back as an empty
        // string with a resource id in the billions.
        ObjectBase rat = Item(2001, RID_RATBGF, RID_RATNAME, 1);

        // Like chat, a description is a string resource id plus whatever
        // it needs substituted - the text itself never goes on the wire.
        var description = new ServerString(
            ChatMessageType.SystemMessage, strings, RID_RATLOOK,
            new List<InlineVariable>(), new List<ChatStyle>());

        // M59_INSCRIBE=1 makes it a thing you can write on: DF_EDITABLE
        // and DF_INSCRIBED together (LookTypeFlags.cs:29-30), which is
        // what the reference tests before offering a writable box and an
        // OK (`UIObjectDetails.cpp:188-201`).
        byte look = Environment.GetEnvironmentVariable("M59_INSCRIBE") == "1" ? (byte)0x3 : (byte)0;
        var inscription = new ServerString(
            ChatMessageType.SystemMessage, strings, RID_RATLOOK,
            new List<InlineVariable>(), new List<ChatStyle>());

        var info = new ObjectInfo(rat, new LookTypeFlags(look), description,
                                  look != 0 ? inscription
                                            : new ServerString(ChatMessageType.SystemMessage));
        Send(ns, ctrl, new LookMessage(info, strings));
    }

    /// <summary>
    /// A spell's description. Everything on that window is a
    /// ServerString the server words itself - school, level, mana and
    /// vigor included - so the client composes none of it.
    /// </summary>
    static void SendLookSpell(NetworkStream ns, MessageControllerClient ctrl)
    {
        ObjectBase spell = Item(5001, RID_BOOKBGF, RID_SPELL1, 0);

        var info = new SpellInfo(
            spell,
            Line(RID_SPELLDESC),
            Line(RID_SCHOOL),
            Line(RID_LEVEL),
            Line(RID_MANA),
            Line(RID_VIGOR));

        Send(ns, ctrl, new LookSpellMessage(info, strings));
    }

    static ServerString Line(uint rid)
        => new ServerString(ChatMessageType.SystemMessage, strings, rid,
                            new List<InlineVariable>(), new List<ChatStyle>());

    /// <summary>
    /// Messages to wait before offering a stat change, or zero for
    /// never. Set M59_STATCHANGE=1 to turn it on. Reset per client the
    /// way stopAfter is: this server is reconnected to between runs,
    /// and a counter that only counts once fires for the first run
    /// only - which is what the first attempt did.
    /// </summary>
    static readonly bool wantStatChange =
        Environment.GetEnvironmentVariable("M59_STATCHANGE") == "1";
    static int statChangeAfter;

    static int lootLeft = 3;
    static bool lootOpen;

    /// <summary>
    /// What has been picked up this session. The pack used to be three
    /// fixed items whatever you did, so taking something off the floor
    /// sent an InventoryAdd and then the next ReqInventory wiped it -
    /// which looks exactly like a client that drops added items.
    /// </summary>
    static readonly List<InventoryObject> takenSoFar = new List<InventoryObject>();

    /// <summary>
    /// The contents of something on the floor. The client puts its loot
    /// window up when this arrives and takes it down when the list is
    /// empty, which is the server's decision rather than the view's.
    /// </summary>
    static void SendLoot(NetworkStream ns, MessageControllerClient ctrl, int count)
    {
        var all = new[]
        {
            Item(3001, RID_COINBGF, RID_COIN, 17),
            Item(3002, RID_BOOKBGF, RID_BOOK, 1),
            Item(3003, RID_AXEBGF,  RID_AXE,  1),
        };

        var left = new List<ObjectBase>();
        for (int i = 0; i < Math.Clamp(count, 0, all.Length); i++) left.Add(all[i]);

        // The id has to be the container's, not any old object: putting
        // something in looks that id up among the room objects and
        // refuses anything not flagged a container.
        Send(ns, ctrl, new ObjectContentsMessage(new ObjectID(3101, 0), left.ToArray()));
    }

    static ObjectBase Item(uint id, uint bgfRid, uint nameRid, uint count)
    {
        return new ObjectBase(
            id, count, bgfRid, nameRid, 0,
            new LightingInfo(),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new List<SubOverlay>());
    }

    /// <summary>
    /// What the merchant sells. BuyList carries the trade partner and a
    /// TradeOfferObject per line - an ordinary object plus a price - and
    /// the client puts up its buy window on the strength of it.
    /// </summary>
    static void SendStock(NetworkStream ns, MessageControllerClient ctrl)
    {
        var stock = new[]
        {
            Offer(7001, RID_AXEBGF,  RID_AXE,  0, 1200),
            Offer(7002, RID_BOOKBGF, RID_BOOK, 0, 75),
            // A stackable line: the window totals count times price for
            // these and price once for everything else.
            Offer(7003, RID_COINBGF, RID_COIN, 10, 12),
        };

        Send(ns, ctrl, new BuyListMessage(
            Item(3103, RID_PLAYERBGF, RID_ALICE, 0), stock));
    }

    /// <summary>
    /// What an NPC offers. QuestUIList carries the giver and a
    /// QuestObjectInfo per quest - an object plus a description and a
    /// requirements string - and the client raises its quest window on
    /// the strength of it.
    ///
    /// The quest kind lives in ObjectFlags.Player, which is a byte of
    /// its own on the wire - not part of the flags integer the
    /// constructor takes. Passing 8, 9 or 10 as that integer sets
    /// nothing: the first try did, and three quests came back
    /// colourless and in the order they were sent. It has to be
    /// assigned to the property after the object exists.
    ///
    /// The data layer sorts active, then valid, then the rest, so all
    /// three go out shuffled here on purpose - a list that comes back
    /// active-first proves the sort rather than the sending order.
    /// </summary>
    /// <summary>
    /// The mailbox. Each mail goes as its own Mail message and the
    /// client files it, asks for the server's copy to be deleted, and
    /// renumbers it locally - so a real server would only ever send
    /// what has not been collected. This one sends the same two every
    /// time, which is what makes the renumbering visible.
    ///
    /// Sending nothing at all is not how "no mail" is said: the client
    /// looks for a mail with no number, no sender, no timestamp and no
    /// recipients (Mail.IsMessageForNoMessages) and ignores it.
    /// </summary>
    static void SendMail(NetworkStream ns, MessageControllerClient ctrl)
    {
        Send(ns, ctrl, new MailMessage(Letter(1, "Alice", RID_MAIL1, new List<string> { "Tester" })));
        Send(ns, ctrl, new MailMessage(Letter(2, "Boris the Outlaw", RID_MAIL2,
                                              new List<string> { "Tester", "Alice" })));
    }

    static Mail Letter(uint num, string from, uint bodyRid, List<string> to)
    {
        // The title argument is ignored on the wire - the client parses
        // it back out of the body - so it is passed for the sake of the
        // model rather than for the message.
        //
        // The timestamp is not a unix one. MeridianDate.ToDateTime, which
        // is what the mail window puts through, reads it as seconds since
        // 1599982030 - 13 September 2020 - so a unix timestamp sent here
        // comes out in 2077. The first attempt did exactly that. The
        // server's epoch is the library's to declare, so this matches it
        // rather than arguing with it.
        uint stamp = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1599982030L);

        return new Mail(num, from, stamp, to, Line(bodyRid), "", true);
    }

    /// <summary>
    /// Who these names are. One id per name asked, in the order asked,
    /// and zero for a name there is no player for - which is the whole
    /// of the client's check before it will send a mail. "Alice" and
    /// "Boris the Outlaw" are known here; anything else comes back
    /// zero, so the error path is reachable by typing a name.
    /// </summary>
    static void SendLookup(NetworkStream ns, MessageControllerClient ctrl, byte[] body)
    {
        // Read by hand rather than through ReqLookupNamesMessage: that
        // constructor parses a whole TCP message, header included, and
        // what arrives here is the body with the PI byte on the front.
        // Handing it the body throws inside the header reader, which is
        // what the first attempt did.
        string[] names = Names(body);
        var ids = new ObjectID[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            string n = (names[i] ?? "").Trim();
            uint id =
                string.Equals(n, "Alice", StringComparison.OrdinalIgnoreCase) ? 4001u :
                string.Equals(n, "Boris the Outlaw", StringComparison.OrdinalIgnoreCase) ? 4002u : 0u;

            Console.WriteLine($"     {n} -> {id}");
            ids[i] = new ObjectID(id, 0);
        }

        Send(ns, ctrl, new LookupNamesMessage(ids));
    }

    static string[] Names(byte[] body)
    {
        int cursor = 1;                                  // past the PI
        ushort count = BitConverter.ToUInt16(body, cursor);
        cursor += 2;

        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            ushort len = BitConverter.ToUInt16(body, cursor);
            cursor += 2;
            names[i] = Meridian59.Common.Util.Encoding.GetString(body, cursor, len);
            cursor += len;
        }
        return names;
    }

    /// <summary>
    /// Your guild. Everything the members tab can do is decided by the
    /// flag word, so this hands over a guildmaster's set - exile, vote,
    /// set rank, abdicate, disband, abandon - which is the only way to
    /// see the row controls enabled at all. A member's set would leave
    /// every one of them greyed, which is correct and untestable.
    ///
    /// The ranks are the guild's own strings, five per gender, and a
    /// member's gender picks the column - so Alice reads as a Sister
    /// where Boris reads as a Brother at the same rank.
    /// </summary>
    static void SendGuild(NetworkStream ns, MessageControllerClient ctrl)
    {
        const uint GUILDMASTER =
            0x00000002 |   // exile
            0x00000004 |   // renounce
            0x00000020 |   // vote
            0x00000040 |   // abdicate
            0x00001000 |   // set rank
            0x00002000 |   // disband
            0x00004000;    // abandon hall

        var members = new[]
        {
            new GuildMemberEntry(1001, 0, "Tester", 5, Gender.Male),
            new GuildMemberEntry(4001, 0, "Alice", 3, Gender.Female),
            new GuildMemberEntry(4002, 0, "Boris the Outlaw", 1, Gender.Male),
        };

        var info = new GuildInfo(
            "The Quiet Hand",
            1,                       // has a hall, so the password box is there
            "rats",
            new GuildFlags(GUILDMASTER),
            new ObjectID(9001, 0),
            "Novice",  "Novice",
            "Brother", "Sister",
            "Elder",   "Matron",
            "Warden",  "Warden",
            "Master",  "Mistress",
            new ObjectID(4001, 0),   // Alice is the supported member
            members);

        Send(ns, ctrl, new UserCommandMessage(new UserCommandGuildInfo(info), strings));
    }

    /// <summary>
    /// The other guilds and where everyone stands.
    ///
    /// One of each case so the diplomacy list has something to show:
    /// a guild you have allied, one you have declared on, one that has
    /// declared on you while you stay neutral, one that has allied you
    /// without an answer, and one nobody has said anything about. Your
    /// own guild is in the list too, because the reference expects it
    /// and greys its row out (`UIGuild.cpp:545`).
    /// </summary>
    static void SendGuildList(NetworkStream ns, MessageControllerClient ctrl)
    {
        var guilds = new[]
        {
            new GuildEntry("The Quiet Hand", 9001),
            new GuildEntry("The Gilded Rose", 9002),
            new GuildEntry("Ashen Company", 9003),
            new GuildEntry("Wardens of Kocatan", 9004),
            new GuildEntry("The Long Road", 9005),
        };

        var info = new DiplomacyInfo(
            guilds,
            new[] { new ObjectID(9002, 0) },   // you have allied the Rose
            new[] { new ObjectID(9003, 0) },   // and declared on Ashen
            new[] { new ObjectID(9004, 0) },   // the Wardens have allied you
            new[] { new ObjectID(9003, 0) });  // Ashen has declared back

        Send(ns, ctrl, new UserCommandMessage(new UserCommandGuildGuildList(info), strings));
        Console.WriteLine($"  -> GuildGuildList ({guilds.Length} guilds)");
    }

    /// <summary>
    /// A stat change on offer.
    ///
    /// StatChangeInfo's OrigLevel fields have protected setters - only
    /// its own ReadFrom fills them, and it sets each school's current
    /// level to the level you arrived with as it goes. So the fixture
    /// is built as the thirteen bytes the model reads rather than by
    /// assignment: six attributes, then seven school levels.
    ///
    /// The numbers are chosen to make the rules visible. The six come
    /// to 195 of the 220 allowed, so there are 25 points spare to push
    /// around; and three schools are studied, which puts a floor under
    /// intellect that only giving those levels up will lower.
    /// </summary>
    static void SendStatChange(NetworkStream ns, MessageControllerClient ctrl)
    {
        byte[] raw =
        {
            40, 35, 30, 30, 30, 30,   // might, intellect, stamina, agility, mysticism, aim
            3, 0, 2, 0, 0, 0, 4,      // sha, qor, kraanan, faren, riija, jala, weaponcraft
        };

        Send(ns, ctrl, new ReqStatChangeMessage(new StatChangeInfo(raw)));
    }

    /// <summary>
    /// Everything a new character can be made out of: the hair and skin
    /// colours, the face art per gender, and the spells and skills on
    /// offer with their costs.
    ///
    /// The two school spells are the point of the fixture. Qor and
    /// Shal'ille refuse each other - SelectSpell answers
    /// AlreadyHaveQorError or AlreadyHaveShalilleError rather than
    /// failing quietly - and there is no way to see that rule work
    /// without one of each on the list.
    /// </summary>
    static void SendCharInfo(NetworkStream ns, MessageControllerClient ctrl)
    {
        var info = new CharCreationInfo
        {
            HairColors = new byte[] { 0, 1, 2, 3 },
            SkinColors = new byte[] { 0, 1, 2 },

            MaleSkullID = new ResourceIDBGF(RID_HEADBGF),
            MaleHairIDs = Parts(2),
            MaleEyeIDs = Parts(2),
            MaleNoseIDs = Parts(2),
            MaleMouthIDs = Parts(2),

            FemaleSkullID = new ResourceIDBGF(RID_HEADBGF),
            FemaleHairIDs = Parts(3),
            FemaleEyeIDs = Parts(2),
            FemaleNoseIDs = Parts(2),
            FemaleMouthIDs = Parts(2),
        };

        info.Spells.Add(new AvatarCreatorSpellObject(101, RID_SPELLA, RID_SPELLDESC, 1, SchoolType.Riija));
        info.Spells.Add(new AvatarCreatorSpellObject(102, RID_SPELLB, RID_SPELLDESC, 2, SchoolType.Shalille));
        info.Spells.Add(new AvatarCreatorSpellObject(103, RID_SPELLC, RID_SPELLDESC, 2, SchoolType.Qor));

        info.Skills.Add(new AvatarCreatorSkillObject(201, RID_SKILLA, RID_SPELLDESC, 1, SchoolType.Kraanan));
        info.Skills.Add(new AvatarCreatorSkillObject(202, RID_SKILLB, RID_SPELLDESC, 1, SchoolType.Shalille));

        Send(ns, ctrl, new CharInfoMessage(info));
    }

    static ResourceIDBGF[] Parts(int count)
    {
        var a = new ResourceIDBGF[count];
        for (int i = 0; i < count; i++) a[i] = new ResourceIDBGF(RID_HAIRBGF);
        return a;
    }

    static readonly bool wantNews =
        Environment.GetEnvironmentVariable("M59_NEWS") == "1";

    static int posted, deleted;

    /// <summary>
    /// The board itself. NewsGlobeObject is what the window is titled
    /// after, and the headline is the line under it.
    /// </summary>
    static void SendNewsGroup(NetworkStream ns, MessageControllerClient ctrl)
    {
        var news = new NewsGroup(
            7, 0,
            Item(3104, RID_BOOKBGF, RID_GLOBE, 0),
            RID_HEADLINE,
            "");                                 // resolved client-side from the id

        Send(ns, ctrl, new LookNewsGroupMessage(news));
    }

    /// <summary>
    /// The headers on the board. A post adds one and a delete takes one
    /// away, so the client's clear-then-reask actually shows a change
    /// rather than the same three lines every time.
    /// </summary>
    static void SendArticles(NetworkStream ns, MessageControllerClient ctrl)
    {
        // ArticleHead does not round-trip its own timestamp.
        // WriteTo stores seconds since MERIDIANZERO and ReadFrom adds a
        // further 1599982030 on the way back, so a date written here
        // comes out fifty years late - which is what the first attempt
        // showed, every article dated 2077. The offset is taken off
        // going in so the client displays what was meant.
        DateTime now = DateTime.Now.AddSeconds(-1599982030d);

        var heads = new List<ArticleHead>
        {
            new ArticleHead(1, now.AddDays(-3), "Alice", "The cellar, again"),
            new ArticleHead(2, now.AddDays(-1), "Boris the Outlaw", "Re: The cellar, again"),
            new ArticleHead(3, now, "Tester", "Has anyone seen my axe"),
        };

        for (int i = 0; i < deleted && heads.Count > 0; i++) heads.RemoveAt(heads.Count - 1);
        for (int i = 0; i < posted; i++)
            heads.Add(new ArticleHead((uint)(100 + i), now, "Tester", "A posting of mine"));

        Send(ns, ctrl, new ArticlesMessage(heads.ToArray()));
    }

    /// <summary>
    /// An article's text. Unlike almost everything else on this server
    /// it is a plain string on the wire, not a resource id - the
    /// article was typed by a player, so there is nothing to look it up
    /// in.
    /// </summary>
    static string Body(uint number)
    {
        switch (number)
        {
            case 1: return "Twenty of them, and the innkeeper counting. Bring a lantern.";
            case 2: return "I brought a lantern. I did not bring twenty arrows.";
            case 3: return "Nerudite. Notched. Answers to nothing. Reward offered.";
            default: return "(nothing here)";
        }
    }

    static void SendNPCQuests(NetworkStream ns, MessageControllerClient ctrl)
    {
        var quests = new[]
        {
            Quest(8002, RID_QUEST2, ObjectFlags.PlayerType.QuestValid,   RID_QDESC2, RID_QREQ2),
            Quest(8003, RID_QUEST3, ObjectFlags.PlayerType.QuestInvalid, RID_QDESC3, RID_QREQ3),
            Quest(8001, RID_QUEST1, ObjectFlags.PlayerType.QuestActive,  RID_QDESC1, RID_QREQ1),
        };

        Send(ns, ctrl, new QuestUIListMessage(
            Item(3103, RID_PLAYERBGF, RID_ALICE, 0), quests, strings));
    }

    static QuestObjectInfo Quest(uint id, uint nameRid, ObjectFlags.PlayerType kind,
                                 uint descRid, uint reqRid)
    {
        ObjectBase obj = Item(id, RID_BOOKBGF, nameRid, 0);
        obj.Flags.Player = kind;

        return new QuestObjectInfo(obj, Line(descRid), Line(reqRid));
    }

    static TradeOfferObject Offer(uint id, uint bgfRid, uint nameRid, uint count, uint price)
    {
        return new TradeOfferObject(
            id, count, bgfRid, nameRid, 0,
            new LightingInfo(),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new List<SubOverlay>(),
            price);
    }

    static void SendCharacters(NetworkStream ns, MessageControllerClient ctrl)
    {
        // One character and one empty slot, which is what a real
        // account looks like: the empty slot is how the creation
        // wizard is reached, and a client that only lists real
        // characters can never get to it.
        var chars = new List<CharSelectItem>
        {
            new CharSelectItem(1001, 1, "Tester", 0),
            new CharSelectItem(0, 0, "", 1),
        };
        var welcome = new WelcomeInfo(chars, new List<CharSelectAd>(), "A fake server. Nothing here is real.");
        Send(ns, ctrl, new CharactersMessage(welcome));
    }

    /// <summary>
    /// Puts the client in the room: where it is, then what is in it.
    /// </summary>
    /// <summary>
    /// A point that is actually inside a room, in the server's own
    /// units. Hard-coding coordinates works for the room this fixture
    /// grew up in and nowhere else: dropped into another .roo they can
    /// land in rock, and a client standing in rock is a bug report
    /// about the client. So the room is read, the roomiest leaf of its
    /// BSP tree is taken, and its centre converted - kod = room/16 + 64,
    /// the same conversion BaseClient uses in reverse.
    /// </summary>
    static (ushort X, ushort Y) Spawn(string file, ushort fallbackX, ushort fallbackY)
    {
        try
        {
            var roo = new RooFile(Path.Combine(dir, file));
            RooSubSector best = null;
            double bestSpan = 0;

            foreach (RooSubSector leaf in roo.BSPTreeLeaves)
            {
                if (leaf?.Vertices == null || leaf.Vertices.Count < 3) continue;

                double minX = leaf.Vertices[0].X, maxX = minX;
                double minY = leaf.Vertices[0].Y, maxY = minY;
                foreach (V2 v in leaf.Vertices)
                {
                    if (v.X < minX) minX = v.X;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Y > maxY) maxY = v.Y;
                }

                double span = (maxX - minX) * (maxY - minY);
                if (span > bestSpan) { bestSpan = span; best = leaf; }
            }

            if (best == null) return (fallbackX, fallbackY);

            double sx = 0, sy = 0;
            foreach (V2 v in best.Vertices) { sx += v.X; sy += v.Y; }
            sx /= best.Vertices.Count;
            sy /= best.Vertices.Count;

            return ((ushort)(sx / 16f + 64f), (ushort)(sy / 16f + 64f));
        }
        catch (Exception e)
        {
            Console.WriteLine($"  !! no spawn point in {file}: {e.GetType().Name}, using the fallback");
            return (fallbackX, fallbackY);
        }
    }

    /// <summary>
    /// The room a Go leads to: the same avatar, one rat, nothing else.
    /// </summary>
    static void EnterSecondRoom(NetworkStream ns, MessageControllerClient ctrl, uint avatarId)
    {
        // M59_BADROOM names a file the client does not have; there is
        // nothing to read a spawn from then, so the old fixed one is used.
        (ushort sx, ushort sy, float sa) = SpawnFor(room2, EnvStr("M59_SPAWN2"), !badRoom);
        okX = sx; okY = sy; okAngle = 0;

        var info = new RoomInfo(
            AvatarID: avatarId,
            AvatarOverlayRID: RID_PLAYERBGF,
            AvatarNameRID: RID_PLAYERNAME,
            RoomID: badRoom ? 3u : 2u,
            RoomFileRID: badRoom ? RID_ROOMFILE3 : RID_ROOMFILE2,
            RoomNameRID: badRoom ? RID_ROOMNAME3 : RID_ROOMNAME2,
            RoomSecurity: 0,
            AmbientLight: ambient,
            AvatarLight: 0,
            BackgroundFileRID: 0,
            WadingSoundFileRID: 0,
            Flags: 0,
            Depth1: 0, Depth2: 0, Depth3: 0);

        Send(ns, ctrl, new PlayerMessage(info));

        // Everything after the Player message can be held back by
        // M59_ROOMGAP_MS; with the switch off this runs at once.
        AfterGap(() =>
        {
            var objects = new[]
            {
                Obj(avatarId, RID_PLAYERBGF, RID_PLAYERNAME, sx, sy, sa, OF_PLAYER),
                // A grid square away, not four units: the README already
                // records what four looks like - one duskrat filling the
                // screen - and the first cut of this room reproduced it,
                // brown blocks and all.
                Obj(2004, RID_RATBGF, RID_RATNAME, (ushort)(sx + 64), sy, 2f,
                    OF_ATTACKABLE, MM_MONSTER),
            };
            Send(ns, ctrl, new RoomContentsMessage(new ObjectID(2, 0), objects));
            Console.WriteLine($"  -> room {(badRoom ? "zzmissing.roo" : room2)} at {sx},{sy} with {objects.Length} objects");

            // Nothing carried over from the other room.
            lootLeft = 0;
            lootOpen = false;
            takenSoFar.Clear();

            // M59_ROOM2BUFFS: the room's own enchantments, re-sent on
            // every entry as the library expects (see the statics).
            if (wantRoom2Buffs)
                Send(ns, ctrl, new AddEnchantmentMessage(BuffType.RoomBuff,
                    Item(6111, RID_COINBGF, RID_ROOMBUFF2, 1)));
        });
    }

    /// <summary>
    /// The count-driven door/wall/texture switches: opens once
    /// M59_GEOM_AFTER messages after a room entry, closes M59_GEOM_PERIOD
    /// later. Off (geomCount stays -1) unless a target was named.
    /// </summary>
    static void Geometry(NetworkStream ns, MessageControllerClient ctrl)
    {
        if (geomCount < 0) return;
        geomCount++;
        if (geomCount == geomAfter) GeometryStep(ns, ctrl, true);
        else if (geomPeriod > 0 && geomCount == geomAfter + geomPeriod)
        {
            GeometryStep(ns, ctrl, false);
            geomCount = -1;
        }
    }

    static void GeometryStep(NetworkStream ns, MessageControllerClient ctrl, bool open)
    {
        string step = open ? "open" : "close";

        if (sectorId >= 0 && (open || sectorBack >= 0))
        {
            ushort h = (ushort)(open ? sectorTo : sectorBack);
            Console.WriteLine($"  -> SectorMove ({step}) sector {sectorId} {(sectorFloor ? "floor" : "ceiling")} to {h} speed {sectorSpeed}");
            Send(ns, ctrl, new SectorMoveMessage(new SectorMove(
                sectorFloor ? AnimationType.FLOORLIFT : AnimationType.CEILINGLIFT,
                (ushort)sectorId, h, (byte)sectorSpeed)));
        }

        if (sectorChangeId >= 0)
        {
            // 4 is the "leave it" value for both fields; closing puts back
            // whatever opening changed.
            int depth = open ? sectorDepth : (sectorDepth == 4 ? 4 : sectorDepthBack);
            int scroll = open ? sectorScroll : (sectorScroll == 4 ? 4 : sectorScrollBack);
            Console.WriteLine($"  -> SectorChange ({step}) sector {sectorChangeId} depth {depth} scroll {scroll}");
            Send(ns, ctrl, new SectorChangeMessage(new SectorChange(
                (ushort)sectorChangeId, (RooSectorFlags.DepthType)depth, (TextureScrollSpeed)scroll)));
        }

        if (wallId >= 0)
        {
            string[] g = wallGroups.Split(',');
            ushort lo = ushort.Parse(g[0]);
            ushort hi = g.Length > 1 ? ushort.Parse(g[1]) : lo;
            ushort fin = g.Length > 2 ? ushort.Parse(g[2]) : hi;

            Animation anim =
                !open || wallAnim == "none" ? new AnimationNone(lo)
                : wallAnim == "once" ? (Animation)new AnimationOnce((uint)wallPeriod, lo, hi, fin)
                : new AnimationCycle((uint)wallPeriod, lo, hi);

            RoomAnimationAction act = RoomAnimationAction.RA_NONE;
            if (wallAction == "passable")
                act = open ? RoomAnimationAction.RA_PASSABLE_END : RoomAnimationAction.RA_IMPASSABLE_END;
            else if (wallAction == "impassable")
                act = open ? RoomAnimationAction.RA_IMPASSABLE_END : RoomAnimationAction.RA_PASSABLE_END;

            Console.WriteLine($"  -> WallAnimate ({step}) sidedef {wallId} {anim.AnimationType} action {act}");
            Send(ns, ctrl, new WallAnimateMessage((ushort)wallId,
                new WallAnimationChange((ushort)wallId, anim, act)));
        }

        if (texId >= 0 && (open || texBack >= 0))
        {
            byte flags = texPart switch
            {
                "above" => 0x01, "below" => 0x04, "floor" => 0x08, "ceiling" => 0x10,
                _ => (byte)0x02,
            };
            ushort tex = (ushort)(open ? texTo : texBack);
            Console.WriteLine($"  -> ChangeTexture ({step}) id {texId} {texPart} to {tex}");
            Send(ns, ctrl, new ChangeTextureMessage(new TextureChangeInfo((ushort)texId, tex, flags)));
        }
    }

    /// <summary>
    /// M59_STACK: the coin stack changes once, M59_STACK_AFTER messages
    /// into the session. See the statics for what the real server does.
    /// </summary>
    static void StackChange(NetworkStream ns, MessageControllerClient ctrl)
    {
        if (stackCount < 0) return;
        if (++stackCount != stackAfter) return;
        stackCount = -1;

        if (stackMode == "shrink")
        {
            coinNow = (uint)stackTo;
            // The inventory form of the object: its id carries the count
            // (ObjectID sets the MULTIOBJ flag when count > 0), then icon,
            // name, flags, lighting, animation and overlays - the same
            // fields InventoryAdd carries - then the motion animation and
            // overlays `SendMoveAnimation`/`SendMoveOverlays` append.
            var upd = new ObjectUpdate(
                coinId, coinNow, RID_COINBGF, RID_COIN, 0,
                new LightingInfo(),
                AnimationType.NONE, 0, 0, new AnimationNone(), new List<SubOverlay>(),
                AnimationType.NONE, 0, 0, new AnimationNone(), new List<SubOverlay>());
            Console.WriteLine($"  -> Change: stack {coinId} is now {coinNow}");
            Send(ns, ctrl, new ChangeMessage(upd));
        }
        else
        {
            Console.WriteLine($"  -> replacing stack {coinId} with a new one of {stackTo}");
            Send(ns, ctrl, new InventoryRemoveMessage(new ObjectID(coinId)));
            coinId = 8004;
            coinNow = (uint)stackTo;
            Send(ns, ctrl, new InventoryAddMessage(
                Carry(coinId, RID_COINBGF, RID_COIN, coinNow, false)));
        }
    }

    /// <summary>
    /// M59_YANK: decides a ReqMove. The body is [PI][Y 2][X 2][mode 1]
    /// [map 4][angle 2] (`ReqMoveMessage.WriteTo`, the library's flavour
    /// carries the angle). Every nth is refused and answered with the
    /// avatar's last accepted position.
    /// </summary>
    static void ServerVerdict(NetworkStream ns, MessageControllerClient ctrl, byte[] body)
    {
        if (body.Length < 12) { Console.WriteLine($"  <- ReqMove ({body.Length} bytes) unparsed"); return; }

        ushort y = BitConverter.ToUInt16(body, 1);
        ushort x = BitConverter.ToUInt16(body, 3);
        ushort ang = BitConverter.ToUInt16(body, 10);
        reqMoves++;

        if (reqMoves % yankEvery != 0)
        {
            okX = x; okY = y; okAngle = ang;
            Console.WriteLine($"  <- ReqMove #{reqMoves} to {x},{y} accepted");
            return;
        }

        ushort tx = okX, ty = okY;
        if (!string.IsNullOrEmpty(yankTo))
        {
            string[] f = yankTo.Split(',');
            if (f.Length >= 2 && ushort.TryParse(f[0], out ushort ux) && ushort.TryParse(f[1], out ushort uy))
            { tx = ux; ty = uy; }
        }
        Console.WriteLine($"  <- ReqMove #{reqMoves} to {x},{y} REFUSED, avatar goes back to {tx},{ty}");
        Send(ns, ctrl, new MoveMessage(1001, tx, ty, (MovementSpeed)yankSpeed, okAngle));
        Send(ns, ctrl, new TurnMessage(1001, okAngle));
    }

    /// <summary>
    /// A skill's description (LookSkill, PI 192), the skill's twin of
    /// SendLookSpell: object, then school, level and description as
    /// server strings (`SkillInfo.WriteTo`: school, level, message).
    /// </summary>
    static void SendLookSkill(NetworkStream ns, MessageControllerClient ctrl, uint id)
    {
        ObjectBase skill = id == 5101
            ? Item(5101, RID_COINBGF, RID_SKILL1, 0)
            : Item(5102, RID_BOOKBGF, RID_SKILL2, 0);

        var info = new SkillInfo(
            skill,
            Line(RID_SKILLDESC),
            Line(RID_SKILLSCHOOL),
            Line(RID_SKILLLEVEL),
            Line(RID_MANA),
            Line(RID_VIGOR));

        Send(ns, ctrl, new LookSkillMessage(info, strings));
    }

    /// <summary>
    /// Holds the rest of a room change back by M59_ROOMGAP_MS, on a Go
    /// only: the first entry has nothing to be in the gap OF.
    /// </summary>
    static void AfterGap(Action rest)
    {
        if (roomGapMs <= 0 || roomsEntered <= 1) { rest(); return; }

        Console.WriteLine($"  .. holding RoomContents for {roomGapMs} ms");
        var t = new Timer(_ =>
        {
            try { rest(); }
            catch (Exception e) { Console.WriteLine($"  !! held room contents not sent: {e.GetType().Name}"); }
        }, null, roomGapMs, Timeout.Infinite);
        lock (gapTimers) gapTimers.Add(t);       // keep it alive
    }

    /// <summary>
    /// Where the avatar starts. M59_SPAWN / M59_SPAWN2 (x,y[,angle]) win;
    /// otherwise the roomiest BSP leaf of the room (see Spawn), which for
    /// barinn is 752,672 - the coordinate this fixture used by hand for
    /// room 1 before it could be pointed at another .roo.
    /// </summary>
    static (ushort X, ushort Y, float Angle) SpawnFor(string file, string env, bool compute)
    {
        if (!string.IsNullOrEmpty(env))
        {
            string[] f = env.Split(',');
            if (f.Length >= 2 && ushort.TryParse(f[0], out ushort ex) && ushort.TryParse(f[1], out ushort ey))
            {
                float ea = f.Length > 2 && float.TryParse(f[2], out float a) ? a : 0f;
                Console.WriteLine($"  (spawn {ex},{ey} angle {ea} from the environment)");
                return (ex, ey, ea);
            }
        }
        (ushort x, ushort y) = compute ? Spawn(file, 752, 672) : ((ushort)752, (ushort)672);
        return (x, y, 0f);
    }

    static void EnterRoom(NetworkStream ns, MessageControllerClient ctrl)
    {
        const uint avatarId = 1001;
        roomsEntered++;
        if (sectorId >= 0 || sectorChangeId >= 0 || wallId >= 0 || texId >= 0)
            geomCount = 0;       // the opening comes M59_GEOM_AFTER messages from now

        // The second room is deliberately barer than the first: a room
        // change that only moved the walls could be a redraw, while one
        // that also empties the object list is a room change.
        if (inRoom2) { EnterSecondRoom(ns, ctrl, avatarId); return; }

        var info = new RoomInfo(
            AvatarID: avatarId,
            AvatarOverlayRID: RID_PLAYERBGF,
            AvatarNameRID: RID_PLAYERNAME,
            RoomID: 1,
            RoomFileRID: RID_ROOMFILE,
            RoomNameRID: RID_ROOMNAME,
            RoomSecurity: 0,
            // A dim-ish room, so the ambient actually does something
            // visible: the client takes the larger of these two over 255
            // and shades everything by it.
            AmbientLight: ambient,
            AvatarLight: 0,
            BackgroundFileRID: 0,
            WadingSoundFileRID: 0,
            Flags: 0,
            Depth1: 0, Depth2: 0, Depth3: 0);

        Send(ns, ctrl, new PlayerMessage(info));

        // Room 1's spawn used to be 752,672, barinn's roomiest leaf, written
        // in by hand - so pointing the fixture at any other .roo stood the
        // avatar in rock. Every object below is now placed relative to it
        // (the offsets are what the absolute coordinates were, minus
        // 752,672), so barinn is unchanged and another room is usable.
        (ushort sx, ushort sy, float sa) = SpawnFor(room, EnvStr("M59_SPAWN"), true);
        okX = sx; okY = sy; okAngle = 0;

        // Everything from here is what the client gets AFTER Player, and
        // M59_ROOMGAP_MS can hold all of it back (a Go only).
        AfterGap(() =>
        {
        // The avatar itself plus something to look at. Positions are in
        // the server's own units, where one unit is sixteen room units
        // and the origin is 64: kod = room/16 + 64. A grid square is 1024
        // room units, so 64 of these. Put the first pair a couple of grid
        // squares off rather than a few units, which stands them on the
        // camera's nose - the first run of this filled the screen with
        // one duskrat.
        var objects = new[]
        {
            Obj(avatarId, RID_PLAYERBGF, RID_PLAYERNAME, sx, sy, sa, OF_PLAYER),
            // OF_ATTACKABLE, so the minimap has something to colour: the
            // game only puts a dot on things you could fight, players and
            // guildmates. An ordinary item on the floor gets none.
            Obj(2001, RID_RATBGF, RID_RATNAME, sx + 64, sy + 0, 1f, OF_ATTACKABLE, MM_MONSTER),
            // Two of the three rats are here to exercise the drawing
            // types RemoteNode2D switches materials on: one half
            // translucent, one a shadowform. Like PlayerType, Drawing is
            // its own byte on the wire rather than a bit in the flags
            // integer, so it is set on the object afterwards.
            Drawn(Obj(2002, RID_RATBGF, RID_RATNAME, sx + 96, sy + 16, 3f, OF_ATTACKABLE, MM_MONSTER),
                  ObjectFlags.DrawingType.Translucent50),
            Drawn(Obj(2003, RID_RATBGF, RID_RATNAME, sx + 128, sy - 16, 2f, OF_ATTACKABLE, MM_MONSTER),
                  ObjectFlags.DrawingType.Black),
            // Something to open and something to pick up, so the Activate
            // and Loot actions have a target: the library looks for a
            // container or an activatable object near you for the first,
            // and fills its loot list from gettable ones for the second.
            Obj(3101, RID_BOOKBGF, RID_BOOK, sx + 16, sy + 16, 0f, OF_CONTAINER | OF_DISPLAY_NAME),
            Obj(3102, RID_COINBGF, RID_COIN, sx - 16, sy + 16, 0f, OF_GETTABLE | OF_DISPLAY_NAME),
            // Somebody to buy from: AvatarAction.Buy looks for a nearby
            // object flagged OF_BUYABLE and asks it for a stock list.
            Obj(3104, RID_BOOKBGF, RID_GLOBE, sx + 28, sy - 16, 0f, OF_DISPLAY_NAME),
            Obj(3103, RID_PLAYERBGF, RID_ALICE, sx + 48, sy + 0, 3f,
                OF_BUYABLE | OF_DISPLAY_NAME, MM_PLAYER, NC_PLAYER),

            // Two other players, so the name labels have something to
            // label. This server's flavour - Server 104's - draws a name
            // when OF_DISPLAY_NAME is set and takes the colour from a
            // separate field the server sends, rather than working it out
            // from a player type. MM_PLAYER is what puts them on the map.
            Obj(4001, RID_PLAYERBGF, RID_ALICE, sx + 48, sy + 32, 3f,
                OF_PLAYER | OF_DISPLAY_NAME, MM_PLAYER, NC_PLAYER),
            Obj(4002, RID_PLAYERBGF, RID_BORIS, sx + 48, sy - 32, 3f,
                OF_PLAYER | OF_DISPLAY_NAME, MM_ENEMY, NC_OUTLAW),
        };

        // M59_QUEST=1 hands out one of each quest flag, so the three
        // colours of marker can be seen at once: Alice has quests going
        // spare (yellow), the globe is an active quest's destination
        // (green) and Boris is a kill target (purple). The colours and
        // their priority are QuestMarkerColors.GetColorFor's, not this
        // server's.
        if (Environment.GetEnvironmentVariable("M59_QUEST") == "1")
            foreach (RoomObject qo in objects)
            {
                if (qo.ID == 3103) qo.Flags.IsNPCHasQuests = true;
                if (qo.ID == 3104) qo.Flags.IsNPCActiveQuest = true;
                if (qo.ID == 4002) qo.Flags.IsMobKillQuest = true;
            }
        Send(ns, ctrl, new RoomContentsMessage(new ObjectID(1, 0), objects));
        Console.WriteLine($"  -> room {room} with {objects.Length} objects");

        // Nothing is sent to loot yet. A real server sends the contents
        // of something when you open it, and this one answers the Loot
        // button the same way, so the window is not simply always up.
        lootLeft = 3;
        lootOpen = false;
        takenSoFar.Clear();

        // A sound from one of the rats, and something for the room to
        // hum. A sound with a source id plays at that object; the music
        // is a room property rather than a one-shot.
        Send(ns, ctrl, new PlayWaveMessage(
            new PlaySound(RID_RATSOUND, 2001, new PlaySound.Flags(0), 0, 0, 0, 100)));
        Send(ns, ctrl, new PlayMusicMessage(RID_MUSIC));

        // A sound with no source object but a grid square, which is how
        // the server places a noise at a spot in the room rather than on
        // a thing, and a looping one at that - the kind a fountain or a
        // fire makes until you leave. The square chosen is next to where
        // the avatar starts, because the game stops mixing a sound past
        // 2000 units and one across the map is silent by design. The stop
        // for it goes out a little later, from the message loop.
        Send(ns, ctrl, new PlayWaveMessage(
            new PlaySound(RID_RATSOUND, 0, new PlaySound.Flags(1), 11, 12, 0, 100)));
        stopAfter = 8;

        // Once per session, not once per room. Everything below used
        // to run on every EnterRoom, which was the same thing until Go
        // made EnterRoom happen again: coming back to this room then
        // re-offered the trade, replayed the flood, and re-sent the
        // avatar's enchantments - and the client is right not to clear
        // those, so the icons doubled. The library clears RoomBuffs on
        // both RoomContents and Player (`DataController.cs:2225`,
        // `:2346`) precisely because the server DOES re-send those, so
        // the room's two stay below the guard.
        if (!sessionExtrasSent)
        {
            sessionExtrasSent = true;

            // M59_CHATFLOOD=1 sends more chat than the client's log holds.
            // DataController caps ChatMessages at 200 by removing the
            // oldest before adding, so past that the count never changes
            // again - which is exactly the state a view that polls the
            // count stops redrawing in. The last line is a different
            // string from all the rest, so a screenshot says plainly
            // whether the log is still alive.
            if (Environment.GetEnvironmentVariable("M59_CHATFLOOD") == "1")
            {
                for (int i = 0; i < 205; i++)
                    Say(ns, ctrl, (i % 2 == 0) ? RID_GREETING : RID_ECHO);
                Say(ns, ctrl, RID_HEADLINE);
            }
            statChangeAfter = wantStatChange ? 12 : 0;
            if (stackMode == "shrink" || stackMode == "replace") stackCount = 0;

            if (Environment.GetEnvironmentVariable("M59_PARALYZE") == "1")
            { paralyzeAfter = 6; releaseAfter = 30; }
            if (Environment.GetEnvironmentVariable("M59_WAIT") == "1")
            { waitAfter = 6; unwaitAfter = 30; }
            if (Environment.GetEnvironmentVariable("M59_SHOOT") == "1")
                shootAfter = 6;
            if (Environment.GetEnvironmentVariable("M59_BLIND") == "1") blindAfter = 6;
            if (Environment.GetEnvironmentVariable("M59_PAIN") == "1") painAfter = 6;
            if (Environment.GetEnvironmentVariable("M59_WHITEOUT") == "1") whiteAfter = 6;
            if (Environment.GetEnvironmentVariable("M59_INVERT") == "1") invertAfter = 6;

            // Somebody offering you a trade. OfferMessage carries the
            // partner and what they are putting up, and the client's
            // TradeInfo.IsVisible goes up on it - the window is the
            // server's decision, not the view's.
            Send(ns, ctrl, new OfferMessage(
                Item(3103, RID_PLAYERBGF, RID_ALICE, 0),
                new ObjectBase[]
                {
                    Item(9101, RID_AXEBGF,  RID_AXE,  0),
                    Item(9102, RID_COINBGF, RID_COIN, 7),
                }));

            // A couple of enchantments, so the avatar panel has icons to show.
            Send(ns, ctrl, new AddEnchantmentMessage(BuffType.AvatarBuff,
                Item(6001, RID_COINBGF, RID_BUFF1, 1)));
            Send(ns, ctrl, new AddEnchantmentMessage(BuffType.AvatarBuff,
                Item(6002, RID_AXEBGF, RID_BUFF2, 1)));
        }


        // And two on the room. Same message, different type byte: the
        // data layer files these in RoomBuffs rather than AvatarBuffs,
        // and until now nothing in the client read that list.
        Send(ns, ctrl, new AddEnchantmentMessage(BuffType.RoomBuff,
            Item(6101, RID_BOOKBGF, RID_ROOMBUFF1, 1)));
        Send(ns, ctrl, new AddEnchantmentMessage(BuffType.RoomBuff,
            Item(6102, RID_COINBGF, RID_ROOMBUFF2, 1)));
        });
    }

    /// <summary>
    /// One object. The angle goes in as RADIANS even though the
    /// constructor types it ushort - it is stored straight into a float
    /// field and read back through RadianToBinaryAngle, so a binary angle
    /// passed here overflows on the way out.
    /// </summary>
    /// <summary>
    /// Makes an object in the room speak. Chat is not text on the wire:
    /// the server sends a string resource id and the client looks it up,
    /// which is why the lines this server can say are the ones it wrote
    /// into the string file.
    /// </summary>
    static void Say(NetworkStream ns, MessageControllerClient ctrl, uint rid)
        => SayAs(ns, ctrl, 2001, RID_RATBGF, rid);

    /// <summary>
    /// The same, from somebody in particular. The speaker's id is what
    /// the ignore list works on: HandleSaid looks the source up in
    /// OnlinePlayers and drops the message when that player's name is
    /// on the list, so a line has to come from a listed player for
    /// muting to be testable at all.
    /// </summary>
    static void SayAs(NetworkStream ns, MessageControllerClient ctrl,
                      uint sourceId, uint bgfRid, uint rid)
    {
        var chat = new ObjectChatMessage(
            sourceId, bgfRid, ChatTransmissionType.Normal,
            strings, rid,
            new List<InlineVariable>(), new List<ChatStyle>());

        Send(ns, ctrl, new SaidMessage(chat, strings));
    }

    // Server 104's own values, from its include/proto.h. The minimap
    // bitfield and the name colour are separate fields from the object
    // flags on this server - they were folded into the flags in vanilla.
    const uint OF_DISPLAY_NAME = 0x00000001;
    const uint OF_PLAYER = 0x00000004;
    const uint OF_ATTACKABLE = 0x00000008;
    const uint OF_GETTABLE = 0x00000010;
    const uint OF_CONTAINER = 0x00000020;
    const uint OF_BUYABLE = 0x00000400;
    // The three quest flags, from the library's non-vanilla block
    // (ObjectFlags.cs:70-72). They are what puts the "!" over a head.
    // OF_NPCHASQUESTS 0x00100000, OF_NPCACTIVEQUEST 0x00200000,
    // OF_MOBKILLQUEST 0x00400000 - set through the library's own
    // properties below rather than as raw bits.

    const uint MM_PLAYER = 0x00000001;
    const uint MM_ENEMY = 0x00000002;
    const uint MM_MONSTER = 0x00000020;

    const uint NC_PLAYER = 0xFFFFFF;
    const uint NC_OUTLAW = 0xFC9E00;

    static RoomObject Obj(uint id, uint bgfRid, uint nameRid, float x, float y,
                          float angleRadians, uint flags = 0,
                          uint minimap = 0, uint nameColor = 0)
    {
        RoomObject o = Make(id, bgfRid, nameRid, x, y, angleRadians, flags);

        // On this server the minimap dot and the name colour travel as
        // their own fields beside the flags, not as bits inside them.
        o.Flags.Minimap = minimap;
        o.Flags.NameColor = nameColor;
        return o;
    }

    static RoomObject Drawn(RoomObject o, ObjectFlags.DrawingType how)
    {
        o.Flags.Drawing = how;
        return o;
    }

    static RoomObject Make(uint id, uint bgfRid, uint nameRid, float x, float y,
                           float angleRadians, uint flags)
    {
        return new RoomObject(
            id, 1, bgfRid, nameRid, flags,
            new LightingInfo(),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new List<SubOverlay>(),
            new V3(x, 0f, y),
            RadiansToParam(angleRadians),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new List<SubOverlay>());
    }

    /// <summary>
    /// The constructor's angle parameter is typed ushort but lands in a
    /// float field that is read back as radians, so this passes the
    /// radians through the cast rather than converting to binary units.
    /// Values above a few are what overflow RadianToBinaryAngle.
    /// </summary>
    static ushort RadiansToParam(float radians) => (ushort)radians;

    static void Send(NetworkStream ns, MessageControllerClient ctrl, GameMessage m)
    {
        // One writer at a time: M59_ROOMGAP_MS sends from a timer thread.
        lock (sendLock)
        {
            m.TransferDirection = MessageDirection.ServerToClient;
            ctrl.SignMessage(m);
            var b = new byte[m.ByteLength];
            m.WriteTo(b, 0);
            ns.Write(b, 0, b.Length);
            ns.Flush();
            Console.WriteLine($"  -> {m.GetType().Name} ({b.Length} bytes)");
        }
    }

    /// <summary>
    /// Reads one framed message and returns its PI - the type byte the
    /// body starts with. The body is not parsed: this server only needs to
    /// know which message arrived, not what is in it.
    /// </summary>
    static byte ReadMessage(NetworkStream ns, out byte[] body)
    {
        body = null;
        var header = new byte[MessageHeader.Tcp.HEADERLENGTH];
        if (!ReadExactly(ns, header, header.Length)) return 0;

        ushort len = BitConverter.ToUInt16(header, 0);
        if (len == 0) return 0;

        body = new byte[len];
        if (!ReadExactly(ns, body, len)) { body = null; return 0; }
        return body[0];
    }

    static bool ReadExactly(NetworkStream ns, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n;
            try { n = ns.Read(buffer, read, count - read); }
            catch { return false; }
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }
}
