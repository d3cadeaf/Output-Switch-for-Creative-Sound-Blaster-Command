# Output Switch for Creative Sound Blaster Command

A small Windows tray utility that switches a Creative SoundCore playback device between headphones and speakers with a configurable global hotkey. Sound Blaster Command must be installed, but it does not need to remain open while switching.

**Created with OpenAI Codex.**

## Download and run

Download the latest `Output-Switch-for-Creative-Sound-Blaster-Command-Windows-x86.zip` from the repository's **Releases** page, extract it, and run `Start Output Switch.cmd`.

The target computer must have Creative Sound Blaster Command and its device driver installed. The program locates Creative's `Interop.CtSndCr.dll` in the standard Sound Blaster Command installation folder at startup; that third-party file is not included in the download.

## Features

- Global toggle hotkey: `Ctrl+Alt+F12` by default
- Tray icon shows the active output and refreshes when the output changes elsewhere
- Tray menu can select headphones or speakers directly, toggle the output, change the hotkey, or exit
- Hotkey choice persists across restarts
- No administrator privileges required

## Compatibility

The application communicates with Creative's SoundCore interface and selects the `SelectOutput` parameter (`0` = headphones, `1` = speakers). It may work with other Creative sound cards that expose a compatible SoundCore endpoint and parameter. **Only the Creative Sound Blaster Z has been tested so far.**

When several Creative playback devices are present, the application prefers an endpoint named Sound Blaster Z or the Windows default playback endpoint. Other devices and driver versions have not been verified.

## Build from source

The project targets .NET Framework 4.0 and x86. Build on Windows with Sound Blaster Command installed, from the project directory:

```cmd
if not exist bin\Release mkdir bin\Release
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /nologo /target:winexe /platform:x86 /langversion:5 /main:HeadphoneHotkey.Bootstrap /out:bin\Release\HeadphoneHotkey.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"C:\Program Files (x86)\Creative\Sound Blaster Command\Platform\Interop.CtSndCr.dll" /win32manifest:app.manifest Program.cs
```

## Technical notes

The program calls Creative `ISoundCore.GetParamValue` and `ISoundCore.SetParamValue` directly. On the tested Sound Blaster Z, it uses feature ID `0x01000001` and `SelectOutput` parameter ID `15` with a DWORD value. It does not read or write registry values. The selected driver output is the source of truth.

A live switch on the tested card was confirmed in both directions. The driver markers `SpeakerMode` and `MultiplexOutput` changed from `0/0` to `1/1` and back. Static analysis initially suggested parameter 19, but runtime enumeration did not expose that parameter on the tested device.

The selected hotkey is stored in `%LOCALAPPDATA%\HeadphoneHotkey\settings.ini`; diagnostics are written to `%LOCALAPPDATA%\HeadphoneHotkey\HeadphoneHotkey.log`.
