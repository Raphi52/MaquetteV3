# Lance RIG V3 en mode developpement, avec reprise a chaud.
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
# PAQUETS A JOUR AVANT VITE (19/11/2026). La regle est la cible InstallerWeb
# du .csproj (marqueur plus ancien que package.json / package-lock.json =>
# npm ci). Elle DOIT passer ici, avant Vite : sinon c'est le premier build de
# dotnet watch qui lance npm ci, alors que Vite tient deja node_modules --
# Windows refuse d'effacer les fichiers charges, l'installation s'arrete a
# moitie (14 paquets sur 77) et aucune fenetre ne s'ouvre. Faite ici, la cible
# est a jour quand watch la rejoue : elle ne tourne plus.
& dotnet msbuild "$PSScriptRoot\RigV3Desktop.csproj" -t:InstallerWeb -v:m -nologo
if ($LASTEXITCODE -ne 0) { Write-Host 'Paquets npm non installes : le serveur Vite ne peut pas demarrer.' -ForegroundColor Red; exit 1 }
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
# On surveille donc l'application, et la FACON dont elle est partie decide.
# Son parent est le « dotnet run » de watch ; l'ordre des deux sorties dit
# qui a ferme (mesure du 19/11/2026, dotnet watch 10.0.402) :
#  - redemarrage par watch (« rude edit ») : watch tue « dotnet run » D'ABORD
#    (code -1), l'application suit ~40 ms apres avec le code 0. Watch la
#    relance lui-meme une fois recompilee : on attend, sans rien tuer ;
#  - fermeture par l'utilisateur : l'application part d'abord, code 0, et
#    « dotnet run » la suit. On arrete CE watch et le script. La relancer la
#    faisait revenir a chaque fermeture, sans fin (le bouton « Lancer »
#    d'Autowin, qui joue ce script, semblait lancer en boucle). Le code 0 seul
#    ne suffit pas : un redemarrage par watch rend 0 aussi ;
#  - autre code, application partie d'abord : plantage ou arret force. On
#    relance CE watch (pid lance ici, et lui seul) pour garder la reprise a
#    chaud ;
#  - trois arrets de suite moins de 30 s apres le demarrage : elle plante au
#    lancement, la relancer ne la reparera pas. On s'arrete.
# Ctrl+C dans cette console arrete tout. Dans TOUS les cas, le serveur Vite
# lance plus haut (pid lance ici, et lui seul) est arrete en sortant.
function Get-AppRig { Get-Process -Name 'RigV3Desktop' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } }
function Get-ParentRig($app) {
    $idParent = (Get-CimInstance Win32_Process -Filter "ProcessId=$($app.Id)" -ErrorAction SilentlyContinue).ParentProcessId
    if (-not $idParent) { return $null }
    $parent = Get-Process -Id $idParent -ErrorAction SilentlyContinue
    if ($parent) { $null = $parent.Handle }
    $parent
}

$codeSortie = 0
try {
    $plantagesAuDemarrage = 0
    while ($true) {
        $watch = Start-Process dotnet -NoNewWindow -PassThru -ArgumentList @(
            'watch', 'run', '--project', "`"$PSScriptRoot\RigV3Desktop.csproj`"", '--non-interactive')
        $app = $null; $parent = $null; $vue = $false; $absente = 0
        while (-not $watch.HasExited) {
            Start-Sleep -Seconds 2
            $vivante = Get-AppRig | Select-Object -First 1
            if ($vivante) {
                if (-not $app -or $app.Id -ne $vivante.Id) {
                    # Les handles ouverts pendant que les processus vivent gardent leur
                    # code et leur heure de sortie lisibles : sans eux, ils restent vides.
                    $app = $vivante; $null = $app.Handle
                    $parent = Get-ParentRig $app
                }
                $vue = $true; $absente = 0
                continue
            }
            if (-not $app) { continue }
            $absente++
            # « dotnet run » suit l'application de quelques ms : on l'attend ~6 s au plus.
            if ($parent -and -not $parent.HasExited -and $absente -lt 3) { continue }
            if ($parent -and $parent.HasExited -and $parent.ExitTime -le $app.ExitTime) {
                Write-Host 'Redemarrage par dotnet watch : la fenetre revient apres la compilation.' -ForegroundColor DarkGray
                $app = $null; $parent = $null; $absente = 0
                continue
            }
            break
        }
        if (-not $vue) { break }  # watch mort sans jamais lancer l'app : erreur de build, on s'arrete
        if (-not $watch.HasExited) {
            & taskkill /PID $watch.Id /T /F | Out-Null
            $watch.WaitForExit(10000) | Out-Null
        }
        if (-not $app -or -not $app.HasExited) { continue }  # watch tombe seul : on le relance, comme avant
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
