using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Courriers divers » (PROC_COURRIER_DIVERS, ecran fait main web/src/ecrans/EcranCourriersDivers.tsx) : les gestes de la fiche d'un
/// modele -- VALIDER (creer ou modifier) et SUPPRIMER --, ecrits dans la base du greffe. Plan « RIG v3 complet, mesure par le banc du
/// parcours complet », lot 8, 28/11/2026. Route : ModuleCourriersDivers.cs ; la lecture : LectureCourriersDivers.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_COURRIER_DIVERS\Edition\OPE_EDITION_COURRIER_DIVERS.cs) :
///   - les champs (Designer :77-135) : code (20 car., obligatoire, non modifiable une fois cree, :185-186), libelle (100 car.,
///     obligatoire), parametre edition (obligatoire, la liste du type d'entite : LectureCourriersDivers.ProcessusParType), avec
///     destinataire, actif, type entite (obligatoire, la liste ULT_COMBO_TYPE_ENTITE_JUDICIAIRE) ;
///   - DoVerifier (:47-52) : a la creation, un code deja pris est refuse (« Le code du courrier divers existe déjà ») ;
///   - Valider (:81-94) et Supprimer (:99-104 ; CanSuppress est toujours vrai, CourrierJudiciaire.cs:43-45) : une demande du processus
///     PROC_COURRIER_DIVERS nommee « Création | Modification | Suppression du courrier divers : CODE », validee aussitot
///     (GestionDemande, :111-115 ; ici RelancesRsurv.DemandeAsync, qui l'ecrit validee), puis l'ecriture de COURRIER_JUDICIAIRE.
/// NON REPRIS, nomme : « Accéder au paramètre édition » ouvre le processus MAQUETTE (:141-148) -- un autre ecran, « Edition des
/// maquettes » ; la question « des changements ont été saisis » (une boite de dialogue du poste).
/// TRACE : famille « reglage », « a cree » / « a modifie » / « a supprime ».
/// </summary>
internal static class EcritureCourriersDivers
{
    /// <summary>Le nom de l'ecran au journal des gestes.</summary>
    public const string Ecran = "Courriers divers";

    /// <summary>Le code du processus de RIG v2 (PROC_COURRIER_DIVERS.Designer.cs:57).</summary>
    public const string Processus = "PROC_COURRIER_DIVERS";

    /// <summary>Les gestes ecrits : tout autre type est refuse par la route (GesteFaitMain, rien d'ecrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Creer, TypeGeste.Enregistrer, TypeGeste.Supprimer];

    /// <summary>Un modele lu avant le geste.</summary>
    private sealed record Modele(int Id, string Code, string Libelle, string TypeEntite, string ParamEdition, bool AvecDestinataire, bool Actif);

    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        Modele? avant = null;
        if (geste.Demande.Type != TypeGeste.Creer)
        {
            if (geste.Demande.Ids is not [var texte] || !int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var idChoisi))
                return IssueGeste.Refuser("Choisissez un courrier divers de la liste.");
            avant = await LireAsync(geste, idChoisi, ct);
            if (avant is null) return IssueGeste.Refuser("Ce courrier divers n'est plus dans la base du greffe : relisez la liste.");
        }

        if (geste.Demande.Type == TypeGeste.Supprimer)
        {
            if (await DemandeAsync(geste, "Suppression", avant!.Code, ct) is { } refusSuppression) return IssueGeste.Refuser(refusSuppression);
            await geste.EcrireAsync("DELETE FROM v2.COURRIER_JUDICIAIRE WHERE COUJU_ID_COUJU = @id", ct, new SqlParameter("@id", avant.Id));
            return IssueGeste.Ecrit($"Courrier divers {avant.Code} supprimé.", [avant.Id.ToString(CultureInfo.InvariantCulture)],
                new TraceGeste("reglage", $"le courrier divers {avant.Code}", avant.Id.ToString(CultureInfo.InvariantCulture), "a supprime", avant, null));
        }

        // Un champ absent garde la valeur de la base (modification) ; a la creation, il est vide (cases : cochees, les defauts de
        // COURRIER_JUDICIAIRE dans RIG_DEV).
        string Champ(string libelle, string? base_) => geste.Valeur(SaisieCourrierDivers.Section, libelle)?.Trim() ?? base_ ?? "";
        bool Case(string libelle, bool base_) => geste.Valeur(SaisieCourrierDivers.Section, libelle) is { } v
            ? v.Trim().ToLowerInvariant() is "1" or "true" or "oui" : base_;
        var code = avant?.Code ?? Champ(SaisieCourrierDivers.Code, null);
        var libelleSaisi = Champ(SaisieCourrierDivers.Libelle, avant?.Libelle);
        var type = Champ(SaisieCourrierDivers.TypeEntite, avant?.TypeEntite);
        var parametre = Champ(SaisieCourrierDivers.ParamEdition, avant?.ParamEdition);
        var avecDestinataire = Case(SaisieCourrierDivers.AvecDestinataire, avant?.AvecDestinataire ?? true);
        var actif = Case(SaisieCourrierDivers.Actif, avant?.Actif ?? true);

        if (avant is not null && Champ(SaisieCourrierDivers.Code, avant.Code) != avant.Code)
            return IssueGeste.Refuser($"« {SaisieCourrierDivers.Code} » ne se modifie pas une fois le courrier divers créé.");
        foreach (var (libelle, valeur, longueur) in new[] { (SaisieCourrierDivers.Code, code, 20), (SaisieCourrierDivers.Libelle, libelleSaisi, 100) })
        {
            if (valeur.Length == 0) return IssueGeste.Refuser(Obligatoire(libelle));
            if (valeur.Length > longueur) return IssueGeste.Refuser($"« {libelle} » : {longueur} caractères au plus.");
        }
        if (parametre.Length == 0) return IssueGeste.Refuser(Obligatoire(SaisieCourrierDivers.ParamEdition));
        if (type.Length == 0) return IssueGeste.Refuser(Obligatoire(SaisieCourrierDivers.TypeEntite));
        if (!LectureCourriersDivers.TypesEntite.Contains(type))
            return IssueGeste.Refuser($"« {type} » n'est pas un type d'entité de la liste ({string.Join(", ", LectureCourriersDivers.TypesEntite)}).");
        var processusEdition = LectureCourriersDivers.ProcessusParType.GetValueOrDefault(type, "EDIT_");
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @p AND PRMED_CODE_PROCESSUS = @proc", ct,
                         new SqlParameter("@p", parametre), new SqlParameter("@proc", processusEdition)))
        {
            await r.ReadAsync(ct);
            if (r.GetInt32(0) == 0)
                return IssueGeste.Refuser($"« {parametre} » n'est pas un paramètre d'édition de la liste pour le type d'entité {type}.");
        }
        if (avant is null)
        {
            await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.COURRIER_JUDICIAIRE WHERE COUJU_CODE = @c", ct, new SqlParameter("@c", code));
            await r.ReadAsync(ct);
            if (r.GetInt32(0) > 0) return IssueGeste.Refuser("Le code du courrier divers existe déjà");
        }
        var apres = new Modele(avant?.Id ?? 0, code, libelleSaisi, type, parametre, avecDestinataire, actif);
        if (avant is not null && apres == avant) return IssueGeste.Refuser("Aucune modification à enregistrer.");

        if (await DemandeAsync(geste, avant is null ? "Création" : "Modification", code, ct) is { } refus) return IssueGeste.Refuser(refus);
        int id;
        if (avant is null)
            id = await InsererAsync(geste, "v2.COURRIER_JUDICIAIRE", "COUJU_ID_COUJU", new Dictionary<string, object?>
            {
                ["COUJU_CODE"] = code, ["COUJU_LIBELLE"] = libelleSaisi, ["COUJU_CODE_PRMED"] = parametre, ["COUJU_WITH_DEST"] = avecDestinataire,
                ["COUJU_ACTIF"] = actif, ["COUJU_TYPE_ENTITE"] = type,
            }, ct);
        else
        {
            id = avant.Id;
            await geste.EcrireAsync("""
                UPDATE v2.COURRIER_JUDICIAIRE SET COUJU_LIBELLE = @l, COUJU_CODE_PRMED = @p, COUJU_WITH_DEST = @d, COUJU_ACTIF = @a, COUJU_TYPE_ENTITE = @t
                WHERE COUJU_ID_COUJU = @id
                """, ct, new SqlParameter("@l", libelleSaisi), new SqlParameter("@p", parametre), new SqlParameter("@d", avecDestinataire),
                new SqlParameter("@a", actif), new SqlParameter("@t", type), new SqlParameter("@id", id));
        }
        var cle = id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit($"Courrier divers {code} {(avant is null ? "créé" : "enregistré")}.", [cle],
            new TraceGeste("reglage", $"le courrier divers {code}", cle, avant is null ? "a cree" : "a modifie", avant, apres with { Id = id }));
    }

    private static async Task<Modele?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT COUJU_ID_COUJU, COUJU_CODE, COUJU_LIBELLE, COUJU_TYPE_ENTITE, COUJU_CODE_PRMED,
                   CONVERT(bit, ISNULL(COUJU_WITH_DEST, 1)) AS AvecDestinataire, CONVERT(bit, ISNULL(COUJU_ACTIF, 1)) AS Actif
            FROM v2.COURRIER_JUDICIAIRE WHERE COUJU_ID_COUJU = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Modele(id, FormeLigne.Texte(r, "COUJU_CODE").Trim(), FormeLigne.Texte(r, "COUJU_LIBELLE").Trim(), FormeLigne.Texte(r, "COUJU_TYPE_ENTITE").Trim(),
            FormeLigne.Texte(r, "COUJU_CODE_PRMED").Trim(), r["AvecDestinataire"] is true, r["Actif"] is true);
    }

    /// <summary>La demande validee du geste (GestionDemande, :111-115) : « &lt;mode&gt; du courrier divers : &lt;code&gt; » ; le refus de
    /// la numerotation (rien d'ecrit), null si la demande est ecrite.</summary>
    private static async Task<string?> DemandeAsync(GesteEnCours geste, string mode, string code, CancellationToken ct)
    {
        var (id, _, refus) = await RelancesRsurv.DemandeAsync(geste, null, $"{mode} du courrier divers : {code}", null, null, null, null, ct, Processus);
        return id is null ? refus ?? "La demande du geste n'a pas pu être numérotée : rien n'est enregistré." : null;
    }
}
