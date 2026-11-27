using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurTiersPayeurs;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Tiers payeurs » (PROC_GESTTIERSPAYEURS) : AJOUTER, MODIFIER et SUPPRIMER un tiers payeur, dans la grille. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurTiersPayeurs.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : OPE_TIERSPAYEURSDETAIL (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GESTTIERSPAYEURS), Valider (l. 188-244)
/// et Supprimer (l. 249-286) ; messages au mot pres.
///   - abrege obligatoire, 10 caracteres au plus, designation obligatoire (MetierObligatoire / MetierMaxLength,
///     OPE_TIERSPAYEURSDETAIL.Designer.cs:92-103) ;
///   - CREATION : « L'abrégé débiteur n'est pas valable » (DEBITEUR ou DÉBITEUR, l. 197-198) ; « L'abrégé saisi existe déjà »
///     (GetPayeurByAbrege, l. 201-202, sur toute la table PAYEUR, Payeur.cs:146) ; le payeur cree est principal (l. 204) ;
///   - MODIFICATION : « L'abregé débiteur n'est pas valable » (l. 221-222 -- la V2 l'ecrit ainsi en modification) ; l'unicite n'est
///     revue que si l'abrege change (l. 225-227) ; les abreges reserves ATTENTE et TPG sont en lecture seule (l. 76) ;
///   - chaque sauvegarde reecrit PYEUR_DATE_CREATION = maintenant (Payeur.Sauve surcharge, Payeur.cs:242-245) ;
///   - le code postal est nettoye par la liste noire de la V2, [\^',%.!$&amp;()\-=@{}] (CodePostal, l. 296) ;
///   - SUPPRESSION physique (Payeur.Delete, l. 271) refusee si le payeur a une ventilation (Ventilation.GetVentilationByPayeur) ou une
///     facture (Facture.GetFactureByIdPayeur), au message cumule de la V2 (l. 256-269) ; ses retours a la ligne deviennent des blancs
///     (un message d'ecran tient sur une ligne).
/// NON REPRIS (nommes) : la recherche de la ville par le code postal (procedure stockee RigChargerCodePostal, l. 301 : un programme de
/// la base, que le socle n'appelle pas) -- la ville saisie est gardee ; le pre-remplissage depuis un mandataire PC (l. 150-168, une aide
/// de saisie sur confirmation) ; la demande « GPAYEUR » creee et cloturee d'un coup a chaque geste (l. 209, 235, 276) -- une trace, que
/// le journal des gestes remplace (decision du plan).
/// TRACE : famille « reglage », « a cree » / « a modifie » / « a supprime ».
/// </summary>
internal sealed partial class EcrivainTiersPayeurs : IEcrivainEcran
{
    public string Procedure => "PROC_GESTTIERSPAYEURS";
    public string Ecran => "Tiers payeurs";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    internal const string DebiteurCreation = "L'abrégé débiteur n'est pas valable";
    internal const string DebiteurModification = "L'abregé débiteur n'est pas valable";
    internal const string AbregeExiste = "L'abrégé saisi existe déjà";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        TypeGeste.Supprimer => SupprimerAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    private sealed record Payeur(int Id, string Abrege, string Libelle, string Adresse, string Complement, string CodePostal, string Ville,
                                 string Courriel, bool Actif);

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private static bool Debiteur(string abrege) => abrege.ToUpperInvariant() is "DEBITEUR" or "DÉBITEUR";

    private static string? Obligatoires(Payeur p)
    {
        if (p.Abrege.Length == 0) return $"« {ColAbrege} » est obligatoire.";
        if (p.Abrege.Length > 10) return $"« {ColAbrege} » : 10 caractères au plus ({p.Abrege}).";
        if (p.Libelle.Length == 0) return $"« {ColDesignation} » est obligatoire.";
        if (p.Libelle.Length > 100) return $"« {ColDesignation} » : 100 caractères au plus.";
        return null;
    }

    private static async Task<bool> AbregePrisAsync(GesteEnCours geste, string abrege, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.PAYEUR WHERE PYEUR_ABREGE = @a", ct, new SqlParameter("@a", abrege));
        await r.ReadAsync(ct);
        return r.GetInt32(0) > 0;
    }

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        var p = new Payeur(0, Cellule(geste, ColAbrege) ?? "", Cellule(geste, ColDesignation) ?? "", Cellule(geste, ColAdresse) ?? "",
            Cellule(geste, ColComplement) ?? "", NettoyerCodePostal(Cellule(geste, ColCodePostal) ?? ""), Cellule(geste, ColVille) ?? "",
            Cellule(geste, ColCourriel) ?? "", (Cellule(geste, ColActif) ?? "oui") == "oui");
        if (Obligatoires(p) is { } manque) return IssueGeste.Refuser(manque);
        if (Debiteur(p.Abrege)) return IssueGeste.Refuser(DebiteurCreation);
        if (await AbregePrisAsync(geste, p.Abrege, ct)) return IssueGeste.Refuser(AbregeExiste);
        var nouveau = await geste.InsererAsync("v2.PAYEUR", "PYEUR_ID_PYEUR", new Dictionary<string, object?>
        {
            ["PYEUR_ABREGE"] = p.Abrege, ["PYEUR_LIBELLE"] = p.Libelle, ["PYEUR_LIGNE_ADRESSE"] = Nul(p.Adresse), ["PYEUR_SUITE_ADRESSE"] = Nul(p.Complement),
            ["PYEUR_CODE_POSTAL"] = Nul(p.CodePostal), ["PYEUR_LOCALITE_POSTALE"] = Nul(p.Ville), ["PYEUR_EMAIL"] = Nul(p.Courriel),
            ["PYEUR_IS_PRINCIPAL"] = true, ["PYEUR_IS_ACTIF"] = p.Actif, ["PYEUR_DATE_CREATION"] = geste.Maintenant,
        }, ct);
        var id = nouveau.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit($"Tiers payeur {p.Abrege} créé : {p.Libelle}.", [id],
            new TraceGeste("reglage", $"le tiers payeur {p.Abrege}", id, "a cree", null, p with { Id = 0 }));
    }

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (CriteresConsultation.Entier(id) is not { } n || await LireAsync(geste, n, ct) is not { } avant)
            return IssueGeste.Refuser($"Tiers payeur introuvable dans la base du greffe ({id}).");
        var apres = avant with
        {
            Abrege = Cellule(geste, ColAbrege) ?? avant.Abrege, Libelle = Cellule(geste, ColDesignation) ?? avant.Libelle,
            Adresse = Cellule(geste, ColAdresse) ?? avant.Adresse, Complement = Cellule(geste, ColComplement) ?? avant.Complement,
            CodePostal = Cellule(geste, ColCodePostal) is { } cp ? NettoyerCodePostal(cp) : avant.CodePostal, Ville = Cellule(geste, ColVille) ?? avant.Ville,
            Courriel = Cellule(geste, ColCourriel) ?? avant.Courriel, Actif = Cellule(geste, ColActif) is { } a ? a == "oui" : avant.Actif,
        };
        if (apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");
        if (Obligatoires(apres) is { } manque) return IssueGeste.Refuser(manque);
        if (apres.Abrege != avant.Abrege && avant.Abrege is "ATTENTE" or "TPG")
            return IssueGeste.Refuser($"L'abrégé {avant.Abrege} est réservé : il ne se modifie pas (RIG v2 le met en lecture seule).");
        if (Debiteur(apres.Abrege)) return IssueGeste.Refuser(DebiteurModification);
        if (apres.Abrege != avant.Abrege && await AbregePrisAsync(geste, apres.Abrege, ct)) return IssueGeste.Refuser(AbregeExiste);
        await geste.EcrireAsync("""
            UPDATE v2.PAYEUR SET PYEUR_ABREGE = @abrege, PYEUR_LIBELLE = @libelle, PYEUR_LIGNE_ADRESSE = @adresse, PYEUR_SUITE_ADRESSE = @complement,
                PYEUR_CODE_POSTAL = @cp, PYEUR_LOCALITE_POSTALE = @ville, PYEUR_EMAIL = @courriel, PYEUR_IS_ACTIF = @actif, PYEUR_DATE_CREATION = @maintenant
            WHERE PYEUR_ID_PYEUR = @id
            """, ct, new SqlParameter("@abrege", apres.Abrege), new SqlParameter("@libelle", apres.Libelle),
            new SqlParameter("@adresse", Nul(apres.Adresse) ?? DBNull.Value), new SqlParameter("@complement", Nul(apres.Complement) ?? DBNull.Value),
            new SqlParameter("@cp", Nul(apres.CodePostal) ?? DBNull.Value), new SqlParameter("@ville", Nul(apres.Ville) ?? DBNull.Value),
            new SqlParameter("@courriel", Nul(apres.Courriel) ?? DBNull.Value), new SqlParameter("@actif", apres.Actif),
            new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", n));
        return IssueGeste.Ecrit($"Tiers payeur {apres.Abrege} enregistré.", [id],
            new TraceGeste("reglage", $"le tiers payeur {apres.Abrege}", id, "a modifie", avant, apres));
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (CriteresConsultation.Entier(id) is not { } n || await LireAsync(geste, n, ct) is not { } p)
            return IssueGeste.Refuser($"Tiers payeur introuvable dans la base du greffe ({id}).");
        int ventilations, factures;
        await using (var r = await geste.LireAsync("""
                         SELECT (SELECT COUNT(*) FROM v2.VENTILATION WHERE VENTI_ID_PYEUR = @id), (SELECT COUNT(*) FROM v2.FACTURE WHERE FACTU_ID_PYEUR = @id)
                         """, ct, new SqlParameter("@id", n)))
        {
            await r.ReadAsync(ct);
            ventilations = r.GetInt32(0);
            factures = r.GetInt32(1);
        }
        // Le message cumule de la V2 (l. 256-269), ses « \n » en blancs.
        if (ventilations > 0 || factures > 0)
            return IssueGeste.Refuser(("Suppression impossible : "
                + (ventilations > 0 ? "- Ce payeur est actuellement associé à une ou plusieurs ventilations " : "")
                + (factures > 0 ? "- Ce payeur est actuellement associé à une ou plusieurs factures " : "")).TrimEnd());
        await geste.EcrireAsync("DELETE FROM v2.PAYEUR WHERE PYEUR_ID_PYEUR = @id", ct, new SqlParameter("@id", n));
        // « Suppression réussie » (l. 278).
        return IssueGeste.Ecrit($"Suppression réussie : le tiers payeur {p.Abrege} ({p.Libelle}) est supprimé.", [id],
            new TraceGeste("reglage", $"le tiers payeur {p.Abrege}", id, "a supprime", p, null));
    }

    private static async Task<Payeur?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT PYEUR_ID_PYEUR, PYEUR_ABREGE, PYEUR_LIBELLE, PYEUR_LIGNE_ADRESSE, PYEUR_SUITE_ADRESSE, PYEUR_CODE_POSTAL, PYEUR_LOCALITE_POSTALE,
                   PYEUR_EMAIL, PYEUR_IS_ACTIF
            FROM v2.PAYEUR WITH (UPDLOCK) WHERE PYEUR_ID_PYEUR = @id AND PYEUR_IS_PRINCIPAL = 1
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Payeur(id, FormeLigne.Texte(r, "PYEUR_ABREGE"), FormeLigne.Texte(r, "PYEUR_LIBELLE"), FormeLigne.Texte(r, "PYEUR_LIGNE_ADRESSE"),
            FormeLigne.Texte(r, "PYEUR_SUITE_ADRESSE"), FormeLigne.Texte(r, "PYEUR_CODE_POSTAL"), FormeLigne.Texte(r, "PYEUR_LOCALITE_POSTALE"),
            FormeLigne.Texte(r, "PYEUR_EMAIL"), r.GetBoolean(r.GetOrdinal("PYEUR_IS_ACTIF")));
    }

    private static object? Nul(string valeur) => valeur.Length == 0 ? null : valeur;

    /// <summary>La liste noire de la V2 (OPE_TIERSPAYEURSDETAIL.cs:296) : ces caracteres sont retires du code postal.</summary>
    internal static string NettoyerCodePostal(string cp) => CaracteresInterdits().Replace(cp, "");

    [GeneratedRegex(@"[\^',%.!$&()\-=@{}]")]
    private static partial Regex CaracteresInterdits();
}
