# ⚡ Fast Ethernet Transfer v2.0

**High-Speed PC-to-PC File Transfer & Game Updater for Windows** — Transfer files, folders, and differential game updates between two PCs over Ethernet at maximum throughput, with zero configuration.

![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D6?logo=windows)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)
![License](https://img.shields.io/badge/License-MIT-green)

---

## ✨ Features

### 🚀 Core File Transfer Pipeline
- **Maximum Ethernet throughput** — Direct TCP streaming with large, configurable I/O buffers (512 KB – 8 MB).
- **Files & directory trees** — Send individual files or complete directory hierarchies preserving empty folders and subfolder structures.
- **Zero-config peer discovery** — Automatic LAN detection using UDP multicast (239.255.43.21:45778) and broadcast fallback. No IP addresses or manual pairing required.
- **Live transfer dashboard** — Real-time throughput (MB/s & Gbps), peak/average speeds, ETA, and byte-level progress.
- **Pause, Resume, and Cancel** — Graceful transfer pause and offset-based resume for interrupted transfers.
- **SHA-256 integrity verification** — End-to-end cryptographic hash verification for every transferred file.
- **Atomic part files** — Files are written to `.fetpart` temporary files and moved only after successful verification.

### 🎮 Game Updater
- **Differential game updating** — Updates game installations on one laptop using another laptop over Ethernet, transferring only new and changed files.
- **Staged comparison engine** — Compares relative paths, sizes, and SHA-256 hashes (never relies solely on timestamps).
- **Preview before update** — Summary metrics (New, Changed, Identical, Destination-Only, Errors) and filterable diff list.
- **Safe by default** — Destination-only files (saves, mods, configs) are preserved. Optional deletion requires explicit confirmation and enforces strict path-traversal protection.
- **Remote folder approval** — The source PC user approves and browses the game directory locally via the app UI.

### 🎨 Modern Windows 11 UI & Themes
- **Compact Sidebar Navigation** — Streamlined sidebar with direct access to Transfer, Game Updater, Activity, and Settings.
- **Dark & Light Theme Switching** — Live theme switching with persistent user selection saved in settings.
- **Drag-and-Drop File Transfers** — Drag and drop individual files or complete directory hierarchies directly from File Explorer into the transfer queue.
- **Queue Management** — File/folder badges, formatted sizes, and individual remove (`✕`) controls.
- **Activity & Transfer History** — Full transfer audit log with direction, peer, files, byte count, transfer speeds, and timestamps.
- **New Multi-Resolution Icon (`newappicon.ico`)** — Fully embedded 9-resolution icon (16×16 to 256×256) for the EXE, window titles, taskbar, and shortcuts.
- **Readable Dropdown Styling** — Dark theme ComboBox styling in `Theme.xaml` ensuring clear text contrast across all states.

---

## 📦 Releases & Distribution

Fast Ethernet Transfer packages are generated into the `dist\` folder:

### Distribution A: Portable ZIP (`dist\FastEthernetTransfer-Portable-win-x64.zip`)
Ideal for copying to a USB thumb drive or directly to another PC without running an installer.

- **Location**: `dist\FastEthernetTransfer-Portable-win-x64.zip` (55 MB)
- **Contents**:
  - `FastEthernetTransfer.exe` (Self-contained single-file executable, 130 MB)
  - `newappicon.ico` (Application icon asset)
  - `README.txt` (Portable usage instructions)
  - `Configure-Firewall.cmd` (Optional one-click firewall rule helper)
- **Zero Dependencies**: Includes the complete Windows x64 .NET 9 runtime. Target PC does **not** need the .NET SDK or .NET Runtime installed.
- **How to Use**:
  1. Extract `FastEthernetTransfer-Portable-win-x64.zip` to any folder (e.g. `Desktop`, `D:\Tools`, or USB drive).
  2. Double-click `FastEthernetTransfer.exe` to launch.
  3. Allow network access when prompted by Windows Defender Firewall on private networks.

### Distribution B: Setup Installer (`dist\FastEthernetTransfer-Setup-win-x64.exe`)
Ideal for permanent installations on your primary laptops.

- **Location**: `dist\FastEthernetTransfer-Setup-win-x64.exe` (41 MB)
- Compiled via Inno Setup 6.
- Configured with `PrivilegesRequired=lowest` and `PrivilegesRequiredOverridesAllowed=dialog` for both per-user and per-machine installations.
- Automatically creates Start Menu and Desktop shortcuts with embedded icons and adds Windows Firewall exceptions.

- **Option 1: One-Click PowerShell Installer (Recommended, No Admin Required)**
  ```powershell
  # Install for current user:
  powershell -ExecutionPolicy Bypass -File .\scripts\Install-FastEthernetTransfer.ps1

  # Uninstall cleanly:
  powershell -ExecutionPolicy Bypass -File .\scripts\Install-FastEthernetTransfer.ps1 -Uninstall
  ```
  - Installs to `%LOCALAPPDATA%\Programs\FastEthernetTransfer`.
  - Creates Start Menu and Desktop shortcuts with the multi-resolution application icon.
  - Automatically configures the Windows Firewall exception.
  - Does **not** require administrator privileges.

- **Option 2: Inno Setup Windows Installer (`FastEthernetTransferSetup.exe`)**
  - Compiled using `installer\setup.iss`.
  - Configured with `PrivilegesRequired=lowest` and `PrivilegesRequiredOverridesAllowed=dialog` for both per-user and per-machine installations.

### Portable vs. Installed Comparison

| Feature | Portable ZIP | Installed Version |
|---|---|---|
| **Installation Required** | No (Extract & Run) | Yes (1-click script / setup) |
| **.NET Runtime Required** | No (Self-contained) | No (Self-contained) |
| **Start Menu / Desktop Shortcuts** | No | Yes (with application icon) |
| **Automatic Firewall Setup** | Via optional `.cmd` script | Yes |
| **Settings & Logs Location** | `%LOCALAPPDATA%\FastEthernetTransfer` | `%LOCALAPPDATA%\FastEthernetTransfer` |
| **Uninstallation** | Delete extracted folder | Automated uninstall script |

---

## 🛠️ Storage & Diagnostics

### Settings & Logs Locations
To ensure portable and installed versions can run from read-only directories without permission issues, all mutable user data is stored in standard Windows local app data:

- **Configuration Settings**:
  `%LOCALAPPDATA%\FastEthernetTransfer\settings.json`
- **Application Logs**:
  `%LOCALAPPDATA%\FastEthernetTransfer\Logs\transfer_YYYYMMDD.log`

### Diagnosing Startup Failures
The application includes global unhandled exception handling and startup logging in `App.xaml.cs`:
1. If the application fails to start or encounters an unhandled exception, it logs the complete stack trace to `%LOCALAPPDATA%\FastEthernetTransfer\Logs\`.
2. A diagnostic message box is displayed with the exact error details.
3. You can inspect Windows Event Viewer under `Windows Logs -> Application` (look for source `.NET Runtime` or `Application Error` with `FastEthernetTransfer.exe`).

---

## 🏗️ Build & Publishing Commands

### 1. Build and Run from Source (Development)
```powershell
# Restore and build Debug version
dotnet build

# Run unit tests
dotnet test

# Launch WPF application
dotnet run --project FastEthernetTransfer.Wpf
```

### 2. Publish Self-Contained Win-x64 Executable
```powershell
# Automated publish script:
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1

# Or via dotnet CLI directly:
dotnet publish .\FastEthernetTransfer.Wpf\FastEthernetTransfer.Wpf.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64
```
**Output**: `publish\win-x64\FastEthernetTransfer.exe`

### 3. Package Portable ZIP Release
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-portable.ps1
```
**Output**: `publish\FastEthernetTransfer-Portable-win-x64.zip`

### 4. Build Windows Installer
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```
**Output**: `publish\installer\FastEthernetTransferSetup.exe` (requires Inno Setup 6 compiler).

### 5. Master Release Build (Runs Tests, Publishes, and Packages)
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

---

## 🎮 Game Updater Workflow

1. **Open** Fast Ethernet Transfer on both laptops connected via Ethernet.
2. On the **Receiving Laptop** (destination):
   - Click **"🎮 Game Updater"** in the top header.
   - Select the **Source PC** from the dropdown.
   - Browse and select your **local game directory** that needs updating.
   - Click **"🔍 Connect & Request Remote Game Folder"**.
3. On the **Sending Laptop** (source):
   - A dialog appears requesting game folder access.
   - Click **Yes** and select the updated game installation folder.
4. On the **Receiving Laptop**:
   - The app scans both installations and computes SHA-256 hashes for same-sized files.
   - Review changes in the **Review Changes** screen (New, Changed, Identical, Destination-Only).
   - Click **"🚀 START GAME UPDATE"**.
   - Watch differential file streaming and verification.

---

## 🔄 Version History & Changelog

See [CHANGELOG.md](CHANGELOG.md) for detailed release notes.
