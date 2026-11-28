using System.Globalization;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// EDITER COMME RIG v2 : la demande d'une entite judiciaire et sa demande d'edition, telles que le moteur de RIG v2 les ecrit
/// avant de passer la main au service d'edition (plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026). La V3 n'appelle aucun
/// service : la demande d'edition est posee dans la file des editions (v2.DEMANDE_EDITION, phase C « creee »), que le service
/// d'edition de RIG v2 relit. Utilise par EcrivainCourriersEnMasse.cs ; la demande du processus et le rattachement d'une demande a sa
/// mere, par EcrivainOrdonnancesASigner.cs (dont les editions viennent de l'evenement de procedure : EditionsEvenementV2.cs).
///
/// SOURCE (ancien RIG) :
///   - la demande : Instance.CreateDemande(enCours, codeProcessus) (Outils\DLL\RigMetier\JUDICIAIRE\Instance.cs:4697-4716) --
///     Demande.CreateDemande sur l'affaire de l'instance, nom = INSTN_NOM_COURANT, indicatif = INSTN_NUM_ROLE, numeros = le role
///     et ceux de la PC, des mesures et de la prevention liees, sequenceur 2J = l'instance ; la procedure collective et la
///     prevention de meme (ProcedureCollective / Prevention.CreateDemande). Numero DMD (Demande.cs:493 et :669, comme
///     CreationV2.CreerDemandeAsync). Validee a la sauvegarde du processus (OPE_RESULTAT_COURRIERM_BASE._Sauver) : la V3 l'ecrit
///     validee d'emblee, dans la transaction du geste ;
///   - la demande d'edition : DemandeEdition.CreateDemandeEditionByParametreGeneration (Edition\DemandeEdition.cs:2579-2600) -- le
///     parametre d'edition (PARAMETRE_EDITION) donne le libelle du document s'il n'est pas fourni, le type de generation, le code
///     CFE-CP et la nature Tribunal digital ; null si le parametre n'existe pas. Les valeurs de creation : CreateDemandeEdition
///     (:536-570) -- papier NORMAL, media IMPR, emission N, phase C, un exemplaire, impression, groupage automatique, l'agent comme
///     utilisateur et interlocuteur. La sortie : OPE_RESULTAT_COURRIERM_BASE._GererSortieFileOuImprimane (:98-108), DMNED_LOT pour
///     une file differee, DMNED_IMPRIMANTE pour une imprimante.
/// NON REPRIS (nomme) : DMNED_PARAMETRE_GENERATION, le XML des parametres de generation (ParametreEdition.ListeParametre) que le
/// service d'edition fabrique s'il manque -- les ecrivains des lots 6 et 8 ne l'ecrivent pas non plus.
/// </summary>
internal static class EditionsV2
{
    /// <summary>Les colonnes de <paramref name="table"/> dans la base du greffe (une base de banc n'en porte qu'une partie).</summary>
    internal static async Task<HashSet<string>> ColonnesAsync(GesteEnCours geste, string table, CancellationToken ct)
    {
        var colonnes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await geste.LireAsync("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@table)", ct, new SqlParameter("@table", table));
        while (await r.ReadAsync(ct)) colonnes.Add(r.GetString(0));
        return colonnes;
    }

    /// <summary>Le parametre d'edition <paramref name="code"/> : son libelle et ce qu'il donne a la demande d'edition ; null s'il
    /// n'existe pas (CreateDemandeEditionByParametreGeneration rend null, DemandeEdition.cs:2580-2584).</summary>
    internal sealed record ParametreEdition(string Code, string Libelle, string TypeGeneration, object? Cfecp, object? NatureTribunalDigital);

    internal static async Task<ParametreEdition?> ParametreAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        var colonnes = await ColonnesAsync(geste, "v2.PARAMETRE_EDITION", ct);
        if (colonnes.Count == 0) return null;
        string Colonne(string nom) => colonnes.Contains(nom) ? nom : $"NULL AS {nom}";
        await using var r = await geste.LireAsync($"""
            SELECT PRMED_CODE, PRMED_LIBELLE, {Colonne("PRMED_TYPE_GENERATION")}, {Colonne("PRMED_LISTE")}, {Colonne("PRMED_CODE_CFECP")},
                   {Colonne("PRMED_TRIBUNAL_DIGITAL_NATURE_DOC")}
            FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @code
            """, ct, new SqlParameter("@code", code));
        if (!await r.ReadAsync(ct)) return null;
        return new ParametreEdition(FormeLigne.Texte(r, "PRMED_CODE"), FormeLigne.Texte(r, "PRMED_LIBELLE"),
            RelancesRsurv.TypeGeneration(FormeLigne.Texte(r, "PRMED_TYPE_GENERATION"), FormeLigne.Texte(r, "PRMED_LISTE")),
            r["PRMED_CODE_CFECP"] is DBNull ? null : r["PRMED_CODE_CFECP"],
            r["PRMED_TRIBUNAL_DIGITAL_NATURE_DOC"] is DBNull ? null : r["PRMED_TRIBUNAL_DIGITAL_NATURE_DOC"]);
    }

    /// <summary>L'objet d'une demande (Instance / ProcedureCollective / Prevention.CreateDemande) : son affaire, son nom, son
    /// indicatif, ses numeros, et le sequenceur qui le lie a la demande.</summary>
    internal sealed record ObjetDemande(int? IdAffaire, string Nom, string Indicatif, string Numeros, string Sequenceur, string Valeur);

    /// <summary>Une demande V2 validee de <paramref name="codeProcessus"/> sur <paramref name="objet"/> ; son id et son numero, ou le refus
    /// de la numerotation DMD.</summary>
    internal static async Task<(int Id, string? Numero, string? Refus)> DemandeValideeAsync(GesteEnCours geste, string codeProcessus, ObjetDemande objet,
        CancellationToken ct)
    {
        var (numero, refus) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
        if (numero is null) return (0, null, refus);
        static string Couper(string s, int n) => s.Length > n ? s[..n] : s;
        var id = await CreationV2.InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", new Dictionary<string, object?>
        {
            ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = "V", ["DMND_CODE_PROSS"] = codeProcessus, ["DMND_ID_AFFRE"] = objet.IdAffaire,
            ["DMND_NOM"] = Couper(objet.Nom, 255), ["DMND_NUM_INDICATIF"] = Couper(objet.Indicatif, 14), ["DMND_LES_NUMEROS"] = Couper(objet.Numeros, 128),
            ["DMND_DATE_CREATION"] = geste.Maintenant, ["DMND_DATE_VALIDATION"] = geste.Maintenant, ["DMND_EN_COURS"] = false,
            ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche,
        }, ct);
        await geste.InsererAsync("v2.DOSSIER_DEMANDE", "DSSDM_ID_DSSDM", new Dictionary<string, object?>
        {
            ["DSSDM_ID_DMND"] = id, ["DSSDM_CODE_SEQUENCEUR"] = objet.Sequenceur, ["DSSDM_VALEUR_SEQUENCEUR"] = objet.Valeur,
        }, ct);
        return (id, numero, null);
    }

    /// <summary>La demande DU PROCESSUS (Processus.demande de RIG v2 : sans dossier ni affaire, nommee par le processus), validee : la
    /// demande mere des demandes par entite (DMND_ID_DMND_MERE, OPE_LISTE_ORDONNANCES.cs:3348-3349, :3390). Rend son id et son numero, ou
    /// le refus de la numerotation DMD. Un <paramref name="nom"/> null laisse DMND_NOM vide, comme la demande que le moteur de RIG v2 cree a
    /// l'ouverture d'un processus « avec demande » (Processus(param, true) : les demandes SURVBOD de RIG_DEV n'ont pas de nom, lu le
    /// 28/11/2026).</summary>
    internal static async Task<(int Id, string? Numero, string? Refus)> DemandeProcessusValideeAsync(GesteEnCours geste, string codeProcessus, string? nom,
        CancellationToken ct)
    {
        var (numero, refus) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
        if (numero is null) return (0, null, refus);
        var id = await CreationV2.InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", new Dictionary<string, object?>
        {
            ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = "V", ["DMND_CODE_PROSS"] = codeProcessus,
            ["DMND_NOM"] = nom is null ? null : nom.Length > 255 ? nom[..255] : nom,
            ["DMND_DATE_CREATION"] = geste.Maintenant, ["DMND_DATE_VALIDATION"] = geste.Maintenant, ["DMND_EN_COURS"] = false,
            ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche,
        }, ct);
        return (id, numero, null);
    }

    /// <summary>Rattache la demande <paramref name="idDemande"/> a sa demande mere (DMND_ID_DMND_MERE) et lui ajoute les dossiers
    /// <paramref name="dossiers"/> (sequenceur, valeur) -- ceux que RIG v2 lie apres coup (Instance.DossierDemandeAddSequenceurLie,
    /// Instance.cs:4761-4767). Une base sans la colonne de la mere (un banc) garde la demande sans mere.</summary>
    internal static async Task LierAsync(GesteEnCours geste, int idDemande, int? idMere, IEnumerable<(string Sequenceur, string Valeur)> dossiers, CancellationToken ct)
    {
        if (idMere is { } mere && (await ColonnesAsync(geste, "v2.DEMANDE", ct)).Contains("DMND_ID_DMND_MERE"))
            await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ID_DMND_MERE = @m WHERE DMND_ID_DMND = @d", ct, new SqlParameter("@m", mere), new SqlParameter("@d", idDemande));
        foreach (var (sequenceur, valeur) in dossiers)
            await geste.InsererAsync("v2.DOSSIER_DEMANDE", "DSSDM_ID_DSSDM", new Dictionary<string, object?>
            {
                ["DSSDM_ID_DMND"] = idDemande, ["DSSDM_CODE_SEQUENCEUR"] = sequenceur, ["DSSDM_VALEUR_SEQUENCEUR"] = valeur,
            }, ct);
    }

    /// <summary>
    /// La demande d'edition de <paramref name="parametre"/> sur la demande <paramref name="idDemande"/>, pour l'entite source
    /// (<paramref name="codeEntite"/>, <paramref name="idSource"/>), vers la file differee <paramref name="lot"/> ou l'imprimante
    /// <paramref name="imprimante"/> (une des deux, ou aucune : le tableau d'editions la choisit). Rend son id. Sans demande
    /// (<paramref name="idDemande"/> null), DMNED_ID_DMND reste vide : la trace est le journal des gestes, comme les registres judiciaires
    /// (EcrivainRegistresJudiciaires.cs). <paramref name="autres"/> : des colonnes de plus (DMNED_LIBELLE_SOURCE...), ecrites si la table
    /// les porte.
    /// </summary>
    // fix-ok: pas un correctif -- lot 1 du plan-f4fb097f (28/11/2026) : les editions de « Surveiller » (SURVBOD, SRBE_RAPPEL, deliberes,
    // taxes, pilotage, statistiques) ont besoin d'une demande facultative, d'une entite source facultative et de colonnes de plus ; les
    // appelants existants passent un int et une entite, inchanges (conversion implicite).
    internal static async Task<int> DemandeEditionAsync(GesteEnCours geste, int? idDemande, ParametreEdition parametre, string? libelle,
        string? codeEntite, string? idSource, string? lot, string? imprimante, CancellationToken ct,
        IReadOnlyDictionary<string, object?>? autres = null)
    {
        var compte = geste.Agent.CompteWindows.Length > 32 ? geste.Agent.CompteWindows[..32] : geste.Agent.CompteWindows;
        var valeurs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DMNED_ID_DMND"] = idDemande, ["DMNED_DATE_CREATION"] = geste.Maintenant, ["DMNED_TYPE_PAPIER"] = "NORMAL", ["DMNED_MEDIA"] = "IMPR",
            ["DMNED_ETAT_EMISSION"] = "N", ["DMNED_PHASE_TRAITEMENT"] = "C", ["DMNED_UTILISATEUR"] = compte, ["DMNED_INTERLOCUTEUR"] = compte,
            ["DMNED_NOMBRE_EXEMPLAIRE"] = (short)1, ["DMNED_IMPRESSION"] = true, ["DMNED_PAPIER_TRAME"] = false, ["DMNED_AGRAFAGE"] = false,
            ["DMNED_RECTO_VERSO"] = false, ["DMNED_AFF_AVANT_IMPRESSION"] = false,
            ["DMNED_LIBELLE_DOCUMENT"] = string.IsNullOrWhiteSpace(libelle) ? parametre.Libelle : libelle,
            ["DMNED_GROUPAGE_AUTOMATIQUE"] = true, ["DMNED_RESUME"] = false, ["DMNED_READ_ONLY"] = false,
            ["DMNED_TYPE_GENERATION"] = parametre.TypeGeneration, ["DMNED_CODE_CFECP"] = parametre.Cfecp,
            ["DMNED_TRIBUNAL_DIGITAL_NATURE_DOC"] = parametre.NatureTribunalDigital,
            ["DMNED_CODE_ENTITE_SOURCE"] = codeEntite, ["DMNED_VALEUR_ID_SOURCE"] = idSource,
            ["DMNED_LOT"] = lot, ["DMNED_IMPRIMANTE"] = imprimante,
            // NOT NULL dans la base V3 sans le defaut ((0)) de RIG_DEV : la V2 les laisse a ce defaut (EcrivainRegistresJudiciaires).
            ["DMNED_ENVOI_PAR_MAIL"] = false, ["DMNED_HAS_NUMLRAR"] = false, ["DMNED_IS_NON_SUPPRESSIBLE"] = false, ["DMNED_HAS_NUMLR"] = false,
            ["DMNED_MASQUER_COFFRE_FORT"] = false,
        };
        foreach (var (colonne, valeur) in autres ?? new Dictionary<string, object?>()) valeurs[colonne] = valeur;
        var presentes = await ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        return await geste.InsererAsync("v2.DEMANDE_EDITION", "DMNED_ID_DMNED",
            valeurs.Where(v => presentes.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);
    }

    /// <summary>
    /// Une edition que RIG v2 produit SUR LE POSTE, sans parametre d'edition ni file : un document Word (RigWord), un etat Crystal
    /// affiche, un classeur Excel. Decision du plan « RIG v3 complet, mesure par le banc du parcours complet » (lot 1, plan-f4fb097f,
    /// 28/11/2026, fil chef conv-2206) : une edition de la V3 est une DEMANDE DANS LA FILE DES EDITIONS, comme toute edition de RIG v2
    /// (EcrivainFileEditions.cs) ; <paramref name="typeGeneration"/> est le type de RIG v2 du meme genre de document
    /// (DemandeEdition.eTypeGeneration, DemandeEdition.cs:160-205 : FUSION_ASPOSE pour un Word de donnees, FUSION_CRYSTAL_NET pour un etat
    /// Crystal .NET, REPORT_SQL pour une requete mise en tableau). Le libelle du document est son titre dans RIG v2.
    /// </summary>
    internal static ParametreEdition SansParametre(string libelle, string typeGeneration) => new("", libelle, typeGeneration, null, null);

    /// <summary>
    /// DemandeEdition.CreateDemandeEditionForDocWord (Edition\DemandeEdition.cs:728-761) : la demande d'edition d'un document Word du
    /// parametre <paramref name="code"/>, sur la demande <paramref name="idDemande"/>. Le parametre doit exister et porter une maquette, sinon
    /// RIG v2 leve -- ses deux messages, au mot pres (:730, :738), sont rendus en refus. La demande reprend le parametre
    /// (CreateDemandeEditionWithParametreEdition, :682-717 : libelle, lot, affichage avant impression, agrafage, entite source, maquette,
    /// processus, groupage, impression, imprimante, nature, exemplaires, non supprimable, lecture seule, recto verso, papier, nature
    /// Tribunal digital ; « USER » du lot remplace par le compte de l'agent, :689), puis FUSION_ASPOSE, sans courriel, phase C (:756-758).
    /// NON REPRIS (nomme) : le fichier de donnees XML de la fusion (AsposeHelper.ToXML, SetFichierFlux, :743-750), que le service d'edition
    /// lit -- RIG v3 n'ecrit aucun fichier sur le partage des editions ; il reste a ecrire quand la V3 aura sa fusion de documents.
    /// </summary>
    internal static async Task<(int Id, string? Refus)> DemandeEditionWordAsync(GesteEnCours geste, int? idDemande, string code, CancellationToken ct,
        IReadOnlyDictionary<string, object?>? autres = null)
    {
        var colonnes = await ColonnesAsync(geste, "v2.PARAMETRE_EDITION", ct);
        string Colonne(string nom) => colonnes.Contains(nom) ? nom : $"NULL AS {nom}";
        // Le parametre de RIG v2 -> la colonne de la demande d'edition (CreateDemandeEditionWithParametreEdition, :689-714).
        (string Parametre, string Edition)[] reprises =
        [
            ("PRMED_LOT", "DMNED_LOT"), ("PRMED_AFFICHER_AVANT_IMPRESSION", "DMNED_AFF_AVANT_IMPRESSION"), ("PRMED_AGRAFAGE", "DMNED_AGRAFAGE"),
            ("PRMED_CODE_ENTITE_SOURCE", "DMNED_CODE_ENTITE_SOURCE"), ("PRMED_CODE_MAQUETTE", "DMNED_CODE_MAQUETTE"),
            ("PRMED_CODE_PROCESSUS", "DMNED_CODE_PROCESSUS"), ("PRMED_GROUPAGE_AUTOMATIQUE", "DMNED_GROUPAGE_AUTOMATIQUE"),
            ("PRMED_IMPRESSION", "DMNED_IMPRESSION"), ("PRMED_IMPRIMANTE", "DMNED_IMPRIMANTE"), ("PRMED_NATURE_EDITION", "DMNED_NATURE_EDITION"),
            ("PRMED_NOMBRE_EXEMPLAIRES", "DMNED_NOMBRE_EXEMPLAIRE"), ("PRMED_NON_SUPPRESSIBLE", "DMNED_IS_NON_SUPPRESSIBLE"),
            ("PRMED_READ_ONLY", "DMNED_READ_ONLY"), ("PRMED_RECTO_VERSO", "DMNED_RECTO_VERSO"), ("PRMED_TYPE_PAPIER", "DMNED_TYPE_PAPIER"),
            ("PRMED_TRIBUNAL_DIGITAL_NATURE_DOC", "DMNED_TRIBUNAL_DIGITAL_NATURE_DOC"),
        ];
        string libelle;
        var valeurs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        await using (var r = colonnes.Count == 0 ? null : await geste.LireAsync($"""
            SELECT PRMED_CODE, PRMED_LIBELLE, {Colonne("PRMED_LIBELLE_DOCUMENT")}, {string.Join(", ", reprises.Select(x => Colonne(x.Parametre)))}
            FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @code
            """, ct, new SqlParameter("@code", code)))
        {
            if (r is null || !await r.ReadAsync(ct)) return (0, "Erreur CreateDemandeEditionForDocWord: Le paramètre édition ne peut être null.");
            if (r["PRMED_CODE_MAQUETTE"] is DBNull)
                return (0, $"Erreur CreateDemandeEditionForDocWord: Aucune maquette n'est associée au paramètre édition {code}.");
            libelle = FormeLigne.Texte(r, "PRMED_LIBELLE_DOCUMENT") is { Length: > 0 } doc ? doc : FormeLigne.Texte(r, "PRMED_LIBELLE");
            foreach (var (parametre, edition) in reprises)
                if (r[parametre] is not DBNull) valeurs[edition] = r[parametre];
        }
        if (valeurs.TryGetValue("DMNED_LOT", out var lot) && lot is string l)
            valeurs["DMNED_LOT"] = l.Replace("USER", geste.Agent.CompteWindows.ToUpperInvariant(), StringComparison.Ordinal);
        if (valeurs.TryGetValue("DMNED_NOMBRE_EXEMPLAIRE", out var n) && n is not null)
            valeurs["DMNED_NOMBRE_EXEMPLAIRE"] = Convert.ToInt16(n, CultureInfo.InvariantCulture);
        valeurs["DMNED_ENVOI_PAR_MAIL"] = false;
        foreach (var (colonne, valeur) in autres ?? new Dictionary<string, object?>()) valeurs[colonne] = valeur;
        var id = await DemandeEditionAsync(geste, idDemande, SansParametre(libelle, "FUSION_ASPOSE"), libelle,
            valeurs.TryGetValue("DMNED_CODE_ENTITE_SOURCE", out var entite) ? entite as string : null, null,
            valeurs.TryGetValue("DMNED_LOT", out var lu) ? lu as string : null, valeurs.TryGetValue("DMNED_IMPRIMANTE", out var imp) ? imp as string : null,
            ct, valeurs);
        return (id, null);
    }

    /// <summary>Un identifiant en texte invariant.</summary>
    internal static string Texte(int id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// L'identifiant d'une ligne d'un ETAT qui s'edite sur une periode (taxes et tours, pilotage, statistiques cour d'appel) :
    /// « cle|aaaammjj|aaaammjj ». La forme Etat n'envoie au geste que les identifiants des lignes affichees (FormeEtat.tsx, useGesteEnLot),
    /// pas ses parametres : la periode que le lecteur a calculee voyage avec la ligne, et l'edition porte la periode du tableau affiche.
    /// </summary>
    internal static string IdSurPeriode(string cle, DateTime debut, DateTime fin)
        => FormattableString.Invariant($"{cle}|{debut:yyyyMMdd}|{fin:yyyyMMdd}");

    /// <summary>La cle et la periode d'un identifiant <see cref="IdSurPeriode"/> ; null s'il n'en est pas un.</summary>
    internal static (string Cle, DateTime Debut, DateTime Fin)? LirePeriode(string? id)
    {
        var parts = (id ?? "").Split('|');
        return parts.Length == 3 && parts[0].Length > 0
               && DateTime.TryParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var debut)
               && DateTime.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fin)
            ? (parts[0], debut, fin) : null;
    }
}
