using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Gestion des banques » (PROC_GESTBANQ, web/src/ecrans/EcranBanques.tsx) : les banques du greffe, lues dans la base V3 (v2.BANQUE
/// et v2.MODE_PAIEMENT_BANQUE recopiées). LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026, sur la
/// marche du lot 17.
///
/// SOURCE (ancien RIG) : la grille OPE_BANQLIST (proc_gestbanq.md, Détail OPE) recharge Banque.GetListBanque, « SELECT … FROM BANQUE »
/// sans tri (Source\Outils\DLL\RigMetier\Facture\Banque.cs:63-67 : l'ordre de la clé BANQU_ID_BANQU, PK_BANQUE) ; ses colonnes Code,
/// Désignation, Compte et Modes associés (BanqRigDataGridView.cs:20-43). « Modes associés » = Banque.GetListeModePaiementBanque
/// (Banque.cs:32-43) : les CODES MODBQ_MODE_PAIEMENT des lignes MODE_PAIEMENT_BANQUE de la banque (ici dans l'ordre du code, la V2
/// suivant l'ordre de son dictionnaire), joints par « ; » (CHAR(59) : LectureV2 refuse tout « ; » dans une requete). La fiche : les champs que relient les quatre sous-opérations (proc_gestbanq.md:180-183).
/// </summary>
public static class LectureBanques
{
    /// <summary>Au plus autant de banques (le plafond des écrans faits main).</summary>
    public const int Plafond = 500;

    private const string Requete = """
        SELECT TOP (@plafond) b.BANQU_ID_BANQU, b.BANQU_CODE, b.BANQU_NOM, b.BANQU_LIGNE_ADRESSE, b.BANQU_SUITE_ADRESSE, b.BANQU_CODE_POSTAL,
               b.BANQU_LOCALITE_POSTALE, b.BANQU_COMPTE, b.BANQU_JOURNAL, b.BANQU_LC_MODELE, b.BANQU_LC_COMPTEUR, b.BANQU_POUR_LC,
               b.BANQU_POUR_LC_NOTIF, b.BANQU_POUR_LC_FOUR,
               (SELECT STRING_AGG(RTRIM(m.MODBQ_MODE_PAIEMENT), CHAR(59)) WITHIN GROUP (ORDER BY m.MODBQ_MODE_PAIEMENT)
                FROM v2.MODE_PAIEMENT_BANQUE AS m WHERE m.MODBQ_ID_BANQU = b.BANQU_ID_BANQU) AS Modes,
               COUNT(*) OVER () AS Total
        FROM v2.BANQUE AS b
        ORDER BY b.BANQU_ID_BANQU
        """;

    /// <summary>Les banques du greffe, au plus <paramref name="plafond"/>.</summary>
    public static async Task<BanquesLues> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<BanqueLue>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", plafond));
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], System.Globalization.CultureInfo.InvariantCulture);
            lignes.Add(new BanqueLue(r.GetInt32(0), FormeLigne.Texte(r, "BANQU_CODE").Trim(), FormeLigne.Texte(r, "BANQU_NOM").Trim(),
                FormeLigne.Texte(r, "BANQU_LIGNE_ADRESSE").Trim(), FormeLigne.Texte(r, "BANQU_SUITE_ADRESSE").Trim(),
                FormeLigne.Texte(r, "BANQU_CODE_POSTAL").Trim(), FormeLigne.Texte(r, "BANQU_LOCALITE_POSTALE").Trim(),
                FormeLigne.Texte(r, "BANQU_COMPTE").Trim(), FormeLigne.Texte(r, "BANQU_JOURNAL").Trim(), FormeLigne.Texte(r, "BANQU_LC_MODELE").Trim(),
                r["BANQU_LC_COMPTEUR"] is long n ? n : null, r["BANQU_POUR_LC"] is true, r["BANQU_POUR_LC_NOTIF"] is true, r["BANQU_POUR_LC_FOUR"] is true,
                FormeLigne.Texte(r, "Modes").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }
        return new BanquesLues([.. lignes], total);
    }
}
