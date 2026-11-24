# Branche RIG V3 sur les DONNEES DE DEVELOPPEMENT : une copie ANONYMISEE de RIG_DEV,
# au lieu des donnees de demonstration (Metier/JeuDemo.cs).
#
#   .\donnees-dev.ps1            prepare les bases (la 1re fois : ~19 min), demarre le
#                                serveur V3, pose RIGV3_SERVEUR / RIGV3_COMPTE / RIGV3_GREFFES
#   .\donnees-dev.ps1 -Lancer    ... puis lance l'application (bin\Debug) branchee
#   .\donnees-dev.ps1 -Refaire   refait la copie (RIG_DEV a change, migration nouvelle)
#   .\donnees-dev.ps1 -Arreter   arrete le serveur lance par ce script
#   .\dev.ps1 -DonneesDev        le mode developpement (reprise a chaud) sur ces donnees
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
# Le serveur tourne depuis sa PROPRE sortie (%LOCALAPPDATA%\RigV3\serveur-dev) : il ne
# verrouille ni Serveur\bin ni bin\Debug, que les bancs et l'app recompilent. Il reste
# lance apres ce script (un second appel le reprend tel quel) ; -Arreter l'arrete, lui
# seul (pid note a son lancement, chemin verifie).
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
$fichierServeur = Join-Path $reglages 'serveur-dev.json'
$sortieServeur = Join-Path $reglages 'serveur-dev'
$dllServeur = Join-Path $sortieServeur 'RigV3.Serveur.dll'
$adresse = "http://127.0.0.1:$Port"
# Le compte sa du SQL Server Docker (docker-compose.yml, Base\BaseJetable.cs) : RIGV3_SQL_MDP s'il est change.
$mdp = if ($env:RIGV3_SQL_MDP) { $env:RIGV3_SQL_MDP } else { 'RigV3_dev_2026!' }

function Ecrire($texte, $couleur = 'Gray') { Write-Host $texte -ForegroundColor $couleur }

# Le serveur que CE script a lance, s'il vit encore : le pid note, et le meme programme
# (dotnet qui charge la dll de serveur-dev). Jamais un autre processus.
function Get-ServeurDev {
    if (-not (Test-Path $fichierServeur)) { return $null }
    $note = Get-Content $fichierServeur -Raw | ConvertFrom-Json
    $p = Get-CimInstance Win32_Process -Filter "ProcessId=$($note.pid)" -ErrorAction SilentlyContinue
    if ($p -and $p.Name -eq 'dotnet.exe' -and $p.CommandLine -like "*$dllServeur*") { return Get-Process -Id $note.pid -ErrorAction SilentlyContinue }
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

if ($Arreter) { Stop-ServeurDev; return }

# --- 1. Le SQL Server Docker de ce poste ------------------------------------------------
$etatSql = (& docker ps --filter 'name=^rigv3-sql$' --format '{{.Status}}' 2>&1) -join ' '
if ($LASTEXITCODE -ne 0) { throw "Docker ne repond pas ($etatSql) : demarrer Docker Desktop, puis relancer ce script." }
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
$sante = Get-Sante
if ($sante -and $sante.base -eq $d.commune) {
    Ecrire "Serveur deja lance sur $adresse (base $($sante.base))."
} else {
    if ($sante) {
        $s = Get-ServeurDev
        if (-not $s) { throw "Le port $Port est pris par un autre serveur (base '$($sante.base)') : passer -Port <autre>." }
        Stop-ServeurDev   # le notre, sur un jeu precedent
    }
    Ecrire "Compilation du serveur dans $sortieServeur ..."
    & dotnet build (Join-Path $racine 'Serveur\RigV3.Serveur.csproj') -o $sortieServeur -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'La compilation du serveur a echoue.' }

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
        $cmd = Start-Process cmd.exe -ArgumentList '/c', "`"dotnet `"$dllServeur`" --urls $adresse 1>`"$journal`" 2>`"$journal.err`"`"" `
            -WorkingDirectory $sortieServeur -WindowStyle Hidden -PassThru
    } finally {
        foreach ($k in $sauve.Keys) { [Environment]::SetEnvironmentVariable($k, $sauve[$k]) }   # l'app n'herite pas de la chaine sa
    }
    $p = $null
    for ($i = 0; $i -lt 60; $i++) {
        if (-not $p) {   # le serveur : le dotnet que ce cmd vient de lancer
            $enfant = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($cmd.Id) AND Name='dotnet.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($enfant) {
                $p = Get-Process -Id $enfant.ProcessId -ErrorAction SilentlyContinue
                @{ pid = $enfant.ProcessId; port = $Port; base = $d.commune; lanceLe = (Get-Date).ToString('s') } | ConvertTo-Json | Set-Content $fichierServeur -Encoding UTF8
            }
        }
        $sante = Get-Sante
        if ($sante -or $cmd.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $sante -or $sante.etat -ne 'ok') {
        throw "Le serveur ne repond pas sur $adresse (etat : $($sante.etat) $($sante.detail)). Journal : $journal (et $journal.err)"
    }
    Ecrire "Serveur lance sur $adresse (pid $($p.Id), base $($sante.base), $($sante.migrationsJouees) migrations)."
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
