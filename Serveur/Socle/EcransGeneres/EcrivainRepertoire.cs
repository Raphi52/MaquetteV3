using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurRepertoire;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Répertoire » (PROC_MODIF_REPER) : ENREGISTRER un repertorie corrige dans sa fiche. Mode d'emploi : EcrivainEcran.cs ; la
/// fiche : LecteurRepertoire.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : OPE_EDITION_MODIF_REPER (PROC_MODIF_REPER\Edition\OPE_EDITION_MODIF_REPER.cs), DoVerifier (:66-79) puis
/// Valider (:208-307), en modification (un repertorie existant : _modeCrea faux, :385).
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. le repertorie existe (sinon « Répertorié introuvable ») ; seuls les champs saisissables de la section « Fiche » se
///      modifient -- l'adresse se lit seulement (voir plus bas) ;
///   2. obligatoires : abrege et civilite (MetierObligatoire, OPE_EDITION_MODIF_REPER.Designer.cs:304, :253) ;
///   3. la taille des colonnes (RIG_DEV : abrege 16, designation 512, adresse mail 70, civilite 7, mode de presentation 3,
///      edition jugement 5, forme juridique 10, organe 32, nationalite 3, profession 128, telephone et fax 22) ; et le format
///      des zones qui en ont un dans la V2 (toutes facultatives, aucun MetierObligatoire au Designer) :
///      - telephone et fax (ULT_TEXT_TELEPHONE, :17-18) : RegEx.TeléphoneFixeOuMobile (RigTools RegEx.cs:33), une erreur
///        bloquante de Ult_Text_Internal.DoVerifier (:342-362) -> « Numéro de téléphone invalide » ;
///      - N° Siren (ULT_TEXT_SIREN) : chiffres seuls a la saisie (MetierRegExSaisie « [0-9] », :84), neuf chiffres
///        (RegEx.Siren, RegEx.cs:27) et la cle de Luhn (Siren.IsValid, RigMetier Numerotation\Siren.cs:22-35 ; la zone garde
///        le focus tant qu'elle est fausse, :36-46) -> « Numéro de Siren invalide. » ; il s'enregistre sans blancs
///        (Siren.DeFormat, :60-64), comme RIG_DEV le garde ;
///      - date de naissance (Ult_Date) : une date ;
///   4. DoVerifier (:67-77) : un AUTRE repertorie du greffe porte deja l'abrege -> « Un répertorié avec cet abrégé existe déjà.
///      L'abrégé doit être unique. » (la V2 n'a pas d'autre controle : ni designation obligatoire, ni format de l'adresse
///      mail, contrairement aux sections anterieures de la fiche, que le « Detail OPE » corrige) ;
///   5. le mode de presentation choisi est dans la liste de la V2 : MODE_PRESENTATION, ses modes « utilisables en saisie »
///      (ULT_COMBO_MODE_PRESENTATION.cs:35-36 -> Outils.makeSqlStandard, SAISIE = R_MODEPRESEN_UTILISABLE_SAISIE) ; de meme
///      l'edition jugement (EDITION_JUGEMENT, ULT_COMBO_EDITION_JUGEMENT.cs:15-17), la forme juridique (FORME_JURIDIQUE_UTI,
///      ULT_COMBO_FORME_JURIDIQUE.cs:32-34) et la nationalite (NATIONALITE, ULT_COMBO_NATIONALITE.cs:15-17). La liste de la V2
///      ne se choisit qu'en ses valeurs (RigComboListBox.cs:593-596, sans saisie libre) et ne propose pas les valeurs non
///      utilisables en saisie (RigComboBoxObjectMoteurCache.cs:69) ; une valeur deja en base hors liste n'y est qu'un
///      avertissement (MetierValeurInexistanteErreur = false, Designer :217, :236, :245) : seule une valeur CHANGEE est controlee ;
///   6. le mode decide des zones affichees (GestionModePresentation, :327-370) : le client sans representant (:336-338), le
///      mandataire postulant avec representant (:340-346), le texte d'assistance avec assistance, alors OBLIGATOIRE (:348-354) ;
///      sans mode, aucune (:355-363). Une zone que la V2 masque ne se saisit pas : la V3 le dit au lieu de l'effacer en silence ;
///   7. le client se designe par son abrege (rec.Client, la fiche reprise du CLIENT de la V2 ; la V2 le choisit dans sa liste
///      ou sa recherche, :417-418, :448-464), le mandataire postulant par le sien (v2.MANDATAIRE, MNDTR_ABREGE : la liste
///      « Mandataire répertorié » d'OPE_MANDATAIRE). Introuvable, ou porte par deux fiches : refuse, rien n'est devine. Le
///      mandataire n'est pas obligatoire pour un repertorie (le controle d'OPE_MANDATAIRE.cs:437-441 ne vise qu'une partie).
///
/// CE QUI S'ECRIT, dans la transaction, comme Valider :
///   - v2.REPERTORIE : les colonnes modifiees ; et la remise en ordre du mode de presentation (:210-226), sur le mode qui sera
///     enregistre : mode a saisie de representant -> plus de client (PRTRP_ID_CLIEN) ; autre mode -> plus de mandataire
///     postulant (PRTRP_ID_MNDTR_POS) ; sans mode -> ni l'un ni l'autre ; texte d'assistance efface si le mode ne le demande
///     pas (R_MODEPRESEN_SAISIE_ASSISTANT, :225-226) ;
///   - v2.PARTIE : abrege ou civilite change et parties liees -> PRT_ABREGE des parties du repertorie = le nouvel abrege
///     (Partie.UpdatePartiesRepertorie, Partie.cs:1607-1615 ; :245-251) ;
///   - v2.CODE_AFFAIRE : abrege change -> R_CODEAFFAIR_ABREGE_DEMANDEUR passe de l'ancien au nouvel abrege
///     (CODE_AFFAIRE.UpdateAbregeDemandeur ; :255-261).
///   Trace : famille « reglage », « a modifie », avant et apres.
///
/// L'ADRESSE SE LIT SEULEMENT : la V2 ne la saisit pas dans la fiche. Sa zone (Ult_Adresse) ouvre la recherche d'adresse de
/// RIG, un ecran a part qui retrouve ou cree une ADRESSE_BASE partagee et rend son identifiant (Ult_Adresse.cs:283-301 :
/// LanceurOcxRecherche « Adresse », ou RigAdresse.Adresse.Saisir de l'ancien composant). La V3 n'a pas cet ecran.
///
/// NON REPRIS (nommes) : la demande de tracabilite MODIFREPER creee et cloturee d'un coup (GestionDemande, :314-318 ; non
/// facturee, PROC_MODIF_REPER.cs:11-12), remplacee par le journal des gestes ; l'ajout et la suppression d'un repertorie (le
/// bouton AJOUTER et la barre de l'OPE, :86-152), qu'une Fiche ne fait pas ; la civilite appliquee aux parties cochees
/// (OPE_EDITION_PARTIES.Valider, :50-86), une Fiche ne cochant pas les lignes d'une sous-liste. Toutes les zones que
/// LinkDonnees lie (:401-420) sont dans la fiche ; le portable (PRTRP_PORTABLE) n'en est pas, la V2 ne l'affiche pas.
/// </summary>
internal sealed class EcrivainRepertoire : IEcrivainEcran
{
    /// <summary>Le nom de l'ecran au journal des gestes (le titre du descripteur) : le lecteur le relit pour l'etat.</summary>
    internal const string NomEcran = "Repertoire";

    public string Procedure => "PROC_MODIF_REPER";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>Les champs ecrits tels quels : leur colonne de v2.REPERTORIE, sa taille dans RIG_DEV (0 : sans borne), et s'il
    /// est obligatoire.</summary>
    private static readonly (string Champ, string Colonne, int Taille, bool Obligatoire)[] Colonnes =
    [
        (ChampAbrege, "PRTRP_ABREGE", 16, true), (ChampDesignation, "PRTRP_DESIGNATION", 512, false),
        (ChampMail, "PRTRP_ADRESSE_MAIL", 70, false), (ChampCivilite, "PRTRP_CIVILITE", 7, true), (ChampActif, "PRTRP_ACTIF", 0, false),
        (ChampMode, "PRTRP_MODE_PRESENTATION", 3, false),
        (ChampEditionJugement, "PRTRP_EDITION_JUGEMENT", 5, false), (ChampFormeJuridique, "PRTRP_FORME_JURIDIQUE_UTI", 10, false),
        (ChampOrgane, "PRTRP_ORGANE", 32, false), (ChampSiren, "PRTRP_SIREN", 10, false), (ChampNaissance, "PRTRP_DATE_NAISSANCE", 0, false),
        (ChampNationalite, "PRTRP_NATIONALITE", 3, false), (ChampProfession, "PRTRP_PROFESSION", 128, false),
        (ChampTelephone, "PRTRP_TELEPHONE", 22, false), (ChampFax, "PRTRP_FAX", 22, false), (ChampQualite, "PRTRP_TEXTE_ESQUALITE", 0, false),
    ];

    /// <summary>Les zones a liste de la V2 : leur table, le prefixe de ses colonnes (Outils.makeSqlStandard : _VALEUR,
    /// _UTILISABLE_SAISIE) et la liste nommee dans le refus.</summary>
    private static readonly (string Champ, string Table, string Prefixe, string Liste)[] Listes =
    [
        (ChampEditionJugement, "v2.EDITION_JUGEMENT", "R_EDITIONJUG", "des éditions de jugement"),
        (ChampFormeJuridique, "v2.FORME_JURIDIQUE_UTI", "R_FORMEJURID", "des formes juridiques"),
        (ChampNationalite, "v2.NATIONALITE", "R_NATIONALIT", "des nationalités"),
    ];

    /// <summary>RegEx.TeléphoneFixeOuMobile de la V2 (RigTools\TOOLS\RegEx.cs:33), au caractere pres.</summary>
    private static readonly Regex Telephone = new(@"^((\+|00)33[\s\.]?|0)[1-9]([\s\.]?\d{2}){4}$", RegexOptions.CultureInvariant);

    /// <summary>Les champs que le mode de presentation montre ou masque (GestionModePresentation, :327-370).</summary>
    private static readonly string[] SelonLeMode = [ChampClient, ChampAssistance, ChampMandataire];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. Le repertorie, verrouille en ecriture jusqu'a la fin de la transaction ; les champs de la fiche.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || await LireRepertorieAsync(geste.Base, geste.Transaction, n, ct) is not { } rep)
            return IssueGeste.Refuser($"Répertorié introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        var saisis = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var champ = Colonnes.Select(c => c.Champ).Concat(SelonLeMode).FirstOrDefault(c => GesteEcran.Cle(SectionRepertorie, c) == cle);
            if (champ is null)
                return IssueGeste.Refuser(cle == GesteEcran.Cle(SectionRepertorie, ChampAdresse)
                    ? "L'adresse d'un répertorié se choisit dans la recherche d'adresse de RIG v2, que la V3 n'a pas encore : elle ne se modifie pas dans la fiche."
                    : $"« {cle} » ne se modifie pas dans la fiche d'un répertorié de RIG v2.");
            saisis[champ] = valeur.Trim();
        }

        // 2. et 3. Obligatoires, formats, tailles. Le Siren s'ecrit sans blancs (Siren.DeFormat), la forme que la base garde.
        if (saisis.TryGetValue(ChampSiren, out var siren))
        {
            if (!SirenValide(siren)) return IssueGeste.Refuser("Numéro de Siren invalide.");
            saisis[ChampSiren] = siren.Replace(" ", "", StringComparison.Ordinal);
        }
        foreach (var (champ, _, taille, obligatoire) in Colonnes.Where(c => saisis.ContainsKey(c.Champ)))
        {
            var v = saisis[champ];
            if (obligatoire && v.Length == 0) return IssueGeste.Refuser($"« {champ} » est obligatoire.");
            if (champ is ChampTelephone or ChampFax && v.Length > 0 && !Telephone.IsMatch(v))
                return IssueGeste.Refuser($"« {champ} » : numéro de téléphone invalide.");
            if (champ == ChampNaissance && v.Length > 0 && !DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return IssueGeste.Refuser($"« {champ} » : date illisible ({v}).");
            if (taille > 0 && v.Length > taille) return IssueGeste.Refuser($"« {champ} » : {taille} caractères au plus ({v.Length} saisis).");
        }

        var avant = rep.Valeurs;
        var changes = saisis.Where(s => avant[s.Key] != s.Value).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (changes.Count == 0) return IssueGeste.Refuser($"Aucun champ n'est modifié : le répertorié {avant[ChampAbrege]} garde ses valeurs.");

        // 4. DoVerifier : l'abrege est unique dans le greffe (Repertorie.GetRepertorieByAbrege, egalite SQL).
        var ancienAbrege = avant[ChampAbrege];
        var abrege = changes.GetValueOrDefault(ChampAbrege, ancienAbrege);
        await using (var r = await geste.LireAsync("SELECT COUNT(*) AS N FROM v2.REPERTORIE WITH (UPDLOCK, HOLDLOCK) WHERE PRTRP_ABREGE = @abrege AND PRTRP_ID_PRTRP <> @id",
                                                    ct, new SqlParameter("@abrege", abrege), new SqlParameter("@id", n)))
            if (await r.ReadAsync(ct) && Convert.ToInt32(r["N"], CultureInfo.InvariantCulture) > 0)
                return IssueGeste.Refuser("Un répertorié avec cet abrégé existe déjà. L'abrégé doit être unique.");

        // 5. Le mode qui sera enregistre, et ce qu'il montre (MODE_PRESENTATION.GetMODE_PRESENTATION ; un mode absent de la
        //    table vaut « sans mode », :212-223).
        var mode = changes.GetValueOrDefault(ChampMode, avant[ChampMode]);
        (bool Representant, bool Assistant, bool Utilisable)? lu = null;
        if (mode.Length > 0)
            await using (var r = await geste.LireAsync("""
                SELECT R_MODEPRESEN_SAISIE_REPRESENTANT, R_MODEPRESEN_SAISIE_ASSISTANT, R_MODEPRESEN_UTILISABLE_SAISIE
                FROM v2.MODE_PRESENTATION WHERE R_MODEPRESEN_VALEUR = @mode
                """, ct, new SqlParameter("@mode", mode)))
                if (await r.ReadAsync(ct))
                    lu = (r["R_MODEPRESEN_SAISIE_REPRESENTANT"] is true, r["R_MODEPRESEN_SAISIE_ASSISTANT"] is true, r["R_MODEPRESEN_UTILISABLE_SAISIE"] is true);
        if (changes.ContainsKey(ChampMode) && mode.Length > 0 && lu is not { Utilisable: true })
            return IssueGeste.Refuser($"Le mode de présentation « {mode} » n'est pas dans la liste des modes utilisables du greffe.");
        foreach (var (champ, table, prefixe, liste) in Listes)
            if (changes.GetValueOrDefault(champ) is { Length: > 0 } valeur && !await UtilisableEnSaisieAsync(geste, table, prefixe, valeur, ct))
                return IssueGeste.Refuser($"« {champ} » : « {valeur} » n'est pas dans la liste {liste} utilisables du greffe.");

        // 6. Les zones que le mode montre ; une zone masquee ne se saisit pas, le texte d'assistance montre est obligatoire.
        bool client = lu is { Representant: false }, mandataire = lu is { Representant: true }, assistance = lu is { Assistant: true };
        if (changes.GetValueOrDefault(ChampClient) is { Length: > 0 } && !client)
            return IssueGeste.Refuser("Le client ne se saisit pas avec ce mode de présentation : RIG v2 ne l'affiche que pour un mode sans représentant.");
        if (changes.GetValueOrDefault(ChampMandataire) is { Length: > 0 } && !mandataire)
            return IssueGeste.Refuser("Le mandataire postulant ne se saisit pas avec ce mode de présentation : RIG v2 ne l'affiche que pour un mode avec représentant.");
        if (changes.GetValueOrDefault(ChampAssistance) is { Length: > 0 } && !assistance)
            return IssueGeste.Refuser("Le texte d'assistance ne se saisit pas avec ce mode de présentation : RIG v2 ne l'affiche que pour un mode avec assistance.");
        if (assistance && changes.GetValueOrDefault(ChampAssistance, avant[ChampAssistance]).Length == 0)
            return IssueGeste.Refuser($"« {ChampAssistance} » est obligatoire.");

        // 7. Le client et le mandataire postulant, par leur abrege.
        var parametres = new List<SqlParameter> { new("@id", n) };
        var affectations = new List<string>();
        if (!client) affectations.Add("PRTRP_ID_CLIEN = NULL");
        else if (changes.TryGetValue(ChampClient, out var abregeClient))
        {
            if (await IdentifiantAsync(geste, "SELECT TOP (2) Id AS Id FROM rec.Client WHERE Abrege = @abrege ORDER BY Id", abregeClient, ct) is not { } idClient)
                return IssueGeste.Refuser($"Aucun client unique du greffe n'a l'abrégé « {abregeClient} ».");
            affectations.Add("PRTRP_ID_CLIEN = @client");
            parametres.Add(new SqlParameter("@client", idClient));
        }
        if (!mandataire) affectations.Add("PRTRP_ID_MNDTR_POS = NULL");
        else if (changes.TryGetValue(ChampMandataire, out var abregeMandataire))
        {
            if (await IdentifiantAsync(geste, "SELECT TOP (2) MNDTR_ID_MNDTR AS Id FROM v2.MANDATAIRE WHERE MNDTR_ABREGE = @abrege ORDER BY MNDTR_ID_MNDTR",
                                       abregeMandataire, ct) is not { } idMandataire)
                return IssueGeste.Refuser($"Aucun mandataire unique du greffe n'a l'abrégé « {abregeMandataire} ».");
            affectations.Add("PRTRP_ID_MNDTR_POS = @mandataire");
            parametres.Add(new SqlParameter("@mandataire", idMandataire));
        }
        if (!assistance) affectations.Add("PRTRP_TEXTE_ASSISTANCE = NULL");
        else if (changes.TryGetValue(ChampAssistance, out var texte))
        {
            affectations.Add("PRTRP_TEXTE_ASSISTANCE = @assistance");
            parametres.Add(new SqlParameter("@assistance", texte));
        }

        // Valider : le repertorie (:236), ses colonnes modifiees.
        foreach (var (champ, colonne, _, _) in Colonnes.Where(c => changes.ContainsKey(c.Champ)))
        {
            affectations.Add($"{colonne} = @{colonne}");
            var v = changes[champ];
            object valeur = champ == ChampActif ? v == "oui" : v.Length == 0 ? DBNull.Value
                : champ == ChampNaissance ? DateTime.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture) : v;
            parametres.Add(new SqlParameter($"@{colonne}", valeur));
        }
        var touchees = await geste.EcrireAsync($"UPDATE v2.REPERTORIE SET {string.Join(", ", affectations)} WHERE PRTRP_ID_PRTRP = @id", ct, [.. parametres]);
        if (touchees != 1) throw new InvalidOperationException($"Repertorie {n} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");

        // Les parties liees (:245-251) et les codes affaire (:255-261).
        if (changes.ContainsKey(ChampAbrege) || changes.ContainsKey(ChampCivilite))
            await geste.EcrireAsync("UPDATE v2.PARTIE SET PRT_ABREGE = @abrege WHERE PRT_ID_PRTRP = @id", ct,
                                    new SqlParameter("@abrege", abrege), new SqlParameter("@id", n));
        if (changes.ContainsKey(ChampAbrege))
            await geste.EcrireAsync("UPDATE v2.CODE_AFFAIRE SET R_CODEAFFAIR_ABREGE_DEMANDEUR = @nouvel WHERE R_CODEAFFAIR_ABREGE_DEMANDEUR = @ancien", ct,
                                    new SqlParameter("@nouvel", abrege), new SqlParameter("@ancien", ancienAbrege));

        var modifies = ChampsFiche.Where(changes.ContainsKey).ToList();
        return IssueGeste.Ecrit($"Répertorié {abrege} enregistré dans la base du greffe : {string.Join(", ", modifies.Select(c => $"« {c} »"))}.",
            [id], new TraceGeste("reglage", $"le répertorié {abrege}", id, "a modifie", modifies.ToDictionary(c => c, c => avant[c]), changes));
    }

    /// <summary>La valeur <paramref name="valeur"/> est-elle dans la liste de la V2 de <paramref name="table"/>, parmi ses valeurs
    /// utilisables en saisie (Outils.makeSqlStandard, RigComboBoxObjectMoteurCache.cs:69) ? Lue aussi par
    /// EcrivainBeneficiairesDossier (la nationalite). <paramref name="table"/> et <paramref name="prefixe"/> sont des constantes
    /// du code, jamais une saisie.</summary>
    internal static async Task<bool> UtilisableEnSaisieAsync(GesteEnCours geste, string table, string prefixe, string valeur, CancellationToken ct)
    {
        await using var r = await geste.LireAsync($"SELECT COUNT(*) AS N FROM {table} WHERE {prefixe}_VALEUR = @valeur AND {prefixe}_UTILISABLE_SAISIE = 1",
                                                  ct, new SqlParameter("@valeur", valeur));
        return await r.ReadAsync(ct) && Convert.ToInt32(r["N"], CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>Le N° Siren saisi est-il valide pour la V2 ? Vide : oui (Siren.IsValid, :23). Sinon des chiffres seuls -- les
    /// blancs de l'affichage formate (Siren.Format) mis a part --, neuf (RegEx.Siren), et la cle de Luhn de Siren.IsValid
    /// (:24-34). (ValiditeSaisie.LuhnValide fait le meme calcul, mais dans le projet de l'application, que le serveur ne
    /// reference pas.)</summary>
    internal static bool SirenValide(string saisi)
    {
        var chiffres = saisi.Replace(" ", "", StringComparison.Ordinal);
        if (chiffres.Length == 0) return true;
        if (chiffres.Length != 9 || !chiffres.All(char.IsAsciiDigit)) return false;
        var somme = 0;
        for (var i = 0; i < 9; i++)
        {
            var chiffre = chiffres[8 - i] - '0';
            somme += i % 2 == 1 ? (chiffre * 2 > 9 ? chiffre * 2 - 9 : chiffre * 2) : chiffre;
        }
        return somme % 10 == 0;
    }

    /// <summary>L'identifiant de la SEULE ligne que <paramref name="sql"/> rend pour cet abrege (une colonne « Id ») ; null si
    /// aucune ou plusieurs ; DBNull pour un abrege vide (la zone videe).</summary>
    private static async Task<object?> IdentifiantAsync(GesteEnCours geste, string sql, string abrege, CancellationToken ct)
    {
        if (abrege.Length == 0) return DBNull.Value;
        var ids = new List<int>();
        await using (var r = await geste.LireAsync(sql, ct, new SqlParameter("@abrege", abrege)))
            while (await r.ReadAsync(ct)) ids.Add(Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture));
        return ids is [var seul] ? seul : null;
    }
}
