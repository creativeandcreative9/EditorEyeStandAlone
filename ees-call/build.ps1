$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $sdk = (& dotnet --version).Trim()
    if (-not $sdk.StartsWith('10.0.')) { throw "Expected .NET SDK 10.0.x, found $sdk" }
    if (Test-Path ./publish) { Remove-Item ./publish -Recurse -Force }
    & dotnet publish ./EesTransportClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    foreach ($name in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
        Copy-Item -LiteralPath (Join-Path (Split-Path (Get-Command dotnet).Source) $name) -Destination ./publish -Force
    }
    $hashes = [ordered]@{}
    foreach ($name in @('Program.cs', 'Properties/AssemblyInfo.cs', 'EesTransportClient.csproj', 'global.json', 'build.ps1', 'publish/ees-call.exe')) {
        $hashes[$name] = (Get-FileHash -LiteralPath $name -Algorithm SHA256).Hash
    }
    [ordered]@{version='1.2.0'; sdk=$sdk; rid='win-x64'; hashes=$hashes} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ./manifest.json -Encoding utf8
} finally { Pop-Location }
