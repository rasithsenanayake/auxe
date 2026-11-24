# Soundmix

Soundmix puts a small, animated speaker in the Windows notification area. Click it to see which apps are playing and choose where each one plays. For example, send Discord to your headset, Chrome and WhatsApp to monitor speakers, and the installed YouTube Music app to Microlab speakers. Windows may list analog speakers by the name of the sound card they plug into.

## Install on Windows 11 x64

1. Run `bin\SoundmixSetup.exe`. Setup installs for your Windows account, adds a Start menu shortcut, and launches Soundmix. It uses the .NET Framework already included with Windows 11.
2. If the speaker is under the tray overflow arrow, drag it beside the clock. Right-click it to exit.
3. Start audio in an app, then pick an output beside its name in Soundmix. Use **System default** to clear that app's preference.

Windows can apply a changed route when playback reconnects. If the app stays on its old output, restart the app or rejoin the call. If the app has its own device setting, set it to its default output so it can follow Windows routing. Soundmix changes playback preferences only.

## Optional Chrome companion

1. Open `chrome://extensions`, enable **Developer mode**, and choose **Load unpacked**. Select `%LOCALAPPDATA%\Programs\Soundmix\chrome`.
2. Open the companion's **Extension options**, then choose **Allow device access** if only the default output is listed. Chrome may request microphone permission to expose output device names. The permission check stops its stream immediately. Playback stays local, and the native connection carries only display and routing data.
3. Play audio in your tab or installed YouTube Music window. Use **Ctrl+Shift+U** or the companion's toolbar button where available. You can change the shortcut at `chrome://extensions/shortcuts`. Repeat the activation for each new tab or window you want to route.
4. Expand **Chrome** in Soundmix to see its tabs. The installed YouTube Music window has its own row when Chrome identifies it as standalone. Enable audio routing again for new tabs or after relaunching Chrome.

If a source cannot be enabled or Chrome does not expose a particular output, it can still follow Chrome's Windows-level route. Per-tab capture depends on Chrome's compatibility with the source.

## Build

From PowerShell in this folder:

```powershell
.\build.ps1
```

The build uses the Windows .NET Framework compiler and produces `bin\Soundmix.exe` and `bin\SoundmixSetup.exe`. There are no NuGet or npm dependencies. The installer is unsigned. To remove it, use **Settings → Apps → Installed apps → Soundmix** and remove the optional companion from Chrome separately.
