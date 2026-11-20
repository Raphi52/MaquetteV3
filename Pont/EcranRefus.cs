using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>
/// La racine de la fenetre principale quand l'agent n'est PAS admis (SOC-001 CA03 a CA06) :
/// l'ecran de refus (web/src/composants/EcranRefus.tsx) A LA PLACE de l'application. Le plan de
/// travail n'est jamais ouvert : aucune donnee n'est lue ni montree. Comme la boite de la V2
/// (RigConsoleAccueil.cs:113-133) qui ferme RIG apres lecture, l'ecran n'offre que de fermer.
/// </summary>
public sealed class RacineRefus(IdentiteAgent refus, FenetreInterop? fenetre) : RacineWeb
{
    /// <summary>Le theme de l'agent (accueil.json), pour que le refus ne l'eblouisse pas.</summary>
    private readonly string Theme = AccueilEtat.Charger().Theme;

    public override string Composant => "EcranRefus";

    public override string[] Rappels => ["fermer", "deplacer"];

    public override IReadOnlyDictionary<string, object?> Proprietes()
        => new Dictionary<string, object?> { ["refus"] = refus, ["theme"] = Theme };

    public override void Ouvrir() => fenetre?.AppliquerFond(Theme);

    public override Task Rappeler(string nom, JsonElement valeur)
    {
        switch (nom)
        {
            case "fermer": fenetre?.Fermer(); break;
            case "deplacer": fenetre?.Deplacer(); break;
            default: return base.Rappeler(nom, valeur);
        }
        return Task.CompletedTask;
    }
}
