using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurNaturesDocument;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Natures de document » (PROC_GED_NATURE_DOC) : AJOUTER, MODIFIER et SUPPRIMER une nature, dans la grille. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurNaturesDocument.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : OPE_GED_NATURE_DOC_EDIT (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GEDJUD\PROC_GED_NATURE_DOC\
/// ETP_GED_NATURE_DOC_EDIT), messages au mot pres :
///   - VERIFIER (DoVerifier, l. 229-244), dans l'ordre de la V2 : en ajout, un code deja porte par une nature -> « Une nature existe deja
///     avec ce code. » ; code vide -> « Le code de nature est manquant » ; libelle vide -> « Le libellé est manquant » ; une quote dans le
///     code -> « Un code de nature ne peut contenir le caractère quote (') » ;
///   - AJOUTER (_Valider, l. 250-266) : une ligne VIDIGED_PARAMETRAGE de type NATURE_DOC, son code, son libelle, son type de document ;
///   - MODIFIER : le code est en lecture seule en modification (ChargerInfos, l. 202) -- le changer est refuse ; libelle et type de
///     document s'ecrivent (l. 255, 265) ;
///   - SUPPRIMER (Toolbar_ButtonClick, l. 115-160) : refusee si un document de la GED porte cette nature (VidigedIndex.GetVidigedIndexParNature,
///     l. 117-119 : « Cette nature de document est déjà associée à un document. » + « Vous ne pouvez pas la supprimer », le retour a la
///     ligne devenu un blanc) ; refusee si c'est la nature par defaut (la ligne de libelle NATURE_DOC de type PARAM_DEFAUT qui porte ce
///     code, l. 122-124) ; sinon la nature liee au deposant (NATURE_DOC_DEPOSANT de meme code, l. 129-134) est supprimee, puis la nature.
///   - Le type de document est une valeur de WEBS_DOC_REGROUPEMENT (la liste ComboWebsDocRegroupement, l. 225) : son code ou son libelle.
/// NON REPRIS (nommes) : les autres reglages GED de la saisie (evenements de depot, centralisation GEIDE, coffre-fort, copie conforme,
/// edition CFEC, Tribunal digital, type de deposant) -- des listes tirees de CODE_EVE_PRO, DEMAT_GEIDE_PARAMETRAGE et PARAMETRE_EDITION
/// (l. 53-83) que la grille ne porte pas : ils se lisent dans la fiche et restent tels quels ; une nature ajoutee les a vides ; la demande
/// RIG v2 creee puis cloturee a chaque geste (CreateAndLockDemande / Cloturer, l. 131-145, 316-319) -- une trace, que le journal des
/// gestes remplace (decision du plan), et le refus « demande en cours » qui l'accompagne (aucune demande n'est ouverte par la V3).
/// TRACE : famille « reglage », « a cree » / « a modifie » / « a supprime ».
/// </summary>
internal sealed class EcrivainNaturesDocument : IEcrivainEcran
{
    public string Procedure => "PROC_GED_NATURE_DOC";
    public string Ecran => "Natures de document";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    internal const string NatureExistante = "Une nature existe déjà avec ce code.";
    internal const string CodeManquant = "Le code de nature est manquant";
    internal const string LibelleManquant = "Le libellé est manquant";
    internal const string CodeAvecQuote = "Un code de nature ne peut contenir le caractère quote (')";
    internal const string DejaAssociee = "Cette nature de document est déjà associée à un document. Vous ne pouvez pas la supprimer";
    internal const string ParDefaut = "Cette nature de document est utilisée comme valeur par défaut. Vous ne pouvez pas la supprimer à moins de changer le paramétrage.";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        TypeGeste.Supprimer => SupprimerAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    private sealed record Nature(int Id, string Code, string Libelle, string TypeDocument);

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private static async Task<int> CompterAsync(GesteEnCours geste, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        await using var r = await geste.LireAsync(sql, ct, p);
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    /// <summary>Le code WEBS_DOC_REGROUPEMENT d'un type saisi (son code ou son libelle) ; "" pour aucun ; null s'il n'existe pas.</summary>
    private static async Task<string?> TypeDocumentAsync(GesteEnCours geste, string saisi, CancellationToken ct)
    {
        if (saisi.Length == 0) return "";
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) R_WEBSDOCREG_VALEUR FROM v2.WEBS_DOC_REGROUPEMENT
            WHERE R_WEBSDOCREG_VALEUR = @t OR R_WEBSDOCREG_LIBELLE_ECRAN = @t ORDER BY R_WEBSDOCREG_NUM_ORDRE
            """, ct, new SqlParameter("@t", saisi));
        return await r.ReadAsync(ct) ? r.GetString(0).Trim() : null;
    }

    private static async Task<string> TypesPermisAsync(GesteEnCours geste, CancellationToken ct)
    {
        var types = new List<string>();
        await using var r = await geste.LireAsync("SELECT R_WEBSDOCREG_LIBELLE_ECRAN FROM v2.WEBS_DOC_REGROUPEMENT ORDER BY R_WEBSDOCREG_NUM_ORDRE", ct);
        while (await r.ReadAsync(ct)) types.Add(r.GetString(0).Trim());
        return string.Join(", ", types);
    }

    /// <summary>Les controles de DoVerifier (l. 229-244), dans l'ordre de la V2 : le premier en defaut est le refus.</summary>
    private static async Task<string?> VerifierAsync(GesteEnCours geste, Nature n, bool ajout, CancellationToken ct)
    {
        if (ajout && n.Code.Length > 0
            && await CompterAsync(geste, "SELECT COUNT(*) FROM v2.VIDIGED_PARAMETRAGE WHERE VGPRM_TYPE = 'NATURE_DOC' AND VGPRM_CODE = @c", ct,
                new SqlParameter("@c", n.Code)) > 0)
            return NatureExistante;
        if (n.Code.Length == 0) return CodeManquant;
        if (n.Libelle.Length == 0) return LibelleManquant;
        if (n.Code.Contains('\'', StringComparison.Ordinal)) return CodeAvecQuote;
        if (n.Code.Length > 50) return $"« {ColCode} » : 50 caractères au plus.";
        if (n.Libelle.Length > 1000) return $"« {ColLibelle} » : 1000 caractères au plus.";
        return null;
    }

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        var saisi = Cellule(geste, ColType) ?? "";
        var n = new Nature(0, Cellule(geste, ColCode) ?? "", Cellule(geste, ColLibelle) ?? "", saisi);
        if (await VerifierAsync(geste, n, ajout: true, ct) is { } refus) return IssueGeste.Refuser(refus);
        if (await TypeDocumentAsync(geste, saisi, ct) is not { } type)
            return IssueGeste.Refuser($"« {ColType} » : « {saisi} » n'est pas un type de document ({await TypesPermisAsync(geste, ct)}).");
        var nouveau = await geste.InsererAsync("v2.VIDIGED_PARAMETRAGE", "VGPRM_ID_VGPRM", new Dictionary<string, object?>
        {
            ["VGPRM_TYPE"] = NatureDoc, ["VGPRM_CODE"] = n.Code, ["VGPRM_LIBELLE"] = n.Libelle, ["VGPRM_WEBS_DOC_REGROUPEMENT"] = type.Length == 0 ? null : type,
        }, ct);
        var id = nouveau.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit($"Nature de document {n.Code} créée : {n.Libelle}.", [id],
            new TraceGeste("reglage", $"la nature de document {n.Code}", id, "a cree", null, n with { TypeDocument = type }));
    }

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (CriteresConsultation.Entier(id) is not { } idn || await LireAsync(geste, idn, ct) is not { } avant)
            return IssueGeste.Refuser($"Nature de document introuvable dans la base du greffe ({id}).");
        if (Cellule(geste, ColCode) is { } code && code != avant.Code)
            return IssueGeste.Refuser($"Le code d'une nature ne se modifie pas ({avant.Code}) : RIG v2 le met en lecture seule.");
        var saisi = Cellule(geste, ColType);
        var type = saisi is null ? avant.TypeDocument : await TypeDocumentAsync(geste, saisi, ct);
        if (type is null) return IssueGeste.Refuser($"« {ColType} » : « {saisi} » n'est pas un type de document ({await TypesPermisAsync(geste, ct)}).");
        var apres = avant with { Libelle = Cellule(geste, ColLibelle) ?? avant.Libelle, TypeDocument = type };
        if (apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");
        if (await VerifierAsync(geste, apres, ajout: false, ct) is { } refus) return IssueGeste.Refuser(refus);
        await geste.EcrireAsync("UPDATE v2.VIDIGED_PARAMETRAGE SET VGPRM_LIBELLE = @l, VGPRM_WEBS_DOC_REGROUPEMENT = @t WHERE VGPRM_ID_VGPRM = @id", ct,
            new SqlParameter("@l", apres.Libelle), new SqlParameter("@t", apres.TypeDocument.Length == 0 ? DBNull.Value : apres.TypeDocument),
            new SqlParameter("@id", idn));
        return IssueGeste.Ecrit($"Nature de document {apres.Code} enregistrée.", [id],
            new TraceGeste("reglage", $"la nature de document {apres.Code}", id, "a modifie", avant, apres));
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (CriteresConsultation.Entier(id) is not { } idn || await LireAsync(geste, idn, ct) is not { } n)
            return IssueGeste.Refuser($"Nature de document introuvable dans la base du greffe ({id}).");
        var code = new SqlParameter("@c", n.Code);
        if (await CompterAsync(geste, "SELECT COUNT(*) FROM v2.VIDIGED_INDEX WHERE VGIDX_NATURE_DOC = @c", ct, code) > 0)
            return IssueGeste.Refuser(DejaAssociee);
        // La nature par defaut (l. 122-123) : la premiere ligne de libelle NATURE_DOC (GetVidigedParametrageByLibelle), de type PARAM_DEFAUT
        // et de ce code.
        if (await CompterAsync(geste, """
                SELECT COUNT(*) FROM (SELECT TOP (1) VGPRM_TYPE, VGPRM_CODE FROM v2.VIDIGED_PARAMETRAGE WHERE VGPRM_LIBELLE = 'NATURE_DOC'
                                      ORDER BY VGPRM_ID_VGPRM) AS d
                WHERE d.VGPRM_TYPE = 'PARAM_DEFAUT' AND d.VGPRM_CODE = @c
                """, ct, new SqlParameter("@c", n.Code)) > 0)
            return IssueGeste.Refuser(ParDefaut);
        var deposants = await geste.EcrireAsync("DELETE FROM v2.VIDIGED_PARAMETRAGE WHERE VGPRM_TYPE = 'NATURE_DOC_DEPOSANT' AND VGPRM_CODE = @c", ct,
            new SqlParameter("@c", n.Code));
        await geste.EcrireAsync("DELETE FROM v2.VIDIGED_PARAMETRAGE WHERE VGPRM_ID_VGPRM = @id", ct, new SqlParameter("@id", idn));
        // « Suppression réalisée avec succès » (l. 154).
        return IssueGeste.Ecrit($"Suppression réalisée avec succès : la nature {n.Code} ({n.Libelle}) est supprimée"
                                + (deposants > 0 ? ", avec son type de déposant." : "."), [id],
            new TraceGeste("reglage", $"la nature de document {n.Code}", id, "a supprime", n, null));
    }

    private static async Task<Nature?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT VGPRM_CODE, VGPRM_LIBELLE, VGPRM_WEBS_DOC_REGROUPEMENT FROM v2.VIDIGED_PARAMETRAGE WITH (UPDLOCK)
            WHERE VGPRM_ID_VGPRM = @id AND VGPRM_TYPE = 'NATURE_DOC'
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Nature(id, FormeLigne.Texte(r, "VGPRM_CODE"), FormeLigne.Texte(r, "VGPRM_LIBELLE"), FormeLigne.Texte(r, "VGPRM_WEBS_DOC_REGROUPEMENT"));
    }
}
