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
        bool already = File.Exists(path);
        if (already) Console.WriteLine($"using the string file already in {dir}");

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
        };

        foreach (RsbResourceID r in stringList)
            FakeServer.strings.TryAdd(r.ID, r.Text, r.Language);

        if (File.Exists(path)) return;

        try
        {
            new RsbFile(stringList).Save(path);
            Console.WriteLine($"wrote {path} with {stringList.Count} strings");
        }
        catch (Exception e) { Console.WriteLine($"could not write {path}: {e.Message}"); }
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

                case MessageTypeGameMode.ReqGet:
                    // A get takes the thing. This server has nothing to
                    // track, so it simply says what happened and sends the
                    // shortened pile back.
                    Console.WriteLine("  <- ReqGet");
                    SendLoot(ns, ctrl, --lootLeft);
                    Say(ns, ctrl, RID_ECHO);
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

    static int lootLeft = 3;

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
            AmbientLight: 0,
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
            Obj(2001, RID_RATBGF, RID_RATNAME, 816, 672, 1f, OF_ATTACKABLE),
            Obj(2002, RID_RATBGF, RID_RATNAME, 848, 688, 3f, OF_ATTACKABLE),
            Obj(2003, RID_RATBGF, RID_RATNAME, 880, 656, 2f, OF_ATTACKABLE),
        };
        Send(ns, ctrl, new RoomContentsMessage(new ObjectID(1, 0), objects));
        Console.WriteLine($"  -> room {room} with {objects.Length} objects");

        // Something to loot, so the loot list has contents to show. A real
        // server sends this when you open a corpse or a chest; there is
        // nothing to open here, so it arrives with the room.
        lootLeft = 3;
        SendLoot(ns, ctrl, lootLeft);
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

    const uint OF_PLAYER = 0x00000004;
    const uint OF_ATTACKABLE = 0x00000008;

    static RoomObject Obj(uint id, uint bgfRid, uint nameRid, float x, float y,
                          float angleRadians, uint flags = 0)
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
