# EndTaskTray

A lightweight Windows system-tray utility that lets you instantly end any running application straight from the taskbar. Just **middle-click** an app's icon on the taskbar, and its process is terminated on the spot. No Task Manager, no right-click menus, no waiting on an unresponsive "End task" dialog.

> Task Manager? Ain't nobody got time for that.

---

## Features

- **One-click kill from the taskbar** — middle-click any running app icon to end it immediately.
- **Whole process tree termination** — ends the target process and all of its child processes, so nothing lingers in the background.
- **Runs silently in the tray** — no main window, just a small shield icon in the notification area.
- **Smart taskbar detection** — only reacts to middle-clicks over the running-apps region of the taskbar, including secondary taskbars on multi-monitor setups.
- **UI Automation lookup** — resolves the clicked icon to its real window and owning process reliably.
- **Clear feedback** — balloon notifications confirm what was ended, or warn you when a window can't be identified.
- **Minimal footprint** — a single self-contained tray application with no background services.

---

## How It Works

EndTaskTray installs a global low-level mouse hook to listen for middle-button clicks. When a click lands over the taskbar's running-apps area (`MSTaskListWClass`), the app:

1. Uses **UI Automation** (`AutomationElement.FromPoint`) to find the taskbar button under the cursor and read its title.
2. Matches that title to a top-level window via `EnumWindows`.
3. Resolves the window's owning process ID with `GetWindowThreadProcessId`.
4. Terminates the process and its entire child tree with `Process.Kill(entireProcessTree: true)`.

The middle-click is swallowed when it targets the taskbar, so it won't trigger any default behavior.

---

## Requirements

- **Windows 10 or Windows 11**
- **.NET 8.0 Desktop Runtime** (Windows Desktop)
- **Administrator privileges** — required to terminate processes that run at higher integrity levels. The app manifest requests elevation automatically (`requireAdministrator`).

---

## Build

The project targets `net8.0-windows` and uses both Windows Forms and WPF (for UI Automation).

```bash
git clone https://github.com/<your-username>/EndTaskTray.git
cd EndTaskTray

dotnet build -c Release
```

To produce a single self-contained executable that runs without installing the .NET runtime:

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The resulting `EndTaskTray.exe` will be placed under the publish output folder.

---

## Usage

1. Launch `EndTaskTray.exe`. Approve the User Account Control (UAC) prompt when asked.
2. A shield icon appears in the system tray, along with a one-time notification confirming it's running.
3. **Middle-click** any running application's icon on the taskbar to end it.
4. A short notification confirms which app and process was terminated.

### Tray menu

Right-click the tray icon for:

- **About** — basic information about the tool.
- **Exit** — remove the mouse hook, hide the icon, and close the app cleanly.

---

## Notes and Limitations

- If a window can't be matched to a process, EndTaskTray shows a warning instead of ending anything.
- Some system or protected processes may refuse to terminate even with administrator rights; in that case an "Insufficient permissions" message is shown.
- Title matching is approximate (it compares the taskbar button name against visible window titles). Apps with identical or dynamic titles may occasionally resolve to an unexpected window, so use with care around sensitive applications.
- Because it ends the **entire process tree**, killing a parent app will also close any helper processes it spawned.

---

## Disclaimer

This tool forcibly terminates processes. Unsaved work in the target application will be lost. Use it the same way you'd use Task Manager's "End task" — deliberately. The authors are not responsible for any data loss.

---

## Tech Stack

- **Language:** C#
- **Framework:** .NET 8 (Windows Forms + WPF UI Automation)
- **Key Win32 APIs:** low-level mouse hook (`SetWindowsHookEx`), `EnumWindows`, `FindWindowEx`, `GetWindowRect`, `GetWindowThreadProcessId`
- **Automation:** `System.Windows.Automation`

---

## License

Released under the [MIT License](LICENSE). You're free to use, modify, and distribute it.
