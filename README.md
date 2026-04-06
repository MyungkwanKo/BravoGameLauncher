# GW Launcher & Run_GWLauncher (Bootstrap Updater)
#readme #GWLauncher
통합 프로젝트 구조 및 동작 방식 정리 문서  
(런처 + 런처 스타터)

### 배포 서버 URL (Nginx)

배포는 **IIS + 포트(:8000)** 가 아닌 **Nginx** 기준 경로를 사용합니다. 코드·`Jenkinsfile.groovy`의 상수와 맞춥니다.

| 구분 | 베이스 URL | 비고 |
|------|------------|------|
| Engine (Installed Build, `latest.json` 등) | `http://bravo-build.omnicraftlabs.co.kr/installed/` | `InstalledBuildServices.cs` |
| 게임 빌드 (`builds.json`, ZIP 다운로드) | `http://bravo-build.omnicraftlabs.co.kr/builds/` | `BuildListService.cs`, `GameBuildLauncher.cs` — ZIP 경로: `{버전}/WIN 또는 DS/{zip파일명}` |
| 런처 스타터 (`launcher.json`, 런처 ZIP) | `http://bravo-build.omnicraftlabs.co.kr/launcher/` | `Run_GWLauncher/Program.cs`, Jenkins `DOWNLOAD_BASE_URL` → `launcher.json`의 `package.downloadUrl` 조합 |

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
- **DS 컬럼(O/X)**: 동일 **Jenkins 빌드 번호**로 DS 목록에 항목이 있으면 `O`. 없으면 기존 **파일명 `클라이언트베이스_DS`** 규칙으로 fallback.
- **실행 시 DS ZIP**: 위 매칭으로 서버에 올라온 **실제 DS `fileName`**을 사용 (클라/DS 설정명이 달라도 잘못된 추측 파일명으로 404 나는 문제 방지).

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
| GWEditor | Unreal Editor 실행 + Local/Build CL 표시, Sync, Local Rollback (v13에서 p4_sync 통합) |
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
- 서버 `latest.json`(엔진 버전별) 기반 최신 빌드 정보 표시
- 설치 경로: `{BasePath}\GW_Engine\{엔진버전}` (BasePath 변경 가능)
- **다운로드**: ZIP만 다운로드 (진행률 + **받은 용량/총 용량** 표시 v17, SHA256 검증)
- **다운로드 + 설치**: 다운로드 후 압축 해제하여 `Engine` 폴더만 적용
- 로컬 `installed_build.meta.json`으로 설치 상태·업데이트 필요 여부 표시
- 탭 진입 시 자동 상태 갱신

### 🔸 Setup_p4 탭
- Perforce 환경 변수 설정 (1회성)
- Workspace(P4CLIENT) 사용자 직접 입력
- `p4 set` 결과 + `p4 info` 전체 로그 출력

### 🔸 GWEditor 탭 (v13: p4_sync 통합)
- **메뉴 표시**: Workspace(P4CLIENT), Project (.uproject), Editor (UnrealEditor.exe), **Local CL**, **GW_ProjectBuild CL**, Sync 필요 여부 (상태 아이콘은 이름칸 우측)
- **실행 버튼** (구분선으로 메뉴와 분리): 새로고침 | Editor실행 / Sync | Local Rollback
- **Editor 실행**: Client Root 기준 Unreal Editor 실행
  - Engine\Binaries\Win64\UnrealEditor.exe, GW\GW.uproject
  - 실행 기본 옵션: **-nocompile** (v11에서 -ddc=noshared 제거)
- **Sync**: GW_ProjectBuild CL까지 동기화 (sync 할 빌드가 있을 때만 버튼 활성화)
- **Local Rollback**: Local CL > GW_ProjectBuild CL일 때만 활성화, 로컬을 Build CL 상태로 되돌림 (`p4 sync //...@buildCL`)
- 탭 진입 시 자동 정보 갱신

### 🔸 GameStarter 탭
- Jenkins `builds.json` 기반 빌드 목록
- **빌드 타입**: All(기본) / Development / Shipping — All이면 두 타입 통합 표시, **Config는 WIN(클라이언트) 기준** (v15)
- **DS O/X**: Jenkins 빌드 번호 우선, DS 실제 파일명으로 다운로드 (v15)
- **실행 대상**: 클라이언트 / DS 체크박스 (클라이언트 기본 체크, 둘 다 동시 선택 가능)
- **게임 실행** 버튼: 선택에 따라 클라이언트만 / DS만 / 둘 다 다운로드·실행 (v11). ZIP 다운로드 중 진행 문구에 **현재/총 용량** 표시 (v17, Engine 탭과 동일 규칙)
- 둘 다 미체크 시 경고 후 실행하지 않음
- **클라이언트 실행 옵션**: 비어 있으면 인자 없음, Reset은 비우기 (v15). **DS 실행 옵션** (v14): `GWServer.exe` 인자 편집·Reset, 비어 있으면 기본값, 멀티라인은 줄바꿈만 공백으로 합침. 상세는 **v14·v15 변경 사항** 참고.
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
| GWEditor | Unreal Editor 실행 + Sync / Local Rollback (v13에서 p4_sync 통합) |


---
# 🆕 v2 변경 사항 요약 (릴리즈 노트)

GW Launcher v2에서 반영된 핵심 변경 사항입니다.

## ✅ 빌드 목록 개선
- Jenkins `builds.json`의 플랫폼 구조(`WIN`/`DS`)를 기준으로 빌드 목록을 표시합니다.
- **DS 컬럼 `O/X` (현행 v15)**:
  - 우선 **동일 Jenkins 빌드 번호**로 DS 플랫폼에 항목이 있는지 판별.
  - 없으면 **동일 파일명 + `_DS`** 규칙으로 fallback.
  - 예: `GW_v0.0.1_CL2443_Development_20251227112732.zip` → DS: `..._Development_..._DS.zip` (또는 같은 빌드 번호의 DS 항목)

## ✅ Local 실행 시 DS 자동 처리 (요구사항 2 + 3)
- **Local 실행**: DS가 존재(O)하면 **DS와 클라이언트를 함께 다운로드/압축해제**하고 실행합니다.
  - 다운로드/압축해제는 **병렬 진행**(순서 무관)
  - 실행 순서는 **DS 먼저 → 클라이언트 실행**
  - DS가 이미 실행 중이면 **기존 DS 프로세스(GWServer.exe) 종료 후 재실행**
- **Server 실행**: 클라이언트만 실행하며 DS는 다운로드/실행하지 않습니다.

### DS 실행 커맨드 (고정)
아래 커맨드로 DS가 실행됩니다. (변경 금지)
```
GWServer.exe /GWBattleRoyale/Maps/L_BR_Proto -log -port=7777
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
| v17 | 2026-03-31 | 다운로드 진행률에 용량(현재/총) 표시 — Engine·GameStarter; 총 ≥1GiB는 GB·미만은 MB·소수 2자리; `DownloadProgressFormatter.cs` 추가 |
| v16 | 2026-03-25 | 배포 서버 IIS→Nginx URL 전환 (`/installed/`, `/builds/`, `/launcher/`); `InstalledBuildServices`·`GameBuildLauncher`·`BuildListService`·`Run_GWLauncher`·`Jenkinsfile` 반영; README 배포 URL·예시 문서 갱신 |
| v15 | 2026-03-25 | GameStarter: 빌드 목록 All·Config는 클라 기준·DS는 빌드번호/실제 DS 파일명 매칭; 클라 실행 옵션 비우면 무인자·Reset/기본 -log 제거; 실행 예외 처리 보강 |
| v14 | 2026-03-23 | GameStarter: 클라이언트/DS 실행 옵션 분리·DS 기본 trace 인자; DS 옵션 멀티라인·빌드 목록 가로 스크롤 비활성화로 로그 영역 겹침 방지 |
| v13 | 2026-03-19 | 탭 통합: p4_sync → GWEditor 통합; Local CL/Build CL 표시, Sync·Local Rollback 버튼 2×2; Sync 필요 여부 메시지·아이콘 위치·구분선 보완 |
| v12 | 2026-03-17 | 런처 v12; GameStarter 캐시 경로 탐색기 바로가기 버튼; 파일 메뉴 런처 저장 경로 바로가기 추가 |
| v11 | 2026-03-04 | GWEditor 기본 옵션 -ddc=noshared 제거; GameStarter 클라이언트/DS 체크박스·unpacked 재사용·캐시 버튼 탭 이전; 상단 옵션 메뉴 제거 |
| v8 | 2026-02-19 | p4_sync 탭: 로컬/배포빌드 CL 표시, Sync 버튼 조건부 활성화 (#PJTGW-1329, commit ce5d777) |
