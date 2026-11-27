using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « BODACC » (PROC_ENV_BOD, web/src/ecrans/EcranBodacc.tsx) : les annonces BODACC judiciaires a envoyer et deja envoyees,
/// lues dans la base V3 du greffe (v2.BODACC et les tables judiciaires, copies telles quelles). LECTURE SEULE (LectureV2).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\BODACC\PROC_ENV_BOD.
///   La recherche (Operations/OPE_RECH_BOD.cs:222-257) : la requete SQLToSendOrAlreadySent de LigneBodaccJud.cs (:298-327,
///   RigMetier\JUDICIAIRE\GenerationXML IPubliciteJudiciaire) -- EVENEMENT_PROCEDURE joint a BODACC, a la procedure
///   collective ou a la prevention et a leur premier etablissement ; une annonce supprimee (BDCCR_ETAT_BODACC = '9') n'est pas
///   montree ; seul le premier etablissement (ETBPC_NUM_ORDRE 1, ou aucun) d'une PC ou d'une prevention numerotee. Ordre :
///   date d'envoi, code de l'evenement, date de l'evenement (:323-327).
///   La periode : du reglage BODACC_JUD_DATE, a defaut le 1er janvier de l'annee, a aujourd'hui (OPE_RECH_BOD.cs:63-66) ;
///   filtre sur BDCCR_DATE_ENVOI, de 00 h le premier jour a 24 h le dernier (:229-230).
///   Le partage : sans numero de bordereau, l'annonce est A ENVOYER ; avec, elle est DEJA ENVOYEE (:207-209).
///   Les colonnes : grille « a envoyer » (Controls/BodaccToSendRigDataGridView.cs:200-205) et « deja envoye »
///   (BodaccAlreadySentRigDataGridView.cs:219-230) -- numero de la PC ou de la prevention (NumeroRole,
///   ILigneGenXML_IPubliciteJudiciaire.cs:98-112), code et date de l'evenement, texte, date d'envoi (prevue), date de
///   transmission du bordereau (BRDRC_DATE_TRANSMISSION, texte jj/mm/aaaa de RIG v2), bordereau, facture, date et numero de
///   parution, numero d'annonce, type (C Creation, M Rectification, autre Annulation : :73-83).
///   Le dossier ouvert par le clic : celui de la PC ou de la prevention (PRCLL_ID_DSRCS / PREVE_ID_DSRCS, fiche rec.DossierRcs).
/// NON REPRIS : la seconde requete des annonces d'evenements SUPPRIMES (GetBodacSentAndDelete, OPE_RECH_BOD.cs:259-298) ; les
/// criteres de l'ecran (type, dates, bordereau, facture, numero) filtrent les annonces lues, sur place, comme avant.
/// </summary>
// fix-ok: pas un correctif -- le lot 20 du plan-676a4b19 AJOUTE la recherche de la palette (ChercherAsync) en trois pas :
//   requete coupee en morceaux (Annonces + Periode + Ordre, meme texte qu'avant), recherche, lecture des lignes partagee ;
//   la lecture de l'ecran est inchangee, prouvee par la section 1c d'EcransMainBaseCheck.
public static class LectureBodacc
{
    /// <summary>Le debut de la periode de recherche de RIG v2 (OPE_RECH_BOD.cs:63).</summary>
    internal static readonly LectureReglage DateDebut = new("BODACC_JUD_DATE", "", "OPE_RECH_BOD.cs:63-65");

    /// <summary>
    /// Les annonces de LigneBodaccJud.SQLToSendOrAlreadySent (:298-327) : sans les supprimees, le premier etablissement d'une
    /// PC ou d'une prevention numerotee. Suivent la periode (<see cref="Periode"/>) ou les mots cherches
    /// (<see cref="ChercherAsync"/>), puis l'ordre de RIG v2 (<see cref="Ordre"/>).
    /// </summary>
    private const string Annonces = """
        SELECT TOP (@plafond)
               b.BDCCR_NUMERO_ANNONCE, COALESCE(pc.PRCLL_NUM_PC, pv.PREVE_NUM_PRV) AS NumPc, b.BDCCR_TYPE_ANNONCE,
               ev.EVNPR_CODE_EVE_PRO, ev.EVNPR_DATE, b.BDCCR_DATE_ENVOI, b.BDCCR_NUM_BORDEREAU, b.BDCCR_NUM_PARUTION,
               b.BDCCR_DATE_PUBLICITE, b.BDCCR_NUM_FACTURE, b.BDCCR_TEXTE_BODACC, ds.NumeroGestion,
               TRY_CONVERT(datetime, LEFT(bo.BRDRC_DATE_TRANSMISSION, 10), 103) AS Transmission,
               COUNT(*) OVER () AS Total
        FROM v2.EVENEMENT_PROCEDURE AS ev
        INNER JOIN v2.BODACC AS b ON ev.EVNPR_ID_EVNPR = b.BDCCR_ID_EVNPR
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = ev.EVNPR_ID_PRCLL
        LEFT JOIN v2.ETABLISSEMENT_PC AS epc ON pc.PRCLL_ID_PRCLL = epc.ETBPC_ID_PRCLL
        LEFT JOIN v2.PREVENTION AS pv ON pv.PREVE_ID_PREVE = ev.EVNPR_ID_PREVE
        LEFT JOIN v2.ETABLISSEMENT_PC AS eprv ON pv.PREVE_ID_PREVE = eprv.ETBPC_ID_PREVE
        LEFT JOIN v2.BORDEREAU AS bo ON bo.BRDRC_ID_BRDRC = b.BDCCR_ID_BRDRC
        LEFT JOIN rec.DossierRcs AS ds ON ds.Id = COALESCE(pc.PRCLL_ID_DSRCS, pv.PREVE_ID_DSRCS)
        WHERE b.BDCCR_ETAT_BODACC <> '9'
          AND ((ISNULL(epc.ETBPC_NUM_ORDRE, 1) = 1 AND pc.PRCLL_NUM_PC IS NOT NULL)
               OR (ISNULL(eprv.ETBPC_NUM_ORDRE, 1) = 1 AND pv.PREVE_NUM_PRV IS NOT NULL))

        """;

    /// <summary>La periode de recherche de l'ecran BODACC, sur la date d'envoi (OPE_RECH_BOD.cs:229-230).</summary>
    private const string Periode = """
          AND b.BDCCR_DATE_ENVOI >= @du AND b.BDCCR_DATE_ENVOI < @lendemainAu

        """;

    /// <summary>L'ordre de RIG v2 : date d'envoi, code de l'evenement, date de l'evenement (LigneBodaccJud.cs:323-327).</summary>
    private const string Ordre = "ORDER BY b.BDCCR_DATE_ENVOI, ev.EVNPR_CODE_EVE_PRO, ev.EVNPR_DATE";

    private const string Requete = Annonces + Periode + Ordre;

    /// <summary>
    /// Ce dans quoi chaque mot cherche doit se trouver : les champs que compare la palette (annonceRepond,
    /// web/src/composants/Palette.tsx) -- numero d'annonce, type (au mot de <see cref="TypeAnnonce"/>), texte, dossier, numero
    /// de PC ou de prevention --, sans casse ni accents (COLLATE ..._CI_AI), comme son pliage.
    /// </summary>
    private const string ChampsCherches = """
        CONCAT(b.BDCCR_NUMERO_ANNONCE, ' ',
               CASE b.BDCCR_TYPE_ANNONCE WHEN 'C' THEN 'Création' WHEN 'M' THEN 'Rectification' ELSE 'Annulation' END, ' ',
               b.BDCCR_TEXTE_BODACC, ' ', ds.NumeroGestion, ' ', COALESCE(pc.PRCLL_NUM_PC, pv.PREVE_NUM_PRV)) COLLATE Latin1_General_CI_AI
        """;

    /// <summary>Au plus autant de mots cherches ; les suivants n'affinent plus.</summary>
    internal const int MotsMax = 6;

    /// <summary>Les annonces de la periode de RIG v2, au jour <paramref name="aujourdhui"/>.</summary>
    public static async Task<AnnoncesBodaccLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, CancellationToken ct)
    {
        var au = aujourdhui.Date;
        var du = DateTime.TryParse(await DateDebut.LireTexteAsync(baseGreffe, ct), CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var reglage)
            ? reglage.Date : new DateTime(au.Year, 1, 1);
        var (lignes, total) = await LireLignesAsync(baseGreffe, Requete, ct,
            new SqlParameter("@plafond", SqlDbType.Int) { Value = RoutesEcransMain.Plafond },
            new SqlParameter("@du", SqlDbType.DateTime) { Value = du },
            new SqlParameter("@lendemainAu", SqlDbType.DateTime) { Value = au.AddDays(1) });
        return new AnnoncesBodaccLues(lignes, total, du, au);
    }

    /// <summary>
    /// La recherche de la palette Ctrl K et de la page Recherche (plan « RIG v3 100 % fonctionnel », lot 20) : les annonces
    /// de l'ecran, SANS sa periode -- RIG v2 ne pose la periode que si une date est saisie (« if (DateDu != null) »,
    /// OPE_RECH_BOD.cs:231-235) --, dont chaque mot tape se retrouve dans <see cref="ChampsCherches"/>. Moins de trois
    /// caracteres (espaces exclus) : rien, comme la palette (annonceRepond). Au plus <paramref name="plafond"/>, dans l'ordre
    /// de RIG v2, et le total en base.
    /// </summary>
    public static async Task<AnnoncesBodaccTrouvees> ChercherAsync(SqlConnection baseGreffe, string? texte, CancellationToken ct,
                                                                  int plafond = RoutesBodaccRecherche.Plafond)
    {
        // Les mots de la palette (motsDe) : deux caracteres au moins ; trois caracteres en tout, espaces exclus.
        var tous = (texte ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var mots = tous.Where(m => m.Length >= 2).Take(MotsMax).ToArray();
        if (string.Concat(tous).Length < 3 || mots.Length == 0) return new AnnoncesBodaccTrouvees([], 0);
        var filtre = string.Concat(mots.Select((_, i) => $"  AND {ChampsCherches} LIKE @mot{i} ESCAPE '\\'\n"));
        var parametres = new List<SqlParameter> { new("@plafond", SqlDbType.Int) { Value = plafond } };
        parametres.AddRange(mots.Select((m, i) => new SqlParameter($"@mot{i}", SqlDbType.NVarChar, 200) { Value = "%" + Echapper(m) + "%" }));
        var (lignes, total) = await LireLignesAsync(baseGreffe, Annonces + filtre + Ordre, ct, [.. parametres]);
        return new AnnoncesBodaccTrouvees(lignes, total);
    }

    /// <summary>Un mot tape, pris a la lettre dans un LIKE : %, _, [ et \ ne sont pas des jokers.</summary>
    private static string Echapper(string mot)
        => mot.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    /// <summary>Les annonces que rend <paramref name="requete"/>, aux colonnes de <see cref="Annonces"/>, et leur total.</summary>
    private static async Task<(AnnonceBodaccLue[] Lignes, int Total)> LireLignesAsync(SqlConnection baseGreffe, string requete,
                                                                                      CancellationToken ct, params SqlParameter[] parametres)
    {
        var lignes = new List<AnnonceBodaccLue>();
        var total = 0;
        await using (var lu = await LectureV2.LireAsync(baseGreffe, requete, ct, parametres))
        {
            while (await lu.ReadAsync(ct))
            {
                total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
                var bordereau = Chaine(lu, "BDCCR_NUM_BORDEREAU");
                var envoi = lu["BDCCR_DATE_ENVOI"] as DateTime?;
                lignes.Add(new AnnonceBodaccLue(
                    Chaine(lu, "BDCCR_NUMERO_ANNONCE"), Chaine(lu, "NumPc"), TypeAnnonce(Chaine(lu, "BDCCR_TYPE_ANNONCE")),
                    Chaine(lu, "EVNPR_CODE_EVE_PRO"), lu["EVNPR_DATE"] as DateTime?, envoi,
                    Envoi: bordereau.Length > 0 ? envoi : null,
                    Chaine(lu, "BDCCR_NUM_PARUTION"), lu["BDCCR_DATE_PUBLICITE"] as DateTime?, bordereau, Chaine(lu, "BDCCR_NUM_FACTURE"),
                    Chaine(lu, "BDCCR_TEXTE_BODACC"), Chaine(lu, "NumeroGestion"), lu["Transmission"] as DateTime?));
            }
        }
        return ([.. lignes], total);
    }

    /// <summary>Le type d'annonce comme RIG v2 l'ecrit (TypeAnnonce, ILigneGenXML_IPubliciteJudiciaire.cs:73-83).</summary>
    internal static string TypeAnnonce(string code) => code switch
    {
        "C" => "Création",
        "M" => "Rectification",
        _ => "Annulation",
    };
}
