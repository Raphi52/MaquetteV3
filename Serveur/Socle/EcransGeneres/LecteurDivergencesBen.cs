using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Divergences BEN » (PROC_DBEN_ALERT) : le tableau de bord des signalements de divergence sur les beneficiaires effectifs
/// encore ouverts. Mode d'emploi des lecteurs : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_DBEN\PROC_DBEN_ALERT\PROC_DBEN_ALERT.cs, _SqlBase
/// (l. 50-61) et RefreshResultats (l. 63-85) : DIVERGENCE JOIN DOSSIER_RCS (DIVER_ID_DSRCS), WHERE DIVER_STATUS = 0 (les
/// divergences ouvertes), le detail = les references de divergence de ses lignes (DIVERGENCE_DETAIL JOIN
/// DIVERGENCE_REFERENCE : « code (libelle) », STRING_AGG), ORDER BY DIVER_DATE_CREATION (la plus ancienne en tete). A
/// l'ouverture, aucun critere : « Exclure les dossiers radies » n'est pas coche (l. 80-81 : DSSRC_DATE_RADIATION IS NULL
/// quand il l'est). Dans la base V3, DOSSIER_RCS est rec.DossierRcs ; DIVERGENCE, DIVERGENCE_DETAIL et DIVERGENCE_REFERENCE
/// sont les tables V2 (v2.*).
///
/// GROUPE : le critere « Exclure les dossiers radies » de la V2 -- « Dossiers radies » si le dossier a une date de radiation
/// (rec.DossierRcs.DateRadiation, la DSSRC_DATE_RADIATION de la V2), sinon « Dossiers actifs ».
/// REGLE D'ETAT : toute ligne est « Ouverte » (AFaire) -- la V2 ne liste QUE DIVER_STATUS = 0. Les deux autres tuiles du
/// descripteur restent a 0 : la V2 n'a pas d'etat intermediaire, et une divergence traitee (statut 1, 2) sort de la liste.
///
/// GESTES : aucun a ecrire. Le seul geste de la V2, « Visualiser le signalement » (menu et double-clic,
/// Resultat\OPE_DIVERGENCES.cs:49-59, 115-117, 132), OUVRE la divergence dans l'ecran de traitement PROC_DBEN : il ne
/// modifie rien ici, l'ecran est en lecture seule (FORM_DBEN_ALERT.cs:12-14 masque VALIDER, VERIFIER, INTERROMPRE).
/// </summary>
internal sealed class LecteurDivergencesBen : ILecteurEcran
{
    public string Procedure => "PROC_DBEN_ALERT";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Actifs = "Dossiers actifs", Radies = "Dossiers radiés";

    private const string Requete = """
        SELECT TOP (@plafond) dv.DIVER_ID_DIVER, dv.DIVER_DATE_CREATION, dv.DIVER_DEMANDEUR, s.NumeroGestion, s.DateRadiation,
               e.Denomination,
               (SELECT STRING_AGG(CAST(rf.DIVREF_CODE_REFERENCE + ' (' + rf.DIVREF_LIBELLE + ')' AS nvarchar(max)), ', ')
                       WITHIN GROUP (ORDER BY dd.DIVDET_ID_DIVDET)
                FROM v2.DIVERGENCE_DETAIL AS dd JOIN v2.DIVERGENCE_REFERENCE AS rf ON rf.DIVREF_ID_DIVREF = dd.DIVDET_ID_DIVREF
                WHERE dd.DIVDET_ID_DIVER = dv.DIVER_ID_DIVER) AS Detail,
               COUNT(*) OVER () AS Total
        FROM v2.DIVERGENCE AS dv
        JOIN rec.DossierRcs AS s ON s.Id = dv.DIVER_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE dv.DIVER_STATUS = 0
        ORDER BY dv.DIVER_DATE_CREATION, dv.DIVER_ID_DIVER
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
        var radiation = FormeLigne.DateDe(r, "DateRadiation");
        var precisions = FormeLigne.Joindre($"Divergence du {FormeLigne.Date(FormeLigne.DateDe(r, "DIVER_DATE_CREATION"))}",
                                            FormeLigne.Texte(r, "DIVER_DEMANDEUR") is { Length: > 0 } d ? $"demandeur {d}" : null,
                                            FormeLigne.Texte(r, "Detail"),
                                            radiation is null ? null : $"radié le {FormeLigne.Date(radiation)}");
        // La regle d'etat (en tete du fichier) : toute divergence listee est ouverte.
        return new LigneLue(nom, precisions, radiation is null ? Actifs : Radies, "AFaire", []);
    }
}
