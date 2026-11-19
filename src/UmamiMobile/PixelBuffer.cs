using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace UmamiMobile
{
    /// <summary>CPU-side image (premultiplied BGRA) used to import wallpapers and show textures. Adapted from the desktop editor.</summary>
    public sealed class PixelBuffer
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Bgra { get; }

        public PixelBuffer(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public static PixelBuffer Load(Stream stream)
        {
            using (var data = SKData.Create(stream))
            using (var bmp = SKBitmap.Decode(data))
            {
                if (bmp == null)
                    throw new InvalidDataException("Unsupported image");
                return FromSkia(bmp);
            }
        }

        public static PixelBuffer FromBgr24(int width, int height, byte[] bgr)
        {
            var bgra = new byte[width * height * 4];
            for (int i = 0, j = 0; j < width * height * 3; i += 4, j += 3)
            {
                bgra[i] = bgr[j];
                bgra[i + 1] = bgr[j + 1];
                bgra[i + 2] = bgr[j + 2];
                bgra[i + 3] = 255;
            }
            return new PixelBuffer(width, height, bgra);
        }

        public byte[] GetBgr24Data()
        {
            var bgr = new byte[Width * Height * 3];
            for (int i = 0, j = 0; i < Bgra.Length; i += 4, j += 3)
            {
                bgr[j] = Bgra[i];
                bgr[j + 1] = Bgra[i + 1];
                bgr[j + 2] = Bgra[i + 2];
            }
            return bgr;
        }

        public PixelBuffer Crop(int x, int y, int width, int height)
        {
            width = Math.Min(width, Width - x);
            height = Math.Min(height, Height - y);
            var dst = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
                Buffer.BlockCopy(Bgra, ((y + row) * Width + x) * 4, dst, row * width * 4, width * 4);
            return new PixelBuffer(width, height, dst);
        }

        /// <summary>
        ///     Scales the image into the top-left (visibleWidth x visibleHeight) area of a black canvas. With
        ///     <paramref name="crop" /> the aspect ratio is kept and the overhang cut off evenly, otherwise it is stretched.
        /// </summary>
        public PixelBuffer Fit(int visibleWidth, int visibleHeight, int canvasWidth, int canvasHeight, bool crop)
        {
            var srcRect = new SKRect(0, 0, Width, Height);
            if (crop)
            {
                var scale = Math.Max((float) visibleWidth / Width, (float) visibleHeight / Height);
                var w = visibleWidth / scale;
                var h = visibleHeight / scale;
                var x = (Width - w) / 2;
                var y = (Height - h) / 2;
                srcRect = new SKRect(x, y, x + w, y + h);
            }
            using (var src = ToSkia())
            using (var dst = new SKBitmap(new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Bgra8888, SKAlphaType.Premul)))
            using (var canvas = new SKCanvas(dst))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(src, srcRect, new SKRect(0, 0, visibleWidth, visibleHeight), paint);
                return FromSkia(dst);
            }
        }

        public byte[] EncodePng()
        {
            using (var bmp = ToSkia())
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                return data.ToArray();
        }

        public WriteableBitmap ToBitmap()
        {
            var bmp = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = bmp.Lock())
            {
                for (var row = 0; row < Height; row++)
                    System.Runtime.InteropServices.Marshal.Copy(Bgra, row * Width * 4, fb.Address + row * fb.RowBytes, Width * 4);
            }
            return bmp;
        }

        private SKBitmap ToSkia()
        {
            var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(Bgra, 0, bmp.GetPixels(), Bgra.Length);
            return bmp;
        }

        private static PixelBuffer FromSkia(SKBitmap src)
        {
            using (var premul = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul)))
            {
                if (!src.CopyTo(premul))
                    throw new InvalidDataException("Unsupported image");
                var bytes = new byte[premul.Width * premul.Height * 4];
                var ptr = premul.GetPixels();
                for (var y = 0; y < premul.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(ptr + y * premul.RowBytes, bytes, y * premul.Width * 4, premul.Width * 4);
                return new PixelBuffer(premul.Width, premul.Height, bytes);
            }
        }
    }
}
