using RigV3.Contrats.EcransGeneres;
using RigV3Desktop.Pont.EcransGeneres;

namespace RigV3Desktop.Pont;

/// <summary>L'extrait Kbis d'un dossier lu dans la base, tel que l'écran le montre.</summary>
/// <param name="Mode">Celui de la fiche (Pont/EcranGenere.cs, VueFicheEcran) : « base », « exemple » ou « erreur ».</param>
/// <param name="Message">Pourquoi il n'y a pas d'extrait ; null s'il y en a un.</param>
/// <param name="Kbis">L'extrait, calculé par <see cref="ApercuKbis.Vue"/> ; null hors du mode « base ».</param>
public sealed record VueApercuKbisDossier(string Mode, string? Message, VueKbis? Kbis);

/// <summary>
/// L'APERÇU KBIS d'un VRAI dossier (plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026) : l'écran d'une opération dont le
/// descripteur nomme une section <see cref="DescripteurEcran.ApercuKbis"/> (PROC_XEX, édition interne d'un Kbis) la dessine en
/// extrait (web/src/composants/ApercuKbis.tsx) au lieu de ses champs. Les valeurs viennent de la FICHE lue dans la base
/// (EcranGenere.FicheAsync ; Serveur/Socle/EcransGeneres/LecteurKbisInterne.cs) ; la mise en page et les règles de l'extrait
/// restent celles de <see cref="ApercuKbis.Vue"/>, telles quelles (aucun doublon) : chaque colonne de RIG v2 qu'il lit est ici le
/// champ de même sens de la section. Le greffe de l'en-tête : le code greffe du siège, celui de la session à défaut (le dossier est
/// dans la base de ce greffe).
/// </summary>
public static class ApercuKbisDossier
{
    /// <summary>La colonne de RIG v2 que <see cref="ApercuKbis.Vue"/> lit -> le champ de la section « Extrait Kbis » de la fiche
    /// (libellés du descripteur, Pont/EcransGeneres/Facturer.cs).</summary>
    private static readonly Dictionary<string, string> Champs = new(StringComparer.Ordinal)
    {
        ["ETBLS_CODE_GREFFE"] = "Code greffe", ["DSSRC_DATE_IMMAT"] = "Date d'immatriculation",
        ["INSPM_DENOMINATION_STATUT"] = "Dénomination", ["CPTL_MONTANT"] = "Capital", ["CPTL_VARIABLE"] = "Capital variable",
        ["CPTL_MONNAIE"] = "Monnaie", ["ETBLS_ID_ADRBS"] = "Adresse du siège", ["ETBLS_DATE_DEBUT"] = "Date de début",
        ["DRGNT_FONCTION_DIRIGEANT"] = "Qualité du dirigeant", ["DRGNT_DATE_ENTREE"] = "Entrée en fonction",
        ["DRGNT_NUM_SIREN"] = "SIREN du dirigeant", ["ACTVT_ACTIVITE_DECLAREE"] = "Activité", ["ACTVT_DATE_DEBUT"] = "Début d'activité",
        ["ACTVT_CODE_NAF"] = "Code NAF", ["ETBLS_ORIGINE_FONDS"] = "Origine du fonds", ["ETBLS_MODE_EXPLOITATION"] = "Mode d'exploitation",
    };

    [JSInvokable("ApercuKbis.Dossier")]
    public static Task<VueApercuKbisDossier> Dossier(string code, string groupe, string? id)
        => DossierAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, code, groupe, id);

    /// <summary>L'extrait du dossier <paramref name="id"/> de l'écran <paramref name="code"/> (son groupe <paramref name="groupe"/>).</summary>
    public static async Task<VueApercuKbisDossier> DossierAsync(ClientServeur? serveur, string? greffe, string code, string groupe, string? id)
    {
        var section = CatalogueGenere.Descripteur(code)?.ApercuKbis
            ?? throw new ArgumentException($"{code} ne dessine aucun extrait Kbis (DescripteurEcran.ApercuKbis).", nameof(code));
        var lue = await EcranGenere.FicheAsync(serveur, greffe, code, groupe, id);
        if (lue.Fiche is not { } fiche) return new VueApercuKbisDossier(lue.Mode, lue.Message, null);
        return new VueApercuKbisDossier(lue.Mode, null, Vue(fiche, section, greffe));
    }

    /// <summary>L'extrait de la fiche <paramref name="fiche"/>, depuis sa section <paramref name="section"/>.</summary>
    internal static VueKbis Vue(FicheEcran fiche, string section, string? greffe)
    {
        var champs = fiche.Sections.FirstOrDefault(s => s.Titre == section)?.Champs ?? [];
        string Champ(string libelle) => champs.FirstOrDefault(c => c.Libelle == libelle)?.Valeur ?? "";
        string Lire(string etape, string colonne)
        {
            if (colonne == "DSSRC_NUM_GESTION") return fiche.Nom;
            var valeur = Champs.TryGetValue(colonne, out var libelle) ? Champ(libelle) : "";
            return colonne switch
            {
                "ETBLS_CODE_GREFFE" when valeur.Length == 0 => greffe ?? "",
                // Une case de la fiche vaut « oui » ; l'apercu lit le « Oui » du formulaire.
                "CPTL_VARIABLE" => valeur == "oui" ? "Oui" : "",
                _ => valeur,
            };
        }
        return ApercuKbis.Vue(fiche.Nom, Champ("SIREN"), Champ("Dénomination"), Champ("Forme juridique"), Lire);
    }
}
