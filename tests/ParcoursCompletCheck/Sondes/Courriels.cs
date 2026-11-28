using RigV3.Contrats.Socle;
using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Courriels envoyés » (PROC_MAIL_LOT1, Pont/EcranCourriels.cs) : un écran SANS geste d'écriture dans RIG v2 -- OPE_LISTE_MAILS_LOT1 lit par
/// SqlRigConnectionReadOnly (OPE_LISTE_MAILS_LOT1.cs:13, :125, :153) et n'a que Rechercher, Effacer les filtres et le double-clic qui montre le
/// texte du courriel (:110-137). Aucun geste n'est inventé : comme une forme générée sans geste (tests/ParcoursCompletCheck/Program.cs:31-32),
/// la sonde prouve sa RECHERCHE (décision du chef du plan, voie A, lot 9 : Sondes/GestionAudiences.cs). Les critères de RIG v2
/// (_ConstruireRequete, :222-257 : période d'envoi du / au inclus, destinataire et modèle en « contient », les plus récents d'abord) appliqués
/// PAR LE SERVEUR (Serveur/Socle/Courriels/TracesCourriels.ListerAsync), joués par le pont au nom d'un agent qui a la fonction MAIL_LOT1, et
/// les lignes relues EXACTEMENT ; l'ouverture sans filtre montre les envois du jour (_ReinitialiserFiltres, :72-86) ; le texte du courriel
/// parti se relit par le détail (le double-clic, :110-137). La V3 trace aussi les échecs (CA03) : son filtre d'état en écarte un.
/// LE JUMEAU refusé : l'agent du banc, sans MAIL_LOT1, ne lit rien (403 du serveur). Lot 14 du plan « RIG v3 complet, mesuré par le banc du
/// parcours complet » (28/11/2026).
///
/// LE JEU : la fonction MAIL_LOT1 (« Mails envoyés en Lot 1 », DLL dans PROCESSUS_ET_FONCTIONALITE de RIG_DEV, lu le 28/11/2026) au catalogue
/// du greffe -- la lecture du banc la refusait au 28/11 (« le compte parcours.lecteur n'a pas la fonction MAIL_LOT1 ») --, donnée au rôle du
/// lecteur du banc (PARCOURS) et à un rôle de la sonde ; l'agent « sonde.courriels » ; dans soc.TraceEnvoi (Base/migrations/SOC-006_courriels.sql,
/// une ligne par étape) un envoi retenu, parti il y a deux jours, son courriel dans soc.CourrielEnvoye, et cinq JUMEAUX, chacun écarté par un
/// critère : parti avant la période, parti aujourd'hui (après elle), un autre destinataire, un autre modèle, un échec. Suivis 44000000-….
/// fix-ok: cause mesurée par le banc du parcours (28/11/2026, avant cette sonde) : « le compte parcours.lecteur n'a pas la fonction MAIL_LOT1 » ;
/// fichier neuf, retouché pour nommer ses constantes SujetEnvoye / CorpsEnvoye (un motif « { Sujet: Sujet } » se lit mal).
/// </summary>
public sealed class SondeCourriels : ISondeEcran
{
    private const string Compte = "sonde.courriels", Role = "SONDE14_MAIL_LOT1";
    private const string Fonction = RoutesCourriels.FonctionCourrielsEnvoyes;
    private static readonly Guid Retenu = new("44000000-0000-4000-8000-000000000001"), DuJour = new("44000000-0000-4000-8000-000000000003");
    private const string Destinataire = "maitre.sonde44@avocat-demo.fr", SujetEnvoye = "Convocation de la sonde 44";
    private const string CorpsEnvoye = "Maitre,|Vous trouverez ci-dessous la convocation de la sonde 44.|$PieceJointe$|Le greffe";

    public string Code => "PROC_MAIL_LOT1";

    public string? Jeu => $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = '{Fonction}')
            INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('{Fonction}', 'Mails envoyés en Lot 1', 'DLL');
        IF NOT EXISTS (SELECT 1 FROM soc.Role WHERE Nom = '{Role}') INSERT INTO soc.Role (Nom, Libelle) VALUES ('{Role}', 'Sonde du lot 14 : MAIL_LOT1');
        INSERT INTO soc.RoleFonction (Role, Fonction)
        SELECT r.Nom, '{Fonction}' FROM soc.Role AS r WHERE r.Nom IN ('PARCOURS', '{Role}')
          AND NOT EXISTS (SELECT 1 FROM soc.RoleFonction AS x WHERE x.Role = r.Nom AND x.Fonction = '{Fonction}');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT TOP 1 '{Compte}', 'COURRIELS', 'Sonde', 'SCOU14', 'M.', CodeGreffe, Id, 0 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT c.Id, r.Nom FROM soc.Compte AS c CROSS JOIN (VALUES ('BANC'), ('{Role}')) AS r (Nom)
        WHERE c.CompteWindows = '{Compte}';

        DECLARE @g varchar(20) = (SELECT TOP 1 CodeGreffe FROM soc.Service ORDER BY Id);
        DECLARE @parti datetime = DATEADD(MINUTE, 615, DATEADD(DAY, -2, CAST(CAST(@aujourdhui AS date) AS datetime)));
        INSERT INTO soc.CourrielEnvoye (Sujet, Corps, CorpsHtml, Expediteur, Reponse, Copie, Destinataires, DateEnvoi, Suivi, TypeModele)
        VALUES ('{SujetEnvoye}', '{CorpsEnvoye}', 0, 'judiciaire@greffe-sonde44.fr', NULL, NULL, '{Destinataire}', @parti, '{Retenu}', 'S44_CONVOC');
        DECLARE @courriel int = SCOPE_IDENTITY();
        -- Un envoi = ses étapes (EtapesCourriel), toutes au même numéro de suivi ; l'état se déduit des étapes (TracesCourriels.EtatSql).
        INSERT INTO soc.TraceEnvoi (DateCreation, CodeGreffe, Suivi, Application, Mode, Etape, IdCourriel, Expediteur, Destinataires, Sujet, EstHtml,
                                    NbPieces, Succes, ErreurType, ErreurMessage, UtilisateurWindows, TypeModele)
        SELECT DATEADD(SECOND, e.Rang, DATEADD(DAY, v.Jours, @parti)), @g, v.Suivi, 'RIGV3', 'MANUEL', e.Etape,
               CASE WHEN v.Suivi = '{Retenu}' AND e.Etape = '{EtapesCourriel.CourrielEnregistre}' THEN @courriel END,
               'judiciaire@greffe-sonde44.fr', v.Dest, '{SujetEnvoye}', 0, 1,
               CASE WHEN e.Etape = '{EtapesCourriel.EnvoiSucces}' THEN 1 WHEN e.Etape = '{EtapesCourriel.EnvoiErreur}' THEN 0 END,
               CASE WHEN e.Etape = '{EtapesCourriel.EnvoiErreur}' THEN 'System.Net.Mail.SmtpFailedRecipientException' END,
               CASE WHEN e.Etape = '{EtapesCourriel.EnvoiErreur}' THEN 'Mailbox unavailable : 5.1.1 adresse inconnue' END,
               '{Compte}', v.Modele
        FROM (VALUES
            -- retenu ; jumeaux : avant la période (J-5), après elle (aujourd'hui), autre destinataire, autre modèle, échec
            (CAST('{Retenu}' AS uniqueidentifier), 0, '{Destinataire}', 'S44_CONVOC', 0),
            (CAST('44000000-0000-4000-8000-000000000002' AS uniqueidentifier), -3, '{Destinataire}', 'S44_CONVOC', 0),
            (CAST('{DuJour}' AS uniqueidentifier), 2, '{Destinataire}', 'S44_CONVOC', 0),
            (CAST('44000000-0000-4000-8000-000000000004' AS uniqueidentifier), 0, 'autre.destinataire@etude-demo.fr', 'S44_CONVOC', 0),
            (CAST('44000000-0000-4000-8000-000000000005' AS uniqueidentifier), 0, '{Destinataire}', 'AUTRE_MODELE', 0),
            (CAST('44000000-0000-4000-8000-000000000006' AS uniqueidentifier), 0, '{Destinataire}', 'S44_CONVOC', 1)
        ) AS v (Suivi, Jours, Dest, Modele, Echec)
        CROSS APPLY (VALUES (0, '{EtapesCourriel.PreparationEmail}'), (1, '{EtapesCourriel.EnvoiDebut}'), (2, '{EtapesCourriel.AppelSmtp}'),
                            (3, CASE WHEN v.Echec = 1 THEN '{EtapesCourriel.EnvoiErreur}' ELSE '{EtapesCourriel.EnvoiSucces}' END),
                            (4, CASE WHEN v.Echec = 1 THEN NULL ELSE '{EtapesCourriel.CourrielEnregistre}' END)) AS e (Rang, Etape)
        WHERE e.Etape IS NOT NULL;
        """;

    private static string Jour(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var client = c.ClientDe(Compte);
        var parti = c.Jour.Date.AddDays(-2).AddMinutes(615);

        // Le jumeau sans droit : l'agent du banc n'a pas MAIL_LOT1 (la fonction de la procédure V2), il ne lit rien.
        var refus = await EcranCourriels.OuvrirAsync(c.Client, c.Greffe, null);
        if (refus is not { Mode: "refuse", Envois.Length: 0 } || !(refus.Message ?? "").Contains($"n'a pas la fonction {Fonction}", StringComparison.Ordinal))
            return new(false, false, $"jumeau refusé attendu (agent sans {Fonction}), rendu : {refus.Mode}, {refus.Envois.Length} envoi(s) « {refus.Message} »");

        // L'ouverture, sans filtre : les envois du jour (RIG v2 coche « du » à aujourd'hui), donc celui du jour et pas celui d'il y a deux jours.
        var ouverture = await EcranCourriels.OuvrirAsync(client, c.Greffe, null);
        if (ouverture.Mode != "serveur" || !ouverture.Envois.Any(e => e.Suivi == DuJour) || ouverture.Envois.Any(e => e.Suivi == Retenu))
            return new(false, false, $"ouverture (envois du jour) : {ouverture.Mode}, {ouverture.Envois.Length} envoi(s) « {ouverture.Message} » " +
                                     "(l'envoi du jour du jeu attendu, sans celui d'il y a deux jours)");

        var filtre = new FiltreCourriels(Jour(c.Jour.AddDays(-3)), Jour(c.Jour.AddDays(-1)), "sonde44@avocat", "CONVOC", EtatsCourriel.Envoye);
        var vue = await EcranCourriels.OuvrirAsync(client, c.Greffe, filtre);
        var e = vue.Envois.FirstOrDefault();
        var ligneOk = vue is { Mode: "serveur", Envois.Length: 1 } && e is
        {
            Etat: EtatsCourriel.Envoye, TypeModele: "S44_CONVOC", Expediteur: "judiciaire@greffe-sonde44.fr", Destinataires: Destinataire,
            Sujet: SujetEnvoye, NbPieces: 1, IdCourriel: not null, Erreur: null, Agent: Compte,
        } && e.Suivi == Retenu && e.Debut == parti;
        if (!ligneOk)
            return new(true, false, $"Rechercher (du, au, destinataire, modèle, état) : {vue.Mode} « {vue.Message} », " +
                                    string.Join(" ; ", vue.Envois.Select(x => $"{x.Suivi} {x.Debut:dd/MM/yyyy HH:mm} {x.Etat} {x.TypeModele} {x.Destinataires}")));

        var detail = await EcranCourriels.DetailAsync(client, c.Greffe, Retenu.ToString());
        var etapes = detail.Detail?.Etapes.Select(x => x.Etape).ToArray() ?? [];
        string[] attendues = [EtapesCourriel.PreparationEmail, EtapesCourriel.EnvoiDebut, EtapesCourriel.AppelSmtp, EtapesCourriel.EnvoiSucces,
                              EtapesCourriel.CourrielEnregistre];
        return detail is { Mode: "serveur", Detail: { Corps: CorpsEnvoye, CorpsHtml: false } } && etapes.SequenceEqual(attendues)
            ? new(true, true, "lecture seule (RIG v2 n'écrit rien dans PROC_MAIL_LOT1) : recherche V2 jouée par le pont, appliquée par le serveur, relue " +
                              "exactement -- période du / au, destinataire et modèle en « contient », état : 1 envoi, ses 5 jumeaux écartés ; ouverture sur " +
                              "les envois du jour ; le texte du courriel et ses 5 étapes relus par le détail ; jumeau refusé sans MAIL_LOT1")
            : new(true, false, $"Détail de l'envoi {Retenu} : {detail.Mode} « {detail.Message} », texte « {detail.Detail?.Corps} », étapes {string.Join(",", etapes)}");
    }
}
