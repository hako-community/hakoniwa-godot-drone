<#
.SYNOPSIS
  native.lock.json に書いた版のネイティブ一式（hakodrone.dll など）を releases-channel から取ってきて置く。

.DESCRIPTION
  DLL 配布 P4（related/devai_dll/hakodrone_dll_distribution_20260927.md §5.2）。
  ビルドの前に自動で呼ばれる（Godot は .csproj の FetchNative、Unreal は build.ps1）。手で呼んでもよい。

    1. 置き場の目印（<dest>/.native.lock.<名前>.json）がロックと同じで、ファイルが揃っていれば何もしない
       （毎回のビルドを遅くしない・一度取ってあればネットワークが無くても通る）
    2. gh release download <tag> -R <repo> -p <asset>（非公開なので gh auth login 済みが前提）
    3. zip の sha256 をロックと照合。違えば止める（取り違え・改ざん・途中で切れた）
    4. 展開して置く（既定は dest。place にファイルごとの置き場を書ける）。目印を書く

  ロック（リポジトリのルートの native.lock.json）の形:
    { "<名前>": { "repo": "...", "tag": "...", "version": "1.0.0", "asset": "...zip", "sha256": "...",
                  "dest": "Plugins/Windows/x86_64", "place": { "<ファイル名>": "<置き場>" } } }
  "_" で始まる名前は注記として読み飛ばす。
  place には「フォルダ/」も書ける（例 "models/hula/": "SimModels/edu/hula"）。zip の中のそのフォルダの下を指定の場所へ置く。
  どの place にも当たらない zip の中のフォルダは置かない（要る機体だけを取る）。

  ★ 公開の前に手元の zip で試すときは HAKO_FETCH_NATIVE_ZIP=<zip のパス>（取りに行かず、sha256 の照合はする）。

  ★ Windows PowerShell 5.1 でも動く書き方にしてある（.csproj から powershell で呼ぶため）。
    ファイルは BOM つき UTF-8 で保存すること（5.1 は BOM が無いと日本語を読み違える）。
  ★ 取得を飛ばしたいとき（認証の無い CI など）は HAKO_SKIP_FETCH_NATIVE=1。
  ★ このファイルは related の各リポジトリで同じ内容を保つ（リポジトリ固有のことはロックに書く）。
#>
[CmdletBinding()]
param(
    # リポジトリのルート（既定: このスクリプトの場所から git で探す）
    [string]$Root = '',
    # 目印が合っていても取り直す
    [switch]$Force
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Say([string]$m) { Write-Host "[fetch_native] $m" }
# ★ sha256 と展開は .NET で行う。powershell.exe（5.1）を PowerShell 7 から起動すると PSModulePath を引き継ぎ、
#   Get-FileHash / Expand-Archive が見つからないことがある（2026-09-29 に dotnet build 経由で実際に踏んだ）。
function Get-Sha256([string]$path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $fs = [System.IO.File]::OpenRead($path)
    try { return (($sha.ComputeHash($fs) | ForEach-Object { $_.ToString('x2') }) -join '') }
    finally { $fs.Dispose(); $sha.Dispose() }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Stop-Fetch([string]$m) {
    Write-Host "[fetch_native] NG: $m" -ForegroundColor Red
    exit 1
}

if ($env:HAKO_SKIP_FETCH_NATIVE -eq '1') {
    Say 'HAKO_SKIP_FETCH_NATIVE=1 なので取得を飛ばす'
    exit 0
}

if (-not $Root) {
    $Root = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
    if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }
}
$Root = (Resolve-Path $Root).Path
$lockPath = Join-Path $Root 'native.lock.json'
if (-not (Test-Path $lockPath)) {
    Say "ロックが無い（$lockPath）。何もしない"
    exit 0
}
$lock = Get-Content -Raw -Encoding UTF8 $lockPath | ConvertFrom-Json

foreach ($prop in $lock.PSObject.Properties) {
    $name = $prop.Name
    if ($name.StartsWith('_')) { continue }
    $e = $prop.Value
    foreach ($k in 'repo', 'tag', 'asset', 'sha256', 'dest') {
        if (-not ($e.PSObject.Properties.Name -contains $k) -or -not $e.$k) { Stop-Fetch "$name に $k が無い（$lockPath）" }
    }
    $sha = $e.sha256.ToLower()
    $dest = Join-Path $Root $e.dest
    $place = @{}
    if ($e.PSObject.Properties.Name -contains 'place' -and $e.place) {
        foreach ($p in $e.place.PSObject.Properties) { $place[$p.Name] = $p.Value }
    }
    $marker = Join-Path $dest ".native.lock.$name.json"
    # 置き場の指定は並びを固定した文字列で比べる（ハッシュ表の並びは毎回同じとは限らない）
    $placeKey = (@($place.Keys | Sort-Object | ForEach-Object { "$_=$($place[$_])" }) -join ';')

    # ---- 1. 目印が合っていてファイルが揃っていれば何もしない
    if (-not $Force -and (Test-Path $marker)) {
        $have = Get-Content -Raw -Encoding UTF8 $marker | ConvertFrom-Json
        $same = ($have.tag -eq $e.tag) -and ($have.sha256 -eq $sha) -and ($have.place -eq $placeKey)
        $allThere = $true
        foreach ($f in $have.files) { if (-not (Test-Path (Join-Path $Root $f))) { $allThere = $false } }
        if ($same -and $allThere) {
            Say "$name $($e.tag) は取得済み"
            continue
        }
    }

    # ---- 2. 取ってくる
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("fetch_native_" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    try {
        $zip = Join-Path $tmp $e.asset
        if ($env:HAKO_FETCH_NATIVE_ZIP) {
            # 公開の前に手元の zip で試すための口（sha256 の照合はそのまま行う）
            Say "$name を手元の zip から置く（HAKO_FETCH_NATIVE_ZIP=$($env:HAKO_FETCH_NATIVE_ZIP)）"
            Copy-Item -Force $env:HAKO_FETCH_NATIVE_ZIP $zip
        } else {
            if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
                Stop-Fetch "gh（GitHub CLI）が無い。入れて認証してから建て直す: winget install GitHub.cli → gh auth login"
            }
            Say "$name $($e.tag) を $($e.repo) から取ってくる"
            $old = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'   # gh の標準エラーで止めない（終了コードで見る）
            & gh release download $e.tag -R $e.repo -p $e.asset -D $tmp
            $rc = $LASTEXITCODE
            $ErrorActionPreference = $old
            if ($rc -ne 0) {
                Stop-Fetch ("取得に失敗（gh の終了コード $rc）。gh auth login 済みか、$($e.repo) を読む権限があるかを確かめる。" +
                            "取らずに建てるなら HAKO_SKIP_FETCH_NATIVE=1")
            }
        }

        # ---- 3. sha256 を照合
        $got = Get-Sha256 $zip
        if ($got -ne $sha) { Stop-Fetch "sha256 がロックと違う（取れたもの $got ／ ロック $sha）。止める" }

        # ---- 4. 展開して置く
        $x = Join-Path $tmp 'x'
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $x)
        New-Item -ItemType Directory -Force $dest | Out-Null
        $placed = @()
        # 置き場の決め方（2026-09-29・機体の定義も同じ zip に入るようになった）:
        #   ・place に「ファイル名」があれば、そのフォルダ
        #   ・place に「フォルダ/」（例 "models/hula/"）があれば、その下のファイルを指定のフォルダへ（中の階層は保つ）
        #   ・zip の直下のファイルは dest。★ どの place にも当たらないフォルダの中身は置かない（要る機体だけ取る）
        $prefixes = @($place.Keys | Where-Object { $_.EndsWith('/') } | Sort-Object { $_.Length } -Descending)
        foreach ($file in Get-ChildItem $x -File -Recurse) {
            $inZip = $file.FullName.Substring($x.Length).TrimStart([char]92, [char]47).Replace([string][char]92, '/')
            $toDir = $null
            if ($place.ContainsKey($inZip)) {
                $toDir = Join-Path $Root $place[$inZip]
            } else {
                foreach ($p in $prefixes) {
                    if ($inZip.StartsWith($p)) {
                        $sub = Split-Path -Parent $inZip.Substring($p.Length)
                        $toDir = Join-Path $Root $place[$p]
                        if ($sub) { $toDir = Join-Path $toDir $sub }
                        break
                    }
                }
                if (-not $toDir -and -not $inZip.Contains('/')) { $toDir = $dest }
            }
            if (-not $toDir) { continue }
            New-Item -ItemType Directory -Force $toDir | Out-Null
            Copy-Item -Force $file.FullName (Join-Path $toDir $file.Name)
            $rel = (Join-Path $toDir $file.Name).Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
            $placed += $rel
        }
        $markerObj = @{ tag = $e.tag; sha256 = $sha; place = $placeKey; files = $placed }
        ($markerObj | ConvertTo-Json -Depth 4) | Set-Content -Encoding UTF8 $marker
        $ver = ''
        $manifest = Join-Path $x 'manifest.json'
        if (Test-Path $manifest) { $ver = ' ・版 ' + (Get-Content -Raw -Encoding UTF8 $manifest | ConvertFrom-Json).version }
        Say ("$name $($e.tag) を置いた（$($placed.Count) ファイル$ver）")
    }
    finally {
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    }
}
exit 0
