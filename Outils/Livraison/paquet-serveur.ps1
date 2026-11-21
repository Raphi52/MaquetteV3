# LE PAQUET DE LA PARTIE SERVEUR D'UN SITE (SOC-010 T02, lot 12 du plan, 2026-11-21).
#
#   .\Outils\Livraison\paquet-serveur.ps1 -Site Antilles -Sortie C:\Paquets\serveur-Antilles
#
# Produit, depuis le depot (poste de construction) :
#   <Sortie>\serveur\                     la partie serveur publiee (RigV3.Serveur.exe, .NET 10,
#                                         dependante du runtime ASP.NET Core 10 installe sur le site)
#   <Sortie>\serveur\appsettings.Production.json   la configuration du site (sites.json) :
#                                         nom, instances servies, base commune, sans aucun mot de passe
#   <Sortie>\installer-serveur.ps1        la pose sur le serveur du site (service Windows)
# La version du paquet est celle de Directory.Build.props, la meme que l'ecran et la base.
# Rien n'est installe ici : la pose se fait sur le serveur du site, par l'exploitation
# (docs\reconstruction\socle-exploitation.md).

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Site,
    [Parameter(Mandatory)][string]$Sortie
)
$ErrorActionPreference = 'Stop'
$depot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

if ((Test-Path $Sortie) -and (Get-ChildItem $Sortie -Force | Select-Object -First 1)) {
    throw "Le dossier $Sortie n'est pas vide : un paquet ne s'ecrit pas par-dessus un autre."
}
$serveur = Join-Path $Sortie 'serveur'

& dotnet publish (Join-Path $depot 'Serveur\RigV3.Serveur.csproj') -c Release -r win-x64 --self-contained false -o $serveur -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "Publication de la partie serveur en echec (code $LASTEXITCODE)." }

# La configuration du site, par l'outil de livraison (une seule regle : Sites.ConfigurationServeur).
$outil = Join-Path ([IO.Path]::GetTempPath()) ("rigv3-livraison-" + [guid]::NewGuid().ToString('N'))
& dotnet build (Join-Path $depot 'Outils\Livraison\RigV3.Livraison.csproj') -nologo -v:q "-p:OutputPath=$outil\"
if ($LASTEXITCODE -ne 0) { throw "Construction de l'outil de livraison en echec (code $LASTEXITCODE)." }
& (Join-Path $outil 'RigV3.Livraison.exe') site --site $Site --sortie $serveur
if ($LASTEXITCODE -ne 0) { throw "Configuration du site $Site en echec (code $LASTEXITCODE)." }
Remove-Item $outil -Recurse -Force

Copy-Item (Join-Path $PSScriptRoot 'installer-serveur.ps1') $Sortie
$version = ((Get-Item (Join-Path $serveur 'RigV3.Serveur.exe')).VersionInfo.ProductVersion -replace '\+.*$', '')
Write-Host "Paquet de la partie serveur $version pour le site $Site : $Sortie" -ForegroundColor Green
