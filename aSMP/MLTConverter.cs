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
        // Geri dönüş tipi: ZX Spectrum Bitmap ve Attribute verileri
        public struct MLTData
        {
            public byte[] BitmapData; // 32x192 = 6144 byte (Linear)
            public byte[] AttributeData; // 32x192 = 6144 byte (MLT Renkleri)
        }

        // Tuple hatasını gidermek için özel bir struct tanımlıyoruz
        private struct BlockResult
        {
            public byte BitmapByte;
            public byte AttributeByte;
        }

        // Giriş Parametreleri
        public struct ConversionParams
        {
            public int RedLow, RedHigh;
            public int GreenLow, GreenHigh;
            public int BlueLow, BlueHigh;
            public int BrightLow, BrightHigh;

            public bool FlipDither;
            public bool Use4Color;
            public bool ForceBright;
            public bool NoBright;
        }

        public MLTData ConvertImage(Bitmap srcImage, ConversionParams p)
        {
            Bitmap processImage = srcImage;
            // Resmi 256x192 değilse yeniden boyutlandır
            if (srcImage.Width != 256 || srcImage.Height != 192)
            {
                processImage = new Bitmap(srcImage, 256, 192);
            }

            byte[] bitmapOut = new byte[6144];
            byte[] attrOut = new byte[6144];

            int rva = 255 - p.RedHigh;
            int rvb = 255 - p.RedLow;
            int gva = 255 - p.GreenHigh;
            int gvb = 255 - p.GreenLow;
            int bva = 255 - p.BlueHigh;
            int bvb = 255 - p.BlueLow;

            byte[,] ditherTable = CalculateDitherTable(p.FlipDither);

            BitmapData bData = processImage.LockBits(new Rectangle(0, 0, 256, 192), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            unsafe
            {
                byte* ptr = (byte*)bData.Scan0;
                int stride = bData.Stride;

                Parallel.For(0, 192, y =>
                {
                    int localC_Flip = (y % 2 == (p.FlipDither ? 1 : 0)) ? 1 : 0;
                    int localC_Simple = (y % 2 == 0) ? 1 : 0;

                    for (int xByte = 0; xByte < 32; xByte++)
                    {
                        int[] blockColors = new int[8];

                        for (int bit = 0; bit < 8; bit++)
                        {
                            int x = (xByte * 8) + bit;

                            byte* px = ptr + (y * stride) + (x * 3);
                            byte B = px[0];
                            byte G = px[1];
                            byte R = px[2];

                            // Basit parlaklık hesabı (Performans için)
                            float brightness = (Math.Max(R, Math.Max(G, B)) + Math.Min(R, Math.Min(G, B))) / 2.0f;

                            int isBright = 0;
                            if (p.ForceBright)
                            {
                                isBright = 1;
                            }
                            else if (!p.NoBright)
                            {
                                if (p.BrightLow <= brightness && brightness <= p.BrightHigh)
                                {
                                    isBright = 1;
                                }
                            }

                            byte bitR = 0, bitG = 0, bitB = 0;

                            if (p.Use4Color)
                            {
                                byte longC = ditherTable[x, y];
                                bitR = GetDitherValue3Levels(R, rva, rvb, localC_Flip, longC);
                                bitG = GetDitherValue3Levels(G, gva, gvb, localC_Flip, longC);
                                bitB = GetDitherValue3Levels(B, bva, bvb, localC_Flip, longC);
                                localC_Flip = 1 - localC_Flip;
                            }
                            else
                            {
                                bitR = GetDitherValue(R, rva, rvb, localC_Simple);
                                bitG = GetDitherValue(G, gva, gvb, localC_Simple);
                                bitB = GetDitherValue(B, bva, bvb, localC_Simple);
                                localC_Simple = 1 - localC_Simple;
                            }

                            int colorIndex = (bitG << 2) | (bitR << 1) | bitB;

                            if (isBright == 1 && !p.NoBright)
                            {
                                colorIndex += 8;
                            }

                            blockColors[bit] = colorIndex;
                        }

                        // Hata veren Tuple yerine Struct döndüren metodu çağırıyoruz
                        BlockResult result = ResolveBlock(blockColors);

                        int outputIndex = (y * 32) + xByte;
                        bitmapOut[outputIndex] = result.BitmapByte;
                        attrOut[outputIndex] = result.AttributeByte;
                    }
                });
            }

            processImage.UnlockBits(bData);
            if (processImage != srcImage) processImage.Dispose();

            return new MLTData { BitmapData = bitmapOut, AttributeData = attrOut };
        }

        // Tuple yerine BlockResult struct'ı döndüren düzeltilmiş metod
        private BlockResult ResolveBlock(int[] colors)
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            foreach (int c in colors)
            {
                if (!counts.ContainsKey(c)) counts[c] = 0;
                counts[c]++;
            }

            var sorted = counts.OrderByDescending(k => k.Value).Select(k => k.Key).ToList();

            int ink = 0;
            int paper = 0;

            if (sorted.Count > 0) ink = sorted[0];
            if (sorted.Count > 1) paper = sorted[1];
            else paper = ink;

            bool inkBright = ink > 7;
            bool paperBright = paper > 7;

            if (inkBright != paperBright)
            {
                if (inkBright && paper < 8) paper += 8;
                else if (!inkBright && paper > 7) paper -= 8;
            }

            int finalBright = (ink > 7) ? 1 : 0;
            int finalInk = ink % 8;
            int finalPaper = paper % 8;

            byte attribute = (byte)((finalBright << 6) | (finalPaper << 3) | finalInk);

            byte bitmap = 0;
            for (int i = 0; i < 8; i++)
            {
                int c = colors[i];
                if (c == ink)
                {
                    bitmap |= (byte)(1 << (7 - i));
                }
            }

            // Struct döndür
            return new BlockResult { BitmapByte = bitmap, AttributeByte = attribute };
        }

        private byte GetDitherValue(int value, int va, int vb, int c)
        {
            if (value < va) return 0;
            else if (value > vb) return 1;
            else return (byte)c;
        }

        private byte GetDitherValue3Levels(int value, int va, int vb, int c, byte lc)
        {
            int r = (vb - va) / 2;
            if (value < va) return 0;
            else if (value > vb) return 1;
            else if ((value < r + va)) return lc;
            else return (byte)c;
        }

        private byte[,] CalculateDitherTable(bool flip)
        {
            byte[,] table = new byte[256, 192];
            int f = flip ? 1 : 0;
            for (int x = 0; x < 256; x += 2)
                for (int y = 0; y < 192; y++)
                {
                    table[x, y] = 0;
                    if ((y % 2) == f) table[x, y] = 1;
                }
            return table;
        }
    }
}