# EXTRAIRE le texte des PDF de demonstration (wwwroot/documents), page par page.
#
#   .\extraire-texte-pdf.ps1
#
# Pourquoi : la palette Ctrl K et la page « Recherche » cherchent dans le TEXTE des
# pieces (Metier/RecherchePalette.cs). Les pieces de demonstration portent leur texte
# dans l'arbre (Apercu) ; les VRAIS documents de greffe, eux, sont des PDF. Ce script
# en tire le texte une fois pour toutes dans « <document>.pdf.txt », a cote du PDF :
# une page par bloc, chaque page terminee par un saut de page (\f), comme pdftotext
# l'ecrit. Metier/TextePieces.cs lit ces fichiers ; la liseuse de la page Recherche
# s'en sert pour montrer la PAGE ou le passage a ete trouve.
#
# Deux voies :
#  - un PDF qui porte du texte : pdftotext (poppler), livre avec Git pour Windows ;
#  - un PDF qui n'est qu'une IMAGE numerisee (aucun texte) : la reconnaissance de
#    caracteres de WINDOWS, en francais (Windows.Data.Pdf dessine chaque page en
#    image, Windows.Media.Ocr la lit). Rien a installer ; il faut Windows PowerShell
#    5.1 (« powershell », pas « pwsh ») et la langue francaise de Windows.
#
# A relancer quand un PDF de wwwroot/documents est ajoute ou remplace.

$ErrorActionPreference = 'Stop'

# ---- pdftotext : sur le PATH, sinon a cote de git.exe ou dans le mingw64\bin de Git.
function Trouver-Pdftotext {
    $commande = Get-Command pdftotext -ErrorAction SilentlyContinue
    if ($commande) { return $commande.Source }
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) { return $null }
    $dossierGit = Split-Path $git.Source
    foreach ($candidat in @((Join-Path $dossierGit 'pdftotext.exe'),
                            (Join-Path (Split-Path $dossierGit) 'mingw64\bin\pdftotext.exe'))) {
        if (Test-Path $candidat) { return $candidat }
    }
    return $null
}

# ---- La reconnaissance de caracteres de Windows (API WinRT, appels asynchrones attendus).
$script:ocr = $null
function Initialiser-Ocr {
    if ($PSVersionTable.PSVersion.Major -gt 5) { throw 'La reconnaissance de caracteres passe par Windows PowerShell 5.1 : lance « powershell -File extraire-texte-pdf.ps1 ».' }
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
    $null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
    $null = [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType = WindowsRuntime]
    $null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
    $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
    $null = [Windows.Media.Ocr.OcrEngine, Windows.Media.Ocr, ContentType = WindowsRuntime]
    $null = [Windows.Globalization.Language, Windows.Globalization, ContentType = WindowsRuntime]
    $moteur = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('fr-FR'))
    if (-not $moteur) { throw 'Reconnaissance de caracteres : la langue francaise de Windows manque (Parametres > Heure et langue > Langue).' }
    $methodes = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 }
    $script:ocr = @{
        Moteur    = $moteur
        Operation = $methodes | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        Action    = $methodes | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
    }
}
function Attendre($operation, [Type] $type) {
    $tache = $script:ocr.Operation.MakeGenericMethod($type).Invoke($null, @($operation))
    $tache.Wait(-1) | Out-Null
    $tache.Result
}
function Attendre-Action($action) {
    $tache = $script:ocr.Action.Invoke($null, @($action))
    $tache.Wait(-1) | Out-Null
}

# Le texte de chaque page d'un PDF numerise, lu par Windows. Chaque page est dessinee a
# environ 300 points par pouce (la page A4 fait 794 unites de large : x3), sous la
# limite d'image du moteur.
function Lire-Scan([string] $chemin) {
    if (-not $script:ocr) { Initialiser-Ocr }
    $fichier = Attendre ([Windows.Storage.StorageFile]::GetFileFromPathAsync($chemin)) ([Windows.Storage.StorageFile])
    $document = Attendre ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($fichier)) ([Windows.Data.Pdf.PdfDocument])
    $maximum = [Windows.Media.Ocr.OcrEngine]::MaxImageDimension
    $pages = @()
    for ($i = 0; $i -lt $document.PageCount; $i++) {
        $page = $document.GetPage([uint32] $i)
        $echelle = [Math]::Min(3.0, ($maximum - 1) / [Math]::Max($page.Size.Width, $page.Size.Height))
        $options = [Windows.Data.Pdf.PdfPageRenderOptions]::new()
        $options.DestinationWidth = [uint32] [Math]::Floor($page.Size.Width * $echelle)
        $options.DestinationHeight = [uint32] [Math]::Floor($page.Size.Height * $echelle)
        $flux = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
        Attendre-Action ($page.RenderToStreamAsync($flux, $options))
        $decodeur = Attendre ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($flux)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $image = Attendre ($decodeur.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $lu = Attendre ($script:ocr.Moteur.RecognizeAsync($image)) ([Windows.Media.Ocr.OcrResult])
        $pages += , (($lu.Lines | ForEach-Object { $_.Text }) -join "`n")
        $image.Dispose(); $flux.Dispose(); $page.Dispose()
    }
    return , $pages
}

$pdftotext = Trouver-Pdftotext
if (-not $pdftotext) { throw 'pdftotext introuvable : installe Git pour Windows (il le livre dans mingw64\bin) ou poppler.' }

$dossier = Join-Path $PSScriptRoot 'wwwroot\documents'
$utf8 = New-Object System.Text.UTF8Encoding $false
$faits = 0
foreach ($pdf in Get-ChildItem -Path $dossier -Filter '*.pdf' | Sort-Object Name) {
    $sortie = "$($pdf.FullName).txt"
    & $pdftotext -enc UTF-8 $pdf.FullName $sortie
    if ($LASTEXITCODE -ne 0) { throw "pdftotext a echoue sur $($pdf.Name) (code $LASTEXITCODE)." }
    $texte = [System.IO.File]::ReadAllText($sortie, $utf8)
    $voie = 'texte du PDF'
    if ($texte -notmatch '[\p{L}\p{N}]') {
        # Une image numerisee : la reconnaissance de caracteres, page par page.
        $pagesLues = Lire-Scan $pdf.FullName
        $texte = ($pagesLues | ForEach-Object { $_ + "`f" }) -join ''
        $voie = 'reconnaissance de caracteres'
        if ($texte -notmatch '[\p{L}\p{N}]') {
            Remove-Item $sortie
            Write-Host "$($pdf.Name) : aucun texte, meme a la lecture de l'image : pas de fichier"
            continue
        }
        [System.IO.File]::WriteAllText($sortie, $texte, $utf8)
    }
    $pages = ($texte -split "`f" | Where-Object { $_.Trim().Length -gt 0 }).Count
    Write-Host "$($pdf.Name) : $pages page(s) de texte ($voie)"
    $faits++
}
Write-Host "$faits PDF extrait(s) dans $dossier"
