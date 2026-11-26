using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Arrivée du courrier » (PROC_ARRIVEE, web/src/ecrans/EcranArrivee.tsx) et « Télétransmissions » (PROC_TELETRANS,
/// EcranTeletransmission.tsx : les arrivees de mode EDI) : le courrier arrive au greffe, lu dans la base V3 du greffe
/// (v2.ARRIVEE et v2.DEMANDE, copies telles quelles). LECTURE SEULE (LectureV2).
///
/// SOURCE (ancien RIG) : RIG v2 n'a PAS de liste des arrivees. PROC_ARRIVEE est la SAISIE d'une arrivee (proc_arrivee.md
/// :214-216 : une OPE_ARRIVEE, puis jusqu'a 99 OPE_DEMANDE) ; l'ecran de la V3 en liste les resultats avec les blocs de
/// criteres de cette saisie (OPE_ARRIVEE.Designer.cs, OPE_DEMANDE.Designer.cs). Ses filtres portent donc sur les colonnes
/// que la saisie ECRIT, et ce sont celles-ci qui sont citees :
///   numero ARRV_NUM_ARRIVEE (attribue par Arrivee, OPE_ARRIVEE.cs:27-34), date et heure ARRV_DATEHEURE et mode
///   ARRV_MODE_ARRIVEE (OPE_ARRIVEE.cs:58-75), expediteur ARRV_NOM, references ARRV_REFERENCES, case ARRV_CASE_COURRIER,
///   paiement joint ARRV_PAIEMENT_JOINT (table ARRIVEE de RIG_DEV) ; libelle du mode = MODE_ARRIVEE.R_MODEARRIVE_LIBELLE_ECRAN
///   (le combo RCS.ULT_COMBO_MODE_ARRIVEE) ;
///   la DEMANDE saisie pour l'arrivee : chaque demande est reliee a son arrivee par DMND_ID_ARRV (OPE_DEMANDE.cs:120-124) ;
///   une demande saisie a pour mere la demande de l'arrivee (DMND_ID_DMND_MERE, OPE_DEMANDE.cs:147) : la ligne montre la
///   premiere demande SAISIE (une fille), a defaut la demande de l'arrivee (une arrivee EDI n'a que la sienne). Son numero
///   DMND_NUM_DEMANDE, son type DMND_LIBELLE_ECRAN (la categorie choisie, OPE_DEMANDE.cs:220-259), l'utilisateur designe
///   DMND_ID_UTLST (OPE_DEMANDE.cs:149-153, fiche soc.Compte) ;
///   « Reste a numeriser » : une demande de l'arrivee a une ligne de dematerialisation (DEMAT_LIASSE_CFE ou DEMAT_DAS, creee
///   par _AddLigneDemat quand la case Numerisation est cochee, OPE_DEMANDE.cs:378-422) encore EN ATTENTE D'ARCHIVAGE en GED
///   (*_ARCHIVAGE_ETAT = '1', varchar(2) dans RIG_DEV ; eDematArchivageEtat.AttenteArchivage, Demat.cs:53-61 ; posee a la creation, Bug 16648, :393/:412) ;
///   « Attestation » : une demande de l'arrivee a une demande d'edition « Attestation provisoire de depot » (maquette
///   LOT2_ATTES_PROV, PROC_ARRIVEE.cs:82 ; DMNED_VALEUR_ID_SOURCE = l'id de la demande, proc_arrivee.md:117-123).
/// FILTRES (ceux de l'ecran, sur ces colonnes) : N arrivee ou N demande contient ; expediteur, references, case contient ;
/// mode egal ; date d'arrivee du..au (jours entiers) ; paiement joint ; reste a numeriser. ORDRE : la plus recente en tete.
/// </summary>
public static class LectureArrivees
{
    /// <summary>Le mode d'une arrivee teletransmise (MODE_ARRIVEE « EDI », RIG_DEV).</summary>
    public const string ModeEdi = "EDI";

    private const string Trouvees = """
        SELECT x.ARRV_NUM_ARRIVEE, x.ARRV_DATEHEURE, x.ARRV_MODE_ARRIVEE, x.ARRV_NOM, x.ARRV_REFERENCES, x.ARRV_CASE_COURRIER,
               x.ARRV_PAIEMENT_JOINT, x.Total, m.R_MODEARRIVE_LIBELLE_ECRAN AS ModeLibelle,
               dm.DMND_NUM_DEMANDE, dm.DMND_LIBELLE_ECRAN, u.Nom AS UtilisateurNom, u.Prenom AS UtilisateurPrenom,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d2
                                 JOIN v2.DEMANDE_EDITION AS ed ON ed.DMNED_VALEUR_ID_SOURCE = CONVERT(varchar(12), d2.DMND_ID_DMND)
                                 WHERE d2.DMND_ID_ARRV = x.ARRV_ID_ARRV AND ed.DMNED_CODE_MAQUETTE = 'LOT2_ATTES_PROV')
                    THEN 1 ELSE 0 END AS Attestation,
               CASE WHEN {0} THEN 1 ELSE 0 END AS ANumeriser
        FROM (SELECT TOP (@plafond) a.ARRV_ID_ARRV, a.ARRV_NUM_ARRIVEE, a.ARRV_DATEHEURE, a.ARRV_MODE_ARRIVEE, a.ARRV_NOM,
                     a.ARRV_REFERENCES, a.ARRV_CASE_COURRIER, a.ARRV_PAIEMENT_JOINT, COUNT(*) OVER () AS Total
              FROM v2.ARRIVEE AS a
              WHERE 1 = 1
        """;

    private const string Affichage = """

              ORDER BY a.ARRV_DATEHEURE DESC, a.ARRV_ID_ARRV DESC) AS x
        LEFT JOIN v2.MODE_ARRIVEE AS m ON m.R_MODEARRIVE_VALEUR = x.ARRV_MODE_ARRIVEE
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE, d.DMND_LIBELLE_ECRAN, d.DMND_ID_UTLST FROM v2.DEMANDE AS d
                     WHERE d.DMND_ID_ARRV = x.ARRV_ID_ARRV
                     ORDER BY CASE WHEN d.DMND_ID_DMND_MERE IS NULL THEN 1 ELSE 0 END, d.DMND_ID_DMND) AS dm
        LEFT JOIN soc.Compte AS u ON u.IdV2 = dm.DMND_ID_UTLST
        ORDER BY x.ARRV_DATEHEURE DESC, x.ARRV_ID_ARRV DESC
        """;

    /// <summary>Une demande de l'arrivee {0} a une ligne de dematerialisation en attente d'archivage (Demat.cs:59-61).</summary>
    private const string EnAttenteDArchivage = """
        EXISTS (SELECT 1 FROM v2.DEMANDE AS dn WHERE dn.DMND_ID_ARRV = {0}.ARRV_ID_ARRV AND (
                    EXISTS (SELECT 1 FROM v2.DEMAT_LIASSE_CFE AS l WHERE l.DMTLCF_ID_DMND = dn.DMND_ID_DMND AND l.DMTLCF_ARCHIVAGE_ETAT = '1')
                 OR EXISTS (SELECT 1 FROM v2.DEMAT_DAS AS s WHERE s.DMTDAS_ID_DMND = dn.DMND_ID_DMND AND s.DMTDAS_ARCHIVAGE_ETAT = '1')))
        """;

    /// <summary>Les arrivees qui repondent a <paramref name="r"/>, et la liste des modes d'arrivee.</summary>
    public static async Task<ArriveesLues> LireAsync(SqlConnection baseGreffe, RechercheArrivees r, CancellationToken ct)
    {
        var modes = new List<ModeArriveeLu>();
        await using (var lu = await LectureV2.LireAsync(baseGreffe,
                         "SELECT R_MODEARRIVE_VALEUR, R_MODEARRIVE_LIBELLE_ECRAN FROM v2.MODE_ARRIVEE ORDER BY R_MODEARRIVE_LIBELLE_ECRAN", ct))
            while (await lu.ReadAsync(ct)) modes.Add(new ModeArriveeLu(Chaine(lu, "R_MODEARRIVE_VALEUR"), Chaine(lu, "R_MODEARRIVE_LIBELLE_ECRAN")));

        var sql = new StringBuilder(string.Format(CultureInfo.InvariantCulture, Trouvees, string.Format(CultureInfo.InvariantCulture, EnAttenteDArchivage, "x")));
        var p = new List<SqlParameter> { new("@plafond", SqlDbType.Int) { Value = Math.Clamp(r.Plafond, 1, RoutesEcransMain.Plafond) } };
        if (!string.IsNullOrWhiteSpace(r.Numero))
        {
            sql.Append(" AND (a.ARRV_NUM_ARRIVEE LIKE @numero OR EXISTS (SELECT 1 FROM v2.DEMANDE AS dd WHERE dd.DMND_ID_ARRV = a.ARRV_ID_ARRV AND dd.DMND_NUM_DEMANDE LIKE @numero))");
            p.Add(Texte("@numero", Contient(r.Numero)));
        }
        Contenant(sql, p, "a.ARRV_NOM", "@nom", r.Nom);
        Contenant(sql, p, "a.ARRV_REFERENCES", "@references", r.References);
        Contenant(sql, p, "a.ARRV_CASE_COURRIER", "@case", r.Case);
        if (!string.IsNullOrWhiteSpace(r.Mode))
        {
            sql.Append(" AND a.ARRV_MODE_ARRIVEE = @mode");
            p.Add(Texte("@mode", r.Mode.Trim()));
        }
        if (r.Du is { } du)
        {
            sql.Append(" AND a.ARRV_DATEHEURE >= @du");
            p.Add(new SqlParameter("@du", SqlDbType.DateTime) { Value = du.ToDateTime(TimeOnly.MinValue) });
        }
        if (r.Au is { } au)
        {
            sql.Append(" AND a.ARRV_DATEHEURE < @lendemainAu");
            p.Add(new SqlParameter("@lendemainAu", SqlDbType.DateTime) { Value = au.AddDays(1).ToDateTime(TimeOnly.MinValue) });
        }
        if (r.PaiementJoint) sql.Append(" AND a.ARRV_PAIEMENT_JOINT = 1");
        if (r.NonNumerise) sql.Append(" AND " + string.Format(CultureInfo.InvariantCulture, EnAttenteDArchivage, "a"));
        sql.Append(Affichage);

        var lignes = new List<ArriveeLue>();
        var total = 0;
        await using (var lu = await LectureV2.LireAsync(baseGreffe, sql.ToString(), ct, [.. p]))
        {
            while (await lu.ReadAsync(ct))
            {
                total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
                var mode = Chaine(lu, "ARRV_MODE_ARRIVEE");
                lignes.Add(new ArriveeLue(
                    Chaine(lu, "ARRV_NUM_ARRIVEE"), lu["ARRV_DATEHEURE"] as DateTime?, mode,
                    Chaine(lu, "ModeLibelle") is { Length: > 0 } libelle ? libelle : mode,
                    Chaine(lu, "ARRV_NOM"), Chaine(lu, "ARRV_REFERENCES"), Chaine(lu, "ARRV_CASE_COURRIER"), lu["ARRV_PAIEMENT_JOINT"] is true,
                    Chaine(lu, "DMND_NUM_DEMANDE"), Chaine(lu, "DMND_LIBELLE_ECRAN"),
                    string.Join(" ", new[] { Chaine(lu, "UtilisateurNom"), Chaine(lu, "UtilisateurPrenom") }.Where(m => m.Length > 0)),
                    Numerise: Convert.ToInt32(lu["ANumeriser"], CultureInfo.InvariantCulture) == 0,
                    Attestation: Convert.ToInt32(lu["Attestation"], CultureInfo.InvariantCulture) == 1));
            }
        }
        return new ArriveesLues([.. lignes], total, lignes.Count(a => !a.Numerise), [.. modes]);
    }

    private static void Contenant(StringBuilder sql, List<SqlParameter> p, string colonne, string nom, string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return;
        sql.Append($" AND {colonne} LIKE {nom}");
        p.Add(Texte(nom, Contient(valeur)));
    }
}
