using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Destinataires institutionnels » (PROC_GDEST_INST) : AJOUTER un destinataire, le MODIFIER, le SUPPRIMER, dans la grille
/// (forme Referentiel). Mode d'emploi : EcrivainEcran.cs ; les lignes : LecteurDestinatairesInstitutionnels.cs (Id = le code,
/// cellules = Nom, Code, Type, Adresse, Adresse courriel). Plan « RIG v3 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\Judiciaire\Exe\PROC_GDEST_INST.
///   - AJOUTER et MODIFIER : frmAddDestInst._Valider (frmAddDestInst.cs:233-316). Les champs saisis sont « trimes » (l. 238-241),
///     l'adresse ligne a ligne (l. 242-249). Puis, dans l'ordre, au mot pres :
///       1. code, nom, type et adresse obligatoires (l. 253-257) : « Insertion d'un destinataire institutionnel impossible.\n Le
///          code, le nom, l'adresse et le type sont obligatoires. » (« Modification ... » en modification) ;
///       2. chaque adresse courriel, coupee sur « ; », suit RIG.TOOLS.RegEx.Email (TesterEmails, l. 319-340 ; la regle :
///          EcrivainMandataires.Email) : « L'adresse mail suivante est incorrecte :\n(...) », ou « Les adresses mails suivantes
///          sont incorrectes :\n(a;b) » s'il y en a plusieurs, apres « Insertion|Modification d'un destinataire institutionnel
///          impossible.\n » (l. 262-267) ;
///       3. en ajout, un code deja pris (l. 270-275) : « Insertion d'un destinataire institutionnel impossible.\n Le code saisi
///          existe déjà. » ;
///     Ecriture (l. 294-308) : la ligne de DESTINATAIRE_INSTITUTIONNEL (un ajout est ACTIF, ckbActif coche a l'ouverture, l. 67 ;
///     les champs facultatifs vides sont NULL, l. 278-282), puis une ligne de DESTINATAIRE_INSTITUTIONNEL_HISTO : « Insertion », ou
///     « Modification : » suivi des changements (constructionTextChangements, l. 344-427, au format de la V2), seulement s'il y en a.
///     L'auteur de l'historique : AmiEnvironment.UserName, le compte Windows (l. 298, 305).
///   - SUPPRIMER : le bouton SUPPRIMER de la liste (frmListeDestInst.cs:228-233) : DELETE de l'historique du code, puis du
///     destinataire. La V2 ne demande rien et ne controle rien.
/// CONTROLES SANS MESSAGE V2 (nommes) : le TYPE se choisit dans une liste deroulante, les types « INST_* » de ENTITE
///   (frmAddDestInst.cs:52) -- un type saisi hors de cette liste est refuse ; le CODE ne se modifie pas (txtCode grise en
///   modification, l. 72) ; la longueur des champs est celle des colonnes de la V2 (BASE_GREFFE_DESTINATAIRE_INSTITUTIONNEL :
///   code 10, nom 100, adresse 150, type 10, courriel 1 000), que les zones de saisie de la V2 bornent ; une modification qui ne
///   change rien n'ecrit rien et le dit (la V2 enregistre sans rien changer) ; un destinataire introuvable est refuse.
/// DIFFERENCE ASSUMEE (nommee) : un TYPE change, la V2 demande « Attention vous avez modifié le type de votre destinataire ce qui
///   peut avoir des conséquences sur les éditions paramétrées. Etes vous sûr de vouloir continuer ? » (l. 286-290). L'ecran V3 n'a
///   pas de question en cours de geste : la modification est ecrite et le message le rappelle.
/// NON REPRIS (nomme) : la civilite developpee, la formule de politesse, le coffre-fort, l'activation et le nom du JAL se saisissent
///   dans le formulaire de la V2, pas dans la grille (ses colonnes) : ils restent tels quels a la modification, vides a l'ajout ; la
///   creation d'un type (frmAddType) n'a pas de geste.
/// Traces : famille « reglage », le destinataire en mots, son code ; avant / apres les colonnes de la grille.
/// </summary>
internal sealed class EcrivainDestinatairesInstitutionnels : IEcrivainEcran
{
    public string Procedure => "PROC_GDEST_INST";
    public string Ecran => "Destinataires institutionnels";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Modifier, TypeGeste.Supprimer];

    /// <summary>Les colonnes de la grille (descripteur PROC_GDEST_INST), au mot pres.</summary>
    internal const string ColNom = "Nom", ColCode = "Code", ColType = "Type", ColAdresse = "Adresse", ColCourriel = "Adresse courriel";

    /// <summary>Le debut des messages de refus de la V2 (frmAddDestInst.cs:255, 257, 264, 266, 274).</summary>
    /// Un retour a la ligne de la V2 (« \n », Environment.NewLine) devient une espace : l'annonce de l'ecran tient sur une ligne.
    // fix-ok: mesure (27/11/2026) -- le banc lit « message: » sur UNE ligne (tests/EcransGeneresBaseCheck/Program.cs:1028) et le compare
    // a l'identique (:657) : un « \n » de la V2 dans le message ne pouvait pas etre controle ; d'ou l'espace, pas une rustine.
    internal const string InsertionImpossible = "Insertion d'un destinataire institutionnel impossible.";
    internal const string ModificationImpossible = "Modification d'un destinataire institutionnel impossible.";
    internal const string Obligatoires = " Le code, le nom, l'adresse et le type sont obligatoires.";
    internal const string CodeExiste = " Le code saisi existe déjà.";

    /// <summary>Un destinataire tel que la grille le montre.</summary>
    private sealed record Destinataire(string Code, string Nom, string Type, string Adresse, string Courriel)
    {
        public Dictionary<string, string> Colonnes() => new()
        {
            [ColNom] = Nom, [ColCode] = Code, [ColType] = Type, [ColAdresse] = Adresse, [ColCourriel] = Courriel,
        };
    }

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Ajouter => await AjouterAsync(geste, ct),
        TypeGeste.Modifier => await ModifierAsync(geste, ct),
        TypeGeste.Supprimer => await SupprimerAsync(geste, ct),
        _ => IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Type}."),
    };

    private static async Task<IssueGeste> AjouterAsync(GesteEnCours geste, CancellationToken ct)
    {
        string Saisi(string colonne) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : "";
        var d = new Destinataire(Saisi(ColCode), Saisi(ColNom), Saisi(ColType), Adresse(Saisi(ColAdresse)), Saisi(ColCourriel));
        if (await ControlerAsync(geste, d, InsertionImpossible, ct) is { } refus) return IssueGeste.Refuser(refus);
        // 3. Le code deja pris (l. 270-275).
        if (await LireAsync(geste, d.Code, ct) is not null) return IssueGeste.Refuser(InsertionImpossible + CodeExiste);

        await geste.EcrireAsync("""
            INSERT INTO v2.DESTINATAIRE_INSTITUTIONNEL (DTINS_CODE, DTINS_LIBELLE, DTINS_BLOC_ADRESSE, DTINS_TYPE, DTINS_ADRESSE_MAIL, DTINS_ACTIF)
            VALUES (@code, @nom, @adresse, @type, @courriel, 1)
            """, ct, new SqlParameter("@code", d.Code), new SqlParameter("@nom", d.Nom), new SqlParameter("@adresse", d.Adresse),
            new SqlParameter("@type", d.Type), new SqlParameter("@courriel", d.Courriel.Length > 0 ? d.Courriel : DBNull.Value));
        await HistoriqueAsync(geste, d.Code, "Insertion", ct);
        return IssueGeste.Ecrit($"Destinataire institutionnel {d.Code} « {d.Nom} » ajouté.", [d.Code],
            new TraceGeste("reglage", $"le destinataire institutionnel {d.Code}", d.Code, "a cree", null, d.Colonnes()));
    }

    private static async Task<IssueGeste> ModifierAsync(GesteEnCours geste, CancellationToken ct)
    {
        var code = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await LireAsync(geste, code, ct) is not { } avant) return IssueGeste.Refuser(Introuvable(code));
        string Saisi(string colonne, string base_) => geste.Demande.Valeurs.TryGetValue(colonne, out var v) ? v.Trim() : base_;
        // Le code ne se modifie pas (txtCode grise en modification, l. 72).
        if (!string.Equals(Saisi(ColCode, avant.Code), avant.Code, StringComparison.Ordinal))
            return IssueGeste.Refuser($"Le code d'un destinataire institutionnel ne se modifie pas ({avant.Code}) : RIG v2 le grise en modification.");
        var apres = avant with
        {
            Nom = Saisi(ColNom, avant.Nom), Type = Saisi(ColType, avant.Type), Adresse = Adresse(Saisi(ColAdresse, avant.Adresse)),
            Courriel = Saisi(ColCourriel, avant.Courriel),
        };
        if (await ControlerAsync(geste, apres, ModificationImpossible, ct) is { } refus) return IssueGeste.Refuser(refus);
        var changements = Changements(avant, apres);
        if (changements.Length == 0) return IssueGeste.Refuser($"Aucune modification du destinataire institutionnel {avant.Code} : rien n'est enregistré.");

        await geste.EcrireAsync("""
            UPDATE v2.DESTINATAIRE_INSTITUTIONNEL
            SET DTINS_LIBELLE = @nom, DTINS_BLOC_ADRESSE = @adresse, DTINS_TYPE = @type, DTINS_ADRESSE_MAIL = @courriel
            WHERE DTINS_CODE = @code
            """, ct, new SqlParameter("@nom", apres.Nom), new SqlParameter("@adresse", apres.Adresse), new SqlParameter("@type", apres.Type),
            new SqlParameter("@courriel", apres.Courriel.Length > 0 ? apres.Courriel : DBNull.Value), new SqlParameter("@code", avant.Code));
        await HistoriqueAsync(geste, avant.Code, "Modification : " + changements, ct);
        var message = $"Destinataire institutionnel {avant.Code} modifié.";
        // l. 286-290 : la V2 demande confirmation d'un type change ; la V3 le rappelle (en tete du fichier).
        if (apres.Type != avant.Type)
            message += " Attention : le type a changé, ce qui peut avoir des conséquences sur les éditions paramétrées.";
        return IssueGeste.Ecrit(message, [avant.Code],
            new TraceGeste("reglage", $"le destinataire institutionnel {avant.Code}", avant.Code, "a modifie", avant.Colonnes(), apres.Colonnes()));
    }

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var code = geste.Demande.Ids.FirstOrDefault() ?? "";
        if (await LireAsync(geste, code, ct) is not { } d) return IssueGeste.Refuser(Introuvable(code));
        // frmListeDestInst.cs:230-231 : l'historique du code, puis le destinataire.
        if (await HistoriquePresentAsync(geste, ct))
            await geste.EcrireAsync("DELETE FROM v2.DESTINATAIRE_INSTITUTIONNEL_HISTO WHERE DIHIS_CODE = @code", ct, new SqlParameter("@code", d.Code));
        await geste.EcrireAsync("DELETE FROM v2.DESTINATAIRE_INSTITUTIONNEL WHERE DTINS_CODE = @code", ct, new SqlParameter("@code", d.Code));
        return IssueGeste.Ecrit($"Destinataire institutionnel {d.Code} « {d.Nom} » supprimé, avec son historique.", [d.Code],
            new TraceGeste("reglage", $"le destinataire institutionnel {d.Code}", d.Code, "a supprime", d.Colonnes(), null));
    }

    private static string Introuvable(string code) => $"Destinataire institutionnel introuvable dans la base du greffe ({code}).";

    /// <summary>Les controles de frmAddDestInst._Valider (en tete du fichier), dans l'ordre ; null si tout passe.</summary>
    private static async Task<string?> ControlerAsync(GesteEnCours geste, Destinataire d, string impossible, CancellationToken ct)
    {
        // 1. Les obligatoires (l. 253).
        if (d.Code.Length == 0 || d.Nom.Length == 0 || d.Type.Length == 0 || d.Adresse.Length == 0) return impossible + Obligatoires;
        // 2. Les adresses courriel (TesterEmails, l. 319-340).
        if (Courriels(d.Courriel) is { } erreur) return impossible + " " + erreur;
        // Les longueurs des colonnes de la V2 et le type de la liste deroulante (nommes en tete du fichier).
        foreach (var (libelle, valeur, longueur) in new[]
                 { (ColCode, d.Code, 10), (ColNom, d.Nom, 100), (ColAdresse, d.Adresse, 150), (ColType, d.Type, 10), (ColCourriel, d.Courriel, 1000) })
            if (valeur.Length > longueur)
                return $"« {libelle} » : {longueur.ToString(CultureInfo.InvariantCulture)} caractères au plus dans RIG v2 ({valeur.Length.ToString(CultureInfo.InvariantCulture)} saisis).";
        if (!await TypeProposeAsync(geste, d.Type, ct))
            return $"Le type « {d.Type} » n'est pas proposé : RIG v2 ne propose que les types de destinataire institutionnel (INST_*) de la base.";
        return null;
    }

    /// <summary>TesterEmails (frmAddDestInst.cs:319-340), au mot pres : les adresses coupees sur « ; », celles qui ne suivent pas la
    /// regle jointes par « ; » ; null si toutes la suivent (ou rien de saisi).</summary>
    internal static string? Courriels(string saisi)
    {
        if (saisi.Length == 0) return null;
        var fausses = saisi.Split(';').Where(m => !EcrivainMandataires.Email().IsMatch(m)).ToList();
        if (fausses.Count == 0) return null;
        return (fausses.Count > 1 ? "Les adresses mails suivantes sont incorrectes :" : "L'adresse mail suivante est incorrecte :")
               + " (" + string.Join(";", fausses) + ")";
    }

    /// <summary>L'adresse « trimee » ligne a ligne, lignes vides retirees (frmAddDestInst.cs:242-249).</summary>
    internal static string Adresse(string saisie)
        => string.Join(Environment.NewLine, saisie.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    /// <summary>constructionTextChangements (frmAddDestInst.cs:344-427), pour les colonnes de la grille : nom, adresse, type,
    /// courriel ; une ligne par changement.</summary>
    private static string Changements(Destinataire avant, Destinataire apres)
    {
        var lignes = new List<string>();
        void Changement(string avantV, string apresV, string suppression, string saisie, string modifie, string accord)
        {
            if (avantV == apresV) return;
            lignes.Add(apresV.Length == 0 ? suppression + avantV
                     : avantV.Length == 0 ? saisie + apresV
                     : $"{modifie}{avantV} {accord} en {apresV}");
        }
        Changement(avant.Nom, apres.Nom, "Suppression du nom :", "Saisie du nom :", "Nom :", "modifié");
        Changement(avant.Adresse, apres.Adresse, "Suppression de l'adresse :", "Saisie de l'adresse :", "Adresse :", "modifiée");
        Changement(avant.Type, apres.Type, "Suppression du type :", "Saisie du type :", "Type :", "modifié");
        Changement(avant.Courriel, apres.Courriel, "Suppression de l'adresse mail :", "Saisie de l'adresse mail :", "Adresse mail :", "modifiée");
        return string.Join(Environment.NewLine, lignes);
    }

    private static async Task<Destinataire?> LireAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        if (code.Length == 0) return null;
        await using var r = await geste.LireAsync("""
            SELECT DTINS_CODE, DTINS_LIBELLE, DTINS_TYPE, DTINS_BLOC_ADRESSE, DTINS_ADRESSE_MAIL
            FROM v2.DESTINATAIRE_INSTITUTIONNEL WITH (UPDLOCK) WHERE DTINS_CODE = @code
            """, ct, new SqlParameter("@code", code));
        return await r.ReadAsync(ct)
            ? new Destinataire(FormeLigne.Texte(r, "DTINS_CODE"), FormeLigne.Texte(r, "DTINS_LIBELLE"), FormeLigne.Texte(r, "DTINS_TYPE"),
                               FormeLigne.Texte(r, "DTINS_BLOC_ADRESSE"), FormeLigne.Texte(r, "DTINS_ADRESSE_MAIL"))
            : null;
    }

    /// <summary>Le type est dans la liste deroulante de la V2 : une ENTITE « INST_&lt;type&gt; » (frmAddDestInst.cs:52).</summary>
    private static async Task<bool> TypeProposeAsync(GesteEnCours geste, string type, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT 1 FROM v2.ENTITE WHERE ENTT_CODE_ENTITE = 'INST_' + @type", ct, new SqlParameter("@type", type));
        return await r.ReadAsync(ct);
    }

    private static async Task<bool> HistoriquePresentAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT OBJECT_ID(N'v2.DESTINATAIRE_INSTITUTIONNEL_HISTO')", ct);
        return await r.ReadAsync(ct) && !r.IsDBNull(0);
    }

    /// <summary>La ligne d'historique de la V2 (DestinataireInstitutionnelHisto.CreateDestinataireInstitutionnelHisto, l. 40-46) :
    /// code, compte Windows, date, action. Une base sans la table (non recopiee) n'en a pas : le journal des gestes garde la trace.</summary>
    private static async Task HistoriqueAsync(GesteEnCours geste, string code, string action, CancellationToken ct)
    {
        if (!await HistoriquePresentAsync(geste, ct)) return;
        var login = geste.Agent.CompteWindows;
        await geste.InsererAsync("v2.DESTINATAIRE_INSTITUTIONNEL_HISTO", "DIHIS_ID_DIHIS", new Dictionary<string, object?>
        {
            ["DIHIS_CODE"] = code, ["DIHIS_LOGIN"] = login.Length > 50 ? login[..50] : login, ["DIHIS_DATE"] = geste.Maintenant,
            ["DIHIS_ACTION"] = action,
        }, ct);
    }
}
