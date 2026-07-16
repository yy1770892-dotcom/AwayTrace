# AwayTrace Privacy Principles

AwayTrace is a local-first personal privacy protection app for Windows. It is designed to help a user protect their own PC while away, not to monitor or identify other people.

## Responsible Use

AwayTrace is a Windows 10/11 desktop program. Use it only on a PC that you own or are authorized to manage, and only for personal privacy protection. Installing it on someone else's PC without permission may create legal problems.

## Core Promise

AwayTrace records local protection events and file-change context only. It does not claim to prove who performed an action, whether a file was read, or whether a specific person accessed the PC.

## What AwayTrace Stores

- Protection session start and end time
- Watched folder paths selected by the user
- File change events:
  - created
  - changed
  - deleted
  - renamed
- System events:
  - protection started
  - protection stopped
  - Windows lock/unlock
  - protected app window hide / close events
  - locked folder lock/unlock status
- PC usage context events (reference only):
  - AwayTrace start/exit time
  - Windows lock/unlock time
  - power-on/shutdown/unexpected-shutdown estimates based on Windows event log IDs 6005/6006/6008 (sleep/modern-standby resume may not be captured)

Data is stored locally in:

```text
%LocalAppData%\AwayTrace\awaytrace.db
```

## What AwayTrace Does Not Store

- File contents
- Keystrokes
- Clipboard contents
- Screenshots
- Webcam or microphone data
- Messenger messages
- Messenger read status
- Chat room names or participants
- Cloud uploads
- Server-side logs
- User identity of a possible actor

## Messenger Protection

While protection is active, AwayTrace can hide the windows of, or close, user-registered apps such as KakaoTalk or NateOn. This is a best-effort assist based on process names, not an official integration, and it does not guarantee that an app cannot be launched or used. AwayTrace does not inspect message contents, chat rooms, read status, contacts, or screen contents.

## File Reading and Copying

File change monitoring cannot reliably prove that a file was read or copied. AwayTrace does not label file reading as confirmed evidence.

When a folder is registered as a locked folder, AwayTrace attempts to block reading/copying by applying Windows file permissions while protection is active. This is a prevention feature, not a proof-of-access feature. The lock targets the current Windows user account and does not guarantee protection against other administrator accounts.

## PIN and Recovery

The PIN is independent from the Windows login password. AwayTrace stores only a PBKDF2-HMAC-SHA256 hash with a random salt.

AwayTrace does not provide email recovery or server recovery. If the PIN is forgotten, the app data must be deleted and configured again.

## Non-Goals

AwayTrace is not:

- spyware
- employee monitoring software
- a forensic evidence tool
- an intruder identification tool
- a messenger surveillance tool

## Public Communication Rule

When describing AwayTrace publicly, use phrases such as:

- "local PC protection"
- "file change context"
- "protected app window hide/close assist"
- "locked folder access prevention"
- "PC usage context (reference only)"

Avoid phrases such as:

- "detect who opened a file"
- "prove file reading"
- "catch an intruder"
- "block messenger apps completely"
- "monitor messenger messages"
- "forensic PC usage evidence"

