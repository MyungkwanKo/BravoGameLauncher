# GW Launcher & Run_GWLauncher (Bootstrap Updater)
#readme #GWLauncher
통합 프로젝트 구조 및 동작 방식 정리 문서  
(런처 + 런처 스타터)

### 배포 서버 URL (Nginx)

배포는 **IIS + 포트(:8000)** 가 아닌 **Nginx** 기준 경로를 사용합니다. 코드·`Jenkinsfile.groovy`의 상수와 맞춥니다.

| 구분 | 베이스 URL | 비고 |
|------|------------|------|
| Engine (`latest.json`) | `http://bravo-build.omnicraftlabs.co.kr/installed/` | Master 고정 — `InstalledBuildServices.cs` |
| Engine (Installed Build ZIP) | Master `bravo-build…` / Agent `bravo-agent…` | **v25:** ms 홀/짝 + failover 분산 다운로드 |
| 게임 빌드 (`builds.json`) | `http://bravo-build.omnicraftlabs.co.kr/builds/` | Master 고정 — `BuildListService.cs` |
| 게임 빌드 (ZIP) | Master `bravo-build…` / Agent `bravo-agent…` | **v25:** ms 홀/짝 + failover — `GameBuildLauncher.cs` |
| 런처 스타터 (`launcher.json`, 런처 ZIP) | `http://bravo-build.omnicraftlabs.co.kr/launcher/` | `Run_GWLauncher/Program.cs`, Jenkins `DOWNLOAD_BASE_URL` |

---
## 🆕 v28 변경 사항 요약

### ✅ 버전
- 런처 버전 **v28** (`LauncherVersionInfo.Version`)

### ✅ GW Sync 탭 — GWEditor 상태 메세지
- Sync 필요 상태 메세지 문구를 "배포된 프로젝트 **빌드**가 있습니다..." → "배포된 프로젝트 **바이너리**가 있습니다..."로 수정

### ✅ GameStarter 탭 — 클라이언트/DS 실행 옵션 기본값
- 클라이언트 실행 옵션 기본값: 비움(공란). 초기 로드 시와 Reset 버튼 모두 빈 칸으로 표시
- DS 실행 옵션 기본값: `-log -trace=cpu,frame,net,bookmark,stats -statnamedevents -tracefile -NetTrace=1` → `-port=7778 -MapBaseId=10111 -log -LogCmds="LogGW Verbose"`
- DS 실행 옵션 입력칸 높이를 1줄(28px)로 축소(기존 2줄/48px)

---
## 🆕 v27 변경 사항 요약

### ✅ 버전
- 런처 버전 **v27** (`LauncherVersionInfo.Version`)

### ✅ GameStarter 탭 — DS 실행 옵션 기본값
- DS 기본 인자에서 맵 지정 옵션 `/GWBattleRoyale/Maps/L_BR_Proto?port=7778` 제거
- 변경 후 기본값: `-log -trace=cpu,frame,net,bookmark,stats -statnamedevents -tracefile -NetTrace=1`

---
## 🆕 v26 변경 사항 요약

### ✅ 버전
- 런처 버전 **v26** (`LauncherVersionInfo.Version`)

### ✅ 탭 통합: Engine · Setup p4 · GWEditor → 하나의 "GW Sync" 탭 (접이식 섹션)
- **Engine / Setup p4 / GWEditor** 탭 3개를 **GW Sync 탭 하나**로 통합. 좌측 탭 메뉴는 **GW Sync / GameStarter** 2개로 축소(`TabItemEngine`, `TabItemSetupP4` 제거, `TabItemGWEditor`의 `Header`만 "GW Sync"로 변경).
- GW Sync 탭 안에 **`Perforce 설정` → `Engine` → `GWEditor`** 순서로 3개의 접이식 섹션(`Expander`)을 세로로 배치.
  - 각 섹션 헤더에 **상태 점(초록=정상/주황=주의/빨강=조치 필요)** 과 **제목 옆(24px 간격) 좌측 정렬 한 줄 요약**을 표시해, 접힌 상태에서도 조치 필요 여부를 바로 확인 가능(요약을 우측 정렬로 창 끝까지 늘이지 않아 창 크기와 무관하게 텍스트 잘림 없음). 요약 텍스트는 `Foreground` `#333333`, `FontSize` 13, `FontWeight` Medium으로 가독성 확보.
  - **Perforce 설정**: 워크스페이스 미설정 시에만 자동 펼침, 설정 후엔 접힘(요약: `{workspace} 적용됨`).
  - **Engine**: 업데이트가 필요하거나 미설치일 때만 자동 펼침(요약: `{label} · 최신/업데이트 필요/미설치`). 섹션 제목은 "Engine (Installed build)"에서 **"Engine"** 으로 단순화. 자동 펼침/접힘 판정은 **최초 상태 확인 시 1회만** 적용되며, 이후 사용자가 직접 펼치거나 접은 상태는 유지됨.
  - **GWEditor**: 항상 기본 펼침(가장 자주 쓰는 기능). 정보 표시는 라벨:값 세로 목록으로, 실행 버튼(새로고침·Editor실행·Sync·Data Sync·Local Rollback)은 우측 구분선 패널 없이 **한 줄**로 배치(시안 레이아웃).
  - **섹션 헤더 경고 색상 강조**: 정상(초록)이 아닌 상태(빨강=조치 필요, 주황=주의)일 때 작은 상태 점뿐 아니라 **섹션 헤더 바 배경 전체**에도 옅은 색(빨강 `#FDECEA`, 주황 `#FFF4E1`)을 입혀 접힌 상태에서도 눈에 잘 띄도록 함(`GetSectionStatusDotBrush`/`GetSectionHeaderBackgroundBrush` 공용 헬퍼, 3개 섹션 공통 적용).
- **로그창 통합 + 가변 크기**: 섹션별 로그(`TxtSetupP4Log`/`TxtEngineLog`/`TxtGWEditorLog`)를 없애고, GW Sync 탭 **하단의 통합 로그창(`TxtSharedLog`)** 하나만 사용. 각 섹션의 동작(P4 적용, Engine 상태 확인/다운로드, GWEditor 새로고침/Sync/Data Sync)은 모두 이 로그에 함께 쌓이며, 다른 섹션 동작 시 로그를 지우지 않음(각 동작이 자체 `=== ... ===` 헤더로 구간을 구분). 로그창 높이는 **고정이 아닌 가변**: 세션(Expander)을 접으면 로그창이 자동으로 커지고 펼치면 자동으로 작아지며(로그창 최소 높이 80px 항상 보장), 세션을 모두 펼쳐 내용이 창보다 커지면 로그창을 가리는 대신 **세션 영역 자체에 스크롤바**가 생김.
- **GWEditor 섹션 중복 정보 제거**: Workspace(P4CLIENT) 행(Perforce 설정 섹션과 중복), Editor(UnrealEditor.exe) 경로 행(내부적으로만 계산해 Editor 실행에 사용, UI 비노출), Sync 필요여부 행(섹션 헤더 상태 점·요약과 중복)을 본문에서 제거.
- **GWEditor 실행 가드**: 설치된 Engine이 없는 상태(미설치)에서 **Editor실행** 버튼을 누르면 UnrealEditor 실행을 시도하지 않고 "설치된 Engine이 없습니다. Engine 세션에서 먼저 엔진을 다운로드 + 설치해주세요." 경고 팝업만 표시.

### ✅ Perforce 설정 섹션 — 워크스페이스 조회 팝업 + 자동 갱신
- Workspace(P4CLIENT) 입력란 옆에 **조회** 버튼 추가. 클릭 시 현재 체크된 **P4USER**와 **로컬 PC의 host명**을 기준으로 `p4 -ztag clients -u {p4user}`를 조회해, **Host가 비어있거나(제한 없음) 로컬 host와 일치하는** 워크스페이스만 팝업 목록으로 표시.
- 팝업에서 워크스페이스를 선택하고 **확인**을 누르면 입력란에 채워짐. **닫기**를 누르면 아무것도 선택하지 않고 닫힘.
- 조회 결과가 없으면 팝업에 "조회된 워크스페이스가 없습니다" 안내 표시. 조회 자체가 실패하면 팝업을 띄우지 않고 로그·오류 메시지로 안내.
- **GW Sync 탭에 들어올 때마다 매번**(최초 1회가 아님) 현재 설정된 P4CLIENT(`p4 -ztag info`의 clientName)를 조회해 입력란에 반영. 조회 팝업에서 워크스페이스를 선택만 하고 **적용을 누르지 않은 채** 탭을 벗어났다가 돌아오면(재조회), 입력란은 그 미적용 값을 유지하지 않고 **실제 설정된 P4CLIENT** 값으로 되돌아감.

### ✅ Engine 섹션 — 버전 고정, 다운로드 정책, 로컬 zip 자동 정리
- **UE Version 드롭다운 제거**, 엔진 버전은 **UE5.6 고정**(버전 변경이 거의 없어 선택 UI가 실질적 의미가 없어짐).
- **다운로드**(단독) 버튼 제거. **다운로드 + 설치** 버튼만 유지(단독 다운로드는 실사용 의미가 없어 제거).
- **설치 경로(BasePath 변경...)** 설정 UI는 필수 기능으로 유지.
- 섹션 본문의 "서버 최신 Installed Build" / "로컬 Installed Build 상태" 박스는 제거 — 상단 섹션 헤더의 상태 점·요약으로 대체.
- **이전 버전 zip 자동 정리**: 다운로드 + 설치가 성공하면 `InstallRoot`에 남아있는 이전 버전 zip 파일들을 자동 삭제하고 방금 설치한 zip만 유지(`CleanupOldEngineZips`). 반복적인 버전 업데이트로 로컬에 zip이 계속 쌓이는 문제 방지.

---
## 🆕 v25 변경 사항 요약

### ✅ 버전
- 런처 버전 **v25** (`LauncherVersionInfo.Version`)

### ✅ Engine / GameStarter — ZIP 분산 다운로드 (Master + Agent)
- **대용량 ZIP** 다운로드 시 `bravo-build.omnicraftlabs.co.kr`(Master)와 `bravo-agent.omnicraftlabs.co.kr`(Agent-Win) 중 요청 시점 **ms 홀/짝**(짝수→Master, 홀수→Agent)으로 1차 호스트 선택.
- 1차 실패(연결 오류, HTTP 404/408/5xx 등) 시 **반대 호스트로 1회 자동 재시도(failover)**. HttpClient 타임아웃도 failover 대상.
- failover 전·최종 실패 시 **부분 다운로드 ZIP 삭제** (손상 캐시 방지).
- **`CancellationToken`으로 사용자 취소**(`IsCancellationRequested`) 시 failover 없이 즉시 중단.
- 다운로드 시 `[HOST] primary=…, failover=…` 로그로 호스트·결과 확인 가능.
- 공용 클래스: `DownloadHostRouter.cs`, `DownloadWithFailover.cs`.
- **`builds.json`·`latest.json`은 Master 고정** — 목록/매니페스트만 Master, ZIP만 분산.
- **Coop 런처:** `GameBuildLauncher.cs` 및 공용 클래스 **링크 컴파일**로 소스는 자동 반영. 사용자 PC 반영은 **Coop 별도 재빌드·배포** 필요.

---
## 🆕 v23 변경 사항 요약

### ✅ 버전
- 런처 버전 **v23** (`LauncherVersionInfo.Version`)

### ✅ GWEditor 탭 — 스트림(Depot)별 Sync 정책
- P4 워크스페이스의 **clientStream**을 기준으로 Sync·Local Rollback 동작을 분기합니다.
- **`//GWArt/ArtDev` (아트 스트림)**:
  - **Sync 필요여부**: 메시지 **「아트 스트림 입니다.」** 고정, 상태 아이콘 초록(가능).
  - **Sync** 버튼: **항상 활성화** (`GW_ProjectBuild CL`과 무관).
  - **Local Rollback** 버튼: **항상 비활성화**.
  - Sync 실행: ProjectBuild CL 기준이 아닌 **`p4 sync`** (스트림 헤드 동기화).
- **`//GW/dev` (개발 스트림)** 및 **그 외 스트림**: v22와 동일 (ProjectBuild CL 기준 Sync·Local Rollback 정책).

### ✅ GWEditor 탭 — 정보 표시
- **Workspace(P4CLIENT)**: `{P4CLIENT} ({clientRoot})` 형식으로 표시  
  - 예: `mk.ko_dev (E:\GW_P4\mk.ko_dev)`
- **Client stream** (신규): `p4 -ztag info`의 clientStream + 구분 라벨  
  - `//GW/dev` → `//GW/dev (개발 스트림)`  
  - `//GWArt/ArtDev` → `//GWArt/ArtDev (아트 스트림)`  
  - 그 외 → `{스트림명} (기타)`
- **Stream Latest CL** (신규): 워크스페이스 스트림 서버의 최신 submitted CL (`p4 changes -m1 -S {stream}`)
- **Project (.uproject)** UI 행 **삭제** — Editor 실행 시 `{clientRoot}\GW\GW.uproject` 경로는 내부에서 계산

### ✅ GWEditor 탭 — CL 조회 정리
- `p4 changes -m1` 공용 헬퍼(`QueryLatestChangeByArgsAsync`)로 **Local CL**·**Stream Latest CL** 조회를 통합.
- 새로고침 시 **Local CL**은 한 번만 조회한 뒤 Build CL·Data Sync 판단에 재사용 (중복 p4 호출 제거).

### ✅ GWEditor 탭 — v22와의 관계
- Data Sync·Sync 버튼 3줄 배치·장시간 작업 중 UI 잠금(v21) 등 v22 이하 동작은 **개발 스트림 기준** 그대로 유지됩니다.

### ✅ Engine 탭 — Installed Build 배포 경로 (flat)
- 서버 루트: **`http://bravo-build.omnicraftlabs.co.kr/installed/`** (엔진 버전 중간 폴더 없음)
- **latest.json**: `{루트}/latest.json` — JSON `engineVersion` 필드로 런처 선택 버전과 일치 여부 확인
- **Engine ZIP**: `{루트}/{zip.fileName}` — 예: `/installed/UE5.6_6661_40.zip`
- `latest.json`의 `zip.url`이 구 경로(`/installed/{버전}/...`)이면 런처가 flat URL로 자동 보정

---
## 🆕 v22 변경 사항 요약 (#PJTGW-1945)

### ✅ 버전
- 런처 버전 **v22** (`LauncherVersionInfo.Version`)

### ✅ GWEditor 탭 — Data Sync / `#DataTableGenerate`
- **Data Sync** 버튼을 **Sync**(GW_ProjectBuild 기준)와 **분리**했습니다.
- **DataTableGenerate CL** 필드: Local CL 이후 서버에서 **`gw_build`** 가 submit한 변경 중, **`#DataTableGenerate`** 가 Description에 포함된 CL 번호를 모두 표시합니다.
  - 조회 depot 범위: **`//GW/dev/...`** 및 **`//streamDepot/dev/DataTable/...`** (두 경로 모두에서 submitted `changes` 병합 후 태그 필터).
- **Data Sync 실행**: 태그가 붙은 CL만 **순차** 처리합니다. 각 CL마다 `p4 describe -s`로 해당 변경의 파일 목록(`//depot/file#rev`)을 얻은 뒤, 위 두 depot 경로에 속하는 파일만 `p4 sync`합니다.  
  → 경로 전체를 `@CL` 한 번에 맞추는 방식과 달리, **태그 CL에 실제로 포함된 파일만** 반영되도록 합니다.

### ✅ GWEditor 탭 — Sync 버튼 정책 (Project 빌드와 Local CL 불일치)
- **Sync** 버튼은 `GW_ProjectBuild CL`이 유효하면 **Local CL과 무관하게 항상 활성화**됩니다.
- 클릭 시 항상 **`p4 sync ...@{GW_ProjectBuild CL}`** 로 Project 빌드 기준 동기화를 수행합니다.  
  → Data Sync만 수행해 Local CL만 올라간 경우에도, Sync가 비활성화되어 Project 바이너리를 못 맞추는 문제를 피합니다.
- **Sync 필요여부** 표시는 참고용으로 유지되며, Sync 버튼 활성화와는 분리됩니다.

### ✅ GWEditor 탭 — UI
- 실행 버튼 **3줄** 배치: **새로고침 · Editor실행** / **Sync · Data Sync** / **Local Rollback** (동일 너비).
- **Sync 필요여부** 행 높이 조정(글자 잘림 완화). **DataTableGenerate CL** 입력란 추가(Local CL 행과 동일 높이).

### ✅ GWEditor 탭 — v21과의 관계
- 장시간 작업 중 **GWEditor 탭 잠금·타 탭 전환 제한** 등 v21 동작은 그대로 유지됩니다.

---
## 🆕 v21 변경 사항 요약

### ✅ 버전
- 런처 버전 **v21** (`LauncherVersionInfo.Version`)

### ✅ 장시간 작업 중 UI 잠금 (`MainWindow.xaml`, `MainWindow.xaml.cs`)
- **목적**: 한 기능이 끝나기 전에 다른 버튼·탭을 눌러 중복 동작·경쟁 상태가 나는 것을 줄입니다.
- **탭 컨트롤**: `MainLauncherTabs` 및 각 탭 `TabItemEngine` / `TabItemSetupP4` / `TabItemGWEditor` / `TabItemGameStarter`에 `x:Name`을 두어 잠금 시 다른 탭 헤더만 비활성화합니다.

### ✅ GameStarter 탭
- **캐시 삭제**, **빌드목록 새로고침**, **다운로드**, **게임 실행**이 진행되는 동안: 해당 탭의 빌드 타입·버튼·체크박스·인자·캐시 버튼·빌드 목록 등 **조작 가능한 요소를 비활성화**합니다.
- 동일 기간 동안 **Engine / Setup p4 / GWEditor 탭으로 전환할 수 없습니다** (GameStarter 탭 헤더만 유지).
- 빌드목록 새로고침은 사용자가 버튼으로 눌렀을 때만 위 잠금을 적용하고, 창 최초 로드 시 서버 목록 자동 로드는 기존처럼 전체 탭을 잠그지 않습니다.
- **캐시 삭제**: 확인 후 `Dispatcher.Yield(ApplicationIdle)`로 비활성화가 화면에 반영된 뒤, 캐시 루트 폴더를 **동기** `Directory.Delete`(recursive)로 삭제합니다. 캐시 내 파일을 **클라이언트·DS 등이 사용 중**이면 삭제가 실패할 수 있으므로, 삭제 전 관련 프로세스를 종료해 주세요.

### ✅ GWEditor 탭
- **Sync**: 확인 후 동기화가 끝날 때까지 **새로고침·Editor실행·Sync·Local Rollback·인자 Reset·인자 입력**을 비활성화하고, **다른 탭으로 이동할 수 없습니다**.
- **Local Rollback**: 확인 후 `p4 sync`가 끝날 때까지 위와 동일하게 **GWEditor 탭 전체 조작·타 탭 전환**을 비활성화합니다.
- Sync는 확인 전까지 **Sync 버튼만** 잠시 비활성화하여 중복 클릭을 막고, 취소·검증 실패 시에는 `finally`에서 정보 갱신 후 기존 규칙대로 버튼 상태가 복구됩니다.

---
## 🆕 v20 변경 사항 요약

### ✅ 버전
- 런처 버전 **v20** (`LauncherVersionInfo.Version`)

### ✅ GameStarter 탭 — 빌드 목록(WIN·DS 합집합) 및 클라이언트 열 (`MainWindow.xaml`, `MainWindow.xaml.cs`)
- **이전**: 서버 `builds.json`의 **WIN 목록만** 순회해 표시했기 때문에, WIN이 비어 있거나 특정 빌드가 **DS에만** 있으면 목록에 나오지 않음.
- **현재**: **클라이언트(WIN) 또는 DS** 중 하나라도 있으면 같은 논리 빌드가 목록에 포함되도록 **합집합**으로 구성.
  - WIN 행은 기존과 같이 생성하고, **어떤 WIN 행에도 짝으로 매칭되지 않은 DS ZIP**은 별도 행으로 추가(해당 행은 WIN `fileName` 없음, `DsFileName`만 설정).
  - 목록은 **빌드 시각(`SortKey`) 내림차순**으로 한 번 정렬해 표시.
- **컬럼 순서**: `Time` → **클라이언트**(`O`/`X`, WIN 패키지 유무) → **DS**(`O`/`X`, v19 규칙의 짝 DS 유무). DS만 있는 행은 클라이언트 `X`, DS `O`.
- **실행·다운로드**: 선택 행에 **WIN 패키지가 없는데** 클라이언트 실행 또는 클라이언트 다운로드가 켜져 있으면 안내 메시지 후 진행하지 않음(DS만 받거나 DS만 실행할 때는 클라이언트 체크 해제).

---
## 🆕 v18 변경 사항 요약

### ✅ 버전
- 런처 버전 **v18** (`LauncherVersionInfo.Version`)

### ✅ GameStarter 탭 — 다운로드 전용 버튼
- **다운로드** 버튼: **게임 실행**과 동일하게 빌드 선택 + **클라이언트 / DS** 체크 조합에 따라 ZIP을 **다운로드·압축 해제만** 수행하고 **프로세스는 실행하지 않음** (`GameBuildLauncher.PrepareBuildsOnlyAsync`)
- DS만 받을 때는 기존 DS 프로세스를 종료하지 않음 (실행 경로와 구분)
- **다운로드**와 **게임 실행**은 작업 중 서로 비활성화되어 중복 실행 방지
- 상단 버튼 순서: **빌드목록 새로고침 → 다운로드 → 게임 실행**

### ✅ GameStarter 탭 — 클라이언트 실행 옵션 기본값
- 시작 시 및 **Reset** 시 기본 인자:  
  `-trace=NetChannel,Cpu,Frame,Bookmark -tracefile -statnamedevents`  
  (`GameBuildLauncher.DefaultClientLaunchArgs`)

---
## 🆕 v19 변경 사항 요약

### ✅ 버전
- 런처 버전 **v19** (`LauncherVersionInfo.Version`)

### ✅ GameStarter 탭 — DS 컬럼(O/X) 및 짝 DS 파일명 (`MainWindow.xaml.cs`)
- 동일 **Jenkins 빌드 번호**에 DS ZIP이 여러 개(Development / Shipping / Test 등) 올라갈 수 있음. 이전에는 번호당 **첫 번째 DS만** 보아 Shipping/Test 행에서 DS가 잘못 `X`로만 나오는 문제가 있었음.
- **현재 동작**: 해당 번호의 DS 목록 가운데 **클라이언트(WIN)와 유효 Config가 같은 항목**만 짝으로 선택.
  - **유효 Config**: `builds.json`의 `config`가 비어 있지 않으면 그 값, 비어 있으면 파일명에서 `_Development_` / `_Shipping_` 여부로 추정(그 외 `Unknown`).
- **DS 컬럼 `O`**: 위 규칙으로 짝 DS가 있을 때만. **`X`**: 짝 없음 또는 Config 불일치.
- **`ServerBuildItem.DsFileName`**: `O`일 때만 서버의 실제 DS `fileName`을 넣음. `X`이면 빈 문자열 — Config 불일치 시 **Development DS zip이 잘못 붙지 않도록** 함.
- Jenkins 번호로 Config 일치 DS를 못 찾으면, 기존과 같이 **`클라이언트 zip stem + "_DS"`** basename으로 DS 후보를 찾고, 여기서도 Config가 같을 때만 짝으로 인정.
- **다운로드 / 게임 실행**: 선택 행의 `FileName`(WIN)과 `DsFileName`(비어 있으면 `stem_DS.zip` fallback)을 그대로 `GameBuildLauncher`에 넘겨 ZIP URL `{버전}/WIN|DS/{파일명}`이 선택한 빌드·Config와 일치하도록 유지.

---
## 🆕 v17 변경 사항 요약

### ✅ 버전
- 런처 버전 **v17** (`LauncherVersionInfo.Version`)

### ✅ 다운로드 진행률 + 용량 표시
- **Engine 탭**: Installed Build ZIP 다운로드 시 진행 문구에 **받은 용량 / 총 용량**을 함께 표시 (`InstalledBuildServices.DownloadZipAsync`, `DownloadProgressFormatter.cs`)
- **GameStarter 탭**: 게임 빌드(WIN/DS) ZIP 다운로드 시에도 동일하게 **현재 / 총** 용량 표시 (`GameBuildLauncher.cs`)
- **표기 규칙**: **총 용량** 기준으로 단위 결정 — **1 GiB(1024³ 바이트) 이상이면 GB**, 미만이면 **MB**. 현재·총은 **항상 같은 단위**, 소수점 **둘째 자리**까지
- **총 크기를 알 수 없는 경우**(예: HTTP `Content-Length` 없음): `xx.xx MB / ?` 형태로 표시. Engine 탭은 이 경우 진행 **ProgressBar를 비결정(indeterminate)** 으로 표시

---
## 🆕 v16 변경 사항 요약

### ✅ 버전
- 런처 버전 **v16** (`LauncherVersionInfo.Version`)

### ✅ 배포 서버 (IIS → Nginx)
- 배포 방식을 IIS·`:8000` 기준에서 **Nginx** 기준 URL로 전환.
- **Engine(Installed Build)**: `http://bravo-build.omnicraftlabs.co.kr/installed/` — `InstalledBuildServices.cs`
- **게임 빌드** (`builds.json`, ZIP): `http://bravo-build.omnicraftlabs.co.kr/builds/` — `BuildListService.cs`, `GameBuildLauncher.cs`
- **런처 스타터** (`launcher.json`, 런처 ZIP 배포): `http://bravo-build.omnicraftlabs.co.kr/launcher/` — `Run_GWLauncher/Program.cs`, `Jenkinsfile.groovy`의 `DOWNLOAD_BASE_URL`
- README: 위 URL 표·`launcher.json` 예시·서버 패치 구조 문구 갱신

---
## 🆕 v15 변경 사항 요약

### ✅ 버전
- 런처 버전 **v15** (`LauncherVersionInfo.Version`)

### ✅ GameStarter 탭 — 빌드 목록
- **빌드 타입** 콤보: **All**(기본) / **Development** / **Shipping**
  - **All**: Shipping·Development 빌드를 한 목록에 시간순으로 표시.
  - **Config** 컬럼 값은 항상 **클라이언트(WIN) 빌드 기준** (예: 클라는 Shipping, DS만 Development여도 행에는 `Shipping` 표시).
- **DS 컬럼(O/X) (v15 당시)**: Jenkins 번호·파일명으로 DS 존재만 판별. **v19**에서 동일 번호에 DS가 여러 개일 때 **클라이언트와 Config가 같은 DS**만 `O`이고 `DsFileName`에 정확한 DS `fileName`을 넣도록 변경됨(아래 **v19** 참고).

### ✅ GameStarter 탭 — 클라이언트 실행 옵션
- 입력란을 **비운 채** 게임 실행 시: `GW.exe`에 **추가 인자 없음** (이전처럼 기본 `-log` 등 자동 삽입 없음).
- 내용을 **일부만** 넣은 경우: 입력한 문자열만 전달.
- **창모드** 체크 시: 옵션이 비어 있으면 `-windowed -ResX=1920 -ResY=1080`만 적용, 이미 옵션이 있으면 그 뒤에 창모드 인자를 덧붙임.
- **Reset**: 기본 명령 복원이 아니라 **입력란을 비움**. 초기 표시도 빈 칸.

### ✅ 안정성
- 게임 실행 경로에서 처리되지 않은 예외 시 로그·메시지로 안내 (다운로드/실행 실패 시 런처가 바로 종료되는 상황 완화).

---
## 🆕 v14 변경 사항 요약

### ✅ 버전
- 런처 버전 **v14**으로 업데이트 (`LauncherVersionInfo.Version`)

### ✅ GameStarter 탭
- **실행 옵션 UI 정리**
  - 기존 **실행 옵션** → **클라이언트 실행 옵션**으로 명칭 변경. `GW.exe` 인자 편집. (v15에서 기본 인자·Reset 동작이 정리됨 — 위 **v15** 참고.)
- **DS 실행 옵션** 추가 (클라이언트와 같은 방식)
  - `GWServer.exe`에 넘길 인자를 편집·Reset으로 기본값 복원.
  - 비어 있으면 기본값으로 실행.
  - 멀티라인 입력 시 **줄바꿈만 공백으로 합쳐** 한 줄 인자로 실행(따옴표 안 공백은 유지).
- **DS 기본 인자** (기존 `...?port=7778 -log`에 다음 추가)
  - `-trace=cpu,frame,net,bookmark,stats -statnamedevents -tracefile -NetTrace=1`
- **레이아웃**
  - 클라이언트/DS 옵션 입력란은 **Grid**로 남은 폭만 사용해 긴 줄이 창 밖으로 밀리지 않도록 정리.
  - **DS 실행 옵션**은 **2줄 높이** 멀티라인(`TextWrapping`)으로 긴 커맨드를 한 화면에서 확인하기 쉽게 표시.
  - 빌드 목록 `ListView`의 **가로 스크롤바 비활성화** → 하단 메시지 로그 영역과 겹치지 않도록 조정 (창 폭이 좁으면 오른쪽 열이 잘릴 수 있음).

---
## 🆕 v13 변경 사항 요약

### ✅ 버전
- 런처 버전 **v13**으로 업데이트

### ✅ 탭 메뉴 통합 (GWEditor)
- **p4 sync 탭과 GWEditor 탭을 하나의 GWEditor 탭으로 통합**
- 통합 탭에서 표시하는 항목:
  - Workspace(P4CLIENT), Project (.uproject), Editor (UnrealEditor.exe)
  - **Local CL**, **GW_ProjectBuild CL** (p4 sync에서 해당 2개 메뉴만 통합)
  - **Sync 필요 여부** (상태 아이콘은 이름칸 우측, 메시지 박스는 다른 박스와 동일 너비)
- **실행 버튼 2×2 배치** (구분선으로 메뉴 표시와 분리):
  - 1줄: **새로고침** | **Editor실행**
  - 2줄: **Sync** | **Local Rollback**
- **Sync**: GW_ProjectBuild CL까지 동기화 (기존 p4 sync 탭과 동일 동작)
- **Local Rollback**:
  - Local CL이 GW_ProjectBuild CL보다 **클 때만** 버튼 활성화
  - 클릭 시 로컬 워크스페이스를 GW_ProjectBuild CL 상태로 되돌림 (`p4 sync //...@buildCL`)
  - 확인 팝업: p4 명령/Client Root 문구 제거, **"열려 있는 파일이 있으면 진행이 되지 않습니다."** 문구 추가
- **Sync 필요 여부 메시지** (Local CL > ProjectBuild 시):
  - "최신 프로젝트 빌드이나 Editor 실행 시 에러 발생할 수 있습니다. 문제가 발생할 경우 Local Rollback 실행하거나 #bravl_all 채널로 Project 빌드를 요청하세요"

---
## 🆕 v12 변경 사항 요약

### ✅ 버전
- 런처 버전 **v12**로 업데이트

### ✅ GameStarter 탭
- **로컬 캐시 경로 탐색기 바로가기**: 캐시 경로 변경·캐시 삭제 버튼 우측에 **바로가기** 버튼 추가
  - 클릭 시 로컬 캐시 경로를 Windows 탐색기로 연다. (폴더 없으면 생성 후 열기)

### ✅ 파일 메뉴
- **런처 저장 경로 바로가기** 메뉴 추가 (종료 메뉴 위)
  - 런처 실행 파일(GWLauncher.exe)이 있는 폴더를 Windows 탐색기로 연다.

---
## 🆕 v11 변경 사항 요약

### ✅ GWEditor 탭
- **실행 기본 옵션**: `-ddc=noshared` 제거 → 기본값 `-nocompile`만 사용

### ✅ GameStarter 탭
- **실행 대상 선택**: "DS만 실행" 버튼 제거 → **클라이언트** / **DS** 체크박스로 변경
  - **클라이언트** 기본 체크, **DS** 체크 해제 상태
  - 클라이언트·DS 동시 선택 가능
- **게임 실행** 동작:
  - **클라이언트만** 체크: 클라이언트만 다운로드·실행 (DS 미다운/미실행)
  - **DS만** 체크: DS만 다운로드·실행 (클라이언트 미다운/미실행)
  - **둘 다** 체크: 기존과 동일하게 클라이언트·DS 모두 다운로드 후 실행
  - **둘 다** 미체크: 경고 메시지 표시 후 아무것도 실행하지 않음
- **실행 속도**: 이미 **unpacked** 폴더가 있으면 삭제·압축 해제 생략 후 바로 실행 (실행 시간 단축)
- **캐시 관리**: 상단 옵션 메뉴 제거 → GameStarter 탭 **로컬 캐시 경로** 옆에 **캐시 경로 변경**, **캐시 삭제** 버튼 개별 추가

### ✅ 상단 메뉴
- **옵션** 메뉴 삭제 (캐시 항목은 GameStarter 탭으로 이전)
- **파일** 메뉴: 런처 저장 경로 바로가기(v12), 종료(_X)

---
## 🆕 v8 변경 사항 요약

### ✅ p4_sync 탭 개선 (#PJTGW-1329)
- **로컬/배포빌드 CL 값 표시**: p4 sync 탭에서 **Local CL**, **GW_ProjectBuild CL**, **Sync 필요여부**를 바로 확인할 수 있도록 UI 추가
  - Local CL: 현재 워크스페이스 최신 체인지리스트
  - GW_ProjectBuild CL: Jenkins 빌드(#JenkinsBuild 태그) 기준 배포 빌드 CL
  - Sync 필요여부: "동기화 필요" / "동기화 불필요" 표시
- **Sync 버튼 조건부 활성화**: sync 할 빌드가 있을 때만(로컬 CL < 배포빌드 CL일 때만) Sync 버튼 활성화
- 새로고침 시 위 CL/상태를 조회해 UI 갱신 후 Sync 버튼 enable/disable 결정
- Sync 실행 후에도 CL 상태를 재조회하여 버튼 상태 갱신

---
## 🆕 v7 변경 사항 요약

### ✅ Engine 탭 추가 (Installed Build)
- **Engine 탭** 신규 추가: Unreal Engine Installed Build 다운로드/설치
- **엔진 버전 선택**: ComboBox로 버전 선택 (예: UE5.6), 설정에 저장
- **설치 경로**: 기본 경로 `{드라이브}\GW_Engine\{엔진버전}` 지원, "변경..."으로 BasePath 수정 가능
- **서버 연동**: `latest.json` 기반 최신 빌드 정보 표시 (label, CL, Jenkins 빌드, ZIP 정보)
- **로컬 상태**: `installed_build.meta.json`으로 설치 여부·버전 표시, 업데이트 필요 시 "다운로드 + 설치" 활성화
- **동작**:
  - **새로고침**: 서버/로컬 상태 재조회
  - **다운로드**: ZIP만 다운로드 (진행률, SHA256 검증)
  - **다운로드 + 설치**: 다운로드 후 압축 해제하여 `Engine` 폴더만 설치 경로에 적용
- 탭 진입 시 자동으로 상태 갱신

---
## 🆕 v5 변경 사항 요약

### ✅ GameStarter 기능 확장
- **기존 게임 실행 버튼 → '게임 실행'으로 명칭 변경**
- **'DS만 실행' 버튼 추가**
  - 창모드 옵션 우측에 배치
  - 선택된 빌드 기준 **DS ZIP만 다운로드/실행**
  - 이미 다운로드/압축 해제된 경우 재사용

### ✅ DS 실행 안정성 개선
- DS 실행 시 **기존 실행 중인 DS(GWServer.exe) 프로세스가 있으면 종료(Kill) 후 재실행**
- 게임 실행 / DS만 실행 모두 **동일한 DS 실행 로직을 재사용**

### ✅ 상단 고정 매뉴얼 링크 추가
- 런처 상단에 항상 노출되는 고정 링크 추가
- 좌측 메뉴 / 탭 전환과 무관하게 항상 표시
- 링크:
  [통합런처 사용 가이드 wiki 바로가기]  
  https://krafton.atlassian.net/wiki/spaces/PROJECTGW/pages/846770135/GW+Launcher

---

## 🆕 v4 변경 사항 요약

### ✅ Local / Server 실행 구조 정리
- Local / Server 실행 선택 UI 제거
- 런처에서는 **Local 실행 기준으로만 동작**
- Server 선택은 게임 내부 로비에서 처리

### ✅ Dedicated Server 실행 포트 변경
- DS 기본 포트: **7777 → 7778**
- DS 실행 인자:
```
/GWBattleRoyale/Maps/L_BR_Proto?port=7778 -log
```

### ✅ DS 프로세스 관리 강화
- 게임 실행 시 DS가 이미 실행 중이면:
  - 기존 DS 프로세스 종료
  - 새로운 DS 인스턴스 실행

---

## 🆕 v3 변경 사항 요약

### ✅ 통합 런처 구조 도입
- 개별 배치 실행 방식 제거
- 하나의 **GWLauncher(v3)** 에서 모든 작업 수행

### ✅ 탭 기반 기능 분리
GWLauncher는 좌측 탭 메뉴 기반으로 기능을 제공합니다.

| 탭 | 기능 |
|----|----|
| Engine | Installed Build(엔진) 다운로드/설치 (latest.json 기반) |
| Setup_p4 | Perforce 환경 변수 설정 |
| GWEditor | Unreal Editor 실행 + Client stream / Stream Latest CL, Sync / Data Sync / Local Rollback (v23 스트림별 Sync·UI, v22 Data Sync·Sync 정책, v13에서 p4_sync 통합) |
| GameStarter | 기존 v2 게임/DS 실행 기능 |

### ✅ 용어 재정의
| 명칭 | 의미 |
|----|----|
| **GWLauncher** | v3 통합 런처 (메인 프로그램) |
| **GameStarter** | v2까지의 기존 런처 기능 |
| **Run_GWLauncher** | 런처 스타터(부트스트랩 실행기) |

---

## 📁 1. 전체 프로젝트 구조

```
/Launcher Project Root
│
├─ GWLauncher/                    ← 통합 런처 (BravoGameLauncherGui.csproj, 출력명 GWLauncher)
│     ├─ MainWindow.xaml
│     ├─ MainWindow.xaml.cs
│     ├─ GameBuildLauncher.cs
│     ├─ BuildListService.cs
│     ├─ AppSettings.cs
│     ├─ InstalledBuildServices.cs   ← Engine(Installed Build) 다운로드/설치
│     ├─ DownloadProgressFormatter.cs ← 다운로드 진행 UI용 용량 포맷 (v17)
│     ├─ LauncherVersionInfo.cs
│     └─ (통합 런처 관련 전체 스크립트)
│
├─ Coop/                          ← 협업부서 배포용 CoopLauncher (GameStarter만, 별도 빌드)
│     ├─ Coop.sln
│     ├─ CoopLauncher/ …
│     └─ publish-coop-launcher.ps1
│
└─ Run_GWLauncher/                ← 런처 스타터
      ├─ Program.cs
      ├─ LauncherState.cs
      ├─ LauncherRemoteInfo.cs
      └─ (업데이트/실행 전용 코드)
```

---

## 📌 2. GWLauncher(v3) 기능 구성

### 🔸 Engine 탭
- Installed Build(Unreal Engine) 다운로드 및 설치
- 서버 **`/installed/latest.json`** (flat, 엔진 버전 폴더 없음) 기반 최신 빌드 정보 표시 — JSON `engineVersion`과 UI 선택 버전 일치 시 사용
- Engine ZIP: **`/installed/{fileName}`** (예: `UE5.6_6661_40.zip`)
- 설치 경로: `{BasePath}\GW_Engine\{엔진버전}` (BasePath 변경 가능)
- **다운로드**: ZIP만 다운로드 (진행률 + **받은 용량/총 용량** 표시 v17, SHA256 검증)
- **다운로드 + 설치**: 다운로드 후 압축 해제하여 `Engine` 폴더만 적용
- 로컬 `installed_build.meta.json`으로 설치 상태·업데이트 필요 여부 표시
- 탭 진입 시 자동 상태 갱신

### 🔸 Setup_p4 탭
- Perforce 환경 변수 설정 (1회성)
- Workspace(P4CLIENT) 사용자 직접 입력
- `p4 set` 결과 + `p4 info` 전체 로그 출력

### 🔸 GWEditor 탭 (v13: p4_sync 통합, v21: 장시간 작업 중 UI 잠금, v22: Data Sync·Sync 정책, v23: 스트림별 Sync·정보 표시)
- **메뉴 표시** (**v23**): **Workspace(P4CLIENT)** `{클라이언트명} ({clientRoot})`, **Client stream** `{스트림} ({구분})`, Editor (UnrealEditor.exe), **Local CL**, **Stream Latest CL**, **GW_ProjectBuild CL**, Sync 필요 여부, **DataTableGenerate CL** (상태 아이콘은 Sync 필요여부 이름칸 우측). Project (.uproject) 행은 UI에서 제거.
- **실행 버튼** (구분선으로 메뉴와 분리, **v22**): 새로고침 | Editor실행 / **Sync | Data Sync** / Local Rollback (3줄·동일 너비)
- **v21**: Sync·Data Sync·Local Rollback 확인 후 실행 중에는 위 버튼·에디터 인자·타 탭 전환이 비활성화됩니다.
- **Editor 실행**: Client Root 기준 Unreal Editor 실행
  - Engine\Binaries\Win64\UnrealEditor.exe, `{clientRoot}\GW\GW.uproject` (내부 계산)
  - 실행 기본 옵션: **-nocompile** (v11에서 -ddc=noshared 제거)
- **Sync** (**v22**, **v23** 아트 스트림 분기):
  - **개발·기타 스트림**: `GW_ProjectBuild CL`이 있으면 Local CL과 무관하게 버튼 활성화. 클릭 시 **`p4 sync ...@{GW_ProjectBuild CL}`**.
  - **아트 스트림** (`//GWArt/ArtDev`): Sync **항상 활성**, Local Rollback **항상 비활성**, Sync 필요여부 **「아트 스트림 입니다.」**, 실행 시 **`p4 sync`**.
- **Data Sync** (**v22**): `#DataTableGenerate` 태그가 있는 submit CL만 순차 처리. 각 CL은 describe로 얻은 **해당 변경 파일** 중 `//GW/dev/...`·`//streamDepot/dev/DataTable/...` 만 sync.
- **Local Rollback**: Local CL > GW_ProjectBuild CL일 때만 활성화(아트 스트림 제외), 로컬을 Build CL 상태로 되돌림 (`p4 sync //...@buildCL`)
- **Stream Latest CL** (**v23**): 워크스페이스 clientStream 서버 최신 CL 표시
- 탭 진입 시 자동 정보 갱신

### 🔸 GameStarter 탭
- **v21**: 캐시 삭제·목록 새로고침(버튼)·다운로드·게임 실행 중에는 탭 내 주요 컨트롤과 **다른 탭**이 비활성화됩니다.
- Jenkins `builds.json` 기반 빌드 목록
- **빌드 타입**: All(기본) / Development / Shipping — All이면 두 타입 통합 표시, **Config는 WIN(클라이언트) 기준** (v15). **DS만 있는 행**은 해당 DS의 Config로 필터·표시 (v20).
- **목록 구성 (v20)**: WIN 목록과 **WIN에 짝으로 쓰이지 않은 DS**를 합쳐 표시. **클라이언트·DS 열** 각각 `O`/`X`.
- **DS O/X·짝 DS**: 동일 Jenkins 번호에 DS가 여러 개면 **클라이언트와 Config가 같은 DS**만 `O`, `DsFileName`에 서버 `fileName` 저장 (v19). 없으면 basename `stem_DS` fallback 후에도 Config 일치할 때만 짝 인정.
- **실행 대상**: 클라이언트 / DS 체크박스 (클라이언트 기본 체크, 둘 다 동시 선택 가능)
- **다운로드** 버튼 (v18): 동일 선택으로 ZIP **다운로드·압축 해제만** (실행 없음). 버튼 순서: 새로고침 → 다운로드 → 게임 실행
- **게임 실행** 버튼: 선택에 따라 클라이언트만 / DS만 / 둘 다 다운로드·실행 (v11). ZIP 다운로드 중 진행 문구에 **현재/총 용량** 표시 (v17, Engine 탭과 동일 규칙)
- 둘 다 미체크 시 경고 후 실행·다운로드하지 않음
- **클라이언트 실행 옵션** (v18): 기본·Reset 시 `-trace=NetChannel,Cpu,Frame,Bookmark -tracefile -statnamedevents`. 내용을 지우면 인자 없음으로 실행. **DS 실행 옵션** (v14, v27): `GWServer.exe` 인자 편집·Reset, 비어 있으면 기본값(`-log -trace=...`, 맵 지정 없음), 멀티라인은 줄바꿈만 공백으로 합침. 상세는 **v14·v15·v18·v27 변경 사항** 참고.
- **캐시**: 로컬 캐시 경로 표시 + **캐시 경로 변경**, **캐시 삭제**, **바로가기**(탐색기 열기, v12) 버튼 (v11에서 상단 옵션 메뉴에서 이전)
- **unpacked 재사용**: 이미 압축 해제된 폴더가 있으면 삭제·재압축 해제 없이 바로 실행 (v11, 실행 시간 단축)

---

## 📌 3. Run_GWLauncher (런처 스타터)

### 역할
- 서버 launcher.json 기반 최신 런처 확인
- 최신 GWLauncher 다운로드 및 실행

### 특징
- Perforce에 올라가는 유일한 실행 파일
- 업데이트 빈도 매우 낮음

---

## 🧠 4. 실행 흐름

```
Run_GWLauncher.exe
      ↓
GWLauncher.exe 실행
      ↓
[Engine | Setup_p4 | GWEditor | GameStarter]
```

---

## 🛠 5. 빌드 방법

### ✔ GWLauncher 빌드
```
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  --self-contained false
```

### ✔ Run_GWLauncher 빌드
```
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  --self-contained false
```

---

## ✔ 6. 요약

| 구성 요소 | 역할 |
|----|----|
| GWLauncher | 통합 개발 런처 |
| Engine | Installed Build(엔진) 다운로드/설치 |
| GameStarter | 게임/DS 실행 |
| Run_GWLauncher | 런처 스타터 |
| Setup_p4 | Perforce 초기 설정 |
| GWEditor | Unreal Editor 실행 + Sync / Data Sync / Local Rollback (v23 스트림별 Sync·UI, v22, v13에서 p4_sync 통합) |


---
# 🆕 v2 변경 사항 요약 (릴리즈 노트)

GW Launcher v2에서 반영된 핵심 변경 사항입니다.

## ✅ 빌드 목록 개선
- Jenkins `builds.json`의 플랫폼 구조(`WIN`/`DS`)를 기준으로 빌드 목록을 표시합니다.
- **DS 컬럼 `O/X` (현행 v19)**:
  - **동일 Jenkins 빌드 번호**의 DS 후보가 여러 개면, **클라이언트(WIN)와 유효 Config가 같은 DS**만 `O`.
  - 번호로 못 찾으면 **클라이언트 stem + `_DS`** basename으로 DS 후보를 찾고, 여기서도 Config 일치 시만 `O`.
  - 예: `..._Shipping_....zip` 행 → `..._Shipping_..._DS.zip`과 짝(같은 Jenkins·같은 Config).

## ✅ Local 실행 시 DS 자동 처리 (요구사항 2 + 3)
- **Local 실행**: DS가 존재(O)하면 **DS와 클라이언트를 함께 다운로드/압축해제**하고 실행합니다.
  - 다운로드/압축해제는 **병렬 진행**(순서 무관)
  - 실행 순서는 **DS 먼저 → 클라이언트 실행**
  - DS가 이미 실행 중이면 **기존 DS 프로세스(GWServer.exe) 종료 후 재실행**
- **Server 실행**: 클라이언트만 실행하며 DS는 다운로드/실행하지 않습니다.

### DS 실행 커맨드 (기본값)
GameStarter **DS 실행 옵션** 기본값(v27, Reset 시 동일). UI에서 수정 가능.
```
GWServer.exe -log -trace=cpu,frame,net,bookmark,stats -statnamedevents -tracefile -NetTrace=1
```

## ✅ DS 다운로드 버튼 정책
- v2부터 DS는 Local 실행 시 자동 처리되므로, 별도의 DS 다운로드 버튼/핸들러에 의존하지 않습니다.

---

# 📁 1. 전체 프로젝트 구조

현재 GW Launcher 솔루션에는 **런처(GWLauncher)** 와 **런처 스타터(Run_GWLauncher)** 두 구성 요소가 함께 관리됩니다.

```
/Launcher Project Root
│
├─ GWLauncher/                    ← GW 런처(WPF, csproj: BravoGameLauncherGui.csproj)
│     ├─ MainWindow.xaml
│     ├─ MainWindow.xaml.cs
│     ├─ GameBuildLauncher.cs
│     ├─ BuildListService.cs
│     ├─ AppSettings.cs
│     ├─ DownloadProgressFormatter.cs
│     ├─ LauncherVersionInfo.cs
│     └─ ... (런처 관련 전체 스크립트)
│
├─ Coop/                          ← CoopLauncher (협업부서용, 선택 빌드)
│     └─ …
│
└─ Run_GWLauncher/                ← 런처 스타터(콘솔)
      ├─ Program.cs
      ├─ LauncherState.cs
      ├─ LauncherRemoteInfo.cs
      └─ (기타 부트스트랩 관련 파일)
```

---

# 📌 2. 구성 요소 설명

---

## 🔹 2-1. **GWLauncher (BravoGameLauncherGui) — 런처 본체**

### 역할
- Jenkins 빌드 서버에서 게임 빌드 ZIP 목록 로드  
- 로컬 캐시에 ZIP 다운로드 / 압축 해제 / 실행  
- 빌드 타입 필터(All / Development / Shipping), Config는 클라이언트(WIN) 기준 표시 (v15)  
- DS 짝: Jenkins 번호당 DS 다중 시 **Config 일치** DS만 O 및 `DsFileName` 지정 (v19)  
- 실행 옵션(Local / Server / Windowed)  
- Jenkins 빌드 번호 포함한 빌드 목록 표시  
- **서버 패치 기반 업데이트를 대비한 정수 버전 구조 포함 (`LauncherVersionInfo.Version`)**

### 구성 파일 및 기능

#### ✔ MainWindow.xaml / MainWindow.xaml.cs
- UI 및 전체 런처 동작 관리  
- 빌드 리스트 표시 / 체크박스 선택  
- 실제 게임 빌드 실행 버튼 처리  
- DS는 Local 실행 시 자동 처리(다운로드/압축해제/실행)  
- ServerBuildItem(빌드 표시 모델) 관리

#### ✔ GameBuildLauncher.cs
- ZIP 다운로드  
- 캐시 구조 관리  
- 압축 해제  
- 실행 파일(GW.exe) 검색  
- 실행 인자 구성 및 프로세스 실행  
- (v17) 다운로드 진행 콜백 메시지에 **현재/총 용량** 문자열 포함 (`DownloadProgressFormatter` 사용)
- (v18) `PrepareBuildsOnlyAsync`: 다운로드·압축 해제만 수행 / `DefaultClientLaunchArgs` 클라이언트 기본 실행 인자

#### ✔ DownloadProgressFormatter.cs (v17)
- 다운로드 진행 UI용: 받은 바이트·총 바이트를 동일 단위(총 ≥1GiB → GB, 미만 → MB)로 포맷

#### ✔ BuildListService.cs
- 서버 `http://bravo-build.omnicraftlabs.co.kr/builds/builds.json` 다운로드  
- JSON → 빌드 리스트로 변환  
- Jenkins 빌드 번호(`jenkinsBuildNumber`) 포함

#### ✔ AppSettings.cs
- 캐시 경로 저장  
- 최근 실행한 ZIP 기록 저장  
- Engine 탭: Installed Build 기본 경로(`InstalledBuildBasePath`), 선택 엔진 버전(`SelectedEngineVersion`) 저장

#### ✔ LauncherVersionInfo.cs
- 런처 버전 정보 (정수 기반 버전: v1, v2, v3...)  
- 서버 패치 업데이트 시 버전 비교 용도  
- Window Title에 표시할 버전 정보 제공

---

## 🔹 2-2. **Run_GWLauncher — 런처 스타터(부트스트랩)**

### 역할
사용자가 Run_GWLauncher.exe만 실행하면:

1. 서버의 `launcher.json` 확인  
2. 최신 버전 여부 판단  
3. 필요 시 런처 ZIP 다운로드  
4. `%LOCALAPPDATA%/GWLauncher/Launcher/v{N}/` 에 설치  
5. 최신 GWLauncher.exe 실행  
6. 종료

즉, **런처 자동 업데이트 + 설치 + 실행 관리자**

### 구성 파일 및 기능

#### ✔ Program.cs
- Run_GWLauncher 전체 흐름  
- launcher_state.json 로드  
- 서버 launcher.json 다운로드  
- 최신 버전 판단  
- 업데이트 수행  
- GWLauncher.exe 실행

#### ✔ LauncherState.cs
- 로컬 상태 저장 모델  
- JSON 저장 위치:
```
%LOCALAPPDATA%/GWLauncher/launcher_state.json
```

필드:
| 필드 | 설명 |
|------|------|
| installedVersion | 설치된 런처 버전 |
| installedPath | 설치된 GWLauncher.exe 경로 |
| lastCheckedAt | 마지막 체크 시각 |

#### ✔ LauncherRemoteInfo.cs
- 서버 launcher.json 구조 매핑

launcher.json 구성 예:

```json
{
  "latestVersion": 2,
  "minSupportedVersion": 1,
  "package": {
    "fileName": "GWLauncher_v2.zip",
    "downloadUrl": "http://bravo-build.omnicraftlabs.co.kr/launcher/GWLauncher_v2.zip"
  },
  "releaseNotes": "v2: DS 표기 및 Local DS 자동 실행/안정화"
}
```

---

# 🧠 3. 런처와 런처 스타터의 동작 관계

```
사용자 실행
       ↓
[ Run_GWLauncher.exe ]
       │
       ├─ launcher_state.json 읽기
       ├─ 서버 launcher.json 읽기
       ├─ 버전 비교
       ├─ 최신 GWLauncher_v{N}.zip 설치
       └─ GWLauncher.exe 실행
                 ↓
          [ BravoGameLauncherGui ]
```

### ✔ 역할 구분 요약

| Run_GWLauncher (Starter) | GWLauncher (Launcher) |
|---------------------------|------------------------|
| 업데이트 체크 | 게임 빌드 UI 표시 |
| ZIP 다운로드 및 설치 | 빌드 ZIP 다운로드 |
| 최신 런처 실행 | 게임 실행 기능 |
| 자체 UI 없음 | UI 기반 런처 |

---

# 🛠 4. 빌드 방법 (dotnet CLI)

런처 & 런처 스타터 모두 Visual Studio 없이 **dotnet CLI만으로 빌드 가능**.

---

## ✔ 4-1. 런처(GWLauncher) 빌드

BravoGameLauncherGui.csproj 위치에서:

```bash
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  --self-contained false
```

출력 위치:

> 현재 프로젝트 TFM 예: `net10.0-windows` (환경에 따라 달라질 수 있음)


```
BravoGameLauncherGui/bin/Release/<TFM>/win-x64/publish/GWLauncher.exe
```

ZIP 패키징 시:
```
GWLauncher_v1.zip
GWLauncher_v2.zip
```

---

## ✔ 4-2. 런처 스타터(Run_GWLauncher) 빌드

Run_GWLauncher.csproj 위치에서 실행:

```bash
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  --self-contained false
```

출력 위치:

> 현재 프로젝트 TFM 예: `net10.0-windows` (환경에 따라 달라질 수 있음)


```
Run_GWLauncher/bin/Release/<TFM>/win-x64/publish/Run_GWLauncher.exe
```

---

# 📡 5. 서버 패치 구조 요약

Nginx에서 런처 패키지를 노출하는 디렉터리 예시 (`/launcher/`):

```
launcher/
  ├─ launcher.json
  ├─ GWLauncher_v1.zip
  ├─ GWLauncher_v2.zip
  ├─ GWLauncher_v3.zip
  └─ ...
```

`Run_GWLauncher`는 `http://bravo-build.omnicraftlabs.co.kr/launcher/launcher.json`을 읽고, `package.downloadUrl`로 ZIP을 받습니다. Jenkins 파이프라인(`Jenkinsfile.groovy`)의 `DOWNLOAD_BASE_URL`이 위 베이스와 일치해야 합니다.

### launcher.json 예시

```json
{
  "latestVersion": 2,
  "minSupportedVersion": 1,
  "package": {
    "fileName": "GWLauncher_v2.zip",
    "downloadUrl": "http://bravo-build.omnicraftlabs.co.kr/launcher/GWLauncher_v2.zip"
  },
  "releaseNotes": "v2: DS 표기 및 Local DS 자동 실행/안정화"
}
```

Run_GWLauncher는 이 파일을 기준으로 업데이트 수행.

---

# ✔ 6. 요약

| 구성 요소 | 목적 |
|-----------|--------|
| **Run_GWLauncher.exe** | 런처 자동 업데이트 + 설치 + 실행 |
| **launcher_state.json** | 로컬 설치 버전 관리 |
| **launcher.json** | 서버 최신 버전 정보 |
| **GWLauncher.exe** | 실제 게임 빌드 실행 기능 담당 |
| **BravoGameLauncherGui** | UI 기반 게임 실행 런처 |

---

필요하면 이 문서에 Jenkins 자동 배포 매뉴얼도 추가해줄 수 있어.

---

## 변경 이력 (Changelog)

| 버전 | 날짜 | 변경 요약 |
|------|------|-----------|
| v28 | 2026-07-30 | GW Sync: GWEditor 상태 메세지 "배포된 프로젝트 빌드가 있습니다" → "배포된 프로젝트 바이너리가 있습니다"; GameStarter: 클라이언트 실행 옵션 기본값 비움, DS 실행 옵션 기본값을 `-port=7778 -MapBaseId=10111 -log -LogCmds="LogGW Verbose"`로 변경, DS 입력칸 1줄 높이로 축소 |
| v27 | 2026-07-24 | GameStarter: DS 실행 옵션 기본값에서 맵 지정(`/GWBattleRoyale/Maps/L_BR_Proto?port=7778`) 제거 |
| v26 | 2026-07-08 | Engine·Setup p4·GWEditor 탭을 **"GW Sync" 탭 하나**로 통합(접이식 섹션, 상태 점·좌측정렬 요약, 경고 시 헤더 배경 강조, 자동 펼침은 최초 1회만); 통합 로그창은 **가변 높이**(세션 접으면 자동 확대, 펼치면 축소, 최소 80px 보장, 넘치면 세션 영역에 스크롤바); Perforce 설정에 **워크스페이스 조회 팝업**(P4User+로컬 host 기준) 추가, **탭 진입마다 매번** 현재 P4CLIENT 재조회(미적용 값은 덮어씀); Engine 섹션 **UE Version 드롭다운 제거**(UE5.6 고정), **다운로드 단독 버튼 제거**(다운로드+설치만 유지), 설치 성공 시 **이전 버전 zip 자동 삭제**; GWEditor는 **Engine 미설치 시 실행 버튼에 경고**만 표시하고 실행 시도 안 함 |
| v25 | 2026-07-03 | Engine·GameStarter: **Master/Agent ZIP 분산**(ms 홀/짝 + failover, 부분 ZIP 삭제, 취소 시 failover 제외), `DownloadHostRouter`·`DownloadWithFailover`; JSON Master 고정; Coop 소스 링크(별도 배포) |
| v23 | 2026-05-29 | GWEditor: **스트림별 Sync 정책**(`//GWArt/ArtDev` 아트 스트림 — Sync 항상 가능·Local Rollback 비활성·「아트 스트림 입니다.」), **Client stream**·**Stream Latest CL** 표시, Workspace `{클라이언트} ({clientRoot})` 형식, Project UI 행 제거, CL 조회 공용화; Engine: **Installed Build flat 배포 경로** (`/installed/latest.json`, `/installed/{zip}`) |
| v22 | 2026-04-28 | GWEditor: **Data Sync** 분리, `#DataTableGenerate` CL 표시·경로별 파일 sync(`//GW/dev/...`, `//streamDepot/dev/DataTable/...`), **Sync**는 ProjectBuild CL 기준 항상 수행 가능(Local CL과 무관). UI 3줄 버튼·레이아웃 조정 (#PJTGW-1945) |
| v21 | 2026-04-17 | GameStarter/GWEditor: 장시간 작업 중 **탭 내 버튼·입력 비활성화** 및 **다른 탭 전환 차단**; 캐시 삭제는 UI 한 틱 양보 후 동기 삭제(캐시 사용 중 프로세스 종료 필요 시 안내) |
| v20 | 2026-04-16 | GameStarter: 빌드 목록 **WIN·DS 합집합**(DS만 있어도 표시); **클라이언트(O/X)** 열 추가; WIN 없는 행에서 클라이언트 실행·다운로드 선택 시 안내 |
| v19 | 2026-04-15 | GameStarter: 동일 Jenkins 번호에 DS 여러 개일 때 **Config 일치** DS만 DS열 O·`DsFileName` 설정; basename fallback에도 Config 검증 |
| v18 | 2026-04-10 | GameStarter 다운로드 전용 버튼·실행과 버튼 순서(다운로드→게임 실행); 클라 기본 인자 trace(`DefaultClientLaunchArgs`); `PrepareBuildsOnlyAsync` |
| v17 | 2026-03-31 | 다운로드 진행률에 용량(현재/총) 표시 — Engine·GameStarter; 총 ≥1GiB는 GB·미만은 MB·소수 2자리; `DownloadProgressFormatter.cs` 추가 |
| v16 | 2026-03-25 | 배포 서버 IIS→Nginx URL 전환 (`/installed/`, `/builds/`, `/launcher/`); `InstalledBuildServices`·`GameBuildLauncher`·`BuildListService`·`Run_GWLauncher`·`Jenkinsfile` 반영; README 배포 URL·예시 문서 갱신 |
| v15 | 2026-03-25 | GameStarter: 빌드 목록 All·Config는 클라 기준·DS는 빌드번호/실제 DS 파일명 매칭; 클라 실행 옵션 비우면 무인자·Reset/기본 -log 제거; 실행 예외 처리 보강 |
| v14 | 2026-03-23 | GameStarter: 클라이언트/DS 실행 옵션 분리·DS 기본 trace 인자; DS 옵션 멀티라인·빌드 목록 가로 스크롤 비활성화로 로그 영역 겹침 방지 |
| v13 | 2026-03-19 | 탭 통합: p4_sync → GWEditor 통합; Local CL/Build CL 표시, Sync·Local Rollback 버튼 2×2; Sync 필요 여부 메시지·아이콘 위치·구분선 보완 |
| v12 | 2026-03-17 | 런처 v12; GameStarter 캐시 경로 탐색기 바로가기 버튼; 파일 메뉴 런처 저장 경로 바로가기 추가 |
| v11 | 2026-03-04 | GWEditor 기본 옵션 -ddc=noshared 제거; GameStarter 클라이언트/DS 체크박스·unpacked 재사용·캐시 버튼 탭 이전; 상단 옵션 메뉴 제거 |
| v8 | 2026-02-19 | p4_sync 탭: 로컬/배포빌드 CL 표시, Sync 버튼 조건부 활성화 (#PJTGW-1329, commit ce5d777) |
