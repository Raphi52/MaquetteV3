using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonctions comptes annuels » (PROC_DCA_INJ) : les depots de comptes annuels recus depuis une injonction de
/// deposer. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) -- deux lectures, reunies ici :
///   - la fiche proc_dca_inj.md (Brain, reference/proc), dont le descripteur reprend les groupes et les etats, decrit
///     le traitement d'un DEPOT de comptes (demande DCA, ou DCADEMAT dematerialisee) : reception, exercices, facturation,
///     reclamation ou refus, validation ;
///   - le code que la meme fiche nomme : PROC_INJDCA (D:\RigApplication\Source\RIG\DLL\Processus\SURVEILLANCES\PROC_SDCA\
///     PROC_DCA_INJ\PROC_INJDCA.cs), titre « Injonctions de depot des comptes annuels » (FORM_INJDCA.cs:27). Enjoindre y
///     cree une instance et, sur la diligence SDCA de l'exercice, une relance « IDCA » qui porte cette instance
///     (PROC_INJDCA.cs:61 CreerRelanceInjonctionDCA). Un exercice est sous injonction quand sa diligence a une relance
///     IDCA avec instance (PROC_DCA.cs:2092 HasOneInjonction) ; valider le depot de cet exercice regularise l'injonction
///     (PROC_DCA\ETP_EXERCICES\ETP_EXERCICES.cs:132-135, demande SURV_REGUL).
/// Une ligne = une demande de depot DCA ou DCADEMAT (v2.DEMANDE) d'un dossier sous injonction DCA, creee le jour de
/// l'injonction ou apres, et au plus tard a la regularisation de sa diligence (DLGRC_DATE_REGULARISATION, posee quand
/// le depot valide clot la diligence : DiligenceRcs.cs:506-508) : les depots des exercices suivants ne repondent plus
/// a l'injonction (RIG_DEV, 25/11/2026 : 9 679 demandes avec cette borne, 29 496 sans). Le dossier sous injonction : relance IDCA avec instance -> diligence SDCA (RLCRC_ID_DLGRC) ->
/// action (DLGRC_ID_ACTRC -> rec.ActionRegistre) -> rec.DossierRcs = DMND_ID_DSRCS. Une demande supprimee (X) n'est pas
/// une ligne (comme PROC_DCA, LecteurComptesAnnuels.cs). Les injonctions qui attendent encore leur depot n'ont pas de
/// groupe dans le descripteur : elles ne sont pas listees.
///
/// GROUPE : reclamation (N) ou refus (O) -> « Reclamations et refus » ; sinon DCADEMAT -> « Depots dematerialises »,
/// DCA -> « Depots papier ».
/// REGLE D'ETAT (DMND_ETAT_DEMANDE, valeurs de v2.ETAT_DEMANDE, lues comme LecteurComptesAnnuels.cs) :
///   A Arrivee, Q Qualifiee, R Reservee -> AFaire  « A traiter » : aucune marche ;
///   N Reclamation                      -> EnCours « En cours » : dossier, exercices, reclamation (0, 1, 3) ;
///   V Validee, T Terminee              -> Validee « Deposee » : dossier, exercices, facturation, validation (0, 1, 2, 4) ;
///   O Refus                            -> Rejetee « Refusee » : dossier, exercices, refus (0, 1, 3) ;
///   tout autre (I, W, ...)             -> EnCours « En cours » : le dossier est charge (0).
/// Ordre : a traiter, en cours, reclamations, puis closes ; la plus recente en tete.
/// </summary>
internal sealed class LecteurInjonctionsComptesAnnuels : ILecteurEcran
{
    public string Procedure => "PROC_DCA_INJ";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Dematerialises = "Dépôts dématérialisés", Papier = "Dépôts papier", ReclamationsRefus = "Réclamations et refus";

    private const string Requete = """
        SELECT TOP (@plafond)
               d.DMND_CODE_PROSS, d.DMND_ETAT_DEMANDE, d.DMND_DATE_CREATION, d.DMND_DATE_VALIDATION,
               s.NumeroGestion, s.Siren, e.Denomination, inj.DateInjonction,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        CROSS APPLY (SELECT MAX(r.RLCRC_DATE_RELANCE) AS DateInjonction
                     FROM rec.ActionRegistre AS a
                     JOIN v2.DILIGENCE_RCS AS g ON g.DLGRC_ID_ACTRC = a.Id AND g.DLGRC_NATURE_DILIGENCE = 'SDCA'
                     JOIN v2.RELANCE_RCS AS r ON r.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC AND r.RLCRC_TYPE_RELANCE = 'IDCA'
                                              AND r.RLCRC_ID_INSTN IS NOT NULL
                     WHERE a.IdDossier = s.Id AND r.RLCRC_DATE_RELANCE <= d.DMND_DATE_CREATION
                       AND (g.DLGRC_DATE_REGULARISATION IS NULL OR d.DMND_DATE_CREATION <= g.DLGRC_DATE_REGULARISATION)) AS inj
        WHERE d.DMND_CODE_PROSS IN ('DCA', 'DCADEMAT') AND d.DMND_ETAT_DEMANDE <> 'X' AND inj.DateInjonction IS NOT NULL
        ORDER BY CASE WHEN d.DMND_ETAT_DEMANDE IN ('A', 'Q', 'R') THEN 0
                      WHEN d.DMND_ETAT_DEMANDE = 'N' THEN 2
                      WHEN d.DMND_ETAT_DEMANDE IN ('V', 'T', 'O') THEN 3
                      ELSE 1 END,
                 d.DMND_DATE_CREATION DESC, d.DMND_ID_DMND DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var (etat, faites) = Etat(etatV2);
        var groupe = etatV2 is "N" or "O" ? ReclamationsRefus
                   : FormeLigne.Texte(r, "DMND_CODE_PROSS") == "DCADEMAT" ? Dematerialises : Papier;
        var creee = FormeLigne.DateDe(r, "DMND_DATE_CREATION");
        var close = FormeLigne.DateDe(r, "DMND_DATE_VALIDATION") ?? creee;
        var quand = etatV2 switch
        {
            "V" or "T" => $"déposé le {FormeLigne.Date(close)}",
            "N" => $"réclamation du {FormeLigne.Date(close)}",
            "O" => $"refusé le {FormeLigne.Date(close)}",
            _ => $"reçu le {FormeLigne.Date(creee)}",
        };
        var inconnu = etatV2 is "A" or "Q" or "R" or "I" or "W" or "N" or "V" or "T" or "O" ? null : $"état « {etatV2} » de l'ancien RIG";
        return new LigneLue(FormeRelance.NomDossier(r),
                            FormeLigne.Joindre($"Injonction du {FormeLigne.Date(FormeLigne.DateDe(r, "DateInjonction"))}", quand, inconnu),
                            groupe, etat, faites);
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(string etatV2) => etatV2 switch
    {
        "A" or "Q" or "R" => ("AFaire", []),
        "N" => ("EnCours", [0, 1, 3]),
        "V" or "T" => ("Validee", [0, 1, 2, 4]),
        "O" => ("Rejetee", [0, 1, 3]),
        _ => ("EnCours", [0]),
    };
}
