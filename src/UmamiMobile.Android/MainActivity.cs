using System.IO;
using Android.App;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Avalonia;
using Avalonia.Android;

namespace UmamiMobile.Android
{
    [Activity(
        Label = "Umami",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity<App>
    {
        private MediaPlayer _player;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            MainView.PlayWav = PlayWav;
            base.OnCreate(savedInstanceState);
        }

        protected override void OnDestroy()
        {
            _player?.Release();
            _player = null;
            base.OnDestroy();
        }

        private void PlayWav(byte[] wav)
        {
            var path = Path.Combine(CacheDir.AbsolutePath, "preview.wav");
            File.WriteAllBytes(path, wav);
            _player?.Release();
            _player = new MediaPlayer();
            _player.SetDataSource(path);
            _player.Prepare();
            _player.Start();
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder);
        }
    }
}
