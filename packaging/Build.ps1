$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'dist/app'
$project = Join-Path $root 'src/ImageChecker.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$packages = (dotnet nuget locals global-packages --list) -replace '^.*?:\s*', ''
$packageVersion = ([xml](Get-Content -Raw $project)).Project.ItemGroup.PackageReference.Version
Copy-Item -LiteralPath (Join-Path $packages "magick.net-q8-x64/$packageVersion/Notice.txt") -Destination (Join-Path $app 'Magick-NOTICE.txt')
$deps = Get-Content -Raw (Join-Path $root 'src/bin/Release/net10.0-windows/win-x64/ImageChecker.deps.json') | ConvertFrom-Json
$libraries = $deps.libraries.PSObject.Properties.Name
$core = ($libraries -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*') -replace '^runtimepack\.', ''
$desktop = ($libraries -like 'runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/*') -replace '^runtimepack\.', ''
foreach ($name in 'LICENSE.TXT','THIRD-PARTY-NOTICES.TXT') {
    Copy-Item -LiteralPath (Join-Path $packages "$core/$name") -Destination (Join-Path $app $name)
}
Copy-Item -LiteralPath (Join-Path $packages "$desktop/LICENSE") -Destination (Join-Path $app 'WindowsDesktop-LICENSE.txt')
Copy-Item -Path (Join-Path $PSScriptRoot '*.cmd') -Destination $app
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $app
$files = @('ImageChecker.exe','Enable Explorer Menu.cmd','Remove Explorer Menu.cmd','README.md','Magick-NOTICE.txt','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT','WindowsDesktop-LICENSE.txt') | ForEach-Object { Join-Path $app $_ }
Compress-Archive -LiteralPath $files -DestinationPath (Join-Path $root 'dist/ImageChecker-win-x64.zip') -Force
