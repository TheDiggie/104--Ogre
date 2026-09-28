# Meridian59.Net8Export

Decodes BGF sprite frames to PNG using nothing but the ported core
library - no NuGet packages, no System.Drawing. This is the asset ->
pixels path any renderer sits on top of, so it is worth being able to
eyeball it.

    dotnet run --project Tools/Meridian59.Net8Export -- <file.bgf|dir> <outdir> [maxFrames]
    dotnet run --project Tools/Meridian59.Net8Export -- --survey <dir>

`--survey` reports BGF versions and how many frames need the CRUSH
codec, which only exists in Windows x86 builds. Everything from
version 10 on uses zlib and decodes anywhere.

Notes on the format, verified against splash.bgf (which has readable
text, so orientation errors are obvious - creature sprites are not a
reliable check):

- Pixel data is top-down, row-major, one palette index per byte, no
  stride padding. The "upside-down" comment in BgfBitmap refers to its
  BMP export path, not the raw array.
- Palette index 254 is the transparency colour and carries alpha 0 in
  ColorTransformation.DefaultPalette, so it maps straight to a
  transparent PNG pixel.
