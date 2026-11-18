using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace RigV3Desktop;

/// <summary>
/// Point d'entree de RIG V3 en client lourd.
/// La fenetre est native (WPF) ; son contenu est rendu par une vue Blazor,
/// ce qui permet de reprendre la charte graphique figee (wwwroot/rig-charte.css)
/// sans la retraduire.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        services.AddSingleton<FenetreInterop>();
        // Passe-plat vers la fenetre detachee du document (VDossier, zone 3).
        services.AddSingleton<DocumentPartage>();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        Resources.Add("services", services.BuildServiceProvider());

        base.OnStartup(e);
    }
}
