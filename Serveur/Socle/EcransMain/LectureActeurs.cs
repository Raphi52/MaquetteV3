using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Acteurs » (PROC_ACTEUR_JUD, web/src/ecrans/EcranActeurs.tsx) : les acteurs des six types de RIG v2, lus dans la base V3 du greffe.
/// LECTURE SEULE (LectureV2) : l'écran V3 n'édite pas (ses champs sont en lecture). Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026,
/// marche du lot 17.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTEUR_JUD\PROC_ACTEUR_JUD\Recherche\OPE_RECHERCHE_ACTEUR.cs --
/// un type a la fois (juge, mandataire PC, parquetier, greffier assermente, intervenant PC, expert : l. 173-194), TOUS les acteurs du type
/// charges puis filtres a l'ecran (plein texte, chambre et statut du juge, statut du parquetier et du greffier, « actifs seulement » :
/// _RechercherJuge l. 462-490 et suivants), tries par abrege (l. 481). Les champs : les colonnes de chaque liste (_InitColumn*, l. 496-714)
/// et de chaque fiche d'edition (Edition\OPE_EDITION_G*), tels que les tables RIG v2 les portent (RigBaseGreffe\GREFFE\JUGE.cs,
/// MANDATAIRE_PC.cs, PARQUETIER.cs, GREFFIER_ASSERMENTE.cs, INTERVENANT_PC_REPER.cs, EXPERT.cs) ; l'adresse est l'ADRESSE_BASE recopiee
/// (rec.Adresse). La V3 rend les six types d'un coup ; l'ecran les filtre comme la V2.
/// NON LU (nomme) : les roles cochables du juge et du mandataire PC (JUG_JUGE_*, MNDPC_MDJ_LIQ…) -- l'ecran V3 en montre la liste, pas
/// les cases ; le compte utilisateur lie au greffier (GRASS_ID_UTLST) se montre par son numero V2 ; le statut et la chambre sont les codes
/// de la table (les libelles StatutString / ChambreString de la V2 viennent de listes de reference non lues ici).
/// </summary>
public static class LectureActeurs
{
    /// <summary>Le plafond par type (la V2 charge tout ; la V3 borne la reponse).</summary>
    public const int Plafond = 500;

    private static string Adresse(string colonne) => $"""
        (SELECT TOP (1) CONCAT_WS(' ', NULLIF(CONCAT_WS(' ', ad.NumeroVoie, ad.TypeVoie, ad.NomVoie), ''), NULLIF(CONCAT_WS(' ', ad.CodePostal, ad.Localite), ''))
         FROM rec.Adresse AS ad WHERE ad.Id = {colonne})
        """;

    /// <summary>Une requete par type : (libelle de l'ecran, requete) ; colonnes au meme nom pour tous.</summary>
    private static readonly (string Type, string Table, string Sql)[] Types =
    [
        ("Juge", "JUGE", $"""
            SELECT TOP (@plafond) a.JUG_ID_JUG AS Id, a.JUG_ABREGE AS Abrege, a.JUG_CIVILITE AS Civilite, a.JUG_NOM AS Nom, a.JUG_PRENOM AS Prenom,
                   '' AS ComplementNom, {Adresse("a.JUG_ID_ADRBS")} AS Adresse, a.JUG_TELEPHONE AS Telephone, a.JUG_FAX AS Fax,
                   a.JUG_ADRESSE_MAIL AS Mail, '' AS Qualification, a.JUG_STATUT AS Statut, a.JUG_CHAMBRE AS Chambre, a.JUG_SECTION AS Section,
                   a.JUG_DATE_DEBUT AS Debut, a.JUG_DATE_FIN AS Fin, a.JUG_BUREAU AS Bureau, '' AS NomCoffre, CAST(a.JUG_ACTIF AS bit) AS Actif,
                   '' AS Siren, '' AS Utilisateur
            FROM v2.JUGE AS a ORDER BY a.JUG_ABREGE, a.JUG_ID_JUG
            """),
        ("Mandataire", "MANDATAIRE_PC", $"""
            SELECT TOP (@plafond) a.MNDPC_ID_MNDPC AS Id, a.MNDPC_ABREGE AS Abrege, a.MNDPC_CIVILITE AS Civilite, a.MNDPC_NOM AS Nom, '' AS Prenom,
                   a.MNDPC_COMPLEMENT_NOM AS ComplementNom, {Adresse("a.MNDPC_ID_ADRBS")} AS Adresse, a.MNDPC_TELEPHONE AS Telephone,
                   a.MNDPC_FAX AS Fax, a.MNDPC_ADRESSE_MAIL AS Mail, a.MNDPC_QUALIF_PROFESSION AS Qualification, '' AS Statut, '' AS Chambre,
                   '' AS Section, CAST(NULL AS datetime) AS Debut, CAST(NULL AS datetime) AS Fin, '' AS Bureau, a.MNDPC_NOM_COFFRE AS NomCoffre,
                   CAST(a.MNDPC_ACTIF AS bit) AS Actif, a.MNDPC_SIREN AS Siren, '' AS Utilisateur
            FROM v2.MANDATAIRE_PC AS a ORDER BY a.MNDPC_ABREGE, a.MNDPC_ID_MNDPC
            """),
        ("Parquetier", "PARQUETIER", $"""
            SELECT TOP (@plafond) a.PRQTR_ID_PRQTR AS Id, a.PRQTR_ABREGE AS Abrege, a.PRQTR_CIVILITE AS Civilite, a.PRQTR_NOM AS Nom,
                   a.PRQTR_PRENOM AS Prenom, '' AS ComplementNom, {Adresse("a.PRQTR_ID_ADRBS")} AS Adresse, a.PRQTR_TELEPHONE AS Telephone,
                   a.PRQTR_FAX AS Fax, a.PRQTR_ADRESSE_MAIL AS Mail, '' AS Qualification, a.PRQTR_STATUT AS Statut, '' AS Chambre, '' AS Section,
                   CAST(NULL AS datetime) AS Debut, CAST(NULL AS datetime) AS Fin, '' AS Bureau, '' AS NomCoffre, CAST(a.PRQTR_ACTIF AS bit) AS Actif,
                   '' AS Siren, '' AS Utilisateur
            FROM v2.PARQUETIER AS a ORDER BY a.PRQTR_ABREGE, a.PRQTR_ID_PRQTR
            """),
        ("Greffier", "GREFFIER_ASSERMENTE", """
            SELECT TOP (@plafond) a.GRASS_ID_GRASS AS Id, a.GRASS_ABREGE AS Abrege, a.GRASS_CIVILITE AS Civilite, a.GRASS_NOM AS Nom,
                   a.GRASS_PRENOM AS Prenom, '' AS ComplementNom, '' AS Adresse, a.GRASS_TELEPHONE AS Telephone, a.GRASS_FAX AS Fax,
                   a.GRASS_ADRESSE_MAIL AS Mail, '' AS Qualification, a.GRASS_STATUT AS Statut, '' AS Chambre, '' AS Section,
                   CAST(NULL AS datetime) AS Debut, CAST(NULL AS datetime) AS Fin, '' AS Bureau, '' AS NomCoffre, CAST(a.GRASS_ACTIF AS bit) AS Actif,
                   '' AS Siren, CAST(a.GRASS_ID_UTLST AS varchar(12)) AS Utilisateur
            FROM v2.GREFFIER_ASSERMENTE AS a ORDER BY a.GRASS_ABREGE, a.GRASS_ID_GRASS
            """),
        // L'intervenant PC n'a pas de colonne « actif » (INTERVENANT_PC_REPER.cs) : il est toujours propose, comme dans RIG v2.
        ("Intervenant", "INTERVENANT_PC_REPER", $"""
            SELECT TOP (@plafond) a.INTRPR_ID_INTRPR AS Id, a.INTRPR_ABREGE AS Abrege, a.INTRPR_CIVILITE AS Civilite, a.INTRPR_NOM AS Nom, '' AS Prenom,
                   a.INTRPR_COMPLEMENT_NOM AS ComplementNom, {Adresse("a.INTRPR_ID_ADRBS")} AS Adresse, a.INTRPR_TELEPHONE AS Telephone,
                   a.INTRPR_FAX AS Fax, a.INTRPR_ADRESSE_MAIL AS Mail, a.INTRPR_QUALIF_PROFESSION AS Qualification, '' AS Statut, '' AS Chambre,
                   '' AS Section, a.INTRPR_DATE_DEMARRAGE_COFFRE AS Debut, CAST(NULL AS datetime) AS Fin, '' AS Bureau,
                   a.INTRPR_NOM_COFFRE AS NomCoffre, CAST(1 AS bit) AS Actif, '' AS Siren, '' AS Utilisateur
            FROM v2.INTERVENANT_PC_REPER AS a ORDER BY a.INTRPR_ABREGE, a.INTRPR_ID_INTRPR
            """),
        ("Expert", "EXPERT", $"""
            SELECT TOP (@plafond) a.EXPRT_ID_EXPRT AS Id, a.EXPRT_ABREGE AS Abrege, a.EXPRT_CIVILITE AS Civilite, a.EXPRT_NOM AS Nom, '' AS Prenom,
                   a.EXPRT_COMPLEMENT_NOM AS ComplementNom, {Adresse("a.EXPRT_ID_ADRBS")} AS Adresse, a.EXPRT_TELEPHONE AS Telephone,
                   a.EXPRT_FAX AS Fax, a.EXPRT_ADRESSE_MAIL AS Mail, a.EXPRT_QUALIF_PROFESSION AS Qualification, '' AS Statut, '' AS Chambre,
                   '' AS Section, CAST(NULL AS datetime) AS Debut, CAST(NULL AS datetime) AS Fin, '' AS Bureau, '' AS NomCoffre,
                   CAST(a.EXPRT_EST_ACTIF AS bit) AS Actif, '' AS Siren, '' AS Utilisateur
            FROM v2.EXPERT AS a ORDER BY a.EXPRT_ABREGE, a.EXPRT_ID_EXPRT
            """),
    ];

    /// <summary>Les acteurs des six types ; une table que la base du greffe n'a pas ne rend rien (OBJECT_ID nul).</summary>
    public static async Task<ActeursLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<ActeurLu>();
        foreach (var (type, table, sql) in Types)
        {
            await using (var o = await LectureV2.LireAsync(baseGreffe, "SELECT OBJECT_ID(@t)", ct, new SqlParameter("@t", $"v2.{table}")))
                if (!await o.ReadAsync(ct) || o.IsDBNull(0)) continue;
            await using var r = await LectureV2.LireAsync(baseGreffe, sql, ct, new SqlParameter("@plafond", plafond));
            while (await r.ReadAsync(ct))
            {
                string T(string c) => r[c] is DBNull or null ? "" : Convert.ToString(r[c], CultureInfo.InvariantCulture)!.Trim();
                string? D(string c) => r[c] is DateTime d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
                lignes.Add(new ActeurLu(type, Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), T("Abrege"), T("Civilite"), T("Nom"), T("Prenom"),
                    T("ComplementNom"), T("Adresse"), T("Telephone"), T("Fax"), T("Mail"), T("Qualification"), T("Statut"), T("Chambre"), T("Section"),
                    D("Debut"), D("Fin"), T("Bureau"), T("NomCoffre"), r["Actif"] is true, T("Siren"), T("Utilisateur")));
            }
        }
        return new ActeursLus([.. lignes], lignes.Count);
    }
}
