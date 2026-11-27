using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Livre de répertoire » (PROC_JLR) : les lettres recommandees deja generees par l'ecran, une par exemplaire. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026. La saisie d'un destinataire : EcrivainLettresRecommandees.cs.
///
/// SOURCE (ancien RIG) : PROC_JLR est un « plugin de génération d'éditions de lettres recommandées » (proc_jlr.md:164-169, le Detail
/// OPE fait foi ; le menu garde le titre « Livre de répertoire », decision du lot 1). Il n'a pas de liste : chaque destinataire saisi
/// empile, par exemplaire, une demande d'edition du parametre « JLR » (lettre simple) ou « JLRAR » (avec accuse de reception)
/// (OPE_LR.cs:56, OPE_LRAR.cs:45), rattachee a la demande du processus JLR (DMNED_CODE_ENTITE_SOURCE = « DEMANDE »,
/// DMNED_VALEUR_ID_SOURCE = son id, OPE_LR.cs:61-62), avec la designation, le bloc adresse et le libelle source « designation
/// numero-exemplaire » (:58-60). La V3 relit ces demandes d'edition : celles des demandes de code processus JLR.
/// Le libelle du document est celui du parametre d'edition (CreateDemandeEditionByParametreGeneration, DemandeEdition.cs:2585-2587) :
/// il dit la sorte de lettre.
///
/// GROUPES : « Lettre recommandée » (parametre JLR) et « Lettre avec accusé de réception » (parametre JLRAR).
/// REGLE D'ETAT : une edition generee a franchi les trois marches (destinataire, exemplaires, type d'envoi) : Validee
/// « Lettre générée », faites [0, 1, 2].
/// </summary>
internal sealed class LecteurLettresRecommandees : ILecteurEcran
{
    public string Procedure => "PROC_JLR";

    internal const string Simple = "Lettre recommandée", AvecAccuse = "Lettre avec accusé de réception";
    internal const string ParametreSimple = "JLR", ParametreAccuse = "JLRAR";

    private const string Requete = """
        SELECT TOP (@plafond) de.DMNED_ID_DMNED, de.DMNED_DESIGNATION_DESTINATAIRE, de.DMNED_BLOC_ADRESSE_DESTINATAIRE, de.DMNED_LIBELLE_SOURCE,
               de.DMNED_LIBELLE_DOCUMENT, de.DMNED_DATE_CREATION, d.DMND_NUM_DEMANDE,
               CASE WHEN EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION AS p WHERE p.PRMED_CODE = 'JLRAR' AND p.PRMED_LIBELLE = de.DMNED_LIBELLE_DOCUMENT)
                    THEN 1 ELSE 0 END AS AvecAccuse,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE_EDITION AS de
        INNER JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = de.DMNED_ID_DMND
        WHERE d.DMND_CODE_PROSS = 'JLR'
        ORDER BY de.DMNED_DATE_CREATION DESC, de.DMNED_ID_DMNED DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var accuse = CriteresConsultation.EntierDe(r, "AvecAccuse") == 1;
        var adresse = FormeLigne.Texte(r, "DMNED_BLOC_ADRESSE_DESTINATAIRE").Replace("\r\n", ", ", StringComparison.Ordinal).Replace("\n", ", ", StringComparison.Ordinal);
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT"), adresse, FormeLigne.Texte(r, "DMNED_LIBELLE_SOURCE"),
            $"demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}", $"le {FormeLigne.Date(FormeLigne.DateDe(r, "DMNED_DATE_CREATION"))}");
        return new LigneLue(FormeLigne.Texte(r, "DMNED_DESIGNATION_DESTINATAIRE"), precisions, accuse ? AvecAccuse : Simple, "Validee", [0, 1, 2])
        {
            Id = FormeLigne.Texte(r, "DMNED_ID_DMNED"),
        };
    }
}
