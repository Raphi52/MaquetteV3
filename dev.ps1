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
# On surveille donc l'application : si elle disparait alors que watch
# tourne encore, on arrete CE watch (pid lance ici, et lui seul) et on le
# relance. Ctrl+C dans cette console arrete tout.
function Get-AppRig { Get-Process -Name 'RigV3Desktop' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } }

while ($true) {
    $watch = Start-Process dotnet -NoNewWindow -PassThru -ArgumentList @(
        'watch', 'run', '--project', "`"$PSScriptRoot\RigV3Desktop.csproj`"", '--non-interactive')
    $vue = $false; $absente = 0
    while (-not $watch.HasExited) {
        Start-Sleep -Seconds 2
        if (Get-AppRig) { $vue = $true; $absente = 0 }
        elseif ($vue) { $absente++ }
        # 3 sondes (~6 s) : laisse passer un redemarrage sur « rude edit ».
        if ($vue -and $absente -ge 3) {
            Write-Host 'Application fermee : relance de dotnet watch...' -ForegroundColor Yellow
            & taskkill /PID $watch.Id /T /F | Out-Null
            $watch.WaitForExit(10000) | Out-Null
            break
        }
    }
    if ($watch.HasExited -and -not $vue) { break }  # watch mort sans jamais lancer l'app : erreur de build, on s'arrete
}

