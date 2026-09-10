# Lanceur du raccourci bureau : COMPILE d'abord, LANCE ensuite.
#
# Pourquoi ce fichier existe : le raccourci « RigV3Desktop » pointait droit sur
# bin\Debug\net9.0-windows\RigV3Desktop.exe. Double-cliquer lancait donc le
# binaire de la DERNIERE compilation -- toute modification du code, d'un .razor
# ou d'un fichier de wwwroot restait invisible tant que personne ne relancait
# `dotnet build` a la main. C'est la cause du « ca ne s'applique jamais »
# constate le 2026-09-10.
#
# Ce script fait les trois gestes dans le bon ordre :
#   1. ferme l'instance deja ouverte (elle VERROUILLE l'exe : sans cela, la
#      copie de bin echoue avec MSB3027 et la compilation ne sert a rien) ;
#   2. compile ;
#   3. lance l'exe fraichement produit.
#
# Pour developper en continu (reprise a chaud, sans relancer), utiliser dev.ps1.

$ErrorActionPreference = 'Stop'
$racine = $PSScriptRoot
$exe = Join-Path $racine 'bin\Debug\net9.0-windows\RigV3Desktop.exe'

# 1. Fermer l'instance en cours -- et ELLE SEULE : on filtre sur le chemin exact
#    de l'executable, jamais sur le seul nom de processus.
Get-Process -Name 'RigV3Desktop' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe } |
    ForEach-Object {
        $null = $_.CloseMainWindow()
        if (-not $_.WaitForExit(5000)) { $_.Kill(); $_.WaitForExit(5000) }
    }

# 2. Compiler. Un echec doit rester VISIBLE : sinon on relancerait en silence
#    l'ancien binaire et le probleme se reproduirait a l'identique.
$journal = Join-Path $env:TEMP 'rigv3-build.log'
& dotnet build "$racine\RigV3Desktop.csproj" -v m --nologo *>&1 | Tee-Object -FilePath $journal | Out-Null
if ($LASTEXITCODE -ne 0) {
    $fin = (Get-Content $journal -Tail 20) -join "`n"
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "La compilation a echoue : l'application n'a PAS ete lancee.`n`n$fin",
        'RIG V3 - compilation en echec') | Out-Null
    exit 1
}

# 3. Lancer la version qui vient d'etre produite.
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
