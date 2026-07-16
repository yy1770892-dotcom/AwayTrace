<p align="center">
  <img src="src/AwayTrace.App/Assets/AwayTrace.png" width="128" alt="AwayTrace logo" />
</p>

<h1 align="center">AwayTrace</h1>

<p align="center"><b>Know what happened on your PC while you were away.<br/>Nothing leaves your computer.</b></p>

<p align="center">
  <a href="README.ko.md">한국어 README</a> ·
  <a href="PRIVACY.md">Privacy Principles</a> ·
  <a href="../../releases/latest">Download</a>
</p>

---

AwayTrace is a free, open-source Windows 10/11 desktop app for one situation: you step away from your own PC and want to review file-change context and PC activity without installing anything that spies on anyone.

It is not spyware, employee monitoring software, or a forensic tool. It records **observed file-change context**, not proof that a file was opened or read.

## Screenshots

<p align="center">
  <img src="docs/screenshot-main.png" width="720" alt="AwayTrace main screen" />
</p>

<p align="center">
  <img src="docs/screenshot-report.png" width="720" alt="Away Report timeline" />
</p>

<details>
<summary>More screenshots (Messenger guard · Options · PC usage)</summary>
<p align="center"><img src="docs/screenshot-messenger.png" width="720" alt="Messenger guard" /></p>
<p align="center"><img src="docs/screenshot-options.png" width="720" alt="Options" /></p>
<p align="center"><img src="docs/screenshot-usage.png" width="720" alt="PC usage timeline" /></p>
</details>

## What You Get

**1. Nothing leaves your PC.**
No cloud, no server, no telemetry. Everything is stored in a local SQLite file (`%LocalAppData%\AwayTrace\awaytrace.db`). AwayTrace does not initiate network connections; you can inspect the source yourself (point 7).

**2. Folder lock while you're away.**
Locked folders are blocked using Windows/NTFS permissions — enforcement is done by Windows itself, not by this app. The lock **survives reboots**: turning the PC off and on again does not unlock your folders.

**3. Away Report.**
Record folders get a timeline of file created / modified / deleted / renamed events, with filters and CSV export. You come back, you read what changed.

**4. PC usage timeline.**
See power-on/shutdown estimates from the Windows event log, activity outside your normal hours, and AwayTrace launch and normal-exit records that help reveal unexplained gaps.

**5. Messenger guard.**
Registered apps (e.g. KakaoTalk) can have their windows hidden or their processes terminated during protection. Hidden windows are restored afterward; terminated apps are not relaunched. AwayTrace never reads chat content — it only handles windows or processes.

**6. PIN lock + interface hide hotkey.**
Stopping protection normally through the app requires a PIN (stored as a PBKDF2-SHA256 hash, never plain text — 5 failed attempts trigger a lockout). A global hotkey hides the app's windows, taskbar presence, and tray icon, and brings them back. The process remains visible in Task Manager.

**7. Open source for inspection.**
Every claim above can be inspected in this repository. There is no keylogging, screen capture, or data-collection code in the AwayTrace source — you do not have to rely on marketing claims; you can read the code.

## What It Does Not Do

AwayTrace does not:

- send data to a server or cloud
- record keystrokes, screenshots, or clipboard contents
- use webcam or microphone input
- read or store file contents, or store file hashes
- inspect messenger messages or read status
- identify who performed an action
- prove that a file was opened, read, or copied
- claim to provide legal or forensic evidence

## Honest Limitations

`FileSystemWatcher` observes file creation, modification, deletion, and renames. It **cannot** detect that a file was merely opened, read, or copied out — Windows does not expose that to normal apps, and we don't pretend otherwise.

Locked folders are prevention, not detection: denied access attempts are blocked by Windows but **not recorded** in v0.1.

While the PC is off or AwayTrace is not running, file events are not recorded (the lock still holds). Sessions interrupted this way are marked as low confidence, and the PC usage timeline shows boot/shutdown context for the gap.

## Responsible Use

⚠️ AwayTrace is a Windows 10/11 desktop program.

Use it only on a PC that you own or are authorized to manage, and only for personal privacy protection. Installing it on someone else's PC without permission may create legal problems.

## Install

1. Download `AwayTrace.exe` from the [latest release](../../releases/latest)
2. Run it — no installer, no .NET runtime required (self-contained single file)

Because this is an unsigned personal open-source app, Windows SmartScreen may show an "Unknown publisher" warning. This does not mean the app is malware. If you trust the release source, choose `More info` → `Run anyway`.

## Build From Source

Requirements: Windows 10/11, .NET 8 SDK

```powershell
dotnet build
dotnet test
dotnet run --project src\AwayTrace.App\AwayTrace.App.csproj
```

Publish a single self-contained Windows x64 executable:

```powershell
cd src\AwayTrace.App
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
```

Output: `src\AwayTrace.App\bin\Release\net8.0-windows\win-x64\publish\AwayTrace.exe`

Only upload the single `AwayTrace.exe` file to Releases. Do not commit or upload local `publish/`, `bin/`, `obj/`, `.pdb`, or working-process files.

## Data Stored Locally

```text
%LocalAppData%\AwayTrace\awaytrace.db
```

Main tables: `settings`, `monitored_folders`, `sessions`, `file_events`, `protected_apps`, `pc_usage_events`.

`file_events` stores timestamp, event type, path, old path when applicable, and session ID. It does not store file contents.

## License

AwayTrace is released under the [MIT License](LICENSE).
