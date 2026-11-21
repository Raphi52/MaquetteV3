using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Un greffe que l'ecran de choix propose.</summary>
public sealed record GreffePropose(string Code, string Nom);

/// <summary>Un greffe du poste que l'ecran de choix ne propose pas, et pourquoi (en clair).</summary>
public sealed record GreffeNonPropose(string Code, string Raison);

/// <summary>
/// La racine de la fenetre principale quand plusieurs greffes du poste s'ouvrent pour l'agent (lot 10 du plan
/// de reconstruction) : l'ecran de choix (web/src/composants/ChoixGreffe.tsx), le pendant de la fenetre de
/// connexion de la V2 (RigFormLogin, RigFormLogin.cs:282-319). Le greffe choisi, l'agent y est juge par le
/// serveur (<paramref name="ouvrir"/>, Pont/SessionAgent.cs) ; cette MEME racine devient alors l'application
/// (admis) ou l'ecran de refus : la page n'est pas rechargee, elle recoit la nouvelle racine comme apres
/// n'importe quel geste (Pont/Repartiteur.cs). « Fermer RIG », l'« Annuler » de la V2, ferme sans rien ouvrir.
/// </summary>
public sealed class RacineChoixGreffe(OuvertureSession ouverture, Func<GreffeDuPoste, Task<IdentiteAgent>> ouvrir,
                                      RacineWeb application, FenetreInterop? fenetre) : RacineWeb
{
    /// <summary>Le theme de l'agent (accueil.json), comme l'ecran de refus.</summary>
    private readonly string Theme = AccueilEtat.Charger().Theme;

    /// <summary>Un choix est en train de s'ouvrir : un second clic (ou Entree repetee) n'ouvre rien de plus.</summary>
    private bool EnCours;

    /// <summary>Ce qui suit le choix : l'application ou l'ecran de refus ; null tant que l'agent n'a pas choisi.</summary>
    public RacineWeb? Suite { get; private set; }

    public override string Composant => Suite?.Composant ?? "ChoixGreffe";

    public override string[] Rappels => Suite?.Rappels ?? ["choisir", "fermer", "deplacer"];

    public override bool Bords => Suite?.Bords ?? false;

    public override IReadOnlyDictionary<string, object?> Proprietes()
        => Suite?.Proprietes() ?? new Dictionary<string, object?>
        {
            ["compte"] = ouverture.Compte,
            ["greffes"] = ouverture.AChoisir.Select(g => new GreffePropose(g.Code, g.Nom ?? "")).ToArray(),
            ["nonProposes"] = ouverture.NonProposes.Select(g => new GreffeNonPropose(g.Code, SessionAgent.Raison(g))).ToArray(),
            ["theme"] = Theme,
        };

    // Le repartiteur ouvre la racine une seule fois, au premier chargement de la page : avant tout choix.
    public override void Ouvrir() => fenetre?.AppliquerFond(Theme);

    public override async Task Rappeler(string nom, JsonElement valeur)
    {
        if (Suite is not null)
        {
            await Suite.Rappeler(nom, valeur);
            return;
        }
        switch (nom)
        {
            case "choisir": await ChoisirAsync(valeur.ValueKind == JsonValueKind.String ? valeur.GetString() : null); break;
            case "fermer": fenetre?.Fermer(); break;
            case "deplacer": fenetre?.Deplacer(); break;
            default: await base.Rappeler(nom, valeur); break;
        }
    }

    /// <summary>Ouvre le greffe <paramref name="code"/>, s'il est l'un de ceux proposes ; un autre code n'ouvre rien.</summary>
    private async Task ChoisirAsync(string? code)
    {
        if (EnCours || ouverture.AChoisir.FirstOrDefault(g => string.Equals(g.Code, code, StringComparison.OrdinalIgnoreCase))
                is not { } greffe) return;
        EnCours = true;
        IdentiteAgent identite;
        try { identite = await ouvrir(greffe); }
        catch
        {
            EnCours = false;   // l'erreur part a la page (Repartiteur) ; l'agent peut choisir de nouveau
            throw;
        }
        RacineWeb suite = identite.Admis ? application : new RacineRefus(identite, fenetre);
        // Branchee comme le repartiteur branche sa racine : ses changements passent par celle-ci.
        suite.Brancher(Planifier);
        suite.Change += Changer;
        Suite = suite;
        suite.Ouvrir();
        // Demarree apres son premier rendu, comme une racine ouverte au chargement (Repartiteur.Ouvrir).
        Planifier(suite.Demarrer);
    }

    protected override async ValueTask SurFermeture()
    {
        if (Suite is null) return;
        Suite.Change -= Changer;
        await Suite.DisposeAsync();
    }
}
