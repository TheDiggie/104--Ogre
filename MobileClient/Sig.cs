using System;
using System.Text;

/// <summary>
/// The per-frame signature pattern without the garbage.
///
/// A dozen pieces of the HUD decide whether to rebuild by writing what
/// they would draw into a StringBuilder, calling ToString and comparing
/// it with last frame's. Right most of the time, and every one of those
/// calls allocated a builder and a string on a frame where the answer
/// was "same" - about ten kilobytes a frame across the HUD at rest,
/// which on a phone is a gen0 collection every few seconds for nothing.
///
/// <see cref="Start"/> hands out one reused builder; <see cref="Changed"/>
/// compares its contents with the stored string IN PLACE and only
/// materialises a new string when they differ. The signature strings
/// themselves are unchanged, so every rebuild still happens exactly
/// when it did.
/// </summary>
static class Sig
{
    [ThreadStatic] static StringBuilder _sb;

    /// <summary>A cleared builder. Do not keep it past the frame.</summary>
    public static StringBuilder Start()
    {
        StringBuilder sb = _sb ??= new StringBuilder(1024);
        sb.Clear();
        return sb;
    }

    /// <summary>
    /// True when <paramref name="sb"/> differs from <paramref name="stored"/>,
    /// in which case <paramref name="stored"/> now holds the new text.
    /// </summary>
    public static bool Changed(StringBuilder sb, ref string stored)
    {
        if (stored != null && sb.Length == stored.Length && sb.Equals(stored.AsSpan())) return false;
        stored = sb.ToString();
        return true;
    }

    /// <summary>
    /// <c>sb.Append(x?.Field)</c> on a nullable number goes through
    /// Append(object) and boxes; this formats it on the stack instead.
    /// Appends nothing for null, as Append(object) did.
    /// </summary>
    public static StringBuilder Opt<T>(this StringBuilder sb, T? v) where T : struct, ISpanFormattable
    {
        if (!v.HasValue) return sb;
        Span<char> buf = stackalloc char[40];
        if (v.Value.TryFormat(buf, out int n, default, null)) sb.Append(buf.Slice(0, n));
        else sb.Append(v.Value.ToString());
        return sb;
    }
}
