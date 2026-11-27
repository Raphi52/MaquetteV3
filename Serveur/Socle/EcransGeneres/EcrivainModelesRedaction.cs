using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using L = RigV3.Serveur.Socle.EcransGeneres.LecteurModelesRedaction;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Modèles de rédaction » (PROC_REDAA), en grille : ce que les fiches de la V2 enregistrent en base -- le MODELE de document, la
/// MAQUETTE REDA, la SECTION et l'entree du GLOSSAIRE. Mode d'emploi : EcrivainEcran.cs ; la liste et les fiches :
/// LecteurModelesRedaction.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_REDAA.
///   MODELE (ETP_MODEL_MAQ\MODEL\OPE_EDT_MODEL.cs ; RigMetier\Maquette\ModeleDocument.cs) :
///   - MODIFIER = Valider (l. 58, 149-169) : Processus.Verifier, puis ModeleDocument.Sauve (ModeleDocument.cs:120-148) -- le code fige
///     (TextCode.MetierReadOnly, l. 98), libelle et type obligatoires (OPE_EDT_MODEL.Designer.cs:92, :148 ; le type, une valeur de la
///     table TYPE_MODELE_DOCUMENT : ULT_COMBO_TYPE_MODELE_DOCUMENT.cs:8-9), la case « Actif », et ses maquettes dans l'ordre : la liste ne
///     propose que les maquettes de type REDA (LoadMaquetteByType, l. 91 ; Maquette.cs:60), une maquette deja presente est refusee
///     (« Maquette déjà présente dans la liste ! », l. 219-220) ; Sauve renumerote les compositions 1..n dans l'ordre (l. 127-133) et
///     supprime celles retirees (l. 135-137). Ici la liste se saisit un code par ligne, ou separes par des virgules (la V2 : une
///     liste a monter / descendre) ;
///   - DUPLIQUER (l. 171-207) : le code du nouveau modele seul est saisi, « Ce code existe déjà » sinon (l. 201-205) ; le modele copie
///     libelle, type et actif (new ModeleDocument(old) = Copie des champs, ModeleDocument.cs:20). La V2 prepare aussi la copie de ses
///     compositions (l. 187-196) mais ne la rattache pas au modele copie : Sauve n'ecrit que l'en-tete (_lstCompositionModeleLoad faux,
///     ModeleDocument.cs:126). Repris tel quel : le message le dit ;
///   - SUPPRIMER (l. 209-216) : refuse tant que des maquettes restent liees, au message de la V2 ;
///   - AJOUTER : la V2 n'a aucun chemin vers un modele vide (pas de bouton Ajouter dans sa recherche, OPE_RECH_MODEL_MAQ.cs:67-70 ; le
///     constructeur vide de ModeleDocument est prive, ModeleDocument.cs:17-18) -- le titre « Ajout d'un modèle de document » (l. 96)
///     n'est jamais atteint. Un ajout se refuse en le disant.
///   SECTION (ETP_SECTION\OPE_EDT_SECTION.cs ; RIG_BASE SECTION_MAQUETTE.cs) :
///   - reservee a l'equipe Amitel : la barre Valider / Annuler / Supprimer n'existe que pour un utilisateur Amitel (l. 64-72 ;
///     Utilisateur.IsUserAmitel, le drapeau soc.Compte.EstAmitel repris de UTILISATEUR) ; la V2 cache, la V3 refuse en le disant ;
///   - AJOUTER (OPE_RECH_SECTION.cs:114-115 une section vide) et MODIFIER = Valider (l. 54-62) : tous les champs obligatoires
///     (OPE_EDT_SECTION.Designer.cs:81-139 : code, description, requete SQL, occurrence, fusion, generation, persistance), les niveaux au
///     texte de leurs listes (LecteurModelesRedaction.Niveaux) ; le code fige une fois cree (l. 108) ; un code deja pris : la cle
///     SCTMQ_CODE_SECTION_MAQUETTE le refuse en base (RequeteSauve insere quand la cle change, SqlBase.cs:506), ici avant ;
///   - SUPPRIMER (l. 75-82) : Delete, sans autre condition ; pas de duplication (MetierAllowUserDuplicate = false, l. 22).
///   GLOSSAIRE (ETP_GLOSSAIRE\OPE_EDT_GLOSSAIRE.cs) :
///   - les entrees de l'agent : les publiques et SES privees (Glossaire.GetAllGlossaireOfUserAndPublic, RigMetier\JUDICIAIRE\
///     Glossaire.cs:64-66 ; OPE_RECH_GLOSSAIRE.cs:96) -- une autre se dit introuvable ; « l'utilisateur courant » est
///     UTLST_LOGIN = le compte Windows (Utilisateur.GetUtilisateurCourant, RigMetier\Utilisateur.cs:424-431) ;
///   - la saisie (OPE_EDT_GLOSSAIRE.Designer.cs:79-100) : « Code » et « Texte associé » obligatoires, la description (ici la colonne
///     « Libellé ») facultative ; le code fige une fois cree (l. 117) ; une entree publique ne redevient pas privee (l. 118) ;
///   - Valider (l. 66-75) : GLOSS_PROPRIETAIRE = '*' (public) ou le login de l'agent (prive) ; Supprimer (l. 84-91) : Delete.
///   MAQUETTE REDA (ETP_MODEL_MAQ\MAQUETTE\OPE_EDT_MAQ.cs) :
///   - MODIFIER = Valider (l. 91-96) : Processus.Verifier -- le libelle obligatoire (OPE_EDT_MAQ.Designer.cs:80), le code fige
///     (MetierReadOnly, :71) -- puis Maquette.Sauve : la ligne MAQUETTE seule, le libelle (TextLib, l. 82) ;
///   - SUPPRIMER (l. 151-162) : « Il est impossible de supprimer cette maquette car elle est utilisée par au moins un modèle ! » si une
///     composition la nomme (l. 153-154) ; sinon Maquette.Delete puis, s'il a supprime une ligne, GedRig.Delete de son document (l. 157-159 :
///     la ligne GED_RIG seule, RigMetier\Divers\GedRig.cs:322-325 ; le fichier Word est la colonne GDRG_FICHIER de cette ligne).
/// Largeurs : celles des colonnes de la base du greffe (COL_LENGTH). La V2 n'a pas de message de reussite (la liste se met a jour) :
/// ceux d'ici disent ce qui est ecrit.
/// NON REPRIS (nommes) : DUPLIQUER une maquette REDA -- la V2 extrait son document Word sur le poste (FichierGedRig.GetFichier en zip)
/// et l'archive en GED sous un nouveau numero (GedRig.CreateGedRig, SetFileZipOnly, OPE_EDT_MAQ.cs:185-193) ; c'est aussi le seul
/// chemin de creation d'une maquette dans PROC_REDAA. « Afficher la maquette » l'ouvre dans Word (l. 103-149). La « Génération du
/// document » du glossaire (OPE_GEN_DOC_GLOSSAIRE) : un document Word.
/// TRACE : famille « reglage », « a cree / a modifie / a supprime », l'objet avant et apres.
/// </summary>
// fix-ok: la cause « une maquette REDA se modifie et se supprime avec son document Word en GED » etait fausse (objection du juge,
// 27/11/2026) : Valider et Supprimer n'ecrivent que les lignes MAQUETTE et GED_RIG (OPE_EDT_MAQ.cs:91-96, :151-162, relus).
internal sealed class EcrivainModelesRedaction : IEcrivainEcran
{
    public string Procedure => "PROC_REDAA";
    public string Ecran => "Modèles de rédaction";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    private static readonly string[] ColonnesLues = ["Nature", "Utilisation"];

    internal const string PasDeCreation = "Un modèle de document ne se crée qu'en dupliquant un modèle existant (« Dupliquer » au menu de sa ligne), comme dans RIG v2. Une maquette REDA ne se crée qu'en dupliquant une maquette avec son document Word, ce qui n'est pas repris ; une section et une entrée du glossaire s'ajoutent dans leur onglet.";
    internal const string MaquetteDupliquee = "Dupliquer une maquette REDA n'est pas repris : RIG v2 copie son document Word, extrait sur le poste puis archivé dans la GED sous un nouveau numéro.";
    internal const string MaquetteUtilisee = "Il est impossible de supprimer cette maquette car elle est utilisée par au moins un modèle !";
    internal const string SectionAmitel = "Les sections ne se modifient que par l'équipe Amitel : RIG v2 cache leur barre d'édition aux autres agents.";
    internal const string CodeFige = "Le code ne se modifie pas : RIG v2 le fige une fois la ligne enregistrée.";
    internal const string PubliqueFigee = "Une entrée publique ne redevient pas privée : RIG v2 fige ce choix une fois l'entrée publique.";
    internal const string RienAEnregistrer = "Rien à enregistrer : aucune valeur ne change.";
    internal const string CodeExisteDeja = "Ce code existe déjà";
    internal const string MaquettesLiees = "Vous devez supprimer les maquettes liées au modèle avant de le supprimer";
    internal const string MaquetteDejaPresente = "Maquette déjà présente dans la liste !";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Type == TypeGeste.Ajouter)
        {
            if (geste.Demande.Ids.Length > 0) return DupliquerAsync(geste, ct);
            if (Saisi(geste, L.SectionGlossaire, L.ChampTexte) is not null || Saisi(geste, L.SectionGlossaire, L.ChampPrive) is not null)
                return AjouterGlossaireAsync(geste, ct);
            if (ChampsSection.Any(c => Saisi(geste, L.SectionSection, c) is not null)) return AjouterSectionAsync(geste, ct);
            return Task.FromResult(IssueGeste.Refuser(PasDeCreation));
        }
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length != 1) return Task.FromResult(IssueGeste.Refuser("Choisissez une seule ligne."));
        var (nature, code) = Nature(ids[0]);
        var supprimer = geste.Demande.Type == TypeGeste.Supprimer;
        return nature switch
        {
            1 => supprimer ? SupprimerModeleAsync(geste, code, ct) : ModifierModeleAsync(geste, code, ct),
            2 => supprimer ? SupprimerMaquetteAsync(geste, code, ct) : ModifierMaquetteAsync(geste, code, ct),
            3 => supprimer ? SupprimerSectionAsync(geste, code, ct) : ModifierSectionAsync(geste, code, ct),
            4 => supprimer ? SupprimerGlossaireAsync(geste, code, ct) : ModifierGlossaireAsync(geste, code, ct),
            _ => Task.FromResult(IssueGeste.Refuser($"Ligne introuvable dans les modèles de rédaction : « {ids[0]} »."))
        };
    }

    // ---------------------------------------------------------------- MODELE DE DOCUMENT

    /// <summary>Entre deux codes de maquette : un retour a la ligne (la fiche les lit ainsi), une virgule ou un point-virgule.</summary>
    // fix-ok: mesure le 27/11/2026 -- un jeu .gestes ne peut pas porter de retour a la ligne dans une valeur (Blocs.Paire du banc
    // EcransGeneresBaseCheck ne decode pas « \n », cf. PROC_DJ_OCCULTATIONS.gestes:4) : la liste ordonnee s'ecrit aussi a la virgule.
    private static readonly char[] SeparateursMaquettes = ['\n', ',', ';'];

    private sealed record Modele(string Code, string Libelle, string Type, bool Actif);
    private sealed record Composition(int Id, string Maquette, int Index);

    private static async Task<IssueGeste> ModifierModeleAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (await ModeleAsync(geste, code, ct) is not { } e) return IssueGeste.Refuser($"Modèle de document introuvable : « {code} ».");
        if (Cellule(geste, L.ChampCode) is { } c && c != e.Code) return IssueGeste.Refuser(CodeFige);
        if (ColonneLue(geste) is { } lue) return IssueGeste.Refuser(lue);
        var libelle = Cellule(geste, L.ChampLibelle) ?? e.Libelle;
        var type = Saisi(geste, L.SectionModele, L.ChampType) ?? e.Type;
        var actif = Saisi(geste, L.SectionModele, L.ChampActif) is { } a ? Coche(a) : e.Actif;
        var compositions = await CompositionsAsync(geste, e.Code, ct);
        var avant = compositions.Select(x => x.Maquette).ToList();
        var maquettes = Saisi(geste, L.SectionModele, L.ChampMaquettes) is { } saisie
            ? saisie.Split(SeparateursMaquettes).Select(m => m.Trim()).Where(m => m.Length > 0).ToList()
            : avant;
        // Processus.Verifier : les champs obligatoires du formulaire, dans son ordre (OPE_EDT_MODEL.Designer.cs:92, :148).
        if (libelle.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(L.ChampLibelle));
        if (type.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(L.ChampType));
        if (type != e.Type && await TypesAsync(geste, ct) is var types && !types.Contains(type, StringComparer.Ordinal))
            return IssueGeste.Refuser($"« {L.ChampType} » : « {type} » n'est pas un type de modèle de ce greffe ({string.Join(", ", types)}).");
        if (maquettes.GroupBy(m => m, StringComparer.Ordinal).Any(g => g.Count() > 1)) return IssueGeste.Refuser(MaquetteDejaPresente);
        foreach (var m in maquettes.Where(m => !avant.Contains(m, StringComparer.Ordinal)))
            if (!await MaquetteRedaAsync(geste, m, ct))
                return IssueGeste.Refuser($"« {m} » n'est pas une maquette REDA de ce greffe : RIG v2 ne propose que celles-là.");
        if (libelle == e.Libelle && type == e.Type && actif == e.Actif && maquettes.SequenceEqual(avant, StringComparer.Ordinal))
            return IssueGeste.Refuser(RienAEnregistrer);
        if (await TropLongAsync(geste, "v2.MODELE_DOCUMENT", ct, ("MDDCM_LIBELLE", L.ChampLibelle, libelle), ("MDDCM_TYPE_MODELE_DOCUMENT", L.ChampType, type)) is { } long_)
            return IssueGeste.Refuser(long_);
        await geste.EcrireAsync("""
            UPDATE v2.MODELE_DOCUMENT SET MDDCM_LIBELLE = @l, MDDCM_TYPE_MODELE_DOCUMENT = @t, MDDCM_ACTIF = @a WHERE MDDCM_CODE_MODELE_DOCUMENT = @c
            """, ct, new SqlParameter("@l", libelle), new SqlParameter("@t", type), new SqlParameter("@a", actif), new SqlParameter("@c", e.Code));
        // ModeleDocument.Sauve (l. 126-137) : les retirees supprimees, les gardees et les neuves numerotees 1..n dans l'ordre.
        foreach (var retiree in compositions.Where(x => !maquettes.Contains(x.Maquette, StringComparer.Ordinal)))
            await geste.EcrireAsync("DELETE FROM v2.COMPOSITION_MODELE WHERE X_CMPMD_ID_CMPMD = @id", ct, new SqlParameter("@id", retiree.Id));
        for (var i = 0; i < maquettes.Count; i++)
        {
            if (compositions.FirstOrDefault(x => x.Maquette == maquettes[i]) is { } gardee)
            {
                if (gardee.Index != i + 1)
                    await geste.EcrireAsync("UPDATE v2.COMPOSITION_MODELE SET X_CMPMD_INDEX = @i WHERE X_CMPMD_ID_CMPMD = @id", ct,
                        new SqlParameter("@i", (short)(i + 1)), new SqlParameter("@id", gardee.Id));
            }
            else
                await geste.InsererAsync("v2.COMPOSITION_MODELE", "X_CMPMD_ID_CMPMD", new Dictionary<string, object?>
                {
                    ["X_CMPMD_CODE_MODELE_DOCUMENT"] = e.Code, ["X_CMPMD_CODE_MAQUETTE"] = maquettes[i], ["X_CMPMD_INDEX"] = (short)(i + 1),
                }, ct);
        }
        var id = L.Prefixes[0] + e.Code;
        return IssueGeste.Ecrit($"Modèle « {e.Code} » enregistré.", [id],
            new TraceGeste("reglage", $"le modèle de document {e.Code}", id, "a modifie", EtatModele(e.Libelle, e.Type, e.Actif, avant),
                EtatModele(libelle, type, actif, maquettes)));
    }

    /// <summary>« Dupliquer » une ligne de modele : un AJOUTER dont Ids porte le modele copie (descripteur, Duplication).</summary>
    private static async Task<IssueGeste> DupliquerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length != 1) return IssueGeste.Refuser("Choisissez une seule ligne.");
        var (nature, codeSource) = Nature(ids[0]);
        if (nature == 2) return IssueGeste.Refuser(MaquetteDupliquee);
        if (nature == 3) return IssueGeste.Refuser("RIG v2 ne duplique pas une section (OPE_EDT_SECTION : duplication interdite).");
        if (nature == 4) return IssueGeste.Refuser("RIG v2 ne duplique pas une entrée du glossaire : sa fiche n'a pas de « Dupliquer ».");
        if (nature != 1 || await ModeleAsync(geste, codeSource, ct) is not { } source)
            return IssueGeste.Refuser($"Modèle de document introuvable : « {ids[0]} ».");
        var code = Cellule(geste, L.ChampCode) ?? "";
        if (code.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(L.ChampCode));
        var libelle = Cellule(geste, L.ChampLibelle) is { Length: > 0 } saisi ? saisi : source.Libelle;
        if (await TropLongAsync(geste, "v2.MODELE_DOCUMENT", ct, ("MDDCM_CODE_MODELE_DOCUMENT", L.ChampCode, code), ("MDDCM_LIBELLE", L.ChampLibelle, libelle)) is { } long_)
            return IssueGeste.Refuser(long_);
        await using (var r = await geste.LireAsync(
                         "SELECT COUNT(*) FROM v2.MODELE_DOCUMENT WITH (UPDLOCK, HOLDLOCK) WHERE MDDCM_CODE_MODELE_DOCUMENT = @c", ct, new SqlParameter("@c", code)))
            if (await r.ReadAsync(ct) && r.GetInt32(0) > 0) return IssueGeste.Refuser(CodeExisteDeja);
        await geste.EcrireAsync("""
            INSERT INTO v2.MODELE_DOCUMENT (MDDCM_CODE_MODELE_DOCUMENT, MDDCM_LIBELLE, MDDCM_TYPE_MODELE_DOCUMENT, MDDCM_ACTIF) VALUES (@c, @l, @t, @a)
            """, ct, new SqlParameter("@c", code), new SqlParameter("@l", libelle), Valeur("@t", source.Type), new SqlParameter("@a", source.Actif));
        var id = L.Prefixes[0] + code;
        return IssueGeste.Ecrit($"Modèle « {code} » créé, copie de « {source.Code} » (libellé, type, actif). Comme dans RIG v2, ses maquettes ne sont pas recopiées : ajoutez-les en le modifiant.",
            [id], new TraceGeste("reglage", $"le modèle de document {code}", id, "a cree", null, EtatModele(libelle, source.Type, source.Actif, [])));
    }

    private static async Task<IssueGeste> SupprimerModeleAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (await ModeleAsync(geste, code, ct) is not { } e) return IssueGeste.Refuser($"Modèle de document introuvable : « {code} ».");
        var compositions = await CompositionsAsync(geste, e.Code, ct);
        if (compositions.Count > 0) return IssueGeste.Refuser(MaquettesLiees);
        await geste.EcrireAsync("DELETE FROM v2.MODELE_DOCUMENT WHERE MDDCM_CODE_MODELE_DOCUMENT = @c", ct, new SqlParameter("@c", e.Code));
        var id = L.Prefixes[0] + e.Code;
        return IssueGeste.Ecrit($"Modèle « {e.Code} » supprimé.", [id],
            new TraceGeste("reglage", $"le modèle de document {e.Code}", id, "a supprime", EtatModele(e.Libelle, e.Type, e.Actif, []), null));
    }

    private static async Task<Modele?> ModeleAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT MDDCM_CODE_MODELE_DOCUMENT, MDDCM_LIBELLE, MDDCM_TYPE_MODELE_DOCUMENT, MDDCM_ACTIF FROM v2.MODELE_DOCUMENT WITH (UPDLOCK)
            WHERE MDDCM_CODE_MODELE_DOCUMENT = @c
            """, ct, new SqlParameter("@c", code));
        return await r.ReadAsync(ct)
            ? new Modele(FormeLigne.Texte(r, "MDDCM_CODE_MODELE_DOCUMENT"), FormeLigne.Texte(r, "MDDCM_LIBELLE"), FormeLigne.Texte(r, "MDDCM_TYPE_MODELE_DOCUMENT"),
                !r.IsDBNull(3) && Convert.ToBoolean(r.GetValue(3), CultureInfo.InvariantCulture))
            : null;
    }

    private static async Task<List<Composition>> CompositionsAsync(GesteEnCours geste, string modele, CancellationToken ct)
    {
        var liste = new List<Composition>();
        await using var r = await geste.LireAsync("""
            SELECT X_CMPMD_ID_CMPMD, X_CMPMD_CODE_MAQUETTE, X_CMPMD_INDEX FROM v2.COMPOSITION_MODELE WITH (UPDLOCK)
            WHERE X_CMPMD_CODE_MODELE_DOCUMENT = @m ORDER BY X_CMPMD_INDEX, X_CMPMD_ID_CMPMD
            """, ct, new SqlParameter("@m", modele));
        while (await r.ReadAsync(ct))
            liste.Add(new Composition(r.GetInt32(0), FormeLigne.Texte(r, "X_CMPMD_CODE_MAQUETTE"),
                r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture)));
        return liste;
    }

    /// <summary>Les valeurs de la liste des types (ULT_COMBO_TYPE_MODELE_DOCUMENT : R_TYPEMODELE_VALEUR de TYPE_MODELE_DOCUMENT).</summary>
    private static async Task<List<string>> TypesAsync(GesteEnCours geste, CancellationToken ct)
    {
        var types = new List<string>();
        await using var r = await geste.LireAsync("SELECT R_TYPEMODELE_VALEUR FROM v2.TYPE_MODELE_DOCUMENT ORDER BY R_TYPEMODELE_VALEUR", ct);
        while (await r.ReadAsync(ct)) types.Add(FormeLigne.Texte(r, "R_TYPEMODELE_VALEUR"));
        return types;
    }

    private static async Task<bool> MaquetteRedaAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.MAQUETTE WHERE MQTT_CODE_MAQUETTE = @c AND MQTT_TYPE_MAQUETTE = 'REDA'", ct,
            new SqlParameter("@c", code));
        return await r.ReadAsync(ct) && r.GetInt32(0) > 0;
    }

    private static Dictionary<string, string> EtatModele(string libelle, string type, bool actif, IEnumerable<string> maquettes) => new()
    {
        ["Libellé"] = libelle, ["Type de modèle"] = type, ["Actif"] = actif ? "oui" : "non", ["Maquettes"] = string.Join(", ", maquettes),
    };

    // ---------------------------------------------------------------- MAQUETTE REDA

    private sealed record MaquetteRedaction(string Code, string Libelle, string Document);

    /// <summary>Valider (OPE_EDT_MAQ.cs:91-96) : Processus.Verifier -- le libelle obligatoire (.Designer.cs:80), le code fige
    /// (TextCode.MetierReadOnly, .Designer.cs:71) -- puis Maquette.Sauve, qui n'ecrit que la ligne MAQUETTE.</summary>
    private static async Task<IssueGeste> ModifierMaquetteAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (await MaquetteAsync(geste, code, ct) is not { } e) return IssueGeste.Refuser($"Maquette REDA introuvable : « {code} ».");
        if (Cellule(geste, L.ChampCode) is { } c && c != e.Code) return IssueGeste.Refuser(CodeFige);
        if (ColonneLue(geste) is { } lue) return IssueGeste.Refuser(lue);
        var libelle = Cellule(geste, L.ChampLibelle) ?? e.Libelle;
        if (libelle.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(L.ChampLibelle));
        if (libelle == e.Libelle) return IssueGeste.Refuser(RienAEnregistrer);
        if (await TropLongAsync(geste, "v2.MAQUETTE", ct, ("MQTT_LIBELLE", L.ChampLibelle, libelle)) is { } long_) return IssueGeste.Refuser(long_);
        await geste.EcrireAsync("UPDATE v2.MAQUETTE SET MQTT_LIBELLE = @l WHERE MQTT_CODE_MAQUETTE = @c", ct,
            new SqlParameter("@l", libelle), new SqlParameter("@c", e.Code));
        var id = L.Prefixes[1] + e.Code;
        return IssueGeste.Ecrit($"Maquette « {e.Code} » enregistrée.", [id],
            new TraceGeste("reglage", $"la maquette REDA {e.Code}", id, "a modifie",
                new Dictionary<string, string> { ["Libellé"] = e.Libelle }, new Dictionary<string, string> { ["Libellé"] = libelle }));
    }

    /// <summary>_SupprimerMaquette (OPE_EDT_MAQ.cs:151-162) : refusee si une composition de modele la nomme
    /// (CompositionModele.GetCompositionModeleByMaquette), sinon la ligne MAQUETTE puis, si elle est partie, la ligne GED_RIG de son
    /// document (GedRig.Delete, RigMetier\Divers\GedRig.cs:322-325 : la ligne seule ; le fichier Word est dans GDRG_FICHIER). Un numero
    /// sans document en GED : la V2 leve sur gedRig null apres avoir supprime la maquette ; ici la maquette part, rien d'autre.</summary>
    private static async Task<IssueGeste> SupprimerMaquetteAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (await MaquetteAsync(geste, code, ct) is not { } e) return IssueGeste.Refuser($"Maquette REDA introuvable : « {code} ».");
        await using (var r = await geste.LireAsync(
                         "SELECT COUNT(*) FROM v2.COMPOSITION_MODELE WITH (UPDLOCK, HOLDLOCK) WHERE X_CMPMD_CODE_MAQUETTE = @c", ct, new SqlParameter("@c", e.Code)))
            if (await r.ReadAsync(ct) && r.GetInt32(0) > 0) return IssueGeste.Refuser(MaquetteUtilisee);
        var documentSupprime = false;
        if (await geste.EcrireAsync("DELETE FROM v2.MAQUETTE WHERE MQTT_CODE_MAQUETTE = @c", ct, new SqlParameter("@c", e.Code)) == 1 && e.Document.Length > 0)
            documentSupprime = await geste.EcrireAsync("DELETE FROM v2.GED_RIG WHERE GDRG_NUM_DOCUMENT = @d", ct, new SqlParameter("@d", e.Document)) > 0;
        var id = L.Prefixes[1] + e.Code;
        return IssueGeste.Ecrit(documentSupprime
                ? $"Maquette « {e.Code} » supprimée, avec son document Word de la GED ({e.Document})."
                : $"Maquette « {e.Code} » supprimée (aucun document Word dans la GED du greffe).", [id],
            new TraceGeste("reglage", $"la maquette REDA {e.Code}", id, "a supprime",
                new Dictionary<string, string> { ["Libellé"] = e.Libelle, ["Document GED"] = documentSupprime ? e.Document : "" }, null));
    }

    private static async Task<MaquetteRedaction?> MaquetteAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT MQTT_CODE_MAQUETTE, MQTT_LIBELLE, MQTT_NUM_DOCUMENT FROM v2.MAQUETTE WITH (UPDLOCK)
            WHERE MQTT_CODE_MAQUETTE = @c AND MQTT_TYPE_MAQUETTE = 'REDA'
            """, ct, new SqlParameter("@c", code));
        return await r.ReadAsync(ct)
            ? new MaquetteRedaction(FormeLigne.Texte(r, "MQTT_CODE_MAQUETTE"), FormeLigne.Texte(r, "MQTT_LIBELLE"), FormeLigne.Texte(r, "MQTT_NUM_DOCUMENT").Trim())
            : null;
    }

    // ---------------------------------------------------------------- SECTION (equipe Amitel)

    private static readonly string[] ChampsSection = [L.ChampRequete, .. L.Niveaux.Select(n => n.Champ)];

    private sealed record Section(string Code, string Libelle, string Requete, int[] Niveaux);

    private static async Task<IssueGeste> AjouterSectionAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (!geste.Agent.EstAmitel) return IssueGeste.Refuser(SectionAmitel);
        var code = Cellule(geste, L.ChampCode) ?? "";
        if (code.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(L.ChampCode));
        var (s, refus) = LireSection(code, Cellule(geste, L.ChampLibelle) ?? "", Saisi(geste, L.SectionSection, L.ChampRequete) ?? "",
            L.Niveaux.Select(n => Saisi(geste, L.SectionSection, n.Champ) ?? "").ToArray());
        if (s is null) return IssueGeste.Refuser(refus!);
        if (await TropLongSectionAsync(geste, s, ct) is { } long_) return IssueGeste.Refuser(long_);
        await using (var r = await geste.LireAsync(
                         "SELECT COUNT(*) FROM v2.SECTION_MAQUETTE WITH (UPDLOCK, HOLDLOCK) WHERE SCTMQ_CODE_SECTION_MAQUETTE = @c", ct, new SqlParameter("@c", code)))
            if (await r.ReadAsync(ct) && r.GetInt32(0) > 0) return IssueGeste.Refuser($"Le code « {code} » existe déjà dans les sections.");
        await geste.EcrireAsync("""
            INSERT INTO v2.SECTION_MAQUETTE (SCTMQ_CODE_SECTION_MAQUETTE, SCTMQ_LIBELLE, SCTMQ_REQUETE_SQL, SCTMQ_NIVEAU_OCCURRENCE, SCTMQ_NIVEAU_FUSION,
                                             SCTMQ_NIVEAU_GENERATION, SCTMQ_NIVEAU_PERSISTANCE)
            VALUES (@c, @l, @q, @n0, @n1, @n2, @n3)
            """, ct, ParametresSection(s));
        var id = L.Prefixes[2] + code;
        return IssueGeste.Ecrit($"Section « {code} » ajoutée.", [id], new TraceGeste("reglage", $"la section {code}", id, "a cree", null, EtatSection(s)));
    }

    private static async Task<IssueGeste> ModifierSectionAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (!geste.Agent.EstAmitel) return IssueGeste.Refuser(SectionAmitel);
        if (await SectionAsync(geste, code, ct) is not { } e) return IssueGeste.Refuser($"Section introuvable : « {code} ».");
        if (Cellule(geste, L.ChampCode) is { } c && c != e.Code) return IssueGeste.Refuser(CodeFige);
        if (ColonneLue(geste) is { } lue) return IssueGeste.Refuser(lue);
        var niveaux = L.Niveaux.Select((n, i) => Saisi(geste, L.SectionSection, n.Champ) ?? Texte(i, e.Niveaux[i])).ToArray();
        var (s, refus) = LireSection(e.Code, Cellule(geste, L.ChampLibelle) ?? e.Libelle, Saisi(geste, L.SectionSection, L.ChampRequete) ?? e.Requete, niveaux);
        if (s is null) return IssueGeste.Refuser(refus!);
        if (s.Libelle == e.Libelle && s.Requete == e.Requete && s.Niveaux.SequenceEqual(e.Niveaux)) return IssueGeste.Refuser(RienAEnregistrer);
        if (await TropLongSectionAsync(geste, s, ct) is { } long_) return IssueGeste.Refuser(long_);
        await geste.EcrireAsync("""
            UPDATE v2.SECTION_MAQUETTE SET SCTMQ_LIBELLE = @l, SCTMQ_REQUETE_SQL = @q, SCTMQ_NIVEAU_OCCURRENCE = @n0, SCTMQ_NIVEAU_FUSION = @n1,
                                           SCTMQ_NIVEAU_GENERATION = @n2, SCTMQ_NIVEAU_PERSISTANCE = @n3
            WHERE SCTMQ_CODE_SECTION_MAQUETTE = @c
            """, ct, ParametresSection(s));
        var id = L.Prefixes[2] + e.Code;
        return IssueGeste.Ecrit($"Section « {e.Code} » enregistrée.", [id],
            new TraceGeste("reglage", $"la section {e.Code}", id, "a modifie", EtatSection(e), EtatSection(s)));
    }

    private static async Task<IssueGeste> SupprimerSectionAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (!geste.Agent.EstAmitel) return IssueGeste.Refuser(SectionAmitel);
        if (await SectionAsync(geste, code, ct) is not { } e) return IssueGeste.Refuser($"Section introuvable : « {code} ».");
        await geste.EcrireAsync("DELETE FROM v2.SECTION_MAQUETTE WHERE SCTMQ_CODE_SECTION_MAQUETTE = @c", ct, new SqlParameter("@c", e.Code));
        var id = L.Prefixes[2] + e.Code;
        return IssueGeste.Ecrit($"Section « {e.Code} » supprimée.", [id], new TraceGeste("reglage", $"la section {e.Code}", id, "a supprime", EtatSection(e), null));
    }

    /// <summary>La section saisie si chaque champ obligatoire est rempli et chaque niveau une option de sa liste, dans l'ordre du
    /// formulaire (OPE_EDT_SECTION.Designer.cs:81-139) ; sinon le refus.</summary>
    private static (Section? Section, string? Refus) LireSection(string code, string libelle, string requete, string[] niveaux)
    {
        if (libelle.Trim().Length == 0) return (null, CreationV2.Obligatoire(L.ChampLibelle));
        if (requete.Trim().Length == 0) return (null, CreationV2.Obligatoire(L.ChampRequete));
        var valeurs = new int[L.Niveaux.Length];
        for (var i = 0; i < L.Niveaux.Length; i++)
        {
            var (champ, _, options) = L.Niveaux[i];
            var texte = niveaux[i].Trim();
            if (texte.Length == 0) return (null, CreationV2.Obligatoire(champ));
            if (options.FirstOrDefault(o => o.Texte == texte) is not { Texte: not null } o)
                return (null, $"« {champ} » : choisissez {string.Join(" ou ", options.Select(x => $"« {x.Texte} »"))}.");
            valeurs[i] = o.Valeur;
        }
        return (new Section(code, libelle.Trim(), requete.Trim(), valeurs), null);
    }

    private static string Texte(int niveau, int valeur) => L.Niveaux[niveau].Options.FirstOrDefault(o => o.Valeur == valeur).Texte ?? "";

    private static async Task<Section?> SectionAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT SCTMQ_CODE_SECTION_MAQUETTE, SCTMQ_LIBELLE, SCTMQ_REQUETE_SQL, SCTMQ_NIVEAU_OCCURRENCE, SCTMQ_NIVEAU_FUSION, SCTMQ_NIVEAU_GENERATION,
                   SCTMQ_NIVEAU_PERSISTANCE
            FROM v2.SECTION_MAQUETTE WITH (UPDLOCK) WHERE SCTMQ_CODE_SECTION_MAQUETTE = @c
            """, ct, new SqlParameter("@c", code));
        if (!await r.ReadAsync(ct)) return null;
        var niveaux = Enumerable.Range(3, 4).Select(i => r.IsDBNull(i) ? -1 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture)).ToArray();
        return new Section(FormeLigne.Texte(r, "SCTMQ_CODE_SECTION_MAQUETTE"), FormeLigne.Texte(r, "SCTMQ_LIBELLE"), FormeLigne.Texte(r, "SCTMQ_REQUETE_SQL"), niveaux);
    }

    private static Task<string?> TropLongSectionAsync(GesteEnCours geste, Section s, CancellationToken ct)
        => TropLongAsync(geste, "v2.SECTION_MAQUETTE", ct, ("SCTMQ_CODE_SECTION_MAQUETTE", L.ChampCode, s.Code), ("SCTMQ_LIBELLE", L.ChampLibelle, s.Libelle),
            ("SCTMQ_REQUETE_SQL", L.ChampRequete, s.Requete));

    private static SqlParameter[] ParametresSection(Section s) =>
    [
        new("@c", s.Code), new("@l", s.Libelle), new("@q", s.Requete),
        new("@n0", s.Niveaux[0]), new("@n1", s.Niveaux[1]), new("@n2", s.Niveaux[2]), new("@n3", s.Niveaux[3]),
    ];

    private static Dictionary<string, string> EtatSection(Section s)
    {
        var etat = new Dictionary<string, string> { ["Libellé"] = s.Libelle, ["Requête SQL"] = s.Requete };
        for (var i = 0; i < L.Niveaux.Length; i++) etat[L.Niveaux[i].Champ] = Texte(i, s.Niveaux[i]);
        return etat;
    }

    // ---------------------------------------------------------------- GLOSSAIRE

    private static async Task<IssueGeste> AjouterGlossaireAsync(GesteEnCours geste, CancellationToken ct)
    {
        var texte = Saisi(geste, L.SectionGlossaire, L.ChampTexte);
        var prive = Saisi(geste, L.SectionGlossaire, L.ChampPrive);
        var code = Cellule(geste, L.ChampCode) ?? "";
        var libelle = Cellule(geste, L.ChampLibelle) ?? "";
        if (code.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(L.ChampCode));
        if (string.IsNullOrEmpty(texte)) return IssueGeste.Refuser(CreationV2.Obligatoire(L.ChampTexte));
        var estPrive = Coche(prive);
        var proprietaire = estPrive ? Login(geste) : "*";
        if (await TropLongAsync(geste, "v2.GLOSSAIRE", ct, ("GLOSS_CODE_GLOSSAIRE", L.ChampCode, code), ("GLOSS_LIBELLE", L.ChampLibelle, libelle),
                ("GLOSS_TEXTE", L.ChampTexte, texte), ("GLOSS_PROPRIETAIRE", "Propriétaire", proprietaire)) is { } long_)
            return IssueGeste.Refuser(long_);
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.GLOSSAIRE WITH (UPDLOCK, HOLDLOCK) WHERE GLOSS_CODE_GLOSSAIRE = @c", ct,
                         new SqlParameter("@c", code)))
            if (await r.ReadAsync(ct) && r.GetInt32(0) > 0) return IssueGeste.Refuser($"Le code « {code} » existe déjà dans le glossaire.");
        await geste.EcrireAsync("""
            INSERT INTO v2.GLOSSAIRE (GLOSS_CODE_GLOSSAIRE, GLOSS_LIBELLE, GLOSS_TEXTE, GLOSS_PROPRIETAIRE) VALUES (@c, @l, @t, @p)
            """, ct, new SqlParameter("@c", code), Valeur("@l", libelle), new SqlParameter("@t", texte), new SqlParameter("@p", proprietaire));
        var id = L.Prefixes[3] + code;
        return IssueGeste.Ecrit(estPrive
                ? $"Entrée « {code} » ajoutée à votre glossaire privé : la liste ne montre que le glossaire public."
                : $"Entrée « {code} » ajoutée au glossaire public.", [id],
            new TraceGeste("reglage", $"l'entrée du glossaire {code}", id, "a cree", null, EtatGlossaire(libelle, texte, proprietaire)));
    }

    private static async Task<IssueGeste> ModifierGlossaireAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (await EntreeAsync(geste, code, ct) is not { } e) return IssueGeste.Refuser(EntreeIntrouvable(code));
        if (Cellule(geste, L.ChampCode) is { } c && c != e.Code) return IssueGeste.Refuser(CodeFige);
        if (ColonneLue(geste) is { } lue) return IssueGeste.Refuser(lue);
        var libelle = Cellule(geste, L.ChampLibelle) ?? e.Libelle;
        var texte = Saisi(geste, L.SectionGlossaire, L.ChampTexte) ?? e.Texte;
        var prive = Saisi(geste, L.SectionGlossaire, L.ChampPrive) is { } p ? Coche(p) : e.Proprietaire != "*";
        if (e.Proprietaire == "*" && prive) return IssueGeste.Refuser(PubliqueFigee);
        if (texte.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(L.ChampTexte));
        var proprietaire = prive ? e.Proprietaire : "*";
        if (libelle == e.Libelle && texte == e.Texte && proprietaire == e.Proprietaire) return IssueGeste.Refuser(RienAEnregistrer);
        if (await TropLongAsync(geste, "v2.GLOSSAIRE", ct, ("GLOSS_LIBELLE", L.ChampLibelle, libelle), ("GLOSS_TEXTE", L.ChampTexte, texte)) is { } long_)
            return IssueGeste.Refuser(long_);
        await geste.EcrireAsync("""
            UPDATE v2.GLOSSAIRE SET GLOSS_LIBELLE = @l, GLOSS_TEXTE = @t, GLOSS_PROPRIETAIRE = @p WHERE GLOSS_CODE_GLOSSAIRE = @c
            """, ct, Valeur("@l", libelle), new SqlParameter("@t", texte), new SqlParameter("@p", proprietaire), new SqlParameter("@c", e.Code));
        var id = L.Prefixes[3] + e.Code;
        return IssueGeste.Ecrit($"Entrée « {e.Code} » du glossaire enregistrée.", [id],
            new TraceGeste("reglage", $"l'entrée du glossaire {e.Code}", id, "a modifie", EtatGlossaire(e.Libelle, e.Texte, e.Proprietaire),
                EtatGlossaire(libelle, texte, proprietaire)));
    }

    private static async Task<IssueGeste> SupprimerGlossaireAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (await EntreeAsync(geste, code, ct) is not { } e) return IssueGeste.Refuser(EntreeIntrouvable(code));
        await geste.EcrireAsync("DELETE FROM v2.GLOSSAIRE WHERE GLOSS_CODE_GLOSSAIRE = @c", ct, new SqlParameter("@c", e.Code));
        var id = L.Prefixes[3] + e.Code;
        return IssueGeste.Ecrit($"Entrée « {e.Code} » supprimée du glossaire.", [id],
            new TraceGeste("reglage", $"l'entrée du glossaire {e.Code}", id, "a supprime", EtatGlossaire(e.Libelle, e.Texte, e.Proprietaire), null));
    }

    private sealed record Entree(string Code, string Libelle, string Texte, string Proprietaire);

    /// <summary>L'entree du glossaire visee, si l'agent la voit (publique, ou privee a lui) ; null sinon.</summary>
    private static async Task<Entree?> EntreeAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT GLOSS_CODE_GLOSSAIRE, GLOSS_LIBELLE, GLOSS_TEXTE, GLOSS_PROPRIETAIRE FROM v2.GLOSSAIRE WITH (UPDLOCK)
            WHERE GLOSS_CODE_GLOSSAIRE = @c AND (GLOSS_PROPRIETAIRE = '*' OR GLOSS_PROPRIETAIRE = @login)
            """, ct, new SqlParameter("@c", code), new SqlParameter("@login", Login(geste)));
        return await r.ReadAsync(ct)
            ? new Entree(FormeLigne.Texte(r, "GLOSS_CODE_GLOSSAIRE"), FormeLigne.Texte(r, "GLOSS_LIBELLE"), FormeLigne.Texte(r, "GLOSS_TEXTE"),
                FormeLigne.Texte(r, "GLOSS_PROPRIETAIRE"))
            : null;
    }

    private static string EntreeIntrouvable(string code) => $"Entrée introuvable dans votre glossaire (publique, ou privée à vous) : « {code} ».";

    private static Dictionary<string, string> EtatGlossaire(string libelle, string texte, string proprietaire) => new()
    {
        ["Libellé"] = libelle, ["Texte associé"] = texte, ["Propriétaire"] = proprietaire == "*" ? "public" : proprietaire,
    };

    // ---------------------------------------------------------------- COMMUN

    /// <summary>La nature (1 modele, 2 maquette, 3 section, 4 glossaire ; 0 inconnue) et le code d'un Id de ligne.</summary>
    private static (int Nature, string Code) Nature(string id)
    {
        var n = Array.FindIndex(L.Prefixes, p => id.StartsWith(p, StringComparison.Ordinal));
        return n < 0 ? (0, id) : (n + 1, id[L.Prefixes[n].Length..]);
    }

    /// <summary>La premiere valeur plus large que sa colonne de <paramref name="table"/> dans la base du greffe ; null si toutes tiennent.</summary>
    private static async Task<string?> TropLongAsync(GesteEnCours geste, string table, CancellationToken ct, params (string Colonne, string Libelle, string Valeur)[] valeurs)
    {
        foreach (var (colonne, libelle, valeur) in valeurs)
        {
            await using var r = await geste.LireAsync("SELECT COL_LENGTH(@t, @c)", ct, new SqlParameter("@t", table), new SqlParameter("@c", colonne));
            var largeur = await r.ReadAsync(ct) && !r.IsDBNull(0) ? Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture) : 0;
            if (largeur > 0 && valeur.Length > largeur) return $"« {libelle} » : {largeur} caractères au plus.";
        }
        return null;
    }

    /// <summary>Le refus d'une colonne lue (Nature, Utilisation) envoyee comme saisie ; null sinon.</summary>
    private static string? ColonneLue(GesteEnCours geste)
        => ColonnesLues.FirstOrDefault(c => Cellule(geste, c) is not null) is { } lue ? $"« {lue} » se lit, ne se saisit pas." : null;

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private static string? Saisi(GesteEnCours geste, string section, string libelle) => geste.Valeur(section, libelle)?.Trim();

    private static bool Coche(string? valeur) => valeur?.ToLowerInvariant() is "oui" or "true" or "1";

    /// <summary>UTLST_LOGIN de la V2 : le compte Windows de l'agent (Utilisateur.GetUtilisateurByName(AmiEnvironment.UserName)).</summary>
    private static string Login(GesteEnCours geste) => geste.Agent.CompteWindows;

    private static SqlParameter Valeur(string nom, string valeur) => new(nom, valeur.Length == 0 ? DBNull.Value : valeur);
}
