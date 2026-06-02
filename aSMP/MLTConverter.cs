using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;

namespace aSMP
{
    public class MLTConverter
    {
        // Output: ZX Spectrum style bitmap and attribute data in MLT layout
        public struct MLTData
        {
            // 256 x 192 / 8 = 6144 bytes, linear per scanline (32 bytes per line)
            public byte[] BitmapData;

            // MLT attributes: 32 attributes per line x 192 lines = 6144 bytes
            public byte[] AttributeData;
        }

        // Internal helper for one 8-pixel block
        private struct BlockResult
        {
            public byte BitmapByte;
            public byte AttributeByte;
        }

        // Conversion parameters
        public struct ConversionParams
        {
            // Thresholds for 1-bit R/G/B dither (same idea as PB rv1/rv2 etc)
            public int RedLow;
            public int RedHigh;
            public int GreenLow;
            public int GreenHigh;
            public int BlueLow;
            public int BlueHigh;

            // PB-style bright handling:
            // "Bright" is the same as PB 'bright' (0..255),
            // internally used as bb0 = 255 - Bright.
            public int Bright;

            // 0 = always bright
            // 1 = sum-based simple threshold (PB mode 1)
            // 2 = average-based (PB mode 2)
            // 3 = never bright
            public int BrightMode;

            // Dither options
            public bool FlipDither;  // for 4-color mode, long pattern
            public bool Use4Color;   // if true, use 3-level dither (tritone-ish)
        }

        public MLTData ConvertImage(Bitmap srcImage, ConversionParams p)
        {
            Bitmap processImage = srcImage;

            // Ensure image is 256x192
            if (srcImage.Width != 256 || srcImage.Height != 192)
            {
                processImage = new Bitmap(srcImage, 256, 192);
            }

            byte[] bitmapOut = new byte[6144];
            byte[] attrOut = new byte[6144];

            // Threshold mapping as in PB: rva = 255 - high, rvb = 255 - low
            int rva = 255 - p.RedHigh;
            int rvb = 255 - p.RedLow;
            int gva = 255 - p.GreenHigh;
            int gvb = 255 - p.GreenLow;
            int bva = 255 - p.BlueHigh;
            int bvb = 255 - p.BlueLow;

            // PB: bb0 = 255 - bright
            int bb0 = 255 - p.Bright;

            // Dither table for 4-color mode
            byte[,] ditherTable = CalculateDitherTable(p.FlipDither);

            BitmapData bData = processImage.LockBits(
                new Rectangle(0, 0, 256, 192),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            unsafe
            {
                byte* ptr = (byte*)bData.Scan0;
                int stride = bData.Stride;

                Parallel.For(0, 192, y =>
                {
                    // These toggles implement the 1-bit ordered dither
                    int localC_Flip = (y % 2 == (p.FlipDither ? 1 : 0)) ? 1 : 0;
                    int localC_Simple = (y % 2 == 0) ? 1 : 0;

                    for (int xByte = 0; xByte < 32; xByte++)
                    {
                        int[] blockColors = new int[8];

                        // Bright accumulation for this 8-pixel horizontal block
                        int sumR = 0;
                        int sumG = 0;
                        int sumB = 0;
                        int cnt = 0;

                        for (int bit = 0; bit < 8; bit++)
                        {
                            int x = (xByte * 8) + bit;

                            byte* px = ptr + (y * stride) + (x * 3);
                            byte B = px[0];
                            byte G = px[1];
                            byte R = px[2];

                            // Bright accumulation: only if bright mode > 0
                            if (p.BrightMode > 0)
                            {
                                if (R > bb0) { sumR += R; cnt++; }
                                if (G > bb0) { sumG += G; cnt++; }
                                if (B > bb0) { sumB += B; cnt++; }
                            }

                            byte bitR;
                            byte bitG;
                            byte bitB;

                            if (p.Use4Color)
                            {
                                // 3-level style dither
                                byte longC = ditherTable[x, y];
                                bitR = GetDitherValue3Levels(R, rva, rvb, localC_Flip, longC);
                                bitG = GetDitherValue3Levels(G, gva, gvb, localC_Flip, longC);
                                bitB = GetDitherValue3Levels(B, bva, bvb, localC_Flip, longC);
                                localC_Flip = 1 - localC_Flip;
                            }
                            else
                            {
                                // Simple 1-bit ordered dither
                                bitR = GetDitherValue(R, rva, rvb, localC_Simple);
                                bitG = GetDitherValue(G, gva, gvb, localC_Simple);
                                bitB = GetDitherValue(B, bva, bvb, localC_Simple);
                                localC_Simple = 1 - localC_Simple;
                            }

                            // Base ZX color index (0..7) from 1-bit R,G,B
                            int baseColorIndex = (bitG << 2) | (bitR << 1) | bitB;

                            blockColors[bit] = baseColorIndex;
                        }

                        // Compute bright bit for this block (PB style)
                        int brightBit = ComputeBright(sumR, sumG, sumB, cnt, bb0, p.BrightMode);

                        // Resolve ink/paper and final attribute/bitmap for this block
                        BlockResult result = ResolveBlock(blockColors, brightBit);

                        int outputIndex = (y * 32) + xByte;
                        bitmapOut[outputIndex] = result.BitmapByte;
                        attrOut[outputIndex] = result.AttributeByte;
                    }
                });
            }

            processImage.UnlockBits(bData);
            if (processImage != srcImage)
                processImage.Dispose();

            return new MLTData
            {
                BitmapData = bitmapOut,
                AttributeData = attrOut
            };
        }

        // Resolve ink/paper for an 8-pixel block, given base colors (0..7) and bright bit
        private BlockResult ResolveBlock(int[] colors, int brightBit)
        {
            // Histogram of color usage inside the block
            Dictionary<int, int> counts = new Dictionary<int, int>();
            foreach (int c in colors)
            {
                if (!counts.ContainsKey(c))
                    counts[c] = 0;
                counts[c]++;
            }

            // Sort colors by usage descending
            var sorted = counts
                .OrderByDescending(k => k.Value)
                .Select(k => k.Key)
                .ToList();

            int ink = 0;
            int paper = 0;

            if (sorted.Count > 0) ink = sorted[0];
            if (sorted.Count > 1) paper = sorted[1];
            else paper = ink;

            int finalInk = ink & 7;   // 0..7
            int finalPaper = paper & 7;

            // Attribute: bit7=0 (no flash here), bit6=bright, bits5-3=paper, bits2-0=ink
            byte attribute = (byte)((brightBit << 6) | (finalPaper << 3) | finalInk);

            // Build bitmap byte: 1 = ink, 0 = paper
            byte bitmap = 0;
            for (int i = 0; i < 8; i++)
            {
                int c = colors[i];
                if (c == ink)
                {
                    bitmap |= (byte)(1 << (7 - i));
                }
            }

            return new BlockResult
            {
                BitmapByte = bitmap,
                AttributeByte = attribute
            };
        }

        // Simple 1-bit ordered dither between va and vb
        private byte GetDitherValue(int value, int va, int vb, int c)
        {
            if (value < va) return 0;
            if (value > vb) return 1;
            return (byte)c;
        }

        // 3-level dither: returns 0, 1 or one of two pattern values
        private byte GetDitherValue3Levels(int value, int va, int vb, int c, byte lc)
        {
            int midRange = (vb - va) / 2;
            if (value < va) return 0;
            if (value > vb) return 1;

            if (value < va + midRange)
                return lc;
            else
                return (byte)c;
        }

        // PB-style bright computation for one block
        private int ComputeBright(int sumR, int sumG, int sumB, int cnt, int max, int brightMode)
        {
            switch (brightMode)
            {
                case 0:
                    // Always bright
                    return 1;

                case 1:
                    // Simple threshold on scaled sums (PB uses r>>3 etc)
                    if (((sumR >> 3) < max) || ((sumG >> 3) < max) || ((sumB >> 3) < max))
                        return 0;
                    else
                        return 1;

                case 2:
                    // Average-based mode (PB mode 2 + coltest approximation)
                    cnt++; // PB increments cnt once more
                    if (cnt <= 0)
                        return 0;

                    int avg = (sumR + sumG + sumB) / cnt;

                    // Approximate coltest(avg, max): 1 if above threshold
                    return (avg >= max) ? 1 : 0;

                case 3:
                default:
                    // Never bright
                    return 0;
            }
        }

        // Build a simple ordered dither table for 4-color mode
        private byte[,] CalculateDitherTable(bool flip)
        {
            byte[,] table = new byte[256, 192];
            int f = flip ? 1 : 0;

            for (int x = 0; x < 256; x += 2)
            {
                for (int y = 0; y < 192; y++)
                {
                    table[x, y] = 0;
                    if ((y % 2) == f)
                        table[x, y] = 1;
                }
            }

            return table;
        }
    }
}
