# Puffer Helper

A small Windows helper for Krillion's **Puffer Party**. When a category comes up, press a hotkey and a
list of answers appears in a little always-on-top window. It also shows them crossed off as they get played. You still type the answers
yourself.

It only looks at your screen. It doesn't touch the game, its network traffic or its servers.

How it works: the hotkey takes a screenshot of the game area and sends it to Claude (Anthropic's API), which
reads the category and returns about 25 answers, safest first. That's one request per category, made while your
friends are still taking their turns. Crossing off what others have played uses Windows' own built-in text
recognition, which is free and doesn't go online.

## What you need

- Windows 10 (version 2004 or later) or Windows 11
- .NET 10 SDK (`dotnet --list-sdks` should list 10.x)
- An Anthropic API key with a little credit on it

## Setup

1. **Create an API key.** Sign in at <https://platform.claude.com>, add some credit under Billing, then create a
   key under API Keys. It starts with `sk-ant-`.
2. **Save it as an environment variable** for your Windows user (paste your own key):

   ```powershell
   [Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...", "User")
   ```

   Programs that were already open don't see the new variable, so open a **new** terminal before the next step.
   The helper reads the key from the environment and never stores it anywhere.
3. **Build and start it** from this folder:

   ```powershell
   dotnet run -c Release
   ```

   Or build once with `dotnet build -c Release` and start
   `bin\Release\net10.0-windows10.0.19041.0\PufferHelper.exe` directly. You can pin that exe to Start.
4. **Optional dry run:** `dotnet run -c Release -- --demo` fills the window with a sample list without calling
   the API. Use it to drag the window to a good spot beside the game. The position is remembered.

## Picking the screen region

Press **Ctrl+Alt+R** with the game showing. The screen freezes and dims. Drag a box around the part of the game
that shows **the category and the answers played so far**, then let go. The area is saved.

- **Esc** cancels. **Right-click** means "use the whole screen".
- Until you pick an area, the helper captures the whole screen the mouse is on. That works, but a tight box
  is faster, cheaper, and keeps chat windows and notifications out of what gets sent to the API.
- If the game window moves or you change the zoom, pick the area again.

## During a game

| Hotkey | What it does |
|---|---|
| **Ctrl+Alt+Q** | New category on screen: get answers. Press it again later in the round to get a fresh list that skips everything used so far. |
| **Ctrl+Alt+W** | Cross off answers the others have already played, read from the screen. Instant and free. |
| **Ctrl+Alt+R** | Pick the game area. |
| **Ctrl+Alt+H** | Hide or show the window. |

- The highlighted answer is the first one not used yet. That's the one to type.
- **Click** an answer to cross it off, for example after you've typed it. Click again to undo. Clicking the
  window doesn't take focus away from the game's text box, so you can keep typing.
- Drag the title to move the window. **Right-click** it for the menu: get answers, pick area, clear list, open
  settings, exit.
- The window is left out of screen sharing (Discord, OBS, Teams), recordings and screenshots, so friends watching
  your stream won't see it. It's only on your physical screen.

## Settings

Stored in `%AppData%\PufferHelper\settings.json`. Right-click the window and choose **Open settings file**,
then restart the helper after editing it.

| Setting | Default | Notes |
|---|---|---|
| `Model` | `claude-opus-5-5` | Use `claude-haiku-5-5` for faster, much cheaper answers. It's a bit weaker on obscure categories. |
| `Effort` | `low` | `low`, `medium`, `high` or `max`. Higher means slower and more expensive. Low is plenty for this. |
| `AnswerCount` | `25` | Answers per request (5–60). |
| `HideFromScreenCapture` | `true` | Set `false` if you want the window to show up in your own screenshots. |
| `HotkeyAsk`, `HotkeyRefresh`, `HotkeyPickRegion`, `HotkeyToggle` | `Ctrl+Alt+Q/W/R/H` | Written like `Ctrl+Shift+F9`. Each needs at least one of Ctrl, Alt, Shift or Win. |
| `Region` | `null` | Saved by Ctrl+Alt+R. `null` means the whole screen. |

## Cost

Each **Ctrl+Alt+Q** is one API request with one screenshot.

- **Claude Opus 5.5** at low effort: roughly 1–2 US cents, so an evening of 100 categories is about $1–2.
- **Claude Haiku 5.5**: well under a tenth of a cent.

Ctrl+Alt+W never calls the API. You can see your actual spending in the Anthropic Console under Usage.

If Claude declines a category (a rare false alarm), the request is set up to let the API retry it automatically
on another model, so you still get a list. This applies to Opus, not Haiku.

## Troubleshooting

| What you see | What to do |
|---|---|
| "ANTHROPIC_API_KEY isn't set" | Do setup step 2, then start the helper from a **new** terminal (or sign out and back in). |
| "The API key was rejected" | Check for a typo in the key, or create a new key. |
| "No category found in the captured area" | Press Ctrl+Alt+R and draw the box around the category text. |
| "Ctrl+Alt+Q is taken by another app" | Another program uses that hotkey. Pick a different one in settings.json. |
| Ctrl+Alt+W never crosses anything off | The game might not show played answers on screen, or your box doesn't include them. Clicking answers works either way. |
| "Windows text recognition isn't installed" | Settings > Time & language > Language: add English (with its basic typing pack). |
| "Puffer Helper is already running" | It's probably hidden. Press Ctrl+Alt+H. |

## Files

| File | What's in it |
|---|---|
| `OverlayForm.cs` | The window, hotkeys and what each action does |
| `AnswerService.cs` | The Claude request: prompt, screenshot, JSON answer format |
| `AnswerListControl.cs` | The two-column clickable answer list |
| `RegionPickerForm.cs` | The drag-a-box screen picker |
| `OcrReader.cs` | Windows text recognition, used by Ctrl+Alt+W |
| `AnswerMatcher.cs` | Loose matching of answers (case, accents, plurals) |
| `ScreenGrabber.cs` | Screenshots and resizing |
| `AppSettings.cs`, `HotkeyBinding.cs`, `NativeMethods.cs` | Settings file, hotkey parsing, Windows API calls |
