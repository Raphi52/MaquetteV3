using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurMandataires;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mandataires » (PROC_MANDATAIRE) : AJOUTER, MODIFIER et SUPPRIMER un mandataire, dans la grille (Referentiel). Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurMandataires.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026. Les cellules arrivent
/// par libelle de colonne (« Désignation », « Civilité », « Abrégé », « SIREN », « Courriel », « Qualification », « Actif »).
///
/// SOURCE (ancien RIG) : PROC_MANDATAIRE et son operation OPE_SAISIE_MANDATAIRE (assembly Rig_Etape_Mandataire ; Detail OPE de
/// proc_mandataire.md, qui fait foi). Les controles, AVANT toute ecriture, au message de la V2 quand elle en a un :
///   - civilite et designation obligatoires (MetierObligatoire du combo civilite et de « Désignation », proc_mandataire.md,
///     « Contrôles réels ») ; la civilite est une valeur de la table CIVILITE (ULT_COMBO_CIVILITE) ;
///   - l'abrege est en majuscules (casse forcee), 16 caracteres au plus (MNDTR_ABREGE_LENGTH = 16, MANDATAIRE.cs:59) ; vide, le
///     mandataire n'est pas « répertorié » (DoVerifier, OPE_SAISIE_MANDATAIRE.cs:138) ; un abrege deja porte par un AUTRE mandataire
///     est refuse « Abrege existant » (Mandataire.AbregeExiste, Mandataire.cs:637-653 ; en modification la V2 exclut le
///     mandataire lui-meme, OPE_SAISIE_MANDATAIRE.cs:143-151) ;
///   - le courriel suit la regle de la V2 (RIG.TOOLS.RegEx.Email, RegEx.cs:17-19), sinon « Email invalide »
///     (OPE_SAISIE_MANDATAIRE.cs:76-77) ;
///   - le SIREN a 9 chiffres (ULT_TEXT_SIREN, MaxLength 9) ; la qualification est une valeur de QUALIF_PROFESSION
///     (ULT_COMBO_QUALIF_PROFESSION) ;
///   - « Actif » n'existe qu'en modification (le setter MandataireCreation ne montre la case qu'alors,
///     OPE_SAISIE_MANDATAIRE.cs:37-45) : un mandataire cree est actif (CreerMandataire, PROC_MANDATAIRE.cs:61-65).
/// AJOUTER : Mandataire.CreateMandataire (Mandataire.cs:540-556) -- un numero source neuf (GUID, MNDTR_NUMERO_SOURCE, :552), actif.
/// SUPPRIMER : la V2 ne supprime physiquement un mandataire (« Delete MANDATAIRE », frmRapproActeurs.cs:1102 ;
///   m.Delete(cnx), OPE_SELECTION_ACTEUR.Mandataires.cs:149) qu'APRES avoir reporte toutes ses liaisons sur un autre mandataire
///   (la fusion de PROC_RAPPACTEUR : « un acteur n'est jamais detruit sans que ses references aient d'abord ete transferees »,
///   proc_rappacteur.md, Detail OPE). D'ou, ici : un mandataire qu'une liaison reference (LiaisonsMandataire.cs) est refuse et
///   l'ecran nomme « Rapprocher des acteurs » ; un mandataire actif qui a un numero CNBF est refuse au message de la V2
///   (Res.TEXTE_MANDATAIRE_ACTIF_AVEC_CNBF, PROC_RAPPACTEUR\Res.cs:35 ; GetSelectedMandataireActifsAvecCNBF,
///   RigDataGridViewMandataires.cs:189-195). Sans liaison, rien n'est a reprendre : la ligne est supprimee.
/// NON REPRIS (nommes) : le chargement par le SIREN (mandataire local, puis le service InfoGreffe, OPE_SAISIE_MANDATAIRE.cs:168-270)
///   -- un service externe, jamais appele ; l'adresse (contrôle composite Ult_Adresse sur ADRESSE_BASE) et le client de
///   facturation (fenetre de recherche client) ne se saisissent pas dans la grille : ils se lisent dans la fiche ; l'invalidation
///   du cache des listes (RigComboBoxObjectMoteurCache, OPE_SAISIE_MANDATAIRE.cs:130), propre au client lourd.
/// TRACE : famille « reglage », « a cree » / « a modifie » / « a supprime », avant et apres.
/// </summary>
internal sealed partial class EcrivainMandataires : IEcrivainEcran
{
    public string Procedure => "PROC_MANDATAIRE";
    public string Ecran => "Mandataires";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    internal const string AbregeExistant = "Abrege existant";
    internal const string EmailInvalide = "Email invalide";
    /// <summary>PROC_RAPPACTEUR\Res.cs:35, {0} = le nom complet du mandataire.</summary>
    internal const string ActifAvecCnbf = "Impossible de supprimer un mandataire actif avec abrégé et n° CNBF. ({0}) Passer par le processus RPVAINS";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => AjouterAsync(geste, ct),
        TypeGeste.Supprimer => SupprimerAsync(geste, ct),
        _ => ModifierAsync(geste, ct),
    };

    private static string? Cellule(GesteEnCours geste, string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : null;

    private sealed record Mandataire(int Id, string Civilite, string Nom, string Abrege, string Siren, string Courriel, string Qualification,
                                     bool Actif, string Cnbf, string AbregeEdition)
    {
        public string NomComplet => NomComplet(Civilite, AbregeEdition, Nom);
        public object Valeurs => new { Civilite, Designation = Nom, Abrege, Siren, Courriel, Qualification, Actif };
    }

    /// <summary>Les valeurs saisies, controlees comme la V2 ; un message de refus, ou null.</summary>
    private static async Task<string?> ControlerAsync(GesteEnCours geste, Mandataire m, CancellationToken ct)
    {
        if (m.Civilite.Length == 0) return $"« {ColCivilite} » est obligatoire.";
        if (m.Nom.Length == 0) return $"« {ColDesignation} » est obligatoire.";
        if (m.Nom.Length > 128) return $"« {ColDesignation} » : 128 caractères au plus.";
        if (m.Abrege.Length > 16) return $"« {ColAbrege} » : 16 caractères au plus ({m.Abrege}).";
        if (m.Siren.Length > 0 && !(m.Siren.Length == 9 && m.Siren.All(char.IsAsciiDigit))) return $"« {ColSiren} » : 9 chiffres ({m.Siren}).";
        if (m.Courriel.Length > 0 && (m.Courriel.Length > 70 || !Email().IsMatch(m.Courriel))) return EmailInvalide;
        if (m.Qualification.Length > 0 && !await ExisteAsync(geste, "SELECT COUNT(*) FROM v2.QUALIF_PROFESSION WHERE R_QUALIFPROF_VALEUR = @v", m.Qualification, ct))
            return $"« {ColQualification} » : « {m.Qualification} » n'est pas une qualification de la profession (QUALIF_PROFESSION).";
        if (m.Abrege.Length > 0)
        {
            await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.MANDATAIRE WHERE MNDTR_ABREGE = @a AND MNDTR_ID_MNDTR <> @id", ct,
                new SqlParameter("@a", m.Abrege), new SqlParameter("@id", m.Id));
            await r.ReadAsync(ct);
            if (r.GetInt32(0) > 0) return AbregeExistant;
        }
        return null;
    }

    /// <summary>La civilite saisie, en code de la table CIVILITE (« ME ») : le code ou son abrege d'edition (« Me ») ; null si aucune.</summary>
    private static async Task<(string Code, string Edition)?> CiviliteAsync(GesteEnCours geste, string saisie, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) R_CIVILITE_VALEUR, R_CIVILITE_ABREGE_EDIT FROM v2.CIVILITE
            WHERE R_CIVILITE_VALEUR = @c OR R_CIVILITE_ABREGE_EDIT = @c ORDER BY CASE WHEN R_CIVILITE_VALEUR = @c THEN 0 ELSE 1 END
            """, ct, new SqlParameter("@c", saisie));
        return await r.ReadAsync(ct) ? (FormeLigne.Texte(r, "R_CIVILITE_VALEUR"), FormeLigne.Texte(r, "R_CIVILITE_ABREGE_EDIT")) : null;
    }

    private static async Task<bool> ExisteAsync(GesteEnCours geste, string sql, string valeur, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct, new SqlParameter("@v", valeur));
        await r.ReadAsync(ct);
        return r.GetInt32(0) > 0;
    }

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        // La case « Actif » d'une ligne neuve est ignoree : la V2 ne la montre qu'en modification et cree le mandataire actif
        // (CreerMandataire, PROC_MANDATAIRE.cs:61-65). fix-ok: mesure au banc (bloc d'ajout « Actif = ») -- la grille envoie une case
        // vide pour « non cochee », que rien ne distingue d'une case jamais touchee : la refuser bloquait tout ajout.
        var saisieCivilite = Cellule(geste, ColCivilite) ?? "";
        var civilite = saisieCivilite.Length == 0 ? null : await CiviliteAsync(geste, saisieCivilite, ct);
        if (saisieCivilite.Length > 0 && civilite is null)
            return IssueGeste.Refuser($"« {ColCivilite} » : « {saisieCivilite} » n'est pas une civilité de la table CIVILITE.");
        var m = new Mandataire(0, civilite?.Code ?? "", Cellule(geste, ColDesignation) ?? "", (Cellule(geste, ColAbrege) ?? "").ToUpperInvariant(),
            (Cellule(geste, ColSiren) ?? "").Replace(" ", "", StringComparison.Ordinal), Cellule(geste, ColCourriel) ?? "",
            (Cellule(geste, ColQualification) ?? "").ToUpperInvariant(), true, "", civilite?.Edition ?? "");
        if (await ControlerAsync(geste, m, ct) is { } refus) return IssueGeste.Refuser(refus);

        var nouveau = await geste.InsererAsync("v2.MANDATAIRE", "MNDTR_ID_MNDTR", new Dictionary<string, object?>
        {
            ["MNDTR_CIVILITE"] = m.Civilite, ["MNDTR_NOM"] = m.Nom, ["MNDTR_ABREGE"] = Nul(m.Abrege), ["MNDTR_SIREN"] = Nul(m.Siren),
            ["MNDTR_ADRESSE_MAIL"] = Nul(m.Courriel), ["MNDTR_QUALIF_PROFESSION"] = Nul(m.Qualification), ["MNDTR_ACTIF"] = true,
            ["MNDTR_NUMERO_SOURCE"] = Guid.NewGuid().ToString(),
        }, ct);
        var id = nouveau.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var trace = new TraceGeste("reglage", $"le mandataire {m.NomComplet}", id, "a cree", null, m.Valeurs);
        return IssueGeste.Ecrit($"Mandataire {m.NomComplet} créé{(m.Abrege.Length > 0 ? $", abrégé {m.Abrege}" : ", non répertorié")}.", [id], trace);
    }

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (CriteresConsultation.Entier(id) is not { } n || await LireAsync(geste, n, ct) is not { } avant)
            return IssueGeste.Refuser($"Mandataire introuvable dans la base du greffe ({id}).");
        var civilite = (avant.Civilite, avant.AbregeEdition);
        if (Cellule(geste, ColCivilite) is { } saisie && saisie != avant.Civilite)
        {
            if (saisie.Length == 0) return IssueGeste.Refuser($"« {ColCivilite} » est obligatoire.");
            if (await CiviliteAsync(geste, saisie, ct) is not { } lue)
                return IssueGeste.Refuser($"« {ColCivilite} » : « {saisie} » n'est pas une civilité de la table CIVILITE.");
            civilite = lue;
        }
        var actif = avant.Actif;
        if (Cellule(geste, ColActif) is { } caseActif) actif = caseActif == "oui";
        var apres = avant with
        {
            Civilite = civilite.Item1, AbregeEdition = civilite.Item2, Nom = Cellule(geste, ColDesignation) ?? avant.Nom,
            Abrege = (Cellule(geste, ColAbrege) ?? avant.Abrege).ToUpperInvariant(),
            Siren = (Cellule(geste, ColSiren) ?? avant.Siren).Replace(" ", "", StringComparison.Ordinal),
            Courriel = Cellule(geste, ColCourriel) ?? avant.Courriel, Qualification = (Cellule(geste, ColQualification) ?? avant.Qualification).ToUpperInvariant(),
            Actif = actif,
        };
        if (apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");
        if (await ControlerAsync(geste, apres, ct) is { } refus) return IssueGeste.Refuser(refus);

        await geste.EcrireAsync("""
            UPDATE v2.MANDATAIRE SET MNDTR_CIVILITE = @civilite, MNDTR_NOM = @nom, MNDTR_ABREGE = @abrege, MNDTR_SIREN = @siren,
                MNDTR_ADRESSE_MAIL = @courriel, MNDTR_QUALIF_PROFESSION = @qualif, MNDTR_ACTIF = @actif
            WHERE MNDTR_ID_MNDTR = @id
            """, ct, new SqlParameter("@civilite", apres.Civilite), new SqlParameter("@nom", apres.Nom),
            new SqlParameter("@abrege", Nul(apres.Abrege) ?? DBNull.Value), new SqlParameter("@siren", Nul(apres.Siren) ?? DBNull.Value),
            new SqlParameter("@courriel", Nul(apres.Courriel) ?? DBNull.Value), new SqlParameter("@qualif", Nul(apres.Qualification) ?? DBNull.Value),
            new SqlParameter("@actif", apres.Actif), new SqlParameter("@id", n));
        var trace = new TraceGeste("reglage", $"le mandataire {apres.NomComplet}", id, "a modifie", avant.Valeurs, apres.Valeurs);
        // Le bandeau de la V2 en modification (OPE_SAISIE_MANDATAIRE.Designer.cs:170-172) : le mandataire est partage.
        return IssueGeste.Ecrit($"Mandataire {apres.NomComplet} enregistré : les modifications se reportent dans toutes ses affaires.", [id], trace);
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (CriteresConsultation.Entier(id) is not { } n || await LireAsync(geste, n, ct) is not { } m)
            return IssueGeste.Refuser($"Mandataire introuvable dans la base du greffe ({id}).");
        if (m.Actif && m.Cnbf.Length > 0) return IssueGeste.Refuser(string.Format(System.Globalization.CultureInfo.InvariantCulture, ActifAvecCnbf, m.NomComplet));
        var comptes = await LiaisonsMandataire.CompterAsync((sql, p) => geste.LireAsync(sql, ct, p), n, ct);
        if (LiaisonsMandataire.EnMots(comptes) is { Length: > 0 } liaisons)
            return IssueGeste.Refuser($"Suppression impossible : le mandataire {m.NomComplet} est utilisé ({liaisons}). RIG v2 ne supprime un mandataire " +
                                      "qu'après avoir repris ses liaisons sur un autre : passez par « Rapprocher des acteurs ».");
        await geste.EcrireAsync("DELETE FROM v2.MANDATAIRE WHERE MNDTR_ID_MNDTR = @id", ct, new SqlParameter("@id", n));
        var trace = new TraceGeste("reglage", $"le mandataire {m.NomComplet}", id, "a supprime", m.Valeurs, null);
        return IssueGeste.Ecrit($"Mandataire {m.NomComplet} supprimé.", [id], trace);
    }

    private static async Task<Mandataire?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT m.MNDTR_ID_MNDTR, m.MNDTR_CIVILITE, m.MNDTR_NOM, m.MNDTR_ABREGE, m.MNDTR_SIREN, m.MNDTR_ADRESSE_MAIL, m.MNDTR_QUALIF_PROFESSION,
                   m.MNDTR_ACTIF, m.MNDTR_NUMERO_CNBF, c.R_CIVILITE_ABREGE_EDIT
            FROM v2.MANDATAIRE AS m WITH (UPDLOCK) LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = m.MNDTR_CIVILITE
            WHERE m.MNDTR_ID_MNDTR = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Mandataire(id, FormeLigne.Texte(r, "MNDTR_CIVILITE"), FormeLigne.Texte(r, "MNDTR_NOM"), FormeLigne.Texte(r, "MNDTR_ABREGE"),
            FormeLigne.Texte(r, "MNDTR_SIREN"), FormeLigne.Texte(r, "MNDTR_ADRESSE_MAIL"), FormeLigne.Texte(r, "MNDTR_QUALIF_PROFESSION"),
            r.GetBoolean(r.GetOrdinal("MNDTR_ACTIF")), FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF"), FormeLigne.Texte(r, "R_CIVILITE_ABREGE_EDIT"));
    }

    private static object? Nul(string valeur) => valeur.Length == 0 ? null : valeur;

    /// <summary>RIG.TOOLS.RegEx.Email (D:\RigApplication\Source\Outils\DLL\RigTools\TOOLS\RegEx.cs:17-19), recopiee : une adresse
    /// simple, ou « Nom &lt;adresse&gt; ».</summary>
    [GeneratedRegex(@"^([a-zA-Z0-9]+(([\.\-_+][a-zA-Z0-9]+)+)?)@(([a-zA-Z0-9]+[\.\-_])+[a-zA-Z]+)$|^[^;<]*<([a-zA-Z0-9]+(([\.\-_+][a-zA-Z0-9]+)+)?)@(([a-zA-Z0-9]+[\.\-_])+[a-zA-Z]+)>$")]
    private static partial Regex Email();
}
