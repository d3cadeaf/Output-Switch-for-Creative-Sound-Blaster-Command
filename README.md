# Output Switch for Creative Sound Blaster Command

A small Windows tray utility that switches a Creative SoundCore playback device between headphones and speakers with a configurable global hotkey. Sound Blaster Command does not need to be open while switching.

**Created with OpenAI Codex.**

## Features

- Global toggle hotkey: `Ctrl+Alt+F12` by default
- Tray icon shows the currently selected output and updates when the output changes elsewhere
- Tray menu can select headphones or speakers directly, toggle the output, change the hotkey, or exit
- Hotkey choice persists across restarts
- No administrator privileges required

## Compatibility

The application communicates with Creative's SoundCore interface and selects the `SelectOutput` parameter (`0` = headphones, `1` = speakers). It may work with other Creative sound cards that expose a compatible SoundCore endpoint and parameter. **Only the Creative Sound Blaster Z has been tested so far.**

When several Creative playback devices are present, the application prefers an endpoint named Sound Blaster Z or the Windows default playback endpoint. Other devices and driver versions have not been verified.

## Run

Build the application on a Windows computer with Creative Sound Blaster Command installed. Start `Start HeadphoneHotkey.cmd`; the program runs in the notification area. Right-click its tray icon to select an output or change the hotkey. `Ctrl+Alt+F10` exits the program.

The Creative interop assembly `Interop.CtSndCr.dll` is supplied by the Sound Blaster Command installation and is intentionally not included in this public source repository. The project expects it at:

`C:\Program Files (x86)\Creative\Sound Blaster Command\Platform\Interop.CtSndCr.dll`

## Build

The project targets .NET Framework 4.0 and x86. From the project directory, create the output directory and then run:

```cmd
mkdir bin\Release
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /nologo /target:winexe /platform:x86 /langversion:5 /out:bin\Release\HeadphoneHotkey.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"C:\Program Files (x86)\Creative\Sound Blaster Command\Platform\Interop.CtSndCr.dll" /win32manifest:app.manifest Program.cs
```

The locally generated executable and package are not tracked in this repository because they bundle Creative's third-party interop assembly. The selected hotkey is stored in `%LOCALAPPDATA%\HeadphoneHotkey\settings.ini`; diagnostics are written to `%LOCALAPPDATA%\HeadphoneHotkey\HeadphoneHotkey.log`.

## Technical notes

The program calls Creative `ISoundCore.GetParamValue` and `ISoundCore.SetParamValue` directly. On the tested Sound Blaster Z, it uses feature ID `0x01000001` and `SelectOutput` parameter ID `15` with a DWORD value. It does not read or write registry values. The device's selected output is the source of truth.

A live switch on the tested card was confirmed in both directions. The driver markers `SpeakerMode` and `MultiplexOutput` changed from `0/0` to `1/1` and back. Static analysis initially suggested parameter 19, but runtime enumeration did not expose that parameter on the tested device.

## Project files

- `Program.cs` – tray application and direct SoundCore integration
- `HeadphoneHotkey.csproj` – Visual Studio project
- `app.manifest` – application execution level
- `Start HeadphoneHotkey.cmd` – launcher for a locally built executable

