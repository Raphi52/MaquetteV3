using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RigV3.Lanceur;

// ============================================================
// L'ECRAN DU LANCEUR (SOC-012, « a maquetter » ; lot 12, 2026-11-21).
//
// Ce que l'agent voit quand RIG V3 ne peut pas s'ouvrir (version perimee, composant absent,
// base d'une autre version, serveur muet) ou quand une nouvelle version l'attend. Pas de
// WebView2 ici, volontairement : le message « composant WebView2 absent » doit pouvoir
// s'afficher sur un serveur qui ne l'a pas. La fenetre reprend donc la charte de la maquette
// en WPF natif (wwwroot/rig-charte.css, themes « clair » et « sombre ») : filet degrade de
// marque en tete, kicker en capitales, titre, texte, tableau des versions ou la ligne en
// cause porte la teinte d'alerte, boutons a filet d'or. Pas d'ecran V2 equivalent : la V2
// affiche une boite de dialogue simple (FORM_ACCUEIL.cs:3721, DialogBox.Show).
// ============================================================

/// <summary>Les teintes d'un theme, recopiees de wwwroot/rig-charte.css.</summary>
public sealed record PaletteLanceur(string Fond, string Surface, string Bord, string Texte, string Texte2, string Texte3,
                                    string Alerte, string Focus, string[] Marque)
{
    /// <summary>[data-rig-theme="clair"] (rig-charte.css:284-320).</summary>
    public static readonly PaletteLanceur Clair = new("#EEF0F4", "#FFFFFF", "#24192850", "#15171F", "#3A3F4E", "#5F6577",
                                                      "#C2410C", "#8A5E14", ["#14294A", "#2563EB", "#F5B912"]);

    /// <summary>:root / [data-rig-theme="sombre"] (rig-charte.css:175-268).</summary>
    public static readonly PaletteLanceur Sombre = new("#1E1E1E", "#282828", "#1AFFFFFF", "#ECECEC", "#B0B0B0", "#959595",
                                                       "#F5B912", "#D9B26A", ["#111111", "#D9B26A", "#F0D59A"]);

    /// <summary>
    /// Le theme de l'agent : celui de ses reglages (accueil.json, propriete « Theme » ; fichier de RIGV3_ETAT s'il est
    /// pose, sinon %APPDATA%\RigV3\accueil.json, comme AccueilEtat.Chemin), « sombre » -> sombre, tout autre -> clair.
    /// <paramref name="force"/> (« clair » / « sombre ») passe devant.
    /// </summary>
    public static PaletteLanceur DeLAgent(string? force)
    {
        var code = force;
        if (string.IsNullOrWhiteSpace(code))
        {
            var chemin = Environment.GetEnvironmentVariable("RIGV3_ETAT") is { Length: > 0 } etat
                ? etat
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RigV3", "accueil.json");
            try
            {
                if (File.Exists(chemin) && JsonDocument.Parse(File.ReadAllText(chemin)).RootElement.TryGetProperty("Theme", out var t))
                    code = t.GetString();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                code = null;   // reglages illisibles : le theme par defaut de RIG, le clair
            }
        }
        return code == "sombre" ? Sombre : Clair;
    }
}

public sealed class FenetreMessage : Window
{
    private bool reessayer;
    private readonly PaletteLanceur P;

    /// <param name="veille">Vrai : avertissement de la veille (un seul bouton, la fenetre ne bloque rien).</param>
    public FenetreMessage(Verdict verdict, PaletteLanceur palette, bool veille = false)
    {
        P = palette;
        Title = "RIG V3";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Pinceau(P.Fond);
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Topmost = veille;

        var corps = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
        corps.Children.Add(new TextBlock
        {
            Text = (veille ? "RIG V3 · MISE À JOUR" : "RIG V3 · LANCEMENT"),
            FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Pinceau(P.Texte3),
            Margin = new Thickness(0, 0, 0, 6), Tag = "kicker",
        });
        corps.Children.Add(new TextBlock
        {
            Text = verdict.Titre, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Pinceau(P.Texte),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });
        corps.Children.Add(new TextBlock
        {
            Text = verdict.Message, Foreground = Pinceau(P.Texte2), TextWrapping = TextWrapping.Wrap, LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 16),
        });
        corps.Children.Add(TableauVersions(verdict));

        var pied = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        var note = new TextBlock
        {
            Text = verdict.Remonter ? "L'exploitation d'Amitel est prévenue." : veille ? "Votre saisie en cours n'est pas touchée." : "",
            Foreground = Pinceau(P.Texte3), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(note, Dock.Left);
        pied.Children.Add(note);
        var fermer = Bouton(veille ? "J'ai compris" : "Fermer", fort: true);
        fermer.IsDefault = true;
        fermer.Click += (_, _) => Close();
        DockPanel.SetDock(fermer, Dock.Right);
        pied.Children.Add(fermer);
        if (verdict.Reessayer && !veille)
        {
            var encore = Bouton("Réessayer", fort: false);
            encore.Margin = new Thickness(0, 0, 10, 0);
            encore.Click += (_, _) => { reessayer = true; Close(); };
            DockPanel.SetDock(encore, Dock.Right);
            pied.Children.Add(encore);
        }
        corps.Children.Add(pied);

        var carte = new Border
        {
            Background = Pinceau(P.Surface), BorderBrush = Pinceau(P.Bord), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Margin = new Thickness(16), ClipToBounds = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.18, Color = Colors.Black },
            Child = new StackPanel { Children = { FiletMarque(), corps } },
        };
        Content = carte;
    }

    /// <summary>Montre le refus et rend vrai si l'agent demande un nouvel essai.</summary>
    public bool MontrerRefus()
    {
        ShowDialog();
        return reessayer;
    }

    private UIElement FiletMarque()
    {
        var degrade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        degrade.GradientStops.Add(new GradientStop(Couleur(P.Marque[0]), 0));
        degrade.GradientStops.Add(new GradientStop(Couleur(P.Marque[1]), 0.55));
        degrade.GradientStops.Add(new GradientStop(Couleur(P.Marque[2]), 1));
        return new Rectangle { Height = 6, Fill = degrade };
    }

    private UIElement TableauVersions(Verdict v)
    {
        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lignes = new List<(string Libelle, string Valeur, bool EnCause)>
        {
            ("RIG V3 sur ce serveur", v.VersionEcran ?? "non installé", v.Ecart == "ecran"),
            ("Partie serveur", v.VersionServeur ?? "non jointe", v.Ecart == "serveur"),
        };
        // Une base sans version lue n'est pas « vide » : le lanceur s'est arrete avant de la consulter.
        if (v.Greffe is not null) lignes.Add(($"Base du greffe {v.Greffe}", v.VersionBase ?? "non vérifiée", v.Ecart == "base"));
        if (v.Ecart == "composant") lignes.Add(("Composant WebView2", "absent", true));
        for (var i = 0; i < lignes.Count; i++)
        {
            grille.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (libelle, valeur, enCause) = lignes[i];
            var filet = new Border
            {
                BorderBrush = Pinceau(enCause ? P.Alerte : P.Bord), BorderThickness = new Thickness(enCause ? 3 : 1, 0, 0, 0),
                Padding = new Thickness(10, 5, 0, 5), Child = new TextBlock { Text = libelle, Foreground = Pinceau(P.Texte3) },
            };
            Grid.SetRow(filet, i);
            grille.Children.Add(filet);
            var texte = new TextBlock
            {
                Text = valeur, Foreground = Pinceau(enCause ? P.Alerte : P.Texte), VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"), FontWeight = enCause ? FontWeights.SemiBold : FontWeights.Normal,
            };
            Grid.SetRow(texte, i);
            Grid.SetColumn(texte, 1);
            grille.Children.Add(texte);
        }
        return grille;
    }

    private Button Bouton(string texte, bool fort)
    {
        var gabarit = new ControlTemplate(typeof(Button));
        var bord = new FrameworkElementFactory(typeof(Border));
        bord.SetValue(Border.BorderBrushProperty, Pinceau(P.Focus));
        bord.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bord.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bord.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bord.SetValue(Border.PaddingProperty, fort ? new Thickness(24, 8, 24, 8) : new Thickness(14, 6, 14, 6));
        var contenu = new FrameworkElementFactory(typeof(ContentPresenter));
        contenu.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        bord.AppendChild(contenu);
        gabarit.VisualTree = bord;
        return new Button
        {
            Content = texte, Template = gabarit, Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = Pinceau(fort ? P.Texte : P.Focus), FontSize = 14,
        };
    }

    private static Color Couleur(string hexa) => (Color)ColorConverter.ConvertFromString(hexa);
    private static SolidColorBrush Pinceau(string hexa) => new(Couleur(hexa));
}
