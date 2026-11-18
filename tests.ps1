# Lance TOUS les bancs de controle de tests/, l'un apres l'autre.
#
#   .\tests.ps1            # tout
#   .\tests.ps1 Ged        # seulement les bancs dont le nom contient « Ged »
#   .\tests.ps1 -Detail    # affiche aussi les lignes OK de chaque banc
#
# Chaque banc est un petit programme console qui rend 0 s'il est vert et un
# code non nul s'il est rouge. Ce script les enchaine, resume, et rend
# LUI-MEME un echec (code 1) des qu'un seul banc est rouge : c'est ce code
# de sortie qui fait foi, pas le texte affiche.

[CmdletBinding()]
param(
    [string]$Filtre = '',
    [switch]$Detail
)

# 'Continue' et non 'Stop' : un banc qui ecrit sur la sortie d'erreur (ou un
# avertissement de compilation) ne doit pas interrompre la serie -- c'est son
# CODE DE SORTIE qui decide, et le bilan final le reporte.
$ErrorActionPreference = 'Continue'

$bancs = Get-ChildItem -Path (Join-Path $PSScriptRoot 'tests') -Filter '*.csproj' -Recurse |
         Sort-Object Name
if ($Filtre) { $bancs = $bancs | Where-Object { $_.BaseName -like "*$Filtre*" } }

if (-not $bancs) {
    Write-Host "Aucun banc trouve dans tests/." -ForegroundColor Yellow
    exit 1
}

$resultats = @()
$debutTotal = Get-Date

foreach ($banc in $bancs) {
    $nom = $banc.BaseName
    Write-Host ""
    Write-Host "=== $nom" -ForegroundColor Cyan
    $debut = Get-Date
    # La sortie d'erreur est fusionnee a la sortie standard : un plantage
    # (exception non rattrapee) doit apparaitre dans le compte-rendu.
    #
    # POURQUOI PAS `dotnet run` (18/09/2026) : le banc reference RigV3Desktop.csproj,
    # donc `dotnet run` recopie RigV3Desktop.exe dans bin\ -- fichier VERROUILLE
    # quand l'application tourne (MSB3021/MSB3027). OutputPath global envoie le
    # banc ET le projet reference dans un dossier a part : bin\ n'est jamais touche.
    # Ce dossier est a CINQ niveaux sous la racine du depot, comme le bin\Debug\
    # net10.0-windows\ d'un banc : 20 bancs relisent leurs sources par
    # AppContext.BaseDirectory + « ../../../../../ ». Sous %TEMP% (avant le
    # 2026-11-18), ces cinq crans tombaient sur C:\Users\<nom>\ : 20 bancs sur
    # 28 sortaient rouges sur DirectoryNotFoundException, quel que soit le code.
    # « binverif* » est deja ignore par git.
    $sortieDir = Join-Path $PSScriptRoot "tests\$nom\binverif\Debug\net10.0-windows\"
    $sortie = & dotnet build $banc.FullName --nologo -v:q "-p:OutputPath=$sortieDir" 2>&1
    $code = $LASTEXITCODE
    if ($code -eq 0) {
        $sortie = & (Join-Path $sortieDir "$nom.exe") 2>&1
        $code = $LASTEXITCODE
    }
    $duree = [int]((Get-Date) - $debut).TotalSeconds

    if ($Detail -or $code -ne 0) {
        $sortie | ForEach-Object { Write-Host "    $_" }
    }
    else {
        # Vert : on ne garde que la derniere ligne, le resume du banc.
        $sortie | Where-Object { "$_".Trim() } | Select-Object -Last 1 |
            ForEach-Object { Write-Host "    $_" }
    }

    $resultats += [pscustomobject]@{
        Banc   = $nom
        Code   = $code
        Vert   = ($code -eq 0)
        Duree  = $duree
        Echecs = @($sortie | Where-Object { "$_" -like 'ECHEC*' }).Count
    }
}

$dureeTotale = [int]((Get-Date) - $debutTotal).TotalSeconds
$rouges = @($resultats | Where-Object { -not $_.Vert })

Write-Host ""
Write-Host "--- Bilan ($($resultats.Count) bancs, $dureeTotale s)" -ForegroundColor Cyan
foreach ($r in $resultats) {
    $etat = if ($r.Vert) { 'VERT ' } else { 'ROUGE' }
    $couleur = if ($r.Vert) { 'Green' } else { 'Red' }
    # NOTE : ne pas nommer cette variable $detail -- PowerShell ignore la casse,
    # elle ecraserait le parametre -Detail (switch) et leverait une erreur de type.
    $ligne = if ($r.Vert) { "$($r.Duree)s" } else { "$($r.Duree)s, $($r.Echecs) echec(s), code $($r.Code)" }
    Write-Host ("  {0}  {1,-18} {2}" -f $etat, $r.Banc, $ligne) -ForegroundColor $couleur
}

Write-Host ""
if ($rouges.Count -eq 0) {
    Write-Host "Tous les bancs sont VERTS." -ForegroundColor Green
    exit 0
}
Write-Host "$($rouges.Count) banc(s) ROUGE(S) : $($rouges.Banc -join ', ')" -ForegroundColor Red
exit 1
