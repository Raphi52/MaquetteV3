using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Facturation Expresso » (PROC_PARAMEXPRESSO) : les parametres de facturation du greffe, un par ligne, et l'association des modes de
/// paiement a leur banque. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_PARAMEXPRESSO (D:\RigApplication\Source\RIG\DLL\Processus\PROC_PARAMEXPRESSO ; Detail OPE de
/// proc_paramexpresso.md, qui fait foi).
///   - un parametre se lit « PARAMETRAGE d'abord, sinon la colonne de GREFFE_PARAMETRE de meme nom » (MetierParametrage.GetValue,
///     Metier\MetierParametrage.cs:71-92). La V3 a repris PARAMETRAGE dans soc.Reglage (Code, Valeur) et GREFFE_PARAMETRE dans
///     soc.ParametreGreffe (une ligne par colonne) : SOC-003, Serveur\Recopie\ReprisesPhase1.cs:47-50 ;
///   - « Paramètres généraux » : les cles d'OPE_FACTURATION (proc_paramexpresso.md:161-175) ; « Pied de page » : EXPRESSO_TYPE_PIED_PAGE
///     et GRPAR_GREFFE_ADR_COMPLETE (OPE_PIED_PAGE.cs:18-44) ; « Comptabilité » : GRPAR_COMPTA_TRESO_USED, GRPAR_COMPTA_BLOCAGE_AUTO,
///     SIREN_GREFFE, GRPAR_COMPTA_TYPE_COMPTA (OPE_COMPTABILITE_PARAMETRES_GENERAUX.cs:17-33) ; puis l'association banque de chaque mode
///     ACTIF, par valeur, groupe « Banques et modes » (MetierBanques, Metier\MetierBanques.cs:38-57 : MODE_PAIEMENT x MODE_PAIEMENT_BANQUE x BANQUE).
/// ETAT : parametre renseigne (ou mode associe a une banque) -> Validee « Enregistré » ; sinon AFaire « À renseigner » (GetValue rend
/// alors une chaine vide, MetierParametrage.cs:77).
/// AUCUN GESTE BRANCHE (27/11/2026) : DoValider (PROC_PARAMEXPRESSO.cs:39-54) ecrit d'un coup la VALEUR des cles PARAMETRAGE modifiees
/// (Parametrage.UpdateValeurParametrage, Metier\MetierParametrage.cs:42-47 -> soc.Reglage), les colonnes GREFFE_PARAMETRE (:49-51 ->
/// soc.ParametreGreffe), les liens mode / banque (MetierBanques.SaveAll, :74-… -> v2.MODE_PAIEMENT_BANQUE) et une demande
/// « Modification du paramétrage en base » close (:52-58). Decision du chef (plan-676a4b19, 27/11) : un geste ne change la Valeur d'un
/// code soc.Reglage existant QUE par l'ecriture partagee que le lot 14 tire de PUT RoutesGreffes.Reglage (ModuleGreffes.cs) ; elle
/// n'est pas encore sur main, et une seconde ne s'ecrit pas. GREFFE_PARAMETRE n'est pas couvert par cette decision. D'ici la, l'ecran
/// lit seulement ; l'association mode / banque s'ecrit deja par « Modes de paiement » (EcrivainModesPaiement, meme table).
/// </summary>
internal sealed class LecteurFacturationExpresso : ILecteurEcran
{
    public string Procedure => "PROC_PARAMEXPRESSO";

    internal const string Generaux = "Paramètres généraux", Banques = "Banques et modes", PiedDePage = "Pied de page", Comptabilite = "Comptabilité";

    /// <summary>Les parametres lus, par groupe, dans l'ordre des ecrans de la V2 (sources en tete du fichier).</summary>
    internal static readonly (string Groupe, string Cle)[] Parametres =
    [
        (Generaux, "GRPAR_ROMPU"), (Generaux, "EXPRESSO_RELANCE"), (Generaux, "EXPRESSO_REMBOURSEMENT"), (Generaux, "GRPAR_ADRESSE_IS_PRINCIPALE"),
        (Generaux, "GRPAR_COMMENTAIRE_COMPTE"), (Generaux, "GRPAR_COMMENTAIRE_COMPTE_PC"), (Generaux, "GRPAR_COMMENTAIRE_DIVERS"),
        (Generaux, "GRPAR_RELEVE_CLT_IS_SOLDE"), (Generaux, "EXPRESSO_EDITIONS_SYNTHETIQUES"), (Generaux, "EXPRESSO_RELEVE_MAIL_ENVOI"),
        (Generaux, "EXPRESSO_RELEVE_MAIL_COPIE"),
        (PiedDePage, "EXPRESSO_TYPE_PIED_PAGE"), (PiedDePage, "GRPAR_GREFFE_ADR_COMPLETE"),
        (Comptabilite, "GRPAR_COMPTA_TRESO_USED"), (Comptabilite, "GRPAR_COMPTA_BLOCAGE_AUTO"), (Comptabilite, "SIREN_GREFFE"),
        (Comptabilite, "GRPAR_COMPTA_TYPE_COMPTA"),
    ];

    private const string Requete = """
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total FROM (
            SELECT p.Rang, p.Groupe, p.Cle, COALESCE(r.Valeur, CONVERT(nvarchar(1000), g.Valeur)) AS Valeur,
                   CASE WHEN r.Code IS NOT NULL THEN 'réglage' WHEN g.Colonne IS NOT NULL THEN 'paramètre du greffe' END AS Source
            FROM (VALUES /*parametres*/) AS p (Rang, Groupe, Cle)
            LEFT JOIN soc.Reglage AS r ON r.Code = p.Cle
            LEFT JOIN soc.ParametreGreffe AS g ON g.Colonne = p.Cle
            UNION ALL
            -- MetierBanques : les modes ACTIFS, par valeur, et leur banque (aucune : la banque nulle de la V2, MetierBanques.cs:40-41)
            SELECT 1000, @banques, m.R_MODEPAIEME_VALEUR, b.BANQU_CODE, 'banque du mode ' + m.R_MODEPAIEME_LIBELLE_ECRAN
            FROM v2.MODE_PAIEMENT AS m
            LEFT JOIN v2.MODE_PAIEMENT_BANQUE AS mb ON mb.MODBQ_MODE_PAIEMENT = m.R_MODEPAIEME_VALEUR
            LEFT JOIN v2.BANQUE AS b ON b.BANQU_ID_BANQU = mb.MODBQ_ID_BANQU
            WHERE m.R_MODEPAIEME_UTILISABLE_SAISIE = 1
        ) AS l
        ORDER BY l.Rang, l.Cle
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // La liste des parametres, en parametres SQL (aucune valeur concatenee).
        var valeurs = string.Join(", ", Parametres.Select((p, i) => $"({i}, @g{i}, @c{i})"));
        var sql = Requete.Replace("/*parametres*/", valeurs, StringComparison.Ordinal);
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@banques", Banques) };
        for (var i = 0; i < Parametres.Length; i++)
        {
            parametres.Add(new SqlParameter($"@g{i}", Parametres[i].Groupe));
            parametres.Add(new SqlParameter($"@c{i}", Parametres[i].Cle));
        }
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var mode = Convert.ToInt32(r["Rang"], System.Globalization.CultureInfo.InvariantCulture) >= 1000;
        var valeur = FormeLigne.Texte(r, "Valeur");
        var source = FormeLigne.Texte(r, "Source");
        var precisions = mode
            ? FormeLigne.Joindre(source, valeur.Length > 0 ? $"banque {valeur}" : "aucune banque")
            : FormeLigne.Joindre(valeur.Length > 0 ? valeur : "non renseigné", source.Length > 0 ? source : null);
        return new LigneLue(mode ? $"Mode {FormeLigne.Texte(r, "Cle")}" : FormeLigne.Texte(r, "Cle"), precisions, FormeLigne.Texte(r, "Groupe"),
            valeur.Length > 0 ? "Validee" : "AFaire", []);
    }
}
