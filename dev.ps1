﻿# Lance RIG V3 en mode developpement, avec reprise a chaud.
#
#   .\dev.ps1
#
# Rien a faire a la main :
#  - ecrans React (web/src, .tsx/.ts) : le serveur Vite, lance ici, les reprend
#    A CHAUD dans la fenetre ouverte -- l'ecran se redessine en gardant son
#    etat, sans recharger la page ni relancer l'application. La fenetre charge
#    sa page depuis ce serveur (variable RIGV3_VITE, Pont/PageWeb.cs) ;
#  - code C# : dotnet watch applique la modification dans l'application qui
#    tourne ;
#  - CSS / images de wwwroot : la fenetre surveille elle-meme les sources et
#    recharge sa page toute seule. L'application ne redemarre pas.
#
# Si une modification C# est trop profonde pour etre appliquee a chaud
# (« rude edit »), l'application est relancee automatiquement.

# Une instance deja ouverte VERROUILLE RigV3Desktop.exe : la premiere
# compilation de watch echouerait (MSB3027). On ferme donc l'instance
# lancee depuis CE dossier -- et elle seule, jamais tous les processus
# portant ce nom.
$exe = Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\RigV3Desktop.exe'
Get-Process -Name 'RigV3Desktop' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe } |
    ForEach-Object {
        $null = $_.CloseMainWindow()
        if (-not $_.WaitForExit(5000)) { $_.Kill(); $_.WaitForExit(5000) }
    }

$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = '1'
$env:DOTNET_USE_POLLING_FILE_WATCHER = '0'
$env:DOTNET_ENVIRONMENT = 'Development'

# --- SERVEUR VITE (ecrans React a chaud, 19/11/2026, retrait de Blazor).
# Un port LIBRE, choisi ici : deux copies du depot peuvent tourner en meme
# temps sans se disputer le 5173 de Vite. strictPort (vite.config.ts) : si le
# port est pris entre-temps, Vite s'arrete au lieu d'en prendre un autre que la
# fenetre ne connaitrait pas.
$web = Join-Path $PSScriptRoot 'web'
if (-not (Test-Path (Join-Path $web 'node_modules\.bin\vite.cmd'))) {
    Push-Location $web
    try { & npm ci --no-audit --no-fund } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { Write-Host 'npm ci en echec : le serveur Vite ne peut pas demarrer.' -ForegroundColor Red; exit 1 }
}
$ecoute = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$ecoute.Start(); $port = $ecoute.LocalEndpoint.Port; $ecoute.Stop()
$adresseVite = "http://127.0.0.1:$port/"
$vite = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "npm run dev -- --port $port" `
    -WorkingDirectory $web -NoNewWindow -PassThru

# La fenetre ne doit pas s'ouvrir avant que la page soit servie : elle
# afficherait « page introuvable ». On attend la premiere reponse de Vite.
$pret = $false
for ($i = 0; $i -lt 60 -and -not $vite.HasExited; $i++) {
    try {
        $null = Invoke-WebRequest -Uri $adresseVite -UseBasicParsing -TimeoutSec 2
        $pret = $true; break
    } catch { Start-Sleep -Milliseconds 500 }
}
if (-not $pret) {
    Write-Host "Le serveur Vite n'a pas repondu sur $adresseVite : arret." -ForegroundColor Red
    if (-not $vite.HasExited) { & taskkill /PID $vite.Id /T /F | Out-Null }
    exit 1
}
# Herite par dotnet watch, donc par l'application qu'il lance.
$env:RIGV3_VITE = $adresseVite

Write-Host 'RIG V3 - mode developpement (reprise a chaud).' -ForegroundColor Cyan
Write-Host "Ecrans React (.tsx) : Vite sur $adresseVite. C#, CSS : repris automatiquement, sans redemarrage." -ForegroundColor DarkGray

# dotnet watch ne relance JAMAIS une application qui s'est fermee : il
# affiche « Exited / Waiting for a file to change » et Ctrl+R est inactif
# dans cet etat (dotnet/sdk#34029). La reprise a chaud est alors morte.
# On surveille donc l'application, et la FACON dont elle est partie decide :
#  - code 0 : l'utilisateur l'a fermee. On arrete CE watch et le script.
#    La relancer la faisait revenir a chaque fermeture, sans fin (le bouton
#    « Lancer » d'Autowin, qui joue ce script, semblait lancer en boucle) ;
#  - autre code : plantage, ou arret force (watch tue l'application sur
#    « rude edit », taskkill /F rend 1). On relance CE watch (pid lance ici,
#    et lui seul) pour garder la reprise a chaud ;
#  - trois arrets de suite moins de 30 s apres le demarrage : elle plante au
#    lancement, la relancer ne la reparera pas. On s'arrete.
# Ctrl+C dans cette console arrete tout. Dans TOUS les cas, le serveur Vite
# lance plus haut (pid lance ici, et lui seul) est arrete en sortant.
function Get-AppRig { Get-Process -Name 'RigV3Desktop' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } }

$codeSortie = 0
try {
    $plantagesAuDemarrage = 0
    while ($true) {
        $watch = Start-Process dotnet -NoNewWindow -PassThru -ArgumentList @(
            'watch', 'run', '--project', "`"$PSScriptRoot\RigV3Desktop.csproj`"", '--non-interactive')
        $app = $null; $absente = 0
        while (-not $watch.HasExited) {
            Start-Sleep -Seconds 2
            $vivante = Get-AppRig | Select-Object -First 1
            if ($vivante) {
                if (-not $app -or $app.Id -ne $vivante.Id) {
                    # Le handle ouvert pendant que l'application vit garde son code de
                    # sortie lisible une fois partie : sans lui, ExitCode reste vide.
                    $app = $vivante; $null = $app.Handle
                }
                $absente = 0
            }
            elseif ($app) { $absente++ }
            # 3 sondes (~6 s) : laisse passer un redemarrage sur « rude edit ».
            if ($app -and $absente -ge 3) { break }
        }
        if (-not $app) { break }  # watch mort sans jamais lancer l'app : erreur de build, on s'arrete
        if (-not $watch.HasExited) {
            & taskkill /PID $watch.Id /T /F | Out-Null
            $watch.WaitForExit(10000) | Out-Null
        }
        if (-not $app.HasExited) { continue }  # watch tombe, l'application vit : on relance watch, comme avant
        if ($app.ExitCode -eq 0) {
            Write-Host 'Application fermee : fin du mode developpement.' -ForegroundColor Yellow
            break
        }
        if (($app.ExitTime - $app.StartTime).TotalSeconds -lt 30) { $plantagesAuDemarrage++ } else { $plantagesAuDemarrage = 0 }
        if ($plantagesAuDemarrage -ge 3) {
            Write-Host "L'application retombe des son demarrage (code $($app.ExitCode)) : arret. Corrige l'erreur puis relance." -ForegroundColor Red
            $codeSortie = 1
            break
        }
        Write-Host "Application arretee (code $($app.ExitCode)) : relance de dotnet watch..." -ForegroundColor Yellow
    }
}
finally {
    if (-not $vite.HasExited) { & taskkill /PID $vite.Id /T /F | Out-Null }
}
exit $codeSortie
