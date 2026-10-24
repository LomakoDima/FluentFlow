# FluentFlow icon

FluentFlow's application icon and mark, and how they were made. The app uses them (see *In the app* below); this
folder is the source.

Open [`board.html`](board.html) in a browser to compare the six concepts and see the final mark at every size.

## The mark: Current

FluentFlow's mirrored visualizer (five bars whose outline is a lens) with one wave of negative space flowing through
it. Above the wave the bars are warm light; below it they deepen. It is what FluentFlow draws on your taskbar, plus the
flow that gives the app its name, on the dark surface of the redesigned player.

- **Grid:** 256. Plate 216 with a 56 corner radius. Bars 22 wide on a 36 pitch, heights 56 / 104 / 152 / 104 / 56,
  centred. The wave is two cubic curves (`M 30,136 C 62,108 94,108 128,128 C 162,148 194,148 226,120`), cut 10 wide.
- **Colour:** above the wave #FCD8C4 → #F0A88E, below #DE8268 → #A9473A, plate #2E2433 → #141117. The peach is the
  one the player and the taskbar visualizer already use.
- **Small sizes:** 16, 20, 24 and 32 px are redrawn on the pixel grid (`final/small`). The cut would land on half
  pixels there and break up the short outer bars, so those sizes keep the wave as the line where warm meets deep.
  From 40 px up the master scales as it is.

### How it was chosen

Six directions were drawn (`concepts/`) and scored on originality, recognisability, quality, scalability, fit with
FluentFlow and fit with Windows 11:

| | Concept | Idea | Verdict |
| --- | --- | --- | --- |
| 01 | Signal | The taskbar visualizer as a lens of bars | Best fit and scales well, but a plain equalizer |
| 02 | Tide | A record split by one wave | Too close to a well-known split-globe logo; dark half vanishes on a dark taskbar |
| 03 | Twist | A ribbon turning over | Reads as a bow tie when small |
| 04 | Horizon | Sound rising off the taskbar | A scene, not a symbol; blurs below 32 px |
| 05 | Loop | One line crossing itself | Reads as an infinity sign |
| 06 | Drop | A wave through a drop | Clean, but says water before music |

The final mark combines Signal's recognisability with the single flowing cut of Tide and Drop. `refine/` keeps the
steps in between, including a rejected version where the bars climbed a slope and looked like a signal-strength icon.

## Files

| File | What it is |
| --- | --- |
| `final/FluentFlow.svg` | Master vector (dark plate) |
| `final/FluentFlow.png` | 1024 × 1024 |
| `final/FluentFlow.ico` | App icon: 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 (32-bit bitmaps below 256, PNG at 256) |
| `final/png/FluentFlow-<size>.png` | 16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512 |
| `final/small/FluentFlow-<size>.svg` | The pixel-tuned sources for 16 to 32 px |
| `final/variants/FluentFlow-dark.*` | Dark theme (same as the master) |
| `final/variants/FluentFlow-light.*` | Light theme: light plate, deeper bars |
| `final/variants/FluentFlow-transparent.*` | The mark alone, for dark backgrounds that bring their own surface |
| `final/variants/FluentFlow-mono.svg` | One colour (`currentColor`), for recolouring |
| `final/variants/FluentFlow-mono-black.*`, `-mono-white.*` | One colour, for light and dark backgrounds |
| `final/tray/FluentFlow-tray-dark-taskbar.*` | Notification-area glyph, white (16, 20, 24, 32 and an .ico) |
| `final/tray/FluentFlow-tray-light-taskbar.*` | The same, near-black for a light taskbar |

## Rebuilding

Everything in `final/` is generated from the geometry in [`build_icons.py`](build_icons.py):

```powershell
python build_icons.py
```

It needs Python 3.10 or later, Pillow, and Microsoft Edge, which it runs headless to rasterise the SVGs.

## In the app

- `FluentFlow/Assets/FluentFlow.ico` is a copy of `final/FluentFlow.ico`: the exe's icon (`ApplicationIcon` in
  `FluentFlow.csproj`) and the window icon of the player and the settings window.
- `FluentFlow/Assets/FluentFlow-256.png` is a copy of `final/png/FluentFlow-256.png`, shown in the settings' About card.
- The settings title bar draws the mark as XAML (`FluentFlowMark` in `Resources/SettingsStyles.xaml`), in its
  small-size form.

After changing the geometry here, run `build_icons.py` and copy those two files over again.

FluentFlow has no notification-area icon today; the tray glyphs are ready for the day it does. For GitHub, use
`final/FluentFlow.png` (or `final/png/FluentFlow-512.png`) as the repository's social preview image.
