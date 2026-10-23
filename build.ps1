param([switch]$RebuildNative,[switch]$Diagnostics)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$taskFramework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$taskVswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$taskCompiler=$null
if(Test-Path -LiteralPath $taskVswhere){$taskVs=& $taskVswhere -latest -products '*' -property installationPath;$taskCompiler=Join-Path $taskVs 'MSBuild/Current/Bin/Roslyn/csc.exe'}
if(-not $taskCompiler -or -not(Test-Path -LiteralPath $taskCompiler)){throw 'Visual Studio C# compiler is required. Install Visual Studio with .NET desktop development.'}
$taskOutput=Join-Path $PSScriptRoot $(if($Diagnostics){'dist/Diagnostics'}else{'dist/MackieTranslator'})
New-Item -ItemType Directory -Force $taskOutput | Out-Null
$taskNative=Join-Path $PSScriptRoot 'native/lib/mackie_native.dll'
if($RebuildNative){$env:CARGO_ENCODED_RUSTFLAGS='--remap-path-prefix='+$env:USERPROFILE+'=build-user';cargo test --manifest-path native/Cargo.toml;if($LASTEXITCODE -ne 0){throw 'Rust tests failed'};cargo build --release --manifest-path native/Cargo.toml;if($LASTEXITCODE -ne 0){throw 'Rust build failed'};$taskNative=Join-Path $PSScriptRoot 'native/target/release/mackie_native.dll'}
if(-not(Test-Path -LiteralPath $taskNative)){throw 'Native DLL missing. Use -RebuildNative with Rust installed.'}
$taskArgs=@('/nologo','/target:winexe','/platform:x64','/optimize+','/debug-','/langversion:latest',"/out:$taskOutput/MackieTranslator.exe",'/win32icon:assets/app_icon.ico','/win32manifest:app.manifest','/resource:src/Styles.xaml,MackieTranslator.Styles.xaml','/resource:assets/app_icon.ico,MackieTranslator.app_icon.ico')
foreach($taskDll in @('System.dll','System.Core.dll','System.Security.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.dll','System.Runtime.WindowsRuntime.dll','System.Runtime.InteropServices.WindowsRuntime.dll')){$taskArgs+='/reference:'+(Join-Path $taskFramework $taskDll)}
foreach($taskDll in @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll')){$taskLocation=Join-Path $taskFramework ('WPF/'+$taskDll);if(-not(Test-Path -LiteralPath $taskLocation)){$taskLocation=Join-Path $taskFramework $taskDll};$taskArgs+='/reference:'+$taskLocation}
$taskSdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/UnionMetadata'
$taskWindowsMetadata=Get-ChildItem -LiteralPath $taskSdkRoot -Recurse -Filter Windows.winmd | Where-Object {$_.Directory.Name -ne 'Facade'} | Sort-Object FullName -Descending | Select-Object -First 1
if(-not $taskWindowsMetadata){throw 'Windows 10 SDK metadata is required for the local OCR build.'}
$taskArgs+='/reference:'+$taskWindowsMetadata.FullName
if($Diagnostics){$taskArgs+='/define:DIAGNOSTICS'}
$taskArgs+=@(Get-ChildItem src -Filter '*.cs' | Where-Object {$Diagnostics -or $_.Name -notlike '*Tests.cs'} | ForEach-Object {$_.FullName})
& $taskCompiler @taskArgs
if($LASTEXITCODE -ne 0){throw 'C# build failed'}
Copy-Item -LiteralPath $taskNative -Destination $taskOutput -Force
Copy-Item -LiteralPath MackieTranslator.exe.config,LICENSE -Destination $taskOutput -Force
if(Test-Path -LiteralPath THIRD-PARTY-NOTICES.txt){Copy-Item -LiteralPath THIRD-PARTY-NOTICES.txt -Destination $taskOutput -Force}
$taskRuntimeRoot=Join-Path $taskVs 'VC/Redist/MSVC'
if(Test-Path -LiteralPath $taskRuntimeRoot){$taskRuntime=Get-ChildItem -LiteralPath $taskRuntimeRoot -Recurse -Filter vcruntime140.dll | Where-Object {$_.FullName -match 'x64.*Microsoft.VC' -and $_.FullName -notmatch 'onecore'} | Sort-Object FullName -Descending | Select-Object -First 1;if($taskRuntime){Copy-Item -LiteralPath $taskRuntime.FullName -Destination $taskOutput -Force}}
if(Test-Path -LiteralPath native/licenses){Copy-Item -LiteralPath native/licenses -Destination $taskOutput -Recurse -Force}
Write-Host "Built: $taskOutput/MackieTranslator.exe"


