# FluentFlow

A small Windows media controller with a live audio visualizer that sits on the taskbar. It shows whatever
Windows is playing (Spotify, a browser tab, a media player, anything that talks to the system media controls),
lets you control it, and gives you a Windows 11 style volume flyout.

Inspired by [FluentFlyout](https://github.com/unchihugo/FluentFlyout).

## Screenshots

The visualizer on the taskbar (a real capture, enlarged 3x):

![Taskbar widget](docs/taskbar-widget.png)

The player window and the volume flyout (sample data):

<p>
  <img src="docs/player.png" alt="Player window" width="340">
</p>
<p>
  <img src="docs/flyout-dark.png" alt="Volume flyout, dark theme" width="368">
  <img src="docs/flyout-light.png" alt="Volume flyout, light theme" width="368">
</p>

## Features

- **Taskbar widget.** A mirrored spectrum visualizer docked next to the notification area. Click it to open the
  player; right-click for *Open*, *Start with Windows* and *Exit*.
- **Compact player window.** Cover art, title, artist, previous / play-pause / next, progress. It opens next to the
  widget and hides again with the widget, the close button or a second click.
- **Volume flyout.** Click the speaker icon: real system volume and mute, mouse wheel (2% per notch), keyboard
  (arrows, Esc) and the name of the current output device. Follows the Windows light/dark theme and accent colour.
- **Start with Windows.** A per-user entry (`HKCU\...\Run`), no elevation. Respects Task Manager's *Startup apps* switch.
- **Fallback.** If the taskbar can't host the widget, the player is shown as an ordinary window instead.

## Download

Grab `FluentFlow-win-x64.zip` from the [Releases](../../releases) page, unzip it and run `FluentFlow.exe`.
It needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

## Requirements

- Windows 10 version 2004 or later (the taskbar widget is designed for the Windows 11 taskbar)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build, the .NET 10 Desktop Runtime to run

## Build and run

```powershell
dotnet build FluentFlow.slnx
dotnet run --project FluentFlow
```

Or open `FluentFlow.slnx` in Visual Studio and press F5.

Publish a framework-dependent build:

```powershell
dotnet publish FluentFlow/FluentFlow.csproj -c Release -r win-x64 --self-contained false -o publish
```

## Command line

| Argument | Effect |
| --- | --- |
| `--no-widget` | Don't use the taskbar; run as an ordinary window. |
| `--autostart` | Set by the *Start with Windows* entry. Waits up to a minute for the taskbar before falling back to a window. |
| `--theme light` / `--theme dark` | Pin the flyout theme instead of following Windows (for testing). |

Only one copy runs at a time; starting a second one brings up the player of the first.

## How the taskbar widget works

The widget is a small WPF window that is re-parented to the taskbar (`Shell_TrayWnd`) and docked to the left of the
notification area (or to its right on right-to-left systems). That is the only way to put your own pixels on the
Windows 11 taskbar, and it comes with caveats:

- It is positioned from the taskbar's real geometry and re-checked every second, so it follows DPI, resolution,
  taskbar size and auto-hide changes. After an Explorer restart it is rebuilt.
- The positions of the taskbar buttons are read through UI Automation on a background thread. If the buttons
  reach the notification area the widget shrinks, and when there is no room at all the app switches to a normal window.
- Vertical taskbars and taskbars thinner than about 24 px are not supported (normal window instead).
- The widget is shown on the primary taskbar only.
- A child window shares input with the taskbar, so FluentFlow keeps its UI thread free of blocking work.

The placement maths is pure geometry and covered by unit tests, including other taskbar edges, DPI scales,
secondary monitors, small buttons, crowded taskbars and right-to-left layouts.

## Tests

```powershell
dotnet test FluentFlow.slnx
```

## Project layout

| Path | Contents |
| --- | --- |
| `FluentFlow/Controls` | Visualizer, taskbar widget (window, host, docking maths), volume flyout, popup helper |
| `FluentFlow/Services` | Media session, audio capture and FFT, system volume, theme, startup registration |
| `FluentFlow/Resources` | Styles and Windows 11 style resources |
| `FluentFlow.Tests` | Unit tests for the geometry |

## License

[MIT](LICENSE)

## Privacy

System audio is captured with WASAPI loopback only to draw the visualizer. It is analysed in memory and never
stored or sent anywhere.
