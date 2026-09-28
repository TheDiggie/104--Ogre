using System;
using System.Linq;
using System.Threading;
using Meridian59.Common;
using Meridian59.Data.Models;

// Headless login and world probe. Connects with the SAME M59Client the
// Godot view uses, runs the handshake, and prints what the server sends:
// room changes, object counts, the avatar's position.
//
//   dotnet run --project Tools/Meridian59.Net8Play -- <resourceDir> [host] [port] [seconds]
//
// Credentials come from the environment, never arguments:
//   set M59USER=yourname
//   set M59PASS=yourpassword
//
// This separates the networking from the rendering: if it reaches "in
// room N" with a sane object count, the client half works and anything
// wrong is in the view.
static class Play
{
    static int Main(string[] a)
    {
        if (a.Length < 1)
        {
            Console.WriteLine("usage: <resourceDir> [host] [port] [seconds]");
            Console.WriteLine("       set M59USER and M59PASS first");
            return 2;
        }

        string dir = a[0];
        string host = a.Length > 1 ? a[1] : "3.141.65.36";
        ushort port = a.Length > 2 ? ushort.Parse(a[2]) : (ushort)5959;
        int seconds = a.Length > 3 ? int.Parse(a[3]) : 25;

        string user = Environment.GetEnvironmentVariable("M59USER");
        string pass = Environment.GetEnvironmentVariable("M59PASS");
        string chr  = Environment.GetEnvironmentVariable("M59CHAR") ?? "";

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            Console.WriteLine("M59USER / M59PASS are not set. Refusing to continue.");
            return 2;
        }

        var client = new M59Client { PreferredCharacter = chr };
        client.Notice += s => Console.WriteLine("  " + s);

        client.Init();
        client.Config.ResourcesPath = dir;
        client.Config.Connections.Add(new ConnectionInfo(
            "probe", host, port, "rsc0000.rsb", user, pass, chr, null));
        client.Config.SelectedConnectionIndex = client.Config.Connections.Count - 1;

        Console.WriteLine($"connecting to {host}:{port} as {user}...");
        try { client.Connect(); }
        catch (Exception e) { Console.WriteLine($"connect failed: {e.GetType().Name}: {e.Message}"); return 1; }

        uint lastRoom = 0;
        int lastObjects = -1;
        bool sawRoom = false;
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < until)
        {
            try { client.Update(); }
            catch (Exception e) { Console.WriteLine($"update: {e.GetType().Name}: {e.Message}"); break; }

            var info = client.Data?.RoomInformation;
            if (info != null && info.RoomID != 0 && info.RoomID != lastRoom)
            {
                lastRoom = info.RoomID;
                sawRoom = true;
                string file = info.ResourceRoom != null
                    ? $"{info.ResourceRoom.Filename}, {info.ResourceRoom.Walls.Count} walls"
                    : "room file not loaded";
                Console.WriteLine($"  room {info.RoomID}: {file}");
            }

            int n = client.Data?.RoomObjects?.Count ?? 0;
            if (n != lastObjects)
            {
                lastObjects = n;
                Console.WriteLine($"  objects: {n}");
                foreach (RoomObject o in (client.Data.RoomObjects ?? Enumerable.Empty<RoomObject>()).Take(6))
                    Console.WriteLine($"     {o.Name,-24} at ({o.Position3D.X:F0}, {o.Position3D.Z:F0})" +
                                      $" {(o.Resource == null ? "no art" : o.Resource.Frames.Count + " frames")}");
            }

            RoomObject me = client.Data?.AvatarObject;
            if (me != null && !sawRoom)
                Console.WriteLine($"  avatar at ({me.Position3D.X:F0}, {me.Position3D.Z:F0})");

            Thread.Sleep(25);
        }

        Console.WriteLine("disconnecting.");
        try { client.Disconnect(); } catch { }

        Console.WriteLine(sawRoom
            ? "\nRESULT: reached the world - the client half works."
            : "\nRESULT: never entered a room. See the messages above.");
        return sawRoom ? 0 : 1;
    }
}
