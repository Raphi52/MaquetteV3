using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Courriers en masse » (PROC_COURRIERM) : « Générer la fiche » et « Générer la seconde édition » posent, pour chaque affaire
/// cochee, sa demande et sa demande d'edition dans la file des editions de la base du greffe. Rien n'est imprime ici : le service
/// d'edition de RIG v2 prend la demande d'edition creee (phase C). Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurCourriers-
/// EnMasse.cs ; la demande et l'edition : EditionsV2.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_COURRIERM\ (proc_courrierm.md, Detail OPE) --
/// OPE_RESULTAT_COURRIERM_INSTANCE._GenererLesDemandesEditions (:153-190) et son jumeau du volet Procedure
/// (OPE_RESULTAT_COURRIERM_PROCEDURE.cs:140-170) : pour chaque ligne cochee « fiche », une demande (Instance.CreateDemande(true,
/// « COURRIERM »), :162) et une demande d'edition FICHE_INSTANCE « fiche instance » (FICHE_PC « fiche procédure » pour une PC,
/// :150) ; pour chaque ligne cochee « seconde edition », la meme demande (ou une neuve) et une demande d'edition au code choisi dans la
/// liste (:175-186) ; la sortie de la liste « file ou imprimante » (_GererSortieFileOuImprimane, :98-108). Les demandes sont validees
/// a la sauvegarde (_Sauver, :81-85) : ici dans la transaction du geste.
/// CONTROLES, avant toute ecriture :
///   1. un geste connu, au moins une affaire cochee -- « Vous devez sélectionner au moins un dossier » (DoVerifier, :126-132) ;
///   2. la sortie, si elle est saisie : une file differee active, non directe, hors files Amitel (Lot.GetListeLotActif sans IsDirect
///      ni IsFileAmitel, UltComboUnionFilesEtImprimantes.cs:46-50), ou une imprimante d'edition hors imprimantes Amitel
///      (ImprimanteLot2, IMPED_ONLY_AMITEL, :30-38) -- la liste de RIG v2 n'en propose pas d'autre ;
///   3. « Générer la seconde édition » : un code d'edition, celui saisi ou le reglage par defaut du volet (LISTEINS_2EDITION,
///      LISTEPC_2EDITION : GetSelectionEditionParDefaut, :40-50) ; sans code, RIG v2 ne genere rien (:176) ; le code est une edition
///      du volet (PRMED_CODE_PROCESSUS EDIT_INS / EDIT_PC) autre que la fiche (MetierProcInstances.RequeteComboBoxEdition, :11) ;
///   4. le parametre d'edition existe dans la base du greffe (CreateDemandeEditionByParametreGeneration rend null sinon,
///      DemandeEdition.cs:2580-2584 ; RIG v2 leve alors une erreur au routage de la sortie) ;
///   5. chaque affaire existe ;
///   6. une MESURE n'a pas de fiche : le volet Mesures de RIG v2 ne genere que la seconde edition (OPE_RESULTAT_COURRIERM_MESURE.cs,
///      DoVerifier :102-106 et _GenererLesDemandesEditions :128-154 ne lisent que la colonne « seconde édition » ; aucun reglage
///      d'edition par defaut, :50).
/// LES VOLETS PRÉVENTIONS ET MESURES (lot 10, apres la reception du chef) : la meme generation que les instances, sur leur entite --
/// Prevention.CreateDemande (Outils\DLL\RigMetier\JUDICIAIRE\Prevention.cs:1868-1871 : nom = PREVE_NOM_AFFAIRE, indicatif et numeros =
/// PREVE_NUM_PRV, sequenceur 2R, sans affaire) et la fiche FICHE_PRV « fiche prévention » (OPE_RESULTAT_COURRIERM_PREVENTION.cs:144),
/// la seconde edition EDIT_PRV hors FICHE_PRV (MetierProcPrevention.cs:10), par defaut LISTEPRV_2EDITION puis LISTEPR_2EDITION (:49-50,
/// :58 ; GetSelectionEditionParDefaut garde la premiere valeur trouvee, OPE_RESULTAT_COURRIERM_BASE.cs:40-50), entite source
/// « PROC_COLL » (Prevention.CodeEntite, Prevention.cs:64 -- sic) ; Mesure.CreateDemande (Mesure.cs:912-915 : indicatif et numeros =
/// MSINS_NUM_MESURE, sequenceur 2F, nom vide remplace par celui de l'instance, :142-144 du volet), la seconde edition EDIT_MESUR
/// (MetierProcMesure.cs:10), entite source « MESURE » (Mesure.cs:61).
/// Traces : famille « demande », « a cree », la demande et le document, sa file.
/// </summary>
internal sealed class EcrivainCourriersEnMasse : IEcrivainEcran
{
    public string Procedure => "PROC_COURRIERM";
    public string Ecran => "Courriers en masse";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Les gestes du descripteur, au mot pres (les deux cases de la grille de RIG v2).</summary>
    internal const string Fiche = "Générer la fiche", SecondeEdition = "Générer la seconde édition";

    /// <summary>Les parametres du descripteur qu'il lit (la liste « file ou imprimante » et la liste des editions de RIG v2).</summary>
    internal const string ParametreSortie = "File différée ou imprimante", ParametreEdition = "Seconde édition";

    /// <summary>Une entite cochee : son volet (« I » instance, « P » procedure, « R » prevention, « F » mesure), son numero, l'objet
    /// de sa demande, l'identifiant source de sa demande d'edition.</summary>
    private sealed record Affaire(char Volet, int Id, string Numero, EditionsV2.ObjetDemande Objet, string? IdSource);

    /// <summary>Ce que chaque volet donne a sa generation : sa fiche (code, libelle ; null : pas de fiche), le processus de ses secondes
    /// editions, ses reglages par defaut dans l'ordre de RIG v2, son entite source, le pluriel et le complement du message.</summary>
    private sealed record VoletV2(string? CodeFiche, string? LibelleFiche, string Processus, string[] Reglages, string CodeEntite, string Pluriel, string De);

    private static readonly Dictionary<char, VoletV2> Volets = new()
    {
        ['I'] = new("FICHE_INSTANCE", "fiche instance", "EDIT_INS", ["LISTEINS_2EDITION"], "INSTANCE", "instances", "du rôle"),
        ['P'] = new("FICHE_PC", "fiche procédure", "EDIT_PC", ["LISTEPC_2EDITION"], "PROC_COLL", "procédures", "de la PC"),
        ['R'] = new("FICHE_PRV", "fiche prévention", "EDIT_PRV", ["LISTEPRV_2EDITION", "LISTEPR_2EDITION"], "PROC_COLL", "préventions", "de la prévention"),
        ['F'] = new(null, null, "EDIT_MESUR", [], "MESURE", "mesures", "de la mesure"),
    };

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        if (nom is not (Fiche or SecondeEdition)) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Vous devez sélectionner au moins un dossier");

        // 2. La sortie.
        var sortie = geste.Demande.Valeurs.GetValueOrDefault(ParametreSortie)?.Trim() ?? "";
        string? lot = null, imprimante = null;
        if (sortie.Length > 0)
        {
            if (await ValeurAsync(geste, "SELECT TOP (1) LOT_NOM FROM v2.LOT WHERE LOT_NOM = @s AND LOT_ACTIF = 1 AND ISNULL(LOT_DIRECT, 0) = 0 AND UPPER(LOT_NOM) NOT LIKE 'AMI%'", sortie, ct) is { } l)
                lot = l;
            else if (await ValeurAsync(geste, "SELECT CASE WHEN OBJECT_ID(N'v2.IMPRIMANTE_EDITION') IS NULL THEN NULL ELSE 1 END", null, ct) is not null
                     && await ValeurAsync(geste, "SELECT TOP (1) IMPED_NOM_LOGIQUE FROM v2.IMPRIMANTE_EDITION WHERE IMPED_NOM_LOGIQUE = @s AND ISNULL(IMPED_ONLY_AMITEL, 0) = 0", sortie, ct) is { } i)
                imprimante = i;
            else
                return IssueGeste.Refuser($"« {sortie} » n'est ni une file différée active ni une imprimante d'édition du greffe.");
        }

        // 5. Les affaires.
        var affaires = new List<Affaire>();
        foreach (var id in ids)
        {
            if (await AffaireAsync(geste, id, ct) is not { } a) return IssueGeste.Refuser($"Affaire introuvable dans la base du greffe ({id}).");
            affaires.Add(a);
        }

        // 3., 4. et 6. Le parametre d'edition de chaque volet coche.
        var parametres = new Dictionary<char, EditionsV2.ParametreEdition>();
        foreach (var lettre in affaires.Select(a => a.Volet).Distinct())
        {
            var v = Volets[lettre];
            string code;
            if (nom == Fiche)
            {
                if (v.CodeFiche is null)
                    return IssueGeste.Refuser("Une mesure n'a pas de fiche dans RIG v2 : seule « Générer la seconde édition » vaut pour le volet Mesures.");
                code = v.CodeFiche;
            }
            else
            {
                code = geste.Demande.Valeurs.GetValueOrDefault(ParametreEdition)?.Trim() ?? "";
                foreach (var reglage in v.Reglages)
                    if (code.Length == 0) code = await ReglageAsync(geste, reglage, ct) ?? "";
                if (code.Length == 0)
                    return IssueGeste.Refuser("Aucune seconde édition choisie : RIG v2 ne génère rien sans édition choisie dans la liste.");
                if (await ValeurAsync(geste, $"SELECT TOP (1) PRMED_CODE FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @s AND PRMED_CODE_PROCESSUS = '{v.Processus}' AND PRMED_CODE NOT LIKE 'FICHE[_]%'", code, ct) is null)
                    return IssueGeste.Refuser($"« {code} » n'est pas une seconde édition des {v.Pluriel} ({v.Processus}) dans le paramétrage du greffe.");
            }
            if (await EditionsV2.ParametreAsync(geste, code, ct) is not { } p)
                return IssueGeste.Refuser($"Le paramètre d'édition {code} est absent de la base du greffe : RIG v2 ne crée pas l'édition.");
            parametres[lettre] = p;
        }

        var traces = new List<TraceGeste>();
        var crees = new List<string>();
        foreach (var a in affaires)
        {
            var (idDemande, numero, refus) = await EditionsV2.DemandeValideeAsync(geste, "COURRIERM", a.Objet, ct);
            if (refus is not null) return IssueGeste.Refuser(refus);
            var p = parametres[a.Volet];
            var v = Volets[a.Volet];
            var libelle = nom == Fiche ? v.LibelleFiche : null;
            var edition = await EditionsV2.DemandeEditionAsync(geste, idDemande, p, libelle, v.CodeEntite, a.IdSource, lot, imprimante, ct);
            crees.Add(EditionsV2.Texte(edition));
            traces.Add(new TraceGeste("demande", $"le courrier « {libelle ?? p.Libelle} » {v.De} {a.Numero}",
                EditionsV2.Texte(edition), "a cree", null,
                new { Demande = numero, Document = libelle ?? p.Libelle, Edition = p.Code, File = lot, Imprimante = imprimante }));
        }

        var n = affaires.Count;
        var document = nom == Fiche ? "fiche" : $"« {parametres.Values.First().Libelle} »";
        var vers = lot is not null ? $" (file différée « {lot} »)" : imprimante is not null ? $" (imprimante « {imprimante} »)" : "";
        var message = (n > 1 ? $"{n} courriers ({document}) créés" : $"Courrier ({document}) créé") + $" dans la file des éditions{vers}. "
                    + "Le service d'édition de RIG v2 les produit : la V3 n'imprime rien.";
        return IssueGeste.Ecrit(message, [.. crees], [.. traces]);
    }

    /// <summary>L'affaire « I-&lt;id&gt; » (instance), « P-&lt;id&gt; » (procedure collective), « R-&lt;id&gt; » (prevention) ou
    /// « F-&lt;id&gt; » (mesure) et l'objet de sa demande (Instance.CreateDemande, Instance.cs:4697-4716 ; ProcedureCollective.CreateDemande,
    /// ProcedureCollective.cs:811-814 ; Prevention.CreateDemande, Prevention.cs:1868-1871 ; Mesure.CreateDemande, Mesure.cs:912-915).</summary>
    private static async Task<Affaire?> AffaireAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        if (id.Length < 3 || id[1] != '-' || !int.TryParse(id.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
        if (id[0] == 'I')
        {
            await using var r = await geste.LireAsync("""
                SELECT i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, i.INSTN_ID_AFFRE, i.INSTN_NUMERO_SOURCE, p.PRCLL_NUM_PC, pr.PREVE_NUM_PRV,
                       (SELECT STRING_AGG(m.MSINS_NUM_MESURE, ' ') FROM v2.MESURE AS m WHERE m.MSINS_ID_INSTN = i.INSTN_ID_INSTN AND m.MSINS_NUM_MESURE IS NOT NULL) AS Mesures
                FROM v2.INSTANCE AS i
                LEFT JOIN v2.PROCEDURE_COLLECTIVE AS p ON p.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
                LEFT JOIN v2.PREVENTION AS pr ON pr.PREVE_ID_PREVE = i.INSTN_ID_PREVE
                WHERE i.INSTN_ID_INSTN = @id
                """, ct, new SqlParameter("@id", n));
            if (!await r.ReadAsync(ct)) return null;
            var role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
            // Les numeros de la demande : le role, puis la PC, les mesures et la prevention liees (Instance.cs:4699-4712).
            var numeros = FormeLigne.Joindre(role, FormeLigne.Texte(r, "PRCLL_NUM_PC"), FormeLigne.Texte(r, "Mesures"), FormeLigne.Texte(r, "PREVE_NUM_PRV"))
                                    .Replace(" · ", " ", StringComparison.Ordinal);
            return new Affaire('I', n, role,
                new EditionsV2.ObjetDemande(r["INSTN_ID_AFFRE"] is int affaire ? affaire : null, FormeLigne.Texte(r, "INSTN_NOM_COURANT"), role, numeros,
                                            VerrouDemandeV2.SequenceurInstance, EditionsV2.Texte(n)),
                FormeLigne.Texte(r, "INSTN_NUMERO_SOURCE") is { Length: > 0 } s ? s : null);
        }
        if (id[0] == 'P')
        {
            await using var r = await geste.LireAsync("SELECT PRCLL_NUM_PC, PRCLL_NOM_AFFAIRE, PRCLL_NUMERO_SOURCE FROM v2.PROCEDURE_COLLECTIVE WHERE PRCLL_ID_PRCLL = @id",
                ct, new SqlParameter("@id", n));
            if (!await r.ReadAsync(ct)) return null;
            var numero = FormeLigne.Texte(r, "PRCLL_NUM_PC");
            return new Affaire('P', n, numero,
                new EditionsV2.ObjetDemande(null, FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE"), numero, numero, DepotsPcV2.SequenceurPc, EditionsV2.Texte(n)),
                FormeLigne.Texte(r, "PRCLL_NUMERO_SOURCE") is { Length: > 0 } s ? s : null);
        }
        if (id[0] == 'R')
        {
            // Une base de banc peut ne pas porter le numero source de la prevention : sans lui, la demande d'edition n'a pas d'id source.
            var source = (await EditionsV2.ColonnesAsync(geste, "v2.PREVENTION", ct)).Contains("PREVE_NUMERO_SOURCE") ? "PREVE_NUMERO_SOURCE" : "NULL AS PREVE_NUMERO_SOURCE";
            await using var r = await geste.LireAsync($"SELECT PREVE_NUM_PRV, PREVE_NOM_AFFAIRE, {source} FROM v2.PREVENTION WHERE PREVE_ID_PREVE = @id",
                ct, new SqlParameter("@id", n));
            if (!await r.ReadAsync(ct)) return null;
            var numero = FormeLigne.Texte(r, "PREVE_NUM_PRV");
            return new Affaire('R', n, numero,
                new EditionsV2.ObjetDemande(null, FormeLigne.Texte(r, "PREVE_NOM_AFFAIRE"), numero, numero, LecteurProceduresCollectives.SequenceurPrevention, EditionsV2.Texte(n)),
                FormeLigne.Texte(r, "PREVE_NUMERO_SOURCE") is { Length: > 0 } s ? s : null);
        }
        if (id[0] == 'F')
        {
            // Le nom de la demande : celui de l'instance de la mesure (Mesure.NomDemande, Mesure.cs:192-196 ; GetNomCourantMesure, :39-42).
            await using var r = await geste.LireAsync("""
                SELECT m.MSINS_NUM_MESURE, m.MSINS_NUMERO_SOURCE, i.INSTN_NOM_COURANT
                FROM v2.MESURE AS m LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = m.MSINS_ID_INSTN WHERE m.MSINS_ID_MSINS = @id
                """, ct, new SqlParameter("@id", n));
            if (!await r.ReadAsync(ct)) return null;
            var numero = FormeLigne.Texte(r, "MSINS_NUM_MESURE");
            return new Affaire('F', n, numero,
                new EditionsV2.ObjetDemande(null, FormeLigne.Texte(r, "INSTN_NOM_COURANT"), numero, numero, LecteurMesuresProvisoires.Sequenceur, EditionsV2.Texte(n)),
                FormeLigne.Texte(r, "MSINS_NUMERO_SOURCE") is { Length: > 0 } s ? s : null);
        }
        return null;
    }

    /// <summary>La premiere valeur de <paramref name="sql"/> (parametre @s = <paramref name="s"/>) ; null si aucune ligne.</summary>
    private static async Task<string?> ValeurAsync(GesteEnCours geste, string sql, string? s, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct, s is null ? [] : [new SqlParameter("@s", s)]);
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Un reglage du greffe (PARAMETRAGE de RIG v2, repris dans soc.Reglage) ; null s'il est vide ou « NULL ».</summary>
    private static async Task<string?> ReglageAsync(GesteEnCours geste, string code, CancellationToken ct)
        => await ValeurAsync(geste, "SELECT Valeur FROM soc.Reglage WHERE Code = @s AND Valeur <> '' AND Valeur <> 'NULL'", code, ct);
}
