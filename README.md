# GW Launcher & Run_GWLauncher (Bootstrap Updater)
#readme #GWLauncher
통합 프로젝트 구조 및 동작 방식 정리 문서  
(런처 + 런처 스타터)

---

# 🆕 v2 변경 사항 요약 (릴리즈 노트)

GW Launcher v2에서 반영된 핵심 변경 사항입니다.

## ✅ 빌드 목록 개선
- Jenkins `builds.json`의 플랫폼 구조(`WIN`/`DS`)를 기준으로 빌드 목록을 표시합니다.
- 클라이언트 빌드(WIN) 항목에 대해 **동일 파일명 + `_DS`** 규칙으로 DS 존재 여부를 계산하여 **DS 컬럼에 `O/X`로 표기**합니다.
  - 예: `GW_v0.0.1_CL2443_Development_20251227112732.zip`
  - DS : `GW_v0.0.1_CL2443_Development_20251227112732_DS.zip`

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
├─ BravoGameLauncherGui/          ← GW 런처(WPF)
│     ├─ MainWindow.xaml
│     ├─ MainWindow.xaml.cs
│     ├─ GameBuildLauncher.cs
│     ├─ BuildListService.cs
│     ├─ AppSettings.cs
│     ├─ LauncherVersionInfo.cs
│     └─ ... (런처 관련 전체 스크립트)
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
- 빌드 타입 필터링(Development/Shipping)  
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

#### ✔ BuildListService.cs
- 서버 `/GameBuilds/builds.json` 다운로드  
- JSON → 빌드 리스트로 변환  
- Jenkins 빌드 번호(`jenkinsBuildNumber`) 포함

#### ✔ AppSettings.cs
- 캐시 경로 저장  
- 최근 실행한 ZIP 기록 저장

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
    "downloadUrl": "http://.../Launcher/GWLauncher_v2.zip"
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

서버 디렉터리 `/Launcher/`:

```
Launcher/
  ├─ launcher.json
  ├─ GWLauncher_v1.zip
  ├─ GWLauncher_v2.zip
  ├─ GWLauncher_v3.zip
  └─ ...
```

### launcher.json 예시

```json
{
  "latestVersion": 2,
  "minSupportedVersion": 1,
  "package": {
    "fileName": "GWLauncher_v2.zip",
    "downloadUrl": "http://server/Launcher/GWLauncher_v2.zip"
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
