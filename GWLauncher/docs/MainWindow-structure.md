# MainWindow.xaml.cs 구조 정리

`BravoGameLauncherGui` 네임스페이스의 메인 윈도우 코드비하인드. WPF 창 UI 이벤트 처리 및 빌드/캐시/P4/엔진 관련 로직을 담당합니다.

---

## 1. 클래스 구성

| 클래스 | 설명 |
|--------|------|
| **MainWindow** | `Window`를 상속한 `partial` 클래스. 런처 GUI 메인 창이며, 빌드 목록·캐시·P4·엔진·GWEditor 탭을 처리합니다. |
| **ServerBuildItem** | MainWindow 내부에 정의된 **중첩 클래스**. 서버에서 받은 빌드 한 건을 표현하는 데이터 모델. (BuildNo, FileName, Config, Version, CL, BuildDate, BuildTime, DS, SortKey) |
| **RunMode** | `enum`. 실행 모드: `GameOnly`, `DedicatedServerOnly`. |

---

## 2. MainWindow 메서드 목록 및 기능

### 2.1 생성자·초기화

| 메서드 | 기능 |
|--------|------|
| `MainWindow()` | 창 초기화: 설정/런처 로드, 엔진 버전·BasePath·P4 기본값 설정, 캐시 경로 표시, 빌드 타입 콤보 연동, 로드 시 서버에서 빌드 목록 자동 갱신. |

---

### 2.2 빌드 목록·UI

| 메서드 | 기능 |
|--------|------|
| `RefreshBuildListUI()` | `_allBuilds`를 선택된 빌드 타입(Development/Shipping 등)으로 필터링해 `LvBuilds`에 바인딩하고, 첫 항목 선택. |
| `GetSelectedBuildType()` | `CmbBuildType`에서 선택된 타입 문자열 반환. 없으면 `"Development"`. |

---

### 2.3 실행 버튼·런치

| 메서드 | 기능 |
|--------|------|
| `BtnRun_Click` | 클라이언트/DS 체크박스에 따라 **게임 실행**. `RunSelectedBuildAsync()` 호출. 둘 다 미선택 시 경고. |
| `RunSelectedBuildAsync()` | 리스트에서 선택된 `ServerBuildItem` 기준으로, `CbRunClient`/`CbRunDS`에 따라 클라이언트만 / DS만 / 둘 다 실행 (`RunLocalClientOnlyAsync` / `RunDedicatedServerAsync` / `RunLocalWithDedicatedServerAsync`). |

---

### 2.4 로그 출력

| 메서드 | 기능 |
|--------|------|
| `AppendToLog(TextBox, string)` | 지정 TextBox에 메시지 추가 후 맨 아래로 스크롤. (공통 헬퍼) |
| `AppendLog(string)` | 메인 로그(TxtLog)에 출력. |
| `AppendEngineLog(string)` | GWEditor 탭 통합 로그(TxtSharedLog)에 출력. (v26부터 섹션별 로그 없이 공용) |
| `AppendSetupP4Log(string)` | GWEditor 탭 통합 로그(TxtSharedLog)에 출력. (v26부터 섹션별 로그 없이 공용) |
| `AppendGWEditorLog(string)` | GWEditor 탭 통합 로그(TxtSharedLog)에 출력. (v26부터 섹션별 로그 없이 공용) |
| `AppendP4SyncLog(string)` | p4 sync 탭 로그(TxtP4SyncLog)에 출력. |

---

### 2.5 메뉴·캐시 (GameStarter)

| 메서드 | 기능 |
|--------|------|
| `MenuExit_Click` | 상단 메뉴 [파일] → [종료]: 창 닫기. |
| `MenuChangeCachePath_Click` | GameStarter 탭 [캐시 경로 변경] 버튼: 폴더 선택 다이얼로그로 캐시 경로 변경 후 설정 저장·런처 경로 갱신·UI 반영. |
| `MenuClearCache_Click` | GameStarter 탭 [캐시 삭제] 버튼: 확인 후 캐시 폴더 전체 삭제. |

---

### 2.6 서버 빌드 목록

| 메서드 | 기능 |
|--------|------|
| `BtnRefreshFromServer_Click` | 서버에서 빌드 목록 다시 가져오기. `RefreshFromServerAsync()` 호출. |
| `RefreshFromServerAsync()` | `BuildListService.FetchBuildListAsync()`로 WIN/DS 빌드 조회 후, WIN 기준으로 `_allBuilds` 구성(DS 존재 여부 포함), `RefreshBuildListUI()` 호출. |

---

### 2.7 빌드 파일명 파싱 (유틸)

| 메서드 | 기능 |
|--------|------|
| `ParseBuildInfoFromFileName(string fileName)` | 파일명에서 버전·CL·타임스탬프(yyyyMMddHHmmss) 추출. 정규식 사용. |
| `GetBuildConfigFromFileName(string fileName)` | 파일명에 `_Shipping_` / `_Development_` 포함 여부로 설정 문자열 반환. |

---

### 2.8 프로세스 실행

| 메서드 | 기능 |
|--------|------|
| `CreateProcessStartInfo(fileName, arguments, workingDirectory)` | 리다이렉트·UTF8·NoWindow 등 공통 옵션으로 `ProcessStartInfo` 생성. |
| `RunProcessAsync(fileName, arguments, log, workingDirectory)` | 프로세스 실행 후 stdout/stderr를 실시간으로 `log`에 넘기고, 종료 시 ExitCode 반환. |
| `RunProcessCaptureAsync(fileName, arguments, workingDirectory)` | 프로세스 실행 후 stdout/stderr 전체를 읽어 `(ExitCode, StdOut, StdErr)` 반환. |

---

### 2.9 P4 설정(Setup P4) 탭

| 메서드 | 기능 |
|--------|------|
| `BtnSetupP4Apply_Click` | Workspace 이름 검사 후 p4 set (P4IGNORE, P4CHARSET, P4USER, P4PORT, P4CLIENT) 및 p4 set/info 실행. Setup P4 로그에 출력. |
| `OnP4UserChecked(current, other1, other2)` | P4 사용자 체크박스 단일 선택(라디오처럼) 처리. |
| `CbP4UserDeveloper_Click` | Developer 선택 시 나머지 해제. |
| `CbP4UserEngine_Click` | Engine 선택 시 나머지 해제. |
| `CbP4UserGuest_Click` | Guest 선택 시 나머지 해제. |
| `GetSelectedP4User()` | 체크된 P4 사용자 체크박스의 Tag 값 반환(gw_developer / gw_engine / gw_guest). |

---

### 2.10 P4 -ztag 파싱·GWEditor

| 메서드 | 기능 |
|--------|------|
| `ParseP4ZtagInfo(string stdout)` | `p4 -ztag info` 출력에서 clientName, clientRoot, clientStream 추출해 튜플로 반환. |
| `RefreshGWEditorP4InfoAsync()` | p4 -ztag info로 Workspace·ClientRoot 조회 후, GWEditor 탭에 Workspace·Project 경로(GW.uproject)·Editor exe 경로(Engine InstallRoot 기준) 표시. |
| `BtnGWEditorRefresh_Click` | GWEditor 탭 정보 새로고침. `RefreshGWEditorP4InfoAsync()` 호출. |
| `BtnRunGWEditor_Click` | UI에 표시된 프로젝트(.uproject)·에디터 exe 경로로 UnrealEditor 실행 (`-nocompile -ddc=noshared`). |

---

### 2.11 탭 전환·자동 새로고침

| 메서드 | 기능 |
|--------|------|
| `MainTab_SelectionChanged` | 탭 선택 시 "GWEditor" / "p4 sync" / "Engine"에 따라 해당 자동 새로고침 메서드 호출. |
| `AutoRefreshEngineAsync()` | 중복 실행 방지 후 Engine 탭 상태 갱신. `RefreshEngineStatusAsync()` 호출. |
| `AutoRefreshP4SyncAsync()` | 중복 실행 방지 후 p4 sync 탭 정보 갱신. `RefreshP4SyncInfoAsync()` 호출. |
| `AutoRefreshGWEditorAsync()` | 중복 실행 방지 후 GWEditor 탭 P4 정보 갱신. `RefreshGWEditorP4InfoAsync()` 호출. |

---

### 2.12 p4 sync 탭

| 메서드 | 기능 |
|--------|------|
| `RefreshP4SyncInfoAsync()` | p4 -ztag info로 Workspace·ClientRoot·Stream 조회 후 UI 반영, `UpdateP4SyncClUiFromCurrentAsync(writeLog: true)`로 CL·동기화 필요 여부 표시. |
| `SetP4SyncClUi(localCL, buildCL, statusText, enableSync)` | 로컬 CL·빌드 CL·상태 텍스트·Sync 버튼 활성화 여부를 UI에 반영. |
| `QueryP4SyncClStateAsync(ws, root, writeLog)` | 로컬 최신 CL 조회, GW_ProjectBuild 최근 5개 CL 중 #JenkinsBuild 태그 있는 CL 찾기, 비교해 `(localCL, buildCL, needSync, status)` 반환. |
| `UpdateP4SyncClUiFromCurrentAsync(writeLog)` | 현재 Workspace/ClientRoot로 `QueryP4SyncClStateAsync` 호출 후 `SetP4SyncClUi`로 UI 갱신. |
| `BtnP4SyncRefresh_Click` | p4 sync 탭 수동 새로고침. `RefreshP4SyncInfoAsync()` 호출. |
| `ParseChangeNumber(string p4ChangesOutput)` | p4 changes 출력 문자열에서 "Change 12345" 형태의 changelist 번호 추출. |
| `BtnP4SyncRun_Click` | 확인 후 `QueryP4SyncClStateAsync`로 상태 조회, needSync이고 buildCL 유효할 때만 `p4 sync ...@{buildCL}` 실행. 완료 후 UI 상태 다시 갱신. |

---

### 2.13 기타 UI

| 메서드 | 기능 |
|--------|------|
| `Hyperlink_RequestNavigate` | 하이퍼링크 클릭 시 URI를 기본 브라우저로 열고 이벤트 처리 완료 표시. |

---

### 2.14 Engine(Installed Build) 탭

| 메서드 | 기능 |
|--------|------|
| `GetInstallRoot(basePath, ueVersion)` | BasePath 아래 GW_Engine\{ueVersion} 경로 계산. (이미 GW_Engine이면 그대로 사용) |
| `ApplyEngineBasePathToUi(string basePath)` | BasePath·InstallRoot 텍스트박스에 표시. |
| `BtnEngineRefresh_Click` | Engine 탭 상태 수동 갱신. `RefreshEngineStatusAsync()` 호출. |
| `BtnEngineDownload_Click` | 엔진 zip 다운로드만 수행. `DownloadInstalledBuildAsync(installAfterDownload: false)`. |
| `BtnEngineDownloadInstall_Click` | 다운로드 후 압축 해제·설치까지 수행. `DownloadInstalledBuildAsync(installAfterDownload: true)`. |
| `RefreshEngineStatusAsync()` | 서버 latest.json·로컬 meta 조회 후 서버/로컬 정보·업데이트 필요 여부·다운로드/설치 버튼 상태 갱신. |
| `DownloadInstalledBuildAsync(bool installAfterDownload)` | 엔진 zip 다운로드 → 무결성 검증 → (옵션) 압축 해제·적용·meta 저장 후 상태 갱신. |
| `BtnEngineChangePath_Click` | 폴더 선택으로 Installed Build BasePath 변경 후 설정 저장·UI 반영·상태 갱신. |

---

## 3. 요약

- **클래스**: `MainWindow`, 내부 데이터 모델 `ServerBuildItem`, 실행 모드 `RunMode` enum.
- **메서드**: 생성자·빌드 목록/실행·로그·메뉴·서버 갱신·파일명 파싱·프로세스 실행·P4 설정·P4 -ztag·GWEditor·탭 자동 새로고침·p4 sync·Engine 탭 관련 메서드로 구성되어 있으며, 각각 위 표에 정리한 기능을 수행합니다.
