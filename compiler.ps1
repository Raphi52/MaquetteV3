# VERIFIER que le code compile, SANS toucher au binaire ni fermer l'application.
#
#   .\compiler.ps1
#
# Pourquoi ce fichier existe : `dotnet build` produit bin\Debug\...\RigV3Desktop.exe.
# Si l'application tourne, ce fichier est VERROUILLE par Windows et la copie echoue
# (MSB3021 / MSB3027). Le reflexe qui suit -- fermer l'application pour debloquer la
# compilation -- fait disparaitre la fenetre de l'utilisateur en plein travail.
#
# La cible `Compile` s'arrete AVANT l'edition de liens et la copie vers bin :
# le compilateur C# tourne, toutes les erreurs remontent, et l'exe n'est jamais
# touche. C'est donc la verification par defaut quand on veut seulement savoir
# « est-ce que ca compile ».
#
# Mesure du 2026-09-17 : application ouverte, `-t:Compile` rend 0 et la date de
# l'exe ne bouge pas ; une erreur de type introduite rend 1 avec son `error CS`.
#
# Pour produire REELLEMENT le binaire et le lancer, c'est lancer.ps1 (qui ferme
# l'instance en cours, volontairement). Pour developper en continu, dev.ps1.

$ErrorActionPreference = 'Stop'

# POURQUOI MarkupCompilePass1 EST DANS LA CHAINE (17/09/2026).
# Avec `-t:Compile` SEUL, la compilation rendait de faux
# `error CS0103: Le nom 'Vue' / 'InitializeComponent' n'existe pas` sur
# MainWindow / FenetreVue / FenetreDocument, plus un CS5001 « pas de methode
# Main ». Ce n'etait pas une casse du code : ce sont les cibles XAML qui
# AJOUTENT obj\...\*.g.cs a la liste des fichiers a compiler. `Compile` seul ne
# les voit pas, meme quand ces .g.cs existent deja sur le disque. Les enchainer
# dans UN SEUL appel msbuild traite la cause. L'exe n'est toujours PAS produit
# ni copie (ni Link ni CopyFilesToOutputDirectory) : l'application reste ouverte.
#
# POURQUOI PrepareResources ET NON MarkupCompilePass1 SEUL (18/11/2026).
# `Compile` ecrit obj\...\RigV3Desktop.dll, la DLL que toute compilation suivante
# recopie dans bin. Sans PrepareResources, elle sortait SANS RigV3Desktop.g.resources
# (les .baml des fenetres WPF) : plus recente que ses sources, elle etait ensuite
# jugee a jour et recopiee telle quelle par `dotnet build`, les bancs et le
# redemarrage de dotnet watch -- d'ou « Impossible de trouver la ressource
# 'fenetredocument.xaml' » (FenetreVueCheck, OngletsCheck rouges sans aucun
# changement de code). PrepareResources enchaine MarkupCompilePass1, Pass2 et la
# generation de g.resources : la DLL de obj est complete, et bin n'est pas touche.
#
# POURQUOI -restore (18/11/2026, passage en .NET 10).
# `dotnet msbuild` ne restaure PAS les paquets NuGet, contrairement a `dotnet build`.
# Sans restauration, obj\project.assets.json manque (copie neuve : NETSDK1004) ou
# decrit encore l'ancienne version de .NET (NETSDK1005 apres un changement de
# TargetFramework) et la compilation echoue avant le premier `error CS`.
# La restauration n'ecrit que dans obj\ : l'exe de bin reste intact.
& dotnet msbuild "$PSScriptRoot\RigV3Desktop.csproj" -restore "-t:ResolveReferences;PrepareResources;Compile" -v:m -nologo
$codeCs = $LASTEXITCODE

# POURQUOI tsc ICI (18/11/2026, ecrans React de web/).
# Vite construit le JavaScript SANS verifier les types : une erreur TypeScript
# partirait dans l'appli sans bruit. tsc --noEmit (mode strict, web/tsconfig.json)
# est donc la verification « est-ce que ca compile » du cote React, comme
# -t:Compile l'est du cote C#. Il n'ecrit rien : ni wwwroot/app, ni l'exe.
# Les DEUX passent toujours, pour voir d'un coup les error CS ET les error TS.
$web = Join-Path $PSScriptRoot 'web'
$codeTs = 0
$codePaquets = 0
if (Test-Path (Join-Path $web 'package.json')) {
    # PAQUETS A JOUR AVANT tsc (19/11/2026). Ils ne sont installes que par un
    # vrai build (cible InstallerWeb, accrochee a la copie vers bin), que
    # -t:Compile n'atteint pas : on appelle donc CETTE cible, seule definition
    # de la regle (marqueur plus ancien que package.json / package-lock.json =>
    # npm ci). Tester « tsc.cmd existe », comme avant, laissait des paquets
    # PERIMES : apres l'ajout de @types/node, TS2591 dans vite.config.ts.
    # Appel SEPARE de la compilation C# : un refus ici (paquets tenus par le
    # serveur Vite de dev.ps1) ne doit pas masquer les error CS.
    & dotnet msbuild "$PSScriptRoot\RigV3Desktop.csproj" -t:InstallerWeb -v:m -nologo
    $codePaquets = $LASTEXITCODE
    Push-Location $web
    try {
        & npm run --silent typecheck
        $codeTs = $LASTEXITCODE
    } finally { Pop-Location }
}

if ($codePaquets -ne 0) {
    # tsc a tourne quand meme, sur les paquets presents : son verdict reste
    # affiche, mais il porte sur des paquets qui ne sont peut-etre plus ceux
    # de package-lock.json. On le dit au lieu de rendre un vert muet.
    Write-Host "Paquets npm NON mis a jour (code $codePaquets) - voir le message ci-dessus ; la verification TypeScript a porte sur les paquets deja installes." -ForegroundColor Yellow
}
if ($codeCs -eq 0 -and $codeTs -eq 0 -and $codePaquets -eq 0) {
    Write-Host 'Compilation OK (C# et TypeScript) - le binaire n a pas ete touche, l application n a pas ete fermee.' -ForegroundColor Green
    exit 0
}
if ($codeCs -ne 0) {
    Write-Host "Compilation C# en ECHEC (code $codeCs) - voir les 'error CS' ci-dessus." -ForegroundColor Red
}
if ($codeTs -ne 0) {
    Write-Host "Verification TypeScript en ECHEC (code $codeTs) - voir les 'error TS' ci-dessus." -ForegroundColor Red
}
exit 1
