using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Dossiers verrouillés » (PROC_VERROUS, web/src/ecrans/EcranVerrous.tsx) : les demandes de RIG v2 qui VERROUILLENT leurs
/// objets, lues dans la base V3 du greffe (v2.DEMANDE, v2.DOSSIER_DEMANDE, v2.ETAT_DEMANDE recopiées). LECTURE SEULE (LectureV2).
/// Plan « RIG v3 100 % fonctionnel », lot 9, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE (ancien RIG) : il n'y a pas de procédure « verrous » dans RIG v2 (aucune fiche proc_verrous.md) ; la RÈGLE est celle
/// du verrou de RIG v2, déjà reprise pour les fiches (Serveur/Socle/EcransGeneres/VerrouDemandeV2.cs) :
/// Outils\DLL\RigMetier\Demande\ISqlLockable.cs:14-19 et Demande.GetOldLock (Demande.cs:1290-1316) -- un objet est verrouillé par
/// une demande liée par DOSSIER_DEMANDE qui est « en cours » ou en attente, interrompue ou en réclamation
/// (« dmnd_en_cours = 1 or dmnd_etat_demande in ('W','I','N') ») ; qui la tient : DMND_NOM_UTILISATEUR (le message de
/// Demande.cs:1315) ; son état : « En cours » si DMND_EN_COURS, sinon le libellé de ETAT_DEMANDE. Une demande sans objet lié ne
/// verrouille rien : elle n'est pas montrée.
/// ORDRE : la plus ancienne d'abord (date de création, puis identifiant) -- celle qui retient le plus longtemps.
/// Le filtre « Mon login » de l'écran (les demandes d'un autre agent) est appliqué par le pont (Pont/EcranVerrousBase.cs).
/// </summary>
public static class LectureVerrous
{
    /// <summary>Au plus autant de demandes (le plafond des écrans faits main).</summary>
    public const int Plafond = 500;

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_NUM_INDICATIF, d.DMND_NOM, d.DMND_NUM_DEMANDE, d.DMND_CODE_PROSS, d.DMND_EN_COURS,
               e.R_ETATDEMAND_LIBELLE_ECRAN, d.DMND_ETAT_DEMANDE, d.DMND_NOM_UTILISATEUR, d.DMND_DATE_CREATION, COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        LEFT JOIN v2.ETAT_DEMANDE AS e ON e.R_ETATDEMAND_VALEUR = d.DMND_ETAT_DEMANDE
        WHERE (d.DMND_EN_COURS = 1 OR d.DMND_ETAT_DEMANDE IN ('W', 'I', 'N'))
          AND EXISTS (SELECT 1 FROM v2.DOSSIER_DEMANDE AS dd WHERE dd.DSSDM_ID_DMND = d.DMND_ID_DMND)
        ORDER BY d.DMND_DATE_CREATION, d.DMND_ID_DMND
        """;

    /// <summary>Les demandes qui verrouillent, au plus <paramref name="plafond"/>.</summary>
    public static async Task<VerrousLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<VerrouLu>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", plafond));
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], System.Globalization.CultureInfo.InvariantCulture);
            var etat = r["DMND_EN_COURS"] is true ? "En cours"
                : FormeLigne.Texte(r, "R_ETATDEMAND_LIBELLE_ECRAN") is { Length: > 0 } libelle ? libelle : FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
            lignes.Add(new VerrouLu(FormeLigne.Texte(r, "DMND_NUM_INDICATIF").Trim(), FormeLigne.Texte(r, "DMND_NOM"),
                FormeLigne.Texte(r, "DMND_NUM_DEMANDE").Trim(), FormeLigne.Texte(r, "DMND_CODE_PROSS"), etat,
                FormeLigne.Texte(r, "DMND_NOM_UTILISATEUR").Trim(), FormeLigne.DateDe(r, "DMND_DATE_CREATION")));
        }
        return new VerrousLus([.. lignes], total);
    }
}
