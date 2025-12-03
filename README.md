📘 Bravo Game Launcher – README

Bravo Game Launcher는 Jenkins에서 생성된 게임 빌드를 자동으로 다운로드/압축해제/실행하도록 돕는 Windows용 GUI 런처입니다.
빌드 작업자가 ZIP 파일을 직접 찾아서 다운로드하거나 압축을 해제할 필요 없으며, 최신 빌드 목록도 자동으로 서버에서 받아옵니다.

✨ 주요 기능
✔ 1) 최신 빌드 목록 자동 로드

런처 실행 시 서버에서 builds.json을 자동으로 다운로드하여 최신 생성된 빌드 목록을 드롭다운으로 표시합니다.

서버 경로

http://bravo-build.omnicraftlabs.co.kr:8000/GameBuilds/builds.json


JSON에는 Jenkins에서 빌드 성공한 최근 10개 빌드만 유지됩니다.

✔ 2) 서버 목록 수동 새로고침

상단의 “서버 목록 새로고침” 버튼을 누르거나
자동 로드가 실패한 경우, 手動으로 목록을 갱신할 수 있습니다.

✔ 3) 자동 다운로드 & 압축 해제 & 실행

Zip 파일명을 직접 입력할 필요 없이 선택만 하면 됩니다.

기존에 다운로드 받은 파일이 있으면 재다운로드 하지 않음

압축해제가 되어 있으면 다시 해제하지 않음

실행 버튼을 누르면 자동으로 exe를 찾아 실행

✔ 4) 캐시 시스템

런처는 다운로드/압축해제 파일을 다음 경로에 저장합니다:

C:\ProgramData\BravoGameBuilds\
     └── 버전\
         └── buildName\
             ├── build.zip
             └── unpacked\ (압축해제)

✔ 5) 캐시 경로 설정/삭제 기능

“옵션 → 캐시 경로 변경” : 다른 드라이브나 경로로 저장소 이동 가능

“옵션 → 캐시 전체 삭제” : 다운로드 및 압축해제된 파일 전체 삭제

✔ 6) 최근 입력 이력 (Local Cache)

드롭다운에서 최근 입력한 ZIP 파일명 10개까지 자동 저장
(단, UI에는 서버 JSON 목록만 표시함 – 히스토리는 내부 저장용)

✔ 7) Jenkins 자동 JSON 갱신

Jenkins 빌드 성공 시 다음 동작 수행:

이번 빌드에서 생성된 ZIP 파일명을 Jenkins ENV 변수에 저장

빌드 성공 후 PowerShell에서 JSON 구조 갱신

최신 10개만 유지하도록 trimming

builds.json 자동 업데이트

🧱 Jenkins – builds.json 자동 업데이트 구조
빌드 성공 시 저장되는 JSON 구조
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
    // ... 최대 10개
  ]
}


Jenkins가 자동으로 최근 빌드 10개만 유지

런처는 이 JSON만 사용해 최신 빌드 리스트 자동 구성

🖥 GUI 화면 설명
┌─────────────────────────────────────────────────────────┐
│ [옵션] 캐시 경로 변경 / 캐시 전체 삭제 / 종료          │
├─────────────────────────────────────────────────────────┤
│ 빌드 ZIP 파일명: [콤보박스 ▼] [실행] [서버 목록 새로고침] │
│ 캐시 경로:  C:\ProgramData\BravoGameBuilds              │
├─────────────────────────────────────────────────────────┤
│ [로그 출력창]                                           │
│ ...                                                     │
└─────────────────────────────────────────────────────────┘

🔧 설치 / 실행 방법
1) 런처 실행파일 위치

프로젝트를 빌드하여 생성되는 최종 실행파일은:

BravoGameLauncherGui\bin\Release\net8.0-windows\win-x64\publish\
    BravoGameLauncherGui.exe

2) 빌드 방법 (.NET 8.x)
✔ 일반 빌드 (테스트 목적)
dotnet build -c Release

✔ 단일 EXE로 배포 (실제 배포에 사용)
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  --self-contained false


⚠ dotnet build는 실제 배포에는 필요 없음
👉 publish 한 번이면 build 포함됨

🧩 프로젝트 구조
📁 BravoGameLauncherGui
 ├── MainWindow.xaml
 ├── MainWindow.xaml.cs
 ├── GameBuildLauncher.cs
 ├── AppSettings.cs
 ├── BuildListService.cs
 ├── BravoGameLauncherGui.csproj
 └── README.md   ← (현재 문서)

🔁 런처 전체 동작 흐름
① 런처 실행

→ Loaded 이벤트
→ 자동으로 서버 JSON (builds.json) 다운로드
→ 콤보박스에 최신목록 표시

② 사용자 선택 후 "실행" 클릭

→ buildName.zip 다운로드 여부 확인
→ 압축해제 여부 확인
→ exe 자동 탐색
→ 실행

③ Jenkins 빌드 성공

→ organizeArtifact()에서 buildName 저장
→ post { success } 에서 JSON 갱신
→ 런처 다음 실행 때 최신 목록 반영

🧪 테스트 체크리스트
런처 측

 런처 실행 시 서버 JSON 자동 로드

 콤보박스 최신순 목록 정상 표시

 선택 후 실행 → 정상 다운로드/압축/실행

 캐시 경로 변경 기능 정상 동작

 캐시 전체 삭제 정상 동작

 서버 목록 새로고침 정상 반영

Jenkins 측

 BUILDS.json 업데이트 테스트

 실패 빌드에서는 JSON 갱신되지 않음

 ZIP 파일명 여러 개 생성 시 모두 JSON에 반영

 JSON 항목 10개 유지

 웹서버에서 최신 JSON 서빙 확인

📄 라이선스

사내 전용. 외부 배포 금지.