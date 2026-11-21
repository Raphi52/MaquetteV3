# L'ICONE DE RIG V3 SUR UN SERVEUR RDS (SOC-012 T01, CA01 ; lot 12 du plan, 2026-11-21).
# A lancer une fois par serveur RDS, en administrateur, APRES la premiere pose de l'ecran :
#
#   RigV3.Livraison rds --ecran <publication RigV3Desktop> --lanceur <publication Lanceur> --racine "\\RDS-01\c$\Program Files\RigV3"
#   .\installer-rds.ps1 -Racine 'C:\Program Files\RigV3' [-Greffe 3801]
#
# Pose l'icone « RIG V3 » sur le bureau commun et dans le menu Demarrer de TOUS les agents du
# serveur, a cote de celle de la V2. Elle ouvre RigV3Lanceur.exe (aucune installation ni droit
# d'administrateur cote agent : tout est sur le serveur, ses donnees vont dans son profil).
# -Greffe : une icone par greffe (« greffe=3801 » passe a l'ecran, comme les icones de la V2) ;
# sans lui, l'ecran prend les greffes declares pour le poste (RIGV3_GREFFES).

[CmdletBinding()]
param(
    [string]$Racine = 'C:\Program Files\RigV3',
    [string]$Greffe
)
$ErrorActionPreference = 'Stop'
$lanceur = Join-Path $Racine 'RigV3Lanceur.exe'
if (-not (Test-Path $lanceur)) { throw "$lanceur introuvable : poser d'abord l'ecran et le lanceur (RigV3.Livraison rds)." }

$nom = if ($Greffe) { "RIG V3 ($Greffe)" } else { 'RIG V3' }
$shell = New-Object -ComObject WScript.Shell
foreach ($dossier in @([Environment]::GetFolderPath('CommonDesktopDirectory'), [Environment]::GetFolderPath('CommonPrograms'))) {
    $raccourci = $shell.CreateShortcut((Join-Path $dossier "$nom.lnk"))
    $raccourci.TargetPath = $lanceur
    $raccourci.WorkingDirectory = $Racine
    if ($Greffe) { $raccourci.Arguments = "greffe=$Greffe" }
    $raccourci.IconLocation = "$lanceur,0"
    $raccourci.Description = 'RIG V3 - greffe du tribunal de commerce'
    $raccourci.Save()
    Write-Host "Icone posee : $(Join-Path $dossier "$nom.lnk")"
}
