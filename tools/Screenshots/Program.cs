using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using ThemeEditor.Common.Audio;
using ThemeEditor.Common.Graphics;
using ThemeEditor.Common.Themes;
using ThemeEditor.Common.Themes.ColorSets;
using ThemeEditor.Common.Themes.Enums;
using UmamiMobile;
using CommonColors = ThemeEditor.Common.Themes.Colors;
using CommonTheme = ThemeEditor.Common.Themes.Theme;

namespace Screenshots
{
    /// <summary>Renders the real Umami Mobile UI headlessly at phone size, with a made-up sample theme, to PNG files.</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var outDir = args.Length > 0 ? args[0] : "shots";
            Directory.CreateDirectory(outDir);

            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .WithInterFont()
                .SetupWithoutStarting();

            Dispatcher.UIThread.Invoke(() => Run(outDir));
            return 0;
        }

        private static void Run(string outDir)
        {
            var view = new MainView();
            var theme = BuildTheme(out var archive);
            view.LoadTheme(theme, archive.Name, archive);
            view.ApplyWallpaper(Wallpaper(412, 240, 0), true, true);
            view.ApplyWallpaper(Wallpaper(320, 240, 1), false, true);

            const int W = 412, H = 892, Scale = 2;
            var status = new Grid { Height = 28, Background = Brushes.Black };
            status.Children.Add(new TextBlock { Text = "9:41", Margin = new Thickness(18, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = Brushes.White });
            status.Children.Add(new TextBlock { Text = "100%", Margin = new Thickness(18, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, FontSize = 13, Foreground = Brushes.White });
            var nav = new Border { Height = 24, Background = Brushes.Black, Child = new Border { Width = 110, Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } };
            var phone = new DockPanel { Width = W, Height = H, Background = Brushes.Black };
            DockPanel.SetDock(status, Dock.Top);
            DockPanel.SetDock(nav, Dock.Bottom);
            phone.Children.Add(status);
            phone.Children.Add(nav);
            phone.Children.Add(view);

            var window = new Window
            {
                Width = W * Scale,
                Height = H * Scale,
                Content = new Viewbox { Stretch = Stretch.Fill, Child = phone },
            };
            window.Show();

            Shot(window, view, outDir, "screens", 0);
            view.ExpandColorSet(2);
            Shot(window, view, outDir, "colours", 1);
            Shot(window, view, outDir, "sounds", 2);
            Shot(window, view, outDir, "music", 3);
        }

        private static void Shot(Window window, MainView view, string dir, string name, int tab)
        {
            view.SelectTab(tab);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            frame.Save(Path.Combine(dir, name + ".png"));
            Console.WriteLine("wrote " + name);
        }

        // ---------- made-up sample theme ----------

        private static CommonTheme BuildTheme(out ThemeArchiveContents archive)
        {
            var palette = new uint[] { 0xE8382F, 0xF2A33A, 0x4C7A9E, 0x2F2F36, 0xF5EFE6, 0xB7416E, 0x6AA84F, 0xFFD6E0 };
            var colors = new CommonColors();
            var n = 0;
            foreach (var f in typeof(CommonColors).GetFields())
            {
                var set = Activator.CreateInstance(f.FieldType);
                foreach (var cf in f.FieldType.GetFields())
                    if (cf.FieldType == typeof(ColorRgb888))
                        cf.SetValue(set, (ColorRgb888) palette[n++ % palette.Length]);
                f.SetValue(colors, set);
            }

            var block = new CwavBlock();
            for (var i = 0; i < 8; i++)
                block.Sounds[(CwavKind) i].CwavData = Beep(440 + i * 70, 0.08 + 0.03 * i);

            var theme = new CommonTheme
            {
                Colors = colors,
                Textures = new Textures(),
                Flags = new Flags { TopDrawType = TopDrawType.Texture, TopFrameType = TopFrameType.Single, BottomDrawType = BottomDrawType.Texture, BottomFrameType = BottomFrameType.Single, SoundEffect = true, BackgroundMusic = true },
                CWAV = block.Generate(),
            };
            archive = new ThemeArchiveContents { Name = "Sakura Dusk", Bgm = Music() };
            return theme;
        }

        private static byte[] Beep(double hz, double seconds)
        {
            const int rate = 22050;
            var n = (int) (rate * seconds);
            var pcm = new short[n];
            for (var i = 0; i < n; i++)
                pcm[i] = (short) (Math.Sin(2 * Math.PI * hz * i / rate) * 9000 * (1 - (double) i / n));
            return CwavConverter.Encode(BcstmConverter.FromPcm(new[] { pcm }, rate));
        }

        private static byte[] Music()
        {
            const int rate = 32000;
            var notes = new[] { 392.0, 440.0, 494.0, 587.0, 494.0, 440.0, 392.0, 330.0 };
            var n = rate * 12;
            var l = new short[n];
            var r = new short[n];
            for (var i = 0; i < n; i++)
            {
                var note = notes[(i / (rate * 3 / 2)) % notes.Length];
                var env = 1 - (i % (rate * 3 / 2)) / (double) (rate * 3 / 2);
                var v = (short) (Math.Sin(2 * Math.PI * note * i / rate) * 7000 * env);
                l[i] = v;
                r[i] = v;
            }
            return BcstmConverter.Encode(BcstmConverter.FromPcm(new[] { l, r }, rate), new BcstmOptions { Loop = true });
        }

        private static PixelBuffer Wallpaper(int w, int h, int variant)
        {
            using (var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)))
            using (var canvas = new SKCanvas(bmp))
            {
                var c0 = variant == 0 ? new SKColor(0xFF, 0x8A, 0x7A) : new SKColor(0x3A, 0x2E, 0x5A);
                var c1 = variant == 0 ? new SKColor(0x6A, 0x3D, 0x8F) : new SKColor(0xE8, 0x38, 0x2F);
                using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), new[] { c0, c1 }, null, SKShaderTileMode.Clamp))
                using (var paint = new SKPaint { Shader = shader })
                    canvas.DrawRect(0, 0, w, h, paint);
                using (var blossom = new SKPaint { Color = new SKColor(255, 220, 230, 150), IsAntialias = true })
                {
                    var rnd = new Random(7 + variant);
                    for (var i = 0; i < 26; i++)
                        canvas.DrawCircle(rnd.Next(w), rnd.Next(h), rnd.Next(4, 16), blossom);
                }
                var bytes = new byte[w * h * 4];
                Marshal.Copy(bmp.GetPixels(), bytes, 0, bytes.Length);
                return new PixelBuffer(w, h, bytes);
            }
        }
    }
}
