[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $IdentityName,

    [Parameter(Mandatory)]
    [ValidatePattern('^CN=')]
    [string] $Publisher,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $PublisherDisplayName,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\store-packages')
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\AntennaGuardian.App\AntennaGuardian.App.csproj'
$shieldPath = Join-Path $repoRoot 'docs\images\antennaguardian-shield.png'
$stagingRoot = Join-Path $repoRoot 'build\store'
$payloadPath = Join-Path $stagingRoot 'payload'
$assetsPath = Join-Path $payloadPath 'Assets'
$manifestPath = Join-Path $payloadPath 'AppxManifest.xml'
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

[xml] $project = Get-Content -LiteralPath $projectPath
$version = ([string]$project.Project.PropertyGroup.Version).Trim()
if ($version -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$') {
    throw "Project Version '$version' must use major.minor.patch format."
}
$packageMajor = [int]$Matches.major + 1
$packageMinor = [int]$Matches.minor
$packagePatch = [int]$Matches.patch
if ($packageMajor -gt 65535 -or $packageMinor -gt 65535 -or $packagePatch -gt 65535) {
    throw "Project Version '$version' cannot be represented as an MSIX version."
}
$packageVersion = "$packageMajor.$packageMinor.$packagePatch.0"

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $assetsPath -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $payloadPath
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $payloadPath
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $payloadPath

Add-Type -AssemblyName System.Drawing
function Export-SquareAsset([string] $Destination, [int] $Size) {
    $source = [System.Drawing.Image]::FromFile($shieldPath)
    try {
        $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source, 0, 0, $Size, $Size)
            }
            finally {
                $graphics.Dispose()
            }
            $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

function Export-WideAsset([string] $Destination) {
    $source = [System.Drawing.Image]::FromFile($shieldPath)
    try {
        $bitmap = [System.Drawing.Bitmap]::new(310, 150)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::FromArgb(18, 21, 27))
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source, 80, 0, 150, 150)
            }
            finally {
                $graphics.Dispose()
            }
            $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

Export-SquareAsset (Join-Path $assetsPath 'Square44x44Logo.png') 44
Export-SquareAsset (Join-Path $assetsPath 'Square150x150Logo.png') 150
Export-SquareAsset (Join-Path $assetsPath 'StoreLogo.png') 50
Export-WideAsset (Join-Path $assetsPath 'Wide310x150Logo.png')

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create($manifestPath, $settings)
try {
    $foundation = 'http://schemas.microsoft.com/appx/manifest/foundation/windows10'
    $uap = 'http://schemas.microsoft.com/appx/manifest/uap/windows10'
    $rescap = 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities'
    $writer.WriteStartDocument()
    $writer.WriteStartElement('Package', $foundation)
    $writer.WriteAttributeString('xmlns', 'uap', $null, $uap)
    $writer.WriteAttributeString('xmlns', 'rescap', $null, $rescap)
    $writer.WriteAttributeString('IgnorableNamespaces', 'uap rescap')

    $writer.WriteStartElement('Identity', $foundation)
    $writer.WriteAttributeString('Name', $IdentityName)
    $writer.WriteAttributeString('Publisher', $Publisher)
    $writer.WriteAttributeString('Version', $packageVersion)
    $writer.WriteAttributeString('ProcessorArchitecture', 'x64')
    $writer.WriteEndElement()

    $writer.WriteStartElement('Properties', $foundation)
    $writer.WriteElementString('DisplayName', $foundation, 'AntennaGuardian')
    $writer.WriteElementString('PublisherDisplayName', $foundation, $PublisherDisplayName)
    $writer.WriteElementString('Description', $foundation, 'A Flex radio antenna-by-band transmit interlock.')
    $writer.WriteElementString('Logo', $foundation, 'Assets\StoreLogo.png')
    $writer.WriteEndElement()

    $writer.WriteStartElement('Resources', $foundation)
    $writer.WriteStartElement('Resource', $foundation)
    $writer.WriteAttributeString('Language', 'en-us')
    $writer.WriteEndElement()
    $writer.WriteEndElement()

    $writer.WriteStartElement('Dependencies', $foundation)
    $writer.WriteStartElement('TargetDeviceFamily', $foundation)
    $writer.WriteAttributeString('Name', 'Windows.Desktop')
    $writer.WriteAttributeString('MinVersion', '10.0.19041.0')
    $writer.WriteAttributeString('MaxVersionTested', '10.0.26100.0')
    $writer.WriteEndElement()
    $writer.WriteEndElement()

    $writer.WriteStartElement('Applications', $foundation)
    $writer.WriteStartElement('Application', $foundation)
    $writer.WriteAttributeString('Id', 'App')
    $writer.WriteAttributeString('Executable', 'AntennaGuardian.exe')
    $writer.WriteAttributeString('EntryPoint', 'Windows.FullTrustApplication')
    $writer.WriteStartElement('VisualElements', $uap)
    $writer.WriteAttributeString('DisplayName', 'AntennaGuardian')
    $writer.WriteAttributeString('Description', 'A Flex radio antenna-by-band transmit interlock.')
    $writer.WriteAttributeString('BackgroundColor', 'transparent')
    $writer.WriteAttributeString('Square150x150Logo', 'Assets\Square150x150Logo.png')
    $writer.WriteAttributeString('Square44x44Logo', 'Assets\Square44x44Logo.png')
    $writer.WriteStartElement('DefaultTile', $uap)
    $writer.WriteAttributeString('Wide310x150Logo', 'Assets\Wide310x150Logo.png')
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndElement()

    $writer.WriteStartElement('Capabilities', $foundation)
    foreach ($capability in @('internetClient', 'privateNetworkClientServer')) {
        $writer.WriteStartElement('Capability', $foundation)
        $writer.WriteAttributeString('Name', $capability)
        $writer.WriteEndElement()
    }
    $writer.WriteStartElement('Capability', $rescap)
    $writer.WriteAttributeString('Name', 'runFullTrust')
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndDocument()
}
finally {
    $writer.Dispose()
}

$makeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
    -Filter makeappx.exe -Recurse | Where-Object FullName -Match '\\x64\\makeappx\.exe$' |
    Sort-Object FullName -Descending | Select-Object -First 1
if ($null -eq $makeAppx) {
    throw 'MakeAppx.exe was not found. Install the Windows SDK.'
}

$packagePath = Join-Path $OutputDirectory "AntennaGuardian-$version-win-x64.msix"
$makeOutput = & $makeAppx.FullName pack /d $payloadPath /p $packagePath /o 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed.`n$($makeOutput -join [Environment]::NewLine)"
}

Write-Output $packagePath
