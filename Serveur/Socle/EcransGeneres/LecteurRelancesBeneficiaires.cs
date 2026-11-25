using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relances beneficiaires » (PROC_DBE_RELANCE) : les entreprises relancees pour n'avoir pas declare leurs
/// beneficiaires effectifs. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG). La fiche proc_dbe_relance.md (Brain, reference/proc) nomme la classe : PROC_RDBE
/// (D:\RigApplication\Source\RIG\DLL\Processus\SURVEILLANCES\PROC_SRBE\PROC_SRBE\PROC_DBE_RELANCE\PROC_RDBE.cs).
/// Relancer y appelle DossierRCS.CreerRelanceDBE (PROC_RDBE.cs:20 ; Outils\DLL\RigMetier\_Dossier\DossierRCS.cs:1798) :
/// une action du dossier, UNE diligence de nature « SDBE » (etat diligence « 1 », etat relance « 2 ») et sa relance n° 1
/// (lettre simple « S », date limite + 1 mois). Une ligne de l'ecran = une diligence SDBE, son dossier par l'action
/// (DLGRC_ID_ACTRC -> rec.ActionRegistre -> rec.DossierRcs, Serveur/Recopie/FichesV3.cs).
///
/// FILTRES. Nature SDBE. Une diligence passee a l'injonction (relance « IDBE » portant une instance :
/// DossierRCS.cs:1821 CreerRelanceInjonctionDBE), ou dont la relance est restee sans effet (delai expire, la condition
/// LecteurInjonctionsBeneficiaires.RelanceSansEffet), est a l'ecran « Injonctions beneficiaires » : la marche « Suivi »
/// du descripteur le dit (« Si la relance reste sans effet, l'injonction DBE prend la suite »), et la V2 n'a qu'une
/// relance SDBE (NATURE_DILIGENCE_PARAM SDBE / RSURV : seule la requete n° 1 existe, RIG_DEV). Les entreprises jamais
/// relancees (la V2 les cherche par la requete parametree NATURE_DILIGENCE_PARAM SDBE / RSURV / 1 : pas de depot ni de
/// formalite BEN, forme juridique, siege hors UE) ne sont pas encore reprises : elles n'ont pas de diligence.
///
/// GROUPE. « DBE non deposee » : la relance de la V2 vise une entreprise sans depot ni formalite de beneficiaires
/// (la requete RSURV / 1 ci-dessus). Rien dans l'ancien RIG ne distingue une declaration INCOMPLETE (motif de diligence
/// toujours « NP » non pertinent, MOTIF_DILIGENCE de RIG_DEV) : le groupe « DBE incomplete » du descripteur reste vide.
///
/// REGLE D'ETAT (DLGRC_ETAT_DILIGENCE et DLGRC_ETAT_RELANCE, valeurs de ETAT_DILIGENCE et ETAT_RELANCE de RIG_DEV) :
///   en cours, etat relance 1, 3 ou 5 (« A faire ... ») -> AFaire  « A relancer » : aucune marche ;
///   aucune relance envoyee (ni RELANCE_RCS, ni etat relance « ... faite » : FormeRelance.Envoyee)
///                                                    -> EnCours « En cours » : entreprise et declaration (0, 1) ;
///   relance envoyee                                  -> Validee « Relancee » : entreprise, declaration, courrier
///                                                       (0, 1, 2), plus le suivi (3) si la diligence est close
///                                                       (8 ou 9 : DiligenceRcs.cs:506-508 la clot regularisee).
/// Ordre : a relancer, en cours, relancees en cours, puis closes ; la relance la plus recente en tete.
/// </summary>
internal sealed class LecteurRelancesBeneficiaires : ILecteurEcran
{
    public string Procedure => "PROC_DBE_RELANCE";

    /// <summary>Le groupe du descripteur, au mot pres (« DBE incomplete » n'a pas de source : voir l'en-tete).</summary>
    internal const string NonDeposee = "DBE non déposée";

    private const string Requete = $"""
        SELECT TOP (@plafond)
               g.DLGRC_ETAT_DILIGENCE, g.DLGRC_ETAT_RELANCE, g.DLGRC_DATE_REGULARISATION,
               s.NumeroGestion, s.Siren, e.Denomination,
               der.RLCRC_NUM_ORDRE, der.RLCRC_TYPE_RELANCE, der.RLCRC_DATE_RELANCE, der.RLCRC_DATE_LIMITE,
               COUNT(*) OVER () AS Total
        FROM v2.DILIGENCE_RCS AS g
        JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
        JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        {FormeRelance.DerniereRelance}
        WHERE g.DLGRC_NATURE_DILIGENCE = 'SDBE'
          AND NOT EXISTS (SELECT 1 FROM v2.RELANCE_RCS AS i WHERE i.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC
                          AND i.RLCRC_TYPE_RELANCE = 'IDBE' AND i.RLCRC_ID_INSTN IS NOT NULL)
          AND NOT ({LecteurInjonctionsBeneficiaires.RelanceSansEffet})
        ORDER BY CASE WHEN g.DLGRC_ETAT_DILIGENCE = '1' AND g.DLGRC_ETAT_RELANCE IN ('1', '3', '5') THEN 0
                      WHEN NOT {FormeRelance.EnvoyeeSql} THEN 1
                      WHEN g.DLGRC_ETAT_DILIGENCE = '1' THEN 2
                      ELSE 3 END,
                 der.RLCRC_DATE_RELANCE DESC, g.DLGRC_ID_DLGRC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui });

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etatDiligence = FormeLigne.Texte(r, "DLGRC_ETAT_DILIGENCE");
        var etatRelance = FormeLigne.Texte(r, "DLGRC_ETAT_RELANCE");
        var (etat, faites) = Etat(etatDiligence, etatRelance, envoyee: FormeRelance.Envoyee(r, etatRelance));
        var suite = etatDiligence switch
        {
            "9" => $"régularisée le {FormeLigne.Date(FormeLigne.DateDe(r, "DLGRC_DATE_REGULARISATION"))}",
            "8" => "close sans régularisation",
            _ => null,
        };
        return new LigneLue(FormeRelance.NomDossier(r), FormeLigne.Joindre(FormeRelance.Relance(r, etatRelance, etat), suite),
                            NonDeposee, etat, faites);
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies.</summary>
    internal static (string Etat, int[] Faites) Etat(string etatDiligence, string etatRelance, bool envoyee)
    {
        if (etatDiligence == "1" && etatRelance is "1" or "3" or "5") return ("AFaire", []);
        if (!envoyee) return ("EnCours", [0, 1]);
        return ("Validee", etatDiligence is "8" or "9" ? [0, 1, 2, 3] : [0, 1, 2]);
    }
}
