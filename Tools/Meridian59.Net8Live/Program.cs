using System;using System.IO;using System.Net.Sockets;using System.Threading;
using Meridian59.Common;using Meridian59.Protocol;using Meridian59.Protocol.Enums;
using Meridian59.Protocol.GameMessages;

// Live protocol probe. Opens a real socket to a Meridian 59 server and runs
// every byte it receives through the ported core library's MessageController.
//
//   dotnet run --project Tools/Meridian59.Net8Live -- <host> [port]
//
// Logging in is optional and OFF unless you set both environment variables:
//   M59USER / M59PASS      (never pass these as arguments - shell history)
// Without them the probe reads the server's login-mode greeting and exits,
// which is enough to prove the socket, framing and parser all work.

static class Live
{
    static int count;

    static int Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("usage: <host> [port]"); return 2; }
        string host = args[0];
        int port = args.Length > 1 ? int.Parse(args[1]) : 5959;
        string user = Environment.GetEnvironmentVariable("M59USER");
        string pass = Environment.GetEnvironmentVariable("M59PASS");

        var ctrl = new MessageControllerClient(new StringDictionary());
        ctrl.MessageAvailable += (s, e) =>
        {
            count++;
            var m = e.Message;
            Console.WriteLine($"  <- {m.GetType().Name,-28} PI={m.PI,-4} {m.Header.BodyLength} bytes");
            if (m is LoginFailedMessage) Console.WriteLine("     server rejected the login");
            if (m is LoginOKMessage ok) Console.WriteLine($"     LOGIN OK  session={ok.SessionID} account={ok.AccountType}");
        };
        ctrl.HandlerError += (s, e) => Console.WriteLine("  !! handler error");
        ctrl.MismatchMessageLengthFound += (s, e) => Console.WriteLine("  !! LENGTH MISMATCH - framing is wrong");
        ctrl.ProtocolModeChanged += (s, e) => Console.WriteLine($"  == protocol mode -> {ctrl.Mode}");

        Console.WriteLine($"connecting to {host}:{port} ...");
        using var tcp = new TcpClient();
        try { tcp.Connect(host, port); }
        catch (Exception ex) { Console.WriteLine($"FAILED to connect: {ex.Message}"); return 1; }
        Console.WriteLine($"connected  local={tcp.Client.LocalEndPoint}");

        NetworkStream ns = tcp.GetStream();
        ns.ReadTimeout = 1000;

        if (user != null && pass != null)
        {
            var login = new LoginMessage(user, pass, "", 5, 0)
            { TransferDirection = MessageDirection.ClientToServer };
            ctrl.SignMessage(login);
            byte[] w = new byte[login.ByteLength];
            login.WriteTo(w, 0);
            ns.Write(w, 0, w.Length);
            Console.WriteLine($"  -> LoginMessage               PI={login.PI}    {w.Length} bytes  (user {user})");
        }
        else Console.WriteLine("  (no M59USER/M59PASS set - listening only, not logging in)");

        var buf = new byte[16384];
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until)
        {
            int n;
            try { n = ns.Read(buf, 0, buf.Length); }
            catch (IOException) { continue; }          // read timeout, keep waiting
            if (n <= 0) { Console.WriteLine("server closed the connection"); break; }
            using var ms = new MemoryStream(buf, 0, n);
            ctrl.ReadRecv(ms, n);
        }

        Console.WriteLine($"\n{count} message(s) parsed from the live server");
        return count > 0 ? 0 : 1;
    }
}
