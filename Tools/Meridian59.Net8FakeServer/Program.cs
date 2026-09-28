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
            new RsbResourceID(RID_GREETING,   "The duskrat regards you with mild contempt.", 4),
            new RsbResourceID(RID_ECHO,       "The duskrat has nothing to say about that.", 4),
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
            Obj(avatarId, RID_PLAYERBGF, RID_PLAYERNAME, 752, 672, 0f),
            Obj(2001, RID_RATBGF, RID_RATNAME, 816, 672, 1f),   // straight ahead
            Obj(2002, RID_RATBGF, RID_RATNAME, 848, 688, 3f),
            Obj(2003, RID_RATBGF, RID_RATNAME, 880, 656, 2f),
        };
        foreach (var o in objects)
            Console.WriteLine($"     object {o.ID} byteLength {o.ByteLength}");
        var rc = new RoomContentsMessage(new ObjectID(1, 0), objects);
        Console.WriteLine($"     RoomContents byteLength {rc.ByteLength}");
        Send(ns, ctrl, rc);
        Console.WriteLine($"  -> room {room} with {objects.Length} objects");
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

    static RoomObject Obj(uint id, uint bgfRid, uint nameRid, float x, float y, float angleRadians)
    {
        return new RoomObject(
            id, 1, bgfRid, nameRid, 0,
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
