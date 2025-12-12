# Bravo Game Launcher (GUI)

게임 빌드 작업자를 위한 Windows GUI 런처입니다.  
Jenkins에서 생성된 최신 게임 빌드를 **자동 다운로드 → 압축해제 → 실행**할 수 있으며,  
서버에서 제공되는 **최신 10개 빌드 목록을 리스트 형태로 확인하고 실행할 빌드를 선택**할 수 있습니다.

---

# ✨ 주요 기능

## ✔ 최신 빌드 목록 자동 로드 + UI 개선
런처 실행 시 서버에서 `builds.json`을 자동 로딩하여 최신 빌드 목록을 리스트로 표시합니다.

기존 드롭다운 방식에서 개선되어 다음과 같은 UI로 표시됩니다:

**컬럼 구성**
- **버전**
- **CL (Changelist)**
- **빌드일자**
- **빌드시간**
- 좌측 체크박스로 실행할 빌드를 선택  
  → **단일 선택만 가능**

---

## ✔ 서버 목록 수동 새로고침 지원
“서버 새로고침“ 버튼을 눌러 최신 빌드 정보를 즉시 다시 가져올 수 있습니다.

---

## ✔ 자동 다운로드 / 압축해제 / 실행
선택된 빌드 하나만 다운로드 및 실행합니다.

- 이미 다운로드된 zip 파일은 재다운로드하지 않음  
- 이미 압축해제된 빌드는 바로 exe 실행  
- exe 자동 탐색 후 실행

---

## ✔ 캐시 기능
캐싱 경로 예시는 다음과 같습니다:

```
C:\ProgramData\BravoGameBuilds\
    └── {version}\
        └── {buildName}\
            ├── build.zip
            └── unpacked\
```

---

## ✔ 캐시 경로 변경 / 전체 삭제
메뉴에서 다음 기능을 제공합니다:

- **캐시 경로 변경**
- **캐시 전체 삭제**

---

## ✔ Jenkins 연동 (자동 JSON 빌드 목록 생성)
서버의 `builds.json` 파일은 Jenkins가 자동 생성합니다.

특징:

- Jenkins 빌드 성공 시 ZIP 파일명을 env에 저장  
- post success 단계에서 JSON에 **이번 빌드만 추가**  
- JSON은 최신 10개만 유지  
- **Development / Shipping** 방식 모두 지원  

---

# 🧩 builds.json 구조

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

---

# 🖥 GUI 구성 (최신 버전)

```
┌──────────────────────────────────────────────────────────┐
│ 옵션: 캐시 경로 변경 / 캐시 전체삭제 / 종료              │
├──────────────────────────────────────────────────────────┤
│ 빌드 타입: [Development ▼] [실행] [서버 새로고침]        │
├──────────────────────────────────────────────────────────┤
│ 캐시 경로: C:\ProgramData\BravoGameBuilds                │
├──────────────────────────────────────────────────────────┤
│  ✓ | 버전   |  CL   | 빌드일자     | 빌드시간            │
│ ---+--------+-------+--------------+----------------------│
│    | 0.0.1  | 2301  | 2025-12-05   | 12:30:10             │
│    | ...                                            │
├──────────────────────────────────────────────────────────┤
│ [로그 창]                                                │
└──────────────────────────────────────────────────────────┘
```

---

# 🔧 빌드 방법 (.NET 8 기준)

## 1) 일반 빌드
```bash
dotnet build -c Release
```

## 2) 단일 EXE 생성 (배포용)
```bash
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  --self-contained false
```

실행 파일 위치:

```
BravoGameLauncherGui/bin/Release/net8.0-windows/win-x64/publish/BravoGameLauncherGui.exe
```

---

# 🔁 런처 전체 동작 흐름

### 1) 런처 실행
- 서버에서 최신 빌드 목록 자동 로딩  
- Development / Shipping 선택에 따라 필터링  
- 리스트에 빌드 항목 표시  
- 하나만 체크 가능

### 2) 실행 버튼 클릭
- 체크한 빌드만 다운로드 및 실행

### 3) Jenkins 빌드 성공 시
- ZIP 파일명 자동 추출  
- builds.json 자동 갱신  
- 런처에서 자동 반영

---

# 🧱 프로젝트 구조

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

# 🧪 테스트 체크리스트

### 런처 기능
- [ ] 서버 JSON 자동 로딩  
- [ ] Development / Shipping 필터 정상 작동  
- [ ] 체크박스는 항상 단일 선택  
- [ ] 선택한 빌드만 실행  
- [ ] 캐시 경로 변경 가능  
- [ ] 캐시 전체 삭제 가능  
- [ ] 로그 정상 출력  

### Jenkins 기능
- [ ] JSON 생성 성공  
- [ ] 최신 10개 유지  
- [ ] ZIP 빌드명 정확히 반영  
- [ ] 웹서버에서 builds.json 정상 서빙  

---

# 📄 라이선스

내부 전용 – 외부 배포 금지
