using RigV3.Contrats.Numerotation;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Compteurs de numéros » (PROC_COMPTEURS_NUM, Pont/EcranCompteurs.cs) : « Rendre » un numéro donné par erreur, le geste de
/// l'écran (NUM-004 CA10 et T04 : la procédure XNUMERO de RIG v2, « Réattribution de numéro métier »), écrit par le serveur
/// (POST RoutesCompteur.Restitutions, programme num.Restituer de la base V3, Base\migrations\NUM-001_compteurs.sql), joué par le pont
/// puis relu par le pont : le compteur compte un numéro rendu de plus. Jumeau refusé : un numéro au-delà du dernier donné (« jamais
/// donné », code -4), le compteur ne bouge pas. Lot 13 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : la fonction XNUMERO (procédure de menu de RIG v2, rôles RCSNIV03/RCSNIV05 de RIG_DEV : RoutesCompteur.FonctionReattribution)
/// si le catalogue du banc ne l'a pas, un rôle qui la donne et l'agent de la sonde ; son propre compteur S13 (format ALC : année sur
/// 4, lettre S, chrono sur 5, dernier chrono 7, numéros rendus redonnés), pour ne toucher à aucun compteur qu'un autre jeu lit.
/// </summary>
public sealed class SondeCompteurs : ISondeEcran
{
    private const string Agent = "sonde.numeros13", Role = "SONDE13NUM", Serie = "S13";

    public string Code => "PROC_COMPTEURS_NUM";

    public string? Jeu => $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = '{RoutesCompteur.FonctionReattribution}')
            INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('{RoutesCompteur.FonctionReattribution}', 'Reattribution de numero metier', 'PROCVB6');
        INSERT INTO soc.Role (Nom, Libelle) VALUES ('{Role}', 'Sonde du parcours : réattribution de numéro');
        INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('{Role}', '{RoutesCompteur.FonctionReattribution}');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT TOP 1 '{Agent}', 'NUMEROS', 'Sonde', 'SNUM13', 'M.', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte WHERE CompteWindows = '{Agent}';
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, '{Role}' FROM soc.Compte WHERE CompteWindows = '{Agent}';
        INSERT INTO num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono, Debut, Chrono, GestionRendus)
        VALUES ('{Serie}', YEAR(@aujourdhui), 0, 'Sonde du parcours complet (lot 13)', 'ALC', 4, 'S', '', 5, 1, 7, 1);
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Agent);
        var annee = c.Jour.Year;
        var rendu = $"{annee}S00005";
        int? Rendus(VueEcranCompteurs v) => v.Compteurs.FirstOrDefault(x => x.Type == Serie && x.Annee == annee)?.NumerosRendus;

        var avant = await EcranCompteurs.OuvrirAsync(client, c.Greffe);
        if (avant.Mode != "serveur" || Rendus(avant) is not { } rendusAvant)
            return new(false, false, $"Compteurs ouverts par le pont : {avant.Mode} « {avant.Message} », compteur {Serie} {(Rendus(avant) is null ? "absent" : "lu")}");

        var vue = await EcranCompteurs.RendreAsync(client, c.Greffe, new DemandeRestitution(Serie, rendu, "Numéro donné par erreur : sonde du parcours complet"));
        if (vue.Mode != "serveur" || vue.Echec) return new(false, false, $"Rendre {rendu} : {vue.Mode} refusé « {vue.Message} »");

        var relu = await EcranCompteurs.OuvrirAsync(client, c.Greffe);
        var jamais = await EcranCompteurs.RendreAsync(client, c.Greffe, new DemandeRestitution(Serie, $"{annee}S00009", "Jumeau : jamais donné"));
        var apresJumeau = Rendus(await EcranCompteurs.OuvrirAsync(client, c.Greffe));
        return Rendus(relu) == rendusAvant + 1 && jamais.Echec && apresJumeau == rendusAvant + 1
            ? new(true, true, $"Rendre un numéro fait (« {vue.Message} »), relu par le pont : compteur {Serie} {annee}, {rendusAvant} → {Rendus(relu)} numéro(s) rendu(s) ; " +
                              $"{annee}S00009 refusé « {jamais.Message} », compteur inchangé")
            : new(true, false, $"Rendre {rendu} rendu fait, mais relu : {rendusAvant} → {Rendus(relu)} rendu(s) ; jumeau {(jamais.Echec ? "refusé" : "ACCEPTÉ")} « {jamais.Message} », puis {apresJumeau}");
    }
}
