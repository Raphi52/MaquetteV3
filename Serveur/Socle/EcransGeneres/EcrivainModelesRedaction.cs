using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Modèles de rédaction » (PROC_REDAA), en grille : AJOUTER, MODIFIER et SUPPRIMER une entree du GLOSSAIRE, comme l'onglet
/// glossaire de la V2. Mode d'emploi : EcrivainEcran.cs ; la liste et la fiche : LecteurModelesRedaction.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_REDAA\ETP_GLOSSAIRE.
///   - les entrees de l'agent : les publiques et SES privees (Glossaire.GetAllGlossaireOfUserAndPublic, RigMetier\JUDICIAIRE\
///     Glossaire.cs:64-66 ; OPE_RECH_GLOSSAIRE.cs:96) -- une autre se dit introuvable ; « l'utilisateur courant » est
///     UTLST_LOGIN = le compte Windows (Utilisateur.GetUtilisateurCourant, RigMetier\Utilisateur.cs:424-431) ;
///   - la saisie (OPE_EDT_GLOSSAIRE.Designer.cs:79-100) : « Code » et « Texte associé » obligatoires, la description (ici la colonne
///     « Libellé ») facultative ; leur largeur, celle des colonnes de la base du greffe (COL_LENGTH : 20, 70, 8000) ;
///   - le code ne se modifie plus une fois l'entree creee (TextCode.MetierReadOnly = !modeCrea, OPE_EDT_GLOSSAIRE.cs:117) ; une
///     entree publique ne redevient pas privee (GrbPublicPrive en lecture, l. 118) ;
///   - Valider (l. 66-75) : GLOSS_PROPRIETAIRE = '*' (public) ou le login de l'agent (prive), puis Sauve ; Supprimer (l. 84-91) :
///     Delete, sans autre condition. Un code deja pris : la cle GLOSS_CODE_GLOSSAIRE (cle primaire) le refuse en base, ici avant.
///   - la V2 n'a pas de message de reussite (la liste se met a jour, MajListe) : ceux d'ici disent ce qui est ecrit.
/// NON REPRIS (nommes) : les MODELES et MAQUETTES se creent, se dupliquent et se suppriment avec leur document Word en GED
/// (OPE_EDT_MAQ.cs:103-147, :164-192) ; les SECTIONS ne s'editent que par l'equipe Amitel (barre d'edition cachee sinon,
/// OPE_EDT_SECTION.cs:64-72), et leur fiche (requete SQL, niveaux de fusion, generation, occurrence, persistance) n'est pas branchee
/// ici : un agent Amitel la modifie dans RIG v2. La « Generation du document » du glossaire (OPE_GEN_DOC_GLOSSAIRE) : un document Word.
/// TRACE : famille « reglage », « a cree / a modifie / a supprime », l'entree avant et apres.
/// </summary>
internal sealed class EcrivainModelesRedaction : IEcrivainEcran
{
    public string Procedure => "PROC_REDAA";
    public string Ecran => "Modèles de rédaction";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    private const string ColCode = "Code", ColLibelle = "Libellé";
    private static readonly string[] ColonnesLues = ["Nature", "Utilisation"];

    internal const string SeulGlossaire = "Seul le glossaire s'ajoute ici, avec son « Texte associé » : un modèle ou une maquette se crée par son document Word en GED, une section par l'équipe Amitel dans RIG v2.";
    internal const string ModeleOuMaquette = "Un modèle ou une maquette se modifie et se supprime avec son document Word en GED : RIG v2 le fait dans sa fiche (OPE_EDT_MAQ), pas dans cette grille.";
    internal const string SectionAmitel = "Les sections ne se modifient que par l'équipe Amitel : RIG v2 cache leur barre d'édition aux autres agents.";
    internal const string SectionNonBranchee = "La fiche d'une section (sa requête SQL et ses niveaux) n'est pas reprise dans RIG v3 : modifiez-la dans RIG v2.";
    internal const string CodeFige = "Le code ne se modifie pas : RIG v2 le fige une fois l'entrée du glossaire enregistrée.";
    internal const string PubliqueFigee = "Une entrée publique ne redevient pas privée : RIG v2 fige ce choix une fois l'entrée publique.";
    internal const string RienAEnregistrer = "Rien à enregistrer : aucune valeur ne change.";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        TypeGeste.Supprimer => SupprimerAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        var texte = Saisi(geste, LecteurModelesRedaction.ChampTexte);
        var prive = Saisi(geste, LecteurModelesRedaction.ChampPrive);
        // Un ajout hors de l'onglet du glossaire n'a pas sa section (SectionsAjout du descripteur) : rien a creer ici.
        if (texte is null && prive is null) return IssueGeste.Refuser(SeulGlossaire);
        var code = Cellule(geste, ColCode) ?? "";
        var libelle = Cellule(geste, ColLibelle) ?? "";
        if (code.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ColCode));
        if (string.IsNullOrEmpty(texte)) return IssueGeste.Refuser(CreationV2.Obligatoire(LecteurModelesRedaction.ChampTexte));
        var estPrive = Coche(prive);
        var proprietaire = estPrive ? Login(geste) : "*";
        if (await TropLongAsync(geste, ct, ("GLOSS_CODE_GLOSSAIRE", ColCode, code), ("GLOSS_LIBELLE", ColLibelle, libelle),
                ("GLOSS_TEXTE", LecteurModelesRedaction.ChampTexte, texte), ("GLOSS_PROPRIETAIRE", "Propriétaire", proprietaire)) is { } long_)
            return IssueGeste.Refuser(long_);
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.GLOSSAIRE WITH (UPDLOCK, HOLDLOCK) WHERE GLOSS_CODE_GLOSSAIRE = @c", ct,
                         new SqlParameter("@c", code)))
            if (await r.ReadAsync(ct) && r.GetInt32(0) > 0) return IssueGeste.Refuser($"Le code « {code} » existe déjà dans le glossaire.");
        await geste.EcrireAsync("""
            INSERT INTO v2.GLOSSAIRE (GLOSS_CODE_GLOSSAIRE, GLOSS_LIBELLE, GLOSS_TEXTE, GLOSS_PROPRIETAIRE) VALUES (@c, @l, @t, @p)
            """, ct, new SqlParameter("@c", code), Valeur("@l", libelle), new SqlParameter("@t", texte), new SqlParameter("@p", proprietaire));
        var id = LecteurModelesRedaction.Prefixes[3] + code;
        return IssueGeste.Ecrit(estPrive
                ? $"Entrée « {code} » ajoutée à votre glossaire privé : la liste ne montre que le glossaire public."
                : $"Entrée « {code} » ajoutée au glossaire public.", [id],
            new TraceGeste("reglage", $"l'entrée du glossaire {code}", id, "a cree", null, Etat(libelle, texte, proprietaire)));
    }

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (await EntreeAsync(geste, ct) is not { } e) return IssueGeste.Refuser(await PasDuGlossaireAsync(geste));
        if (Cellule(geste, ColCode) is { } code && code != e.Code) return IssueGeste.Refuser(CodeFige);
        foreach (var lue in ColonnesLues)
            if (Cellule(geste, lue) is not null) return IssueGeste.Refuser($"« {lue} » se lit, ne se saisit pas.");
        var libelle = Cellule(geste, ColLibelle) ?? e.Libelle;
        var texte = Saisi(geste, LecteurModelesRedaction.ChampTexte) ?? e.Texte;
        var prive = Saisi(geste, LecteurModelesRedaction.ChampPrive) is { } p ? Coche(p) : e.Proprietaire != "*";
        if (e.Proprietaire == "*" && prive) return IssueGeste.Refuser(PubliqueFigee);
        if (texte.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(LecteurModelesRedaction.ChampTexte));
        var proprietaire = prive ? e.Proprietaire : "*";
        if (libelle == e.Libelle && texte == e.Texte && proprietaire == e.Proprietaire) return IssueGeste.Refuser(RienAEnregistrer);
        if (await TropLongAsync(geste, ct, ("GLOSS_LIBELLE", ColLibelle, libelle), ("GLOSS_TEXTE", LecteurModelesRedaction.ChampTexte, texte)) is { } long_)
            return IssueGeste.Refuser(long_);
        await geste.EcrireAsync("""
            UPDATE v2.GLOSSAIRE SET GLOSS_LIBELLE = @l, GLOSS_TEXTE = @t, GLOSS_PROPRIETAIRE = @p WHERE GLOSS_CODE_GLOSSAIRE = @c
            """, ct, Valeur("@l", libelle), new SqlParameter("@t", texte), new SqlParameter("@p", proprietaire), new SqlParameter("@c", e.Code));
        var id = LecteurModelesRedaction.Prefixes[3] + e.Code;
        return IssueGeste.Ecrit($"Entrée « {e.Code} » du glossaire enregistrée.", [id],
            new TraceGeste("reglage", $"l'entrée du glossaire {e.Code}", id, "a modifie", Etat(e.Libelle, e.Texte, e.Proprietaire), Etat(libelle, texte, proprietaire)));
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (await EntreeAsync(geste, ct) is not { } e) return IssueGeste.Refuser(await PasDuGlossaireAsync(geste));
        await geste.EcrireAsync("DELETE FROM v2.GLOSSAIRE WHERE GLOSS_CODE_GLOSSAIRE = @c", ct, new SqlParameter("@c", e.Code));
        var id = LecteurModelesRedaction.Prefixes[3] + e.Code;
        return IssueGeste.Ecrit($"Entrée « {e.Code} » supprimée du glossaire.", [id],
            new TraceGeste("reglage", $"l'entrée du glossaire {e.Code}", id, "a supprime", Etat(e.Libelle, e.Texte, e.Proprietaire), null));
    }

    private sealed record Entree(string Code, string Libelle, string Texte, string Proprietaire);

    /// <summary>L'entree du glossaire visee, si l'agent la voit (publique, ou privee a lui) ; null sinon.</summary>
    private static async Task<Entree?> EntreeAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        var prefixe = LecteurModelesRedaction.Prefixes[3];
        if (ids.Length != 1 || !ids[0].StartsWith(prefixe, StringComparison.Ordinal)) return null;
        await using var r = await geste.LireAsync("""
            SELECT GLOSS_CODE_GLOSSAIRE, GLOSS_LIBELLE, GLOSS_TEXTE, GLOSS_PROPRIETAIRE FROM v2.GLOSSAIRE WITH (UPDLOCK)
            WHERE GLOSS_CODE_GLOSSAIRE = @c AND (GLOSS_PROPRIETAIRE = '*' OR GLOSS_PROPRIETAIRE = @login)
            """, ct, new SqlParameter("@c", ids[0][prefixe.Length..]), new SqlParameter("@login", Login(geste)));
        return await r.ReadAsync(ct)
            ? new Entree(FormeLigne.Texte(r, "GLOSS_CODE_GLOSSAIRE"), FormeLigne.Texte(r, "GLOSS_LIBELLE"), FormeLigne.Texte(r, "GLOSS_TEXTE"),
                FormeLigne.Texte(r, "GLOSS_PROPRIETAIRE"))
            : null;
    }

    /// <summary>Pourquoi la ligne visee ne se modifie ni ne se supprime ici.</summary>
    private static Task<string> PasDuGlossaireAsync(GesteEnCours geste)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        var p = LecteurModelesRedaction.Prefixes;
        if (geste.Demande.Ids.Distinct(StringComparer.Ordinal).Count() != 1) return Task.FromResult("Choisissez une seule ligne.");
        if (id.StartsWith(p[0], StringComparison.Ordinal) || id.StartsWith(p[1], StringComparison.Ordinal)) return Task.FromResult(ModeleOuMaquette);
        if (id.StartsWith(p[2], StringComparison.Ordinal)) return Task.FromResult(geste.Agent.EstAmitel ? SectionNonBranchee : SectionAmitel);
        var code = id.StartsWith(p[3], StringComparison.Ordinal) ? id[p[3].Length..] : id;
        return Task.FromResult($"Entrée introuvable dans votre glossaire (publique, ou privée à vous) : « {code} ».");
    }

    /// <summary>La premiere valeur plus large que sa colonne de v2.GLOSSAIRE dans la base du greffe ; null si toutes tiennent.</summary>
    private static async Task<string?> TropLongAsync(GesteEnCours geste, CancellationToken ct, params (string Colonne, string Libelle, string Valeur)[] valeurs)
    {
        foreach (var (colonne, libelle, valeur) in valeurs)
        {
            await using var r = await geste.LireAsync("SELECT COL_LENGTH('v2.GLOSSAIRE', @c)", ct, new SqlParameter("@c", colonne));
            var largeur = await r.ReadAsync(ct) && !r.IsDBNull(0) ? Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture) : 0;
            if (largeur > 0 && valeur.Length > largeur) return $"« {libelle} » : {largeur} caractères au plus.";
        }
        return null;
    }

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private static string? Saisi(GesteEnCours geste, string libelle) => geste.Valeur(LecteurModelesRedaction.SectionGlossaire, libelle)?.Trim();

    private static bool Coche(string? valeur) => valeur?.ToLowerInvariant() is "oui" or "true" or "1";

    /// <summary>UTLST_LOGIN de la V2 : le compte Windows de l'agent (Utilisateur.GetUtilisateurByName(AmiEnvironment.UserName)).</summary>
    private static string Login(GesteEnCours geste) => geste.Agent.CompteWindows;

    private static SqlParameter Valeur(string nom, string valeur) => new(nom, valeur.Length == 0 ? DBNull.Value : valeur);

    private static Dictionary<string, string> Etat(string libelle, string texte, string proprietaire) => new()
    {
        ["Libellé"] = libelle, ["Texte associé"] = texte, ["Propriétaire"] = proprietaire == "*" ? "public" : proprietaire,
    };
}
