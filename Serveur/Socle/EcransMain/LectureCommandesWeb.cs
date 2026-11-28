using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Commandes web » (PROC_COMMANDES_WEB, web/src/ecrans/EcranCommandesWeb.tsx) : les commandes d'états d'endettement, de
/// certificats de non-faillite et de non-opposition reçues par le web, lues dans la base V3 du greffe (v2.COMMANDE_WEB
/// recopiée). LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026, marche du lot 17.
///
/// SOURCE : PAS D'ECRAN RIG v2. La V2 traitait ces commandes depuis les réquisitions (PROC_REQUI, fiche 275) ; l'écran et la
/// table COMMANDE_WEB sont ceux de NéoRIG (D:\RIG-V3, lu sans y toucher), dont la maquette recopie les règles
/// (Metier/TraitementCommandes.cs) :
///   - la liste : la recherche CommandesWebSearch (RIG.Application\Rcs\CommandesWeb\CommandesWebSearch.cs:22-24), triée par
///     DateCommande, sans autre critère à l'ouverture ; une famille hors de FamilleCommandeWeb (2, 5, 11, 12, 13 :
///     RIG.Domain\Rcs\CommandesWeb\FamilleCommandeWeb.cs:12-19) est écartée (CommandeWebRepository.cs:86) ;
///   - l'état (CMDWB_ETAT : A, T, V, R, S -- EtatCommandeWeb.cs:25-40) et l'origine (CMDWB_ORIGINE : SL, IG, MAN --
///     OrigineCommandeWeb.cs:16-28) sont rendus tels quels, le pont les traduit.
/// NON LU : l'adresse de livraison, l'e-mail, les types d'inscription et le sous-type commandés, que NéoRIG tire du flux XML de
/// la commande (CMDWB_FLUX, CommandeWebRepository.cs:121-177, :300-330) ; les lots d'envoi. LA DEMANDE de la commande
/// (CMDWB_ID_DMND, 1824-creer-commande-web.sql:27) : son numéro et le numéro de gestion qu'elle porte, posé par « Valider »
/// (EcritureCommandesWeb.cs, lot 7 du plan « RIG v3 complet ») -- le dossier auquel la commande validée est rattachée. Le
/// rapprochement (chercher le débiteur) est la recherche de « Réquisitions » (LectureEntreprisesRequi.cs), appelée par le pont.
/// </summary>
public static class LectureCommandesWeb
{
    /// <summary>Au plus autant de commandes (le plafond des écrans faits main).</summary>
    public const int Plafond = RoutesEcransMain.Plafond;

    private const string Requete = """
        SELECT TOP (@plafond) c.CMDWB_ID_CMDWB, c.CMDWB_REFERENCE_COMMANDE, c.CMDWB_NUMERO_SUIVI, c.CMDWB_DATE_COMMANDE, c.CMDWB_FAMILLE,
               c.CMDWB_TYPE_PRODUIT, c.CMDWB_ORIGINE, c.CMDWB_SIREN, c.CMDWB_DENOMINATION, c.CMDWB_NOM_REQUERANT, c.CMDWB_ETAT,
               c.CMDWB_MOTIF, c.CMDWB_DATE_TRAITEMENT, d.DMND_NUM_DEMANDE AS NumeroDemande, d.DMND_NUM_INDICATIF AS DossierDemande,
               COUNT(*) OVER () AS Total
        FROM v2.COMMANDE_WEB AS c
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = c.CMDWB_ID_DMND
        WHERE c.CMDWB_FAMILLE IN (2, 5, 11, 12, 13)
        ORDER BY c.CMDWB_DATE_COMMANDE, c.CMDWB_ID_CMDWB
        """;

    /// <summary>Les commandes reçues, au plus <paramref name="plafond"/>.</summary>
    public static async Task<CommandesWebLues> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<CommandeWebLue>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond });
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            string T(string c) => FormeLigne.Texte(r, c).Trim();
            lignes.Add(new CommandeWebLue(Convert.ToInt32(r["CMDWB_ID_CMDWB"], CultureInfo.InvariantCulture), T("CMDWB_REFERENCE_COMMANDE"),
                T("CMDWB_NUMERO_SUIVI"), FormeLigne.DateDe(r, "CMDWB_DATE_COMMANDE"), Convert.ToInt32(r["CMDWB_FAMILLE"], CultureInfo.InvariantCulture),
                T("CMDWB_TYPE_PRODUIT"), T("CMDWB_ORIGINE"), T("CMDWB_SIREN"), T("CMDWB_DENOMINATION"), T("CMDWB_NOM_REQUERANT"),
                T("CMDWB_ETAT"), T("CMDWB_MOTIF"), FormeLigne.DateDe(r, "CMDWB_DATE_TRAITEMENT"), T("NumeroDemande"), T("DossierDemande")));
        }
        return new CommandesWebLues([.. lignes], total);
    }
}
