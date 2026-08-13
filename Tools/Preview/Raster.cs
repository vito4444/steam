using System;
using System.IO;
using System.IO.Compression;
using Worker.Core;

namespace Worker.Preview
{
    /// <summary>
    /// A tiny CPU rasteriser plus a dependency-free PNG writer.
    ///
    /// This exists so the project can render, screenshot and visually review the game
    /// on a machine with no GPU and no Unity licence. It draws with exactly the same
    /// <see cref="Palette"/> and the same geometry rules as the Unity renderer, so a
    /// preview frame is a faithful proxy for the real thing when judging composition,
    /// density, value separation and readability.
    /// </summary>
    public sealed class Raster
    {
        public readonly int Width;
        public readonly int Height;
        private readonly byte[] _pixels; // RGBA, row 0 is the top row

        public Raster(int width, int height)
        {
            Width = width;
            Height = height;
            _pixels = new byte[width * height * 4];
        }

        public void Clear(RgbColor color)
        {
            for (int i = 0; i < _pixels.Length; i += 4)
            {
                _pixels[i] = color.R;
                _pixels[i + 1] = color.G;
                _pixels[i + 2] = color.B;
                _pixels[i + 3] = 255;
            }
        }

        public void SetPixel(int x, int y, RgbColor color)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;
            if (color.A == 0) return;

            int index = (y * Width + x) * 4;
            if (color.A == 255)
            {
                _pixels[index] = color.R;
                _pixels[index + 1] = color.G;
                _pixels[index + 2] = color.B;
                _pixels[index + 3] = 255;
                return;
            }

            int a = color.A;
            int inv = 255 - a;
            _pixels[index] = (byte)((color.R * a + _pixels[index] * inv) / 255);
            _pixels[index + 1] = (byte)((color.G * a + _pixels[index + 1] * inv) / 255);
            _pixels[index + 2] = (byte)((color.B * a + _pixels[index + 2] * inv) / 255);
            _pixels[index + 3] = 255;
        }

        public RgbColor GetPixel(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return RgbColor.Clear;
            int index = (y * Width + x) * 4;
            return new RgbColor(_pixels[index], _pixels[index + 1], _pixels[index + 2], _pixels[index + 3]);
        }

        public void FillRect(int x, int y, int width, int height, RgbColor color)
        {
            int x0 = Math.Max(0, x);
            int y0 = Math.Max(0, y);
            int x1 = Math.Min(Width, x + width);
            int y1 = Math.Min(Height, y + height);

            for (int py = y0; py < y1; py++)
            {
                for (int px = x0; px < x1; px++) SetPixel(px, py, color);
            }
        }

        public void StrokeRect(int x, int y, int width, int height, int thickness, RgbColor color)
        {
            FillRect(x, y, width, thickness, color);
            FillRect(x, y + height - thickness, width, thickness, color);
            FillRect(x, y, thickness, height, color);
            FillRect(x + width - thickness, y, thickness, height, color);
        }

        public void FillCircle(int cx, int cy, int radius, RgbColor color)
        {
            int r2 = radius * radius;
            for (int py = cy - radius; py <= cy + radius; py++)
            {
                for (int px = cx - radius; px <= cx + radius; px++)
                {
                    int dx = px - cx;
                    int dy = py - cy;
                    if (dx * dx + dy * dy <= r2) SetPixel(px, py, color);
                }
            }
        }

        public void FillRoundedRect(int x, int y, int width, int height, int radius, RgbColor color)
        {
            if (radius <= 0)
            {
                FillRect(x, y, width, height, color);
                return;
            }

            radius = Math.Min(radius, Math.Min(width, height) / 2);
            FillRect(x + radius, y, width - radius * 2, height, color);
            FillRect(x, y + radius, radius, height - radius * 2, color);
            FillRect(x + width - radius, y + radius, radius, height - radius * 2, color);

            FillCircle(x + radius, y + radius, radius, color);
            FillCircle(x + width - radius - 1, y + radius, radius, color);
            FillCircle(x + radius, y + height - radius - 1, radius, color);
            FillCircle(x + width - radius - 1, y + height - radius - 1, radius, color);
        }

        public void DrawLine(int x0, int y0, int x1, int y1, RgbColor color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                SetPixel(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Horizontal progress bar with a track, used for work progress and stamina.</summary>
        public void DrawBar(int x, int y, int width, int height, int numerator, int denominator,
            RgbColor fill, RgbColor track)
        {
            FillRect(x, y, width, height, track);
            if (denominator <= 0 || numerator <= 0) return;

            int filled = numerator >= denominator ? width : width * numerator / denominator;
            if (filled > 0) FillRect(x, y, filled, height, fill);
        }

        // ------------------------------------------------------------------- PNG

        public void SavePng(string path)
        {
            using var file = File.Create(path);
            WritePng(file);
        }

        public byte[] ToPngBytes()
        {
            using var memory = new MemoryStream();
            WritePng(memory);
            return memory.ToArray();
        }

        private void WritePng(Stream output)
        {
            output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            var header = new byte[13];
            WriteBigEndian(header, 0, Width);
            WriteBigEndian(header, 4, Height);
            header[8] = 8;  // bit depth
            header[9] = 6;  // colour type: RGBA
            header[10] = 0; // deflate
            header[11] = 0; // adaptive filtering
            header[12] = 0; // no interlace
            WriteChunk(output, "IHDR", header);

            // Each scanline is prefixed with filter type 0 (None). Filtering would shrink
            // the file, but frames are throwaway artefacts and simplicity is worth more.
            var raw = new byte[Height * (Width * 4 + 1)];
            int cursor = 0;
            for (int y = 0; y < Height; y++)
            {
                raw[cursor++] = 0;
                Buffer.BlockCopy(_pixels, y * Width * 4, raw, cursor, Width * 4);
                cursor += Width * 4;
            }

            WriteChunk(output, "IDAT", ZlibCompress(raw));
            WriteChunk(output, "IEND", Array.Empty<byte>());
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            using var memory = new MemoryStream();
            memory.WriteByte(0x78); // CMF: deflate, 32K window
            memory.WriteByte(0x01); // FLG: no dictionary, fastest

            using (var deflate = new DeflateStream(memory, CompressionLevel.Fastest, true))
            {
                deflate.Write(data, 0, data.Length);
            }

            uint adler = Adler32(data);
            memory.WriteByte((byte)(adler >> 24));
            memory.WriteByte((byte)(adler >> 16));
            memory.WriteByte((byte)(adler >> 8));
            memory.WriteByte((byte)adler);

            return memory.ToArray();
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            output.Write(length, 0, 4);

            var typeBytes = new byte[4];
            for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
            output.Write(typeBytes, 0, 4);
            output.Write(data, 0, data.Length);

            uint crc = Crc32(typeBytes, data);
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, (int)crc);
            output.Write(crcBytes, 0, 4);
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] type, byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = 0; i < type.Length; i++) c = CrcTable[(c ^ type[i]) & 0xFF] ^ (c >> 8);
            for (int i = 0; i < data.Length; i++) c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
