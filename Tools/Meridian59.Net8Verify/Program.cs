using System;using System.IO;using System.Diagnostics;
using Meridian59.Common;using Meridian59.Common.Enums;
using Meridian59.Files.BGF;using Meridian59.Files.ROO;
using Meridian59.Protocol;using Meridian59.Protocol.Enums;
using Meridian59.Protocol.GameMessages;

static class Verify
{
    static int pass, fail;

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? "  " + detail : "")}");
        if (ok) pass++; else fail++;
    }

    static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (Directory.Exists(args[0])) Assets(args[0]);
            else { Console.WriteLine($"assets: no such directory: {args[0]}\n"); fail++; }
        }
        else Console.WriteLine("assets: skipped (no resource path given)\n");
        Protocol();
        Console.WriteLine($"\n{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }

    static void Assets(string root)
    {
        Console.WriteLine($"assets  {root}");
        var sw = Stopwatch.StartNew();

        int ok = 0, bad = 0; long frames = 0, px = 0;
        foreach (string f in Directory.GetFiles(root, "*.bgf", SearchOption.AllDirectories))
            try { var b = new BgfFile(f); frames += b.Frames.Count; foreach (var fr in b.Frames) px += fr.PixelData.Length; ok++; }
            catch (Exception e) { bad++; if (bad <= 5) Console.WriteLine($"   ! {Path.GetFileName(f)}: {e.GetType().Name} {e.Message}"); }
        Check("BGF", bad == 0, $"{ok} ok / {bad} failed, {frames} frames, {px / 1024 / 1024} MB pixel data");

        int rok = 0, rbad = 0; long walls = 0, sectors = 0;
        foreach (string f in Directory.GetFiles(root, "*.roo", SearchOption.AllDirectories))
            try { var r = new RooFile(f); walls += r.Walls.Count; sectors += r.Sectors.Count; rok++; }
            catch (Exception e) { rbad++; if (rbad <= 5) Console.WriteLine($"   ! {Path.GetFileName(f)}: {e.GetType().Name} {e.Message}"); }
        Check("ROO", rbad == 0, $"{rok} ok / {rbad} failed, {walls} walls, {sectors} sectors");

        Console.WriteLine($"        {sw.ElapsedMilliseconds} ms\n");
    }

    static void Protocol()
    {
        var ctrl = new MessageControllerClient(new StringDictionary());
        GameMessage got = null; string err = null;
        ctrl.MessageAvailable += (s, e) => { got = e.Message; };
        ctrl.HandlerError += (s, e) => { err = "handler error"; };

        Console.WriteLine("protocol  client -> server");
        var login = new LoginMessage("testuser", "testpass", "0123456789abcdef", 5, 0)
        { TransferDirection = MessageDirection.ClientToServer };
        ctrl.SignMessage(login);
        byte[] w = new byte[login.ByteLength];
        login.WriteTo(w, 0);
        Check("LoginMessage serializes", w.Length == login.ByteLength, $"PI={login.PI}, {w.Length} bytes");
        Check("header length written", login.Header.BodyLength > 0, $"body={login.Header.BodyLength}");

        var back = new LoginMessage(w, MessageHeader.Tcp.HEADERLENGTH);
        Check("username round trips", back.Username == "testuser", $"user={back.Username}");

        byte[] md5 = MeridianMD5.ComputeMD5("testpass");
        Check("password hash round trips",
            back.PasswordHash.HASH1 == BitConverter.ToUInt32(md5, 0) &&
            back.PasswordHash.HASH2 == BitConverter.ToUInt32(md5, 4) &&
            back.PasswordHash.HASH3 == BitConverter.ToUInt32(md5, 8) &&
            back.PasswordHash.HASH4 == BitConverter.ToUInt32(md5, 12),
            "MeridianMD5 survives the wire format");

        Console.WriteLine("protocol  server -> client (parser + controller)");
        foreach (GameMessage m in new GameMessage[] {
            new GetLoginMessage(),
            new LoginOKMessage(AccountType.USER, 12345),
            new LoginFailedMessage() })
        {
            m.TransferDirection = MessageDirection.ServerToClient;
            ctrl.SignMessage(m);
            byte[] b = new byte[m.ByteLength];
            m.WriteTo(b, 0);
            got = null; err = null;
            using (var ms = new MemoryStream(b)) ctrl.ReadRecv(ms, b.Length);
            Check(m.GetType().Name,
                err == null && got != null && got.GetType() == m.GetType(),
                err ?? (got == null ? "parser produced nothing" : $"PI={got.PI}, {b.Length} bytes"));
        }

        Console.WriteLine("protocol  split TCP stream");
        var ok2 = new LoginOKMessage(AccountType.USER, 999) { TransferDirection = MessageDirection.ServerToClient };
        ctrl.SignMessage(ok2);
        byte[] full = new byte[ok2.ByteLength];
        ok2.WriteTo(full, 0);
        int cut = full.Length / 2;
        got = null;
        using (var ms = new MemoryStream(full, 0, cut)) ctrl.ReadRecv(ms, cut);
        Check("partial message is buffered", got == null);
        using (var ms = new MemoryStream(full, cut, full.Length - cut)) ctrl.ReadRecv(ms, full.Length - cut);
        Check("reassembled on second chunk", got is LoginOKMessage o && o.SessionID == 999);
    }
}
