using System.Globalization;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// CE QUE LES RELANCES DE RIG v2 ECRIVENT EN PLUS DE LA RELANCE, partage par les ecrivains des relances de l'ecran reuni
/// « Relances et injonctions » (EcrivainRelancesComptesAnnuels, EcrivainRelancesBeneficiaires) : le moteur PROC_RSURV
/// (SURVEILLANCES\PROC_RSURV\PROC_RSURV.cs) est le meme pour toutes les natures de diligence, regle par nature, processus
/// RSURV et numero de relance dans NATURE_DILIGENCE_PARAM. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026 (code
/// sorti tel quel d'EcrivainRelancesComptesAnnuels.cs, ou il etait ecrit pour la seule nature SDCA).
///   - les reglages de la relance N (type de relance, delai, parametre d'edition, file d'impression) ;
///   - la date limite (NatureDiligence.CalculDateLimiteRelance) ;
///   - la demande RSURV validee d'un dossier, ou celle du lot (Demande.CreateDemande puis Cloturer(Validee)) ;
///   - le courrier de la relance (v2.DEMANDE_EDITION, GenererLesEditionsRelance, PROC_RSURV.cs:320-380).
/// </summary>
internal static class RelancesRsurv
{
    /// <summary>Les reglages de la relance N d'une nature, processus RSURV : type de relance, delai, parametre d'edition et file
    /// d'impression. Une base sans la table de reglages les laisse vides : relance « S », sans limite ni courrier.</summary>
    internal sealed record Reglages(string Type, string? Delai, string? CodeEdition, string? File);

    /// <summary>Les reglages de la relance <paramref name="relance"/> de la nature <paramref name="nature"/> (NATURE_DILIGENCE_PARAM,
    /// processus RSURV) ; type « S » sans reglage TypeRelance (PROC_RSURV.cs:334-335).</summary>
    internal static async Task<Reglages> ReglagesAsync(GesteEnCours geste, string nature, int relance, CancellationToken ct)
    {
        var valeurs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (await Scalaire(geste, "SELECT OBJECT_ID(N'v2.NATURE_DILIGENCE_PARAM')", ct) is not DBNull and not null)
            await using (var r = await geste.LireAsync("""
                SELECT NADIP_CODE_PARAM, NADIP_VALEUR_PARAM FROM v2.NATURE_DILIGENCE_PARAM
                WHERE NADIP_NATURE_DILIGENCE = @nature AND NADIP_CODE_PROCESSUS = 'RSURV' AND NADIP_NUM_ORDRE = @num
                  AND NADIP_CODE_PARAM IN ('TypeRelance', 'DelaiRelance', 'CodeParamEdition', 'FileImpression')
                """, ct, new SqlParameter("@nature", nature), new SqlParameter("@num", relance)))
                while (await r.ReadAsync(ct)) valeurs[FormeLigne.Texte(r, "NADIP_CODE_PARAM")] = FormeLigne.Texte(r, "NADIP_VALEUR_PARAM");
        string? Lu(string code) => valeurs.TryGetValue(code, out var v) && v.Length > 0 ? v : null;
        return new Reglages(Lu("TypeRelance") ?? "S", Lu("DelaiRelance"), Lu("CodeParamEdition"), Lu("FileImpression"));
    }

    /// <summary>La date limite d'apres le reglage DelaiRelance (« Jour=15 », « Mois=1 », « Annee=1 ») : la derniere cle lue
    /// l'emporte, comme NatureDiligence.CalculDateLimiteRelance (NatureDiligence.cs:233-249) ; null sans reglage.</summary>
    internal static DateTime? Limite(string? delai, DateTime jour)
    {
        if (string.IsNullOrWhiteSpace(delai)) return null;
        var cles = delai.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2))
                        .Where(x => x.Length == 2).ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.Ordinal);
        DateTime? limite = null;
        if (cles.TryGetValue("Jour", out var j) && int.TryParse(j, CultureInfo.InvariantCulture, out var nj)) limite = jour.AddDays(nj);
        if (cles.TryGetValue("Mois", out var m) && int.TryParse(m, CultureInfo.InvariantCulture, out var nm)) limite = jour.AddMonths(nm);
        if (cles.TryGetValue("Annee", out var a) && int.TryParse(a, CultureInfo.InvariantCulture, out var na)) limite = jour.AddYears(na);
        return limite;
    }

    /// <summary>Une demande RSURV validee (Demande.CreateDemande puis Cloturer(Validee), PROC_RSURV.cs:344-348 et :245-247) : du
    /// dossier <paramref name="idDossier"/>, ou celle du lot (null) ; <paramref name="idAction"/> = l'action RCS que la demande
    /// porte (trt.Dmnd.AddActionRcs, l. 163), null sinon.</summary>
    internal static async Task<(int? Id, string? Numero, string? Refus)> DemandeAsync(GesteEnCours geste, int? idDossier, string nom, string? indicatif,
        int? mere, string? parametres, int? idAction, CancellationToken ct)
    {
        var (numero, refus) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
        if (numero is null) return (null, null, refus);
        var valeurs = new Dictionary<string, object?>
        {
            ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = "V", ["DMND_CODE_PROSS"] = "RSURV",
            ["DMND_ID_DSRCS"] = idDossier, ["DMND_NOM"] = nom.Length > 255 ? nom[..255] : nom,
            ["DMND_NUM_INDICATIF"] = indicatif, ["DMND_LES_NUMEROS"] = indicatif, ["DMND_ID_DMND_MERE"] = mere,
            ["DMND_TEXTE_PARAMETRE"] = parametres, ["DMND_DATE_CREATION"] = geste.Maintenant, ["DMND_DATE_VALIDATION"] = geste.Maintenant,
            ["DMND_EN_COURS"] = false, ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche,
        };
        if (idAction is not null) valeurs["DMND_ID_ACTRC"] = idAction;
        var id = await CreationV2.InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", valeurs, ct);
        return (id, numero, null);
    }

    /// <summary>Le courrier de la relance (v2.DEMANDE_EDITION) du dossier <paramref name="idDossier"/>, si le parametre d'edition
    /// de ses reglages existe ; son libelle (celui du parametre + la nature, PROC_RSURV.cs:366), null sinon.</summary>
    internal static async Task<string?> CourrierAsync(GesteEnCours geste, int idDemande, int idDossier, string designation, string nature,
        Reglages reglages, CancellationToken ct)
    {
        if (reglages.CodeEdition is null || await Scalaire(geste, "SELECT OBJECT_ID(N'v2.PARAMETRE_EDITION')", ct) is DBNull or null) return null;
        // Les colonnes du parametre que la table du greffe porte (une base de banc n'en a qu'une partie).
        var colonnesParametre = await ColonnesAsync(geste, "v2.PARAMETRE_EDITION", ct);
        string Colonne(string nom) => colonnesParametre.Contains(nom) ? nom : $"NULL AS {nom}";
        (string Libelle, bool RectoVerso, bool Afficher, object? Cfecp, string TypeGeneration)? parametre = null;
        await using (var r = await geste.LireAsync($"""
            SELECT PRMED_LIBELLE, PRMED_RECTO_VERSO, PRMED_AFFICHER_AVANT_IMPRESSION, PRMED_CODE_CFECP,
                   {Colonne("PRMED_TYPE_GENERATION")}, {Colonne("PRMED_LISTE")}
            FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @code
            """, ct, new SqlParameter("@code", reglages.CodeEdition)))
            if (await r.ReadAsync(ct))
                parametre = (FormeLigne.Texte(r, "PRMED_LIBELLE"), r["PRMED_RECTO_VERSO"] is true, r["PRMED_AFFICHER_AVANT_IMPRESSION"] is true,
                             r["PRMED_CODE_CFECP"] is DBNull ? null : r["PRMED_CODE_CFECP"],
                             TypeGeneration(FormeLigne.Texte(r, "PRMED_TYPE_GENERATION"), FormeLigne.Texte(r, "PRMED_LISTE")));
        if (parametre is not { } p) return null;
        var libelle = $"{p.Libelle} {nature}";
        var valeurs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DMNED_ID_DMND"] = idDemande, ["DMNED_DATE_CREATION"] = geste.Maintenant, ["DMNED_TYPE_PAPIER"] = "NORMAL",
            ["DMNED_MEDIA"] = "IMPR", ["DMNED_ETAT_EMISSION"] = "N", ["DMNED_PHASE_TRAITEMENT"] = "C",
            ["DMNED_UTILISATEUR"] = geste.Agent.CompteWindows, ["DMNED_INTERLOCUTEUR"] = geste.Agent.CompteWindows,
            ["DMNED_NOMBRE_EXEMPLAIRE"] = 1, ["DMNED_IMPRESSION"] = true, ["DMNED_PAPIER_TRAME"] = false, ["DMNED_AGRAFAGE"] = false,
            ["DMNED_RECTO_VERSO"] = p.RectoVerso, ["DMNED_AFF_AVANT_IMPRESSION"] = p.Afficher, ["DMNED_LIBELLE_DOCUMENT"] = libelle,
            ["DMNED_GROUPAGE_AUTOMATIQUE"] = true, ["DMNED_RESUME"] = false, ["DMNED_READ_ONLY"] = false,
            ["DMNED_CODE_ENTITE_SOURCE"] = "DOSSIER_RCS", ["DMNED_VALEUR_ID_SOURCE"] = idDossier.ToString(CultureInfo.InvariantCulture),
            ["DMNED_CODE_CFECP"] = p.Cfecp, ["DMNED_LOT"] = reglages.File, ["DMNED_DESIGNATION_DESTINATAIRE"] = designation,
            // Le type de generation du parametre (CreateDemandeEditionByParametreGeneration, DemandeEdition.cs:2588-2589).
            ["DMNED_TYPE_GENERATION"] = p.TypeGeneration,
            // Mesure le 26/11/2026 (banc reuni au lot 6 de main, erreur 515 « Cannot insert the value NULL into column
            // 'DMNED_MASQUER_COFFRE_FORT' ») -- RIG_DEV donne a ces colonnes NOT NULL le defaut ((0)) (INFORMATION_SCHEMA de
            // SQL-DEV\DEV), que la base V3 n'a pas repris : la V2 les laisse a ce defaut, ecrit ici en clair (comme
            // EcrivainRegistresJudiciaires).
            ["DMNED_ENVOI_PAR_MAIL"] = false, ["DMNED_HAS_NUMLRAR"] = false, ["DMNED_IS_NON_SUPPRESSIBLE"] = false,
            ["DMNED_HAS_NUMLR"] = false, ["DMNED_MASQUER_COFFRE_FORT"] = false,
        };
        // Les colonnes que la table du greffe porte (une base de banc n'en a qu'une partie) : celles de RIG v2 sinon.
        var presentes = await ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        await geste.InsererAsync("v2.DEMANDE_EDITION", "DMNED_ID_DMNED",
            valeurs.Where(v => presentes.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);
        return libelle;
    }

    /// <summary>« ... : 2 courriers « … », « … » en file d'édition. », ou l'absence de parametre d'edition (pas de reglage
    /// CodeParamEdition, ou un reglage qui designe un parametre absent de la base : RIG v2 ne cree alors aucun courrier).</summary>
    internal static string Courriers(IReadOnlyCollection<string> courriers)
        => courriers.Count == 0
            ? "aucun courrier, pas de paramètre d'édition pour cette relance dans la base du greffe."
            : $"{courriers.Count} courrier{(courriers.Count > 1 ? "s" : "")} « {string.Join(" », « ", courriers.Distinct())} » en file d'édition.";

    /// <summary>Les colonnes de <paramref name="table"/> dans la base du greffe.</summary>
    internal static async Task<HashSet<string>> ColonnesAsync(GesteEnCours geste, string table, CancellationToken ct)
    {
        var colonnes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await geste.LireAsync("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@table)", ct,
                                                  new SqlParameter("@table", table));
        while (await r.ReadAsync(ct)) colonnes.Add(r.GetString(0));
        return colonnes;
    }

    /// <summary>La premiere valeur d'un SELECT dans la transaction du geste ; null sans ligne.</summary>
    internal static async Task<object?> Scalaire(GesteEnCours geste, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        await using var r = await geste.LireAsync(sql, ct, p);
        return await r.ReadAsync(ct) ? r.GetValue(0) : null;
    }

    /// <summary>Les types de generation de RIG v2 (DemandeEdition.eTypeGeneration, DemandeEdition.cs:160-206), par valeur.</summary>
    private static readonly string[] TypesGeneration =
        ["0", "EMISSION_SIMPLE", "FUSION_CRYSTAL", "FUSION_CRYSTAL_NET", "FUSION_RIG", "5", "KBIS", "REPORT_SQL", "RIG_LOT_UN", "FACTURE",
         "FUSION_ASPOSE", "REDA_CREATE", "REDA_EDITION", "REDA_DEPEND", "REDA_LETTRE", "BORDEREAU"];

    /// <summary>Le type de generation que la V2 ecrit : celui du parametre d'edition (sa colonne, ou l'attribut TYPE_GENERATION de
    /// sa liste XML), lu comme Enum.TryParse sans casse ni nombre hors enum (GetTypeGenerationFromString, DemandeEdition.cs:207-211),
    /// « 0 » s'il ne se lit pas ; INFOGREFFE est un alias de FUSION_RIG (l. 172).</summary>
    internal static string TypeGeneration(string colonne, string liste)
    {
        var brut = colonne.Length > 0 ? colonne
                 : System.Text.RegularExpressions.Regex.Match(liste, "TYPE_GENERATION=\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value : "";
        brut = brut.Trim();
        if (brut.Equals("INFOGREFFE", StringComparison.OrdinalIgnoreCase)) return "FUSION_RIG";
        if (int.TryParse(brut, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return n >= 0 && n < TypesGeneration.Length ? TypesGeneration[n] : n.ToString(CultureInfo.InvariantCulture);
        return TypesGeneration.FirstOrDefault(t => t.Equals(brut, StringComparison.OrdinalIgnoreCase) && !char.IsAsciiDigit(t[0])) ?? "0";
    }
}
