# AwayTrace UI/UX 작업 인수인계

## 배경
WPF-UI(4.3.0) 라이브러리를 적용해 7개 창을 `ui:FluentWindow` 기반 다크 테마로 전환한 상태다.
빌드/테스트는 통과하지만, 실제 실행 화면에서 아래 UI 문제들이 남아 있다.
디자인(XAML/App.xaml/csproj)만 수정하고 **C# 로직·ViewModel·바인딩 이름·Command는 절대 변경하지 말 것.**
프라이버시 안내 문구의 의미도 바꾸지 말 것(배치/스타일만 조정).

## 지금까지 발견·처리한 이력
1. 탭([보호]/[메신저 보호]/[옵션])이 흰 바탕에 회색 텍스트라 다크 테마와 안 어울림
   → App.xaml에서 TabControl/TabItem 템플릿을 알약(pill) 형태 다크 스타일로 교체함(1차 처리).
   여전히 개선 여지 있음(아래 요청 참고).
2. FluentWindow의 Mica 반투명 배경 때문에 창을 숨겼다 다시 그릴 때 흰 화면이 잠깐 노출됨
   → App.xaml의 `ui:FluentWindow` 스타일에 `WindowBackdropType="None"` 추가함.
3. 단일 파일 publish 중 앱이 실행 중이면 네이티브 DLL 복사가 실패해 폴더가 반쪽 상태가 되는 문제
   → csproj에 `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` 추가함.
   publish 전에는 반드시 `Stop-Process -Name AwayTrace -Force` 로 앱을 종료할 것.

## 남은 UI/UX 요청
1. 탭 디자인을 더 가시성 있게: 선택된 탭과 비선택 탭의 대비를 명확히,
   호버/선택 상태가 한눈에 보이도록. Windows 11 설정 앱의 상단 탭 느낌 참고.
2. 전체적으로 다크 테마 일관성 점검:
   - 모든 창(MainWindow, ReportWindow, PcUsageLogWindow, PinSetupWindow,
     PinPromptWindow, PinChangeWindow, RunningAppPickerWindow)에서
     흰 배경/밝은 잔상이 남는 곳이 없는지 확인.
   - 텍스트 대비(회색 글씨가 어두운 배경에서 안 보이는 곳) 점검.
3. 여백/정렬/카드 스타일을 일관되게 정리해 "요즘 앱" 느낌으로.

## 반드시 지킬 것
- 수정 범위: `App.xaml`, `src/AwayTrace.App/Views/*.xaml`, `AwayTrace.App.csproj` 만.
- `.xaml.cs`는 base class가 이미 `Wpf.Ui.Controls.FluentWindow`로 정렬돼 있음. 더 건드리지 말 것.
- 폴더 목록 빈 상태 placeholder("아직 추가된 폴더가 없습니다") 유지.
- MainWindow의 AllowClose 속성·OnClosing 핸들러 관련 XAML(Closing 이벤트) 유지.
- 작업 후 검증:
  1. `dotnet build .\AwayTrace.sln` → 경고/오류 0 확인
  2. `dotnet test .\AwayTrace.sln` → 13개 테스트 통과 확인
  3. publish 전 `Stop-Process -Name AwayTrace -Force -ErrorAction SilentlyContinue` 실행

## 참고: 현재 색상 팔레트(App.xaml 정의)
- WindowBackground #0F141A / PanelBackground #151C24 / PanelBackgroundSoft #1B2430
- Accent #52C7B8 / TextPrimary #F3F7FA / TextSecondary #AEBBC7 / TextMuted #738392
