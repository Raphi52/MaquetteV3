using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonctions beneficiaires » (PROC_DBE_INJ) : les entreprises a enjoindre de declarer leurs beneficiaires effectifs
/// apres une relance restee sans effet, et les injonctions emises. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG). La fiche proc_dbe_inj.md (Brain, reference/proc) nomme la classe : PROC_IDBE
/// (D:\RigApplication\Source\RIG\DLL\Processus\SURVEILLANCES\PROC_SRBE\PROC_SRBE\PROC_DBE_INJ\PROC_IDBE.cs). Enjoindre y
/// cree une instance et, sur la diligence SDBE du dossier, une relance de plus portant cette instance
/// (PROC_IDBE.cs:71 -> Outils\DLL\RigMetier\_Dossier\DossierRCS.cs:1821 CreerRelanceInjonctionDBE : auteur « 4 » juge,
/// RLCRC_ID_INSTN = l'instance ; le type est celui de la nature, « IDBE » sur RIG_DEV). Deux sortes de lignes :
///   « Relance restee sans effet » : la diligence SDBE en cours (« 1 ») dont la 1re relance est faite (etat relance
///       « 2 ») et sa date limite passee, sans injonction : la jointure de la recherche « relance N » de la V2
///       (Outils\DLL\RigMetier\Surveillances\NatureDiligence.parametre.cs:784-797 : DLGRC_ETAT_DILIGENCE = 1,
///       DLGRC_ETAT_RELANCE = 2, RLCRC_NUM_ORDRE = 1, RLCRC_DATE_LIMITE avant le jour, compare a la date seule : DBDate),
///       dossier non radie (DSSRC_DATE_RADIATION IS NULL, requete NATURE_DILIGENCE_PARAM SDBE / ISURV / 1). Le reglage
///       IgnorerExistenceRelance (« 1 » pour SDBE sur RIG_DEV) laisse la V2 enjoindre sans relance prealable : non repris,
///       le descripteur exige une relance restee sans effet. Non repris non plus, les autres exclusions de cette requete
///       (societe cotee, forme juridique, siege hors UE, depot BEN en cours) : un depot BEN clot la diligence
///       (DiligenceRcs.cs:506-508), qui sort alors de la liste ;
///   « Injonction a suivre » : la diligence SDBE qui porte une injonction (relance IDBE avec instance), en cours ou close.
/// La condition « relance restee sans effet » est ecrite une fois (RelanceSansEffet) : LecteurRelancesBeneficiaires
/// ecarte les memes diligences, une relance n'est qu'a un ecran.
/// Le dossier : DLGRC_ID_ACTRC -> rec.ActionRegistre -> rec.DossierRcs (Serveur/Recopie/FichesV3.cs).
///
/// REGLE D'ETAT :
///   relance restee sans effet          -> AFaire  « A enjoindre » : aucune marche ;
///   injonction, diligence en cours (1) -> Validee « Injonction emise » : entreprise, relance, injonction (0, 1, 2) ;
///   injonction, diligence close (8, 9) -> Validee « Injonction emise » : et le suivi (3), clos.
/// L'etat « En cours » n'a pas de source : la V2 cree l'instance et la relance d'injonction d'un seul geste
/// (EcrivainInjonctionsBeneficiaires.cs, moteur InjonctionsIsurv.cs).
/// Ordre : a enjoindre, injonctions en cours, puis closes ; la plus recente en tete.
/// </summary>
internal sealed class LecteurInjonctionsBeneficiaires : ILecteurEcran
{
    public string Procedure => "PROC_DBE_INJ";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string SansEffet = "Relance restée sans effet", ASuivre = "Injonction à suivre";

    private const string Injonction =
        "i.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC AND i.RLCRC_TYPE_RELANCE = 'IDBE' AND i.RLCRC_ID_INSTN IS NOT NULL";

    /// <summary>La relance restee sans effet (en tete du fichier), sur la diligence g et son dossier s, au jour
    /// @aujourdhui. LecteurRelancesBeneficiaires l'ecarte avec la meme condition : une relance est a un seul ecran.</summary>
    internal const string RelanceSansEffet = $"""
        g.DLGRC_ETAT_DILIGENCE = '1' AND g.DLGRC_ETAT_RELANCE = '2' AND s.DateRadiation IS NULL
                AND EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS r1 WHERE r1.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC AND r1.RLCRC_NUM_ORDRE = 1
                            AND r1.RLCRC_DATE_LIMITE < CAST(@aujourdhui AS date))
                AND NOT EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS i WHERE {Injonction})
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) CAST(CASE WHEN j.RLCRC_DATE_RELANCE IS NULL THEN 0 ELSE 1 END AS bit) AS Injonction,
               g.DLGRC_ID_DLGRC, g.DLGRC_ETAT_DILIGENCE, g.DLGRC_DATE_REGULARISATION,
               COALESCE(j.RLCRC_DATE_RELANCE, r.RLCRC_DATE_RELANCE) AS DateRelance, r.RLCRC_DATE_LIMITE AS DateLimite,
               s.NumeroGestion, s.Siren, e.Denomination, COUNT(*) OVER () AS Total
        FROM v2.DILIGENCE_RCS AS g
        JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
        JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        OUTER APPLY (SELECT TOP (1) i.RLCRC_DATE_RELANCE FROM v2.RELANCE_RCS AS i WHERE {Injonction}
                     ORDER BY i.RLCRC_NUM_ORDRE DESC, i.RLCRC_ID_RLCRC DESC) AS j
        OUTER APPLY (SELECT TOP (1) rn.RLCRC_DATE_RELANCE, rn.RLCRC_DATE_LIMITE FROM v2.RELANCE_RCS AS rn
                     WHERE rn.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC AND rn.RLCRC_NUM_ORDRE = 1 ORDER BY rn.RLCRC_ID_RLCRC DESC) AS r
        WHERE g.DLGRC_NATURE_DILIGENCE = 'SDBE' AND (j.RLCRC_DATE_RELANCE IS NOT NULL OR ({RelanceSansEffet}))
        ORDER BY CASE WHEN j.RLCRC_DATE_RELANCE IS NULL THEN 0 WHEN g.DLGRC_ETAT_DILIGENCE = '1' THEN 1 ELSE 2 END,
                 DateRelance DESC, g.DLGRC_ID_DLGRC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui });

    private static LigneLue Ligne(SqlDataReader r)
    {
        var injonction = r["Injonction"] is true;
        var etatDiligence = FormeLigne.Texte(r, "DLGRC_ETAT_DILIGENCE");
        var (etat, faites) = Etat(injonction, etatDiligence);
        var date = FormeLigne.Date(FormeLigne.DateDe(r, "DateRelance"));
        var precisions = injonction
            ? FormeLigne.Joindre($"Injonction du {date}", etatDiligence switch
              {
                  "9" => $"régularisée le {FormeLigne.Date(FormeLigne.DateDe(r, "DLGRC_DATE_REGULARISATION"))}",
                  "8" => "close sans régularisation",
                  _ => null,
              })
            : $"Relance du {date} · délai expiré le {FormeLigne.Date(FormeLigne.DateDe(r, "DateLimite"))}";
        // Id = la diligence SDBE : l'injonction s'ecrit sur elle (EcrivainInjonctionsBeneficiaires, CreerRelanceInjonctionDBE).
        return new LigneLue(FormeRelance.NomDossier(r), precisions, injonction ? ASuivre : SansEffet, etat, faites)
            { Id = Convert.ToString(r["DLGRC_ID_DLGRC"], System.Globalization.CultureInfo.InvariantCulture) };
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(bool injonction, string etatDiligence)
        => !injonction ? ("AFaire", [])
         : etatDiligence is "8" or "9" ? ("Validee", [0, 1, 2, 3])
         : ("Validee", [0, 1, 2]);
}
