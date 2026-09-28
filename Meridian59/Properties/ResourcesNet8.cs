// Portable replacement for the generated Resources.Designer.cs.
//
// The generated accessor pulls the palette colour tables out of a .resx,
// which needs System.Resources.Extensions on modern .NET. Both tables are
// already plain 1024-byte files in the repo, so net8.csproj embeds them
// directly and this reads them back - no extra package, no .resx.
//
// Only compiled by net8.csproj; the .NET Framework project still uses the
// generated file.

using System;
using System.IO;
using System.Reflection;

namespace Meridian59.Properties
{
    internal static class Resources
    {
        private static byte[] table;
        private static byte[] tableVale;

        /// <summary>
        /// BMP colour table for the default palette (256 BGRA entries).
        /// </summary>
        internal static byte[] BitmapColorTable
        {
            get { return table ?? (table = Load("Meridian59.colortable.hex")); }
        }

        /// <summary>
        /// BMP colour table used by Vale of Sorrow and earlier.
        /// </summary>
        internal static byte[] BitmapColorTableVale
        {
            get { return tableVale ?? (tableVale = Load("Meridian59.colortable-vale.hex")); }
        }

        private static byte[] Load(string name)
        {
            Assembly asm = typeof(Resources).Assembly;
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null)
                    throw new InvalidOperationException(
                        "Embedded resource '" + name + "' is missing. Expected it to be " +
                        "embedded by net8.csproj. Found: " +
                        string.Join(", ", asm.GetManifestResourceNames()));

                byte[] buf = new byte[s.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = s.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return buf;
            }
        }
    }
}
