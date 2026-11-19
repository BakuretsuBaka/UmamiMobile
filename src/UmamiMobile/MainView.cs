using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ThemeEditor.Common.Compression;
using ThemeEditor.Common.Graphics;
using ThemeEditor.Common.Themes;
using ThemeEditor.Common.Themes.Enums;
using CommonColors = ThemeEditor.Common.Themes.Colors;
using CommonTheme = ThemeEditor.Common.Themes.Theme;

namespace UmamiMobile
{
    /// <summary>The whole app: open a theme, see both screens, swap wallpapers, recolour, save.</summary>
    public class MainView : UserControl
    {
        private const int TopVisible = 412, BottomVisible = 320, ScreenHeight = 240, TexW = 512, TexH = 256;

        private CommonTheme _theme;
        private ThemeArchiveContents _archive;
        private string _name = "theme";

        private readonly Image _topImage = new Image { Stretch = Stretch.Uniform, MaxHeight = 220 };
        private readonly Image _bottomImage = new Image { Stretch = Stretch.Uniform, MaxHeight = 220 };
        private readonly StackPanel _colors = new StackPanel { Spacing = 4 };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        private readonly TextBox _nameBox = new TextBox { Watermark = "Theme name" };
        private readonly CheckBox _cropBox = new CheckBox { Content = "Crop pictures to fill the screen (off: stretch)", IsChecked = true };
        private readonly Button _saveZip = Btn("Save ZIP"), _saveBin = Btn("Save body_LZ.bin");
        private readonly Button _setTop = Btn("Top wallpaper"), _setBottom = Btn("Bottom wallpaper");

        public MainView()
        {
            var open = Btn("Open theme");
            open.Click += async (s, e) => await Guard(OpenTheme);
            _saveZip.Click += async (s, e) => await Guard(() => Save(true));
            _saveBin.Click += async (s, e) => await Guard(() => Save(false));
            _setTop.Click += async (s, e) => await Guard(() => SetWallpaper(true));
            _setBottom.Click += async (s, e) => await Guard(() => SetWallpaper(false));
            _nameBox.TextChanged += (s, e) => _name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "theme" : _nameBox.Text.Trim();

            var content = new StackPanel { Spacing = 10, Margin = new Thickness(16, 12, 16, 24) };
            content.Children.Add(new TextBlock { Text = "Umami", FontSize = 26, FontWeight = FontWeight.Bold });
            content.Children.Add(new TextBlock { Text = "3DS HOME Menu theme editor", Opacity = 0.7 });
            content.Children.Add(Row(open, _saveZip, _saveBin));
            content.Children.Add(_status);
            content.Children.Add(_nameBox);
            content.Children.Add(Heading("Preview"));
            content.Children.Add(new Border { Background = Brushes.Black, Child = _topImage, CornerRadius = new CornerRadius(4) });
            content.Children.Add(new Border { Background = Brushes.Black, Child = _bottomImage, CornerRadius = new CornerRadius(4), Width = 280, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(Heading("Wallpapers"));
            content.Children.Add(_cropBox);
            content.Children.Add(Row(_setTop, _setBottom));
            content.Children.Add(Heading("Colours"));
            content.Children.Add(_colors);

            Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            Refresh();
            SetStatus("Open a theme (.zip or body_LZ.bin) to begin.");
        }

        // ---------- file handling ----------

        private async Task OpenTheme()
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp == null)
                return;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open theme", AllowMultiple = false });
            if (files.Count == 0)
                return;
            byte[] bytes = await ReadAll(files[0]);
            ThemeArchiveContents archive = null;
            byte[] body;
            if (bytes.Length > 4 && bytes[0] == 'P' && bytes[1] == 'K')
            {
                archive = ThemeArchive.Read(bytes);
                if (archive == null)
                    throw new InvalidDataException("That ZIP has no body_LZ.bin in it.");
                body = archive.Body;
            }
            else
            {
                body = bytes;
            }

            CommonTheme theme;
            using (var src = new MemoryStream(body))
            using (var dec = new MemoryStream())
            {
                LZ11.Decompress(src, src.Length, dec);
                dec.Position = 0;
                theme = CommonTheme.Read(dec);
            }

            _theme = theme;
            _archive = archive;
            var fileName = Path.GetFileNameWithoutExtension(files[0].Name);
            _name = archive != null && !string.IsNullOrWhiteSpace(archive.Name) ? archive.Name : fileName;
            _nameBox.Text = _name;
            Refresh();
            SetStatus("Opened " + files[0].Name);
        }

        private async Task Save(bool zip)
        {
            if (_theme == null)
                return;
            byte[] compressed;
            using (var dec = new MemoryStream())
            using (var com = new MemoryStream())
            {
                CommonTheme.Write(_theme, dec);
                dec.Position = 0;
                LZ11.Compress(dec, dec.Length, com, true);
                compressed = com.ToArray();
            }

            byte[] output = compressed;
            if (zip)
            {
                byte[] preview = null;
                var top = DecodeTexture(_theme.Textures?.Top, TopVisible - 12);
                if (top != null)
                    preview = top.EncodePng();
                output = ThemeArchive.Create(_name, compressed, _archive?.Smdh, _archive?.Bgm, null, preview);
            }

            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp == null)
                return;
            var safe = string.Concat(_name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save theme",
                SuggestedFileName = zip ? safe + ".zip" : "body_LZ.bin",
                DefaultExtension = zip ? "zip" : "bin",
            });
            if (file == null)
                return;
            using (var s = await file.OpenWriteAsync())
            {
                s.SetLength(0);
                await s.WriteAsync(output, 0, output.Length);
            }
            SetStatus("Saved " + file.Name + (zip && _archive?.Smdh == null ? " (no info.smdh: the theme has no icon or title data yet)" : string.Empty));
        }

        private static async Task<byte[]> ReadAll(IStorageFile file)
        {
            using (var s = await file.OpenReadAsync())
            using (var ms = new MemoryStream())
            {
                await s.CopyToAsync(ms);
                return ms.ToArray();
            }
        }

        // ---------- wallpapers ----------

        private async Task SetWallpaper(bool top)
        {
            if (_theme == null)
                return;
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp == null)
                return;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = top ? "Choose the top wallpaper" : "Choose the bottom wallpaper",
                AllowMultiple = false,
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            });
            if (files.Count == 0)
                return;

            var existing = top ? _theme.Textures?.Top : _theme.Textures?.Bottom;
            if (existing != null && existing.Format != RawTexture.DataFormat.Invalid && existing.Width != TexW)
                throw new InvalidOperationException("This theme uses a scrolling or patterned " + (top ? "top" : "bottom")
                    + " background, which the mobile editor can't replace yet.");

            var bytes = await ReadAll(files[0]);
            PixelBuffer image;
            using (var ms = new MemoryStream(bytes))
                image = PixelBuffer.Load(ms);

            var visible = top ? TopVisible : BottomVisible;
            var fitted = image.Fit(visible, ScreenHeight, TexW, TexH, _cropBox.IsChecked == true);

            if (_theme.Textures == null)
                _theme.Textures = new Textures();
            var tex = new RawTexture(TexW, TexH, RawTexture.DataFormat.Bgr565);
            tex.Encode(fitted.GetBgr24Data(), TexW, TexH, RawTexture.DataFormat.Bgr565);
            tex.EdgeBleed(0, 0, visible, ScreenHeight);

            if (top)
            {
                _theme.Textures.Top = tex;
                _theme.Textures.TopExt = _theme.Textures.TopExt ?? new RawTexture();
                _theme.Flags.TopDrawType = TopDrawType.Texture;
                _theme.Flags.TopFrameType = TopFrameType.Single;
            }
            else
            {
                _theme.Textures.Bottom = tex;
                _theme.Flags.BottomDrawType = BottomDrawType.Texture;
                _theme.Flags.BottomFrameType = BottomFrameType.Single;
            }
            Refresh();
            SetStatus((top ? "Top" : "Bottom") + " wallpaper replaced.");
        }

        private static PixelBuffer DecodeTexture(RawTexture tex, int visibleWidth)
        {
            if (tex == null || tex.Format == RawTexture.DataFormat.Invalid || tex.Width == 0)
                return null;
            var full = PixelBuffer.FromBgr24(tex.Width, tex.Height, tex.Decode());
            return full.Crop(0, 0, Math.Min(visibleWidth, tex.Width), Math.Min(ScreenHeight, tex.Height));
        }

        // ---------- UI refresh ----------

        private void Refresh()
        {
            var loaded = _theme != null;
            _saveZip.IsEnabled = _saveBin.IsEnabled = _setTop.IsEnabled = _setBottom.IsEnabled = _nameBox.IsEnabled = loaded;
            _topImage.Source = loaded ? DecodeTexture(_theme.Textures?.Top, 400)?.ToBitmap() : null;
            _bottomImage.Source = loaded ? DecodeTexture(_theme.Textures?.Bottom, BottomVisible)?.ToBitmap() : null;
            BuildColors();
        }

        private void BuildColors()
        {
            _colors.Children.Clear();
            if (_theme?.Colors == null)
                return;
            foreach (var setField in typeof(CommonColors).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var set = setField.GetValue(_theme.Colors);
                if (set == null)
                    continue;
                var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6) };
                foreach (var f in set.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Where(x => x.FieldType == typeof(ColorRgb888)))
                    panel.Children.Add(ColorRow(set, f));
                if (panel.Children.Count > 0)
                    _colors.Children.Add(new Expander { Header = Spaced(setField.Name), Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            if (_colors.Children.Count == 0)
                _colors.Children.Add(new TextBlock { Text = "This theme sets no custom colours.", Opacity = 0.7 });
        }

        private Control ColorRow(object set, FieldInfo field)
        {
            var current = (ColorRgb888) field.GetValue(set);
            var swatch = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(6), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            var hex = new TextBox { MaxLength = 7, Width = 110, VerticalAlignment = VerticalAlignment.Center };
            var slidersPanel = new StackPanel { IsVisible = false, Spacing = 2 };
            var sliders = new[] { MakeSlider("R", slidersPanel), MakeSlider("G", slidersPanel), MakeSlider("B", slidersPanel) };

            var busy = false;
            void Apply(ColorRgb888 c, bool fromHex, bool fromSlider)
            {
                busy = true;
                field.SetValue(set, c);
                swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                if (!fromHex)
                    hex.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                if (!fromSlider)
                {
                    sliders[0].Value = c.R;
                    sliders[1].Value = c.G;
                    sliders[2].Value = c.B;
                }
                busy = false;
            }

            Apply(current, false, false);
            hex.TextChanged += (s, e) =>
            {
                if (busy)
                    return;
                var t = (hex.Text ?? string.Empty).TrimStart('#');
                if (t.Length == 6 && uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v))
                    Apply(new ColorRgb888((byte) (v >> 16), (byte) (v >> 8), (byte) v), true, false);
            };
            foreach (var sl in sliders)
                sl.ValueChanged += (s, e) =>
                {
                    if (!busy)
                        Apply(new ColorRgb888((byte) sliders[0].Value, (byte) sliders[1].Value, (byte) sliders[2].Value), false, true);
                };

            var toggle = new Button { Content = swatch, Padding = new Thickness(2) };
            toggle.Click += (s, e) => slidersPanel.IsVisible = !slidersPanel.IsVisible;

            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(toggle);
            head.Children.Add(new TextBlock { Text = Spaced(field.Name), VerticalAlignment = VerticalAlignment.Center, Width = 90 });
            head.Children.Add(hex);
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(head);
            row.Children.Add(slidersPanel);
            return row;
        }

        private static Slider MakeSlider(string label, Panel into)
        {
            var sl = new Slider { Minimum = 0, Maximum = 255, SmallChange = 1, Width = 260 };
            var box = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            box.Children.Add(new TextBlock { Text = label, Width = 16, VerticalAlignment = VerticalAlignment.Center });
            box.Children.Add(sl);
            into.Children.Add(box);
            return sl;
        }

        // ---------- small helpers ----------

        private static Button Btn(string text)
        {
            return new Button { Content = text, Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 8, 8) };
        }

        private static Control Row(params Control[] items)
        {
            var wrap = new WrapPanel();
            foreach (var i in items)
                wrap.Children.Add(i);
            return wrap;
        }

        private static Control Heading(string text)
        {
            return new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) };
        }

        private static string Spaced(string name)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }

        private async Task Guard(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
        }
    }
}
