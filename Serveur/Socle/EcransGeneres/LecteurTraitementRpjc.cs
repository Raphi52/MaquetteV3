using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPJC » (PROC_EDI_RPJC_TRCNC) : les conclusions transmises par les avocats sur le flux RPJC, a integrer en GED.
/// Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDI_RPJC_TRCNC, fiche Brain proc_edi_rpjc_trcnc.md,
/// sur la base du greffe. L'ecran V3 a les trois groupes du descripteur, lus dans les trois listes de la V2 :
///   - « À traiter » : la grille « Affaires a traiter », chargee a l'ouverture
///     (Affaires a traiter\OPE_AFFAIRE_A_TRAITER.cs:33-37 et 397-399) : RPJCC_ETAT = '1', personne dessus (RPJCC_USER_EN_COURS IS NULL), un mandataire ACTIF AVEC ABREGE
///     pour son CNBF (MNDTR_ACTIF = 1, MNDTR_ABREGE NOT NULL) et une instance pour son numero d'affaire (INSTN_NUM_ROLE) ;
///   - « En cours » : la grille « Affaires en cours de traitement », chargee elle aussi (Affaires en cours\
///     OPE_AFFAIRE_EN_COURS.cs:16-19 et 168-169) : les memes, avec un agent dessus (RPJCC_USER_EN_COURS NOT NULL) ;
///   - « Historique » : le bouton « Historique » (FORM_EDI_RPJC_TRCNC.cs:33-34 et 64-69), a ses criteres pre-remplis
///     (Historique\OPE_RECHERCHE_HISTORIQUE.cs:28-30 ; cases « Acceptés » et « Rejetés » cochees, .Designer.cs:100 et 145) :
///     traitees (RPJCC_ETAT_TRAITEMENT_RIG IN ('R', 'A')) du jour moins 15 jours a 0 h au jour a 24 h, et d'un type de
///     document connu (jointure sur EXPERIAN_CORRESP_EDI, l. 83 ; ni mandataire ni instance exiges, l. 76-112). La V2
///     attend le clic sur « Rechercher » ; l'ecran V3 lit la liste a ses criteres pre-remplis, comme LecteurRenvoisRpjc.
/// Le mandataire affiche est celui de la V2 : actif, avec abrege, le plus recent (Mandataire.cs:353-359,
/// GetMandataireAbregeActifByCNBF) ; l'affaire, la premiere instance du role (comme LecteurRenvoisRpjc).
/// Ecarts assumes : une ligne par transmission (la V2 en montre une par couple mandataire x instance, et refuse ensuite de
/// traiter une affaire a plusieurs instances, OPE_AFFAIRE_A_TRAITER.cs:255-266) ; une transmission n'entre que dans le
/// premier bloc qui la prend ; l'ordre -- a traiter puis en cours par numero (la V2 n'en donne pas), puis l'historique,
/// le plus recent en tete (ORDER BY RPJCC_ID_RPJCC DESC, OPE_RECHERCHE_HISTORIQUE.cs:111). Le bouton « Rejet Docapost »
/// (consultation seule des transmissions a l'etat 9, Rejet Extelia\OPE_REJET.cs:15-18) n'est pas lu en plus : une
/// transmission rejetee par Extelia apparait dans l'historique si elle y entre, marche « Rejet » franchie.
/// RPJCC_ETAT suit eDematEtat (Outils\DLL\RigMetier\JUDICIAIRE\DematEtat.cs) : 1 integre (attend le greffe), 2 traite par
/// le greffe, 3 envoye, 4 valide par Extelia, 8 erreur d'envoi, 9 rejete par Extelia.
///
/// REGLE D'ETAT (marches du descripteur : 0 Visualisation, 1 Verrouillage, 2 Traitement, 3 Rejet, 4 Cloture) :
///   « À traiter »                  -> AFaire « À traiter » : aucune marche ;
///   « En cours »                   -> EnCours « En cours » : cocher Integrer ou Refuser pose le nom de l'agent et ouvre
///                                     les documents s'ils n'ont pas ete vus (OPE_AFFAIRE_A_TRAITER.cs:242-245) :
///                                     visualisation et verrou (0, 1) ;
///   « Historique », A ou R         -> Validee « Traitée » : seules les lignes aux documents vus sont traitees (l. 687) ;
///                                     A = documents inseres en GED (l. 839-843), R = refus motive (l. 870-875) : 0, 1, 2, 4.
///                                     Le descripteur n'a pas d'etat « Rejetee » : un refus est Traitee, « refusée » dans
///                                     ses precisions, avec son motif ;
///   ... et RPJCC_ETAT = '9'        -> la reponse du greffe a ete rejetee par Extelia : la marche « Rejet » (3) en plus.
/// </summary>
internal sealed class LecteurTraitementRpjc : ILecteurEcran
{
    public string Procedure => "PROC_EDI_RPJC_TRCNC";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string ATraiter = "À traiter", EnCours = "En cours", Historique = "Historique";

    // Bloc : 0 a traiter, 1 en cours, 2 historique (l'ordre d'affichage, et le groupe de la ligne).
    private const string Requete = """
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT c.RPJCC_ID_RPJCC, c.RPJCC_ETAT, c.RPJCC_ETAT_TRAITEMENT_RIG, c.RPJCC_DATE_TRAITEMENT_RIG,
                     LEFT(c.RPJCC_MOTIF_TRAITEMENT_RIG, 160) AS MotifRefus, c.RPJCC_DATE_RECEPTION_EXTELIA_AI,
                     c.RPJCC_DATE_TRANSMISSION, c.RPJCC_CNBF_TRANSMISSION, c.RPJCC_NUMERO_AFFAIRE, c.RPJCC_USER_EN_COURS,
                     c.RPJCC_NB_PIECES, e.EXPCE_CODE, e.EXPCE_LIBELLE, i.INSTN_NOM_COURANT, m.MNDTR_CIVILITE, m.MNDTR_NOM,
                     b.Bloc
              FROM v2.DEMAT_RPJC_TRCNC AS c
              OUTER APPLY (SELECT TOP (1) md.MNDTR_ID_MNDTR, md.MNDTR_CIVILITE, md.MNDTR_NOM FROM v2.MANDATAIRE AS md
                           WHERE md.MNDTR_NUMERO_CNBF = c.RPJCC_CNBF_TRANSMISSION AND md.MNDTR_ACTIF = 1
                             AND md.MNDTR_ABREGE IS NOT NULL
                           ORDER BY md.MNDTR_ID_MNDTR DESC) AS m
              OUTER APPLY (SELECT TOP (1) x.INSTN_ID_INSTN, x.INSTN_NOM_COURANT FROM v2.INSTANCE AS x
                           WHERE x.INSTN_NUM_ROLE = c.RPJCC_NUMERO_AFFAIRE ORDER BY x.INSTN_ID_INSTN) AS i
              OUTER APPLY (SELECT TOP (1) t.EXPCE_CODE, t.EXPCE_LIBELLE FROM v2.EXPERIAN_CORRESP_EDI AS t
                           WHERE t.EXPCE_CODE = c.RPJCC_TYPE_DOCUMENT ORDER BY t.EXPCE_TYPE) AS e
              CROSS APPLY (SELECT CASE
                               WHEN c.RPJCC_ETAT = '1' AND m.MNDTR_ID_MNDTR IS NOT NULL AND i.INSTN_ID_INSTN IS NOT NULL
                                   THEN CASE WHEN c.RPJCC_USER_EN_COURS IS NULL THEN 0 ELSE 1 END
                               WHEN c.RPJCC_ETAT_TRAITEMENT_RIG IN ('R', 'A') AND e.EXPCE_CODE IS NOT NULL
                                    AND c.RPJCC_DATE_TRAITEMENT_RIG >= DATEADD(DAY, -15, CAST(CAST(@aujourdhui AS date) AS datetime))
                                    AND c.RPJCC_DATE_TRAITEMENT_RIG < DATEADD(DAY, 1, CAST(CAST(@aujourdhui AS date) AS datetime))
                                   THEN 2
                           END AS Bloc) AS b
              WHERE b.Bloc IS NOT NULL) AS l
        ORDER BY l.Bloc, CASE WHEN l.Bloc < 2 THEN l.RPJCC_ID_RPJCC END, l.RPJCC_ID_RPJCC DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui });

    private static LigneLue Ligne(SqlDataReader r)
    {
        var bloc = Convert.ToInt32(r["Bloc"]);
        var rejetExtelia = FormeLigne.Texte(r, "RPJCC_ETAT") == "9";
        var traitee = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCC_DATE_TRAITEMENT_RIG"));
        var dateRejet = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCC_DATE_RECEPTION_EXTELIA_AI"));

        // La regle d'etat (en tete du fichier).
        (string Groupe, string Etat, int[] Faites, string? Statut) regle = bloc switch
        {
            0 => (ATraiter, "AFaire", [], null),
            1 => (EnCours, "EnCours", [0, 1], $"verrouillée par {FormeLigne.Texte(r, "RPJCC_USER_EN_COURS")}"),
            _ => (Historique, "Validee", rejetExtelia ? [0, 1, 2, 3, 4] : [0, 1, 2, 4],
                  FormeLigne.Joindre(
                      FormeLigne.Texte(r, "RPJCC_ETAT_TRAITEMENT_RIG") == "A"
                          ? $"intégrée le {traitee}"
                          : FormeLigne.Joindre($"refusée le {traitee}", FormeLigne.Texte(r, "MotifRefus")),
                      rejetExtelia ? (dateRejet.Length > 0 ? $"réponse rejetée par Extelia le {dateRejet}" : "réponse rejetée par Extelia") : null)),
        };

        // Le type de document de la V2 : « code - libelle » pour une piece, « Voir tooltip » au-dela (l. 33) : ici le nombre.
        var pieces = r["RPJCC_NB_PIECES"] is int n ? n : 0;
        var code = FormeLigne.Texte(r, "EXPCE_CODE");
        var document = pieces > 1 ? $"{pieces} pièces"
            : code.Length > 0 ? $"{code} - {FormeLigne.Texte(r, "EXPCE_LIBELLE")}" : null;
        var mandataire = $"{FormeLigne.Texte(r, "MNDTR_CIVILITE")} {FormeLigne.Texte(r, "MNDTR_NOM")}".Trim();
        var transmise = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCC_DATE_TRANSMISSION"));

        var nom = FormeLigne.Joindre($"Rôle {FormeLigne.Texte(r, "RPJCC_NUMERO_AFFAIRE")}", FormeLigne.Texte(r, "INSTN_NOM_COURANT"));
        var precisions = FormeLigne.Joindre(
            mandataire.Length > 0 ? mandataire : $"mandataire non trouvé (CNBF {FormeLigne.Texte(r, "RPJCC_CNBF_TRANSMISSION")})",
            transmise.Length > 0 ? $"transmise le {transmise}" : null,
            document,
            regle.Statut);
        return new LigneLue(nom, precisions, regle.Groupe, regle.Etat, regle.Faites);
    }
}
