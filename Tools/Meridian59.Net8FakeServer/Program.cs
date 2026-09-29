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
    const uint RID_MAIL1 = 60130;
    const uint RID_MAIL2 = 60131;
    const uint RID_ROOMBUFF1 = 60072;
    const uint RID_ROOMBUFF2 = 60073;
    const uint RID_BUFF1 = 60070;
    const uint RID_BUFF2 = 60071;
    static int stopAfter;
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
            // A mail's subject is not a field on the wire: it is the
            // first line of the body, after a "Subject: " that the
            // client strips back off. So these strings carry it.
            new RsbResourceID(RID_MAIL1,
                "Subject: The cellar again\nAlice says the rats are back. She is not wrong.", 4),
            new RsbResourceID(RID_MAIL2,
                "Subject: Re: your axe\nI have it. Come and get it, or do not.", 4),
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
                    // There is no separate request for a spell's
                    // description: the client sends ReqLook with the
                    // spell's id and the server decides to answer with
                    // LookSpell rather than Look. The ids the spell list
                    // hands out are the ones sent in the Spells message.
                    uint lookAt = body.Length >= 5 ? BitConverter.ToUInt32(body, 1) : 0;
                    Console.WriteLine($"  <- ReqLook {lookAt}");
                    if (lookAt == 5001 || lookAt == 5002) SendLookSpell(ns, ctrl);
                    else SendLook(ns, ctrl);
                    break;

                case MessageTypeGameMode.ReqGet:
                    // The first get opens the pile; each one after takes
                    // something out of it. This server tracks nothing, so
                    // it just sends a shorter list each time.
                    Console.WriteLine("  <- ReqGet");
                    if (!lootOpen) { lootOpen = true; SendLoot(ns, ctrl, lootLeft); }
                    else { SendLoot(ns, ctrl, --lootLeft); Say(ns, ctrl, RID_ECHO); }
                    break;

                case MessageTypeGameMode.UserCommand:
                {
                    // The body is the PI and then the user-command type,
                    // so one byte says which of two dozen commands this
                    // is without parsing the rest.
                    byte cmd = body.Length > 1 ? body[1] : (byte)0;
                    Console.WriteLine($"  <- UserCommand {(UserCommandType)cmd}");
                    if (cmd == (byte)UserCommandType.ReqGuildInfo) SendGuild(ns, ctrl);
                    break;
                }

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
                    // A real server takes the money and hands over the
                    // goods. This just says something, so the exchange
                    // is visible from the client's side.
                    Console.WriteLine("  <- ReqBuyItems");
                    Say(ns, ctrl, RID_ECHO);
                    break;

                case MessageTypeGameMode.ReqOffer:
                case MessageTypeGameMode.ReqCounterOffer:
                    Console.WriteLine($"  <- {(MessageTypeGameMode)pi}");
                    Say(ns, ctrl, RID_ECHO);
                    break;

                case MessageTypeGameMode.AcceptOffer:
                    Console.WriteLine("  <- AcceptOffer");
                    Say(ns, ctrl, RID_ECHO);
                    break;

                case MessageTypeGameMode.CancelOffer:
                    Console.WriteLine("  <- CancelOffer");
                    break;

                case MessageTypeGameMode.SendPlayers:
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
            Quest(2, RID_QUEST1, 9001, 25),
            Quest(3, RID_QUEST2, 9002, 40),
            Quest(4, RID_QHEAD2, 0,    0),
            Quest(5, RID_QUEST3, 9003, 10),
        };

        Send(ns, ctrl, new StatGroupMessage(StatGroup.Quests, quests));
    }

    static StatList Quest(byte num, uint nameRid, uint objectId, uint points)
        => new StatList(num, nameRid, objectId, points, 0);

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
            Carry(8003, RID_COINBGF, RID_COIN, 25, false),
        };

        Send(ns, ctrl, new InventoryMessage(bag));
    }

    static InventoryObject Carry(uint id, uint bgfRid, uint nameRid, uint count, bool inUse)
    {
        return new InventoryObject(
            id, count, bgfRid, nameRid, 0,
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
            // Two of the three rats are here to exercise the drawing
            // types RemoteNode2D switches materials on: one half
            // translucent, one a shadowform. Like PlayerType, Drawing is
            // its own byte on the wire rather than a bit in the flags
            // integer, so it is set on the object afterwards.
            Drawn(Obj(2002, RID_RATBGF, RID_RATNAME, 848, 688, 3f, OF_ATTACKABLE, MM_MONSTER),
                  ObjectFlags.DrawingType.Translucent50),
            Drawn(Obj(2003, RID_RATBGF, RID_RATNAME, 880, 656, 2f, OF_ATTACKABLE, MM_MONSTER),
                  ObjectFlags.DrawingType.Black),
            // Something to open and something to pick up, so the Activate
            // and Loot actions have a target: the library looks for a
            // container or an activatable object near you for the first,
            // and fills its loot list from gettable ones for the second.
            Obj(3101, RID_BOOKBGF, RID_BOOK, 768, 688, 0f, OF_CONTAINER | OF_DISPLAY_NAME),
            Obj(3102, RID_COINBGF, RID_COIN, 736, 688, 0f, OF_GETTABLE | OF_DISPLAY_NAME),
            // Somebody to buy from: AvatarAction.Buy looks for a nearby
            // object flagged OF_BUYABLE and asks it for a stock list.
            Obj(3103, RID_PLAYERBGF, RID_ALICE, 800, 672, 3f,
                OF_BUYABLE | OF_DISPLAY_NAME, MM_PLAYER, NC_PLAYER),

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

        // And two on the room. Same message, different type byte: the
        // data layer files these in RoomBuffs rather than AvatarBuffs,
        // and until now nothing in the client read that list.
        Send(ns, ctrl, new AddEnchantmentMessage(BuffType.RoomBuff,
            Item(6101, RID_BOOKBGF, RID_ROOMBUFF1, 1)));
        Send(ns, ctrl, new AddEnchantmentMessage(BuffType.RoomBuff,
            Item(6102, RID_COINBGF, RID_ROOMBUFF2, 1)));
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
    const uint OF_BUYABLE = 0x00000400;

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
