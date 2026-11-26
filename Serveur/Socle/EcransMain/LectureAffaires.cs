using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Recherche d'affaires » (web/src/ecrans/EcranRechAffaires.tsx) : les affaires du greffe, lues dans la base V3. LECTURE SEULE
/// (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026 (marche du lot 17).
///
/// SOURCE (ancien RIG) : PROC_RECHAFF (Processus\PROC_RECHAFF ; fiche proc_rechaff.md:95-132, :255-310).
///   - La requete (OPE_CRITERES.SearchQuery, :30-126) part toujours d'AFFAIRE. Le NUMERO saisi est celui de l'affaire
///     (AFFRE_ID_AFFRE, :78-79), d'une de ses demandes (DMND_NUM_DEMANDE) ou d'une de leurs factures (FACTU_NUMERO_FACTURE,
///     FACTU_NUM_DEMANDE = DMND_NUM_DEMANDE) : la V2 a trois champs, l'ecran de la V3 un seul, essaye sur les trois.
///   - Les colonnes (ULT_GRIDVIEW_RECHAFF.cs:27-47, OPE_RESULTATS.cs:54-60) : numero, libelle, date de creation, solde
///     (Affaire.GetSolde, Outils\DLL\RigMetier\Facture\Affaire.cs:318-335 : somme des VENTI_MONTANT de l'affaire moins somme
///     des ARTUT_MONTANT_TTC, arrondi au centime), demandes et factures jointes par « ; » (factures vides ecartees).
///   - Sans aucun critere, la V2 ne cherche rien (SearchQuery rend une chaine vide, :48/:125) : ici non plus (aucune ligne).
/// AJOUTS DE LA V3, nommes (en tete de EcranRechAffaires.tsx) : le libelle ou le SIREN (AFFRE_LIBELLE contient, ou SIREN du dossier
/// RCS d'une demande) et les criteres de l'instance rattachee (INSTN_ID_AFFRE) -- id, role (blancs retires, egal, comme la
/// recherche de PROC_VJUD : LecteurDossierJudiciaire.cs), code affaire (egal), nom courant (contient), periode de saisine du
/// greffe. Non repris : client, titre d'encaissement et payeur (aucun champ a l'ecran).
/// </summary>
public static class LectureAffaires
{
    /// <summary>Le nombre d'affaires montrees.</summary>
    public const int Plafond = 200;

    private const string Requete = """
        SELECT TOP (@plafond) af.AFFRE_ID_AFFRE, af.AFFRE_LIBELLE, af.AFFRE_DATE_CREATION,
               ROUND(ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_AFFRE = af.AFFRE_ID_AFFRE), 0)
                     - ISNULL((SELECT SUM(u.ARTUT_MONTANT_TTC) FROM v2.ARTICLE_UTILISE AS u WHERE u.ARTUT_ID_AFFRE = af.AFFRE_ID_AFFRE), 0), 2) AS Solde,
               (SELECT STRING_AGG(d.DMND_NUM_DEMANDE, ' ' + CHAR(59) + ' ') WITHIN GROUP (ORDER BY d.DMND_ID_DMND)
                FROM v2.DEMANDE AS d WHERE d.DMND_ID_AFFRE = af.AFFRE_ID_AFFRE) AS Demandes,
               (SELECT STRING_AGG(f.FACTU_NUMERO_FACTURE, ' ' + CHAR(59) + ' ') WITHIN GROUP (ORDER BY f.FACTU_NUMERO_FACTURE)
                FROM v2.DEMANDE AS d JOIN v2.FACTURE AS f ON f.FACTU_NUM_DEMANDE = d.DMND_NUM_DEMANDE
                WHERE d.DMND_ID_AFFRE = af.AFFRE_ID_AFFRE AND ISNULL(f.FACTU_NUMERO_FACTURE, '') <> '') AS Factures,
               i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_CODE_AFFAIRE, i.INSTN_NOM_COURANT, i.INSTN_DATE_SAISINE_GREFFE,
               ds.Siren, CASE WHEN ds.Id IS NULL THEN 0 ELSE 1 END AS AvecDossierRcs,
               COUNT(*) OVER () AS Total
        FROM v2.AFFAIRE AS af
        OUTER APPLY (SELECT TOP (1) x.INSTN_ID_INSTN, x.INSTN_NUM_ROLE, x.INSTN_CODE_AFFAIRE, x.INSTN_NOM_COURANT, x.INSTN_DATE_SAISINE_GREFFE
                     FROM v2.INSTANCE AS x
                     WHERE x.INSTN_ID_AFFRE = af.AFFRE_ID_AFFRE
                       AND (@idInstance IS NULL OR x.INSTN_ID_INSTN = @idInstance)
                       AND (@role = '' OR REPLACE(x.INSTN_NUM_ROLE, ' ', '') = @role)
                       AND (@codeAffaire = '' OR x.INSTN_CODE_AFFAIRE = @codeAffaire)
                       AND (@nom = '' OR x.INSTN_NOM_COURANT LIKE '%' + @nom + '%')
                       AND (@saisineDu IS NULL OR CONVERT(date, x.INSTN_DATE_SAISINE_GREFFE) >= @saisineDu)
                       AND (@saisineAu IS NULL OR CONVERT(date, x.INSTN_DATE_SAISINE_GREFFE) <= @saisineAu)
                     ORDER BY x.INSTN_ID_INSTN) AS i
        OUTER APPLY (SELECT TOP (1) s.Id, s.Siren FROM v2.DEMANDE AS d JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
                     WHERE d.DMND_ID_AFFRE = af.AFFRE_ID_AFFRE ORDER BY d.DMND_ID_DMND) AS ds
        WHERE (@numero = ''
               OR af.AFFRE_ID_AFFRE = TRY_CONVERT(int, @numero)
               OR EXISTS (SELECT 1 FROM v2.DEMANDE AS d WHERE d.DMND_ID_AFFRE = af.AFFRE_ID_AFFRE AND d.DMND_NUM_DEMANDE = @numero)
               OR EXISTS (SELECT 1 FROM v2.DEMANDE AS d JOIN v2.FACTURE AS f ON f.FACTU_NUM_DEMANDE = d.DMND_NUM_DEMANDE
                          WHERE d.DMND_ID_AFFRE = af.AFFRE_ID_AFFRE AND f.FACTU_NUMERO_FACTURE = @numero))
          AND (@libelle = '' OR af.AFFRE_LIBELLE LIKE '%' + @libelle + '%' OR (@siren <> '' AND ds.Siren = @siren))
          AND (@avecInstance = 0 OR i.INSTN_ID_INSTN IS NOT NULL)
        ORDER BY af.AFFRE_DATE_CREATION DESC, af.AFFRE_ID_AFFRE DESC
        """;

    /// <summary>Les affaires de <paramref name="r"/> ; aucun critere : aucune ligne, comme la V2.</summary>
    public static async Task<AffairesLues> LireAsync(SqlConnection baseGreffe, RechercheAffaires r, CancellationToken ct)
    {
        string T(string? s) => (s ?? "").Trim();
        var libelle = T(r.Libelle);
        var siren = new string(libelle.Where(char.IsDigit).ToArray());
        int? idInstance = int.TryParse(T(r.IdInstance), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        var role = T(r.Role).Replace(" ", "");
        var avecInstance = T(r.IdInstance).Length > 0 || role.Length > 0 || T(r.CodeAffaire).Length > 0 || T(r.Nom).Length > 0
                           || r.SaisineDu is not null || r.SaisineAu is not null;
        if (T(r.Numero).Length == 0 && libelle.Length == 0 && !avecInstance) return new AffairesLues([], 0);
        // Un id d'instance illisible ne trouve rien (jamais toute la base).
        if (T(r.IdInstance).Length > 0 && idInstance is null) return new AffairesLues([], 0);

        var lignes = new List<AffaireLue>();
        var total = 0;
        await using var lu = await LectureV2.LireAsync(baseGreffe, Requete, ct,
            new SqlParameter("@plafond", SqlDbType.Int) { Value = Math.Clamp(r.Plafond, 1, Plafond) },
            new SqlParameter("@numero", SqlDbType.VarChar, 40) { Value = T(r.Numero) },
            new SqlParameter("@libelle", SqlDbType.VarChar, 255) { Value = libelle },
            new SqlParameter("@siren", SqlDbType.VarChar, 9) { Value = siren.Length == 9 ? siren : "" },
            new SqlParameter("@idInstance", SqlDbType.Int) { Value = (object?)idInstance ?? DBNull.Value },
            new SqlParameter("@role", SqlDbType.VarChar, 20) { Value = role },
            new SqlParameter("@codeAffaire", SqlDbType.VarChar, 10) { Value = T(r.CodeAffaire) },
            new SqlParameter("@nom", SqlDbType.VarChar, 250) { Value = T(r.Nom) },
            new SqlParameter("@saisineDu", SqlDbType.Date) { Value = (object?)r.SaisineDu?.Date ?? DBNull.Value },
            new SqlParameter("@saisineAu", SqlDbType.Date) { Value = (object?)r.SaisineAu?.Date ?? DBNull.Value },
            new SqlParameter("@avecInstance", SqlDbType.Bit) { Value = avecInstance });
        while (await lu.ReadAsync(ct))
        {
            total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new AffaireLue(Convert.ToInt32(lu["AFFRE_ID_AFFRE"], CultureInfo.InvariantCulture), Chaine(lu, "AFFRE_LIBELLE"),
                lu["AFFRE_DATE_CREATION"] as DateTime?, Convert.ToDecimal(lu["Solde"], CultureInfo.InvariantCulture), Chaine(lu, "Demandes"),
                Chaine(lu, "Factures"), Chaine(lu, "INSTN_ID_INSTN"), Chaine(lu, "INSTN_NUM_ROLE"), Chaine(lu, "INSTN_CODE_AFFAIRE"),
                Chaine(lu, "INSTN_NOM_COURANT"), lu["INSTN_DATE_SAISINE_GREFFE"] as DateTime?, Chaine(lu, "Siren"),
                Convert.ToInt32(lu["AvecDossierRcs"], CultureInfo.InvariantCulture) == 1));
        }
        return new AffairesLues([.. lignes], total);
    }
}
