# Umami Mobile

An Android port of [Umami 3DS Theme Editor](https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor): make custom
themes for the Nintendo 3DS HOME Menu from your phone.

* Open a theme (`.zip` from Theme Plaza / Themezer, or a bare `body_LZ.bin`)
* Preview the top and bottom screen wallpapers
* Replace the wallpapers with any picture (cropped to fill, or stretched)
* Edit every colour of the theme (hex or RGB sliders)
* Save as a theme ZIP or a `body_LZ.bin`

The theme format code (LZ11, textures, theme/SMDH IO) is the same library as the desktop editor; the UI is
[Avalonia](https://avaloniaui.net/) on .NET 8. Not in the mobile version yet: sound effects, music conversion, the resource
library, flag editing, undo/redo and scrolling/patterned backgrounds.

## Install

Download the APK from the [latest release](https://github.com/BakuretsuBaka/UmamiMobile/releases/latest) and open it on your
Android 7.0+ phone (allow "install unknown apps" for your browser or file manager when asked).

## Building

CI builds the APK (`.github/workflows/build.yml`). Locally: install the .NET 8 SDK and JDK 17, then

```bash
dotnet workload install android
dotnet publish src/UmamiMobile.Android -c Release -f net8.0-android
```

## Credits

Based on [usagirei/3DS-Theme-Editor](https://github.com/usagirei/3DS-Theme-Editor) (MIT) and the Umami fork. MIT licensed.
