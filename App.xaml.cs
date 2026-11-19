using System.Windows;

namespace RigV3Desktop;

/// <summary>
/// Point d'entree de RIG V3 en client lourd.
/// La fenetre est native (WPF) ; son contenu est une page React servie par un
/// controle WebView2 (Pont/PageWeb.cs), ce qui permet de reprendre la charte
/// graphique figee (wwwroot/rig-charte.css) sans la retraduire.
/// </summary>
public partial class App : Application
{
    /// <summary>Les commandes de la fenetre PRINCIPALE (deplacer, reduire,
    /// redimensionner...) : une seule pour l'application, partagee par la
    /// fenetre principale et les onglets sortis.</summary>
    public static FenetreInterop Fenetre { get; } = new();

    /// <summary>Passe-plat vers les fenetres detachees du document (VDossier, zone 3).</summary>
    public static DocumentPartage Partage { get; } = new();
}
