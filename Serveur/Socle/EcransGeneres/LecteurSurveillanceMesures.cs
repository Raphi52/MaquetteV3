using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mesures » (PROC_SURVMES) : la surveillance des ECHEANCES des mesures d'instruction -- les evenements de procedure d'une
/// mesure active arrives a echeance, a faire expirer. Une FILE. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_survmes.md (Detail OPE, qui fait foi) et Source\RIG\DLL\Processus\PROC_SURVMES\ :
///   - la requete : Classe\LigneMesureEvePro.cs:46-71 (MESURE -> EVENEMENT_PROCEDURE, CODE_EVE_PRO, DECISION_JUDICIAIRE,
///     INSTANCE) et ses filtres toujours poses : evenement ni survenu ni expire (EVNPR_SURVENU = 0, EVNPR_DELAI_EXPIRE = 0), mesure
///     active (ni rapport depose, ni sans suite, ni caduque) ;
///   - l'echeance : ISNULL(EVNPR_DATE_MAXI, ISNULL(EVNPR_DATE_MINI, EVNPR_DATE_FIXE)) au plus aujourd'hui moins le delai
///     supplementaire (OPE_RECHERCHE_MES.cs:49-51), 0 jour par defaut ; « Expirable » decochee par defaut (Designer :90) ;
///   - l'ordre : numero de mesure decroissant (DataGridMesures.Charger, :226).
/// GROUPE et ETAT (DataGridMesures.Charger, :240-245) : un evenement dont le code n'a PAS de code « expire »
/// (R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE vide) est decoche, grise, en rouge : il ne peut pas expirer -- « Sans code d'expiration »,
/// Rejetee ; les autres, coches d'office : « Mesures à traiter », AFaire.
///
/// CE QUE LA V3 NE FAIT PAS ENCORE (nomme, aucun ecrivain) : « Valider » fait expirer l'evenement et CREE l'evenement survenu et
/// ses futurs (EvenementProcedure.PasserEnExpirerEveProFuturWithParent -> creationEvenementSurvenuDepuisExpire, RigMetier), sous
/// une Demande SURVMES verrouillee par mesure (CreateAndLockDemande, OPE_AFFICHAGE_MES.cs:83-121), avec son dossier « 2J » et les
/// demandes d'edition Lot2 (:122-136) : le moteur des evenements de procedure de RIG v2 n'est pas dans la V3 ; « Editer tout » et
/// « Editer les lignes cochées » ouvrent un apercu Word a l'ecran (EditerLigne, :157) : pas d'edition Word dans la V3. La grille
/// des codes d'evenement (cocher un code coche ses mesures, :55-66) est un filtre de l'ecran, pas de la base.
/// </summary>
internal sealed class LecteurSurveillanceMesures : ILecteurEcran
{
    public string Procedure => "PROC_SURVMES";

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string ATraiter = "Mesures à traiter", SansExpiration = "Sans code d'expiration";

    private const string Requete = """
        SELECT TOP (@plafond) e.EVNPR_ID_EVNPR, m.MSINS_ID_MSINS, m.MSINS_NUM_MESURE, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT,
               d.DCJDC_TYPE_DECISION, d.DCJDC_DATE, e.EVNPR_CODE_EVE_PRO, c.R_CODEEVEPRO_LIBELLE_ECRAN,
               ISNULL(e.EVNPR_DATE_MAXI, ISNULL(e.EVNPR_DATE_MINI, e.EVNPR_DATE_FIXE)) AS Echeance,
               ISNULL(c.R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE, '') AS CodeExpire, COUNT(*) OVER () AS Total
        FROM v2.MESURE AS m
        JOIN v2.EVENEMENT_PROCEDURE AS e ON m.MSINS_ID_MSINS = e.EVNPR_ID_MSINS
        LEFT JOIN v2.CODE_EVE_PRO AS c ON c.R_CODEEVEPRO_VALEUR = e.EVNPR_CODE_EVE_PRO
        LEFT JOIN v2.DECISION_JUDICIAIRE AS d ON m.MSINS_ID_DCJDC = d.DCJDC_ID_DCJDC
        LEFT JOIN v2.INSTANCE AS i ON d.DCJDC_ID_INSTN = i.INSTN_ID_INSTN
        WHERE e.EVNPR_SURVENU = 0 AND e.EVNPR_DELAI_EXPIRE = 0
          AND m.MSINS_DATE_DEPOT_RAPPORT IS NULL AND m.MSINS_DATE_SANS_SUITE IS NULL AND m.MSINS_DATE_CADUCITE IS NULL
          AND ISNULL(e.EVNPR_DATE_MAXI, ISNULL(e.EVNPR_DATE_MINI, e.EVNPR_DATE_FIXE)) <= @aujourdhui
        ORDER BY m.MSINS_NUM_MESURE DESC, e.EVNPR_ID_EVNPR
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond),
                                  new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date });

    private static LigneLue Ligne(SqlDataReader r)
    {
        var expirable = FormeLigne.Texte(r, "CodeExpire").Length > 0;
        var decision = FormeLigne.DateDe(r, "DCJDC_DATE");
        var code = FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO");
        var libelle = FormeLigne.Texte(r, "R_CODEEVEPRO_LIBELLE_ECRAN");
        var nom = FormeLigne.Joindre($"Mesure {FormeLigne.Texte(r, "MSINS_NUM_MESURE")}", FormeLigne.Texte(r, "INSTN_NOM_COURANT"));
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "INSTN_NUM_ROLE") is { Length: > 0 } role ? $"rôle {role}" : null,
                                            libelle.Length > 0 ? $"{code} {libelle}" : code,
                                            $"échéance {FormeLigne.Date(FormeLigne.DateDe(r, "Echeance"))}",
                                            FormeLigne.Texte(r, "DCJDC_TYPE_DECISION") is { Length: > 0 } type
                                                ? $"décision {type}{(decision is null ? "" : $" du {FormeLigne.Date(decision)}")}" : null,
                                            expirable ? null : "sans code d'expiration");
        return new LigneLue(nom, precisions, expirable ? ATraiter : SansExpiration, expirable ? "AFaire" : "Rejetee", [])
        {
            Id = Convert.ToInt32(r["EVNPR_ID_EVNPR"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
