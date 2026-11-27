using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Documents dématérialisés » (PROC_DOC_DEMAT, web/src/ecrans/EcranDocDemat.tsx) : les pièces dématérialisées des demandes du
/// greffe, lues dans la base V3 (v2.DEMAT_CREFO_PJ, v2.TYPE_PJ_CREFO, v2.DEMAT_LIASSE_CFE, v2.DEMAT_DAS, v2.DEMANDE recopiées).
/// LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE (ancien RIG) : proc_doc_demat.md (le Détail OPE fait foi : « les tables VIDIGED_* n'apparaissent nulle part dans le
/// code de ce plugin », le modèle est RIG.METIER.DEMAT) :
///   - une pièce est une ligne de DEMAT_CREFO_PJ, rattachée à UNE démat : une liasse CFE (DMCOPJ_ID_DMTLCF) OU un DAS
///     (DMCOPJ_ID_DMTDAS), l'un ou l'autre (ViewModelDocuments.Notify.cs:139-172) ; la démat porte la demande (DMTLCF_ID_DMND,
///     DMTDAS_ID_DMND) ;
///   - son type est TYPE_PJ_CREFO (catégorie, ordre ; R_TYPEPJCREF_ABREGE_PIECE et R_TYPEPJCREF_LIBELLE_ECRAN, les colonnes
///     AbrPieceLibelle et DisplayLibelle de l'arbre) ; sa date est DMCOPJ_DATE_PIECE ;
///   - une pièce supprimée garde sa ligne (DMCOPJ_SUPPRIME, DMCOPJ_DATE_SUPPRIME : « Restaurer » la remet, :112-137) : elle est
///     lue, marquée « Supprimée » ;
///   - l'ordre dans une démat : DMCOPJ_NUM_ORDRE, que la validation réaffecte après le tri de RIG v2 (:104-110), puis l'identifiant ;
///     les démats, de la plus récemment créée (DMTLCF_DATE_CREATION, DMTDAS_DATE_CREATION) à la plus ancienne.
/// ÉCART NOMMÉ : RIG v2 ouvre les pièces d'UNE demande, reçue en paramètre (IdendifiantDemande, OPE_DOC_DEMAT.cs:67-75) ; l'écran
/// V3, ouvert depuis le menu, montre les pièces des démats les plus récentes (au plus le plafond), groupées par dossier.
/// </summary>
public static class LectureDocDemat
{
    /// <summary>Au plus autant de pièces (le plafond des écrans faits main).</summary>
    public const int Plafond = 500;

    private const string Requete = """
        SELECT TOP (@plafond) p.DMCOPJ_ID_DMCOPJ, p.DMCOPJ_ID_DMTLCF, p.DMCOPJ_ID_DMTDAS, p.DMCOPJ_NOM_FICHIER, p.DMCOPJ_DATE_PIECE,
               p.DMCOPJ_SUPPRIME, p.DMCOPJ_ETAT_INFOGREFFE, p.DMCOPJ_TYPE_PJ_CREFO, t.R_TYPEPJCREF_ABREGE_PIECE, t.R_TYPEPJCREF_LIBELLE_ECRAN,
               d.DMND_NUM_INDICATIF, d.DMND_NUM_DEMANDE, d.DMND_NOM, COUNT(*) OVER () AS Total
        FROM v2.DEMAT_CREFO_PJ AS p
        LEFT JOIN v2.TYPE_PJ_CREFO AS t ON t.R_TYPEPJCREF_VALEUR = p.DMCOPJ_TYPE_PJ_CREFO
        LEFT JOIN v2.DEMAT_LIASSE_CFE AS l ON l.DMTLCF_ID_DMTLCF = p.DMCOPJ_ID_DMTLCF
        LEFT JOIN v2.DEMAT_DAS AS s ON s.DMTDAS_ID_DMTDAS = p.DMCOPJ_ID_DMTDAS
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = COALESCE(l.DMTLCF_ID_DMND, s.DMTDAS_ID_DMND)
        ORDER BY COALESCE(l.DMTLCF_DATE_CREATION, s.DMTDAS_DATE_CREATION) DESC, p.DMCOPJ_ID_DMTLCF DESC, p.DMCOPJ_ID_DMTDAS DESC,
                 p.DMCOPJ_NUM_ORDRE, p.DMCOPJ_ID_DMCOPJ
        """;

    /// <summary>Les pièces dématérialisées, au plus <paramref name="plafond"/>.</summary>
    public static async Task<PiecesDematLues> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<PieceDematLue>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", plafond));
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], System.Globalization.CultureInfo.InvariantCulture);
            var indicatif = FormeLigne.Texte(r, "DMND_NUM_INDICATIF").Trim();
            var demande = FormeLigne.Texte(r, "DMND_NUM_DEMANDE").Trim();
            var demat = r["DMCOPJ_ID_DMTLCF"] is not DBNull ? $"Liasse CFE {r["DMCOPJ_ID_DMTLCF"]}"
                      : r["DMCOPJ_ID_DMTDAS"] is not DBNull ? $"DAS {r["DMCOPJ_ID_DMTDAS"]}" : "Sans démat";
            var abrege = FormeLigne.Texte(r, "R_TYPEPJCREF_ABREGE_PIECE").Trim();
            var libelle = FormeLigne.Texte(r, "R_TYPEPJCREF_LIBELLE_ECRAN").Trim();
            var type = FormeLigne.Texte(r, "DMCOPJ_TYPE_PJ_CREFO").Trim();
            lignes.Add(new PieceDematLue(
                Convert.ToInt32(r["DMCOPJ_ID_DMCOPJ"], System.Globalization.CultureInfo.InvariantCulture),
                // Le dossier de la demande (le numéro indicatif, comme « Dossiers verrouillés »), sinon le numéro de demande,
                // sinon la démat elle-même : une pièce n'est jamais rangée sous un dossier vide.
                indicatif.Length > 0 ? indicatif : demande.Length > 0 ? demande : demat,
                FormeLigne.Texte(r, "DMND_NOM").Trim(),
                demat,
                abrege.Length > 0 ? abrege : type,
                libelle.Length > 0 ? libelle : type,
                FormeLigne.DateDe(r, "DMCOPJ_DATE_PIECE"),
                FormeLigne.Texte(r, "DMCOPJ_NOM_FICHIER").Trim(),
                r["DMCOPJ_SUPPRIME"] is true,
                FormeLigne.Texte(r, "DMCOPJ_ETAT_INFOGREFFE").Trim()));
        }
        return new PiecesDematLues([.. lignes], total);
    }
}
