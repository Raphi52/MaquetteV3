using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Fiches en conflit » (PROC_FICHES_CONFLIT, Pont/EcranConflits.cs) : « Traité » (REC-004 CA03 ; écran NEUF de la V3, la V2 n'a pas de
/// recopie), joué par le pont au nom de l'agent désigné (la fonction RECOPIE_ECARTS du greffe, RoutesConflits.FonctionSuivi) sur l'écart
/// de son jeu, puis relu par le pont : l'écart sort des « à traiter » et se lit dans « Traités » avec l'agent et la date ; le geste est au
/// journal (ModuleConflits.cs, « a valide »). Lot 11 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : un écart de recopie sur une fiche client (rec.Ecart et son champ, rec.EcartChamp : Base/migrations/REC-002_recopie.sql),
/// l'agent « sonde.conflits » admis dans le greffe, avec un rôle qui a RECOPIE_ECARTS.
/// </summary>
public sealed class SondeConflits : ISondeEcran
{
    private const string Compte = "sonde.conflits", Role = "SONDE11_ECARTS", IdV2 = "4100301";

    public string Code => "PROC_FICHES_CONFLIT";

    public string? Jeu => $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Role WHERE Nom = '{Role}') INSERT INTO soc.Role (Nom, Libelle) VALUES ('{Role}', 'Sonde du lot 11 : fiches en conflit');
        IF NOT EXISTS (SELECT 1 FROM soc.RoleFonction WHERE Role = '{Role}' AND Fonction = 'RECOPIE_ECARTS')
            INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('{Role}', 'RECOPIE_ECARTS');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT TOP 1 '{Compte}', 'CONFLITS', 'Sonde', 'SCONF11', 'M.', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT c.Id, r.Nom FROM soc.Compte AS c CROSS JOIN (VALUES ('BANC'), ('{Role}')) AS r (Nom)
        WHERE c.CompteWindows = '{Compte}';
        INSERT INTO rec.Ecart (BaseV2, TableV2, IdV2, TableV3, IdV3, Fiche, Cible, DetecteLe, DateV2, AuteurV2, DateV3, AuteurV3)
        VALUES ('RIG_SONDE_LOT11', 'CLIENT', '{IdV2}', 'rec.Client', '{IdV2}', N'client {IdV2} — SONDE FICHES EN CONFLIT', NULL,
                @aujourdhui, @aujourdhui, NULL, @aujourdhui, N'Sonde du lot 11');
        INSERT INTO rec.EcartChamp (IdEcart, Champ, ValeurV2, ValeurV3) VALUES (SCOPE_IDENTITY(), 'CLIEN_EMAIL', N'"v2@sonde.fr"', N'"v3@sonde.fr"');
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Compte);
        var avant = await EcranConflits.OuvrirAsync(client, c.Greffe);
        if (avant.Mode != "serveur") return new(false, false, $"Fiches en conflit au nom de {Compte} : {avant.Mode} « {avant.Message} »");
        if (avant.Ecarts.FirstOrDefault(e => e.IdV2 == IdV2) is not { } ecart)
            return new(false, false, $"l'écart {IdV2} du jeu n'est pas dans les « à traiter » ({avant.Ecarts.Length} écart(s))");
        if (!avant.PeutTraiter) return new(false, false, $"{Compte} n'a pas le bouton « Traité » (fonction RECOPIE_ECARTS)");

        var journal = await c.Journal();
        var vue = await EcranConflits.TraiterAsync(client, c.Greffe, ecart.Id, false, null);
        if (vue.Echec || vue.Mode != "serveur") return new(false, false, $"Traité : refusé « {vue.Message} »");
        if (await c.Journal() == journal) return new(true, false, $"Traité rendu fait (« {vue.Message} ») sans trace au journal des gestes");

        var traites = await EcranConflits.OuvrirAsync(client, c.Greffe, traites: true);
        var relu = traites.Ecarts.FirstOrDefault(e => e.Id == ecart.Id);
        return relu is { TraiteLe: not null, TraitePar.Length: > 0 } && vue.Ecarts.All(e => e.Id != ecart.Id)
            ? new(true, true, $"Traité fait (« {vue.Message} »), relu par le pont : l'écart « {ecart.Fiche} » sort des « à traiter » et se lit dans " +
                              $"« Traités », par {relu.TraitePar} le {relu.TraiteLe:dd/MM/yyyy HH:mm}")
            : new(true, false, $"Traité rendu fait, mais relu : {(relu is null ? "absent des « Traités »" : $"traité par « {relu.TraitePar} »")}, " +
                               $"{(vue.Ecarts.Any(e => e.Id == ecart.Id) ? "encore" : "plus")} dans les « à traiter »");
    }
}
