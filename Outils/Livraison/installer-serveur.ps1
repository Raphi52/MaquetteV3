# LA POSE DE LA PARTIE SERVEUR SUR UN SITE (SOC-010 T02, CA04 ; lot 12 du plan, 2026-11-21).
# A lancer SUR le serveur du site (Amitel, Antilles, Reunion, Polynesie), en administrateur,
# depuis le paquet produit par paquet-serveur.ps1 :
#
#   .\installer-serveur.ps1 -Compte 'DOMAINE\svc-rigv3' [-Racine C:\RigV3Serveur]
#   .\installer-serveur.ps1 -Revenir 2026.1.0          # rebascule le service sur une version deja posee
#
# Ce qu'il fait :
#   1. pose la version du paquet A COTE des autres (<Racine>\versions\<version>) : la version en
#      service n'est pas touchee tant que la nouvelle n'est pas complete ;
#   2. cree le service Windows « RigV3Serveur » (compte de service : celui qui ouvre les bases,
#      session Windows comme la V2), ou l'arrete et le fait pointer sur la nouvelle version ;
#   3. lui fait redemarrer tout seul apres une panne (CA04 : 5 s, 10 s, puis 60 s) ;
#   4. le redemarre et attend que GET /sante reponde.
# L'HEURE : une mise a jour arrete la partie serveur quelques secondes. Elle se lance hors des
# heures d'ouverture des greffes du site, a LEUR heure locale (CA04) : la commande
# « RigV3.Livraison sites » donne la prochaine fenetre de chaque site. Les ecrans ouverts
# reprennent seuls au redemarrage (chaque appel est independant) ; une saisie validee est deja
# en base (transaction validee), rien n'est garde en memoire dans la partie serveur.

[CmdletBinding()]
param(
    [string]$Racine = 'C:\RigV3Serveur',
    [string]$Compte,
    [string]$Revenir
)
$ErrorActionPreference = 'Stop'
$nom = 'RigV3Serveur'

if ($Revenir) {
    $cible = Join-Path $Racine "versions\$Revenir"
    if (-not (Test-Path (Join-Path $cible 'RigV3.Serveur.exe'))) { throw "La version $Revenir n'est pas posee dans $Racine\versions." }
} else {
    $source = Join-Path $PSScriptRoot 'serveur'
    $exe = Join-Path $source 'RigV3.Serveur.exe'
    if (-not (Test-Path $exe)) { throw "$exe introuvable : lancer ce script depuis un paquet de paquet-serveur.ps1." }
    $version = ((Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', '')
    $cible = Join-Path $Racine "versions\$version"
    if (Test-Path $cible) { throw "La version $version est deja posee ($cible) : -Revenir $version pour y rebasculer." }
    $partiel = "$cible.partiel"
    if (Test-Path $partiel) { Remove-Item $partiel -Recurse -Force }
    Copy-Item $source $partiel -Recurse
    Rename-Item $partiel (Split-Path $cible -Leaf)
}
$binaire = '"' + (Join-Path $cible 'RigV3.Serveur.exe') + '"'

$service = Get-Service -Name $nom -ErrorAction SilentlyContinue
if ($null -eq $service) {
    if (-not $Compte) { throw "Premiere pose : -Compte (le compte de service qui ouvre les bases) est obligatoire." }
    $mdp = Read-Host "Mot de passe du compte $Compte" -AsSecureString
    New-Service -Name $nom -BinaryPathName $binaire -DisplayName 'RIG V3 - partie serveur' -StartupType Automatic `
        -Credential (New-Object System.Management.Automation.PSCredential($Compte, $mdp)) | Out-Null
} else {
    Stop-Service -Name $nom
    & sc.exe config $nom binPath= $binaire | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe config en echec (code $LASTEXITCODE)." }
}
& sc.exe failure $nom reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null
Start-Service -Name $nom

# La partie serveur repond-elle ? (adresse du site : appsettings.Production.json, cle Urls)
$config = Get-Content (Join-Path $cible 'appsettings.Production.json') -Raw | ConvertFrom-Json
$adresse = if ($config.Urls) { ($config.Urls -split ';')[0] } else { 'http://localhost:5000' }
for ($i = 0; $i -lt 30; $i++) {
    try { $sante = Invoke-RestMethod "$adresse/sante" -UseDefaultCredentials; break } catch { Start-Sleep -Seconds 2 }
}
if (-not $sante) { throw "La partie serveur ne repond pas sur $adresse/sante apres 60 s : voir le journal des evenements Windows ; -Revenir <version> pour rebasculer." }
Write-Host "RigV3Serveur en service sur $cible : $($sante.etat), base $($sante.base), version $($sante.version)." -ForegroundColor Green
