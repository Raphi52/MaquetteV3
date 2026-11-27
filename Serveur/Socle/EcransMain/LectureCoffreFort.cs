using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Coffre-fort » (PROC_COFFRE_FORT, web/src/ecrans/EcranCoffreFort.tsx) : l'HISTORIQUE des dépôts au coffre-fort électronique,
/// lu dans la base V3 du greffe (v2.DEMAT_CFEC, v2.DEMAT_ETAT, v2.MANDATAIRE_PC recopiées). LECTURE SEULE (LectureV2). Plan
/// « RIG v3 100 % fonctionnel », lot 20, 27/11/2026, marche du lot 17.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_COFFRE_FORT\Historique\Recherche.
///   - La REQUETE (OPE_RECHERCHE_HISTORIQUE.cs:79-83) : DEMAT_CFEC JOIN DEMAT_ETAT (CFEC_ETAT = R_DEMATETAT_VALEUR : un dépôt d'un
///     état inconnu n'est pas montré), LEFT JOIN MANDATAIRE_PC (CFEC_ID_DESTINATAIRE) ; à l'ouverture, aucun critère (entité,
///     date de traitement, audience, destinataire : :84-152, ils restent les filtres de l'écran, sur place).
///   - ORDRE : CFEC_DATE_TRAITEMENT_RIG, CFEC_DATE_ACCUSE_RECEPTION, CFEC_DATE_DEPOT, CFEC_DATE_RECEPTION_AR, CFEC_DATE_LECTURE
///     (:155) ; la V2 n'en montre que les premières (nbMaxHisto, OPE_RESULTAT_HISTORIQUE.cs:288-292) et dit « n résultat(s) sur N ».
///   - Les COLONNES (OPE_RESULTAT_HISTORIQUE.cs:293-360) : Détail Id. = CFEC_ID_CFEC ; Numéro instance = CFEC_NUM_ENTITE ; Etat =
///     CFEC_ETAT + « - libellé » ou « - erreur » ; Nature doc = CFEC_NATURE_DOC ; Date avis de dépôt coffre = CFEC_DATE_DEPOT ;
///     Date AR destinataire = CFEC_DATE_ACCUSE_RECEPTION ; Date lecture destinataire = CFEC_DATE_LECTURE ; Date traitement RIG =
///     CFEC_DATE_TRAITEMENT_RIG ; Date AR Infogreffe = CFEC_DATE_RECEPTION_AR ; Nom du document = CFEC_NOM_DOCUMENT_DS_COFFRE ;
///     DestWord = type de destinataire et abrégé du mandataire (:82). La couleur (:356-358) : état 8 ou 9 en rouge, 3 ou 4 en vert,
///     sans date de dépôt en orange.
/// NON LU : « Date envoi infogreffe » et l'historique des envois, lus dans la base EDI du greffe (FLUX_SORTANT / FICHIER_SORTANT,
/// :347-351 ; Common.CodeGreffeEdi), que la base V3 n'a pas ; « Nbre envoi » (FluxSortant, :319) pour la même raison ; le nom
/// d'un destinataire institutionnel (GetNomDestinataire) ; et la liste « À transmettre » (OPE_TREEVIEW_TRANSMETTRE, quatre
/// requêtes réunies d'OPE_RECHERCHE_COFFRE.cs:190-260 sur évènements, éditions et décisions). Le dépôt lui-même part au
/// coffre-fort électronique, un SERVICE EXTERNE : la V3 ne l'appelle jamais.
/// </summary>
public static class LectureCoffreFort
{
    /// <summary>Au plus autant de dépôts (le plafond des écrans faits main).</summary>
    public const int Plafond = RoutesEcransMain.Plafond;

    private const string Requete = """
        SELECT TOP (@plafond) c.CFEC_ID_CFEC, c.CFEC_NUM_ENTITE, c.CFEC_TYPE_ENTITE, c.CFEC_NATURE_DOC, c.CFEC_TYPE_DE_DESTINATAIRE,
               m.MNDPC_ABREGE, m.MNDPC_NOM, c.CFEC_ETAT, et.R_DEMATETAT_LIBELLE_ECRAN, c.CFEC_ERREUR, c.CFEC_NOM_DOCUMENT_DS_COFFRE,
               c.CFEC_IDENTIFIANT_DEPOT, c.CFEC_DATE_TRAITEMENT_RIG, c.CFEC_DATE_RECEPTION_AR, c.CFEC_DATE_DEPOT,
               c.CFEC_DATE_ACCUSE_RECEPTION, c.CFEC_DATE_LECTURE, COUNT(*) OVER () AS Total
        FROM v2.DEMAT_CFEC AS c
        INNER JOIN v2.DEMAT_ETAT AS et ON c.CFEC_ETAT = et.R_DEMATETAT_VALEUR
        LEFT JOIN v2.MANDATAIRE_PC AS m ON c.CFEC_ID_DESTINATAIRE = m.MNDPC_ID_MNDPC
        ORDER BY c.CFEC_DATE_TRAITEMENT_RIG, c.CFEC_DATE_ACCUSE_RECEPTION, c.CFEC_DATE_DEPOT, c.CFEC_DATE_RECEPTION_AR, c.CFEC_DATE_LECTURE,
                 c.CFEC_ID_CFEC
        """;

    /// <summary>Les dépôts de l'historique, au plus <paramref name="plafond"/>.</summary>
    public static async Task<DepotsCoffreLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<DepotCoffreLu>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond });
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            string T(string c) => FormeLigne.Texte(r, c).Trim();
            lignes.Add(new DepotCoffreLu(Convert.ToInt32(r["CFEC_ID_CFEC"], CultureInfo.InvariantCulture), T("CFEC_NUM_ENTITE"),
                T("CFEC_TYPE_ENTITE"), T("CFEC_NATURE_DOC"), T("CFEC_TYPE_DE_DESTINATAIRE"), T("MNDPC_ABREGE"), T("MNDPC_NOM"),
                T("CFEC_ETAT"), T("R_DEMATETAT_LIBELLE_ECRAN"), T("CFEC_ERREUR"), T("CFEC_NOM_DOCUMENT_DS_COFFRE"), T("CFEC_IDENTIFIANT_DEPOT"),
                FormeLigne.DateDe(r, "CFEC_DATE_TRAITEMENT_RIG"), FormeLigne.DateDe(r, "CFEC_DATE_RECEPTION_AR"), FormeLigne.DateDe(r, "CFEC_DATE_DEPOT"),
                FormeLigne.DateDe(r, "CFEC_DATE_ACCUSE_RECEPTION"), FormeLigne.DateDe(r, "CFEC_DATE_LECTURE")));
        }
        return new DepotsCoffreLus([.. lignes], total);
    }
}
