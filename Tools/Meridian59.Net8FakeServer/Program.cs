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
    const uint RID_SHILLING = 60210;
    const uint RID_PLATINUM = 60211;
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
    // M59_WEATHER=rain|snow|clear: the one message the whole weather
    // path hangs off. The server sends effect 70 with an EffectType of
    // 9 Raining, 10 Snowing or 11 ClearWeather, and `Effects.
    // HandleEffect` turns it into two booleans that stay set until it is
    // told otherwise (Effects.cs:324-334) - weather is a server-wide
    // flag, not a property of the room. The fixture sent the other six
    // effects and never one of these, so the live path - a real room, a
    // real camera, the overlay reading the data layer - had never been
    // played by anybody. Counted in client messages like M59_SHOOT, so
    // M59_WEATHER_AFTER picks the message it goes out on (default 6).
    static int weatherAfter;
    static string weatherKind;

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

    // --- Sound ---------------------------------------------------------------
    // The fixture's whole audible surface used to be three messages in room 1:
    // a one-shot from a rat, one music track, one looping sound (the same
    // file as the one-shot). Room 2 said nothing at all, so the room-change
    // sound path was only ever run in the direction where the loop had
    // already been stopped. Everything here is off unless set, and unset the
    // sounds go out exactly as before. The FX* files come from make-sounds.sh
    // (the test dump holds two oggs, both 2 s of 440 Hz at -21 dB); each has
    // its own level so the Master bus says WHICH sound is sounding.
    // Timed events count client messages from the room entry, like M59_SHOOT:
    // M59_SND_AFTER (default 8, which is also how long the room-1 loop has
    // always lived before its StopWave - so it moves that stop too).
    //
    // M59_LOOPFIRST=1  room 1's loop goes out right after PlayerMessage,
    //   before RoomContents. The real server does exactly that: ToCliPlayer
    //   sends BP_PLAYER and then `Send(poOwner,@SendLoopingSounds,#who=self)`
    //   (`user.kod:3370-3395`, `room.kod:662-690`); the music comes later,
    //   from the room's own SomethingEnter (`room.kod:2127-2129`). The
    //   fixture put the loop after the contents, so the Player message and
    //   the loop were never in the same drained batch the way they are live.
    //   (Room 2's own loop, below, always goes out in the real place.)
    // M59_ROOM2SND=1   room 2 gets its own looping ambient (FXRoom2, at the
    //   grid square of its spawn, SOUND_LOOP, no object: `room.kod:686-689`)
    //   sent right after Player, and its own music track (FXMusic2, after the
    //   contents, `room.kod:2129`). Real rooms do both on every entry, and
    //   most rooms have their own music, so a Go is a change of track.
    // M59_MUSIC=swap|repeat|zero   one PlayMusic, M59_SND_AFTER messages in.
    //   swap:   the OTHER track (room 1: FXMusic2; room 2: AMBCave) - a room
    //     that changes its music under you, as feyforst and duke5 do for
    //     everyone present (`feyforst.kod:250-264`, `duke5.kod:264,319`);
    //     the client must stop the old one and start the new
    //     (`ControllerSound.cpp:506-519`).
    //   repeat: the track already playing, again - SendRoomMusic is sent on
    //     EVERY entry and by those re-sends (`room.kod:2129`); the client must
    //     not restart it (`ControllerSound.cpp:492-519` only starts when there
    //     is none or the name differs, `:505`).
    //   zero:   PlayMusic with resource 0 - `SendRoomMusic(music_rsc = $)`
    //     writes `AddPacket(4,0)` (`user.kod:4530-4545`), what a room with no
    //     music sends; the reference returns before touching what is playing
    //     (`ControllerSound.cpp:485`).
    // M59_WADE=<1-3>   the room named by M59_WADE_ROOM (1 or 2, default 1)
    //   sends a WadingSoundFileRID (FXSplash) in its PlayerMessage and, right
    //   after it, a SectorChange putting the sector under the spawn at that
    //   depth. The real Player packet carries the room's wading sound in
    //   SendExtraRoomInfo (`room.kod:2272-2285`; vrWading_Sound defaults to
    //   splash.ogg for EVERY room, `room.kod:81,192` - so the fixture's 0 was
    //   the unfaithful value) and depth arrives as SectorChangeSendUser from
    //   plSector_flag_changes (`user.kod:3399`, `room.kod:2209-2223`,
    //   `user.kod:4757-4770`). Wading only fires when the avatar MOVES in
    //   that sector (ControllerSound.cpp:155-195), so drive a walk.
    // M59_SNDNAME=miscase|missing
    //   miscase: room 1's whole soundscape goes out under names the disk does
    //     not spell that way: one-shot "rat_awr.wav", loop "RAT_AWR.WAV",
    //     music "ambcave.wav" (disk: Rat_awr.ogg, AMBCave.ogg). A .rsb written
    //     on a case-insensitive filesystem names files in whatever case its
    //     author typed; 238 of 520 names in a real dump differ from the file.
    //     THIS ONE MUST PASS: same levels as a run without it. The client
    //     resolves case-insensitively (ResourceManager's Wavs/Music maps use
    //     OrdinalIgnoreCase; M59Sound.OnDisk is the backstop).
    //   missing: a one-shot, a loop and a music track whose files are not on
    //     disk (FXGone, FXGoneMusic), then a StopWave for the file that never
    //     started. A client on an older resource set than the server: kod
    //     names whatever it likes. Nothing may play, the music in progress
    //     must carry on, and the next real sound must still work.
    // M59_SNDSTOP=none|loop|oneshot   any value splits the one file that the
    //   loop and the one-shot shared: the loop is FXLoop1 (2 s, looped), the
    //   one-shot is FXShot (10 s, from rat 2001), and the stop is sent by
    //   NAME with no object, as `WaveSendUserStop(wave_rsc, source_obj = 0)`
    //   does (`user.kod:4518-4527`, `room.kod:742`). loop stops only the loop
    //   (the one-shot must run on), oneshot stops only the one-shot (the
    //   loop must run on), none sends no stop. The old loop stop is not sent.
    // M59_LOOPS2=1     a second, different loop (FXLoop2, the square beside)
    //   - plLooping_sounds is a list and SendLoopingSounds sends every entry
    //   (`room.kod:662-690`; the sewer king's lair has six). The default
    //   stop (Rat_awr) or M59_SNDSTOP=loop must leave this one sounding.
    // M59_SNDOWNER=1   a loop whose source is rat 2003 (FXLoop3, ID set),
    //   then rat 2003 is REMOVED, with no StopWave. The client has to drop
    //   it with the object (`RemoteNode.cpp:111-124` stops a node's sounds
    //   in its destructor; `ControllerSound.cpp:405-408`). Server-104's kod
    //   never sends a loop with an object: SendLoopingSounds and
    //   AddLoopingSound pass row/col only (`room.kod:684-689,715-719`) - this
    //   is the wire shape the library and the reference accept
    //   (`PlaySound.cs` ID + Flags.IsLoop), not something Server-104 does.
    // M59_SNDGHOST=1   a loop with an id for an object that is not in the
    //   room (9999, FXLoop3), then a StopWave naming the same id. Lookup
    //   fails in the reference (`ControllerSound.cpp:393-411` leaves x,y,z at 0
    //   and attaches nothing), so it plays at the origin, is kept in the
    //   global list, and the stop's third pass finds it (`:326-335`). Server-104 sends ids of objects that can leave
    //   before the sound does. Use either this or M59_SNDOWNER in one run,
    //   they share FXLoop3.
    static readonly bool loopFirst = EnvOn("M59_LOOPFIRST");
    static readonly bool room2Snd = EnvOn("M59_ROOM2SND");
    static readonly string musicMode = EnvStr("M59_MUSIC");
    static readonly int wadeDepth = EnvInt("M59_WADE", 0);
    static readonly int wadeRoom = EnvInt("M59_WADE_ROOM", 1);
    static readonly string sndName = EnvStr("M59_SNDNAME");
    static readonly string sndStop = EnvStr("M59_SNDSTOP");
    static readonly bool loops2 = EnvOn("M59_LOOPS2");
    static readonly bool sndOwner = EnvOn("M59_SNDOWNER");
    static readonly bool sndGhost = EnvOn("M59_SNDGHOST");
    static readonly int sndAfter = EnvInt("M59_SND_AFTER", 8);
    static int sndClock = -1;        // -1: no timed sound events this room
    static readonly List<(int At, Action<NetworkStream, MessageControllerClient> Do)> sndPlan
        = new List<(int, Action<NetworkStream, MessageControllerClient>)>();
    static uint musicNow;            // the track the client was last told
    static bool SndSplit => sndStop != null;
    static uint ShotRid => SndSplit ? RID_FXSHOT : sndName == "miscase" ? RID_RATSOUND_LC : RID_RATSOUND;
    static uint LoopRid => SndSplit ? RID_FXLOOP1 : sndName == "miscase" ? RID_RATSOUND_UC : RID_RATSOUND;
    static uint Music1Rid => sndName == "miscase" ? RID_MUSIC_LC : RID_MUSIC;
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
    // The four quest-log headers, in Server-104's own words
    // (`user.kod:205-208`). M59_QUESTLOG builds the group with these
    // rather than with the two above, because the shape under test is
    // kod's: a header for each half whether or not that half has
    // anything in it.
    const uint RID_QACTIVE = 60126;
    const uint RID_QNOACTIVE = 60127;
    const uint RID_QDONE = 60128;
    const uint RID_QNODONE = 60129;
    const uint RID_RATSOUND = 60080;
    const uint RID_MUSIC = 60081;
    // The audible test surface (see "Sound" in the switch block). The FX*
    // files do not exist in the client's test dump, which holds exactly two
    // oggs; Tools/Meridian59.Net8FakeServer/make-sounds.sh writes them into
    // a resource folder. The *_LC / *_UC ids name files that DO exist
    // (Rat_awr.ogg, AMBCave.ogg) in a case the disk does not use.
    const uint RID_FXLOOP1 = 60170;
    const uint RID_FXLOOP2 = 60171;
    const uint RID_FXLOOP3 = 60172;
    const uint RID_FXSHOT = 60173;
    const uint RID_FXROOM2 = 60174;
    const uint RID_FXMUSIC2 = 60175;
    const uint RID_FXSPLASH = 60176;
    const uint RID_RATSOUND_LC = 60177;
    const uint RID_RATSOUND_UC = 60178;
    const uint RID_MUSIC_LC = 60179;
    const uint RID_FXGONE = 60180;
    const uint RID_FXGONEMUSIC = 60181;

    // --- Guilds, using things, lists under an open window, preferences ------
    // Five audits in a row found player-visible bugs in areas this fixture
    // could not reach, and each one built these switches in a throwaway copy.
    // They are folded in here. Everything below is OFF unless its M59_* var
    // is set; unset, the server says exactly what it said before.
    //
    // These knobs are read when they are USED rather than once at start, so
    // the trigger's `env NAME=VALUE` can change a rank, a hall or a flag word
    // in the middle of a session and the next `info` sends the new one. The
    // older switches above are `static readonly` on purpose and are not
    // settable that way.
    //
    // THE GUILD SURFACE. The stock fixture answered ReqGuildInfo and
    // ReqGuildList and nothing else, always as a guildmaster, with a flag
    // word that set RENOUNCE and DISBAND at once - which no real player ever
    // holds (`guildcmd/gcrennce.kod:57-69`: a master has RENOUNCE REMOVED,
    // everyone else has it added; `guildcmd.kod:81-91`: every other command
    // is added only at or above its own viRank_needed). So every flow past
    // reading the roster was unreachable.
    //
    // M59_GUILD=member|lieutenant|master|solo|none|both  who you are in the
    //   guild the roster describes. The flag word is built from the real
    //   GCID_* bits (`kod/include/blakston.khd:2956-2971`) at the rank's own
    //   thresholds, so:
    //     member     (RANK_APPRENTICE 1): RENOUNCE | VOTE
    //     lieutenant (RANK_LIEUTENANT 4): the above plus EXILE, SET_RANK,
    //                 ABANDON_HALL, the four diplomacy rights
    //     master     (RANK_MASTER 5): everything a lieutenant has EXCEPT
    //                 RENOUNCE, plus ABDICATE, DISBAND, SET_PASSWORD, INVITE,
    //                 ROSTER
    //     solo       master of a guild whose roster is one name
    //     none       no guild: the server answers ReqGuildInfo with a
    //                 MsgSendUser line and no GuildInfo at all
    //                 (`user.kod:2749-2753`, user_no_guild)
    //     both       the old unrealistic word, kept so a run can show what
    //                 the fixture used to say
    //   Unset behaves as `both`, which is what this server always sent.
    // M59_GUILD_HALL=0|1  whether the guild owns a hall. The hall is NOT a
    //   field on the wire: the server sends the password byte and the chest
    //   password only to a RANK_MASTER of a guild that HAS a hall, and 0 for
    //   everyone else (`user.kod:2756-2765`) - which is the whole of the
    //   client's evidence. Unset is 1, as before.
    // M59_GUILD_FLAGS=<n>  the flag word verbatim (0x... or decimal), after
    //   the rank has chosen one. For the four diplomacy rights on their own,
    //   and for a word with VOTE clear while a vote is on the wire.
    // M59_GUILD_VOTE=<name|none>  which member the supported-member field
    //   names (`user.kod:2775-2790`: the server writes YOUR row's third
    //   element, 0 for nobody). Names are matched case-insensitively against
    //   the roster; unset is Alice, as before.
    // M59_GUILD_HALLS=<n>  ReqGuildList answers with a GuildHalls list of n
    //   halls instead of the diplomacy list. There is no client request for
    //   this list at all - the real one comes from SendBuyGuildHall when you
    //   talk to a guild broker (`user.kod:7670-7700`) - so a request the
    //   client does send has to carry it. n=0 is the legitimate empty list
    //   that SendBuyGuildHall sends when nothing is affordable, and silently
    //   closing on it was itself a bug (fa28d39).
    // M59_GUILD_ASK=<n>  pushes a GuildAsk n client messages into the
    //   session: the two prices a guild costs (`user.kod:7703-7712`,
    //   SendCreateGuild, which a broker sends unasked). `ask` on the trigger
    //   sends one at a chosen moment instead.
    // M59_GUILD_SHIELD=1  answers the shield designer's two requests, the
    //   sample list and the shield itself - but the shield reply ONCE only,
    //   so a second open with no reply shows whatever the panel kept.
    //   (`user.kod:2860-2881` samples, `:2883-2925` the shield.)
    // M59_SHIELD_ERROR=1  a ClaimShield is answered with GuildShieldError
    //   rather than a shield. The real server has no error packet on this
    //   path - UserCommandClaimShield always answers with
    //   UserGuildSendShieldInfo (`user.kod:2627-2659`) - so this stands in
    //   for the reference's own error case and is the library's class.
    // M59_UCLOG=1  every UserCommand body as hex, so the ids a flow puts on
    //   the wire can be read off without a packet capture.
    //
    // USING THINGS. The fixture answered no ReqUse, no ReqUnuse and no
    // ReqApply and sent no UseList, so IsInUse was false client-side for the
    // whole session and the `inUse` argument to Carry was lost on the wire.
    // M59_USE=1  ReqUse is answered with Use and ReqUnuse with Unuse, the
    //   way `user.kod` does through UserUse/UserUnuse, and ReqInventory
    //   carries a UseList of what is already worn.
    // M59_USE_LIST=<ids|none>  what that UseList names (default 8001, the
    //   axe, which is the item Carry already marks inUse).
    // M59_USE_CHANGE=rename|applyable|flags  after a successful use, an
    //   in-place change to the same item: a new name, the APPLYABLE flag set
    //   through a full ObjectUpdate, or the same flag through
    //   ChangeObjectFlags. All three are what the caption depends on
    //   (`DataController.cs:2288-2292`, `:2139-2143`), and the third is a
    //   Server-104-flavour packet the client only compiles outside VANILLA
    //   and OPENMERIDIAN.
    //
    // LISTS CHANGING UNDER AN OPEN WINDOW. M59_LOOT=counts|shrink|empty|remove
    //   re-sends the container's contents, changed, when the trigger fires
    //   (`loot` on the trigger file) or M59_LOOT_AFTER messages in: the same
    //   counts at new values, a shorter list, an empty list, or one named
    //   item gone. A real server re-sends the whole list on any change
    //   (`user.kod` SendObjectContents is the only shape there is), which is
    //   why a window that keeps its own widgets goes stale.
    // M59_FLOOREQUIP=1  the floor coin and the axe inside the container carry
    //   OF_EQUIPPED. Server-104 does not send that bit at all - kod keeps
    //   0x8000 free (`kod/include/blakston.khd:2739`) - it is the library's
    //   and the other flavours' (`ObjectFlags.cs:65`), and it is what the
    //   loot list reads to append " (in use)".
    //
    // PREFERENCES AND PASSWORDS. Neither UC_REQ_PREFERENCES nor
    // UC_SEND_PREFERENCES was answered, so PreferencesFlags.Enabled stayed
    // false and all seven server preferences were disabled every session;
    // BP_CHANGE_PASSWORD was not answered either.
    // M59_PREFS=1  both are answered with ReceivePreferences carrying the
    //   stored word - the server echoes it after a set so the client can see
    //   a bit it refused (`user.kod:2405-2409`, UserCommandSetPreferences
    //   ends in UserSendPreferences; `:2411-2418` is the reply itself).
    // M59_PREFS_WORD=<n>  the word the session starts with (default 0x7E,
    //   CF_DEFAULT_PREF, `blakston.khd:80`).
    // M59_PREFS_REFUSE=<n>  bits the server will not let you set: they are
    //   masked out of what you send before the echo, which is exactly the
    //   case the echo exists for.
    // M59_PREFS_DELAY=<ms>  how long the FIRST answer is held back. The
    //   client asks for preferences the moment the character is accepted
    //   (`BaseClient.cs:565-572`) and again in RequestInfoAfterLogin
    //   (`:2765-2772`), so a slow server is what the status bar's safety
    //   control races.
    // M59_PASSWORD=ok|bad  BP_CHANGE_PASSWORD is answered with BP_PASSWORD_OK
    //   or BP_PASSWORD_NOT_OK; the real server compares the old hash and
    //   sends one or the other and nothing else (`blakserv/game.c:536-566`).
    //
    // QUESTS. ReqTriggerQuest only echoed a say line and nothing ever sent a
    // CHANGED quest log, so the whole progress-and-completion half of the
    // quest UI had never been played: the log was five rows that could not
    // move, and ReqNPCQuests answered with the same three quests whatever it
    // was asked about, so the empty-NPC window and the rebuild-while-open
    // path were both unreachable.
    // M59_QUESTLOG=1  a ReqTriggerQuest changes the player's quest state and
    //   the WHOLE group 5 goes out again, unasked. That is the real shape:
    //   AddCurrentQuest ends in Send(self,@ToCliStats,#group=5)
    //   (`user.kod:10204-10207`) and RemoveCurrentQuest Posts the same
    //   (`:10236-10241`, posted so plQuestHistory updates first), which are
    //   the only senders besides the BP_SEND_STATS reply at `:1824`. The
    //   first trigger of a quest advances it to its next node; the second
    //   finishes it, and it reappears under the completed header. The group
    //   is built as ToCliStats group 5 builds it (`user.kod:3663-3703`):
    //   TWO header rows always, "No Active Quests"/"No Completed Quests"
    //   where a list is empty, so the log is never actually empty. An active
    //   row's fourth field is the quest TEMPLATE id, not a score
    //   (`SendActiveQuestData`, `:3765-3767`), which is what moves when a
    //   quest advances.
    // M59_NPCQ=empty|changing  what ReqNPCQuests answers with.
    //   empty is a zero-length list - a legitimate answer the wire admits
    //   (`QuestUIListMessage.cs:71-79`) and the state an NPC with nothing on
    //   offer is in; the client's empty-NPC window was fixed without ever
    //   being driven. changing hands out a different list on every request,
    //   so the panel's rebuild-while-open path runs instead of its signature
    //   comparing equal every time (`NpcQuestsPanel.cs:403`).
    // M59_NEWSROW=1  the news globe (3104) is also a row inside the
    //   container on the floor, so a held row press sends ReqLook on it
    //   (`LootPanel.cs:438`) and the news window can be opened by a scripted
    //   run at all. A real server's board is an object you look at where it
    //   stands; both the board and the globe sit at the avatar's feet here,
    //   where no tap and no @obj: reaches them, so the contents list - whose
    //   rows send the same ReqLook - is the route. The reply is unchanged:
    //   ReqLook 3104 answers LookNewsGroup either way.
    //
    // THE TRIGGER. Several of these have to happen at a MOMENT - while a
    // panel is open, while a popup is armed, between two messages - and two
    // audits each built their own timing hack. There is one mechanism now:
    // M59_FIRE=<n>:<cmd>[,<n>:<cmd>...]  runs cmd after n client messages,
    //   counted in CLIENT MESSAGES like every timed switch here, because
    //   pings outpace frames under load (the convention is kept, not
    //   replaced).
    // M59_TRIGGER=<path>  a file polled every 200 ms; each line appended to
    //   it is one command, run at the next client message so it is sent on
    //   the session's own thread, in order with everything else. The
    //   log-watcher half lives in `watch.sh`, which appends a line when a
    //   pattern appears in the client's log - so "when the panel is open" is
    //   expressed once, in one place, for every switch.
    // The commands: info, list, halls [n], ask, shield, shielderr [text],
    //   prefs, bag, loot [mode], quests, use <id>, unuse <id>, uselist [ids|none],
    //   change [mode] [id], say, env NAME=VALUE.
    static string GuildMode => EnvStr("M59_GUILD") ?? "both";
    static readonly string guildFire = EnvStr("M59_FIRE");
    static readonly string triggerPath = EnvStr("M59_TRIGGER");
    static readonly bool wantUse = EnvOn("M59_USE");
    static readonly bool wantUcLog = EnvOn("M59_UCLOG");
    static readonly bool wantShieldReply = EnvOn("M59_GUILD_SHIELD");
    static readonly bool wantFloorEquip = EnvOn("M59_FLOOREQUIP");
    static readonly bool wantPrefs = EnvOn("M59_PREFS");
    static readonly int prefsDelayMs = EnvInt("M59_PREFS_DELAY", 0);
    static readonly string passwordMode = EnvStr("M59_PASSWORD");
    static readonly int guildAskAfter = EnvInt("M59_GUILD_ASK", 0);
    static readonly int lootAfter = EnvInt("M59_LOOT_AFTER", 0);
    static readonly bool wantQuestLog = EnvOn("M59_QUESTLOG");
    static readonly string npcQuestMode = EnvStr("M59_NPCQ");
    static readonly bool wantNewsRow = EnvOn("M59_NEWSROW");
    /// <summary>The preference word the server is holding for this player.</summary>
    static uint prefsWord = ParseWord(EnvStr("M59_PREFS_WORD"), 0x7E);
    /// <summary>Set once the shield reply has gone out, so the second ask is met with silence.</summary>
    static bool shieldSent;
    /// <summary>True once the first preference answer has been sent, which is the only one M59_PREFS_DELAY holds.</summary>
    static bool prefsAnswered;
    /// <summary>Client messages seen in game mode; what every timed switch counts.</summary>
    static int clientMsgs;
    /// <summary>Commands the trigger file has handed over and the loop has not run yet.</summary>
    static readonly Queue<string> fired = new Queue<string>();
    /// <summary>M59_FIRE, parsed: message count -> command.</summary>
    static readonly List<(int At, string Cmd)> firePlan = new List<(int, string)>();

    // The guild hall names and the two strings only these switches need.
    const uint RID_HALL1 = 60200;
    const uint RID_HALL2 = 60201;
    const uint RID_HALL3 = 60202;
    const uint RID_SHIELDERR = 60203;
    const uint RID_AXEBLESSED = 60204;
    const uint RID_NOGUILD = 60205;

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
            // The other two coins, so the HUD's purse has something to
            // count. Named the way a server names a stack - with the
            // article and the plural attached - because that is what the
            // client has to match against, and a fixture that answers
            // with a bare "shilling" would make the matching look easier
            // than it is. See notes/harness.md.
            new RsbResourceID(RID_SHILLING,   "12 shillings",    4),
            new RsbResourceID(RID_PLATINUM,   "a platinum bar",  4),
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
            // BGF FILENAMES, as a real server sends them. These used to
            // be readable words - "health", "mana" - and that is not
            // what a condition's resource string is: it is the key the
            // library looks the ICON up by
            // (`StatNumeric.cs:304-316`, `GetObject(resourceName)`). The
            // readable version hid a bug for months, because the client
            // was printing the string next to the bar and it happened to
            // read correctly here while a real server put "icon.bgf" on
            // screen. A fixture that answers more nicely than the thing
            // it stands in for is a fixture that cannot fail.
            new RsbResourceID(RID_COND1,      "iconveyance.bgf",  4),
            new RsbResourceID(RID_COND2,      "imanashld.bgf",    4),
            // One with no art behind it and one that is the literal
            // "icon.bgf" a real server sends, so both fallback shapes
            // are on screen: a name worth printing, and one that is not.
            new RsbResourceID(RID_COND3,      "vigor.bgf",        4),
            new RsbResourceID(RID_COND4,      "icon.bgf",         4),
            new RsbResourceID(RID_QACTIVE,    "Active Quests: ",  4),
            new RsbResourceID(RID_QNOACTIVE,  "No Active Quests", 4),
            new RsbResourceID(RID_QDONE,      "Completed Quests: ", 4),
            new RsbResourceID(RID_QNODONE,    "No Completed Quests", 4),
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
            new RsbResourceID(RID_FXLOOP1,    "FXLoop1.wav",      4),
            new RsbResourceID(RID_FXLOOP2,    "FXLoop2.wav",      4),
            new RsbResourceID(RID_FXLOOP3,    "FXLoop3.wav",      4),
            new RsbResourceID(RID_FXSHOT,     "FXShot.wav",       4),
            new RsbResourceID(RID_FXROOM2,    "FXRoom2.wav",      4),
            new RsbResourceID(RID_FXMUSIC2,   "FXMusic2.wav",     4),
            // Named .ogg, as Server-104 names every sound (`room.kod:81`, splash.ogg):
            // RoomInfo resolves the wading name as sent, with none of the
            // .wav -> .ogg swap PlaySound applies (`RoomInfo.cs:849`).
            new RsbResourceID(RID_FXSPLASH,   "FXSplash.ogg",     4),
            // The same two files as above, spelled the way a .rsb that was
            // written on a case-insensitive filesystem spells them.
            new RsbResourceID(RID_RATSOUND_LC, "rat_awr.wav",     4),
            new RsbResourceID(RID_RATSOUND_UC, "RAT_AWR.WAV",     4),
            new RsbResourceID(RID_MUSIC_LC,   "ambcave.wav",      4),
            // Names with no file behind them at all.
            new RsbResourceID(RID_FXGONE,     "FXGone.wav",       4),
            new RsbResourceID(RID_FXGONEMUSIC, "FXGoneMusic.wav", 4),
            // The guild halls a broker offers (`user.kod:7686-7694` sends each
            // name as a resource id), the shield designer's error line, and
            // the name M59_USE_CHANGE=rename renames the axe to.
            new RsbResourceID(RID_HALL1,      "Hall of the Broken Wheel", 4),
            new RsbResourceID(RID_HALL2,      "Marion Guild House", 4),
            new RsbResourceID(RID_HALL3,      "Tos Guild Tower",   4),
            new RsbResourceID(RID_SHIELDERR,
                "That shield is already claimed by another guild.", 4),
            new RsbResourceID(RID_AXEBLESSED, "a blessed nerudite axe", 4),
            new RsbResourceID(RID_NOGUILD,    "You do not belong to a guild.", 4),
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
                        StartTrigger();
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
                    new StopSound(LoopRid, 0)));
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
            // Rain, snow or a clear sky. One message each and nothing
            // else: the flag it sets stays set, and the client is
            // expected to keep drawing weather until a ClearWeather
            // turns it off or the connection goes.
            if (weatherAfter > 0 && --weatherAfter == 0)
            {
                Console.WriteLine($"  -> Weather {weatherKind}");
                Send(ns, ctrl, new EffectMessage(
                    weatherKind == "rain" ? (Effect)new EffectRaining() :
                    weatherKind == "snow" ? new EffectSnowing() :
                                            new EffectClearWeather()));
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
            Triggers(ns, ctrl);
            Geometry(ns, ctrl);
            StackChange(ns, ctrl);
            SoundPlan(ns, ctrl);

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

                // Logging out, modelled on what Server-104 actually does
                // with BP_REQ_QUIT, because the shape is the whole point
                // of the test and a fixture that invented a kinder one
                // could not fail.
                //
                // The real server answers with one BP_QUIT byte and then
                // puts the session back into STATE_SYNCHED
                // (`Server-104/blakserv/game.c:423-425`). That state
                // change is what takes the character out of the world
                // (`GameExit` -> `ClientHangupToBlakod`, `game.c:71-81`),
                // and `SynchedInit` then skips the password - the account
                // is still known and verified - and goes straight to the
                // main menu, which sends AP_GETCHOICE
                // (`blakserv/synched.c:47-57`, `:818-835`). The socket is
                // never closed.
                //
                // So: Quit, then back out of game mode, then GetChoice.
                // `inGame = false` is this loop's STATE_SYNCHED, and it
                // matches what the client's own message controller does on
                // Quit - back to login protocol mode
                // (`Meridian59/Protocol/MessageController/MessageController.cs:1049-1055`)
                // - so the ReqGameState the library sends next is read on
                // the right side of this loop's own switch. From there the
                // existing ReqGame arm sends GameState and the characters,
                // which is exactly the route the real server takes too.
                case MessageTypeGameMode.ReqQuit:
                    Console.WriteLine("  <- ReqQuit");
                    Send(ns, ctrl, new QuitMessage());
                    inGame = false;
                    Send(ns, ctrl, new GetChoiceMessage(new HashTable()));
                    Console.WriteLine("  (client is back at the main menu)");
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

                case MessageTypeGameMode.ReqUse:
                case MessageTypeGameMode.ReqUnuse:
                case MessageTypeGameMode.ReqApply:
                {
                    // The id is the four bytes after the PI, as ReqLook's is
                    // (`ReqUseMessage.WriteTo`): these carry a bare ObjectID
                    // with no count, so there is no ReqGet-style flag byte in
                    // the way. Logged whether or not M59_USE is set, because
                    // "did the press reach the wire" is the first question a
                    // use bug asks and "game-mode 102, ignored" is a poor
                    // answer to it.
                    uint item = body.Length >= 5 ? BitConverter.ToUInt32(body, 1) : 0;
                    Console.WriteLine($"  <- {(MessageTypeGameMode)pi} {item}");
                    if (!wantUse) break;
                    if (pi == (byte)MessageTypeGameMode.ReqUse) UseItem(ns, ctrl, item, true);
                    else if (pi == (byte)MessageTypeGameMode.ReqUnuse) UseItem(ns, ctrl, item, false);
                    // ReqApply is logged and not answered: the real server
                    // applies the item to a target and the result is whatever
                    // the item does, which is not a protocol reply at all.
                    break;
                }

                case MessageTypeGameMode.ChangePassword:
                    // M59_PASSWORD: the account server compares the old hash
                    // and answers with one byte either way and nothing else
                    // (`blakserv/game.c:536-566`). Unset, the request is only
                    // logged - which is where the password flow used to stop.
                    Console.WriteLine($"  <- ChangePassword ({body.Length} bytes)");
                    if (passwordMode == "ok")
                        Send(ns, ctrl, new PasswordOKMessage());
                    else if (passwordMode == "bad")
                        Send(ns, ctrl, new PasswordNotOKMessage());
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
                    // M59_UCLOG=1: the body as hex. Every guild flow past the
                    // roster is a UserCommand carrying an id - the member
                    // being exiled, the guild being declared on - and reading
                    // those off the wire needed a packet capture before.
                    if (wantUcLog)
                        Console.WriteLine("     body " + BitConverter.ToString(body));

                    if (cmd == (byte)UserCommandType.ReqGuildInfo) SendGuild(ns, ctrl);
                    if (cmd == (byte)UserCommandType.ReqGuildList)
                    {
                        // M59_GUILD_HALLS: nothing the client sends asks for
                        // the hall list (the real one comes unasked from a
                        // broker, `user.kod:7670-7700`), so the request it
                        // does send carries it when the switch is set.
                        string halls = EnvStr("M59_GUILD_HALLS");
                        if (halls != null) SendGuildHalls(ns, ctrl, EnvInt("M59_GUILD_HALLS", 3));
                        else SendGuildList(ns, ctrl);
                    }
                    // UC 7 is BOTH Safety and ReqPreferences in the enum, and
                    // on the wire they differ only in length: the request is
                    // the command byte alone, Safety carries a value.
                    if (cmd == (byte)UserCommandType.ReqPreferences && wantPrefs)
                        AnswerPrefsRequest(ns, ctrl);
                    if (cmd == (byte)UserCommandType.SendPreferences && wantPrefs)
                        SetPrefs(ns, ctrl, body);
                    if (cmd == (byte)UserCommandType.GuildShields && wantShieldReply)
                        SendShieldSamples(ns, ctrl);
                    if (cmd == (byte)UserCommandType.GuildShield && wantShieldReply)
                    {
                        // Once only. The reply is what fills the designer, so
                        // a second open with no answer shows whatever the
                        // panel kept from the first - which is the whole
                        // point of sending it once.
                        if (shieldSent) Console.WriteLine("     (shield already sent once; silence)");
                        else { shieldSent = true; SendShield(ns, ctrl, 10, 20, 2, 9001); }
                    }
                    if (cmd == (byte)UserCommandType.ClaimShield && wantShieldReply)
                        ClaimShield(ns, ctrl, body);
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
                        // THE SLOT IS CHECKED, because the real server
                        // checks it. Server-104 reads the first field of
                        // NewCharInfo as the user object - `oUser =
                        // Nth(client_msg,2)` then `Send(oUser,
                        // @IsFirstTime)` (`kod/util/system.kod:4570`,
                        // `:4572`, `:4579`) - so a slot the account was
                        // never offered is refused. This fixture used to
                        // accept anything, and because its only empty
                        // slot was also id 0, the client's slot-0 bug
                        // looked identical to correct behaviour here and
                        // only showed up on the live server, at the very
                        // end of the wizard. NotFirstTime is the nearest
                        // of the errors the client can already explain.
                        uint slot = uint.MaxValue;
                        try { slot = new Meridian59.Protocol.SubMessage.SubMessageNewCharInfo(body, 1).AvatarID.ID; }
                        catch (Exception ex)
                        { Console.WriteLine($"     (NewCharInfo unreadable: {ex.Message})"); }

                        if (FreeSlots.Count > 0 && !FreeSlots.Contains(slot))
                        {
                            Console.WriteLine($"     (new character refused: slot {slot} is not "
                                            + $"one of this account's empty slots "
                                            + $"[{string.Join(",", FreeSlots)}])");
                            Send(ns, ctrl, new CharInfoNotOkMessage(
                                CharInfoNotOkError.NotFirstTime));
                        }
                        else if (Environment.GetEnvironmentVariable("M59_NAMETAKEN") == "1")
                        {
                            Console.WriteLine("     (new character refused: name in use)");
                            Send(ns, ctrl, new CharInfoNotOkMessage(
                                CharInfoNotOkError.NameInUse));
                        }
                        else
                        {
                            Console.WriteLine($"     (slot {slot} accepted)");
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
                {
                    // The body is the PI, the NPC's ObjectID and then the
                    // quest's - both through ObjectID rather than a raw
                    // uint read, because the top four bits are a flag
                    // saying a count follows (`ReqTriggerQuestMessage.cs:
                    // 50-57`).
                    uint npc = 0, quest = 0;
                    if (body.Length >= 5)
                    {
                        var who = new ObjectID(body, 1);
                        npc = who.ID;
                        if (body.Length >= 1 + who.ByteLength + 4)
                            quest = new ObjectID(body, 1 + who.ByteLength).ID;
                    }
                    Console.WriteLine($"  <- ReqTriggerQuest npc {npc} quest {quest}");
                    // A real server starts or finishes the quest and the
                    // quest log follows, unasked. Without M59_QUESTLOG it
                    // still only answers, so the accept is visible from
                    // the client's side and nothing else moves.
                    if (wantQuestLog) TriggerQuest(ns, ctrl, quest);
                    Say(ns, ctrl, RID_ECHO);
                    break;
                }

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
                    // M59_USE: and what of it is already worn. The pack's
                    // inUse argument never reached the wire, so without this
                    // nothing is ever in use client-side and the whole
                    // unwield half of the window is unreachable.
                    if (wantUse) SendUseList(ns, ctrl);
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
        if (wantQuestLog) { SendQuestLog(ns, ctrl); return; }

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
    /// M59_QUESTLOG: the three quests this fixture offers, as a quest
    /// TEMPLATE id (what an active row's fourth field carries,
    /// `user.kod:3765-3767`), the quest object the row points at, its
    /// name and its icon. The NPC offer hands out 8001-8003 for the same
    /// three, so a trigger names one of those; the log's own object ids
    /// are the 9001-9003 the stock log already used.
    /// </summary>
    static readonly (uint Offer, uint Template, uint Obj, uint Name, uint Icon)[] questBook =
    {
        (8001, 1, 9001, RID_QUEST1, RID_BOOKBGF),
        (8002, 2, 9002, RID_QUEST2, RID_AXEBGF),
        (8003, 3, 9003, RID_QUEST3, RID_COINBGF),
    };

    /// <summary>plActiveQuests and plQuestHistory, in template order.</summary>
    static readonly List<uint> questsActive = new List<uint>();
    static readonly List<uint> questsDone = new List<uint>();

    /// <summary>
    /// The quest log as ToCliStats builds group 5 (`user.kod:3663-3703`).
    ///
    /// Two header rows ALWAYS: the active header or "No Active Quests",
    /// then the completed header or "No Completed Quests". A real log is
    /// therefore never empty, which the stock fixture's five fixed rows
    /// happened to satisfy and no changed log had ever tested. A header
    /// is a StatList with no object and no points, which is what the
    /// quest window draws in bold and refuses to open
    /// (`UIQuests.cpp:37-53`).
    ///
    /// The index byte is kod's: 0 for the active header, then 1.. over
    /// the active quests, then the running count for the completed
    /// header and the ones after it.
    /// </summary>
    static void SendQuestLog(NetworkStream ns, MessageControllerClient ctrl)
    {
        var rows = new List<Stat>();
        byte at = 0;

        rows.Add(Quest(0, questsActive.Count > 0 ? RID_QACTIVE : RID_QNOACTIVE, 0, 0));
        at = 1;
        foreach (uint t in questsActive)
        {
            var q = Find(t);
            rows.Add(Quest(at, q.Name, q.Obj, q.Template, q.Icon));
            at++;
        }

        rows.Add(Quest(at, questsDone.Count > 0 ? RID_QDONE : RID_QNODONE, 0, 0));
        at++;
        foreach (uint t in questsDone)
        {
            var q = Find(t);
            rows.Add(Quest(at, q.Name, q.Obj, q.Template, q.Icon));
            at++;
        }

        Console.WriteLine($"  -> StatGroup Quests: {questsActive.Count} active, " +
                          $"{questsDone.Count} completed, {rows.Count} rows");
        Send(ns, ctrl, new StatGroupMessage(StatGroup.Quests, rows.ToArray()));
    }

    static (uint Offer, uint Template, uint Obj, uint Name, uint Icon) Find(uint template)
    {
        foreach (var q in questBook) if (q.Template == template) return q;
        return questBook[0];
    }

    /// <summary>
    /// M59_QUESTLOG: what a ReqTriggerQuest does to the quest state, and
    /// the unasked group 5 that follows it.
    ///
    /// A quest the player does not have starts: AddCurrentQuest appends
    /// it and ends in Send(self,@ToCliStats,#group=5)
    /// (`user.kod:10204-10207`). Triggering the same quest again finishes
    /// it: RemoveCurrentQuest takes it off the active list and Posts the
    /// same group - posted, so plQuestHistory has updated first and the
    /// quest comes back under the completed header rather than vanishing
    /// (`:10236-10241`). Those two and the BP_SEND_STATS reply at `:1824`
    /// are the only places group 5 is built, which is why a log that
    /// could only ever be asked for had no way to show a change.
    ///
    /// Nothing here invents a progress BAR: an active row's four fields
    /// are the quest's name, its object, its template id and its icon
    /// (`SendActiveQuestData`), none of which move as a quest runs. What
    /// moves is which list the quest is in.
    /// </summary>
    static void TriggerQuest(NetworkStream ns, MessageControllerClient ctrl, uint offerId)
    {
        uint template = 0;
        foreach (var q in questBook) if (q.Offer == offerId) template = q.Template;
        if (template == 0)
        {
            Console.WriteLine($"  (no quest {offerId} to trigger)");
            return;
        }

        if (!questsActive.Contains(template) && !questsDone.Contains(template))
        {
            questsActive.Add(template);
            Console.WriteLine($"  -> quest {template} started (AddCurrentQuest)");
        }
        else if (questsActive.Contains(template))
        {
            questsActive.Remove(template);
            questsDone.Add(template);
            Console.WriteLine($"  -> quest {template} completed (RemoveCurrentQuest + history)");
        }
        else
        {
            // Already in the history. The real server will not re-run a
            // finished quest template for the same player, and the log
            // does not change, so neither does this.
            Console.WriteLine($"  (quest {template} is already completed)");
            return;
        }

        SendQuestLog(ns, ctrl);
    }

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
            // Two stacks of shillings rather than one, because the purse
            // adds stacks up and a single stack would not show whether
            // it does.
            Carry(8011, RID_COINBGF, RID_SHILLING, 240, false),
            Carry(8012, RID_COINBGF, RID_SHILLING, 67,  false),
            // Count 0 is NOT STACKABLE, which for a coin means one of
            // it - the case the purse has to read as 1 and not as 0.
            Carry(8013, RID_COINBGF, RID_PLATINUM, 0,   false),
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
        // Through LootItem, so M59_FLOOREQUIP reaches the FIRST list the
        // window is built from and not only the re-sends.
        var all = new[]
        {
            LootItem(3001, RID_COINBGF, RID_COIN, 17),
            LootItem(3002, RID_BOOKBGF, RID_BOOK, 1),
            LootItem(3003, RID_AXEBGF,  RID_AXE,  1),
        };

        var left = new List<ObjectBase>();
        for (int i = 0; i < Math.Clamp(count, 0, all.Length); i++) left.Add(all[i]);

        // M59_NEWSROW: the news globe is in the box as well as on the
        // floor, which is the only route a scripted run has to a ReqLook
        // on it - a held row press sends one (`LootPanel.cs:438`), while
        // the globe and the board themselves stand at the avatar's feet
        // where neither a tap nor @obj: reaches a pixel of them. The
        // answer is the same LookNewsGroup either way; nothing about the
        // news path itself is faked.
        if (wantNewsRow) left.Add(Item(3104, RID_BOOKBGF, RID_GLOBE, 0));

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
    /// flag word, and the fixture used to hand over one impossible set -
    /// a guildmaster's, with RENOUNCE as well as DISBAND - so every rank
    /// but that one was untestable and the panel was only ever seen in a
    /// state no player can be in. M59_GUILD picks the rank now and the
    /// word is built from the real thresholds; see FlagsForRank and the
    /// switch block at the top of the file.
    ///
    /// The ranks are the guild's own strings, five per gender, and a
    /// member's gender picks the column - so Alice reads as a Sister
    /// where Boris reads as a Brother at the same rank.
    /// </summary>
    static void SendGuild(NetworkStream ns, MessageControllerClient ctrl)
    {
        // The flag word the fixture always sent: RENOUNCE and DISBAND at
        // once, which no rank ever holds. Kept, as M59_GUILD=both, so a
        // run can show what every earlier audit was looking at.
        const uint GUILDBOTH =
            GCID_EXILE |
            GCID_RENOUNCE |
            GCID_VOTE |
            GCID_ABDICATE |
            GCID_SET_RANK |
            GCID_DISBAND |
            GCID_ABANDON_HALL;

        string mode = GuildMode;

        // No guild at all: the server says so in a line and sends no
        // GuildInfo (`user.kod:2749-2753`). Every panel that reads
        // GuildInfo is then reading an empty model, which is the state a
        // guildless player is in for the whole session.
        if (mode == "none")
        {
            Console.WriteLine("  -> (no guild: MsgSendUser, no GuildInfo)");
            Say(ns, ctrl, RID_NOGUILD);
            return;
        }

        int rank = mode switch
        {
            "member" => 1,
            "lieutenant" => 4,
            _ => 5,            // master, solo, both
        };

        var members = new List<GuildMemberEntry>
        {
            new GuildMemberEntry(1001, 0, "Tester", (byte)rank, Gender.Male),
        };
        if (mode != "solo")
        {
            members.Add(new GuildMemberEntry(4001, 0, "Alice", 3, Gender.Female));
            members.Add(new GuildMemberEntry(4002, 0, "Boris the Outlaw", 1, Gender.Male));
        }

        uint flags = mode == "both" ? GUILDBOTH : FlagsForRank(rank);
        string raw = EnvStr("M59_GUILD_FLAGS");
        if (raw != null) flags = ParseWord(raw, flags);

        // The hall is not on the wire. The password byte and the chest
        // password go out only to a RANK_MASTER of a guild that has one
        // (`user.kod:2756-2765`); everyone else gets a 0 whether the
        // guild owns a hall or not, so that byte is the client's whole
        // evidence and a member can never be told the truth.
        bool hall = EnvInt("M59_GUILD_HALL", 1) != 0;
        byte passwordFlag = (byte)(hall && rank == 5 ? 1 : 0);

        // Whose row the supported-member field names. The server writes
        // the third element of YOUR OWN row, and 0 when you support
        // nobody (`user.kod:2775-2790`).
        var supported = new ObjectID(VotedFor(members), 0);

        var info = new GuildInfo(
            "The Quiet Hand",
            passwordFlag,
            passwordFlag == 1 ? "rats" : "",
            new GuildFlags(flags),
            new ObjectID(9001, 0),
            "Novice",  "Novice",
            "Brother", "Sister",
            "Elder",   "Matron",
            "Warden",  "Warden",
            "Master",  "Mistress",
            supported,
            members.ToArray());

        Console.WriteLine($"  -> GuildInfo as {mode} (rank {rank}), flags 0x{flags:X4}, " +
                          $"password byte {passwordFlag}, supporting {supported.ID}, " +
                          $"{members.Count} member(s)");
        Send(ns, ctrl, new UserCommandMessage(new UserCommandGuildInfo(info), strings));
    }

    // The guild command bits, as the server names them
    // (`kod/include/blakston.khd:2956-2971`). PROMOTE and DEMOTE have no
    // command object in Server-104's guildcmd/ at all, so no rank ever
    // holds them and they are not here.
    const uint GCID_INVITE = 0x0001;
    const uint GCID_EXILE = 0x0002;
    const uint GCID_RENOUNCE = 0x0004;
    const uint GCID_VOTE = 0x0020;
    const uint GCID_ABDICATE = 0x0040;
    const uint GCID_ROSTER = 0x0080;
    const uint GCID_FORGE_ALLIANCE = 0x0100;
    const uint GCID_END_ALLIANCE = 0x0200;
    const uint GCID_DECLARE_ENEMY = 0x0400;
    const uint GCID_PEACE = 0x0800;
    const uint GCID_SET_RANK = 0x1000;
    const uint GCID_DISBAND = 0x2000;
    const uint GCID_ABANDON_HALL = 0x4000;
    const uint GCID_SET_PASSWORD = 0x8000;

    /// <summary>
    /// The flag word a player of that rank really holds.
    ///
    /// Every guild command is an object with a viRank_needed, and
    /// ResetCommand adds its bit at or above that rank and removes it
    /// below (`kod/object/passive/guildcmd.kod:72-95`). The thresholds
    /// are each command's own classvar: VOTE at RANK_APPRENTICE,
    /// INVITE and ROSTER at RANK_LORD, EXILE / SET_RANK / ABANDON_HALL
    /// and the four diplomacy rights at RANK_LIEUTENANT, ABDICATE /
    /// DISBAND / SET_PASSWORD at RANK_MASTER (gcpword.kod sets no
    /// viRank_needed and so inherits guildcmd.kod's RANK_MASTER).
    ///
    /// RENOUNCE is the exception and the reason the old fixture word was
    /// impossible: gcrennce.kod overrides ResetCommand to REMOVE the bit
    /// at RANK_MASTER and add it at every other rank
    /// (`guildcmd/gcrennce.kod:57-69`), so RENOUNCE and DISBAND are
    /// mutually exclusive on a real player.
    /// </summary>
    static uint FlagsForRank(int rank)
    {
        uint f = 0;
        if (rank >= 1) f |= GCID_VOTE;
        if (rank >= 3) f |= GCID_INVITE | GCID_ROSTER;
        if (rank >= 4) f |= GCID_EXILE | GCID_SET_RANK | GCID_ABANDON_HALL
                          | GCID_FORGE_ALLIANCE | GCID_END_ALLIANCE
                          | GCID_DECLARE_ENEMY | GCID_PEACE;
        if (rank >= 5) f |= GCID_ABDICATE | GCID_DISBAND | GCID_SET_PASSWORD;
        if (rank != 5) f |= GCID_RENOUNCE;
        return f;
    }

    /// <summary>0x-prefixed hex or plain decimal; anything else keeps the default.</summary>
    static uint ParseWord(string text, uint dflt)
    {
        if (text == null) return dflt;
        text = text.Trim();
        try
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(text.Substring(2), 16);
            return uint.Parse(text);
        }
        catch { return dflt; }
    }

    /// <summary>
    /// M59_GUILD_VOTE: which member's id goes in the supported-member
    /// field. A name, matched against the roster, or "none" for the 0 the
    /// server writes when you support nobody (`user.kod:2779-2783`).
    /// </summary>
    static uint VotedFor(List<GuildMemberEntry> members)
    {
        // Unset is Alice, as before - but only while she is on the roster:
        // a solo guild has nobody else, and the server can only name a row
        // it actually sent.
        string want = EnvStr("M59_GUILD_VOTE") ?? "Alice";
        if (want.Equals("none", StringComparison.OrdinalIgnoreCase)) return 0;

        foreach (GuildMemberEntry m in members)
            if (m.Name != null && m.Name.Equals(want, StringComparison.OrdinalIgnoreCase))
                return m.ID;

        if (EnvStr("M59_GUILD_VOTE") != null)
            Console.WriteLine($"     (M59_GUILD_VOTE=\"{want}\" is not on the roster; supporting nobody)");
        return 0;
    }

    /// <summary>
    /// M59_GUILD_HALLS: the halls a broker has on offer.
    ///
    /// `SendBuyGuildHall` counts the halls this player could afford and
    /// writes that count and then one row each - id, name resource,
    /// purchase value, and the rent times 24 (`user.kod:7670-7700`). A
    /// count of 0 is a legitimate answer: every hall is taken, or none is
    /// affordable. Nothing the client sends asks for this list, so
    /// ReqGuildList carries it when the switch is set.
    /// </summary>
    static void SendGuildHalls(NetworkStream ns, MessageControllerClient ctrl, int count)
    {
        var all = new[]
        {
            Hall(11, RID_HALL1, "Hall of the Broken Wheel", 15000, 250),
            Hall(12, RID_HALL2, "Marion Guild House", 40000, 900),
            Hall(13, RID_HALL3, "Tos Guild Tower", 125000, 2400),
        };

        var info = new GuildHallsInfo();
        for (int i = 0; i < Math.Clamp(count, 0, all.Length); i++) info.GuildHalls.Add(all[i]);

        Console.WriteLine($"  -> GuildHalls ({info.GuildHalls.Count} halls)");
        Send(ns, ctrl, new UserCommandMessage(new UserCommandGuildHalls(info), strings));
    }

    static GuildHall Hall(uint id, uint nameRid, string name, uint cost, uint rentPerHour)
    {
        // The name travels as a resource id and the client resolves it
        // (`DataController.ResolveStringResources` :1308-1309); Name is
        // set as well so the server's own log line reads.
        return new GuildHall(name, id)
        {
            NameRID = nameRid,
            Cost = cost,
            // What the server writes is 24 * the hall's rent value.
            Rent = rentPerHour * 24,
        };
    }

    /// <summary>
    /// M59_GUILD_ASK / the trigger's `ask`: the two prices founding a
    /// guild costs. A broker sends this unasked (`user.kod:7703-7712`,
    /// SendCreateGuild), so there is no request to answer and it can only
    /// arrive on a timer or a trigger - which is exactly the push that
    /// lands on top of an open popup.
    /// </summary>
    static void SendGuildAsk(NetworkStream ns, MessageControllerClient ctrl)
    {
        Console.WriteLine("  -> GuildAsk (25000 / 75000)");
        Send(ns, ctrl, new UserCommandMessage(
            new UserCommandGuildAsk(new GuildAskData(25000, 75000)), strings));
    }

    /// <summary>
    /// M59_GUILD_SHIELD: the designer's two requests answered.
    ///
    /// The sample list is every shield design the server knows
    /// (`user.kod:2860-2881`, one resource id each) and is sent every
    /// time it is asked for. The shield itself (`:2883-2925`) carries the
    /// owning guild's id - 0 when the design is unclaimed - its name and
    /// the three bytes of the design, and is sent ONCE here: the second
    /// open gets no reply, so whatever the panel kept from the first is
    /// what is on screen, which is how a stale designer shows up.
    /// </summary>
    static void SendShieldSamples(NetworkStream ns, MessageControllerClient ctrl)
    {
        var samples = new[]
        {
            new ResourceIDBGF(RID_BOOKBGF),
            new ResourceIDBGF(RID_COINBGF),
            new ResourceIDBGF(RID_AXEBGF),
            new ResourceIDBGF(RID_RATBGF),
        };
        Console.WriteLine($"  -> GuildShieldList ({samples.Length} designs)");
        Send(ns, ctrl, new UserCommandMessage(
            new UserCommandGuildShieldList(samples), strings));
    }

    static void SendShield(NetworkStream ns, MessageControllerClient ctrl,
                           byte c1, byte c2, byte design, uint owner)
    {
        Console.WriteLine($"  -> GuildShield owner {owner} colours {c1}/{c2} design {design}");
        Send(ns, ctrl, new UserCommandMessage(
            new UserCommandGuildShieldInfo(new GuildShieldInfo(
                new ObjectID(owner, 0), "The Quiet Hand", c1, c2, design)), strings));
    }

    static void SendShieldError(NetworkStream ns, MessageControllerClient ctrl)
    {
        Console.WriteLine("  -> GuildShieldError");
        Send(ns, ctrl, new UserCommandMessage(
            new UserCommandGuildShieldError(Line(RID_SHIELDERR), strings), strings));
    }

    /// <summary>
    /// M59_SHIELD_ERROR / a ClaimShield. The body is the command byte and
    /// then colour, colour, design and the really-claim byte
    /// (`UserCommandClaimShield.ReadFrom`); the server looks the design up
    /// and answers with the shield, naming the guild that owns it or 0 if
    /// nobody does (`user.kod:2627-2659`). A claim with really-claim set,
    /// by a master, of an unclaimed design takes it - so the answer names
    /// your own guild from then on.
    /// </summary>
    static void ClaimShield(NetworkStream ns, MessageControllerClient ctrl, byte[] body)
    {
        byte c1 = body.Length > 2 ? body[2] : (byte)0;
        byte c2 = body.Length > 3 ? body[3] : (byte)0;
        byte design = body.Length > 4 ? body[4] : (byte)0;
        byte really = body.Length > 5 ? body[5] : (byte)0;
        Console.WriteLine($"  <- ClaimShield {c1}/{c2} design {design} really {really}");

        if (EnvOn("M59_SHIELD_ERROR")) { SendShieldError(ns, ctrl); return; }

        // Design 1 is already somebody else's, which is what makes the
        // claimed and unclaimed branches both reachable in one run.
        uint owner = design == 1 ? 9002u : (really != 0 ? 9001u : 0u);
        SendShield(ns, ctrl, c1, c2, design, owner);
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

    // ===================================================================
    // USING THINGS, LISTS THAT CHANGE, PREFERENCES, PASSWORDS, AND THE
    // ONE TRIGGER THEY ALL HANG OFF. See the switch block at the top for
    // what each of these stands in for.
    // ===================================================================

    /// <summary>OF_APPLYABLE (`blakston.khd:71`), the bit that turns the bag's Use button into Apply.</summary>
    const uint OF_APPLYABLE = 0x00001000;

    /// <summary>What the pack is currently wearing, so Use and Unuse answer honestly.</summary>
    static readonly HashSet<uint> inUseNow = new HashSet<uint>();

    /// <summary>
    /// M59_USE_LIST: the ids the UseList on a ReqInventory names. The
    /// client has no other way to learn that something is already worn -
    /// `Carry`'s inUse argument is not on the wire at all, only the
    /// EQUIPPED flag is - so without this message IsInUse is false for
    /// everything, all session (`DataController.cs:2477-2490`).
    /// </summary>
    static void SendUseList(NetworkStream ns, MessageControllerClient ctrl)
    {
        string spec = EnvStr("M59_USE_LIST") ?? "8001";
        var ids = new List<ObjectID>();
        if (!spec.Equals("none", StringComparison.OrdinalIgnoreCase))
            foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (uint.TryParse(part.Trim(), out uint id)) { ids.Add(new ObjectID(id)); inUseNow.Add(id); }

        Console.WriteLine($"  -> UseList ({ids.Count}: {string.Join(" ", ids.ConvertAll(o => o.ID.ToString()))})");
        Send(ns, ctrl, new UseListMessage(ids.ToArray()));
    }

    /// <summary>
    /// A use or an unuse, confirmed. The real server answers a ReqUse by
    /// wielding the item and sending BP_USE back with the id it equipped
    /// (and nothing at all when it refuses), and a ReqUnuse with BP_UNUSE.
    /// Everything downstream of that - the gold border on the slot, the
    /// caption the button carries, a hotbar button that follows a worn
    /// item - hangs on the reply, and the fixture sent neither.
    /// </summary>
    static void UseItem(NetworkStream ns, MessageControllerClient ctrl, uint id, bool on)
    {
        if (on) inUseNow.Add(id); else inUseNow.Remove(id);
        Console.WriteLine($"  -> {(on ? "Use" : "Unuse")} {id}");
        if (on) Send(ns, ctrl, new UseMessage(new ObjectID(id)));
        else Send(ns, ctrl, new UnuseMessage(new ObjectID(id)));

        if (on) UseChange(ns, ctrl, id, EnvStr("M59_USE_CHANGE"));
    }

    /// <summary>
    /// M59_USE_CHANGE: the item changes UNDER the open bag, in place, the
    /// moment it is worn. A real server does this constantly - a magic
    /// item identifies itself on use and comes back with a new name, and
    /// what an item can do changes with what is in your hand - and the
    /// library applies all three forms to the SAME InventoryObject rather
    /// than replacing it (`DataController.cs:2288-2292` through
    /// NextUpdate, `:2139-2143`). A panel that only re-reads on a new
    /// instance therefore keeps the old caption, which is 07f2920.
    /// </summary>
    static void UseChange(NetworkStream ns, MessageControllerClient ctrl, uint id, string mode)
    {
        if (mode == null) return;

        uint name = mode == "rename" ? RID_AXEBLESSED : RID_AXE;
        uint flags = OF_EQUIPPED | (mode == "rename" ? 0u : OF_APPLYABLE);

        if (mode == "flags")
        {
            // Server-104's own flags-only packet. The client's handler for
            // it is compiled out under VANILLA and OPENMERIDIAN
            // (`DataController.cs:2125-2143`); this build defines neither.
            Console.WriteLine($"  -> ChangeObjectFlags {id} flags 0x{flags:X4}");
            Send(ns, ctrl, new ChangeObjectFlagsMessage(new ObjectFlagsUpdate(id, flags)));
            return;
        }

        // A full ObjectUpdate, which is what a Change carries. The
        // constructor leaves LightingInfo and Animation null, and a
        // message built without them is malformed and silently dropped -
        // that cost an audit a day once, and the note in fake-server.md
        // about "the client ignores a hand-built ChangeMessage" is this.
        var upd = new ObjectUpdate(
            id, 0, RID_AXEBGF, name, flags,
            new LightingInfo(),
            AnimationType.NONE, 0, 0, new AnimationNone(), new List<SubOverlay>(),
            AnimationType.NONE, 0, 0, new AnimationNone(), new List<SubOverlay>());
        Console.WriteLine($"  -> Change {id}: name {name}, flags 0x{flags:X4} ({mode})");
        Send(ns, ctrl, new ChangeMessage(upd));
    }

    /// <summary>
    /// M59_LOOT: the container's list changes while its window is open.
    ///
    /// counts  the same three items at new counts - including a stack of
    ///         one, which the reference still labels (`ObjectID.cs:202-205`)
    /// shrink  two items instead of three
    /// empty   no items at all, the state the window is meant to close on
    /// remove  the middle item gone, the others unchanged - which is what
    ///         leaves a tick pointing at an object that has left the room
    /// A real server has only one shape for any of this: the whole list,
    /// re-sent. Both loot bugs in 6970025 need exactly that.
    /// </summary>
    static void LootChange(NetworkStream ns, MessageControllerClient ctrl, string mode)
    {
        mode ??= "counts";
        var items = new List<ObjectBase>();
        switch (mode)
        {
            case "shrink":
                items.Add(LootItem(3001, RID_COINBGF, RID_COIN, 9));
                items.Add(LootItem(3002, RID_BOOKBGF, RID_BOOK, 1));
                break;
            case "empty":
                break;
            case "remove":
                items.Add(LootItem(3001, RID_COINBGF, RID_COIN, 17));
                items.Add(LootItem(3003, RID_AXEBGF, RID_AXE, 1));
                break;
            default:
                items.Add(LootItem(3001, RID_COINBGF, RID_COIN, 9));
                items.Add(LootItem(3002, RID_BOOKBGF, RID_BOOK, 1));
                items.Add(LootItem(3003, RID_AXEBGF, RID_AXE, 1));
                break;
        }

        Console.WriteLine($"  -> ObjectContents ({mode}): {items.Count} items " +
                          string.Join(" ", items.ConvertAll(o => $"{o.ID}x{o.Count}")));
        Send(ns, ctrl, new ObjectContentsMessage(new ObjectID(3101, 0), items.ToArray()));
    }

    /// <summary>
    /// A loot row, with M59_FLOOREQUIP putting the EQUIPPED bit on the
    /// axe. Server-104 never sends that bit - kod keeps 0x8000 free
    /// (`blakston.khd:2739`) - but the library and the other flavours do
    /// (`ObjectFlags.cs:65`), and the contents window is where it reads
    /// as " (in use)" after a name.
    /// </summary>
    static ObjectBase LootItem(uint id, uint bgfRid, uint nameRid, uint count)
    {
        uint flags = wantFloorEquip && id == 3003 ? OF_EQUIPPED : 0u;
        return new ObjectBase(
            id, count, bgfRid, nameRid, flags,
            new LightingInfo(),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new List<SubOverlay>());
    }

    /// <summary>
    /// M59_PREFS: the preference word, answered.
    ///
    /// Until it is, `PreferencesFlags.Enabled` is false for the whole
    /// session and every server-side option is disabled - the client has
    /// no word to toggle a bit in. The server answers both the request
    /// and a set, because a set ENDS in the same reply
    /// (`user.kod:2405-2409`): the echo is how the client learns which
    /// bits were refused, and M59_PREFS_REFUSE is a server that refuses
    /// some.
    /// </summary>
    static void SendPrefs(NetworkStream ns, MessageControllerClient ctrl)
    {
        Console.WriteLine($"  -> ReceivePreferences 0x{prefsWord:X2}");
        Send(ns, ctrl, new UserCommandMessage(
            new UserCommandReceivePreferences(new PreferencesFlags(prefsWord)), strings));
    }

    /// <summary>
    /// A set: the word the client sent, less the bits this server will
    /// not grant, then the echo. The body is the command byte and four
    /// bytes of flags (`UserCommandSendPreferences`), which kod reads as
    /// Nth(client_msg,2) (`user.kod:2435-2437`).
    /// </summary>
    static void SetPrefs(NetworkStream ns, MessageControllerClient ctrl, byte[] body)
    {
        uint asked = body.Length >= 6 ? BitConverter.ToUInt32(body, 2) : prefsWord;
        uint refuse = ParseWord(EnvStr("M59_PREFS_REFUSE") ?? "0", 0);
        prefsWord = asked & ~refuse;
        Console.WriteLine($"  <- SendPreferences 0x{asked:X2}" +
                          (refuse != 0 ? $", refusing 0x{refuse:X2} -> 0x{prefsWord:X2}" : ""));
        SendPrefs(ns, ctrl);
    }

    /// <summary>
    /// M59_PREFS_DELAY: the FIRST answer, held back. The client asks the
    /// moment the character is accepted and again after login
    /// (`BaseClient.cs:565-572`, `:2765-2772`), so the window where the
    /// status bar has no preference word to read is a real one and this
    /// is how wide it gets.
    /// </summary>
    static void AnswerPrefsRequest(NetworkStream ns, MessageControllerClient ctrl)
    {
        if (prefsAnswered || prefsDelayMs <= 0) { prefsAnswered = true; SendPrefs(ns, ctrl); return; }

        prefsAnswered = true;
        Console.WriteLine($"     (holding the preference word back {prefsDelayMs} ms)");
        var t = new Timer(_ => { try { SendPrefs(ns, ctrl); } catch { } },
                          null, prefsDelayMs, Timeout.Infinite);
        gapTimers.Add(t);
    }

    // --- The trigger ----------------------------------------------------

    /// <summary>
    /// Reads M59_FIRE and starts the M59_TRIGGER poller. Called once, when
    /// the first client reaches game mode.
    /// </summary>
    static void StartTrigger()
    {
        if (guildFire != null)
            foreach (string part in guildFire.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.IndexOf(':');
                if (colon > 0 && int.TryParse(part.Substring(0, colon).Trim(), out int at))
                    firePlan.Add((at, part.Substring(colon + 1).Trim()));
                else Console.WriteLine($"     (M59_FIRE: cannot read \"{part}\")");
            }
        // The two switches that name their own moment in messages go into
        // the same plan, so there is one timer and one place that fires.
        if (guildAskAfter > 0) firePlan.Add((guildAskAfter, "ask"));
        if (lootAfter > 0) firePlan.Add((lootAfter, "loot"));

        if (firePlan.Count > 0)
            Console.WriteLine($"M59_FIRE: {firePlan.Count} timed commands, counted in client messages");

        if (triggerPath == null) return;

        // Polled rather than watched: a FileSystemWatcher misses an append
        // that lands in the same tick as the one before it, and the whole
        // point of this is that the moment matters. The lines are queued
        // and run on the session thread, so the trigger never writes to
        // the socket itself.
        try { File.WriteAllText(triggerPath, ""); } catch { }
        Console.WriteLine($"M59_TRIGGER: polling {triggerPath} every 200 ms");
        long seen = 0;
        var poll = new Timer(_ =>
        {
            try
            {
                var fi = new FileInfo(triggerPath);
                if (!fi.Exists || fi.Length <= seen) return;
                using var fs = new FileStream(triggerPath, FileMode.Open,
                                              FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(seen, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                string rest = sr.ReadToEnd();
                seen = fs.Position;
                foreach (string line in rest.Split('\n'))
                {
                    string cmd = line.Trim();
                    if (cmd.Length == 0 || cmd.StartsWith("#")) continue;
                    lock (fired) fired.Enqueue(cmd);
                    Console.WriteLine($"  trigger: {cmd}");
                }
            }
            catch { }
        }, null, 200, 200);
        gapTimers.Add(poll);
    }

    /// <summary>
    /// Runs whatever the trigger has handed over and whatever M59_FIRE has
    /// reached, once per client message. Counted in CLIENT MESSAGES, which
    /// is the convention every timed switch in this file uses: pings
    /// outpace frames under load, so a millisecond deadline fires before
    /// the frame that was supposed to show the result.
    /// </summary>
    static void Triggers(NetworkStream ns, MessageControllerClient ctrl)
    {
        clientMsgs++;

        for (int i = firePlan.Count - 1; i >= 0; i--)
            if (firePlan[i].At == clientMsgs)
            {
                string cmd = firePlan[i].Cmd;
                firePlan.RemoveAt(i);
                Console.WriteLine($"  fire ({clientMsgs}): {cmd}");
                RunTrigger(ns, ctrl, cmd);
            }

        while (true)
        {
            string cmd;
            lock (fired) { if (fired.Count == 0) break; cmd = fired.Dequeue(); }
            RunTrigger(ns, ctrl, cmd);
        }
    }

    /// <summary>
    /// One trigger command. The vocabulary is deliberately small and the
    /// same for the file and for M59_FIRE, so "at this moment, do that"
    /// is written once however the moment is chosen.
    /// </summary>
    static void RunTrigger(NetworkStream ns, MessageControllerClient ctrl, string line)
    {
        string[] parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
        string verb = parts[0].ToLowerInvariant();
        string arg = parts.Length > 1 ? parts[1] : null;

        switch (verb)
        {
            case "info": SendGuild(ns, ctrl); break;
            case "list": SendGuildList(ns, ctrl); break;
            case "halls":
                SendGuildHalls(ns, ctrl,
                    int.TryParse(arg, out int n) ? n : EnvInt("M59_GUILD_HALLS", 3));
                break;
            case "ask": SendGuildAsk(ns, ctrl); break;
            case "shield": SendShield(ns, ctrl, 10, 20, 2, 9001); break;
            case "shielderr": SendShieldError(ns, ctrl); break;
            case "samples": SendShieldSamples(ns, ctrl); break;
            case "prefs": SendPrefs(ns, ctrl); break;
            case "bag": SendBag(ns, ctrl); break;
            case "loot": LootChange(ns, ctrl, arg ?? EnvStr("M59_LOOT")); break;
            // The NPC's offer, sent again. With M59_NPCQ=changing that is
            // a DIFFERENT list each time, which is the only way to reach
            // the panel's rebuild while it is open: the client asks once,
            // when the Quest button is pressed, and the button is behind
            // the window from then on. A real server pushes QuestUIList
            // whenever the offer changes - the client never asks twice
            // either - so the unasked re-send is its shape, not a
            // convenience.
            case "quests": SendNPCQuests(ns, ctrl); break;
            case "uselist": SendUseList(ns, ctrl); break;
            case "use":
                if (uint.TryParse(arg, out uint uid)) UseItem(ns, ctrl, uid, true);
                break;
            case "unuse":
                if (uint.TryParse(arg, out uint xid)) UseItem(ns, ctrl, xid, false);
                break;
            case "change":
            {
                string[] a = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string mode = a.Length > 0 ? a[0] : EnvStr("M59_USE_CHANGE") ?? "rename";
                uint id = a.Length > 1 && uint.TryParse(a[1], out uint cid) ? cid : 8001;
                UseChange(ns, ctrl, id, mode);
                break;
            }
            case "say": Say(ns, ctrl, RID_ECHO); break;
            case "env":
            {
                // Only the switches in this block are read at use time, so
                // only they can be changed mid-session; the older
                // `static readonly` ones cannot and are not pretended to be.
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0) { Console.WriteLine("     (env wants NAME=VALUE)"); break; }
                Environment.SetEnvironmentVariable(arg.Substring(0, eq), arg.Substring(eq + 1));
                Console.WriteLine($"     (env {arg})");
                break;
            }
            default:
                Console.WriteLine($"     (trigger: no command \"{verb}\")");
                break;
        }
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

    /// <summary>How many times ReqNPCQuests has been answered, for M59_NPCQ=changing.</summary>
    static int npcQuestAsks;

    static void SendNPCQuests(NetworkStream ns, MessageControllerClient ctrl)
    {
        var all = new[]
        {
            Quest(8002, RID_QUEST2, ObjectFlags.PlayerType.QuestValid,   RID_QDESC2, RID_QREQ2),
            Quest(8003, RID_QUEST3, ObjectFlags.PlayerType.QuestInvalid, RID_QDESC3, RID_QREQ3),
            Quest(8001, RID_QUEST1, ObjectFlags.PlayerType.QuestActive,  RID_QDESC1, RID_QREQ1),
        };

        QuestObjectInfo[] quests = all;
        npcQuestAsks++;
        switch (npcQuestMode)
        {
            case "empty":
                // A zero-length list. The wire admits one
                // (`QuestUIListMessage.cs:71-79`) and the data layer sets
                // IsVisible after the sort with no count test
                // (`DataController.cs:3034-3056`), so this is an NPC with
                // nothing on offer, not an NPC who refuses to talk - and
                // the client's window for it had never been driven,
                // because every answer carried the same three quests
                // whoever was asked.
                quests = Array.Empty<QuestObjectInfo>();
                break;

            case "changing":
                // A DIFFERENT list every time, so the panel rebuilds
                // instead of its signature comparing equal and the whole
                // path short-circuiting (`NpcQuestsPanel.cs:403`). The
                // three answers in turn: all three, the middle one gone,
                // then one left with new words - which is what an NPC
                // whose offer moved while you were reading it sends.
                switch ((npcQuestAsks - 1) % 3)
                {
                    case 1:
                        quests = new[] { all[0], all[2] };
                        break;
                    case 2:
                        quests = new[]
                        {
                            // Same id, same flags, different text: the
                            // case the signature has to notice through
                            // the description as well as the id.
                            new QuestObjectInfo(all[2].ObjectBase,
                                                Line(RID_QDESC2), Line(RID_QREQ3)),
                        };
                        break;
                }
                break;
        }

        if (npcQuestMode != null)
            Console.WriteLine($"  -> QuestUIList ({npcQuestMode}, ask {npcQuestAsks}): {quests.Length} quests");

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

    /// <summary>
    /// Which slot ids were offered as empty, so NewCharInfo can be
    /// judged against them the way a real server judges it. See the
    /// NewCharInfo arm.
    /// </summary>
    static readonly HashSet<uint> FreeSlots = new HashSet<uint>();

    static void SendCharacters(NetworkStream ns, MessageControllerClient ctrl)
    {
        // One character and one empty slot, which is what a real
        // account looks like: the empty slot is how the creation
        // wizard is reached, and a client that only lists real
        // characters can never get to it.
        var chars = new List<CharSelectItem>();
        // M59_NOCHARS=1 is a BRAND NEW ACCOUNT: empty slots and nothing
        // else. Worth a switch of its own because it is the one shape
        // every player meets exactly once, and the client used to treat
        // it as a special case - skipping the selection screen and
        // asking for the creation palette with no slot named, which the
        // live server refuses. Untestable until now.
        if (Environment.GetEnvironmentVariable("M59_NOCHARS") != "1")
            chars.Add(new CharSelectItem(1001, 1, "Tester", 0));
        // M59_CHARS=n puts n characters on the account.
        if (int.TryParse(Environment.GetEnvironmentVariable("M59_CHARS"), out int many) && many > 1)
            for (int i = 2; i <= many; i++)
                chars.Add(new CharSelectItem((uint)(1000 + i), 1, "Tester" + i, 0));
        // M59_NOSLOT=1 reports an account with no room left, which is
        // the shape that used to skip the selection screen entirely:
        // one character and nowhere to put another. Reproduced here
        // because it is a real server's answer and was not testable.
        //
        // THE IDS ARE NOT ZERO ANY MORE, and that is the point of this
        // change. The empty slot used to be `new CharSelectItem(0, 0,
        // "", 1)` - id zero - so a client that sent slot 0 to create a
        // character was indistinguishable here from a client that sent
        // the right slot, and the one bug that broke creation on the
        // live server (server-104 reads that field as the user object:
        // `kod/util/system.kod:4570`, `:4572`, `:4579`) could not be
        // reproduced. A fixture that answers more nicely than the real
        // thing cannot fail. 2001 upward, and M59_SLOTS=n gives several,
        // so a client that picks "the first empty one" rather than the
        // one tapped is caught too.
        if (Environment.GetEnvironmentVariable("M59_NOSLOT") != "1")
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("M59_SLOTS"), out int free) || free < 1)
                free = 1;
            FreeSlots.Clear();
            for (int i = 0; i < free; i++)
            {
                chars.Add(new CharSelectItem((uint)(2001 + i), 0, "", 1));
                FreeSlots.Add((uint)(2001 + i));
            }
        }
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
    /// Room 1's looping sounds, as SendLoopingSounds sends them
    /// (`room.kod:662-690`): row/col, SOUND_LOOP, no object. M59_LOOPS2 adds
    /// a second entry, which the real list can hold any number of.
    /// </summary>
    static void SendRoom1Loops(NetworkStream ns, MessageControllerClient ctrl)
    {
        Send(ns, ctrl, new PlayWaveMessage(
            new PlaySound(LoopRid, 0, new PlaySound.Flags(1), 11, 12, 0, 100)));
        if (loops2)
            Send(ns, ctrl, new PlayWaveMessage(
                new PlaySound(RID_FXLOOP2, 0, new PlaySound.Flags(1), 12, 12, 0, 100)));
    }

    /// <summary>M59_WADE: the wading sound this room's Player message carries.</summary>
    static uint WadeRid(int roomNo) => wadeDepth > 0 && wadeRoom == roomNo ? RID_FXSPLASH : 0u;

    /// <summary>
    /// M59_WADE: the depth of the sector under the spawn, sent where the real
    /// server sends its flag changes - straight after the Player packet
    /// (`user.kod:3399`, `room.kod:2209-2223`).
    /// </summary>
    static void SendWade(NetworkStream ns, MessageControllerClient ctrl, int roomNo,
                         string file, ushort kx, ushort ky)
    {
        if (WadeRid(roomNo) == 0) return;
        int sid = -1;
        try
        {
            var roo = new RooFile(Path.Combine(dir, file));
            roo.GetHeightAt((kx - 64f) * 16f, (ky - 64f) * 16f, out RooSubSector leaf, false, false);
            if (leaf?.Sector != null) sid = leaf.Sector.ServerID;
        }
        catch (Exception e) { Console.WriteLine($"  !! no sector under the spawn in {file}: {e.GetType().Name}"); }
        if (sid < 0) { Console.WriteLine("  !! M59_WADE: no sector found, no depth sent"); return; }
        Console.WriteLine($"  -> SectorChange sector {sid} depth {wadeDepth} (wading sound FXSplash)");
        Send(ns, ctrl, new SectorChangeMessage(new SectorChange(
            (ushort)sid, (RooSectorFlags.DepthType)wadeDepth, (TextureScrollSpeed)4)));
    }

    /// <summary>
    /// Builds this room's timed sound events from the switches (see the
    /// Sound block); empty, and the clock off, when none is set.
    /// </summary>
    static void PlanSound(bool second)
    {
        sndPlan.Clear();
        sndClock = -1;
        int at = sndAfter;

        if (sndStop == "loop" && !second)
            sndPlan.Add((at, (n, c) => { Console.WriteLine("  -> StopWave FXLoop1 (the loop, by name)");
                Send(n, c, new StopWaveMessage(new StopSound(RID_FXLOOP1, 0))); }));
        if (sndStop == "oneshot" && !second)
            sndPlan.Add((at, (n, c) => { Console.WriteLine("  -> StopWave FXShot (the one-shot, by name)");
                Send(n, c, new StopWaveMessage(new StopSound(RID_FXSHOT, 0))); }));

        if (musicMode == "swap" || musicMode == "repeat" || musicMode == "zero")
            sndPlan.Add((at, (n, c) =>
            {
                uint now = musicNow != 0 ? musicNow : RID_MUSIC;
                uint next = musicMode == "repeat" ? now
                          : musicMode == "zero" ? 0u
                          : (now == RID_FXMUSIC2 ? RID_MUSIC : RID_FXMUSIC2);
                Console.WriteLine($"  -> PlayMusic {(next == 0 ? "0 (no music)" : next.ToString())} ({musicMode})");
                Send(n, c, new PlayMusicMessage(next));
                if (next != 0) musicNow = next;
            }));

        if (sndName == "missing")
        {
            sndPlan.Add((at, (n, c) => { Console.WriteLine("  -> PlayWave FXGone (no such file)");
                Send(n, c, new PlayWaveMessage(new PlaySound(RID_FXGONE, 0, new PlaySound.Flags(0), 0, 0, 0, 100))); }));
            sndPlan.Add((at + 1, (n, c) => { Console.WriteLine("  -> PlayWave FXGone loop (no such file)");
                Send(n, c, new PlayWaveMessage(new PlaySound(RID_FXGONE, 0, new PlaySound.Flags(1), 11, 12, 0, 100))); }));
            sndPlan.Add((at + 2, (n, c) => { Console.WriteLine("  -> PlayMusic FXGoneMusic (no such file)");
                Send(n, c, new PlayMusicMessage(RID_FXGONEMUSIC)); }));
            sndPlan.Add((at + 3, (n, c) => { Console.WriteLine("  -> StopWave FXGone (never started)");
                Send(n, c, new StopWaveMessage(new StopSound(RID_FXGONE, 0))); }));
        }

        if (sndOwner)
        {
            uint owner = second ? 2004u : 2003u;
            sndPlan.Add((at, (n, c) => { Console.WriteLine($"  -> PlayWave FXLoop3 loop owned by {owner}");
                Send(n, c, new PlayWaveMessage(new PlaySound(RID_FXLOOP3, owner, new PlaySound.Flags(1), 0, 0, 0, 100))); }));
            sndPlan.Add((at + 6, (n, c) => { Console.WriteLine($"  -> Remove {owner} (no StopWave)");
                Send(n, c, new RemoveMessage(owner)); }));
        }

        if (sndGhost)
        {
            sndPlan.Add((at, (n, c) => { Console.WriteLine("  -> PlayWave FXLoop3 loop, source 9999 (not in the room)");
                Send(n, c, new PlayWaveMessage(new PlaySound(RID_FXLOOP3, 9999, new PlaySound.Flags(1), 0, 0, 0, 100))); }));
            sndPlan.Add((at + 6, (n, c) => { Console.WriteLine("  -> StopWave FXLoop3 for source 9999");
                Send(n, c, new StopWaveMessage(new StopSound(RID_FXLOOP3, 9999))); }));
        }

        if (sndPlan.Count > 0) sndClock = 0;
    }

    /// <summary>Fires the timed sound events; one tick per client message.</summary>
    static void SoundPlan(NetworkStream ns, MessageControllerClient ctrl)
    {
        if (sndClock < 0) return;
        sndClock++;
        foreach (var e in sndPlan)
            if (e.At == sndClock) e.Do(ns, ctrl);
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
            WadingSoundFileRID: WadeRid(2),
            Flags: 0,
            Depth1: 0, Depth2: 0, Depth3: 0);

        Send(ns, ctrl, new PlayerMessage(info));

        // Straight after BP_PLAYER, as in room 1 (`user.kod:3395-3399`).
        SendWade(ns, ctrl, 2, badRoom ? "" : room2, sx, sy);
        if (room2Snd)
        {
            // The grid square of the spawn: the client takes the middle of
            // (column - 1) * 1024 + 512 room units, and kod = room/16 + 64.
            int col = (int)((sx - 64) * 16) / 1024 + 1, row = (int)((sy - 64) * 16) / 1024 + 1;
            Console.WriteLine($"  -> room 2 loop FXRoom2 at row {row} col {col}");
            Send(ns, ctrl, new PlayWaveMessage(
                new PlaySound(RID_FXROOM2, 0, new PlaySound.Flags(1), row, col, 0, 100)));
        }

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

            // M59_ROOM2SND: the room's own track, after the contents as
            // the real server sends it (`room.kod:2127-2129`).
            if (room2Snd)
            {
                Send(ns, ctrl, new PlayMusicMessage(RID_FXMUSIC2));
                musicNow = RID_FXMUSIC2;
            }
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
        PlanSound(inRoom2);
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
            WadingSoundFileRID: WadeRid(1),
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

        // What the real server sends straight after BP_PLAYER
        // (`user.kod:3395-3399`): looping sounds and sector depth.
        SendWade(ns, ctrl, 1, room, sx, sy);
        if (loopFirst) SendRoom1Loops(ns, ctrl);

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
            // M59_FLOOREQUIP=1 puts the EQUIPPED bit on this one. A thing
            // lying on the floor that something is wearing is a corpse's
            // weapon; Server-104 never sends the bit (`blakston.khd:2739`
            // keeps 0x8000 free) but the library and the other flavours do
            // (`ObjectFlags.cs:65`), and it is what a name gets " (in use)"
            // appended from.
            Obj(3102, RID_COINBGF, RID_COIN, sx - 16, sy + 16, 0f,
                OF_GETTABLE | OF_DISPLAY_NAME | (wantFloorEquip ? OF_EQUIPPED : 0u)),
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
            new PlaySound(ShotRid, 2001, new PlaySound.Flags(0), 0, 0, 0, 100)));
        Send(ns, ctrl, new PlayMusicMessage(Music1Rid));
        musicNow = Music1Rid;

        // A sound with no source object but a grid square, which is how
        // the server places a noise at a spot in the room rather than on
        // a thing, and a looping one at that - the kind a fountain or a
        // fire makes until you leave. The square chosen is next to where
        // the avatar starts, because the game stops mixing a sound past
        // 2000 units and one across the map is silent by design. The stop
        // for it goes out a little later, from the message loop.
        if (!loopFirst) SendRoom1Loops(ns, ctrl);
        // M59_SNDSTOP and the other timed sound switches send their own
        // stop (see PlanSound), so the old fixed one is not sent with them.
        stopAfter = SndSplit ? 0 : sndAfter;

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
            weatherKind = Environment.GetEnvironmentVariable("M59_WEATHER");
            if (weatherKind == "rain" || weatherKind == "snow" || weatherKind == "clear")
                weatherAfter = EnvInt("M59_WEATHER_AFTER", 6);

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
            // M59_BUFFS=n puts n of them on. Two is not a row: the
            // enchantment row runs fourteen wide, and the bug it hid
            // was that past the second icon the row runs under the
            // condition bars - which cannot happen with two.
            if (int.TryParse(Environment.GetEnvironmentVariable("M59_BUFFS"), out int buffs))
                for (uint i = 3; i <= (uint)buffs; i++)
                    Send(ns, ctrl, new AddEnchantmentMessage(BuffType.AvatarBuff,
                        Item(6000 + i, (i % 2) == 0 ? RID_COINBGF : RID_AXEBGF,
                             (i % 2) == 0 ? RID_BUFF1 : RID_BUFF2, 1)));
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
