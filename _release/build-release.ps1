<#
    build-release.ps1

    Construit, pour chacun des 3 plugins, un dossier "propre" contenant
    uniquement ce qui est nécessaire à l'exécution (pas le code source,
    pas les fichiers de build), puis empaquette chacun en .pext via
    l'outil Toolbox de Playnite -- le format que les utilisateurs
    installent ensuite par simple glisser-déposer sur la fenêtre de
    Playnite, ou via l'annuaire officiel une fois publié.

    Usage : lance ce script depuis PowerShell (pas besoin d'admin).
    Les .pext générés se retrouvent dans _release\output\.
#>

$ErrorActionPreference = "Stop"

$root        = "C:\Users\frede\AppData\Local\Playnite\Extensions"
$stagingRoot = "C:\Users\frede\Documents\GitHub\Playnite-physical-cartridge-disc\_release\staging"
$outputDir   = "C:\Users\frede\Documents\GitHub\Playnite-physical-cartridge-disc\_release\output"
$toolbox     = "C:\Users\frede\AppData\Local\Playnite\Toolbox.exe"

function New-CleanDir($path) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path | Out-Null
}

# La version vient de extension.yaml : elle ne doit jamais être écrite en dur
# ici, sous peine de publier un .pext dont le nom ment sur son contenu.
function Get-ExtensionVersion($stagingDir) {
    $line = Get-Content "$stagingDir\extension.yaml" |
        Where-Object { $_ -match '^\s*Version\s*:' } |
        Select-Object -First 1
    if (-not $line) { throw "Version introuvable dans $stagingDir\extension.yaml" }
    ($line -split ':', 2)[1].Trim()
}

# Renomme le .pext produit par Toolbox (<Id>_<horodatage>.pext) en <Nom>-v<version>.pext.
function Rename-Package($id, $name, $version) {
    $produced = Get-ChildItem $outputDir -Filter "${id}_*.pext"
    if (-not $produced) { throw "Toolbox n'a produit aucun paquet pour $name ($id)" }
    $produced | ForEach-Object { Move-Item $_.FullName "$outputDir\$name-v$version.pext" -Force }
    Write-Host "  -> $name-v$version.pext" -ForegroundColor Green
}

function Copy-IfExists($source, $destDir) {
    if (Test-Path $source) {
        Copy-Item $source $destDir
    } else {
        Write-Warning "Absent (ignoré) : $source"
    }
}

New-CleanDir $stagingRoot
# On ne vide pas $outputDir : il conserve les .pext des versions déjà
# publiées, vers lesquelles les manifestes continuent de pointer.
if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# ============================================================
# CartridgeShelf
# ============================================================
Write-Host "`n=== CartridgeShelf ===" -ForegroundColor Cyan
$src = "$root\CartridgeShelf"
$dst = "$stagingRoot\CartridgeShelf"
New-CleanDir $dst
New-CleanDir "$dst\Database"
New-CleanDir "$dst\Assets"

Copy-IfExists "$src\extension.yaml" $dst
Copy-IfExists "$src\CartridgeShelf.dll" $dst
Copy-IfExists "$src\Database\Snes.csv" "$dst\Database"
Copy-IfExists "$src\Database\UserLibrary.csv" "$dst\Database"
Copy-IfExists "$src\Database\ScreenScraperCredentials.csv" "$dst\Database"
Copy-IfExists "$src\Database\SteamGridDbCredentials.csv" "$dst\Database"
Copy-IfExists "$src\Assets\no_cartridge.png" "$dst\Assets"

& $toolbox pack $dst $outputDir

Rename-Package "a1312fcb-7107-4168-95ba-181dd6069299" "CartridgeShelf" (Get-ExtensionVersion $dst)

# ============================================================
# DiscShelf
# ============================================================
Write-Host "`n=== DiscShelf ===" -ForegroundColor Cyan
$src = "$root\DiscShelf"
$dst = "$stagingRoot\DiscShelf"
New-CleanDir $dst
New-CleanDir "$dst\Database"
New-CleanDir "$dst\Assets"

Copy-IfExists "$src\extension.yaml" $dst
Copy-IfExists "$src\DiscShelf.dll" $dst
Copy-IfExists "$src\Assets\no_disc.png" "$dst\Assets"

foreach ($f in @(
    "Dreamcast.csv", "MegaCD.csv", "NeoGeoCD.csv", "Playstation.csv",
    "PlayStation2.csv", "PlayStation3.txt", "Saturn.csv",
    "ScreenScraperCredentials.csv", "SteamGridDbCredentials.csv", "UserLibrary.csv"
)) {
    Copy-IfExists "$src\Database\$f" "$dst\Database"
}

& $toolbox pack $dst $outputDir

Rename-Package "7c3e2d1a-9f61-4c7d-8b4e-123456789abc" "DiscShelf" (Get-ExtensionVersion $dst)

# ============================================================
# DigitalShelf
# ============================================================
Write-Host "`n=== DigitalShelf ===" -ForegroundColor Cyan
$src = "$root\DigitalShelf"
$dst = "$stagingRoot\DigitalShelf"
New-CleanDir $dst
New-CleanDir "$dst\Assets"

Copy-IfExists "$src\extension.yaml" $dst
Copy-IfExists "$src\DigitalShelf.dll" $dst
Copy-IfExists "$src\mosaic_enabled.txt" $dst
Copy-IfExists "$src\sounds.txt" $dst
Copy-IfExists "$src\Assets\console_return.png" "$dst\Assets"
Copy-IfExists "$src\Assets\digital_game.png" "$dst\Assets"

& $toolbox pack $dst $outputDir

Rename-Package "f7ba5ce6-190b-47ed-ba0c-59928375d2a1" "DigitalShelf" (Get-ExtensionVersion $dst)

Write-Host "`n=== Terminé ===" -ForegroundColor Green
Write-Host "Fichiers .pext générés dans : $outputDir"
Get-ChildItem $outputDir -Filter *.pext | Format-Table Name, Length
