using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Dépôts judiciaires » (web/src/ecrans/EcranDepotsJud.tsx) : les instances ou deposer, lues dans la base V3 du greffe.
/// LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026 (marche du lot 17).
///
/// SOURCE (ancien RIG) : PROC_DEPOTJUD (Processus\PROC_DEPOTJUD.cs ; fiche proc_depotjud.md) -- l'objet est une INSTANCE
/// (MetierBase, PROC_DEPOTJUD.cs:19) trouvee par l'etape de recherche, serie JROP seule (MetierParamNumMetiers « Instance=JROP »,
/// PROC_DEPOTJUD.Designer.cs:121 ; filtre CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE, comme LecteurNouvelleInstance). Colonnes :
///   - le numero de role et le nom courant (INSTN_NUM_ROLE, INSTN_NOM_COURANT), la nature d'affaire par son libelle
///     (NATURE_AFFAIRE, comme LecteurInstanceJudiciaire), la date d'enrolement ;
///   - le DERNIER evenement survenu de l'instance (EVENEMENT_PROCEDURE, EVNPR_SURVENU = 1, libelle de CODE_EVE_PRO), l'objet
///     de l'etape « Nouvel événement survenu » (PROC_DEPOTJUD.cs:34) ;
///   - « à facturer » : l'etape de facturation ne s'affiche QUE si l'instance n'a aucun client (PROC_DEPOTJUD.cs:58-64), les
///     clients etant ceux des factures des demandes de son affaire (Instance.GetListeClientsInstance, Instance.cs:2791-2802 ;
///     facture d'une demande par FACTU_NUM_DEMANDE, comme LectureDemandes) ;
///   - la demande qui la tient (verrou de RIG v2, VerrouDemandeV2 : en cours, ou W/I/N, liee en 2J) -- une information de la
///     base ; PROC_DEPOTJUD ne pose pas de verrou (PROC_DEPOTJUD.cs:42-51).
/// ORDRE : role decroissant, comme la liste de « Nouvelle instance ». NON LUE : la chambre (aucune colonne de l'instance).
/// </summary>
public static class LectureDepotsJud
{
    /// <summary>Le nombre d'instances montrees.</summary>
    public const int Plafond = 200;

    private const string Requete = """
        SELECT TOP (@plafond) i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_DATE_ENROLEMENT,
               COALESCE(na.R_NATUREAFFA_LIBELLE_ECRAN, i.INSTN_NATURE_AFFAIRE) AS Nature,
               ev.Evenement, ev.EVNPR_DATE, v.DMND_NUM_DEMANDE AS Verrou,
               CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d
                                              JOIN v2.FACTURE AS fa ON fa.FACTU_NUM_DEMANDE = d.DMND_NUM_DEMANDE
                                              JOIN rec.Client AS cl ON cl.Id = fa.FACTU_ID_CLIEN
                                              WHERE d.DMND_ID_AFFRE = i.INSTN_ID_AFFRE)
                                 THEN 0 ELSE 1 END) AS AFacturer,
               COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
        LEFT JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
        OUTER APPLY (SELECT TOP (1) COALESCE(c.R_CODEEVEPRO_LIBELLE_ECRAN, e.EVNPR_CODE_EVE_PRO) AS Evenement, e.EVNPR_DATE
                     FROM v2.EVENEMENT_PROCEDURE AS e LEFT JOIN v2.CODE_EVE_PRO AS c ON c.R_CODEEVEPRO_VALEUR = e.EVNPR_CODE_EVE_PRO
                     WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN AND e.EVNPR_SURVENU = 1
                     ORDER BY e.EVNPR_DATE DESC, e.EVNPR_ID_EVNPR DESC) AS ev
        OUTER APPLY (SELECT TOP (1) d.DMND_NUM_DEMANDE FROM v2.DEMANDE AS d
                     JOIN v2.DOSSIER_DEMANDE AS dd ON dd.DSSDM_ID_DMND = d.DMND_ID_DMND
                     WHERE dd.DSSDM_CODE_SEQUENCEUR = '2J' AND dd.DSSDM_VALEUR_SEQUENCEUR = CONVERT(varchar(100), i.INSTN_ID_INSTN)
                       AND (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
                     ORDER BY d.DMND_ID_DMND) AS v
        WHERE ca.R_CODEAFFAIR_TYPE_NUM_ROLE = 'JROP'
        ORDER BY i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC
        """;

    /// <summary>Les instances de la serie JROP, au plus <paramref name="plafond"/>.</summary>
    public static async Task<InstancesDepotLues> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<InstanceDepotLue>();
        var total = 0;
        await using var lu = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond });
        while (await lu.ReadAsync(ct))
        {
            total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new InstanceDepotLue(Chaine(lu, "INSTN_NUM_ROLE"), Chaine(lu, "INSTN_NOM_COURANT"), Chaine(lu, "Nature"),
                lu["INSTN_DATE_ENROLEMENT"] as DateTime?, Chaine(lu, "Evenement"), lu["EVNPR_DATE"] as DateTime?, Chaine(lu, "Verrou"),
                lu["AFacturer"] is true));
        }
        return new InstancesDepotLues([.. lignes], total);
    }
}
