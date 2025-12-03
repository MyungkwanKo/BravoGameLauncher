# Bravo Game Launcher (GUI)

게임 빌드 작업자를 위한 Windows GUI 런처입니다.  
Jenkins에서 생성된 최신 게임 빌드를 자동으로 다운로드 → 압축해제 → 실행하도록 도와주며,  
서버에서 제공되는 최신 10개 빌드 목록을 자동으로 불러와 사용할 수 있습니다.

---

## ✨ 주요 기능

### ✔ 최신 빌드 목록 자동 로드
런처 실행 시 서버에서 `builds.json`을 자동으로 다운로드하여  
**최신 생성된 빌드 목록을 드롭다운에서 바로 선택할 수 있습니다.**

- 서버 URL  
  ```
  http://bravo-build.omnicraftlabs.co.kr:8000/GameBuilds/builds.json
  ```
- Jenkins가 유지하는 빌드 정보는 **최대 10개(최신순)**만 유지됩니다.

---

### ✔ 서버 목록 수동 새로고침
자동 로드 실패 시 **“서버 목록 새로고침”** 버튼으로 즉시 갱신할 수 있습니다.

---

### ✔ 자동 다운로드 / 압축 해제 / 실행
게임 실행을 위한 수동 작업을 모두 자동화했습니다.

- 기존 다운로드 파일이 있으면 재다운로드 하지 않음  
- 압축 폴더가 있으면 재압축하지 않음  
- EXE 파일을 자동 탐색 후 즉시 실행

---

### ✔ 캐시 기능
게임 빌드 캐시는 다음 위치에 저장됩니다:

```
C:\ProgramData\BravoGameBuilds\
    └── {version}\
        └── {buildName}\
            ├── build.zip
            └── unpacked\
```

---

### ✔ 캐시 경로 변경 / 전체 삭제
메뉴에서 다음 기능을 지원합니다:

- **캐시 경로 변경** → 다른 드라이브로 이동 가능  
- **캐시 전체 삭제** → 다운로드 및 압축해제 기록 전체 제거

---

### ✔ Jenkins 자동 JSON 갱신 연동
프로젝트는 Jenkins 빌드 결과를 기반으로  
**전역 JSON 파일(`builds.json`)을 자동으로 갱신**하도록 구성되어 있습니다.

특징:

- Jenkins 빌드 성공 시에만 JSON 갱신  
- 이번 빌드에서 생성된 ZIP 파일만 JSON에 반영  
- JSON에는 최신 10개의 빌드 정보만 유지  
- `sizeBytes` 등 불필요한 정보는 저장하지 않음  

---

## 🧩 builds.json 구조

Jenkins는 다음 구조의 JSON을 유지합니다:

```json
{
  "project": "GW",
  "builds": [
    {
      "fileName": "GW_v0.0.1_CL2301_Shipping_20251205123010.zip",
      "version": "0.0.1",
      "cl": 2301,
      "config": "Shipping",
      "platform": "WIN",
      "buildTime": "2025-12-05T12:30:10",
      "jenkinsBuildNumber": 57
    }
  ]
}
```

- JSON은 항상 **최신순**으로 정렬됩니다.  
- 각 항목은 **하나의 Jenkins 빌드 결과 ZIP 파일**을 의미합니다.

---

## 🖥 GUI 구성

```
┌─────────────────────────────────────────────────────────┐
│ [옵션] 캐시 경로 변경 / 캐시 전체 삭제 / 종료          │
├─────────────────────────────────────────────────────────┤
│ 빌드 ZIP 파일명: [콤보박스 ▼] [실행] [서버 목록 새로고침] │
│ 캐시 경로: C:\ProgramData\BravoGameBuilds               │
├─────────────────────────────────────────────────────────┤
│ [로그 창]                                               │
│ ...                                                     │
└─────────────────────────────────────────────────────────┘
```

---

## 🔧 빌드 방법 (.NET 8 기준)

### 1) 일반 빌드 (Develop/Test 용)

```bash
dotnet build -c Release
```

### 2) 단일 EXE 생성 (배포용)

```bash
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  --self-contained false
```

> ⚠ `dotnet build`는 생략 가능  
> 👉 `dotnet publish`는 build 포함이므로 마지막 publish만 실행해도 완성본이 생성됨

### 실행 파일 위치

```
BravoGameLauncherGui/bin/Release/net8.0-windows/win-x64/publish/BravoGameLauncherGui.exe
```

---

## 🔁 런처 동작 흐름

### 1) 런처 실행
- 자동으로 `builds.json` 다운로드  
- 콤보박스에 최신 10개 빌드 표시  
- 실패 시 수동 새로고침 가능

### 2) 실행 버튼 클릭
- ZIP 미존재 → 다운로드  
- 압축 미해제 → 자동 압축해제  
- EXE 자동 탐색 후 실행

### 3) Jenkins 빌드 성공 시
- 조직된 zip 파일명(env 변수 저장)  
- post success 단계에서 `builds.json` 갱신  
- 최신 10개 유지  
- 런처 실행 시 최신 목록 반영  

---

## 🧱 Jenkins 빌드 연동 구조

### organizeArtifact (WIN)
- ZIP 파일 생성 후  
  → `${buildName}.zip` 을 `env.WIN_ZIP_FILES`에 누적

예)
```
GW_v0.0.1_CL2301_Shipping_20251205123010.zip
```

### post { success } 단계
- `$env:WIN_ZIP_FILES` 읽기  
- 파일명 기반으로 JSON에만 추가  
- 기존 10개 초과 시 오래된 항목 삭제  
- JSON 파일 저장 (`D:\Build\GameBuilds\builds.json`)

---

## 📁 프로젝트 구조

```
📁 BravoGameLauncherGui
 ├── MainWindow.xaml
 ├── MainWindow.xaml.cs
 ├── GameBuildLauncher.cs
 ├── BuildListService.cs
 ├── AppSettings.cs
 ├── BravoGameLauncherGui.csproj
 └── README.md
```

---

## 🧪 테스트 체크리스트

### 런처
- [ ] 런처 실행 시 자동으로 목록 로딩  
- [ ] 서버 목록 새로고침 정상 작동  
- [ ] 선택 후 다운로드/압축/실행 정상  
- [ ] 캐시 경로 변경 기능 정상  
- [ ] 캐시 전체 삭제 기능 정상  

### Jenkins
- [ ] JSON 업데이트 로그 출력 확인  
- [ ] JSON에 “이번 빌드에서 만든 ZIP만” 들어가는지 확인  
- [ ] JSON에 최대 10개만 존재하는지 확인  
- [ ] 웹서버에서 builds.json 정상 서빙  

---

## 📄 라이선스

내부 전용 – 외부 배포 금지
