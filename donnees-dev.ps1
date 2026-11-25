# Branche RIG V3 sur les DONNEES DE DEVELOPPEMENT : une copie ANONYMISEE de RIG_DEV,
# au lieu des donnees de demonstration (Metier/JeuDemo.cs).
#
#   .\donnees-dev.ps1            prepare les bases (la 1re fois : ~19 min), demarre le
#                                serveur V3, pose RIGV3_SERVEUR / RIGV3_COMPTE / RIGV3_GREFFES
#   .\donnees-dev.ps1 -Lancer    ... puis lance l'application (bin\Debug) branchee
#   .\donnees-dev.ps1 -Refaire   refait la copie (RIG_DEV a change, migration nouvelle)
#   .\donnees-dev.ps1 -Arreter   arrete le serveur lance par ce script
#   .\dev.ps1, .\lancer.ps1      l'appellent d'office (mode par defaut depuis le 2026-11-24 ;
#                                -Demo pour la demonstration)
#
# Ce qui est prepare (Outils\Repetition\DonneesDev.cs, commande "donnees-dev") :
#   - RIG_DEV est LU sur SQL-DEV\DEV (SELECT seul) ; la copie s'ecrit deja anonymisee
#     sur le SQL Server Docker de ce poste (noms, adresses, documents remplaces : SOC-021) ;
#   - elle est transferee dans une base V3 (comptes, droits, reglages, compteurs) ;
#   - une base commune porte le greffe 3801 au catalogue ;
#   - l'agent est un administrateur d'Amitel de la copie (son compte est un pseudonyme :
#     la session Windows du poste n'y est pas, d'ou RIGV3_COMPTE et un serveur en
#     Development, seul a ecouter ce changement de compte).
# Les noms des bases et de l'agent : %LOCALAPPDATA%\RigV3\donnees-dev.json.
#
# Le serveur tourne depuis sa PROPRE sortie (%LOCALAPPDATA%\RigV3\serveur-dev-<date>) : il ne
# verrouille ni Serveur\bin ni bin\Debug, que les bancs et l'app recompilent. Il reste
# lance apres ce script ; -Arreter l'arrete, lui seul (pid note a son lancement, chemin verifie).
# Un second appel le reprend tel quel si son code est a jour, et le RELANCE s'il a ete construit
# sur un code serveur plus ancien que celui du depot (2026-11-25 : sinon les ecrans branches par
# les autres fils restaient sur leurs exemples) : le commit apres "+" dans la version de /sante
# est compare (git diff) a Serveur\, aux projets qu'il reference de proche en proche et aux
# fichiers MSBuild de la racine ; un changement de web\ (React) ou de l'ecran seul ne le relance
# pas. La relance construit d'abord dans un dossier NEUF (la dll du serveur en cours est
# verrouillee), puis arrete l'ancien et demarre le neuf : quelques secondes de coupure.
# Jamais arrete : un serveur lance hors de ce script (message). Repris tel quel, avec un
# avertissement : une version sans commit, un commit inconnu du depot, git absent.
#
# Les variables sont posees dans CE processus PowerShell : appele par "&" ou depuis
# dev.ps1, l'application lancee ensuite les herite. Docs : docs\reconstruction\architecture.md,
# "Travailler sur les donnees de developpement".

param(
    [switch]$Lancer,
    [switch]$Refaire,
    [switch]$Arreter,
    [int]$Port = 5080
)

$ErrorActionPreference = 'Stop'
$racine = $PSScriptRoot
$reglages = Join-Path $env:LOCALAPPDATA 'RigV3'
$fichierDonnees = Join-Path $reglages 'donnees-dev.json'
# Une note par port : un serveur lance avec -Port <autre> n'efface pas celle du 5080 (qui deviendrait alors
# "pas le notre", et ne serait plus jamais relance).
$fichierServeur = Join-Path $reglages $(if ($Port -eq 5080) { 'serveur-dev.json' } else { "serveur-dev-$Port.json" })
$dllAvant = Join-Path $reglages 'serveur-dev\RigV3.Serveur.dll'   # la sortie unique d'avant le 2026-11-25 (note sans "dll")
$adresse = "http://127.0.0.1:$Port"
# Le compte sa du SQL Server Docker (docker-compose.yml, Base\BaseJetable.cs) : RIGV3_SQL_MDP s'il est change.
$mdp = if ($env:RIGV3_SQL_MDP) { $env:RIGV3_SQL_MDP } else { 'RigV3_dev_2026!' }

function Ecrire($texte, $couleur = 'Gray') { Write-Host $texte -ForegroundColor $couleur }

# Le serveur que CE script a lance sur ce port, s'il vit encore : le pid note, et le meme programme
# (dotnet qui charge la dll notee, sous %LOCALAPPDATA%\RigV3\serveur-dev*). Jamais un autre processus.
function Get-ServeurDev {
    if (-not (Test-Path $fichierServeur)) { return $null }
    $note = Get-Content $fichierServeur -Raw | ConvertFrom-Json
    if ($note.port -and $note.port -ne $Port) { return $null }
    $dll = if ($note.dll) { "$($note.dll)" } else { $dllAvant }
    if (-not $dll.StartsWith((Join-Path $reglages 'serveur-dev'), [StringComparison]::OrdinalIgnoreCase)) { return $null }
    $p = Get-CimInstance Win32_Process -Filter "ProcessId=$($note.pid)" -ErrorAction SilentlyContinue
    if ($p -and $p.Name -eq 'dotnet.exe' -and $p.CommandLine -like "*$dll*") { return Get-Process -Id $note.pid -ErrorAction SilentlyContinue }
    return $null
}

function Stop-ServeurDev {
    $s = Get-ServeurDev
    if ($s) { $s.Kill(); $null = $s.WaitForExit(10000); Ecrire "Serveur de developpement arrete (pid $($s.Id))." }
    Remove-Item $fichierServeur -ErrorAction SilentlyContinue
}

# La sante du serveur sur ce port : $null si rien ne repond.
function Get-Sante {
    try { return Invoke-RestMethod -Uri "$adresse/sante" -TimeoutSec 3 }
    catch {
        $r = $_.Exception.Response
        if ($r) {   # 503 : le serveur repond, sa base non -- le corps le dit
            try { return (New-Object IO.StreamReader($r.GetResponseStream())).ReadToEnd() | ConvertFrom-Json } catch { return [pscustomobject]@{ etat = 'illisible' } }
        }
        return $null
    }
}

# Les sources du serveur, en chemins du depot pour git : Serveur\ et les projets qu'il reference, de proche en proche
# (ProjectReference), plus les fichiers que MSBuild et dotnet lisent pour tous les projets depuis la racine. Ce qui
# n'y est pas (web\, Pont\, l'ecran) ne change pas le serveur.
function Get-SourcesServeur([string]$depot) {
    $ErrorActionPreference = 'Stop'   # un projet illisible leve : Test-CodeServeur le dit
    $depot = [IO.Path]::GetFullPath($depot).TrimEnd('\')
    $dossiers = New-Object System.Collections.Generic.List[string]
    $aLire = New-Object System.Collections.Generic.Queue[string]
    $aLire.Enqueue((Join-Path $depot 'Serveur\RigV3.Serveur.csproj'))
    while ($aLire.Count -gt 0) {
        $projet = [IO.Path]::GetFullPath($aLire.Dequeue())
        $dossier = Split-Path $projet -Parent
        if ($dossiers -contains $dossier) { continue }
        $dossiers.Add($dossier)
        foreach ($ref in ([xml](Get-Content $projet -Raw)).SelectNodes("//*[local-name()='ProjectReference']")) {
            $aLire.Enqueue((Join-Path $dossier $ref.GetAttribute('Include')))
        }
    }
    foreach ($dossier in $dossiers) {   # un projet hors du depot n'a pas d'historique a comparer
        if ($dossier.StartsWith("$depot\", [StringComparison]::OrdinalIgnoreCase)) { $dossier.Substring($depot.Length + 1).Replace('\', '/') }
    }
    'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'NuGet.config'
}

# Le code du serveur en cours face au depot : 'a-jour', 'perime', ou 'illisible' (version sans commit apres "+",
# git absent, commit inconnu du depot, projets illisibles) -- le serveur est alors repris tel quel, avec un avertissement.
# Le commit est celui que le SDK .NET inscrit dans la version a la compilation (SourceRevisionId : la tete du depot).
function Test-CodeServeur([string]$version, [string]$depot) {
    $ErrorActionPreference = 'Continue'   # git ecrit sur stderr (commit inconnu) : son code de sortie decide, sans lever
    $r = [pscustomobject]@{ etat = 'illisible'; commit = $null; tete = $null; raison = $null }
    if ($version -notmatch '\+([0-9a-fA-F]{7,40})\b') { $r.raison = "version '$version' sans commit apres '+'"; return $r }
    $r.commit = $Matches[1]
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { $r.raison = 'git introuvable'; return $r }
    $tete = & git -C $depot rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { $r.raison = "$depot n'est pas un depot git"; return $r }
    $r.tete = "$tete".Trim()
    try { $sources = @(Get-SourcesServeur $depot) }
    catch { $r.raison = "projets du serveur illisibles ($($_.Exception.Message))"; return $r }
    & git -C $depot diff --quiet $r.commit $r.tete -- @sources 2>$null
    switch ($LASTEXITCODE) {
        0 { $r.etat = 'a-jour' }
        1 { $r.etat = 'perime' }
        default { $r.raison = "commit $($r.commit) inconnu du depot $depot" }
    }
    return $r
}

if ($Arreter) { Stop-ServeurDev; return }

# --- 1. Le SQL Server Docker de ce poste ------------------------------------------------
# En 'Continue' le temps de l'appel : en 'Stop', Windows PowerShell leve des la 1re ligne d'erreur de
# docker (2>&1), et ce message-ci ne s'affichait jamais (mesure le 2026-11-24, contexte docker absent).
$ErrorActionPreference = 'Continue'
$etatSql = (& docker ps --filter 'name=^rigv3-sql$' --format '{{.Status}}' 2>&1) -join ' '
$codeDocker = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($codeDocker -ne 0) { throw "Docker ne repond pas ($etatSql) : demarrer Docker Desktop, puis relancer." }
if (-not $etatSql) {
    Ecrire 'SQL Server de developpement arrete : docker compose up -d'
    & docker compose -f (Join-Path $racine 'docker-compose.yml') up -d
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up -d a echoue.' }
}
for ($i = 0; $i -lt 60 -and ((& docker ps --filter 'name=^rigv3-sql$' --format '{{.Status}}') -notlike '*healthy*'); $i++) { Start-Sleep -Seconds 2 }

# --- 2. Les bases (deja pretes : quelques secondes) -------------------------------------
Ecrire 'Donnees de developpement : verification des bases (la premiere preparation prend ~19 min)...'
$options = @('run', '--project', (Join-Path $racine 'Outils\Repetition\RigV3.Repetition.csproj'), '--', 'donnees-dev', '--fichier', $fichierDonnees)
if ($Refaire) { $options += '--refaire' }
& dotnet @options
if ($LASTEXITCODE -ne 0) { throw "Les donnees de developpement ne sont pas pretes (code $LASTEXITCODE) : voir les lignes au-dessus." }
$d = Get-Content $fichierDonnees -Raw | ConvertFrom-Json

# --- 3. Le serveur V3 sur la base commune ------------------------------------------------
# Construit le serveur dans un dossier NEUF (celui du serveur en cours est verrouille par sa dll), PUIS arrete
# l'ancien (le notre seul : Stop-ServeurDev), PUIS demarre le neuf : la coupure ne dure que son demarrage.
# Rend $null si la compilation echoue : l'ancien serveur, pas encore arrete, reste alors en service.
function Start-ServeurDev {
    $neuf = Join-Path $reglages ('serveur-dev-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $dll = Join-Path $neuf 'RigV3.Serveur.dll'
    Ecrire "Compilation du serveur dans $neuf ..."
    & dotnet build (Join-Path $racine 'Serveur\RigV3.Serveur.csproj') -o $neuf -v q -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { Remove-Item $neuf -Recurse -Force -ErrorAction SilentlyContinue; return $null }

    $ancien = Get-ServeurDev
    Stop-ServeurDev
    $coupe = Get-Date
    $sauve = @{}
    $env_serveur = @{
        'ConnectionStrings__RigV3'  = "Server=$($d.serveur);Database=$($d.commune);User ID=sa;Password=$mdp;TrustServerCertificate=True"
        'Greffes__Authentification' = "User ID=sa;Password=$mdp;TrustServerCertificate=True"   # serveur et base : au catalogue
        'ASPNETCORE_ENVIRONMENT'    = 'Development'                                           # RIGV3_COMPTE ecoute
    }
    foreach ($k in $env_serveur.Keys) { $sauve[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $env_serveur[$k]) }
    # Par cmd, SANS -RedirectStandardOutput : avec, le serveur herite des canaux de CE processus et un appelant qui lit la
    # sortie du script (pipe, Tee-Object, un lanceur) attend la fin du serveur, c'est-a-dire jamais (mesure le 2026-11-24 :
    # 14 s bloquees sur un processus de 15 s, 0 s par cmd). L'environnement, lui, passe.
    $journal = Join-Path $reglages 'serveur-dev.log'
    try {
        $cmd = Start-Process cmd.exe -ArgumentList '/c', "`"dotnet `"$dll`" --urls $adresse 1>`"$journal`" 2>`"$journal.err`"`"" `
            -WorkingDirectory $neuf -WindowStyle Hidden -PassThru
    } finally {
        foreach ($k in $sauve.Keys) { [Environment]::SetEnvironmentVariable($k, $sauve[$k]) }   # l'app n'herite pas de la chaine sa
    }
    $p = $null
    $sante = $null
    for ($i = 0; $i -lt 60; $i++) {
        if (-not $p) {   # le serveur : le dotnet que ce cmd vient de lancer
            $enfant = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($cmd.Id) AND Name='dotnet.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($enfant) {
                $p = Get-Process -Id $enfant.ProcessId -ErrorAction SilentlyContinue
                @{ pid = $enfant.ProcessId; port = $Port; base = $d.commune; dll = $dll; lanceLe = (Get-Date).ToString('s') } |
                    ConvertTo-Json | Set-Content $fichierServeur -Encoding UTF8
            }
        }
        $sante = Get-Sante
        if ($sante -or $cmd.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $sante -or $sante.etat -ne 'ok') {
        throw "Le serveur ne repond pas sur $adresse (etat : $($sante.etat) $($sante.detail)). Journal : $journal (et $journal.err)"
    }
    $coupure = [math]::Round(((Get-Date) - $coupe).TotalSeconds, 1)

    # Les sorties des serveurs precedents, sauf celles qu'un dotnet charge encore (un serveur d'un autre port). Un
    # fichier encore tenu (antivirus, cmd qui se termine) reste : la relance suivante le reprend.
    $enService = @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue | ForEach-Object { $_.CommandLine })
    foreach ($sortie in Get-ChildItem $reglages -Directory -Filter 'serveur-dev*') {
        if ($sortie.FullName -eq $neuf -or ($enService -like "*$($sortie.FullName)\*")) { continue }
        Remove-Item $sortie.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    $commit = if ("$($sante.version)" -match '\+([0-9a-fA-F]{7})') { $Matches[1] } else { "version $($sante.version)" }
    return [pscustomobject]@{ pid = $p.Id; sante = $sante; commit = $commit; coupure = $(if ($ancien) { $coupure } else { $null }) }
}

$sante = Get-Sante
if ($sante -and $sante.base -eq $d.commune) {
    $code = Test-CodeServeur $sante.version $racine
    $court = if ($code.commit) { $code.commit.Substring(0, 7) } else { '?' }
    if ($code.etat -eq 'a-jour') {
        Ecrire "Serveur deja lance sur $adresse (base $($sante.base), code a jour : $court)."
    } elseif ($code.etat -eq 'illisible') {
        Ecrire "Attention : serveur repris tel quel sur $adresse (base $($sante.base)), son code n'est pas verifie : $($code.raison)." Yellow
    } elseif (-not (Get-ServeurDev)) {
        Ecrire ("Attention : serveur de dev perime ($court) sur $adresse, mais il n'a pas ete lance par ce script : il n'est pas arrete. " +
            "L'arreter soi-meme puis relancer, ou passer -Port <autre>.") Yellow
    } else {
        $lance = Start-ServeurDev
        if ($lance) { Ecrire "Serveur de dev perime ($court) : relance sur $($lance.commit) (pid $($lance.pid), coupure $($lance.coupure) s)." Cyan }
        else { Ecrire "Attention : serveur de dev perime ($court), mais sa compilation a echoue (lignes au-dessus) : l'ancien reste en service." Yellow }
    }
} else {
    if ($sante -and -not (Get-ServeurDev)) { throw "Le port $Port est pris par un autre serveur (base '$($sante.base)') : passer -Port <autre>." }
    $lance = Start-ServeurDev   # le notre, sur un jeu precedent, n'est arrete qu'une fois le neuf compile
    if (-not $lance) { throw 'La compilation du serveur a echoue.' }
    Ecrire "Serveur lance sur $adresse (pid $($lance.pid), base $($lance.sante.base), $($lance.sante.migrationsJouees) migrations, code $($lance.commit))."
}

# --- 4. L'application : les variables qu'elle lit ----------------------------------------
$env:RIGV3_SERVEUR = $adresse
$env:RIGV3_COMPTE = $d.compte
$env:RIGV3_GREFFES = $d.greffe
Ecrire "Greffe $($d.greffe) ($($d.nomGreffe)), agent $($d.compte) ($($d.nomAgent)) -- donnees du $($d.prepareesLe)." Cyan

if ($Lancer) {
    $exe = Join-Path $racine 'bin\Debug\net10.0-windows\RigV3Desktop.exe'
    if (-not (Test-Path $exe)) { throw "$exe absent : lancer d'abord .\lancer.ps1 (ou .\dev.ps1 -DonneesDev)." }
    Start-Process $exe
}
