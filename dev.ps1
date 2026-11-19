# Lance RIG V3 en mode developpement, avec reprise a chaud.
#
#   .\dev.ps1
#
# Rien a faire a la main :
#  - code C# et fichiers .razor : dotnet watch applique la modification dans
#    l'application qui tourne ;
#  - CSS / JS / images de wwwroot : la fenetre surveille elle-meme les sources
#    et recharge sa vue toute seule (BlazorWebView ne le fait pas -- limite
#    connue, dotnet/aspnetcore#37496). L'application ne redemarre pas.
#
# Si une modification est trop profonde pour etre appliquee a chaud
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

Write-Host 'RIG V3 - mode developpement (reprise a chaud).' -ForegroundColor Cyan
Write-Host 'C#, .razor, CSS, JS : repris automatiquement, sans redemarrage.' -ForegroundColor DarkGray

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
# Ctrl+C dans cette console arrete tout.
function Get-AppRig { Get-Process -Name 'RigV3Desktop' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } }

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
        exit 1
    }
    Write-Host "Application arretee (code $($app.ExitCode)) : relance de dotnet watch..." -ForegroundColor Yellow
}

