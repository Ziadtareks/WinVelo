$ErrorActionPreference = "Stop"
$tweaksDir = "C:\Dev\Tweakora\Catalog\Tweaks"
$files = Get-ChildItem -Path $tweaksDir -Filter "*.json"

$allTweaks = @()
foreach ($file in $files) {
    $json = Get-Content $file.FullName -Raw | ConvertFrom-Json
    foreach ($tweak in $json) {
        $tweak | Add-Member -NotePropertyName "_file" -NotePropertyValue $file.Name -Force
        $allTweaks += $tweak
    }
}

Write-Host "Total tweaks loaded: $($allTweaks.Count)"

$registryTargets = @{}
$serviceTargets = @{}

foreach ($tweak in $allTweaks) {
    if ($tweak.apply) {
        foreach ($action in $tweak.apply) {
            if ($action.type -eq "registry" -or $action.type -eq "Registry") {
                $hive = ($action.hive -replace "HKEY_", "").ToUpper()
                $key = ($action.key -replace "^\\", "" -replace "\\$", "").ToUpper()
                $val = if ($action.valueName) { $action.valueName.ToUpper() } else { "@DEFAULT" }
                $target = "$hive\$key\$val"
                
                if (-not $registryTargets.ContainsKey($target)) {
                    $registryTargets[$target] = @()
                }
                $registryTargets[$target] += $tweak
            }
            elseif ($action.type -eq "service" -or $action.type -eq "Service") {
                $svc = $action.serviceName.ToUpper()
                if (-not $serviceTargets.ContainsKey($svc)) {
                    $serviceTargets[$svc] = @()
                }
                $serviceTargets[$svc] += $tweak
            }
        }
    }
}

Write-Host "`n=== REGISTRY COLLISIONS ==="
$collisionCount = 0
foreach ($key in $registryTargets.Keys) {
    $uniqueTweaks = $registryTargets[$key] | Sort-Object id -Unique
    if ($uniqueTweaks.Count -gt 1) {
        $collisionCount++
        Write-Host "`nTarget: $key"
        foreach ($t in $uniqueTweaks) {
            Write-Host "  - [$($t._file)] $($t.id) : '$($t.name)'"
        }
    }
}

Write-Host "`n=== SERVICE COLLISIONS ==="
foreach ($key in $serviceTargets.Keys) {
    $uniqueTweaks = $serviceTargets[$key] | Sort-Object id -Unique
    if ($uniqueTweaks.Count -gt 1) {
        $collisionCount++
        Write-Host "`nTarget: $key"
        foreach ($t in $uniqueTweaks) {
            Write-Host "  - [$($t._file)] $($t.id) : '$($t.name)'"
        }
    }
}

Write-Host "`nTotal colliding targets: $collisionCount"
