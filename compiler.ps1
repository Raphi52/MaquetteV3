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
& dotnet msbuild "$PSScriptRoot\RigV3Desktop.csproj" "-t:ResolveReferences;MarkupCompilePass1;Compile" -v:m -nologo
$code = $LASTEXITCODE

if ($code -eq 0) {
    Write-Host 'Compilation OK - le binaire n a pas ete touche, l application n a pas ete fermee.' -ForegroundColor Green
} else {
    Write-Host "Compilation en ECHEC (code $code) - voir les 'error CS' ci-dessus." -ForegroundColor Red
}

exit $code
