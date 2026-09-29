using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Meridian59.Common;
using Meridian59.Common.Enums;
using Meridian59.Data.Models;
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
    const uint RID_BUFF1 = 60070;
    const uint RID_BUFF2 = 60071;
    static int stopAfter;
    const uint RID_RATSOUND = 60080;
    const uint RID_MUSIC = 60081;

    /// <summary>The server's own copy of what it wrote to the string file.</summary>
    static readonly StringDictionary strings = new StringDictionary();

    static string room = "barinn.roo";
    static string dir = "/tmp/res";

    static int Main(string[] args)
    {
        int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 15999;
        if (args.Length > 1) dir = args[1];
        if (args.Length > 2) room = args[2];

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
            new RsbResourceID(RID_BUFF1,      "shielding",        4),
            new RsbResourceID(RID_BUFF2,      "haste",            4),
            // Sound files are named as .wav in the string table and the
            // library swaps the extension to .ogg, which is what is
            // actually on disk.
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
    static bool HasAll(string path, List<RsbResourceID> wanted)
    {
        try
        {
            var have = new RsbFile(); have.Load(path);
            var ids = new HashSet<uint>();
            foreach (RsbResourceID r in have.StringResources) ids.Add(r.ID);
            foreach (RsbResourceID r in wanted) if (!ids.Contains(r.ID)) return false;
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

                case MessageTypeGameMode.ReqLook:
                    // The look reply: the object, what kind of look it is,
                    // the description, and an inscription if it has one.
                    // The client's own LookObject is what goes up on
                    // screen, so this is all the window needs.
                    Console.WriteLine("  <- ReqLook");
                    SendLook(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqGet:
                    // The first get opens the pile; each one after takes
                    // something out of it. This server tracks nothing, so
                    // it just sends a shorter list each time.
                    Console.WriteLine("  <- ReqGet");
                    if (!lootOpen) { lootOpen = true; SendLoot(ns, ctrl, lootLeft); }
                    else { SendLoot(ns, ctrl, --lootLeft); Say(ns, ctrl, RID_ECHO); }
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
                    // The client asks for its stats once it is in the
                    // world. The condition group is the one behind the
                    // bars: hit points, mana, vigor, and vanilla's fourth,
                    // the chance of getting tougher.
                    Console.WriteLine("  <- SendStats");
                    SendConditions(ns, ctrl);
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
                    Console.WriteLine($"  <- game-mode {pi}, ignored");
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
            new StatNumeric(1, 0, 0,  74,  0, 120, 120),   // hit points
            new StatNumeric(2, 0, 0,  38,  0,  90,  90),   // mana
            new StatNumeric(3, 0, 0,  22,  0, 100, 100),   // vigor, low enough to blink
            new StatNumeric(4, 0, 0,  61,  0, 100, 100),   // tougher chance
        };

        Send(ns, ctrl, new StatGroupMessage(StatGroup.Condition, stats));
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
            Spell(5001, RID_SPELL1),
            Spell(5002, RID_SPELL2),
        };
        Send(ns, ctrl, new SpellsMessage(objects));

        var stats = new Stat[]
        {
            new StatList(1, RID_SPELL1, 5001, 63, 0),
            new StatList(2, RID_SPELL2, 5002, 21, 0),
        };
        Send(ns, ctrl, new StatGroupMessage(StatGroup.Spells, stats));
    }

    static void SendSkills(NetworkStream ns, MessageControllerClient ctrl)
    {
        var objects = new[]
        {
            Skill(5101, RID_SKILL1),
            Skill(5102, RID_SKILL2),
        };
        Send(ns, ctrl, new SkillsMessage(objects));

        var stats = new Stat[]
        {
            new StatList(1, RID_SKILL1, 5101, 88, 0),
            new StatList(2, RID_SKILL2, 5102, 40, 0),
        };
        Send(ns, ctrl, new StatGroupMessage(StatGroup.Skills, stats));
    }

    static SpellObject Spell(uint id, uint nameRid)
    {
        return new SpellObject(
            id, 1, 0, nameRid, 0,
            new LightingInfo(),
            AnimationType.NONE, 0, 0,
            new AnimationNone(),
            new List<SubOverlay>(),
            1, 0);
    }

    static SkillObject Skill(uint id, uint nameRid)
    {
        return new SkillObject(
            id, 1, 0, nameRid, 0,
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

        var info = new ObjectInfo(rat, new LookTypeFlags(0), description,
                                  new ServerString(ChatMessageType.SystemMessage));
        Send(ns, ctrl, new LookMessage(info, strings));
    }

    static int lootLeft = 3;
    static bool lootOpen;

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

        Send(ns, ctrl, new ObjectContentsMessage(new ObjectID(2001, 0), left.ToArray()));
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

    static void SendCharacters(NetworkStream ns, MessageControllerClient ctrl)
    {
        var chars = new List<CharSelectItem> { new CharSelectItem(1001, 1, "Tester", 0) };
        var welcome = new WelcomeInfo(chars, new List<CharSelectAd>(), "A fake server. Nothing here is real.");
        Send(ns, ctrl, new CharactersMessage(welcome));
    }

    /// <summary>
    /// Puts the client in the room: where it is, then what is in it.
    /// </summary>
    static void EnterRoom(NetworkStream ns, MessageControllerClient ctrl)
    {
        const uint avatarId = 1001;

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
            AmbientLight: 160,
            AvatarLight: 0,
            BackgroundFileRID: 0,
            WadingSoundFileRID: 0,
            Flags: 0,
            Depth1: 0, Depth2: 0, Depth3: 0);

        Send(ns, ctrl, new PlayerMessage(info));

        // The avatar itself plus something to look at. Positions are in
        // the server's own units, where one unit is sixteen room units
        // and the origin is 64: kod = room/16 + 64. A grid square is 1024
        // room units, so 64 of these. Put the first pair a couple of grid
        // squares off rather than a few units, which stands them on the
        // camera's nose - the first run of this filled the screen with
        // one duskrat.
        var objects = new[]
        {
            Obj(avatarId, RID_PLAYERBGF, RID_PLAYERNAME, 752, 672, 0f, OF_PLAYER),
            // OF_ATTACKABLE, so the minimap has something to colour: the
            // game only puts a dot on things you could fight, players and
            // guildmates. An ordinary item on the floor gets none.
            Obj(2001, RID_RATBGF, RID_RATNAME, 816, 672, 1f, OF_ATTACKABLE, MM_MONSTER),
            Obj(2002, RID_RATBGF, RID_RATNAME, 848, 688, 3f, OF_ATTACKABLE, MM_MONSTER),
            Obj(2003, RID_RATBGF, RID_RATNAME, 880, 656, 2f, OF_ATTACKABLE, MM_MONSTER),
            // Something to open and something to pick up, so the Activate
            // and Loot actions have a target: the library looks for a
            // container or an activatable object near you for the first,
            // and fills its loot list from gettable ones for the second.
            Obj(3001, RID_BOOKBGF, RID_BOOK, 768, 688, 0f, OF_CONTAINER | OF_DISPLAY_NAME),
            Obj(3002, RID_COINBGF, RID_COIN, 736, 688, 0f, OF_GETTABLE | OF_DISPLAY_NAME),

            // Two other players, so the name labels have something to
            // label. This server's flavour - Server 104's - draws a name
            // when OF_DISPLAY_NAME is set and takes the colour from a
            // separate field the server sends, rather than working it out
            // from a player type. MM_PLAYER is what puts them on the map.
            Obj(4001, RID_PLAYERBGF, RID_ALICE, 800, 704, 3f,
                OF_PLAYER | OF_DISPLAY_NAME, MM_PLAYER, NC_PLAYER),
            Obj(4002, RID_PLAYERBGF, RID_BORIS, 800, 640, 3f,
                OF_PLAYER | OF_DISPLAY_NAME, MM_ENEMY, NC_OUTLAW),
        };
        Send(ns, ctrl, new RoomContentsMessage(new ObjectID(1, 0), objects));
        Console.WriteLine($"  -> room {room} with {objects.Length} objects");

        // Nothing is sent to loot yet. A real server sends the contents
        // of something when you open it, and this one answers the Loot
        // button the same way, so the window is not simply always up.
        lootLeft = 3;
        lootOpen = false;

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

        // A couple of enchantments, so the avatar panel has icons to show.
        Send(ns, ctrl, new AddEnchantmentMessage(BuffType.AvatarBuff,
            Item(6001, RID_COINBGF, RID_BUFF1, 1)));
        Send(ns, ctrl, new AddEnchantmentMessage(BuffType.AvatarBuff,
            Item(6002, RID_AXEBGF, RID_BUFF2, 1)));
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
    {
        var chat = new ObjectChatMessage(
            2001, RID_RATBGF, ChatTransmissionType.Normal,
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
        m.TransferDirection = MessageDirection.ServerToClient;
        ctrl.SignMessage(m);
        var b = new byte[m.ByteLength];
        m.WriteTo(b, 0);
        ns.Write(b, 0, b.Length);
        ns.Flush();
        Console.WriteLine($"  -> {m.GetType().Name} ({b.Length} bytes)");
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
