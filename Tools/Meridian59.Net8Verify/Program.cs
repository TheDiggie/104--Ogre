using System;using System.IO;using System.Diagnostics;
using Meridian59.Common;using Meridian59.Common.Enums;
using Meridian59.Files.BGF;using Meridian59.Files.ROO;
using Meridian59.Protocol;using Meridian59.Protocol.Enums;
using Meridian59.Protocol.GameMessages;
using Meridian59.Files;
using System.Linq;

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
        if (args.Length > 0 && Directory.Exists(args[0])) CaseSensitivity(args[0]);
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

    /// <summary>
    /// Asking for a file by a different casing than the one on disk.
    ///
    /// The resource dictionaries are case-insensitive, so the lookup
    /// succeeds - but the load used to build its path from the name that
    /// was asked for, and opening that path is case-sensitive on Linux and
    /// Android. On Windows this never happens. Anywhere else it threw
    /// FileNotFoundException, which for an Android client means every
    /// object whose casing the server does not match exactly.
    ///
    /// A fresh ResourceManager per attempt matters: asking with the right
    /// casing first loads and caches the file, and every later casing then
    /// finds it in the dictionary without touching the disk. Written the
    /// other way round, this check reports that all is well.
    /// </summary>
    static void CaseSensitivity(string root)
    {
        Console.WriteLine("\ncase sensitivity");

        string mixed = Directory.GetFiles(root, "*.bgf")
            .Select(Path.GetFileName)
            .FirstOrDefault(n => !n.StartsWith("grd") && n != n.ToLowerInvariant());

        if (mixed == null) { Console.WriteLine("  no mixed-case object here, skipped"); return; }

        foreach (string name in new[]{ mixed, mixed.ToLowerInvariant(), mixed.ToUpperInvariant() })
        {
            bool ok;
            try
            {
                var rm = new ResourceManager();
                rm.Init(root, root, root, root, root, root, root);
                ok = rm.GetObject(name) != null;
            }
            catch (Exception e) { ok = false; Console.Write($"  ({e.GetType().Name}) "); }
            Check($"{mixed} asked for as {name}", ok);
        }
    }
}
