param(
    [string] $Repo = "L34T/EpubSharp_Elib2Ebook",
    [string] $Tag = "",
    [string] $OutDir = "Core/External",
    [switch] $Force
)

$ErrorActionPreference = "Stop"

function Get-LatestRelease {
    param([string] $repo)

    $headers = @{
        "Accept" = "application/vnd.github+json"
        "User-Agent" = "Elib2Ebook-fetch-epubsharp"
    }

    return Invoke-RestMethod -Headers $headers -Uri ("https://api.github.com/repos/{0}/releases/latest" -f $repo)
}

function Get-ReleaseByTag {
    param([string] $repo, [string] $tag)

    $headers = @{
        "Accept" = "application/vnd.github+json"
        "User-Agent" = "Elib2Ebook-fetch-epubsharp"
    }

    return Invoke-RestMethod -Headers $headers -Uri ("https://api.github.com/repos/{0}/releases/tags/{1}" -f $repo, $tag)
}

function Ensure-Dir {
    param([string] $path)
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
    }
}

function Download-Asset {
    param(
        [string] $url,
        [string] $path,
        [switch] $force
    )

    if (-not $force -and (Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Length -gt 0)) {
        Write-Host ("Skip (exists): {0}" -f $path)
        return
    }

    Write-Host ("Download: {0}" -f $path)
    Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing | Out-Null
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $release = Get-LatestRelease -repo $Repo
}
else {
    $release = Get-ReleaseByTag -repo $Repo -tag $Tag
}

if ($null -eq $release -or [string]::IsNullOrWhiteSpace($release.tag_name)) {
    throw "Unable to resolve release for repo '$Repo' tag '$Tag'."
}

$resolvedTag = $release.tag_name
Write-Host ("Using release tag: {0}" -f $resolvedTag)

Ensure-Dir -path $OutDir

$required = @{
    "EpubSharp-net10.dll" = "EpubSharp.dll"
    "EpubSharp-net10.pdb" = "EpubSharp.pdb"
    "EpubSharp-net10.deps.json" = "EpubSharp.deps.json"
}

foreach ($item in $required.GetEnumerator()) {
    $assetName = $item.Key
    $destName = $item.Value
    $asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if ($null -eq $asset -or [string]::IsNullOrWhiteSpace($asset.browser_download_url)) {
        throw "Missing asset '$assetName' in release '$resolvedTag' for repo '$Repo'."
    }

    $dest = Join-Path $OutDir $destName
    Download-Asset -url $asset.browser_download_url -path $dest -force:$Force
}

Write-Host "Done."

