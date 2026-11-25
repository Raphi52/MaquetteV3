using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Comptes annuels » (PROC_DCA) : les demandes de depot des comptes annuels du greffe, au guichet et
/// dematerialisees. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : la fiche proc_dca.md (Brain, reference/proc) -- le plugin PROC_DCA traite UNE demande
/// (v2.DEMANDE) ; son mode (guichet / DCADEMAT) et son etat (« EtatDemande Qualifiee / Validee / Reclamee /
/// Refusee / Supprimee », regle coeur 1) pilotent l'ecran. Le code processus de la demande est DMND_CODE_PROSS :
/// « DCA » au guichet, « DCADEMAT » pour un depot dematerialise (RIG_DEV, lu le 24/11/2026 : 360 270 et 88 411
/// demandes). Le depot dematerialise a sa ligne v2.DEMAT_DCA (DMTDCA_ID_DMND) : origine du flux (INPI_DCA,
/// CREFO_DCA, CREFO...), millesime et cloture de l'exercice. Au guichet, l'exercice depose se lit par l'action de
/// la demande (DMND_ID_ACTRC -> rec.ActionRegistre.IdDepot -> v2.DEPOT_RCS.DPTRC_ID_EXRCC -> v2.EXERCICE).
/// Le dossier : DMND_ID_DSRCS -> rec.DossierRcs (fiche reprise de DOSSIER_RCS) -> rec.Entreprise (designation).
///
/// FILTRES : les deux codes ; une demande supprimee (etat X) n'est pas une ligne. Ordre : ce qui reste a faire
/// d'abord (arrivee, qualifiee, reservee), puis ce qui est commence, les reclamations, enfin ce qui est clos
/// (validee, terminee, refus) ; dans chaque bloc, la plus recente en tete.
///
/// REGLE D'ETAT (DMND_ETAT_DEMANDE, valeurs de v2.ETAT_DEMANDE -> etats du descripteur, FonctionsRetrouvees.cs) :
///   A Arrivee, Q Qualifiee, R Reservee  -> AFaire  « A enregistrer » : personne n'a encore saisi le depot ;
///                                          aucune marche franchie ;
///   I Interrompue, W En attente,        -> EnCours « En cours » : la saisie est commencee (la V2 reprend une
///   tout autre code (« S » vu sur RIG_DEV)  saisie interrompue, proc_dca.md « ChargementInterrompuFin ») ; le
///                                          dossier est retrouve (marche 0), l'exercice aussi s'il est connu (1) ;
///   N Reclamation                       -> EnAttente « Reclamation » : dossier, exercices, reclamation (0, 1, 4) ;
///   V Validee, T Terminee               -> Validee « Depose » : toutes les marches obligatoires (dossier,
///                                          exercices, facturation : 0, 1, 3), plus les pieces jointes (2) d'un
///                                          depot dematerialise, recues avec son flux ;
///   O Refus                             -> Rejetee « Refuse » : dossier, exercices, refus (0, 1, 4) ;
///   X Supprime                          -> pas de ligne.
/// </summary>
internal sealed class LecteurComptesAnnuels : ILecteurEcran
{
    public string Procedure => "PROC_DCA";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Guichet = "Guichet", Dematerialise = "Dématérialisé (INPI, CREFO)";

    private const string Requete = """
        SELECT TOP (@plafond)
               d.DMND_CODE_PROSS, d.DMND_ETAT_DEMANDE, d.DMND_NUM_DEMANDE, d.DMND_DATE_CREATION, d.DMND_DATE_VALIDATION,
               s.NumeroGestion, s.Siren, e.Denomination, x.EXRCC_DATE_CLOTURE,
               m.DMTDCA_ORIGINE, m.DMTDCA_EXERCICE_MILLESIME, m.DMTDCA_DATE_CLOTURE,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        LEFT JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        LEFT JOIN rec.ActionRegistre AS a ON a.Id = d.DMND_ID_ACTRC
        LEFT JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = a.IdDepot
        LEFT JOIN v2.EXERCICE AS x ON x.EXRCC_ID_EXRCC = p.DPTRC_ID_EXRCC
        OUTER APPLY (SELECT TOP (1) dm.DMTDCA_ORIGINE, dm.DMTDCA_EXERCICE_MILLESIME, dm.DMTDCA_DATE_CLOTURE
                     FROM v2.DEMAT_DCA AS dm WHERE dm.DMTDCA_ID_DMND = d.DMND_ID_DMND
                     ORDER BY dm.DMTDCA_ID_DMTDCA DESC) AS m
        WHERE d.DMND_CODE_PROSS IN ('DCA', 'DCADEMAT') AND d.DMND_ETAT_DEMANDE <> 'X'
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
        var demat = FormeLigne.Texte(r, "DMND_CODE_PROSS") == "DCADEMAT";
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var cloture = FormeLigne.DateDe(r, "EXRCC_DATE_CLOTURE") ?? FormeLigne.DateDe(r, "DMTDCA_DATE_CLOTURE");
        var millesime = FormeLigne.Texte(r, "DMTDCA_EXERCICE_MILLESIME");
        var (etat, faites) = Etat(etatV2, demat, exerciceConnu: cloture is not null || millesime.Length > 0);

        var creee = FormeLigne.DateDe(r, "DMND_DATE_CREATION");
        var close = FormeLigne.DateDe(r, "DMND_DATE_VALIDATION") ?? creee;
        var quand = etat switch
        {
            "Validee" => $"déposé le {FormeLigne.Date(close)}",
            "EnAttente" => $"réclamation du {FormeLigne.Date(close)}",
            "Rejetee" => $"refusé le {FormeLigne.Date(close)}",
            _ => creee is null ? "" : $"reçu le {FormeLigne.Date(creee)}",
        };
        var exercice = (millesime, cloture) switch
        {
            ({ Length: > 0 }, { } c) => $"Exercice {millesime} clos le {FormeLigne.Date(c)}",
            ({ Length: > 0 }, null) => $"Exercice {millesime}",
            (_, { } c) => $"Exercice clos le {FormeLigne.Date(c)}",
            _ => "",
        };
        var inconnu = etatV2 is "A" or "Q" or "R" or "I" or "W" or "N" or "V" or "T" or "O" ? "" : $"état « {etatV2} » de l'ancien RIG";

        return new LigneLue(Nom(r), FormeLigne.Joindre(demat ? Origine(FormeLigne.Texte(r, "DMTDCA_ORIGINE")) : null, exercice, quand, inconnu),
                            demat ? Dematerialise : Guichet, etat, faites);
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(string etatV2, bool demat, bool exerciceConnu) => etatV2 switch
    {
        "A" or "Q" or "R" => ("AFaire", []),
        "V" or "T" => ("Validee", demat ? [0, 1, 2, 3] : [0, 1, 3]),
        "N" => ("EnAttente", [0, 1, 4]),
        "O" => ("Rejetee", [0, 1, 4]),
        _ => ("EnCours", exerciceConnu ? [0, 1] : [0]),
    };

    /// <summary>La designation du dossier et son SIREN (sinon son numero de gestion) ; sans dossier, le numero de la demande.</summary>
    private static string Nom(SqlDataReader r)
    {
        var siren = FormeLigne.Siren(FormeLigne.Texte(r, "Siren"));
        var gestion = FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"));
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"),
                                     siren.Length > 0 ? $"SIREN {siren}" : gestion);
        return nom.Length > 0 ? nom : $"Demande n° {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}";
    }

    /// <summary>L'origine d'un depot dematerialise (DMTDCA_ORIGINE : INPI_DCA, CREFO_DCA, CREFO, CREFO_REFU, DEMAT_DCA...).</summary>
    private static string Origine(string origine)
        => origine.StartsWith("INPI", StringComparison.OrdinalIgnoreCase) ? "Flux INPI"
         : origine.StartsWith("CREFO", StringComparison.OrdinalIgnoreCase) ? "Flux CREFO"
         : "Dépôt dématérialisé";
}
