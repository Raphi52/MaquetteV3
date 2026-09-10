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
$exe = Join-Path $PSScriptRoot 'bin\Debug\net9.0-windows\RigV3Desktop.exe'
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

dotnet watch run --project "$PSScriptRoot\RigV3Desktop.csproj" --non-interactive
