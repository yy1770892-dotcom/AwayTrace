# AwayTrace v0.1 MVP 구현 계획

## 목표

AwayTrace는 Windows 11에서 사용자가 직접 보호를 시작한 로컬 PC의 지정 업무 폴더에 대해 파일 변경 정황만 기록하고, 복귀 후 타임라인 리포트를 보여주는 개인용 로컬 프라이버시 앱이다.

이 앱은 감시, 스파이웨어, 포렌식, 법적 증거 수집, 침입자 식별 도구가 아니다. 파일 내용, 키 입력, 화면, 클립보드, 웹캠, 마이크, 네트워크 전송 기능은 만들지 않는다.

## 기술 선택

- 언어: C#
- 런타임: .NET 8
- UI: WPF, Windows 전용
- 구조: MVVM
- 저장소: SQLite
- 트레이: `System.Windows.Forms.NotifyIcon`
- 테스트: 외부 NuGet 없이 실행 가능한 최소 단위 테스트 프로젝트

SQLite 접근은 NuGet 패키지 의존을 피하기 위해 Windows 내장 `winsqlite3.dll`을 얇은 P/Invoke 래퍼로 사용한다. 데이터베이스 파일은 `%LocalAppData%\AwayTrace\awaytrace.db`에 저장한다.

## 폴더 구조

```text
AwayTrace.sln
PLAN.md
README.md
src/
  AwayTrace.Core/
    AwayTrace.Core.csproj
    Models/
    Services/
    Storage/
  AwayTrace.App/
    AwayTrace.App.csproj
    App.xaml
    App.xaml.cs
    ViewModels/
    Views/
    Services/
tests/
  AwayTrace.Tests/
    AwayTrace.Tests.csproj
    Program.cs
```

## 핵심 데이터 모델

- `settings`
  - PIN salt, hash, iteration count, 실패 횟수, 잠금 해제 시각
- `monitored_folders`
  - 감시 폴더 경로
- `sessions`
  - 세션 ID, 시작 시각, 종료 시각, 상태, 신뢰도 낮음 여부, 감시 폴더 스냅샷
- `file_events`
  - timestamp, event_type, path, old_path, session_id

이벤트에는 파일 내용, 파일 해시, 사용자 식별 정보, 프로세스 정보, 화면 정보, 키 입력 내용을 저장하지 않는다.

## 주요 서비스

- `PinService`
  - 최초 PIN 설정
  - PBKDF2-HMAC-SHA256 + 랜덤 salt 해시
  - 최소 6자리 검증
  - 5회 실패 시 30초 대기
- `AwayTraceDatabase`
  - 앱 데이터 폴더 생성
  - SQLite 스키마 초기화
  - 폴더, 세션, 이벤트, 설정 저장
- `FileChangeDebouncer`
  - 2초 이내 중복 FileSystemWatcher 이벤트 억제
- `FileMonitorService`
  - `Created`, `Changed`, `Deleted`, `Renamed`, watcher error 기록
  - 파일 내용은 읽지 않음
- `SessionRecoveryService`
  - 이전 실행에서 종료되지 않은 활성 세션을 비정상 종료 및 기록 신뢰도 낮음으로 표시
- `ReportExportService`
  - 리포트 JSON, CSV 내보내기
- `ProtectionCoordinator`
  - 보호 시작/종료 흐름 조율
  - 보호 시작 성공 시 `LockWorkStation` 호출
  - 잠금 실패 시 보호 시작 취소
- `TrayIconService`
  - 보호 중 트레이 아이콘과 메뉴 제공

## UI 화면

### 최초 PIN 설정

- 한국어 UI
- 앱 종료용 PIN 최초 1회 설정
- Windows 로그인 비밀번호와 연동하지 않는 별도 PIN임을 표시
- 최소 6자리

### 메인 화면

- 상태: 보호 해제 / 보호 중
- 감시 폴더 목록
- 폴더 추가 / 삭제
- 보호 시작
- 최근 리포트 보기
- 설명 문구:
  - "AwayTrace는 지정 폴더의 파일 변경 정황을 로컬에 기록합니다. 파일 내용, 키 입력, 화면은 기록하지 않습니다."

### 보호 중

- 메인 창은 숨길 수 있고 앱은 트레이에 남음
- 트레이 tooltip: "AwayTrace - 보호 중"
- Windows session lock / unlock 이벤트를 시스템 이벤트로 기록
- 정상 종료 시 PIN 인증 요구

### 리포트

- 제목: "자리비움 리포트"
- 기간
- 상태:
  - 파일 변경 없음
  - 파일 변경 감지
  - 기록 신뢰도 낮음
- 타임라인
- 이벤트 필터:
  - 전체 / 생성 / 수정 / 삭제 / 이름 변경 / 시스템
- JSON / CSV 내보내기
- 안내 문구:
  - "이 리포트는 파일 변경 정황을 보여주며, 행위자 식별이나 파일 열람 여부를 증명하지 않습니다."

## v0.1 제외 범위

- USB 감지
- 원격접속 감지
- 회사 보안 에이전트 점검
- AI 요약
- 스크린샷
- 키 입력 기록
- 클립보드 기록
- 웹캠 / 마이크
- 네트워크 전송 / 클라우드 업로드
- 행위자 식별
- 파일 열람 여부 증명

## 테스트 계획

- `FileChangeDebouncer`
  - 2초 이내 동일 이벤트 억제
  - 2초 이후 동일 이벤트 허용
  - 이름 변경 이벤트의 old/new path 기준 중복 처리
- `SessionRecoveryService`
  - 종료되지 않은 활성 세션을 비정상 종료 및 기록 신뢰도 낮음으로 표시
  - 활성 세션이 없으면 변경하지 않음

## 검증 순서

1. `dotnet build`
2. `dotnet test`
3. 수동 실행 후 최초 PIN 설정, 폴더 추가, 보호 시작 잠금 흐름 확인
4. 복귀 후 트레이 메뉴에서 보호 종료, PIN 인증, 리포트 확인

