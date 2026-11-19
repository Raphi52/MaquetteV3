# EXTRAIRE le texte des PDF de demonstration (wwwroot/documents), page par page.
#
#   .\extraire-texte-pdf.ps1
#
# Pourquoi : la palette Ctrl K et la page « Recherche » cherchent dans le TEXTE des
# pieces (Metier/RecherchePalette.cs). Les pieces de demonstration portent leur texte
# dans l'arbre (Apercu) ; les VRAIS documents de greffe, eux, sont des PDF. Ce script
# en tire le texte une fois pour toutes dans « <document>.pdf.txt », a cote du PDF :
# une page par bloc, les pages separees par un saut de page (\f), comme pdftotext
# l'ecrit. Metier/TextePieces.cs lit ces fichiers ; la liseuse de la page Recherche
# s'en sert pour montrer la PAGE ou le passage a ete trouve.
#
# A relancer quand un PDF de wwwroot/documents est ajoute ou remplace. Un PDF qui
# n'est qu'une image numerisee (aucun texte) ne produit pas de fichier : il faudrait
# une reconnaissance de caracteres, que ce script ne fait pas.
#
# Outil : pdftotext (poppler), livre avec Git pour Windows (mingw64\bin).

$ErrorActionPreference = 'Stop'

$commande = Get-Command pdftotext -ErrorAction SilentlyContinue
$outil = if ($commande) { $commande.Source } else { $null }
if (-not $outil) {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        $candidat = Join-Path (Split-Path (Split-Path $git.Source)) 'mingw64\bin\pdftotext.exe'
        if (Test-Path $candidat) { $outil = $candidat }
    }
}
if (-not $outil) { throw 'pdftotext introuvable : installe Git pour Windows (il le livre dans mingw64\bin) ou poppler.' }

$dossier = Join-Path $PSScriptRoot 'wwwroot\documents'
$faits = 0
foreach ($pdf in Get-ChildItem -Path $dossier -Filter '*.pdf' | Sort-Object Name) {
    $sortie = "$($pdf.FullName).txt"
    & $outil -enc UTF-8 $pdf.FullName $sortie
    if ($LASTEXITCODE -ne 0) { throw "pdftotext a echoue sur $($pdf.Name) (code $LASTEXITCODE)." }
    $texte = Get-Content -Raw -Encoding UTF8 $sortie
    if ($texte -notmatch '[\p{L}\p{N}]') {
        Remove-Item $sortie
        Write-Host "$($pdf.Name) : aucun texte (image numerisee), pas de fichier"
        continue
    }
    $pages = ($texte -split "`f" | Where-Object { $_.Trim().Length -gt 0 }).Count
    Write-Host "$($pdf.Name) : $pages page(s) de texte"
    $faits++
}
Write-Host "$faits PDF extrait(s) dans $dossier"
