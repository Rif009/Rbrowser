$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
 ./tools/Build-Banner.ps1
 dotnet run --project Rbrowser.Checks/Rbrowser.Checks.csproj -c Release -- --compile
 if ($LASTEXITCODE -ne 0) { throw 'Core checks failed' }
 node tests/navigation.test.cjs
 if ($LASTEXITCODE -ne 0) { throw 'Navigation checks failed' }
 node tests/twitch-adblock.test.cjs
 if ($LASTEXITCODE -ne 0) { throw 'Twitch HLS checks failed' }
 node tests/media-detector.test.cjs
 if ($LASTEXITCODE -ne 0) { throw 'Media detector checks failed' }
 node tests/localization.test.cjs
 if ($LASTEXITCODE -ne 0) { throw 'Localization checks failed' }
 dotnet publish Rbrowser/Rbrowser.csproj -c Release -p:TvDiagnostics=false -p:RunAOTCompilation=false -p:EmbedAssembliesIntoApk=true
 if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
 New-Item -ItemType Directory -Force -Path artifacts | Out-Null
 Copy-Item -LiteralPath 'Rbrowser/bin/Release/net10.0-android/publish/dev.rbrowser.tv-Signed.apk' -Destination 'artifacts/Rbrowser-0.8.2-tv.apk'
 Get-FileHash -LiteralPath 'artifacts/Rbrowser-0.8.2-tv.apk' -Algorithm SHA256 | Format-List
} finally { Pop-Location }

