pipeline {
  agent { label 'master' }  // ✅ master 고정

    options {
        disableConcurrentBuilds()  // 동시 실행 방지 (Binaries 충돌 방지)
        timestamps()
        buildDiscarder(logRotator(numToKeepStr: '30', daysToKeepStr: '90'))
    }

  parameters {
    string(name: 'RELEASE_NOTES', defaultValue: '', description: '릴리즈 노트(launcher.json releaseNotes에 반영)')
    booleanParam(name: 'FORCE_UPDATE', defaultValue: true, description: '강제 업데이트 여부(true면 minSupportedVersion=latestVersion)')
  }

  environment {
    // 배포 관련 (스크립트 하드코딩 X, Jenkinsfile env 고정)
    DEPLOY_ROOT       = 'D:\\Build\\Launcher'
    DOWNLOAD_BASE_URL = 'http://bravo-build.omnicraftlabs.co.kr:8000/Launcher/'
    LAUNCHER_JSON     = 'D:\\Build\\Launcher\\launcher.json'

    // Jenkins 작업 폴더 내 산출물 경로
    PUBLISH_DIR  = '_publish'
    ARTIFACT_DIR = '_artifacts'
  }

  stages {

    stage('Clean Workspace') {
      steps {
        // workspace 전체 삭제
        deleteDir()
      }
    }

    stage('Checkout') {
      steps {
        checkout([
          $class: 'GitSCM',
          branches: [[name: '*/main']],
          userRemoteConfigs: [[
            url: 'http://bravo-repo.omnicraftlabs.co.kr/bravounit/jenkins/bravogamelauncher.git',
            credentialsId: 'gitlab-jenkins'
          ]]
        ])
      }
    }

    stage('Read Version') {
      steps {
        powershell '''
          $ErrorActionPreference = "Stop"
    
          # workspace 전체에서 LauncherVersionInfo.cs를 찾아서 첫 번째 사용
          $file = Get-ChildItem -Path $env:WORKSPACE -Recurse -Filter "LauncherVersionInfo.cs" -File |
                  Select-Object -First 1
    
          if (-not $file) { throw "LauncherVersionInfo.cs not found under workspace: $env:WORKSPACE" }
    
          $text = Get-Content $file.FullName -Raw -Encoding UTF8
    
          # 예: public const int Version = 4;  또는  public static string Version = "4";
          $m = [regex]::Match($text, 'Version\\s*=\\s*"?([0-9]+)"?\\s*;')
          if (-not $m.Success) { throw "Failed to parse Version from $($file.FullName)" }
    
          $v = $m.Groups[1].Value
          Write-Host "Detected VERSION=$v from $($file.FullName)"
    
          "VERSION=$v" | Out-File -FilePath "$env:WORKSPACE\\version.env" -Encoding ascii
        '''
        script {
          def props = readProperties file: 'version.env'
          env.VERSION = props['VERSION']
          env.ZIP_NAME = "GWLauncher_v${env.VERSION}.zip"
          env.ZIP_PATH = "${env.ARTIFACT_DIR}\\${env.ZIP_NAME}"
        }
      }
    }

    stage('Publish') {
      steps {
        bat '''
          chcp 65001 >nul
          setlocal

          if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
          if exist "%ARTIFACT_DIR%" rmdir /s /q "%ARTIFACT_DIR%"
          mkdir "%PUBLISH_DIR%"
          mkdir "%ARTIFACT_DIR%"

          rem TODO: 통합 런처 csproj 경로가 다르면 아래만 수정
          set CSPROJ=BravoGameLauncherGui.csproj
          if not exist "%CSPROJ%" (
            if exist "GWLauncher\\BravoGameLauncherGui.csproj" set CSPROJ=GWLauncher\\BravoGameLauncherGui.csproj
          )
          if not exist "%CSPROJ%" (
            echo [ERROR] csproj not found. Please set CSPROJ path correctly.
            exit /b 1
          )

          echo Using CSPROJ=%CSPROJ%
          dotnet publish "%CSPROJ%" -c Release -r win-x64 ^
            -p:PublishSingleFile=true ^
            -p:IncludeNativeLibrariesForSelfExtract=true ^
            -p:DebugType=None ^
            --self-contained false ^
            -o "%PUBLISH_DIR%"

          if errorlevel 1 exit /b 1
          endlocal
        '''
      }
    }

    stage('Zip (batch tar)') {
      steps {
        bat '''
          chcp 65001 >nul
          setlocal

          echo Creating ZIP: %ZIP_PATH%
          if exist "%ZIP_PATH%" del /q "%ZIP_PATH%"

          tar -a -c -f "%ZIP_PATH%" -C "%PUBLISH_DIR%" .

          if errorlevel 1 (
            echo [ERROR] zip creation failed
            exit /b 1
          )

          dir "%ARTIFACT_DIR%"
          endlocal
        '''
      }
    }

    stage('Deploy ZIP') {
      steps {
        bat '''
          chcp 65001 >nul
          setlocal

          if not exist "%DEPLOY_ROOT%" mkdir "%DEPLOY_ROOT%"

          echo Copy ZIP to deploy: %DEPLOY_ROOT%\\%ZIP_NAME%
          copy /y "%ZIP_PATH%" "%DEPLOY_ROOT%\\%ZIP_NAME%"
          if errorlevel 1 exit /b 1

          endlocal
        '''
      }
    }

    stage('Update launcher.json') {
      steps {
        powershell '''
          $ErrorActionPreference = "Stop"

          $jsonPath = $env:LAUNCHER_JSON
          if (-not (Test-Path $jsonPath)) { throw "launcher.json not found: $jsonPath" }

          $version = [int]$env:VERSION
          $zipName = $env:ZIP_NAME
          $downloadUrl = ($env:DOWNLOAD_BASE_URL.TrimEnd('/') + '/' + $zipName)

          $releaseNotes = $env:RELEASE_NOTES
          $forceUpdate = $env:FORCE_UPDATE

          $raw = Get-Content $jsonPath -Raw -Encoding UTF8
          $obj = $raw | ConvertFrom-Json

          $obj.latestVersion = $version
          if ($forceUpdate -eq "true" -or $forceUpdate -eq $true) {
            $obj.minSupportedVersion = $version
          }
          $obj.package.fileName = $zipName
          $obj.package.downloadUrl = $downloadUrl
          $obj.releaseNotes = $releaseNotes

          $tmp = "$jsonPath.tmp"
          $bak = "$jsonPath.bak"

          Copy-Item $jsonPath $bak -Force
          ($obj | ConvertTo-Json -Depth 20) | Set-Content -Path $tmp -Encoding UTF8
          Move-Item -Path $tmp -Destination $jsonPath -Force

          "launcher.json updated:"
          "  latestVersion      = $($obj.latestVersion)"
          "  minSupportedVersion= $($obj.minSupportedVersion)"
          "  package.fileName   = $($obj.package.fileName)"
          "  package.downloadUrl= $($obj.package.downloadUrl)"
        '''
      }
    }
  }

  post {
    always {
      // ✅ zip 파일을 Jenkins 빌드 Artifact로 보관
      archiveArtifacts artifacts: '_artifacts/*.zip', fingerprint: true
    }
    success {
      echo "DONE: ${env.ZIP_NAME} deployed to ${env.DEPLOY_ROOT} and launcher.json updated."
    }
  }
}
