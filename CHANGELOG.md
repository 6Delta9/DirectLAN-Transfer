# Changelog

All notable changes to Fast Ethernet Transfer are documented in this file.

## [2.0.0] - 2026-10-10

### 🎮 Direct In-Window Game Updater Integration
- **Direct Embedded Game Updater Interface**:
  - Refactored Game Updater from a standalone window prompt into an embedded `GameUpdaterView` `UserControl` hosted directly inside `MainWindow`.
  - Clicking **Game Updater** in the sidebar immediately switches the content view to the complete differential updater wizard without any intermediate landing card or pop-up window.
  - Persistent state preservation across sidebar navigation: peer selection, folder paths, scan progress, comparison metrics, and diff filters remain active and intact when navigating between Transfer, Game Updater, Activity, and Settings.
  - Retains full folder drag-and-drop capability, real-time SHA-256 comparison, differential review data grid, and live Ethernet transfer streaming.

### 🖥️ Modern Windows 11 UI Redesign & Drag-and-Drop
- **Modern Windows 11 Sidebar Navigation**:
  - Implemented 4-section sidebar: **Transfer**, **Game Updater**, **Activity**, and **Settings**.
  - Integrated local PC status pill displaying active physical adapter, link speed, and local IP.
  - Live runtime **Dark/Light theme switcher** with instant UI refresh and persistent preference saving.
- **Native Drag-and-Drop Transfer Pipeline**:
  - Direct drag-and-drop from Windows File Explorer onto the transfer area.
  - Supports multiple files, complete nested directories, and large files.
  - Thread-safe duplicate suppression, path normalization, and path traversal defense (`..` checks).
  - Selected items queue with distinct file/folder icons, individual item removal (`✕`), and clear-all actions.
  - Drag-and-drop support also added to Game Updater destination folder selection.
- **Activity & Transfer History Service**:
  - Automatic audit logging for all completed and cancelled transfers into `%LOCALAPPDATA%\FastEthernetTransfer\history.json`.
  - Activity viewer with transfer direction badges, peer names, item counts, speeds, durations, and timestamps.
- **Dual Release Distribution in `dist\`**:
  - Portable release: `dist\FastEthernetTransfer-Portable-win-x64.zip` (55 MB).
  - Windows installer: `dist\FastEthernetTransfer-Setup-win-x64.exe` (41 MB).
  - Standalone binary: `publish\win-x64\FastEthernetTransfer.exe` (130 MB).

### 🚀 Self-Contained Executable & Packaging Fixes

- **Fixed Root-Cause Startup Crash (0xe0434352 / APPCRASH)**:
  - **Issue**: The published self-contained single-file executable `publish\win-x64\FastEthernetTransfer.exe` exited immediately on launch. Windows Event Viewer diagnostics captured `System.Windows.Markup.XamlParseException: Cannot locate resource 'appicon.ico'`.
  - **Root Cause**: The `.csproj` project file declared `<ApplicationIcon>`, which sets the Win32 PE header icon on the output binary, but did not declare `<Resource Include="..." />`. When WPF attempted to resolve `Icon="pack://application:,,,/..."` during window initialization, it threw an unhandled `IOException`.
  - **Fix**: Added `<Resource Include="newappicon.ico" />` and `<Resource Include="appicon.ico" />` to `FastEthernetTransfer.Wpf.csproj`. Pack URIs now resolve properly from the compiled assembly in both Debug and Release single-file modes.

- **Startup Diagnostics & Exception Protection**:
  - Added global unhandled exception handling in `App.xaml.cs` (`DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`).
  - Added startup logging to `%LOCALAPPDATA%\FastEthernetTransfer\Logs\`.
  - Added user-facing diagnostic dialogs on fatal errors instead of silent crashes.

### 🖼️ New Application Icon Integration

- **Integrated `newappicon.ico`**:
  - Embedded high-resolution multi-size Windows icon containing 9 resolutions: 16×16, 24×24, 32×32, 48×48, 64×64, 72×72, 96×96, 128×128, and 256×256 (32 bpp).
  - Applied as Win32 application icon for `FastEthernetTransfer.exe`.
  - Set as window and taskbar icon across `MainWindow`, `GameUpdaterWindow`, `BenchmarkWindow`, and `SettingsWindow`.
  - Applied to Start Menu and Desktop shortcuts created by the installer and PowerShell script.
  - Configured as setup icon in `installer\setup.iss`.

### 📦 Distribution & Packaging

- **Distribution A: Portable ZIP Release**:
  - Created `publish\FastEthernetTransfer-Portable-win-x64.zip` (55 MB compressed, self-contained).
  - Contains standalone `FastEthernetTransfer.exe`, `newappicon.ico`, `README.txt`, and optional `Configure-Firewall.cmd`.
  - Requires no installation or .NET runtime on target machines.
- **Distribution B: Windows Installers**:
  - Updated `installer\setup.iss` with `newappicon.ico`, modern per-user and per-machine dialogs, and firewall configuration.
  - Updated `scripts\Install-FastEthernetTransfer.ps1` with shortcut icon bindings and safe uninstallation.
- **Reproducible Build Automation Scripts**:
  - Created `scripts\publish-win-x64.ps1`: Clean self-contained single-file publish with automatic startup verification.
  - Created `scripts\package-portable.ps1`: Staging, packaging, and compression of portable ZIP archive.
  - Created `scripts\build-installer.ps1`: Automated detection of Inno Setup compiler and compilation.
  - Updated `scripts\build.ps1`: Master release orchestration script running tests, publish, portable packaging, and installer steps.

---

### ✨ Features & Improvements (v2.0.0 Initial)

#### 🎮 Game Updater
- **Differential game folder updating**: Transfer only new and changed files between two PCs instead of the entire game installation.
- **Smart comparison engine**: Compares files using relative paths, sizes, and SHA-256 content hashes — never relies solely on timestamps.
- **Review changes before updating**: Summary metrics (New, Changed, Identical, Destination-Only, Total Transfer Size) and filterable diff list.
- **Remote folder approval**: The source PC user approves and browses the game directory locally via the app UI.
- **Safe destination-only file handling**: Files that exist only on destination (saves, mods, configs) are preserved by default.
- **Atomic file replacement**: Files written to `.fetpart` temporary files first and moved only after successful verification.

#### 🐛 Dropdown Menu Text Visibility
- Added global dark-themed `ComboBox` and `ComboBoxItem` styles in `Theme.xaml`.
- Fixed text contrast across closed, expanded, hover, selected, and disabled states.

---

## [1.0.0] - Initial Release

- High-speed direct PC-to-PC file transfer over Ethernet (TCP streaming).
- Zero-config peer discovery via UDP multicast and broadcast.
- Send files or complete folders preserving directory structure.
- Real-time transfer dashboard with speed, ETA, progress bar.
- Pause, resume, and cancel transfers with partial file resume.
- SHA-256 file integrity verification.
