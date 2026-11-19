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
using ThemeEditor.Common.Audio;
using ThemeEditor.Common.Compression;
using VGAudio.Formats;
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

        /// <summary>Set by the Android head: plays a WAV file image. Null when playback isn't available.</summary>
        public static Action<byte[]> PlayWav;

        private const int SfxMaxBytes = 0x2DC00;

        private CommonTheme _theme;
        private ThemeArchiveContents _archive;
        private string _name = "theme";
        private CwavBlock _sfx = new CwavBlock();
        private byte[] _bgm;
        private bool _dirty, _discardArmed, _busy;
        private string _author = string.Empty;
        private AudioData _pendingAudio;
        private string _pendingName;

        private readonly Image _topImage = new Image { Stretch = Stretch.Uniform, MaxHeight = 220 };
        private readonly Image _bottomImage = new Image { Stretch = Stretch.Uniform, MaxHeight = 220 };
        private readonly StackPanel _colors = new StackPanel { Spacing = 4 };
        private readonly StackPanel _sounds = new StackPanel { Spacing = 8 };
        private readonly StackPanel _music = new StackPanel { Spacing = 8 };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        private readonly TextBox _nameBox = new TextBox { Watermark = "Theme name" };
        private readonly TextBox _authorBox = new TextBox { Watermark = "Author" };
        private readonly CheckBox _cropBox = new CheckBox { Content = "Crop pictures to fill the screen (off: stretch)", IsChecked = true };
        private readonly Button _saveZip = Btn("Save ZIP"), _saveBin = Btn("Save body_LZ.bin");
        private readonly Button _setTop = Btn("Top wallpaper"), _setBottom = Btn("Bottom wallpaper"), _setBoth = Btn("Both screens");
        private readonly TabControl _tabs = new TabControl { TabStripPlacement = Dock.Bottom };
        private readonly List<Expander> _colorSets = new List<Expander>();

        public MainView()
        {
            var open = Btn("Open theme");
            var fresh = Btn("New");
            fresh.Click += (s, e) => { if (ConfirmDiscard()) NewTheme(); };
            _setBoth.Click += async (s, e) => await Guard(SetBothScreens);
            open.Click += async (s, e) => await Guard(OpenTheme);
            _saveZip.Click += async (s, e) => await Guard(() => Save(true));
            _saveBin.Click += async (s, e) => await Guard(() => Save(false));
            _setTop.Click += async (s, e) => await Guard(() => SetWallpaper(true));
            _setBottom.Click += async (s, e) => await Guard(() => SetWallpaper(false));
            _nameBox.TextChanged += (s, e) => { _name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "theme" : _nameBox.Text.Trim(); _dirty = true; };
            _authorBox.TextChanged += (s, e) => { _author = (_authorBox.Text ?? string.Empty).Trim(); _dirty = true; };

            var header = new StackPanel { Spacing = 6, Margin = new Thickness(16, 12, 16, 4) };
            header.Children.Add(new TextBlock { Text = "Umami", FontSize = 26, FontWeight = FontWeight.Bold });
            header.Children.Add(Row(fresh, open, _saveZip, _saveBin));
            header.Children.Add(_status);
            header.Children.Add(_nameBox);
            header.Children.Add(_authorBox);

            var screens = new StackPanel { Spacing = 10 };
            screens.Children.Add(new Border { Background = Brushes.Black, Child = _topImage, CornerRadius = new CornerRadius(4) });
            screens.Children.Add(new Border { Background = Brushes.Black, Child = _bottomImage, CornerRadius = new CornerRadius(4), Width = 280, HorizontalAlignment = HorizontalAlignment.Center });
            screens.Children.Add(_cropBox);
            screens.Children.Add(Row(_setTop, _setBottom, _setBoth));
            screens.Children.Add(new TextBlock { Text = "Pictures are cropped to the exact screen size automatically. \"Both screens\" splits one picture across the top and bottom screen.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

            _tabs.Items.Add(Tab("Screens", screens));
            _tabs.Items.Add(Tab("Colours", _colors));
            _tabs.Items.Add(Tab("Sounds", _sounds));
            _tabs.Items.Add(Tab("Music", _music));

            var dock = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);
            dock.Children.Add(_tabs);
            Content = dock;
            NewTheme();
            SetStatus("Started a new theme. Pick wallpapers, colours and sounds, or open an existing theme.");
        }

        /// <summary>Starts a blank theme from scratch: a gradient wallpaper on both screens and a full set of colours.</summary>
        public void NewTheme()
        {
            var palette = new uint[] { 0xE8382F, 0xF2A33A, 0x4C7A9E, 0x2F2F36, 0xF5EFE6, 0xB7416E, 0x6AA84F, 0xFFD6E0 };
            var colors = new CommonColors();
            var n = 0;
            foreach (var f in typeof(CommonColors).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var set = Activator.CreateInstance(f.FieldType);
                foreach (var cf in f.FieldType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (cf.FieldType == typeof(ColorRgb888))
                        cf.SetValue(set, (ColorRgb888) palette[n++ % palette.Length]);
                f.SetValue(colors, set);
            }
            var theme = new CommonTheme
            {
                Colors = colors,
                Textures = new Textures(),
                CWAV = new byte[0],
                Flags = new Flags
                {
                    ArrowButtonColor = true, ArrowColor = true, BottomBackgroundInnerColor = true, BottomBackgroundOuterColor = true,
                    BottomCornerButtonColor = true, CursorColor = true, DemoTextColor = true, FileColor = true, FolderArrowColor = true,
                    FolderBackgroundColor = true, FolderColor = true, OpenCloseColor = true, TopCornerButtonColor = true,
                    TopDrawType = TopDrawType.Texture, TopFrameType = TopFrameType.Single,
                    BottomDrawType = BottomDrawType.Texture, BottomFrameType = BottomFrameType.Single,
                },
            };
            LoadTheme(theme, "New theme", null);
            ApplyWallpaper(GradientPicture(TopVisible, ScreenHeight, 0xFF8A7A, 0x6A3D8F), true, false);
            ApplyWallpaper(GradientPicture(BottomVisible, ScreenHeight, 0x3A2E5A, 0xE8382F), false, false);
            _dirty = false;
            SetStatus("Started a new theme.");
        }

        private static PixelBuffer GradientPicture(int w, int h, uint from, uint to)
        {
            var bgra = new byte[w * h * 4];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var t = (x / (double) w + y / (double) h) / 2;
                    var i = (y * w + x) * 4;
                    bgra[i] = (byte) (((from & 0xFF) * (1 - t)) + ((to & 0xFF) * t));
                    bgra[i + 1] = (byte) ((((from >> 8) & 0xFF) * (1 - t)) + (((to >> 8) & 0xFF) * t));
                    bgra[i + 2] = (byte) ((((from >> 16) & 0xFF) * (1 - t)) + (((to >> 16) & 0xFF) * t));
                    bgra[i + 3] = 255;
                }
            return new PixelBuffer(w, h, bgra);
        }

        private static TabItem Tab(string title, Control content)
        {
            content.Margin = new Thickness(16, 8, 16, 24);
            return new TabItem
            {
                Header = title,
                FontSize = 14,
                Padding = new Thickness(10, 8),
                Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            };
        }

        /// <summary>Switches to a tab (0 Screens, 1 Colours, 2 Sounds, 3 Music).</summary>
        public void SelectTab(int index)
        {
            _tabs.SelectedIndex = index;
        }

        /// <summary>Opens one colour set in the Colours tab.</summary>
        public void ExpandColorSet(int index)
        {
            if (index >= 0 && index < _colorSets.Count)
                _colorSets[index].IsExpanded = true;
        }

        // ---------- file handling ----------

        private bool ConfirmDiscard()
        {
            if (!_dirty || _discardArmed)
            {
                _discardArmed = false;
                return true;
            }
            _discardArmed = true;
            SetStatus("You have unsaved changes. Tap again to discard them.");
            return false;
        }

        private async Task OpenTheme()
        {
            if (!ConfirmDiscard())
                return;
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

            var fileName = Path.GetFileNameWithoutExtension(files[0].Name);
            LoadTheme(theme, archive != null && !string.IsNullOrWhiteSpace(archive.Name) ? archive.Name : fileName, archive);
            SetStatus("Opened " + files[0].Name);
        }

        /// <summary>Shows a theme: its wallpapers, colours, sound effects and music.</summary>
        public void LoadTheme(CommonTheme theme, string name, ThemeArchiveContents archive)
        {
            _theme = theme;
            _archive = archive;
            _name = name;
            _author = string.Empty;
            if (archive?.Smdh != null)
            {
                try
                {
                    using (var ms = new MemoryStream(archive.Smdh))
                    {
                        var smdh = ThemeEditor.Common.SMDH.SMDH.Read(ms);
                        var t = smdh.AppTitles.FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.Publisher));
                        if (t != null)
                            _author = t.Publisher.Trim();
                    }
                }
                catch (Exception)
                {
                }
            }
            _nameBox.Text = _name;
            _authorBox.Text = _author;
            _sfx = new CwavBlock();
            if (theme.Flags.SoundEffect && theme.CWAV != null && theme.CWAV.Length > 0)
                _sfx.TryImport(theme.CWAV);
            _bgm = archive?.Bgm != null && archive.Bgm.Length > 0 ? archive.Bgm : null;
            _pendingAudio = null;
            Refresh();
            _dirty = false;
            SetStatus("Loaded " + name);
        }

        private async Task Save(bool zip)
        {
            if (_theme == null)
                return;
            var sfxSize = _sfx.Sounds.Values.Sum(f => f.Size);
            if (sfxSize > 0)
            {
                var block = _sfx.Generate();
                if (block.Length > SfxMaxBytes)
                    throw new InvalidOperationException("The sound effects are " + block.Length / 1024 + " kB; the HOME Menu allows " + SfxMaxBytes / 1024 + " kB. Remove or shorten some.");
                _theme.CWAV = block;
                _theme.Flags.SoundEffect = true;
            }
            else
            {
                _theme.CWAV = new byte[0];
                _theme.Flags.SoundEffect = false;
            }
            _theme.Flags.BackgroundMusic = _bgm != null;

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
                var icon = top?.Fit(48, 48, 48, 48, true);
                output = ThemeArchive.Create(_name, compressed, BuildSmdh(icon), _bgm, icon?.EncodePng(), preview);
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
            _dirty = false;
            _discardArmed = false;
            SetStatus("Saved " + file.Name);
        }

        /// <summary>The info.smdh for the theme: the loaded one (or a new one) with this name and author and an icon from the top wallpaper.</summary>
        private byte[] BuildSmdh(PixelBuffer icon)
        {
            ThemeEditor.Common.SMDH.SMDH smdh = null;
            if (_archive?.Smdh != null)
            {
                try
                {
                    using (var ms = new MemoryStream(_archive.Smdh))
                        smdh = ThemeEditor.Common.SMDH.SMDH.Read(ms);
                }
                catch (Exception)
                {
                }
            }
            var fresh = smdh == null;
            smdh = smdh ?? new ThemeEditor.Common.SMDH.SMDH();
            foreach (var t in smdh.AppTitles)
            {
                t.ShortDesc = _name;
                t.LongDesc = _name;
                t.Publisher = string.IsNullOrEmpty(_author) ? t.Publisher : _author;
            }
            if (fresh && icon != null)
            {
                smdh.LargeIcon.Encode(icon.GetBgr24Data(), 48, 48, RawTexture.DataFormat.Bgr565);
                var small = icon.Fit(24, 24, 24, 24, true);
                smdh.SmallIcon.Encode(small.GetBgr24Data(), 24, 24, RawTexture.DataFormat.Bgr565);
            }
            using (var ms = new MemoryStream())
            {
                ThemeEditor.Common.SMDH.SMDH.Write(smdh, ms);
                return ms.ToArray();
            }
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
            ApplyWallpaper(image, top, _cropBox.IsChecked == true);
            SetStatus((top ? "Top" : "Bottom") + " wallpaper replaced.");
        }

        private async Task SetBothScreens()
        {
            var picked = await Pick("Choose a picture for both screens");
            if (picked == null)
                return;
            PixelBuffer image;
            using (var ms = new MemoryStream(picked.Value.bytes))
                image = PixelBuffer.Load(ms);
            // One picture spanning the top screen over the bottom screen, cropped to fill, then cut into the two screens
            var all = image.Fit(TopVisible, ScreenHeight * 2, TopVisible, ScreenHeight * 2, _cropBox.IsChecked == true);
            ApplyWallpaper(all.Crop(0, 0, TopVisible, ScreenHeight), true, false);
            ApplyWallpaper(all.Crop((TopVisible - BottomVisible) / 2, ScreenHeight, BottomVisible, ScreenHeight), false, false);
            SetStatus("Picture split across both screens.");
        }

        /// <summary>Fits a picture to the top or bottom screen and writes it into the theme.</summary>
        public void ApplyWallpaper(PixelBuffer image, bool top, bool crop)
        {
            var visible = top ? TopVisible : BottomVisible;
            var fitted = image.Fit(visible, ScreenHeight, TexW, TexH, crop);

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
            RefreshPreview();
            _dirty = true;
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
            _saveZip.IsEnabled = _saveBin.IsEnabled = _setTop.IsEnabled = _setBottom.IsEnabled = _setBoth.IsEnabled = _nameBox.IsEnabled = _authorBox.IsEnabled = loaded;
            RefreshPreview();
            BuildColors();
            BuildSounds();
            BuildMusic();
        }

        private void RefreshPreview()
        {
            var loaded = _theme != null;
            _topImage.Source = loaded ? DecodeTexture(_theme.Textures?.Top, 400)?.ToBitmap() : null;
            _bottomImage.Source = loaded ? DecodeTexture(_theme.Textures?.Bottom, BottomVisible)?.ToBitmap() : null;
        }

        // ---------- sound effects ----------

        private static readonly string[] SoundNames =
        {
            "Cursor", "Launch a title", "Open folder", "Close folder", "Page flip 1", "Page flip 2", "Page flip 3", "Open lid",
        };

        private void BuildSounds()
        {
            _sounds.Children.Clear();
            if (_theme == null)
            {
                _sounds.Children.Add(new TextBlock { Text = "Open a theme to edit its sound effects.", Opacity = 0.7 });
                return;
            }
            var size = _sfx.EstimatedSize;
            var bar = new ProgressBar { Maximum = SfxMaxBytes, Value = Math.Min(size, SfxMaxBytes), Height = 10 };
            _sounds.Children.Add(new TextBlock { Text = "Sound effects: " + size / 1024 + " of " + SfxMaxBytes / 1024 + " kB", Foreground = size > SfxMaxBytes ? Brushes.IndianRed : null });
            _sounds.Children.Add(bar);
            _sounds.Children.Add(new TextBlock { Text = "Pick any WAV, MP3 or OGG (or a .bcwav): it is converted to mono 22 kHz for the HOME Menu.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

            for (var i = 0; i < 8; i++)
            {
                var kind = (CwavKind) i;
                var file = _sfx.Sounds[kind];
                var has = file.Size > 0 && file.WavData.Length > 0;
                var card = new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)) };
                var box = new StackPanel { Spacing = 6 };
                box.Children.Add(new TextBlock { Text = SoundNames[i], FontWeight = FontWeight.SemiBold });
                box.Children.Add(new TextBlock { Text = has ? file.Size / 1024.0 > 1 ? (file.Size / 1024) + " kB" : file.Size + " bytes" : "No sound", Opacity = 0.7 });
                var play = SmallBtn("Play"); play.IsEnabled = has;
                var import = SmallBtn("Import");
                var export = SmallBtn("Export"); export.IsEnabled = has;
                var remove = SmallBtn("Remove"); remove.IsEnabled = has;
                play.Click += (s, e) => PlaySound(file.WavData);
                import.Click += async (s, e) => await Guard(() => ImportSound(kind));
                export.Click += async (s, e) => await Guard(() => ExportSound(kind, file));
                remove.Click += (s, e) => { file.CwavData = CwavFile.EmptyData; _dirty = true; BuildSounds(); };
                box.Children.Add(Row(play, import, export, remove));
                card.Child = box;
                _sounds.Children.Add(card);
            }
        }

        private async Task ImportSound(CwavKind kind)
        {
            var picked = await Pick("Choose a sound effect");
            if (picked == null)
                return;
            var ext = Path.GetExtension(picked.Value.name).ToLowerInvariant();
            byte[] cwav;
            if (ext == ".bcwav" || ext == ".cwav")
                cwav = picked.Value.bytes;
            else
            {
                SetStatus("Converting…");
                var bytes = picked.Value.bytes;
                cwav = await Task.Run(() =>
                {
                    using (var ms = new MemoryStream(bytes))
                        return CwavFile.EncodeCwav(ms, ext);
                });
            }
            if (cwav.Length == 0)
                throw new InvalidDataException("That file couldn't be converted to a sound effect.");
            _sfx.Sounds[kind].CwavData = cwav;
            _dirty = true;
            BuildSounds();
            SetStatus(SoundNames[(int) kind] + " replaced.");
        }

        private async Task ExportSound(CwavKind kind, CwavFile file)
        {
            var wav = new FilePickerFileType("WAV audio") { Patterns = new[] { "*.wav" } };
            var cwav = new FilePickerFileType("BCWAV sound") { Patterns = new[] { "*.bcwav" } };
            var saved = await SaveBytes("Export sound", kind.ToString().ToLowerInvariant() + ".wav", new[] { wav, cwav },
                name => name.EndsWith(".bcwav", StringComparison.OrdinalIgnoreCase) ? file.CwavData : file.WavData);
            if (saved != null)
                SetStatus("Exported " + saved);
        }

        private void PlaySound(byte[] wav)
        {
            if (PlayWav == null)
                SetStatus("Audio playback isn't available here.");
            else if (wav == null || wav.Length == 0)
                SetStatus("There is nothing to play.");
            else
                try { PlayWav(wav); } catch (Exception ex) { SetStatus("Couldn't play: " + ex.Message); }
        }

        // ---------- music ----------

        private static readonly int[] Rates = { 0, 32000, 22050, 16000 };

        private void BuildMusic()
        {
            _music.Children.Clear();
            if (_theme == null)
            {
                _music.Children.Add(new TextBlock { Text = "Open a theme to edit its background music.", Opacity = 0.7 });
                return;
            }

            if (_bgm != null)
            {
                string detail;
                try
                {
                    var info = BcstmConverter.GetInfo(BcstmConverter.ReadBcstm(_bgm));
                    detail = TimeSpan.FromSeconds(info.Duration).ToString(@"m\:ss") + ", " + info.SampleRate + " Hz, " + (info.Looping ? "loops" : "plays once");
                }
                catch (Exception)
                {
                    detail = "unreadable BCSTM";
                }
                var over = _bgm.Length > BcstmConverter.MaxThemeBgmBytes;
                _music.Children.Add(new TextBlock { Text = "Background music", FontWeight = FontWeight.SemiBold });
                _music.Children.Add(new TextBlock { Text = detail });
                _music.Children.Add(new TextBlock
                {
                    Text = _bgm.Length / 1024 + " of " + BcstmConverter.MaxThemeBgmBytes / 1024 + " kB" + (over ? " (too large for the HOME Menu)" : string.Empty),
                    Foreground = over ? Brushes.IndianRed : null,
                });
                _music.Children.Add(new ProgressBar { Maximum = BcstmConverter.MaxThemeBgmBytes, Value = Math.Min(_bgm.Length, BcstmConverter.MaxThemeBgmBytes), Height = 10 });
                var play = SmallBtn("Play");
                var bcstm = SmallBtn("Export BCSTM");
                var wav = SmallBtn("Export WAV");
                var remove = SmallBtn("Remove");
                play.Click += (s, e) => Guard(() => { PlaySound(BcstmConverter.BcstmToWav(_bgm)); return Task.CompletedTask; });
                bcstm.Click += async (s, e) => await Guard(() => ExportMusic(false));
                wav.Click += async (s, e) => await Guard(() => ExportMusic(true));
                remove.Click += (s, e) => { _bgm = null; _dirty = true; BuildMusic(); SetStatus("Background music removed."); };
                _music.Children.Add(Row(play, bcstm, wav, remove));
            }
            else
            {
                _music.Children.Add(new TextBlock { Text = "This theme has no background music.", Opacity = 0.7 });
            }

            var import = Btn(_bgm == null ? "Add music" : "Replace music");
            import.Click += async (s, e) => await Guard(PickMusic);
            _music.Children.Add(import);
            _music.Children.Add(new TextBlock { Text = "WAV, MP3, OGG, BCSTM, BFSTM, BRSTM and more. It is converted to stereo BCSTM.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

            if (_pendingAudio != null)
                _music.Children.Add(BuildConvertPanel());
        }

        private async Task PickMusic()
        {
            var picked = await Pick("Choose music");
            if (picked == null)
                return;
            var ext = Path.GetExtension(picked.Value.name).ToLowerInvariant();
            var bytes = picked.Value.bytes;
            SetStatus("Reading " + picked.Value.name + "…");
            _pendingAudio = await Task.Run(() =>
            {
                using (var ms = new MemoryStream(bytes))
                    return BcstmConverter.Load(ms, ext);
            });
            _pendingName = picked.Value.name;
            BuildMusic();
            SetStatus("Choose how to convert " + _pendingName + ".");
        }

        private Control BuildConvertPanel()
        {
            var info = BcstmConverter.GetInfo(_pendingAudio);
            var codec = new ComboBox { ItemsSource = new[] { "DSP-ADPCM (small)", "PCM 16-bit (large)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var rate = new ComboBox { ItemsSource = new[] { "Keep original (" + info.SampleRate + " Hz)", "32000 Hz", "22050 Hz", "16000 Hz" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var loop = new CheckBox { Content = "Loop the whole track", IsChecked = true };
            var estimate = new TextBlock { TextWrapping = TextWrapping.Wrap };

            void Update()
            {
                var r = Rates[Math.Max(0, rate.SelectedIndex)];
                var eff = r == 0 ? info.SampleRate : r;
                var samples = info.Duration * eff;
                var bytes = (codec.SelectedIndex == 1 ? samples * 2 : samples * 8 / 14) * 2 + 0x1000;
                var tooBig = bytes > BcstmConverter.MaxThemeBgmBytes;
                estimate.Text = "About " + (int) (bytes / 1024) + " kB of " + BcstmConverter.MaxThemeBgmBytes / 1024 + " kB"
                    + (tooBig ? ": too large. Use ADPCM, a lower rate or a shorter track." : string.Empty);
                estimate.Foreground = tooBig ? Brushes.IndianRed : null;
            }
            codec.SelectionChanged += (s, e) => Update();
            rate.SelectionChanged += (s, e) => Update();
            Update();

            var convert = Btn("Convert and use");
            var cancel = Btn("Cancel");
            cancel.Click += (s, e) => { _pendingAudio = null; BuildMusic(); };
            convert.Click += async (s, e) => await Guard(async () =>
            {
                var r = Rates[Math.Max(0, rate.SelectedIndex)];
                var options = new BcstmOptions
                {
                    Codec = codec.SelectedIndex == 1 ? BcstmCodec.Pcm16 : BcstmCodec.DspAdpcm,
                    SampleRate = r == 0 ? (int?) null : r,
                    Loop = loop.IsChecked == true,
                    LoopStart = 0,
                    LoopEnd = 0,
                };
                var audio = _pendingAudio;
                SetStatus("Converting music, this can take a moment…");
                var result = await Task.Run(() => BcstmConverter.Encode(audio, options));
                _bgm = result;
                _dirty = true;
                _pendingAudio = null;
                BuildMusic();
                SetStatus("Music added" + (result.Length > BcstmConverter.MaxThemeBgmBytes ? ", but it is too large for the HOME Menu." : "."));
            });

            var box = new StackPanel { Spacing = 8 };
            box.Children.Add(new TextBlock { Text = "Convert " + _pendingName, FontWeight = FontWeight.SemiBold });
            box.Children.Add(new TextBlock { Text = TimeSpan.FromSeconds(info.Duration).ToString(@"m\:ss") + ", " + info.SampleRate + " Hz", Opacity = 0.7 });
            box.Children.Add(codec);
            box.Children.Add(rate);
            box.Children.Add(loop);
            box.Children.Add(estimate);
            box.Children.Add(Row(convert, cancel));
            return new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)), Child = box };
        }

        private async Task ExportMusic(bool wav)
        {
            var type = wav ? new FilePickerFileType("WAV audio") { Patterns = new[] { "*.wav" } } : new FilePickerFileType("BCSTM music") { Patterns = new[] { "*.bcstm" } };
            var saved = await SaveBytes("Export music", wav ? "bgm.wav" : "bgm.bcstm", new[] { type },
                name => wav ? BcstmConverter.BcstmToWav(_bgm) : _bgm);
            if (saved != null)
                SetStatus("Exported " + saved);
        }

        // ---------- file picker helpers ----------

        private async Task<(string name, byte[] bytes)?> Pick(string title)
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp == null)
                return null;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
            if (files.Count == 0)
                return null;
            return (files[0].Name, await ReadAll(files[0]));
        }

        private async Task<string> SaveBytes(string title, string suggested, IReadOnlyList<FilePickerFileType> types, Func<string, byte[]> data)
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp == null)
                return null;
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggested, FileTypeChoices = types });
            if (file == null)
                return null;
            var bytes = data(file.Name);
            using (var s = await file.OpenWriteAsync())
            {
                s.SetLength(0);
                await s.WriteAsync(bytes, 0, bytes.Length);
            }
            return file.Name;
        }

        private static Button SmallBtn(string text)
        {
            return new Button { Content = new TextBlock { Text = text }, Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 6, 6) };
        }

        private void BuildColors()
        {
            _colors.Children.Clear();
            _colorSets.Clear();
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
                {
                    var exp = new Expander { Header = Spaced(setField.Name), Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    _colorSets.Add(exp);
                    _colors.Children.Add(exp);
                }
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
                if (!Equals(field.GetValue(set), c))
                    _dirty = true;
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
            return new Button { Content = new TextBlock { Text = text }, Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 8, 8) };
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
            if (_busy)
                return;
            _busy = true;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
