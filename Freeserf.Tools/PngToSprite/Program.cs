using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using StbImageSharp;

namespace PngToSprite;

// Converts PNG images into the custom sprite format used by
// Freeserf.Core\Render\CustomSprites.cs (a color palette plus a byte index
// array, one index per pixel) and back: the generated C# code can be pasted
// into CustomSprites.cs, and existing sprites can be rendered back to PNG.
static class Program
{
    readonly record struct Rgba(byte R, byte G, byte B, byte A);

    static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "/?")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] == "to-code")
            return ToCode(args.Skip(1).ToArray());

        if (args[0] == "to-png")
            return ToPng(args.Skip(1).ToArray());

        // No subcommand given: default to "to-code" for convenience.
        return ToCode(args);
    }

    // PNG -> C# code (palette + color indices).
    static int ToCode(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string input = args[0];
        string name = "MySprite";
        int maxColors = 256;
        string? output = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name" when i + 1 < args.Length:
                    name = args[++i];
                    break;
                case "--max-colors" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out maxColors) || maxColors < 1)
                        throw new ArgumentException("Invalid --max-colors value.");
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        if (maxColors > 256)
            maxColors = 256; // indices are bytes, so the palette cannot exceed 256 entries

        if (!File.Exists(input))
            throw new FileNotFoundException($"Input file not found: {input}");

        ImageResult image;
        using (var stream = File.OpenRead(input))
            image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        int width = image.Width;
        int height = image.Height;
        byte[] data = image.Data; // RGBA

        // Collect unique colors. Fully transparent pixels are normalized to a
        // single entry so they always share one palette slot.
        var unique = new HashSet<Rgba>();
        for (int i = 0; i < data.Length; i += 4)
        {
            byte r = data[i], g = data[i + 1], b = data[i + 2], a = data[i + 3];
            unique.Add(a == 0 ? new Rgba(0, 0, 0, 0) : new Rgba(r, g, b, a));
        }

        var palette = Quantize(unique.ToList(), maxColors);

        var indices = new byte[width * height];
        for (int i = 0; i < indices.Length; i++)
        {
            byte r = data[i * 4], g = data[i * 4 + 1], b = data[i * 4 + 2], a = data[i * 4 + 3];
            var color = a == 0 ? new Rgba(0, 0, 0, 0) : new Rgba(r, g, b, a);
            indices[i] = (byte)FindNearest(color, palette);
        }

        string code = GenerateCode(name, width, height, palette, indices);

        if (output != null)
        {
            File.WriteAllText(output, code);
            Console.WriteLine($"Wrote {output}");
        }
        else
        {
            Console.WriteLine(code);
        }

        Console.Error.WriteLine(
            $"// {width}x{height}, {palette.Count} palette colors, {unique.Count} unique colors in source");

        return 0;
    }

    // C# code (palette + color indices) -> PNG.
    static int ToPng(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string input = args[0];
        string? name = null;
        string? output = null;
        int width = 0, height = 0;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name" when i + 1 < args.Length:
                    name = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--width" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out width) || width < 1)
                        throw new ArgumentException("Invalid --width value.");
                    break;
                case "--height" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out height) || height < 1)
                        throw new ArgumentException("Invalid --height value.");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        if (!File.Exists(input))
            throw new FileNotFoundException($"Input file not found: {input}");

        var (palette, indices, codeWidth, codeHeight) = ParseCode(File.ReadAllText(input), name);

        if (width <= 0)
            width = codeWidth;
        if (height <= 0)
            height = codeHeight;

        if (width <= 0 || height <= 0)
            throw new ArgumentException(
                "Could not determine the sprite dimensions. Add --width and --height or keep the Create(...) call in the file.");

        if (width * height != indices.Length)
            throw new ArgumentException(
                $"Indices count ({indices.Length}) does not match {width}x{height}.");

        foreach (byte index in indices)
        {
            if (index >= palette.Count)
                throw new ArgumentException(
                    $"Index {index} is out of range for a palette of {palette.Count} colors.");
        }

        var rgba = new byte[width * height * 4];
        for (int i = 0; i < indices.Length; i++)
        {
            var c = palette[indices[i]];
            rgba[i * 4] = c.R;
            rgba[i * 4 + 1] = c.G;
            rgba[i * 4 + 2] = c.B;
            rgba[i * 4 + 3] = c.A;
        }

        output ??= Path.ChangeExtension(input, ".png");

        using (var stream = File.Create(output))
            PngWriter.Write(stream, width, height, rgba);

        Console.WriteLine($"Wrote {output} ({width}x{height}, {palette.Count} palette colors)");
        return 0;
    }

    // Extracts the palette, the color indices and the dimensions from a
    // CustomSprites.cs style code snippet. If name is null, the first
    // matching arrays are used.
    static (List<Rgba> Palette, byte[] Indices, int Width, int Height) ParseCode(string text, string? name)
    {
        string namePattern = name != null ? Regex.Escape(name) : @"\w+";

        var paletteMatch = Regex.Match(
            text, $@"Color\[\]\s+{namePattern}Colors\s*=\s*\[(.*?)\];", RegexOptions.Singleline);
        if (!paletteMatch.Success)
            throw new ArgumentException("No Color[] palette found in the file.");

        var palette = new List<Rgba>();
        foreach (Match m in Regex.Matches(
            paletteMatch.Groups[1].Value, @"new\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)"))
        {
            palette.Add(new Rgba(
                (byte)int.Parse(m.Groups[1].Value),
                (byte)int.Parse(m.Groups[2].Value),
                (byte)int.Parse(m.Groups[3].Value),
                (byte)int.Parse(m.Groups[4].Value)));
        }

        if (palette.Count == 0)
            throw new ArgumentException("No colors found in the palette.");

        var indicesMatch = Regex.Match(
            text, $@"byte\[\]\s+{namePattern}ColorIndices\s*=\s*\[(.*?)\];", RegexOptions.Singleline);
        if (!indicesMatch.Success)
            throw new ArgumentException("No byte[] color indices found in the file.");

        var indices = new List<byte>();
        foreach (Match m in Regex.Matches(
            indicesMatch.Groups[1].Value, @"0x([0-9a-fA-F]{1,2})|\b(\d{1,3})\b"))
        {
            indices.Add(m.Groups[1].Success
                ? Convert.ToByte(m.Groups[1].Value, 16)
                : byte.Parse(m.Groups[2].Value));
        }

        if (indices.Count == 0)
            throw new ArgumentException("No color indices found in the file.");

        int width = 0, height = 0;
        var createMatch = Regex.Match(
            text, $@"Create\s*\(\s*(\d+)u?\s*,\s*(\d+)u?\s*,\s*{namePattern}Colors\s*,");
        if (createMatch.Success)
        {
            width = int.Parse(createMatch.Groups[1].Value);
            height = int.Parse(createMatch.Groups[2].Value);
        }

        return (palette, indices.ToArray(), width, height);
    }

    // Reduces the color set to at most maxColors entries using median cut.
    static List<Rgba> Quantize(List<Rgba> colors, int maxColors)
    {
        if (colors.Count <= maxColors)
            return colors;

        var boxes = new List<List<Rgba>> { colors };

        while (boxes.Count < maxColors)
        {
            int bestBox = -1;
            int bestRange = -1;
            int bestChannel = 0;

            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Count < 2)
                    continue;

                int rMin = 255, rMax = 0, gMin = 255, gMax = 0, bMin = 255, bMax = 0, aMin = 255, aMax = 0;
                foreach (var c in box)
                {
                    rMin = Math.Min(rMin, c.R); rMax = Math.Max(rMax, c.R);
                    gMin = Math.Min(gMin, c.G); gMax = Math.Max(gMax, c.G);
                    bMin = Math.Min(bMin, c.B); bMax = Math.Max(bMax, c.B);
                    aMin = Math.Min(aMin, c.A); aMax = Math.Max(aMax, c.A);
                }

                int rRange = rMax - rMin, gRange = gMax - gMin, bRange = bMax - bMin, aRange = aMax - aMin;
                int range = Math.Max(Math.Max(rRange, gRange), Math.Max(bRange, aRange));

                if (range > bestRange)
                {
                    bestRange = range;
                    bestBox = i;
                    bestChannel = range == rRange ? 0 : range == gRange ? 1 : range == bRange ? 2 : 3;
                }
            }

            if (bestBox == -1)
                break; // no box can be split any further

            var boxToSplit = boxes[bestBox];
            var sorted = bestChannel switch
            {
                0 => boxToSplit.OrderBy(c => c.R).ToList(),
                1 => boxToSplit.OrderBy(c => c.G).ToList(),
                2 => boxToSplit.OrderBy(c => c.B).ToList(),
                _ => boxToSplit.OrderBy(c => c.A).ToList(),
            };

            int mid = sorted.Count / 2;
            boxes.RemoveAt(bestBox);
            boxes.Add(sorted.Take(mid).ToList());
            boxes.Add(sorted.Skip(mid).ToList());
        }

        return boxes.Select(box =>
        {
            int r = 0, g = 0, b = 0, a = 0;
            foreach (var c in box)
            {
                r += c.R; g += c.G; b += c.B; a += c.A;
            }

            int n = box.Count;
            return new Rgba((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
        }).ToList();
    }

    static int FindNearest(Rgba color, List<Rgba> palette)
    {
        int best = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < palette.Count; i++)
        {
            var p = palette[i];
            int dr = color.R - p.R;
            int dg = color.G - p.G;
            int db = color.B - p.B;
            int da = color.A - p.A;
            int distance = dr * dr + dg * dg + db * db + da * da;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    static string GenerateCode(string name, int width, int height, List<Rgba> palette, byte[] indices)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"    static readonly Color[] {name}Colors =");
        sb.AppendLine("    [");
        for (int i = 0; i < palette.Count; i++)
        {
            var c = palette[i];
            sb.Append($"        new({c.R}, {c.G}, {c.B}, {c.A})");
            if (i < palette.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine($"    static readonly byte[] {name}ColorIndices =");
        sb.AppendLine("    [");
        for (int i = 0; i < indices.Length; i++)
        {
            if (i % 16 == 0)
                sb.Append("        ");

            sb.Append($"0x{indices[i]:x2}");

            if (i < indices.Length - 1)
                sb.Append(", ");

            if (i % 16 == 15 || i == indices.Length - 1)
                sb.AppendLine();
        }

        sb.AppendLine("    ];");
        sb.AppendLine();
        sb.AppendLine($"    public static readonly Sprite {name} = Create({width}u, {height}u, {name}Colors, {name}ColorIndices);");

        return sb.ToString();
    }

    static void PrintUsage()
    {
        Console.WriteLine("Converts PNG images to and from the custom sprite format used by CustomSprites.cs");
        Console.WriteLine("(a color palette plus a byte index array per pixel).");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  PngToSprite to-code <input.png> [options]   PNG -> C# code (palette + indices)");
        Console.WriteLine("  PngToSprite to-png <input.cs> [options]     C# code -> PNG");
        Console.WriteLine();
        Console.WriteLine("to-code options:");
        Console.WriteLine("  --name <name>        Sprite name used in the generated code (default: MySprite)");
        Console.WriteLine("  --max-colors <n>     Maximum palette size, 1-256 (default: 256)");
        Console.WriteLine("  --output <file>      Write the generated C# code to a file instead of the console");
        Console.WriteLine();
        Console.WriteLine("to-png options:");
        Console.WriteLine("  --name <name>        Use the sprite with this name if the file contains several");
        Console.WriteLine("  --width <n>          Sprite width (only needed if the Create(...) call is missing)");
        Console.WriteLine("  --height <n>         Sprite height (only needed if the Create(...) call is missing)");
        Console.WriteLine("  --output <file>      Output PNG file (default: input file with .png extension)");
        Console.WriteLine();
        Console.WriteLine("  -h, --help           Show this help");
    }
}

// Minimal PNG encoder (RGBA, 8 bit per channel) used to write sprites back
// to image files without an external image-writing dependency.
static class PngWriter
{
    static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static void Write(Stream stream, int width, int height, byte[] rgba)
    {
        stream.Write(Signature, 0, Signature.Length);
        WriteChunk(stream, "IHDR", BuildIHDR(width, height));
        WriteChunk(stream, "IDAT", CompressScanlines(width, height, rgba));
        WriteChunk(stream, "IEND", Array.Empty<byte>());
    }

    static byte[] BuildIHDR(int width, int height)
    {
        var data = new byte[13];
        WriteInt32(data, 0, width);
        WriteInt32(data, 4, height);
        data[8] = 8;  // bit depth
        data[9] = 6;  // color type: RGBA
        data[10] = 0; // compression
        data[11] = 0; // filter
        data[12] = 0; // interlace
        return data;
    }

    static byte[] CompressScanlines(int width, int height, byte[] rgba)
    {
        // Each scanline is prefixed with a filter byte (0 = None).
        var raw = new byte[height * (1 + width * 4)];
        int src = 0, dst = 0;

        for (int y = 0; y < height; y++)
        {
            raw[dst++] = 0;
            Array.Copy(rgba, src, raw, dst, width * 4);
            src += width * 4;
            dst += width * 4;
        }

        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);

        return ms.ToArray();
    }

    static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);

        var length = BitConverter.GetBytes(data.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(length);
        stream.Write(length, 0, 4);

        stream.Write(typeBytes, 0, 4);
        stream.Write(data, 0, data.Length);

        var crc = BitConverter.GetBytes(Crc32(typeBytes, data));
        if (BitConverter.IsLittleEndian)
            Array.Reverse(crc);
        stream.Write(crc, 0, 4);
    }

    static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte b in type.Concat(data))
        {
            crc ^= b;

            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }

        return ~crc;
    }

    static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
