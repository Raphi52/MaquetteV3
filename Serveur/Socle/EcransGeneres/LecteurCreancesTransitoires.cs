using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les CREANCES TRANSITOIRES (v2.CREANCE_TRANSITOIRE) de deux ecrans du lot 12 (plan « RIG v3 100 % fonctionnel », 27/11/2026) :
/// « Ajouter une créance » (PROC_ADDCREANCE, une Saisie : les creances deja saisies, et la saisie d'une nouvelle) et « Intégrer des
/// créances » (PROC_INTEGCREANCE, une File : celles qui attendent leur integration). Mode d'emploi : LecteurEcran.cs ; les gestes :
/// EcrivainAjoutCreance.cs, EcrivainIntegrationCreances.cs ; l'integration : CreancesPcV2.cs.
///
/// SOURCE (ancien RIG) : CREANCE_TRANSITOIRE (SSDT RigDatabase\dbo\Tables\CREANCE_TRANSITOIRE.sql), ecrite par le formulaire d'ajout
/// (PROC_ADDCREANCE.cs:195-214, :302-315) et par l'import des fichiers des mandataires (INTCREA\0 Integration des fichiers) ;
/// integree quand CRTRA_DATE_INSERTION_RIG est posee (ImportationTerminee, CreanceTransitoire.cs:975-977), en erreur quand
/// CRTRA_NATURE_ERREUR l'est (IntegrationCreances.cs:110-516). Le MODE : CRTRA_CREANCE_IS_CONTESTE (contestation),
/// CRTRA_DEFAUT_REPONSE (defaut de reponse), sinon admission (PROC_ADDCREANCE.cs:203-213 ; eModeIntegrationCreances).
/// « Intégrer » (Creances en attente\OPE_CREANCE_INTEGCREANCE.cs) ne montre que les creances pas encore integrees.
/// ORDRE : les plus recentes d'abord (CRTRA_DATE_CREATION, puis l'identifiant).
/// </summary>
internal static class CreancesTransitoires
{
    internal const string Admission = "Admission", Contestation = "Contestation", DefautReponse = "Défaut de réponse";

    internal static string Requete(bool enAttenteSeulement) => $"""
        SELECT TOP (@plafond) t.CRTRA_ID_CRTRA, t.CRTRA_NUM_PC, t.CRTRA_NOM_CR, t.CRTRA_DATE_CREATION, t.CRTRA_DATE_INSERTION_RIG,
               t.CRTRA_NATURE_ERREUR, t.CRTRA_CREANCE_IS_CONTESTE, t.CRTRA_DEFAUT_REPONSE, t.CRTRA_NATURE_CREANCE_ADM, t.CRTRA_OBSERVATIONS,
               t.CRTRA_DATE_ACTE, t.CRTRA_NOM_FICHIER,
               {string.Join(", ", CreancesPcV2.Montants.Select(m => "t.CRTRA_" + m.Suffixe))},
               COUNT(*) OVER () AS Total
        FROM v2.CREANCE_TRANSITOIRE AS t
        WHERE {(enAttenteSeulement ? "t.CRTRA_DATE_INSERTION_RIG IS NULL" : "1 = 1")}
        ORDER BY t.CRTRA_DATE_CREATION DESC, t.CRTRA_ID_CRTRA DESC
        """;

    internal static string Groupe(SqlDataReader r)
        => CriteresConsultation.Vrai(r, "CRTRA_CREANCE_IS_CONTESTE") ? Contestation
           : CriteresConsultation.Vrai(r, "CRTRA_DEFAUT_REPONSE") ? DefautReponse : Admission;

    /// <summary>La somme admise (les sept montants admis, sans le rejete) ; null si aucun n'est saisi.</summary>
    internal static decimal? Admis(SqlDataReader r)
    {
        var lus = CreancesPcV2.Montants.Where(m => m.Suffixe != "REJETE").Select(m => CriteresConsultation.Decimal(r, "CRTRA_" + m.Suffixe)).Where(m => m is not null).ToList();
        return lus.Count == 0 ? null : lus.Sum();
    }

    internal static string Precisions(SqlDataReader r)
    {
        var rejete = CriteresConsultation.Decimal(r, "CRTRA_REJETE");
        var integree = FormeLigne.DateDe(r, "CRTRA_DATE_INSERTION_RIG");
        var erreur = FormeLigne.Texte(r, "CRTRA_NATURE_ERREUR");
        return FormeLigne.Joindre(FormeLigne.Texte(r, "CRTRA_NUM_PC") is { Length: > 0 } pc ? "PC " + pc : "sans PC",
            Admis(r) is { } a ? "admis " + CriteresConsultation.Euros(a) : "aucun montant admis",
            rejete is { } j ? "rejeté " + CriteresConsultation.Euros(j) : null,
            "saisie le " + FormeLigne.Date(FormeLigne.DateDe(r, "CRTRA_DATE_CREATION")),
            integree is { } d ? "intégrée le " + FormeLigne.Date(d) : erreur.Length > 0 ? "erreur : " + erreur : "à intégrer");
    }

    internal static string Nom(SqlDataReader r)
        => FormeLigne.Texte(r, "CRTRA_NOM_CR") is { Length: > 0 } n ? n : "Créance transitoire n° " + FormeLigne.Texte(r, "CRTRA_ID_CRTRA");
}

/// <summary>
/// « Ajouter une créance » (PROC_ADDCREANCE) : les creances transitoires saisies (CreancesTransitoires, ci-dessus), une Saisie.
/// REGLE DES ETAPES (marches du descripteur, Pont/EcransGeneres/Facturer.cs), lue dans la transitoire :
///   0 Procédure collective = CRTRA_NUM_PC ; 1 Créancier = CRTRA_NOM_CR ; 2 Mode = contestation ou defaut de reponse ;
///   3 Montants = au moins un montant admis ou rejete (le controle de OPE_CREANCE.cs:315-320) ; 4 Créance = nature admise ou
///   observations ; 5 Dates = date de l'acte ; 6 Intégration = CRTRA_DATE_INSERTION_RIG.
/// ETAT (regle des ecrans generes) : aucune etape -> AFaire ; toutes les obligatoires (0, 1, 3, 6) -> Validee « Ajoutée » ;
/// sinon EnCours « En saisie » (une creance pas encore integree, ou en erreur : l'erreur est dans les precisions).
/// </summary>
internal sealed class LecteurAjoutCreance : ILecteurEcran
{
    public string Procedure => "PROC_ADDCREANCE";

    private static readonly int[] Obligatoires = [0, 1, 3, 6];

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, CreancesTransitoires.Requete(enAttenteSeulement: false), Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var faites = new List<int>();
        if (FormeLigne.Texte(r, "CRTRA_NUM_PC").Length > 0) faites.Add(0);
        if (FormeLigne.Texte(r, "CRTRA_NOM_CR").Length > 0) faites.Add(1);
        if (CriteresConsultation.Vrai(r, "CRTRA_CREANCE_IS_CONTESTE") || CriteresConsultation.Vrai(r, "CRTRA_DEFAUT_REPONSE")) faites.Add(2);
        if (CreancesPcV2.Montants.Any(m => CriteresConsultation.Decimal(r, "CRTRA_" + m.Suffixe) is not null)) faites.Add(3);
        if (FormeLigne.Texte(r, "CRTRA_NATURE_CREANCE_ADM").Length > 0 || FormeLigne.Texte(r, "CRTRA_OBSERVATIONS").Length > 0) faites.Add(4);
        if (FormeLigne.DateDe(r, "CRTRA_DATE_ACTE") is not null) faites.Add(5);
        if (FormeLigne.DateDe(r, "CRTRA_DATE_INSERTION_RIG") is not null) faites.Add(6);
        var etat = faites.Count == 0 ? "AFaire" : Obligatoires.All(faites.Contains) ? "Validee" : "EnCours";
        return new LigneLue(CreancesTransitoires.Nom(r), CreancesTransitoires.Precisions(r), CreancesTransitoires.Groupe(r), etat, [.. faites])
        {
            Id = FormeLigne.Texte(r, "CRTRA_ID_CRTRA"),
        };
    }
}
