# Auxe

Pick where each Windows app plays. Click the dancing speaker near the clock to send Discord to a headset, Chrome to a monitor, or YouTube Music to separate speakers.

**[Download AuxeSetup.exe for Windows](https://github.com/rasithsenanayake/auxe/releases/latest/download/AuxeSetup.exe)**

## Screenshots

| App outputs | Expanded Chrome tabs |
| --- | --- |
| ![Auxe showing separate outputs for Discord, Chrome, WhatsApp, and YouTube Music](screenshots/app-outputs.png) | ![Auxe showing individual output selectors for two Chrome tabs](screenshots/chrome-tabs.png) |

Screenshots use sample apps and devices.

## Install

Download and run **AuxeSetup.exe** on Windows 11 x64. Click **Install Auxe** to add it to your Start menu and open the tray app. The installer is unsigned.

![Auxe Setup window with an Install Auxe button](screenshots/setup.png)

To build from source, run `.\build.ps1` in PowerShell. It creates `bin\AuxeSetup.exe`.

Start audio in an app, open Auxe, and pick its output. Choose **System default** to clear a route. If an app stays on the old output, restart playback or rejoin the call.

## Chrome tabs and YouTube Music

Load `%LOCALAPPDATA%\Programs\Auxe\chrome` as an unpacked extension from `chrome://extensions` with **Developer mode** enabled. Play audio in Chrome or the installed YouTube Music app and press **Ctrl+Shift+U** to enable that source for the current session. Expand Chrome in Auxe to see its tabs.

If only the default output appears, use **Allow device access** in the extension options. Chrome may ask for microphone permission to list playback devices. Auxe stops the permission-check stream immediately.
