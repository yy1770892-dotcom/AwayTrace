<p align="center">
  <img src="src/AwayTrace.App/Assets/AwayTrace.png" width="128" alt="AwayTrace logo" />
</p>

<h1 align="center">AwayTrace</h1>

<h2 align="center">Protect your PC before you step away.</h2>

<p align="center">
Lock sensitive work folders so they cannot be opened,<br/>
keep messenger windows out of sight,<br/>
and return to a timeline of file changes and PC activity.
</p>

<p align="center"><b>All records stay on your computer. Nothing is sent to an external server or cloud.</b></p>

<p align="center">
  <a href="../../releases/latest">Download AwayTrace</a> ·
  <a href="README.ko.md">한국어 README</a> ·
  <a href="PRIVACY.md">Privacy Principles</a>
</p>

---

Meetings, lunch, or even a quick trip away from your desk can mean leaving a PC behind.

AwayTrace is a personal privacy app for Windows 10/11. It records whether files changed while you were away and protects selected folders and messenger windows.

It does not film anyone or track their behavior. It does not record file contents, keystrokes, your screen, or messenger conversations. On a PC that you own, AwayTrace helps you review **file-change and PC-activity context** from protection sessions that you start yourself.

## How AwayTrace Works

### 1. Choose What to Protect

Folders can be added for different purposes.

- **Record folder:** Records file creation, modification, deletion, and rename events.
- **Locked folder:** Records changes and uses Windows permissions to block access.
- **Messenger protection:** Hides the windows of running messenger or work apps, or terminates the apps.

Use only the features you need. You can record changes without locking a folder.

### 2. Start Protection Before You Step Away

Starting protection begins file-change recording for your selected folders and applies the protection mode chosen for locked folders and registered apps.

AwayTrace can also lock the Windows session when protection starts. Even when its window is hidden, AwayTrace keeps working in the background.

### 3. Review the Session When You Return

Enter your PIN to stop protection and open the Away Report for that session.

The report shows what changed and when. If no file changes were observed, it says so clearly.

## See the Session at a Glance

<p align="center">
  <img src="docs/screenshot-report.png" width="720" alt="AwayTrace Away Report" />
</p>

The Away Report presents the events observed between the start and end of a protection session as one timeline.

- File creation, modification, deletion, and renames
- Protection start and stop
- Windows session lock and unlock
- Protection actions for registered messenger and work apps
- Monitoring errors and possible recording gaps

Filter the timeline by event type or export it as a CSV or JSON file.

If AwayTrace exits unexpectedly or a recording session is interrupted, the report does not present the session as complete. It is marked **"Low confidence"** to show that the timeline may contain gaps.

The report is a reference record of observed file-change context. It does not prove who changed a file or whether someone opened, read, or copied it.

## Manage Important Folders in Two Ways

<p align="center">
  <img src="docs/screenshot-main.png" width="720" alt="AwayTrace main window" />
</p>

A **record folder** remains accessible and works as usual. AwayTrace only records file changes observed during protection.

A **locked folder** uses Windows NTFS permissions to block access for the current Windows account. When protection ends, AwayTrace removes that access restriction.

The folder lock remains in place across a reboot. With automatic protection recovery enabled, AwayTrace can resume the previous protection session after the next Windows sign-in.

## Protect Messenger Windows Too

Register KakaoTalk, a work messenger, or another app whose on-screen content you want to keep out of sight.

- **Hide windows:** Keeps the app running but hides its visible windows.
- **Terminate app:** Terminates the registered app when it runs.
- **Leave open:** Leaves registered apps alone while other AwayTrace features remain active.

Windows hidden by AwayTrace are shown again when protection ends. Apps terminated by AwayTrace are not relaunched automatically.

AwayTrace does not use an official messenger integration. It works with running Windows app windows and processes, and it does not inspect conversations or read status.

## Review PC Activity Context

AwayTrace uses Windows system records to show estimated PC startup and shutdown times, along with unexpected shutdown records.

Set your usual working hours to distinguish records found outside that schedule. AwayTrace launch and normal-exit records are shown as well, helping you review unexplained gaps.

The PC activity view is reference information assembled from the Windows event log. It is not a complete record of every action, and it may not include all sleep and resume activity.

## Control Protection With a PIN

The AwayTrace PIN is separate from your Windows sign-in password.

The PIN is never stored as plain text. It is stored locally as a PBKDF2-HMAC-SHA256 hash. After five consecutive failed attempts, another attempt is blocked for 30 seconds.

A global hotkey can hide the AwayTrace window, taskbar presence, and tray icon, then restore them with the same key. This only hides the interface. The AwayTrace process remains visible in Task Manager.

## Records Stay on Your Computer

AwayTrace has no account registration or sign-in process.

It does not use a server or cloud service, and AwayTrace does not send its records elsewhere. Settings and records are stored in a local SQLite database at:

```text
%LocalAppData%\AwayTrace\awaytrace.db
```

Stored data includes protection sessions, registered folders, observed file events, protected-app settings, and PC activity context. File contents are not stored.

The complete source code is public. You can inspect this repository to see what AwayTrace records and how the data is stored.

## Information AwayTrace Does Not Record

AwayTrace does not collect or record:

- File contents or file hashes
- Keystrokes or clipboard contents
- Screens or screenshots
- Webcam or microphone input
- Messenger conversations or read status
- Records of files being opened or read
- Records proving that a file was copied
- The identity of a user or actor

AwayTrace is not designed for employee monitoring, identifying intruders, forensic investigation, or collecting legal evidence.

## Things to Know

AwayTrace records **observed file-change context**. It can record when a file is created, modified, deleted, or renamed, but it cannot tell when a file is merely opened, read, or copied.

Windows blocks access to locked folders. However, this version does not record that someone clicked a locked folder or attempted to access it.

Messenger window protection checks registered apps at a selected interval. Depending on the app and PC state, a window may appear briefly before it is hidden. This is a supporting privacy feature, not the same as a dedicated security product.

AwayTrace cannot record file changes while the PC is off or the app is not running. A user with administrator access to the PC can also change folder permissions or terminate or remove the app.

For these reasons, AwayTrace is a personal tool for reviewing changes while you were away and reducing the exposure of sensitive information. It is not a tool for proving file access or intrusion.

## Install

1. Download `AwayTrace.exe` from the [latest release](../../releases/latest).
2. Run the downloaded file.
3. Set a separate AwayTrace PIN on first launch.

No installer or separate .NET runtime is required.

AwayTrace is currently an unsigned personal open-source app. Windows SmartScreen may show an "Unknown publisher" warning on first launch. Check the release source and file before deciding whether to select `More info` and run it.

## Before You Use AwayTrace

AwayTrace is a Windows 10/11 desktop application.

Use it for personal privacy protection only on a PC that you own or are authorized to manage. Installing it on another person's PC without consent or using it for surveillance goes against the purpose of AwayTrace.

## For Developers and Reviewers

Requirements: Windows 10/11, .NET 8 SDK

```powershell
dotnet build
dotnet test
dotnet run --project src\AwayTrace.App\AwayTrace.App.csproj
```

Publish a single self-contained executable:

```powershell
cd src\AwayTrace.App
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
```

Main local data tables:

```text
settings
monitored_folders
sessions
file_events
protected_apps
pc_usage_events
```

`file_events` stores the timestamp, event type, path, previous path when applicable, and session ID. It does not store file contents.

## License

AwayTrace is released under the [MIT License](LICENSE).
