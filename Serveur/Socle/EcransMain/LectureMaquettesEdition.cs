using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Edition des maquettes » (PROC_EDIT_MAQUETTE, web/src/ecrans/EcranMaquettesEdition.tsx) : les maquettes du greffe, leurs
/// paramètres d'édition et les tables liées à chacun, lus dans la base V3 (v2.MAQUETTE, v2.PARAMETRE_EDITION, v2.CEP_EDITION,
/// v2.CODE_EVE_PRO, v2.TYPE_DECISION, v2.VIDIGED_PARAMETRAGE, v2.ADAPTATEUR_ULT, v2.COURRIER_JUDICIAIRE recopiées). LECTURE SEULE
/// (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDIT_MAQUETTE ; Détail OPE de proc_edit_maquette.md, qui fait foi) :
///   - les maquettes : OPE_MAQUETTE._LoadDataMaquette (MAQUETTE\OPE_MAQUETTE.cs:80-85) charge les types RIG, CRYSTAL, ASPOSE pour un
///     agent du greffe, et en plus EXCEL, REDA, REDA_ENTETE_FIN pour un agent Amitel (Maquette.GetMaquettesFromTypes, RigMetier
///     Maquette\Maquette.cs:80-92 : « WHERE MQTT_TYPE_MAQUETTE IN (…) », sans tri -- ici l'ordre du code, la clé de MAQUETTE) ;
///     colonnes de la liste : code, libellé, type (OPE_MAQUETTE.cs:692-694) ;
///   - ses paramètres d'édition : Maquette.LstParametreEdition = PARAMETRE_EDITION dont PRMED_CODE_MAQUETTE vaut son code
///     (ParametreEdition.GetListeParametreEditionByParam, Edition\ParametreEdition.cs:139-152 ; l'« autocorrection » des lignes VB6
///     qui l'y précède ÉCRIT la colonne depuis PRMED_LISTE : non reprise, une lecture n'écrit pas) ; leurs champs : ceux
///     d'OPE_PARAM_EDT_MAJ (proc_edit_maquette.md:104-112, :296-316) ;
///   - les tables liées d'un paramètre : TableLiee.GetTableLiee (Classe\TableLiee.cs:94-111) -- CEP_EDITION.CEPED_CODE_EDITION
///     (CepEdition.cs:32-34, libellé R_CODEEVEPRO_DEVELOPPE_EDIT du code événement, :47-52), TYPE_DECISION.R_TYPEDECISI_PARAMETRE_EDITION
///     (TYPE_DECISION.cs:172), VIDIGED_PARAMETRAGE.VGPRM_PARAMETRE_EDITION_CFEC sans casse (VidigedParametrage.cs:86-88),
///     ADAPTATEUR_ULT « ADPUL_CODE = 'EDITPARAM' AND ADPUL_VALEUR_DEFAUT LIKE code » (AdaptateurUlt.cs:117-121),
///     COURRIER_JUDICIAIRE.COUJU_CODE_PRMED (CourrierJudiciaire.cs:30-32) ; dédoublonnées par code et libellé
///     (OPE_PARAM_EDT_TABLE_LIEE.cs:45-48).
/// AUCUN GESTE : l'écran de la V3 n'a ni Modifier, ni Dupliquer, ni Supprimer, ni Valider. Deux familles dans la V2 :
///   - la MAQUETTE : ses gestes ouvrent ou recopient son document Word en GED (OPE_MAQUETTE.cs:160-246, PROC_MAJ_MAQ) ou reportent
///     ses demandes d'édition (:379-458) ;
///   - le PARAMÈTRE D'ÉDITION : il s'enregistre et se supprime EN BASE, sans document (OPE_PARAM_EDT_MAJ.cs:494-541, Sauve et
///     Delete de PARAMETRE_EDITION). Sa fiche compte une trentaine de champs et de listes (lots, imprimantes, papiers, types de
///     document, paramètres CFE) et ses règles (code processus reporté sur tous les paramètres de la maquette, :505-507 ; code
///     unique, :310 ; exemplaires, :318-324 ; suppression réservée à Amitel, :111). NON BRANCHÉ au lot 15 : reste à faire, pas une
///     dépendance à la GED.
/// </summary>
public static class LectureMaquettesEdition
{
    /// <summary>Au plus autant de maquettes (le plafond des écrans faits main).</summary>
    public const int Plafond = 500;

    /// <summary>Les types d'un agent du greffe, puis ceux d'un agent Amitel (OPE_MAQUETTE.cs:81-84).</summary>
    internal static readonly string[] TypesGreffe = ["RIG", "CRYSTAL", "ASPOSE"];
    internal static readonly string[] TypesAmitel = ["RIG", "CRYSTAL", "ASPOSE", "EXCEL", "REDA", "REDA_ENTETE_FIN"];

    private const string RequeteMaquettes = """
        SELECT TOP (@plafond) q.MQTT_CODE_MAQUETTE, q.MQTT_LIBELLE, q.MQTT_TYPE_MAQUETTE, q.MQTT_EXTENSION, q.MQTT_ENTETE_FORMAT_PLIEUSE,
               COUNT(*) OVER () AS Total
        FROM v2.MAQUETTE AS q
        WHERE q.MQTT_TYPE_MAQUETTE IN (/*types*/)
        ORDER BY q.MQTT_CODE_MAQUETTE
        """;

    // Les paramètres des maquettes lues (le même filtre que la première requête, sans plafond : un paramètre d'une maquette hors du
    // plafond est écarté au rangement).
    private const string RequeteParametres = """
        SELECT p.PRMED_CODE, p.PRMED_LIBELLE, p.PRMED_CODE_MAQUETTE, p.PRMED_CODE_PROCESSUS, p.PRMED_NATURE_EDITION, p.PRMED_IMPRIMANTE,
               p.PRMED_TYPE_PAPIER, p.PRMED_LOT, p.PRMED_LOT_LRAR, p.PRMED_LOT_ILRAR, p.PRMED_LOT_SUBSTITUTION, p.PRMED_NOMBRE_EXEMPLAIRES,
               p.PRMED_RECTO_VERSO, p.PRMED_AGRAFAGE, p.PRMED_AFFICHER_AVANT_IMPRESSION, p.PRMED_READ_ONLY, p.PRMED_IMPRESSION,
               p.PRMED_GARDER_EDITION_LOT1, p.PRMED_TYPE_GENERATION, p.PRMED_REQUETE_SQL
        FROM v2.PARAMETRE_EDITION AS p
        INNER JOIN v2.MAQUETTE AS q ON q.MQTT_CODE_MAQUETTE = p.PRMED_CODE_MAQUETTE
        WHERE q.MQTT_TYPE_MAQUETTE IN (/*types*/)
        ORDER BY p.PRMED_CODE
        """;

    // Les tables liées de ces paramètres, une ligne par lien (Parametre, Code, Libelle, Type), dans l'ordre de TableLiee.GetTableLiee.
    private const string RequeteTablesLiees = """
        SELECT t.Parametre, t.Code, t.Libelle, t.Type FROM (
            SELECT c.CEPED_CODE_EDITION AS Parametre, c.CEPED_CODE_EVE_PRO AS Code, ISNULL(e.R_CODEEVEPRO_DEVELOPPE_EDIT, '') AS Libelle,
                   'CODE EVE PRO' AS Type, 1 AS Rang
            FROM v2.CEP_EDITION AS c LEFT JOIN v2.CODE_EVE_PRO AS e ON e.R_CODEEVEPRO_VALEUR = c.CEPED_CODE_EVE_PRO
            UNION ALL
            SELECT d.R_TYPEDECISI_PARAMETRE_EDITION, d.R_TYPEDECISI_VALEUR, ISNULL(d.R_TYPEDECISI_LIBELLE_ECRAN, ''), 'TYPE DECISION', 2
            FROM v2.TYPE_DECISION AS d
            UNION ALL
            SELECT v.VGPRM_PARAMETRE_EDITION_CFEC, v.VGPRM_CODE, ISNULL(v.VGPRM_LIBELLE, ''), 'VIDIGED PARAMETRAGE', 3
            FROM v2.VIDIGED_PARAMETRAGE AS v
            UNION ALL
            SELECT a.ADPUL_VALEUR_DEFAUT, a.ADPUL_CODE, ISNULL(a.ADPUL_LIBELLE, ''), 'ULT DE PROCESSUS', 4
            FROM v2.ADAPTATEUR_ULT AS a WHERE a.ADPUL_CODE = 'EDITPARAM'
            UNION ALL
            SELECT j.COUJU_CODE_PRMED, j.COUJU_CODE, ISNULL(j.COUJU_LIBELLE, ''), 'COURRIER JUDICIAIRE', 5
            FROM v2.COURRIER_JUDICIAIRE AS j
        ) AS t
        INNER JOIN v2.PARAMETRE_EDITION AS p ON (t.Rang = 4 AND t.Parametre LIKE p.PRMED_CODE)
                                             OR (t.Rang = 3 AND UPPER(t.Parametre) = UPPER(p.PRMED_CODE))
                                             OR (t.Rang IN (1, 2, 5) AND t.Parametre = p.PRMED_CODE)
        INNER JOIN v2.MAQUETTE AS q ON q.MQTT_CODE_MAQUETTE = p.PRMED_CODE_MAQUETTE
        WHERE q.MQTT_TYPE_MAQUETTE IN (/*types*/)
        ORDER BY t.Rang, t.Code
        """;

    /// <summary>Les maquettes que voit l'agent (<paramref name="agentAmitel"/> : les types avancés en plus), au plus <paramref name="plafond"/>.</summary>
    public static async Task<MaquettesLues> LireAsync(SqlConnection baseGreffe, bool agentAmitel, CancellationToken ct, int plafond = Plafond)
    {
        var types = agentAmitel ? TypesAmitel : TypesGreffe;
        // Les types sont des constantes de ce fichier, jamais une saisie : ils s'écrivent en littéraux (LectureV2 n'accepte pas de liste).
        var dansTypes = string.Join(", ", types.Select(t => $"'{t}'"));

        var maquettes = new List<(string Code, string Libelle, string Type, string Extension, bool Plieuse)>();
        var total = 0;
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteMaquettes.Replace("/*types*/", dansTypes, StringComparison.Ordinal), ct,
                         new SqlParameter("@plafond", plafond)))
        {
            while (await r.ReadAsync(ct))
            {
                total = Convert.ToInt32(r["Total"], System.Globalization.CultureInfo.InvariantCulture);
                maquettes.Add((FormeLigne.Texte(r, "MQTT_CODE_MAQUETTE").Trim(), FormeLigne.Texte(r, "MQTT_LIBELLE").Trim(),
                    FormeLigne.Texte(r, "MQTT_TYPE_MAQUETTE").Trim(), FormeLigne.Texte(r, "MQTT_EXTENSION").Trim(), r["MQTT_ENTETE_FORMAT_PLIEUSE"] is true));
            }
        }

        // Les tables liées, par paramètre (sans casse : VidigedParametrage compare sans casse, SQL Server aussi par sa collation).
        var tables = new Dictionary<string, List<TableLieeLue>>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteTablesLiees.Replace("/*types*/", dansTypes, StringComparison.Ordinal), ct))
        {
            while (await r.ReadAsync(ct))
            {
                var parametre = FormeLigne.Texte(r, "Parametre").Trim();
                var lien = new TableLieeLue(FormeLigne.Texte(r, "Code").Trim(), FormeLigne.Texte(r, "Libelle").Trim(), FormeLigne.Texte(r, "Type"));
                if (!tables.TryGetValue(parametre, out var liste)) tables[parametre] = liste = [];
                // Dédoublonnées par code et libellé (OPE_PARAM_EDT_TABLE_LIEE.cs:45-48 : GroupBy(CODE, LIBELLE).First()).
                if (!liste.Any(t => t.Code == lien.Code && t.Libelle == lien.Libelle)) liste.Add(lien);
            }
        }

        var parametres = new Dictionary<string, List<ParametreEditionLu>>(StringComparer.Ordinal);
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteParametres.Replace("/*types*/", dansTypes, StringComparison.Ordinal), ct))
        {
            while (await r.ReadAsync(ct))
            {
                var code = FormeLigne.Texte(r, "PRMED_CODE").Trim();
                var maquette = FormeLigne.Texte(r, "PRMED_CODE_MAQUETTE").Trim();
                if (!parametres.TryGetValue(maquette, out var liste)) parametres[maquette] = liste = [];
                liste.Add(new ParametreEditionLu(code, FormeLigne.Texte(r, "PRMED_LIBELLE").Trim(), FormeLigne.Texte(r, "PRMED_CODE_PROCESSUS").Trim(),
                    FormeLigne.Texte(r, "PRMED_NATURE_EDITION").Trim(), FormeLigne.Texte(r, "PRMED_IMPRIMANTE").Trim(),
                    FormeLigne.Texte(r, "PRMED_TYPE_PAPIER").Trim(), FormeLigne.Texte(r, "PRMED_LOT").Trim(), FormeLigne.Texte(r, "PRMED_LOT_LRAR").Trim(),
                    FormeLigne.Texte(r, "PRMED_LOT_ILRAR").Trim(), FormeLigne.Texte(r, "PRMED_LOT_SUBSTITUTION").Trim(),
                    r["PRMED_NOMBRE_EXEMPLAIRES"] is int n ? n : null, r["PRMED_RECTO_VERSO"] is true, r["PRMED_AGRAFAGE"] is true,
                    r["PRMED_AFFICHER_AVANT_IMPRESSION"] is true, r["PRMED_READ_ONLY"] is true, r["PRMED_IMPRESSION"] is true,
                    r["PRMED_GARDER_EDITION_LOT1"] is true, FormeLigne.Texte(r, "PRMED_TYPE_GENERATION").Trim(),
                    FormeLigne.Texte(r, "PRMED_REQUETE_SQL").Trim(), tables.TryGetValue(code, out var liees) ? [.. liees] : []));
            }
        }

        return new MaquettesLues([.. maquettes.Select(m => new MaquetteLue(m.Code, m.Libelle, m.Type, m.Extension, m.Plieuse,
            parametres.TryGetValue(m.Code, out var p) ? [.. p] : []))], total);
    }
}
