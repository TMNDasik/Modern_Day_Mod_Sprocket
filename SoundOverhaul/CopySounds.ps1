
param(
    [string]$Source = "F:\NEWAudio",
    [string]$Target = "C:\Program Files (x86)\Steam\steamapps\common\Sprocket\BepInEx\plugins\MDSoundOverhaul"
)
$manifest = Join-Path $PSScriptRoot "banks.txt"
$dest = Join-Path $Target "Sound"

function SourceName([string]$name) { if ($name -like 'Sound_*') { return 'GHPC_' + $name.Substring(6) } return $null }
function TargetName([string]$name) { return [regex]::Replace($name, '(?i)ghpc', 'Sound') }
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $manifest (Join-Path $Target "banks.txt") -Force
$copied = 0; $bytes = 0; $missing = @()
foreach ($line in Get-Content $manifest) {
    $l = $line.Trim()
    if ($l -eq '' -or $l.StartsWith('#')) { continue }
    $parts = $l.Split('|')
    if ($parts.Count -lt 3) { continue }
    foreach ($pat in $parts[2].Split(';')) {
        $p = $pat.Trim().Replace('/', '\')
        if ($p -eq '') { continue }
        $dir = Split-Path $p -Parent
        $name = Split-Path $p -Leaf
        $srcDir = Join-Path $Source $dir
        $found = @(Get-ChildItem -LiteralPath $srcDir -Filter $name -File -ErrorAction SilentlyContinue)
        $alt = SourceName $name
        if ($found.Count -eq 0 -and $alt) { $found = @(Get-ChildItem -LiteralPath $srcDir -Filter $alt -File -ErrorAction SilentlyContinue) }
        if ($found.Count -eq 0) { $missing += $p; continue }
        $outDir = Join-Path $dest $dir
        New-Item -ItemType Directory -Force $outDir | Out-Null
        foreach ($f in $found) {
            $to = Join-Path $outDir (TargetName $f.Name)
            if (-not (Test-Path -LiteralPath $to) -or (Get-Item -LiteralPath $to).Length -ne $f.Length) {
                Copy-Item -LiteralPath $f.FullName $to -Force
                $copied++
            }
            $bytes += $f.Length
        }
    }
}
"Files referenced: {0:N1} MB, newly copied: {1}" -f ($bytes / 1MB), $copied
if ($missing.Count -gt 0) { "Patterns with no files:"; $missing }
