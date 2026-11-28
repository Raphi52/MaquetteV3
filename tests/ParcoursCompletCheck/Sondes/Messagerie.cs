using RigV3.Contrats.Socle;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Messagerie » (PROC_GESTEMAIL, Pont/EcranMessagerie.cs) : « Valider » un modèle de courriel modifié, le geste principal de l'écran RIG v2
/// « Modèle de mail » (OPE_EDIT_EMAILMODEL.cs:139-141, ValidationSauver ; écriture : Serveur/Socle/Courriels/GestionCourriels.ModifierModeleAsync),
/// joué par le pont (EcranMessagerie.EnregistrerModeleAsync) au nom d'un agent du greffe qui a la fonction GESTEMAIL, puis relu par le pont :
/// la liste des modèles porte le sujet saisi, et l'aperçu du modèle son texte. « Désactiver » / « Activer » (ActiverDesactiver, :202-205)
/// sont des gestes secondaires, non prouvés ici. LES JUMEAUX, refusés sans écriture (journal des gestes inchangé) : le même geste par l'agent
/// du banc, sans la fonction GESTEMAIL (403 du serveur) ; un sujet vide (MetierObligatoire de RIG v2, OPE_EDIT_EMAILMODEL.Designer.cs:103 :
/// « Le sujet est obligatoire. »). Lot 14 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : la fonction GESTEMAIL (« Gestion modèles emails », DLL dans PROCESSUS_ET_FONCTIONALITE de RIG_DEV, lu le 28/11/2026) au catalogue
/// du greffe si elle manque, donnée au rôle du lecteur du banc (PARCOURS) et à un rôle de la sonde ; l'agent « sonde.messagerie », admis
/// dans le greffe, agent du greffe (pas Amitel) ; un modèle texte S44_MODELE dans soc.ModeleMessage (table du socle, Base/migrations/SOC-006_courriels.sql).
/// </summary>
public sealed class SondeMessagerie : ISondeEcran
{
    private const string Compte = "sonde.messagerie", Role = "SONDE14_GESTEMAIL", Modele = "S44_MODELE";
    private const string SujetAvant = "Sujet avant la sonde 44", SujetApres = "Sujet apres la sonde 44";
    private const string CorpsApres = "Madame, Monsieur,|Le modele de la sonde 44 a ete enregistre.|$PieceJointe$|Le greffe";

    public string Code => "PROC_GESTEMAIL";

    public string? Jeu => $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = '{RoutesCourriels.FonctionGestion}')
            INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('{RoutesCourriels.FonctionGestion}', 'Gestion modèles emails', 'DLL');
        IF NOT EXISTS (SELECT 1 FROM soc.Role WHERE Nom = '{Role}') INSERT INTO soc.Role (Nom, Libelle) VALUES ('{Role}', 'Sonde du lot 14 : GESTEMAIL');
        INSERT INTO soc.RoleFonction (Role, Fonction)
        SELECT r.Nom, '{RoutesCourriels.FonctionGestion}' FROM soc.Role AS r WHERE r.Nom IN ('PARCOURS', '{Role}')
          AND NOT EXISTS (SELECT 1 FROM soc.RoleFonction AS x WHERE x.Role = r.Nom AND x.Fonction = '{RoutesCourriels.FonctionGestion}');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT TOP 1 '{Compte}', 'MESSAGERIE', 'Sonde', 'SMSG14', 'M.', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT c.Id, r.Nom FROM soc.Compte AS c CROSS JOIN (VALUES ('BANC'), ('{Role}')) AS r (Nom)
        WHERE c.CompteWindows = '{Compte}';
        INSERT INTO soc.ModeleMessage (Type, Sujet, Corps, CorpsHtml, Expediteur, Reponse, Copie, Lot1, Lot2, ReserveAmitel, LibelleEcran,
                                       Inscription, UtilisableEnSaisie, Comptabilite, DestinataireDefaut, EnvoiAutomatique, SignatureActive, Signature)
        VALUES ('{Modele}', '{SujetAvant}', 'Madame, Monsieur,|Avant la sonde 44.|$PieceJointe$|Le greffe', 0,
                'Greffe du tribunal de commerce <[ADRESSE_MAIL_JUDICIAIRE]>', '[ADRESSE_MAIL_NO_REPLY]', NULL, 0, 1, 0, 'Modele de la sonde 44',
                0, 1, 0, NULL, 0, 0, NULL);
        """;

    /// <summary>Le modèle tel que l'écran l'enregistre : ses champs du jeu, le sujet et le texte saisis.</summary>
    private static SaisieModele Saisie(string sujet) => new(Modele, "Modele de la sonde 44", sujet, CorpsApres,
        "Greffe du tribunal de commerce <[ADRESSE_MAIL_JUDICIAIRE]>", "[ADRESSE_MAIL_NO_REPLY]", null, null,
        Lot1: false, Lot2: true, Inscription: false, Comptabilite: false, EnvoiAutomatique: false, ReserveAmitel: false, SignatureActive: false, Signature: null);

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Compte);
        var avant = await EcranMessagerie.OuvrirAsync(client, c.Greffe);
        if (avant.Mode != "serveur" || !avant.PeutModifier || avant.EstAmitel)
            return new(false, false, $"la Messagerie au nom de {Compte} : {avant.Mode}, modifier {(avant.PeutModifier ? "permis" : "refusé")}, " +
                                     $"Amitel {avant.EstAmitel} « {avant.Message} » (agent du greffe avec GESTEMAIL attendu)");
        if (avant.Modeles.FirstOrDefault(m => m.Type == Modele) is not { Sujet: SujetAvant })
            return new(false, false, $"le modèle {Modele} du jeu n'est pas lu avec son sujet « {SujetAvant} » ({avant.Modeles.Length} modèle(s))");

        var journal = await c.Journal();
        var sansDroit = await EcranMessagerie.EnregistrerModeleAsync(c.Client, c.Greffe, Saisie(SujetApres), Modele);
        if (sansDroit.Ok || !sansDroit.Message.Contains($"n'a pas la fonction {RoutesCourriels.FonctionGestion}", StringComparison.Ordinal)
            || await c.Journal() != journal)
            return new(false, false, $"jumeau refusé attendu (agent sans GESTEMAIL), rendu : {(sansDroit.Ok ? "fait" : "refusé")} « {sansDroit.Message} »");
        var sansSujet = await EcranMessagerie.EnregistrerModeleAsync(client, c.Greffe, Saisie(" "), Modele);
        if (sansSujet.Ok || sansSujet.Message != "Le sujet est obligatoire." || await c.Journal() != journal)
            return new(false, false, $"jumeau refusé attendu (sujet vide), rendu : {(sansSujet.Ok ? "fait" : "refusé")} « {sansSujet.Message} »");

        var r = await EcranMessagerie.EnregistrerModeleAsync(client, c.Greffe, Saisie(SujetApres), Modele);
        if (!r.Ok) return new(false, false, $"Valider le modèle {Modele} : refusé « {r.Message} »");
        if (await c.Journal() == journal) return new(true, false, $"Valider le modèle {Modele} rendu fait sans trace au journal des gestes");

        var relue = await EcranMessagerie.OuvrirAsync(client, c.Greffe);
        var fiche = await EcranMessagerie.ModeleAsync(client, c.Greffe, Modele);
        var ligne = relue.Modeles.FirstOrDefault(m => m.Type == Modele);
        return relue.Mode == "serveur" && ligne is { Sujet: SujetApres, LibelleEcran: "Modele de la sonde 44", Lot2: true, UtilisableEnSaisie: true }
               && fiche is { Mode: "serveur", Apercu.Corps: CorpsApres }
            ? new(true, true, $"Valider le modèle fait (« {r.Message} »), relu par le pont : la liste des modèles porte le sujet « {SujetApres} », " +
                              "l'aperçu du modèle son nouveau texte ; jumeaux refusés : agent sans GESTEMAIL, sujet vide")
            : new(true, false, $"Valider le modèle rendu fait, mais relu {relue.Mode} « {ligne?.Sujet ?? "absent"} », aperçu {fiche.Mode} " +
                               $"« {fiche.Apercu?.Corps ?? fiche.Message} »");
    }
}
