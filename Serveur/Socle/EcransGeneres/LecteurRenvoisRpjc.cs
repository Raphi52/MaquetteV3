using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPJC renvois » (PROC_EDI_RPJC_REN) : les demandes de renvoi d'audience des avocats recues par le flux RPJC.
/// Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDI_RPJC_REN, sur la base du greffe.
///   - la liste (ETP_RECHERCHE\OPE_RECHERCHE.cs:98-131) : DEMAT_RPJC_REN, deux cases -- « En attente » (RPJCR_ETAT = '1',
///     cochee a l'ouverture) OU « Traites » (RPJCR_ETAT <> '1' et audience initiale RPJCR_DATE_AUDIENCE_INITIALE du ... au
///     ..., pre-remplie du jour moins un mois au jour, l. 38-39 ; borne basse a 0 h, borne haute a 24 h) ; ORDER BY
///     RPJCR_ID_RPJCR DESC, 200 lignes au plus (l. 41) ;
///   - le bouton « REJET DOCAPOST » (FORM_EDI_RPJC_REN.cs:22-37, Rejet Extelia\OPE_REJET_EXTELIA.cs:101) : toutes les
///     demandes RPJCR_ETAT = '9', sans date ;
///   - chaque ligne (ETP_RESULTAT_RECHERCHE\OPE_RESULTAT_RECHERCHE.cs:289-356) : le nom de l'affaire (INSTANCE.INSTN_NOM_COURANT
///     par INSTN_NUM_ROLE = RPJCR_NUMERO_AFFAIRE, la premiere) et l'avocat retrouve par son CNBF (MANDATAIRE.MNDTR_NUMERO_CNBF,
///     l'actif d'abord : Outils\DLL\RigMetier\JUDICIAIRE\Mandataire.cs:276-277), « Non trouvé » sinon.
/// L'ecran V3 a les trois groupes du descripteur (A traiter, Traitees, Rejets Extelia) : il lit la liste des deux cases
/// cochees, aux dates pre-remplies de la V2 (le mois ecoule, @aujourdhui), plus tous les rejets Extelia du bouton.
/// Ecart assume : l'ordre -- ce qui attend une action d'abord (a traiter, puis rejets Extelia), puis les traitees ; dans
/// chaque bloc, la demande la plus recente en tete (RPJCR_ID_RPJCR DESC, celui de la V2). Une demande sans CNBF n'a pas
/// d'avocat (la V2 cherchait un mandataire sans CNBF, « IS NULL »).
/// RPJCR_ETAT suit eDematEtat (Outils\DLL\RigMetier\JUDICIAIRE\DematEtat.cs) : 1 integre (attend le greffe), 2 traite par
/// le greffe, 3 envoye, 4 valide par Extelia, 8 erreur d'envoi, 9 rejete par Extelia.
///
/// GROUPES ET REGLE D'ETAT (marches du descripteur : 0 Demande, 1 Avocat, 2 Audiences, 3 Decision, 4 Recapitulatif) :
///   RPJCR_ETAT = '9'                        -> « Rejets Extelia », EnAttente « Rejet Extelia » : la reponse du greffe a ete
///                                              rejetee par la plateforme ; la decision etait prise (0 a 3), sinon aucune ;
///   RPJCR_ETAT_TRAITEMENT_RIG = 'A'         -> « Traitees », Validee « Acceptee » : OPE_INFO_DEMANDE.cs:487-489 (audience de
///                                              renvoi choisie) : demande, avocat, audiences, decision (0 a 3) ;
///   RPJCR_ETAT_TRAITEMENT_RIG = 'R'         -> « Traitees », Rejetee « Rejetee par le greffe » : OPE_INFO_DEMANDE.cs:518-526
///                                              (motif RPJCR_MOTIF_REJET_RIG) : 0 a 3 ;
///   pas de decision, RPJCR_USER_EN_COURS    -> « A traiter », EnCours « En cours » : un agent l'a ouverte
///                                              (OPE_INFO_DEMANDE.cs:96, les autres sont refuses) : demande (0), plus
///                                              l'avocat (1) s'il est retrouve par son CNBF ;
///   pas de decision, personne               -> « A traiter », AFaire « En attente » : aucune marche.
/// Le recapitulatif (4, un document Word) n'est pas trace dans la base : jamais franchi ici.
/// </summary>
internal sealed class LecteurRenvoisRpjc : ILecteurEcran
{
    public string Procedure => "PROC_EDI_RPJC_REN";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string ATraiter = "À traiter", Traitees = "Traitées", RejetsExtelia = "Rejets Extelia";

    // Bloc : 0 a traiter, 1 rejet Extelia, 2 traitee (l'ordre d'affichage, et le groupe de la ligne).
    private const string Requete = """
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT r.RPJCR_ID_RPJCR, r.RPJCR_ETAT, r.RPJCR_ETAT_TRAITEMENT_RIG, r.RPJCR_DATE_TRAITEMENT_RIG,
                     r.RPJCR_NUMERO_AFFAIRE, r.RPJCR_CNBF_DEMANDE, r.RPJCR_DATE_AUDIENCE_INITIALE, r.RPJCR_DATE_RENVOI_DEMANDEE,
                     r.RPJCR_USER_EN_COURS, LEFT(r.RPJCR_MOTIF_REJET_RIG, 160) AS MotifRejet,
                     r.RPJCR_DATE_RECEPTION_EXTELIA_CT, i.INSTN_NOM_COURANT, m.MNDTR_NOM,
                     CASE WHEN r.RPJCR_ETAT = '9' THEN 1 WHEN r.RPJCR_ETAT_TRAITEMENT_RIG IN ('A', 'R') THEN 2 ELSE 0 END AS Bloc
              FROM v2.DEMAT_RPJC_REN AS r
              OUTER APPLY (SELECT TOP (1) x.INSTN_NOM_COURANT FROM v2.INSTANCE AS x
                           WHERE x.INSTN_NUM_ROLE = r.RPJCR_NUMERO_AFFAIRE ORDER BY x.INSTN_ID_INSTN) AS i
              OUTER APPLY (SELECT TOP (1) md.MNDTR_NOM FROM v2.MANDATAIRE AS md
                           WHERE md.MNDTR_NUMERO_CNBF = r.RPJCR_CNBF_DEMANDE
                           ORDER BY md.MNDTR_ACTIF DESC, md.MNDTR_ID_MNDTR) AS m
              WHERE r.RPJCR_ETAT = '1' OR r.RPJCR_ETAT = '9'
                 OR (r.RPJCR_ETAT <> '1'
                     AND r.RPJCR_DATE_AUDIENCE_INITIALE >= DATEADD(MONTH, -1, CAST(CAST(@aujourdhui AS date) AS datetime))
                     AND r.RPJCR_DATE_AUDIENCE_INITIALE < DATEADD(DAY, 1, CAST(CAST(@aujourdhui AS date) AS datetime)))) AS l
        ORDER BY l.Bloc, l.RPJCR_ID_RPJCR DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui });

    private static LigneLue Ligne(SqlDataReader r)
    {
        var bloc = Convert.ToInt32(r["Bloc"]);
        var decision = FormeLigne.Texte(r, "RPJCR_ETAT_TRAITEMENT_RIG");
        var decidee = decision is "A" or "R";
        var avocat = FormeLigne.Texte(r, "MNDTR_NOM");
        var ouvertePar = FormeLigne.Texte(r, "RPJCR_USER_EN_COURS");
        var traitee = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCR_DATE_TRAITEMENT_RIG"));
        var renvoiDemande = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCR_DATE_RENVOI_DEMANDEE"));
        var rejetExtelia = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCR_DATE_RECEPTION_EXTELIA_CT"));

        // La regle d'etat (en tete du fichier).
        (string Groupe, string Etat, int[] Faites, string? Statut) regle = bloc switch
        {
            1 => (RejetsExtelia, "EnAttente", decidee ? [0, 1, 2, 3] : [],
                  rejetExtelia.Length > 0 ? $"rejet Extelia du {rejetExtelia}" : "rejet Extelia"),
            2 when decision == "A" => (Traitees, "Validee", [0, 1, 2, 3], $"renvoi accepté le {traitee}"),
            2 => (Traitees, "Rejetee", [0, 1, 2, 3], FormeLigne.Joindre($"rejeté le {traitee}", FormeLigne.Texte(r, "MotifRejet"))),
            _ when ouvertePar.Length > 0 => (ATraiter, "EnCours", avocat.Length > 0 ? [0, 1] : [0], $"ouverte par {ouvertePar}"),
            _ => (ATraiter, "AFaire", [], renvoiDemande.Length > 0 ? $"renvoi demandé au {renvoiDemande}" : null),
        };

        var nom = FormeLigne.Joindre($"Rôle {FormeLigne.Texte(r, "RPJCR_NUMERO_AFFAIRE")}", FormeLigne.Texte(r, "INSTN_NOM_COURANT"));
        var initiale = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCR_DATE_AUDIENCE_INITIALE"));
        var precisions = FormeLigne.Joindre(
            avocat.Length > 0 ? avocat : $"avocat non trouvé (CNBF {FormeLigne.Texte(r, "RPJCR_CNBF_DEMANDE")})",
            initiale.Length > 0 ? $"audience initiale du {initiale}" : null,
            regle.Statut);
        // L'id : la demande (RPJCR_ID_RPJCR), que « Accepter » et « Rejeter » visent (EcrivainRenvoisRpjc.cs, lot 16).
        return new LigneLue(nom, precisions, regle.Groupe, regle.Etat, regle.Faites)
        {
            Id = Convert.ToInt32(r["RPJCR_ID_RPJCR"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
