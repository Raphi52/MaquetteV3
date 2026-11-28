using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Numéros manquants » (PROC_NUMEROS_MANQUANTS, Pont/EcranManquants.cs) : « Éditer la liste », l'étape ALNUMCM de la procédure LNUMCM de
/// RIG v2 (écriture : Serveur/Socle/EcransMain/EcritureManquants.cs), jouée par le pont au nom d'un agent qui a la fonction LNUMCM, après
/// la lecture de la liste par le même pont ; relue par contrôle SQL : la demande LNUMCM validée porte sa ligne d'édition ALNUMCM_1_IMP à
/// imprimer. Le jumeau refusé : le même geste par l'agent du banc, sans la fonction, rend le refus du serveur, rien d'écrit. Lot 11 du plan
/// « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : la fonction LNUMCM (« Liste des numéros chrono manquants », PROCVB6 dans PROCESSUS_ET_FONCTIONALITE de RIG_DEV) au catalogue du
/// greffe -- la lecture du banc la refusait au 28/11 (« le compte parcours.lecteur n'a pas la fonction LNUMCM ») --, donnée au rôle du lecteur
/// du banc (PARCOURS : le banc lui donne les fonctions du catalogue AVANT les jeux des sondes) et à un rôle de la sonde ; l'agent
/// « sonde.manquants », admis dans le greffe ; les tables de la demande et de la ligne d'édition, aux types de RIG_DEV.
/// LE TROU DU REGISTRE : un numéro RCS manquant repris de la V2 (F24/410001), au journal des numéros comme l'écrit la reprise
/// (Base/migrations/NUM-005_reprise.sql:731-734, motif « manquant constaté par la V2, motif inconnu ») ; sa jumelle F24/410002, manquante
/// puis rendue (évènement « restitution »), que la liste écarte (vue num.NumeroManquant, NUM-005_reprise.sql:235-246). La sonde lit la liste
/// avant et après le geste : le trou y est, la jumelle non.
/// fix-ok: sans trou au jeu, la sonde relisait « 0 numéro(s) » et l'écran « le registre est continu » (contrôle final du lot 11, capture
/// 06-numeros-manquants-editer-fait.png du 28/11/2026) : la liste éditée était vide, le geste ne prouvait rien sur les données.
/// </summary>
public sealed class SondeManquants : ISondeEcran
{
    private const string Compte = "sonde.manquants", Role = "SONDE11_LNUMCM", Trou = "F24/410001", Rendu = "F24/410002";

    public string Code => "PROC_NUMEROS_MANQUANTS";

    public string? Jeu => JeuSonde.EnDeuxTemps(JeuSonde.Demandes + JeuSonde.LignesEdition, $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = 'LNUMCM')
            INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('LNUMCM', 'Liste des numéros chrono manquants', 'PROCVB6');
        IF NOT EXISTS (SELECT 1 FROM soc.Role WHERE Nom = '{Role}') INSERT INTO soc.Role (Nom, Libelle) VALUES ('{Role}', 'Sonde du lot 11 : LNUMCM');
        INSERT INTO soc.RoleFonction (Role, Fonction)
        SELECT r.Nom, 'LNUMCM' FROM soc.Role AS r WHERE r.Nom IN ('PARCOURS', '{Role}')
          AND NOT EXISTS (SELECT 1 FROM soc.RoleFonction AS x WHERE x.Role = r.Nom AND x.Fonction = 'LNUMCM');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT TOP 1 '{Compte}', 'MANQUANTS', 'Sonde', 'SMANQ11', 'M.', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT c.Id, r.Nom FROM soc.Compte AS c CROSS JOIN (VALUES ('BANC'), ('{Role}')) AS r (Nom)
        WHERE c.CompteWindows = '{Compte}';
        INSERT INTO num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Numero, Code, Logiciel, Explication)
        VALUES ('manquant', 'RC1', 'RC1', 2024, 0, 410001, '{Trou}', 0, 'V2', N'manquant constaté par la V2, motif inconnu'),
               ('manquant', 'RC1', 'RC1', 2024, 0, 410002, '{Rendu}', 0, 'V2', N'manquant constaté par la V2, motif inconnu'),
               ('restitution', 'RC1', 'RC1', 2024, 0, 410002, '{Rendu}', 0, 'V3', N'rendu : le numéro a servi');
        """);

    /// <summary>Le trou du jeu est dans la liste lue par le pont, sa jumelle rendue non ; sinon ce qui manque.</summary>
    private static string? Ecart(VueEcranManquants liste)
    {
        var trou = liste.Manquants.Where(m => m.Numero == Trou).ToArray();
        if (trou is not [{ Serie: "RC1", Annee: 2024, Chrono: 410001, Logiciel: "V2" }])
            return $"le trou {Trou} du jeu n'est pas lu une fois, en RC1 2024 V2 ({trou.Length} ligne(s))";
        return liste.Manquants.Any(m => m.Numero == Rendu) ? $"la jumelle rendue {Rendu} est dans la liste" : null;
    }

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Compte);
        var liste = await EcranManquants.OuvrirAsync(client, c.Greffe);
        if (liste.Mode != "serveur") return new(false, false, $"la liste ne se lit pas au nom de {Compte} ({liste.Mode} « {liste.Message} »)");
        if (Ecart(liste) is { } avant) return new(false, false, $"liste lue avant le geste : {avant}");

        var journal = await c.Journal();
        var refus = await EcranManquants.CreerEditionAsync(c.Client, c.Greffe);
        if (refus.Fait || !refus.Message.Contains("n'a pas la fonction LNUMCM", StringComparison.Ordinal) || await c.Journal() != journal)
            return new(false, false, $"jumeau refusé attendu (agent sans LNUMCM), rendu : {(refus.Fait ? "fait" : "refusé")} « {refus.Message} »");

        var r = await EcranManquants.CreerEditionAsync(client, c.Greffe);
        if (!r.Fait || r.Ids is not [var id]) return new(false, false, $"Éditer la liste : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        if (await c.Journal() == journal) return new(true, false, "Éditer la liste rendu fait sans trace au journal des gestes");

        var relue = await c.Valeur($"""
            SELECT CONCAT(d.DMND_CODE_PROSS, '|', d.DMND_ETAT_DEMANDE, '|', l.LGEDT_CODE_MODELE_DOC, '|', l.LGEDT_TRANSMISSION, '|', l.LGEDT_ETAT_EDT)
            FROM v2.DEMANDE AS d JOIN v2.LIGNE_EDITION AS l ON l.LGEDT_ID_DMND = d.DMND_ID_DMND WHERE d.DMND_ID_DMND = {int.Parse(id)}
            """);
        var apres = await EcranManquants.OuvrirAsync(client, c.Greffe);
        var ecart = apres.Mode == "serveur" ? Ecart(apres) : $"liste {apres.Mode} « {apres.Message} »";
        return relue == "LNUMCM|V|ALNUMCM_1_IMP|IMP|I" && ecart is null
            ? new(true, true, $"Éditer la liste fait (« {r.Message} »), contrôle SQL : la demande LNUMCM validée porte ALNUMCM_1_IMP (IMP, à imprimer) ; " +
                              $"la liste relue par le pont avant et après ({apres.Manquants.Length} numéro(s)) porte le trou {Trou} du jeu, " +
                              $"sans sa jumelle rendue {Rendu} ; jumeau refusé sans la fonction LNUMCM")
            : new(true, false, $"Éditer la liste rendu fait (demande {id}), mais relu « {relue} », liste après le geste : {ecart}");
    }
}
