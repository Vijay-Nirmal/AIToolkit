[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$InputPath,

    [string]$OutputFolder,

    [int]$Width = 0,

    [int]$Height = 0,

    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Release-ComObject {
    param(
        [Parameter(ValueFromPipeline)]
        $ComObject
    )

    if ($null -ne $ComObject -and [System.Runtime.InteropServices.Marshal]::IsComObject($ComObject)) {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($ComObject)
    }
}

$resolvedInputPath = (Resolve-Path -LiteralPath $InputPath).Path

if ([System.IO.Path]::GetExtension($resolvedInputPath) -notin @('.pptx', '.pptm', '.potx', '.potm')) {
    throw "Input file must be a PowerPoint presentation or template (.pptx, .pptm, .potx, .potm)."
}

if (($Width -gt 0 -and $Height -le 0) -or ($Height -gt 0 -and $Width -le 0)) {
    throw "Width and Height must both be greater than zero when either value is provided."
}

if ([string]::IsNullOrWhiteSpace($OutputFolder)) {
    $inputDirectory = Split-Path -Path $resolvedInputPath -Parent
    $inputName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedInputPath)
    $OutputFolder = Join-Path $inputDirectory "$inputName-png"
}

if (Test-Path -LiteralPath $OutputFolder) {
    if (-not $Force) {
        throw "Output folder already exists: $OutputFolder. Use -Force to overwrite existing PNG files."
    }
}
else {
    [void](New-Item -ItemType Directory -Path $OutputFolder -Force)
}

$powerPoint = $null
$presentation = $null
$slides = @()

try {
    try {
        $powerPoint = New-Object -ComObject PowerPoint.Application
    }
    catch {
        throw "Microsoft PowerPoint must be installed on this Windows machine to export slides as PNG."
    }

    $presentation = $powerPoint.Presentations.Open($resolvedInputPath, $false, $true, $false)

    foreach ($slide in $presentation.Slides) {
        $slides += $slide
    }

    foreach ($slide in $slides) {
        $fileName = "Slide{0:D3}.png" -f [int]$slide.SlideIndex
        $targetPath = Join-Path $OutputFolder $fileName

        if ((Test-Path -LiteralPath $targetPath) -and -not $Force) {
            throw "Output file already exists: $targetPath. Use -Force to overwrite existing PNG files."
        }

        if ($Width -gt 0 -and $Height -gt 0) {
            $slide.Export($targetPath, 'PNG', $Width, $Height)
        }
        else {
            $slide.Export($targetPath, 'PNG')
        }
    }

    Write-Host "Exported $($slides.Count) slide(s) to $OutputFolder"
}
finally {
    foreach ($slide in $slides) {
        $slide | Release-ComObject
    }

    if ($null -ne $presentation) {
        $presentation.Close()
        $presentation | Release-ComObject
    }

    if ($null -ne $powerPoint) {
        $powerPoint.Quit()
        $powerPoint | Release-ComObject
    }

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}