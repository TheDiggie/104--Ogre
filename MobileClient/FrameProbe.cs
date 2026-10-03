using System;
using System.Diagnostics;
using Godot;

/// <summary>
/// Per-frame cost probe for the Godot layer. M59PROF=1 turns it on
/// (M59PROF=N prints every N frames, default 120); off, every call is a
/// static bool test and nothing else - no stopwatch, no allocation.
/// </summary>
/// <remarks>
/// Usage: <c>FrameProbe.Begin()</c> at the top of the frame, then
/// <c>FrameProbe.Mark("name")</c> after each system - the time since the
/// previous mark is charged to that name. <c>FrameProbe.End()</c> closes
/// the frame and prints a line every N frames:
///
///   [PROF] 120f  total 3.21ms/f  pump 0.40 ... | alloc 18.2KB/f gen0 2 (1.0/min) | render 118/120 upload 118/120
///
/// "alloc" is <c>GC.GetTotalAllocatedBytes</c> per frame across the whole
/// process, "gen0" the collections in the window and their rate per
/// minute, and the trailing counts say how many frames actually called
/// the renderer and uploaded the texture, so a low-power skip is
/// visible as a number rather than a hope.
/// </remarks>
static class FrameProbe
{
    public static readonly bool On;
    static readonly int Every;

    const int Max = 32;
    static readonly string[] Names = new string[Max];
    static readonly double[] Ms = new double[Max];
    static readonly long[] Bytes = new long[Max];
    static long _lastAlloc, _endAlloc, _outside, _threadTotal;
    static int _count;
    static readonly Stopwatch Sw = new Stopwatch();
    static long _lastTicks;
    static int _frames;
    static long _allocAt;
    static int _gen0At;
    static double _wallStart;
    static int _rendered, _uploaded, _processed;

    static FrameProbe()
    {
        string s = System.Environment.GetEnvironmentVariable("M59PROF");
        On = !string.IsNullOrEmpty(s) && s != "0";
        Every = int.TryParse(s, out int n) && n > 1 ? n : 120;
    }

    public static void Begin()
    {
        if (!On) return;
        if (!Sw.IsRunning)
        {
            Sw.Start();
            _allocAt = GC.GetTotalAllocatedBytes(false);
            _gen0At = GC.CollectionCount(0);
            _wallStart = Time.GetTicksMsec();
        }
        _lastTicks = Sw.ElapsedTicks;
        _lastAlloc = GC.GetAllocatedBytesForCurrentThread();
        // Main-thread bytes between the last End and this Begin: the
        // other nodes' _Process, _Draw, input and the engine glue.
        if (_endAlloc != 0) _outside += _lastAlloc - _endAlloc;
    }

    public static void Mark(string name)
    {
        if (!On) return;
        long now = Sw.ElapsedTicks;
        double ms = (now - _lastTicks) * 1000.0 / Stopwatch.Frequency;
        _lastTicks = now;
        long a = GC.GetAllocatedBytesForCurrentThread();
        long db = a - _lastAlloc; _lastAlloc = a; _threadTotal += db;
        int i = 0;
        for (; i < _count; i++) if (ReferenceEquals(Names[i], name) || Names[i] == name) break;
        if (i == _count) { if (_count == Max) return; Names[_count++] = name; }
        Ms[i] += ms;
        Bytes[i] += db;
    }

    /// <summary>What ran this frame; counted so the skip is a number.</summary>
    public static void Did(bool rendered, bool uploaded, bool processed)
    {
        if (!On) return;
        if (rendered) _rendered++;
        if (uploaded) _uploaded++;
        if (processed) _processed++;
    }

    public static void End()
    {
        if (!On) return;
        _endAlloc = GC.GetAllocatedBytesForCurrentThread();
        if (++_frames < Every) return;

        long alloc = GC.GetTotalAllocatedBytes(false);
        int gen0 = GC.CollectionCount(0);
        double wallMs = Time.GetTicksMsec() - _wallStart;
        double total = 0, render = 0;
        var sb = new System.Text.StringBuilder(256);
        sb.Append($"[PROF] {_frames}f  ");
        for (int i = 0; i < _count; i++)
        {
            total += Ms[i];
            if (Names[i] == "render") render = Ms[i];
        }
        sb.Append($"total {total / _frames:F2}ms/f  godot {(total - render) / _frames:F2}ms/f  ");
        for (int i = 0; i < _count; i++)
        {
            sb.Append($"{Names[i]} {Ms[i] / _frames:F2}");
            if (Bytes[i] > 0) sb.Append($"/{Bytes[i] / (double)_frames:F0}B");
            sb.Append(' ');
            Ms[i] = 0; Bytes[i] = 0;
        }
        double perMin = wallMs > 0 ? (gen0 - _gen0At) * 60000.0 / wallMs : 0;
        sb.Append($"| alloc {(alloc - _allocAt) / (double)_frames / 1024.0:F1}KB/f (pump {_threadTotal / (double)_frames:F0}B, main-outside {_outside / (double)_frames:F0}B) gen0 {gen0 - _gen0At} ({perMin:F1}/min) ");
        _threadTotal = 0; _outside = 0;
        sb.Append($"| render {_rendered}/{_frames} upload {_uploaded}/{_frames} ui {_processed}/{_frames}  wall {wallMs / _frames:F1}ms/f");
        GD.Print(sb.ToString());

        _frames = 0; _rendered = 0; _uploaded = 0; _processed = 0;
        _allocAt = alloc; _gen0At = gen0; _wallStart = Time.GetTicksMsec();
    }
}
