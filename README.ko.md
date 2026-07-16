<p align="center">
  <img src="src/AwayTrace.App/Assets/AwayTrace.png" width="128" alt="AwayTrace 로고" />
</p>

<h1 align="center">AwayTrace</h1>

<p align="center"><b>자리를 비운 사이, 내 PC에 무슨 일이 있었는지.<br/>내 컴퓨터 밖으로는 아무것도 나가지 않습니다.</b></p>

<p align="center">
  <a href="README.md">English README</a> ·
  <a href="PRIVACY.md">프라이버시 원칙</a> ·
  <a href="../../releases/latest">다운로드</a>
</p>

---

AwayTrace는 딱 한 가지 상황을 위한 무료 오픈소스 Windows 10/11 데스크톱 앱입니다. 내 PC에서 잠깐 자리를 비울 때 — 누군가를 감시하는 프로그램 없이, 지정 폴더의 변경 정황과 PC 사용 흔적을 확인하고 싶을 때.

이 앱은 감시 도구, 직원 모니터링 소프트웨어, 포렌식 도구가 아닙니다. 기록하는 것은 **관찰된 파일 변경 정황**이며, 파일이 열람됐다는 증명이 아닙니다.

## 스크린샷

<p align="center">
  <img src="docs/screenshot-main.png" width="720" alt="AwayTrace 메인 화면" />
</p>

<p align="center">
  <img src="docs/screenshot-report.png" width="720" alt="자리비움 리포트" />
</p>

<details>
<summary>스크린샷 더 보기 (메신저 보호 · 옵션 · PC 사용 기록)</summary>
<p align="center"><img src="docs/screenshot-messenger.png" width="720" alt="메신저 보호" /></p>
<p align="center"><img src="docs/screenshot-options.png" width="720" alt="옵션" /></p>
<p align="center"><img src="docs/screenshot-usage.png" width="720" alt="PC 사용 기록" /></p>
</details>

## 핵심 기능

**1. 내 컴퓨터 밖으로 아무것도 나가지 않습니다.**
클라우드 없음, 서버 없음, 전송 없음. 모든 기록은 로컬 SQLite 파일(`%LocalAppData%\AwayTrace\awaytrace.db`)에만 저장됩니다. AwayTrace는 네트워크 연결을 시작하지 않으며, 소스에서 직접 확인할 수 있습니다(7번 참조).

**2. 자리 비운 사이 폴더 잠금.**
잠금 폴더는 Windows/NTFS 권한으로 접근이 차단됩니다 — 차단은 이 앱이 아니라 Windows 자체가 수행합니다. 잠금은 **재부팅해도 유지됩니다.** PC를 껐다 켜도 폴더는 풀리지 않습니다.

**3. 자리비움 리포트.**
기록 폴더의 파일 생성 · 수정 · 삭제 · 이름변경 정황을 타임라인으로 보여줍니다. 유형별 필터와 엑셀용 CSV 내보내기를 지원합니다. 돌아와서, 무엇이 바뀌었는지 읽으면 됩니다.

**4. PC 사용 기록.**
Windows 이벤트 로그 기반으로 PC 켜짐/꺼짐 추정 시각과 표준 사용 시간 외 활동을 보여줍니다. AwayTrace의 실행·정상 종료 기록도 함께 표시해 설명되지 않는 기록 공백을 확인하는 데 도움을 줍니다.

**5. 메신저 보호.**
등록한 앱(예: 카카오톡)은 보호 중 창을 숨기거나 프로세스를 종료할 수 있습니다. 숨긴 창은 보호 종료 후 복원되지만 종료한 앱은 다시 실행하지 않습니다. 대화 내용은 읽지 않으며 창이나 프로세스만 처리합니다.

**6. PIN 잠금 + 앱 UI 숨김 단축키.**
앱에서 정상적으로 보호를 종료하려면 PIN 인증이 필요합니다(PIN은 평문이 아닌 PBKDF2-SHA256 해시로 저장되며, 5회 실패 시 잠금). 전역 단축키로 앱 창, 작업표시줄 표시, 트레이 아이콘을 숨기고 다시 표시할 수 있습니다. 작업 관리자에는 프로세스가 표시됩니다.

**7. 오픈소스로 확인할 수 있습니다.**
위의 모든 주장은 이 저장소에서 확인할 수 있습니다. AwayTrace 소스에는 키로깅, 화면 캡처, 데이터 수집 코드가 없습니다 — 홍보 문구만 믿을 필요 없이 직접 코드를 읽어볼 수 있습니다.

## 이 앱이 하지 않는 것

AwayTrace는 다음을 하지 않습니다:

- 서버·클라우드로 데이터 전송
- 키 입력, 화면, 클립보드 기록
- 웹캠 · 마이크 사용
- 파일 내용 읽기/저장, 파일 해시 저장
- 메신저 대화 내용 · 읽음 여부 확인
- 행위자 식별
- 파일이 열람·복사됐다는 증명
- 법적 · 포렌식 증거 제공 주장

## 솔직한 한계

`FileSystemWatcher`는 파일의 생성 · 수정 · 삭제 · 이름변경을 관찰합니다. 파일이 단순히 **열리거나, 읽히거나, 복사된 것은 감지할 수 없습니다** — Windows가 일반 앱에 그 정보를 제공하지 않으며, 이 앱은 할 수 있는 척하지 않습니다.

잠금 폴더는 예방이지 탐지가 아닙니다. 거부된 접근 시도는 Windows가 차단하지만 v0.1에서는 **기록되지 않습니다.**

PC가 꺼져 있거나 AwayTrace가 실행 중이 아닌 동안은 파일 이벤트가 기록되지 않습니다(잠금은 유지됩니다). 이렇게 중단된 세션은 "기록 신뢰도 낮음"으로 표시되며, 그 공백의 부팅/종료 정황은 PC 사용 기록에서 확인할 수 있습니다.

## 책임 있는 사용

⚠️ AwayTrace는 Windows 10/11 전용 데스크톱 프로그램입니다.

사용자 본인이 소유하거나 관리 권한을 가진 PC에서 개인 프라이버시 보호 목적으로만 사용하세요. 타인의 PC에 무단 설치하는 것은 법적 문제를 일으킬 수 있습니다.

## 설치

1. [최신 릴리스](../../releases/latest)에서 `AwayTrace.exe` 다운로드
2. 실행하면 끝 — 설치 과정 없음, .NET 런타임 설치 불필요 (단일 파일)

서명 없는 개인 오픈소스 앱이라 처음 실행 시 Windows SmartScreen이 "알 수 없는 게시자" 경고를 표시할 수 있습니다. 이는 악성 프로그램이라는 뜻이 아닙니다. 배포 출처를 신뢰한다면 `추가 정보` → `실행`을 선택하세요.

## 소스에서 빌드하기

요구 사항: Windows 10/11, .NET 8 SDK

```powershell
dotnet build
dotnet test
dotnet run --project src\AwayTrace.App\AwayTrace.App.csproj
```

자체 포함 단일 exe 빌드:

```powershell
cd src\AwayTrace.App
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
```

결과물: `src\AwayTrace.App\bin\Release\net8.0-windows\win-x64\publish\AwayTrace.exe`

Releases에는 단일 `AwayTrace.exe` 파일만 업로드하세요. 로컬 `publish/`, `bin/`, `obj/`, `.pdb`, 작업 과정 파일은 커밋하거나 업로드하지 마세요.

## 로컬에 저장되는 데이터

```text
%LocalAppData%\AwayTrace\awaytrace.db
```

주요 테이블: `settings`, `monitored_folders`, `sessions`, `file_events`, `protected_apps`, `pc_usage_events`

`file_events`에는 시각, 이벤트 유형, 경로, (해당 시) 이전 경로, 세션 ID만 저장됩니다. 파일 내용은 저장하지 않습니다.

## 라이선스

AwayTrace는 [MIT 라이선스](LICENSE)로 배포됩니다.
