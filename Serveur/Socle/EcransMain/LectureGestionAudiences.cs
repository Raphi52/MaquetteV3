using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Gestion des audiences » (web/src/ecrans/EcranGestionAudiences.tsx) : les audiences et cabinets SUPPRIMES, lus dans la base V3
/// du greffe. LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026 (marche du lot 17).
///
/// SOURCE (ancien RIG) : PROC_GESTAUDS, Opérations\OPE_RECHERCHE_GESTAUDS.cs -- ses deux requetes lisent AUDIENCE_CABINET_SUPPRIMEE :
///   - les CABINETS (ChargementSQLCabinet, :279-323) : AUDNCS_AUDIENCE = 0, le code cabinet « code - libelle » (CODE_CABINET), le
///     juge et le greffier « abrege - civilite nom prenom » (JUGE, GREFFIER_ASSERMENTE ; vides sans nom) ;
///   - les AUDIENCES (ChargementSQLAudience, :325-390) : AUDNCS_AUDIENCE = 1, la chambre « code - libelle » (CHAMBRE), la section ;
///   - pour toutes : date, heure, type (AUDNCS_TYPE_AUD_CAB), types de classement admis, publique, auteur et date de la suppression,
///     date de diffusion (« Envoi suppression Infogreffe »), non disponible sur le web ;
///   - ORDRE : date, heure (strQueryOrder, :170).
/// A L'OUVERTURE (LireAsync), la V3 lit les deux modes d'un coup (au plus 500 lignes) ; la RECHERCHE de RIG v2 (dates, type, chambre,
/// section, juge, code cabinet, type de classement, nombre maximum, mode) est appliquee PAR LE SERVEUR : RechercherAsync (plan « parcours
/// complet », lot 9, 28/11/2026). NON LUS : les compteurs d'appels et de lectures
/// (NOMBRE_APPELS / NOMBRE_LECTURES, :288-289, que la liste n'affiche pas) et l'infobulle de la composition (:381-383, :393-432) :
/// RIG v2 y joint COMPOSITION sur l'identifiant de la LIGNE SUPPRIMEE (CMPST_ID_ADNC = AUDNCS_ID_AUDNCS, :394-399), pas sur celui
/// de l'audience (AUDNCS_ID_AUDNC) ; elle montre donc la composition d'une AUTRE audience dont l'identifiant tombe pareil. Mesure du
/// 27/11/2026 (RIG_DEV) : 89 des 207 audiences supprimees ont ainsi une « composition » qui n'est pas la leur, et aucune (0 sur 207)
/// n'a garde la sienne (COMPOSITION par AUDNCS_ID_AUDNC). La V3 ne recopie pas une valeur fausse ; la vraie serait toujours vide.
/// AUCUN GESTE D'ECRITURE : PROC_GESTAUDS est une consultation dans RIG v2 -- boutons Valider et Verifier caches (FORM_GESTAUDS.cs:17-18),
/// menu de la liste retire (OPE_RESULTAT_GESTAUDS.cs:167-168 : ParamCreationAudCab, que son seul appelant ne passe pas,
/// FORM_RECHERCHE_AUDCAB.cs:154-163), et son « Supprimer » lirait la colonne 13 que ces lignes n'ont pas (PROC_GESTAUDS.cs:272-280).
/// </summary>
public static class LectureGestionAudiences
{
    /// <summary>Le nombre de lignes montrees a l'ouverture.</summary>
    public const int Plafond = 500;

    /// <summary>Le nombre de lignes au plus d'une recherche (le « Nombre de résultats maximum » de l'ecran V3 : 1 a 999).</summary>
    public const int MaxRecherche = 999;

    /// <summary>La liste de PROC_GESTAUDS : <paramref name="where"/> (« AND ... ») filtre AUDIENCE_CABINET_SUPPRIMEE (alias s).</summary>
    private static string Requete(string where) => $"""
        SELECT TOP (@plafond) CONVERT(bit, CASE WHEN s.AUDNCS_AUDIENCE = 0 THEN 1 ELSE 0 END) AS Cabinet, s.AUDNCS_DATE, s.AUDNCS_HEURE,
               s.AUDNCS_TYPE_AUD_CAB,
               CASE WHEN ISNULL(s.AUDNCS_CODE_CABINET, '') = '' THEN '' ELSE s.AUDNCS_CODE_CABINET + ' - ' + ISNULL(cc.R_CODECABINE_LIBELLE_ECRAN, '') END AS CodeCabinet,
               CASE WHEN ISNULL(j.JUG_NOM, '') = '' THEN ''
                    ELSE ISNULL(j.JUG_ABREGE, '') + ' - ' + ISNULL(j.JUG_CIVILITE, '') + ' ' + j.JUG_NOM + ' ' + ISNULL(j.JUG_PRENOM, '') END AS Juge,
               CASE WHEN ISNULL(g.GRASS_NOM, '') = '' THEN ''
                    ELSE ISNULL(g.GRASS_ABREGE, '') + ' - ' + ISNULL(g.GRASS_CIVILITE, '') + ' ' + g.GRASS_NOM + ' ' + ISNULL(g.GRASS_PRENOM, '') END AS Greffier,
               CASE WHEN ISNULL(s.AUDNCS_CHAMBRE, '') = '' THEN '' ELSE s.AUDNCS_CHAMBRE + ' - ' + ISNULL(ch.R_CHAMBRE_LIBELLE_ECRAN, '') END AS Chambre,
               s.AUDNCS_SECTION, s.AUDNCS_TYPE_CLASSEMENT_ADMIS, CONVERT(bit, ISNULL(s.AUDNCS_PUBLIQUE, 0)) AS Publique, s.AUDNCS_AUTEUR_SUPPRESSION,
               s.AUDNCS_DATE_SUPPRESSION, s.AUDNCS_DATE_DIFFUSION, CONVERT(bit, ISNULL(s.AUDNCS_NON_DISPONIBLE_WEB, 0)) AS NonDisponibleWeb,
               s.AUDNCS_CHAMBRE AS CodeChambre, s.AUDNCS_CODE_CABINET AS CodeCabinetSeul, s.AUDNCS_ID_JUG AS IdJuge,
               COUNT(*) OVER () AS Total
        FROM v2.AUDIENCE_CABINET_SUPPRIMEE AS s
        LEFT JOIN v2.CODE_CABINET AS cc ON cc.R_CODECABINE_VALEUR = s.AUDNCS_CODE_CABINET
        LEFT JOIN v2.JUGE AS j ON j.JUG_ID_JUG = s.AUDNCS_ID_JUG
        LEFT JOIN v2.GREFFIER_ASSERMENTE AS g ON g.GRASS_ID_GRASS = s.AUDNCS_ID_GRASS
        LEFT JOIN v2.CHAMBRE AS ch ON ch.R_CHAMBRE_VALEUR = s.AUDNCS_CHAMBRE
        WHERE 1 = 1{where}
        ORDER BY s.AUDNCS_DATE, s.AUDNCS_HEURE, s.AUDNCS_ID_AUDNCS
        """;

    /// <summary>Les audiences et cabinets supprimes, au plus <paramref name="plafond"/>.</summary>
    public static Task<AudiencesSupprimeesLues> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
        => LignesAsync(baseGreffe, "", plafond, [], ct);

    /// <summary>
    /// La RECHERCHE de RIG v2, appliquee par le serveur (plan « parcours complet », lot 9, 28/11/2026), au mot pres de
    /// OPE_RECHERCHE_GESTAUDS._Rechercher (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GESTAUDS\Opérations\OPE_RECHERCHE_GESTAUDS.cs) :
    ///   - il faut une date de DEBUT, d'audience ou de suppression (:172) ; sans elle, RIG v2 ne cherche rien (en silence ; la V3 le dit) ;
    ///     des dates incoherentes (debut apres fin, pour les deux) : « La date de début ne peut être vide » (:173, :259) ;
    ///   - la date d'audience : du et au -> entre du 00 h et au 23:59:59.998, ou sans date ; du seul -> ce jour a 00 h (:187-191, :227-231) ;
    ///     la date de suppression : du et au -> entre les deux, ou sans date ; du seul -> ce jour-la, ou sans date (:193-196, :233-236) ;
    ///   - CABINETS (AUDNCS_AUDIENCE = 0, :209) : type, code cabinet, juge (son numero) (:198-200) ; AUDIENCES (AUDNCS_AUDIENCE = 1, :249) :
    ///     type, chambre, section (:238-240) -- les criteres de l'autre mode sont caches dans RIG v2 (InitData, :73-89) et ignores ici ;
    ///   - UN type de classement : premier, au milieu, dernier ou seul de la liste admise (:202-207, :242-247) ;
    ///   - le nombre maximum (ult_IntNumMax, 200 par defaut, :105 ; « top », :285, :352) ; ordre : date puis heure (:170).
    /// NON REPRIS (nommes) : « préaudience » (ParamPreaud, :302) et l'instance (ParamIdInstance, :217, :345), deux parametres que seul un
    /// autre ecran passe ; le message « Aucun cabinet ne correspond » (_VerifierSiCabinetsExistent, :269-277) : la V3 montre la liste vide.
    /// </summary>
    public static async Task<AudiencesSupprimeesLues> RechercherAsync(SqlConnection baseGreffe, RechercheAudiencesSupprimees r, CancellationToken ct)
    {
        static DateTime? Jour(string? texte)
            => DateTime.TryParseExact(texte?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var (audDu, audAu, supDu, supAu) = (Jour(r.AudienceDu), Jour(r.AudienceAu), Jour(r.SuppressionDu), Jour(r.SuppressionAu));
        if (audDu is null && supDu is null)
            return new AudiencesSupprimeesLues([], 0) { Message = "Aucune recherche : RIG v2 cherche à partir d'une date de début, d'audience ou de suppression." };
        if (!((audAu is null || audDu <= audAu) || (supAu is null || supDu <= supAu)))
            return new AudiencesSupprimeesLues([], 0) { Message = "La date de début ne peut être vide" };

        var where = new StringBuilder($" AND s.AUDNCS_AUDIENCE = {(r.Cabinets ? 0 : 1)}");
        var p = new List<SqlParameter>();
        void Ajouter(string sql, string nom, object valeur) { where.Append(sql); p.Add(new SqlParameter(nom, valeur)); }
        if (audDu is { } ad)
        {
            if (audAu is { } aa)
            {
                where.Append(" AND ((s.AUDNCS_DATE >= @audDu AND s.AUDNCS_DATE <= @audAu) OR s.AUDNCS_DATE IS NULL)");
                p.Add(Date("@audAu", aa, finDeJour: true));
            }
            else where.Append(" AND s.AUDNCS_DATE = @audDu");
            p.Add(Date("@audDu", ad, finDeJour: false));
        }
        if (supDu is { } sd)
        {
            where.Append(" AND ((s.AUDNCS_DATE_SUPPRESSION >= @supDu AND s.AUDNCS_DATE_SUPPRESSION <= @supAu) OR s.AUDNCS_DATE_SUPPRESSION IS NULL)");
            p.Add(Date("@supDu", sd, finDeJour: false));
            p.Add(Date("@supAu", supAu ?? sd, finDeJour: true));
        }
        if (Rempli(r.Type) is { } type) Ajouter(" AND s.AUDNCS_TYPE_AUD_CAB = @type", "@type", type);
        if (r.Cabinets)
        {
            if (Rempli(r.CodeCabinet) is { } code) Ajouter(" AND s.AUDNCS_CODE_CABINET = @code", "@code", code);
            if (r.Juge is { } juge and not 0) Ajouter(" AND s.AUDNCS_ID_JUG = @juge", "@juge", juge);
        }
        else
        {
            if (Rempli(r.Chambre) is { } chambre) Ajouter(" AND s.AUDNCS_CHAMBRE = @chambre", "@chambre", chambre);
            if (Rempli(r.Section) is { } section) Ajouter(" AND s.AUDNCS_SECTION = @section", "@section", section);
        }
        if (Rempli(r.TypeClassement) is { } c)
        {
            where.Append(" AND (s.AUDNCS_TYPE_CLASSEMENT_ADMIS LIKE @cl1 OR s.AUDNCS_TYPE_CLASSEMENT_ADMIS LIKE @cl2"
                         + " OR s.AUDNCS_TYPE_CLASSEMENT_ADMIS LIKE @cl3 OR s.AUDNCS_TYPE_CLASSEMENT_ADMIS = @cl)");
            var echappe = c.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
            p.AddRange([new("@cl1", echappe + ";%"), new("@cl2", "%;" + echappe + ";%"), new("@cl3", "%;" + echappe), new("@cl", c)]);
        }
        return await LignesAsync(baseGreffe, where.ToString(), Math.Clamp(r.Max, 1, MaxRecherche), [.. p], ct);
    }

    private static string? Rempli(string? texte) => string.IsNullOrWhiteSpace(texte) ? null : texte.Trim();

    /// <summary>Un jour a 00:00:00.000 (ToSQL00Heure) ou a 23:59:59.998 (ToSQL24Heure, RigTools\SQL\DBObject.cs:541-546).</summary>
    private static SqlParameter Date(string nom, DateTime jour, bool finDeJour)
        => new(nom, SqlDbType.DateTime) { Value = finDeJour ? jour.Date.AddDays(1).AddMilliseconds(-2) : jour.Date };

    private static async Task<AudiencesSupprimeesLues> LignesAsync(SqlConnection baseGreffe, string where, int plafond, SqlParameter[] criteres,
                                                                   CancellationToken ct)
    {
        var lignes = new List<AudienceSupprimeeLue>();
        var total = 0;
        await using var lu = await LectureV2.LireAsync(baseGreffe, Requete(where), ct,
            [new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond }, .. criteres]);
        while (await lu.ReadAsync(ct))
        {
            total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new AudienceSupprimeeLue(lu["Cabinet"] is true, lu["AUDNCS_DATE"] as DateTime?, Chaine(lu, "AUDNCS_HEURE"),
                Chaine(lu, "AUDNCS_TYPE_AUD_CAB"), Chaine(lu, "CodeCabinet"), Chaine(lu, "Juge"), Chaine(lu, "Greffier"), Chaine(lu, "Chambre"),
                Chaine(lu, "AUDNCS_SECTION"), Chaine(lu, "AUDNCS_TYPE_CLASSEMENT_ADMIS"), lu["Publique"] is true, Chaine(lu, "AUDNCS_AUTEUR_SUPPRESSION"),
                lu["AUDNCS_DATE_SUPPRESSION"] as DateTime?, lu["AUDNCS_DATE_DIFFUSION"] as DateTime?, lu["NonDisponibleWeb"] is true)
            {
                CodeChambre = Chaine(lu, "CodeChambre"), CodeCabinetSeul = Chaine(lu, "CodeCabinetSeul"), IdJuge = lu["IdJuge"] as int?,
            });
        }
        return new AudiencesSupprimeesLues([.. lignes], total);
    }
}
