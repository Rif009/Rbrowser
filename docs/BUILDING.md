# Building and signing

The checked environment uses .NET SDK 10.0.403, MAUI 10.0.110, JDK 21, Android SDK 36, build-tools 36.0.0, PowerShell 7 and Node.js 24+. The Android application targets API 36 and supports API 26+.

Install the MAUI Android workload and configure `JAVA_HOME` and `ANDROID_HOME` for your machine. All project paths are relative; no machine-specific configuration is included.

```powershell
dotnet workload install maui-android
./tools/Publish.ps1
```

`Publish.ps1` runs checks, regenerates `compiled-v2.bin` from the included text filters, generates the TV banner and publishes an ARM32 + ARM64 APK. Build the Android project rather than trying to deploy the platform-independent Core project.

For a single architecture:

```powershell
dotnet publish Rbrowser/Rbrowser.csproj -c Release -r android-arm64 -p:RunAOTCompilation=false -p:EmbedAssembliesIntoApk=true
```

Use `android-arm` for an ARM32 TV. `android-x64` is for a suitable emulator, with `-p:RuntimeIdentifiers=android-x64` if needed.

## Release signing

The included APK was built with a local Android debug signing key for device testing. The private key is **not** distributed. GitHub Actions also uses an ephemeral debug key. Those builds do not provide a stable release-signing identity.

For a maintained release, create and keep your own signing key outside the repository. Back it up privately. Use `AndroidKeyStore=true`, `AndroidSigningKeyStore`, `AndroidSigningKeyAlias`, `AndroidSigningStorePass` and `AndroidSigningKeyPass` when publishing. Supply passwords through secure environment-backed MSBuild values or private CI configuration, not committed command files. Microsoft documents the supported signing properties at https://learn.microsoft.com/dotnet/maui/android/deployment/publish-cli .

The application ID is `dev.rbrowser.tv`. Updates require the same signing identity and an appropriate version code. If you switch from the testing key to a new release key, Android may require uninstalling the old package, which deletes its private local data.

## Optional local WebView diagnostics

```powershell
dotnet publish Rbrowser/Rbrowser.csproj -c Release -p:TvDiagnostics=true -p:RunAOTCompilation=false -p:EmbedAssembliesIntoApk=true
```

Only use this for a local device you control. `tools/cdp.cjs` expects an explicitly created ADB forward to `127.0.0.1:9222`. Do not distribute that diagnostic build. Default Release builds omit this capability and the synthetic history preview.
