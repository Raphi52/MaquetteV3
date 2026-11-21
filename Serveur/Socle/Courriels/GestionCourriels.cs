using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>Un geste de la gestion des modeles refuse : le statut de la reponse et le motif montre a l'agent.</summary>
public sealed class RefusGestionCourriels(int statut, string motif) : Exception(motif)
{
    public int Statut { get; } = statut;
}

/// <summary>Un modele tel qu'il sera ecrit, une fois sa saisie controlee et mise en forme (<see cref="ReglesCourriels.ControlerModele"/>).</summary>
public sealed record ModeleAEcrire(
    string Type, string? LibelleEcran, string Sujet, string Corps, bool CorpsHtml, string Expediteur, string? Reponse, string? Copie,
    string? DestinataireDefaut, bool Lot1, bool Lot2, bool Inscription, bool Comptabilite, bool EnvoiAutomatique, bool ReserveAmitel,
    bool SignatureActive, string? Signature, int[] ListesParDefaut);

/// <summary>Une liste telle qu'elle sera ecrite (<see cref="ReglesCourriels.ControlerListe"/>) : les adresses en « a;b;c ».</summary>
public sealed record ListeAEcrire(string Code, string Libelle, string Adresses, bool Rcs, bool Judiciaire, bool Publique);

/// <summary>Ce que les regles du texte d'un modele demandent de savoir de l'habillage du greffe.</summary>
/// <param name="Regle">Le reglage SEND_EMAIL_TEMPLATE_PATH est rempli (OPE_EDIT_EMAILMODEL._TemplateActif, :415-417).</param>
/// <param name="Present">Le greffe a un habillage, en base ou en fichier (MailTemplateStore.LireAvecRepliFichier non vide, :103-104).</param>
public sealed record HabillageDuGreffe(bool Regle, bool Present);

/// <summary>
/// LES REGLES DE SAISIE de l'ecran V2 « Modele de mail » (PROC_GESTEMAIL), relues dans son code et recopiees :
///  - le nom d'un modele et le code d'une liste sont uniques et ne changent plus une fois crees (DoVerifier,
///    OPE_EDIT_EMAILMODEL.cs:86-90 et :251 ; OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:59-63 et :191) -- l'unicite se juge en base, dans
///    la transaction (<see cref="GestionCourriels"/>) ;
///  - un modele HTML sous un habillage regle porte $PieceJointe$ ; $LOGO$ est interdit quand le greffe a un habillage
///    (OPE_EDIT_EMAILMODEL.cs:91-110) ; le texte devient HTML s'il porte $LOGO$ (EmailModel.Sauve, EmailModel.cs:307-308) ;
///  - l'envoi automatique n'existe que pour un modele RCS : sans RCS il est remis a faux (_SetVisibilityChkEnvoiAuto, :271-279) ;
///  - une adresse est une adresse de courriel, nue ou nommee, ou -- pour un modele -- une boite du greffe en etiquette
///    (« Greffe &lt;[ADRESSE_MAIL_RCS]&gt; », OPE_EDIT_EMAILMODEL_ONE_CHOICE.cs:56-73) ; les expressions sont celles de
///    RIG.TOOLS.RegEx (RigTools\TOOLS\RegEx.cs:16-23), une liste se controle comme ULT_MULTITEXT_MULTIMAIL (« Courriel (x) invalide »).
/// Chaque refus est une phrase pour l'agent ; toutes sont rendues ensemble, comme la liste d'erreurs de la V2.
/// </summary>
public static partial class ReglesCourriels
{
    // RIG.TOOLS.RegEx._EmailSimple et _EmailNomme, tels quels.
    private const string EmailSimple = @"([a-zA-Z0-9]+(([\.\-_+][a-zA-Z0-9]+)+)?)@(([a-zA-Z0-9]+[\.\-_])+[a-zA-Z]+)";
    private const string EmailNomme = @"[^;<]*<" + EmailSimple + ">";
    // RIG.TOOLS.RegEx._EmailTableInfogreffe et _EmailTableInfogreffeNomme ; ancres des deux cotes (la V2 n'ancrait chaque
    // alternative que d'un cote : « [ADRESSE_MAIL_RCS]xyz » passait) et au moins une lettre entre les crochets.
    private const string Etiquette = @"\[([a-zA-Z_]+)\]";
    private const string EtiquetteNommee = @"[^;<]*<" + Etiquette + ">";

    [GeneratedRegex("^" + EmailSimple + "$|^" + EmailNomme + "$")] private static partial Regex Email();
    [GeneratedRegex("^(" + Etiquette + "|" + EtiquetteNommee + ")$")] private static partial Regex BoiteDuGreffe();
    [GeneratedRegex(@"\[PWD_", RegexOptions.IgnoreCase)] private static partial Regex EtiquetteSecrete();

    /// <summary>
    /// Controle et met en forme un modele saisi. <paramref name="corpsHtmlAvant"/> : le modele en base est deja HTML (faux a la
    /// creation, la V2 creant un modele texte). Rend le modele a ecrire, ou null et les refus.
    /// </summary>
    public static (ModeleAEcrire? Modele, List<string> Erreurs) ControlerModele(SaisieModele s, bool corpsHtmlAvant, HabillageDuGreffe habillage)
    {
        var erreurs = new List<string>();
        var type = Obligatoire(s.Type, "Le nom du modèle", 40, erreurs);
        // Libelle ecran et sujet obligatoires comme la V2 : MetierObligatoire (OPE_EDIT_EMAILMODEL.Designer.cs:191 et :103), refuse a
        // la validation par Ult.moteur.cs:516. Un modele repris vide (RIG_DEV : DIVJUD sans sujet) se complete a son prochain
        // enregistrement, comme dans la V2.
        // fix-ok: Facultatif() laissait passer sujet et libelle vides (tests/SocleCourrielsCheck : 201/201 avant, 400/400 apres).
        var libelle = Obligatoire(s.LibelleEcran, "Le libellé écran", 100, erreurs);
        var sujet = Obligatoire(s.Sujet, "Le sujet", 200, erreurs);
        // Le texte garde ses blancs (la V2 l'enregistre tel que saisi) ; il ne peut pas etre vide (EMAILMOD_CORPS NOT NULL).
        var corps = s.Corps ?? "";
        if (corps.Trim().Length == 0) erreurs.Add("Le texte du modèle est obligatoire.");
        else if (corps.Length > 4000) erreurs.Add($"Le texte du modèle tient en 4000 caractères ({corps.Length} saisis).");
        var expediteur = AdressesDuModele(s.Expediteur, "Expéditeur", erreurs);
        if (expediteur is null) erreurs.Add("L'expéditeur est obligatoire : une boîte du greffe ([ADRESSE_MAIL_…]) ou une adresse.");
        else if (expediteur.Contains(';')) erreurs.Add("L'expéditeur est une seule adresse.");
        var reponse = AdressesDuModele(s.Reponse, "Répondre à", erreurs);
        var copie = AdressesDuModele(s.Copie, "Copie cachée", erreurs);
        var destinataire = AdressesDuModele(s.DestinataireDefaut, "Destinataire par défaut", erreurs);
        var signature = Facultatif(s.Signature, "La signature", 2000, erreurs);

        if (corpsHtmlAvant && habillage.Regle && corps.IndexOf("$PieceJointe$", StringComparison.OrdinalIgnoreCase) < 0)
            erreurs.Add("Le tag $PieceJointe$ est obligatoire dans le corps d'un modèle HTML.");
        if (habillage.Present && corps.IndexOf("$LOGO$", StringComparison.OrdinalIgnoreCase) >= 0)
            erreurs.Add("Le tag $LOGO$ est interdit quand un template est actif (le logo est positionné dans le template).");

        if (erreurs.Count > 0) return (null, erreurs);
        return (new ModeleAEcrire(type!, libelle, sujet!, corps, corpsHtmlAvant || corps.Contains("$LOGO$", StringComparison.Ordinal),
            expediteur!, reponse, copie, destinataire, s.Lot1, s.Lot2, s.Inscription, s.Comptabilite, s.Lot1 && s.EnvoiAutomatique,
            s.ReserveAmitel, s.SignatureActive, signature, [.. (s.ListesParDefaut ?? []).Distinct()]), erreurs);
    }

    /// <summary>Controle et met en forme une liste saisie : rend la liste a ecrire, ou null et les refus.</summary>
    public static (ListeAEcrire? Liste, List<string> Erreurs) ControlerListe(SaisieListe s)
    {
        var erreurs = new List<string>();
        var code = Obligatoire(s.Code, "Le code de la liste", 35, erreurs);
        var libelle = Obligatoire(s.Libelle, "Le libellé de la liste", 200, erreurs);
        var adresses = new List<string>();
        foreach (var adresse in Morceaux(s.Adresses))
        {
            if (!Email().IsMatch(adresse)) erreurs.Add($"Courriel ({adresse}) invalide.");
            else if (!adresses.Contains(adresse, StringComparer.Ordinal)) adresses.Add(adresse);
        }
        if (adresses.Count == 0 && erreurs.Count(e => e.StartsWith("Courriel", StringComparison.Ordinal)) == 0)
            erreurs.Add("La liste a au moins une adresse.");
        var jointes = string.Join(";", adresses);
        if (jointes.Length > 7000) erreurs.Add($"Les adresses de la liste tiennent en 7000 caractères ({jointes.Length}).");
        if (erreurs.Count > 0) return (null, erreurs);
        return (new ListeAEcrire(code!, libelle!, jointes, s.Rcs, s.Judiciaire, s.Publique), erreurs);
    }

    /// <summary>Une adresse ou une boite du greffe (etiquette, jamais celle d'un mot de passe d'INFO_GREFFE) est-elle valide ?</summary>
    public static bool AdresseDeModele(string adresse)
        => Email().IsMatch(adresse) || (BoiteDuGreffe().IsMatch(adresse) && !EtiquetteSecrete().IsMatch(adresse));

    /// <summary>Les adresses d'un champ de modele, controlees, en « a;b » ; null si le champ est vide.</summary>
    private static string? AdressesDuModele(string? saisie, string champ, List<string> erreurs)
    {
        var morceaux = Morceaux(saisie);
        foreach (var m in morceaux.Where(m => !AdresseDeModele(m)))
            erreurs.Add($"{champ} : « {m} » n'est ni une adresse de courriel ni une boîte du greffe ([ADRESSE_MAIL_…]).");
        if (morceaux.Count == 0) return null;
        var jointes = string.Join(";", morceaux);
        if (jointes.Length > 200) erreurs.Add($"{champ} tient en 200 caractères ({jointes.Length}).");
        return jointes;
    }

    /// <summary>Les morceaux non vides d'une saisie d'adresses : « ; » ou un saut de ligne les separent (la V2 remplacait les sauts
    /// de ligne par « ; », ULT_MULTITEXT_MULTIMAIL.cs:16).</summary>
    private static List<string> Morceaux(string? saisie)
        => [.. (saisie ?? "").Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    private static string? Obligatoire(string? saisie, string champ, int taille, List<string> erreurs)
    {
        var v = Facultatif(saisie, champ, taille, erreurs);
        if (v is null) erreurs.Add($"{champ} est obligatoire.");
        return v;
    }

    private static string? Facultatif(string? saisie, string champ, int taille, List<string> erreurs)
    {
        var v = (saisie ?? "").Trim();
        if (v.Length > taille) erreurs.Add($"{champ} tient en {taille} caractères ({v.Length} saisis).");
        return v.Length == 0 ? null : v;
    }
}

/// <summary>
/// LA GESTION DES MODELES ET DES LISTES (SOC-006 T04, suite) : ce que les boutons de l'ecran V2 « Modele de mail » ecrivent, dans
/// la base V3 du greffe, chaque geste dans SA transaction avec sa ligne au journal des gestes (SOC-016, famille
/// <see cref="Famille"/>) -- la V2 n'en gardait qu'une demande creee deja validee, sur une autre connexion que l'ecriture
/// (proc_gestemail.md, § 9).
///
/// Le droit (fonction GESTEMAIL) est verifie par la route (ModuleCourriels) ; ici, ce que l'agent voit et peut toucher :
///  - un modele reserve a Amitel n'est modifiable, activable ou desactivable que par Amitel ; pour un agent du greffe il
///    n'existe pas (404, la meme reponse que sa lecture : CA05) ; seul Amitel coche « réservé à Amitel » (403 sinon) ;
///  - une liste privee n'est vue et modifiee que par son proprietaire (404 pour un collegue) ; une liste publique ne
///    redevient pas privee (OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:198) ;
///  - les listes par defaut d'un modele se choisissent parmi celles que l'agent voit ; les autres liens restent.
/// Rien n'est jamais supprime : un modele ou une liste se desactive (EMAILMOD_UTILISABLE_EN_SAISIE, LSTDM_ACTIF).
/// </summary>
public static class GestionCourriels
{
    /// <summary>La famille du journal des gestes (Base/migrations/SOC-006_gestion.sql).</summary>
    public const string Famille = "courriel";

    /// <summary>L'habillage du greffe, tel que les regles du texte le demandent. Lu AVANT la transaction du geste.</summary>
    public static async Task<HabillageDuGreffe> HabillageAsync(SqlConnection cn, CancellationToken ct)
        => new(!string.IsNullOrEmpty(await ModeleCourriel.CheminFichier.LireTexteAsync(cn, ct)),
               !string.IsNullOrEmpty(await ModeleCourriel.LireAvecRepliFichierAsync(cn, ct)));

    // --- Modeles ------------------------------------------------------------------------------------------------

    public static async Task<ModeleMessage> CreerModeleAsync(SqlConnection cn, string greffe, Appelant moi, SaisieModele saisie, CancellationToken ct)
    {
        if (saisie.ReserveAmitel && !moi.EstAmitel) throw ReserveAmitel();
        var m = Controle(ReglesCourriels.ControlerModele(saisie, corpsHtmlAvant: false, await HabillageAsync(cn, ct)));
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        if (await LireModeleAsync(cn, tx, m.Type, ct) is not null)
            throw new RefusGestionCourriels(StatusCodes.Status400BadRequest, $"Le nom du modèle de mail existe déjà : « {m.Type} ».");
        var visibles = await ListesVisiblesAsync(cn, tx, moi, ct);
        ExigerVisibles(m.ListesParDefaut, visibles);
        await using (var cmd = new SqlCommand("""
            INSERT INTO soc.ModeleMessage (Type, Sujet, Corps, CorpsHtml, Expediteur, Reponse, Copie, Lot1, Lot2, ReserveAmitel, LibelleEcran,
                                           Inscription, UtilisableEnSaisie, Comptabilite, DestinataireDefaut, EnvoiAutomatique, SignatureActive, Signature)
            VALUES (@type, @sujet, @corps, @html, @de, @reponse, @copie, @lot1, @lot2, @amitel, @libelle, @ins, 1, @cpt, @dest, @auto, @sigActive, @sig);
            """, cn, tx))
        {
            Parametres(cmd, m);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await LierListesAsync(cn, tx, m.Type, m.ListesParDefaut, visibles, ct);
        await JournalGestes.EcrireAsync(cn, tx, moi, saisie.Ecran, Famille, ObjetModele(m.Type), m.Type, "a cree", null, Instantane(m), ct);
        await tx.CommitAsync(ct);
        return await RelireModeleAsync(cn, m.Type, ct);
    }

    public static async Task<ModeleMessage> ModifierModeleAsync(SqlConnection cn, string greffe, Appelant moi, string type, SaisieModele saisie,
                                                              CancellationToken ct)
    {
        var habillage = await HabillageAsync(cn, ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var avant = await ModeleVisibleAsync(cn, tx, greffe, moi, type, ct);
        if (!string.Equals((saisie.Type ?? "").Trim(), avant.Type, StringComparison.OrdinalIgnoreCase))
            throw new RefusGestionCourriels(StatusCodes.Status400BadRequest,
                $"Le nom d'un modèle ne change pas une fois créé (« {avant.Type} ») : créez un nouveau modèle.");
        if (saisie.ReserveAmitel && !moi.EstAmitel) throw ReserveAmitel();
        var m = Controle(ReglesCourriels.ControlerModele(saisie with { Type = avant.Type }, avant.CorpsHtml, habillage));
        var visibles = await ListesVisiblesAsync(cn, tx, moi, ct);
        ExigerVisibles(m.ListesParDefaut, visibles);
        await using (var cmd = new SqlCommand("""
            UPDATE soc.ModeleMessage SET Sujet = @sujet, Corps = @corps, CorpsHtml = @html, Expediteur = @de, Reponse = @reponse, Copie = @copie,
                   Lot1 = @lot1, Lot2 = @lot2, ReserveAmitel = @amitel, LibelleEcran = @libelle, Inscription = @ins, Comptabilite = @cpt,
                   DestinataireDefaut = @dest, EnvoiAutomatique = @auto, SignatureActive = @sigActive, Signature = @sig
            WHERE Type = @type;
            """, cn, tx))
        {
            Parametres(cmd, m);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await LierListesAsync(cn, tx, m.Type, m.ListesParDefaut, visibles, ct);
        // Apres le geste, le modele garde aussi ses liens vers les listes que l'agent ne voit pas : le journal dit l'etat reel.
        var listesApres = avant.ListesParDefaut.Where(id => !visibles.Contains(id)).Concat(m.ListesParDefaut).Distinct().Order().ToArray();
        await JournalGestes.EcrireAsync(cn, tx, moi, saisie.Ecran, Famille, ObjetModele(m.Type), m.Type, "a modifie",
            Instantane(avant), Instantane(m with { ListesParDefaut = listesApres }), ct);
        await tx.CommitAsync(ct);
        return await RelireModeleAsync(cn, m.Type, ct);
    }

    /// <summary>« Désactiver » / « Activer » (OPE_EDIT_EMAILMODEL.ActiverDesactiver, :141-147). Un modele deja dans l'etat demande
    /// reste tel quel, sans ligne au journal : rien n'a change.</summary>
    public static async Task<ModeleMessage> ActiverModeleAsync(SqlConnection cn, string greffe, Appelant moi, string type, SaisieActivation saisie,
                                                             CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var avant = await ModeleVisibleAsync(cn, tx, greffe, moi, type, ct);
        if (avant.Actif != saisie.Actif)
        {
            await using (var cmd = new SqlCommand("UPDATE soc.ModeleMessage SET UtilisableEnSaisie = @actif WHERE Type = @type;", cn, tx))
            {
                cmd.Parameters.Add(new SqlParameter("@actif", SqlDbType.Bit) { Value = saisie.Actif });
                cmd.Parameters.Add(new SqlParameter("@type", SqlDbType.VarChar, 40) { Value = avant.Type });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await JournalGestes.EcrireAsync(cn, tx, moi, saisie.Ecran, Famille, ObjetModele(avant.Type), avant.Type,
                saisie.Actif ? "a modifie" : "a desactive", new { actif = avant.Actif }, new { actif = saisie.Actif }, ct);
        }
        await tx.CommitAsync(ct);
        return await RelireModeleAsync(cn, avant.Type, ct);
    }

    // --- Listes de distribution --------------------------------------------------------------------------------

    public static async Task<ListeDiffusion> CreerListeAsync(SqlConnection cn, Appelant moi, SaisieListe saisie, CancellationToken ct)
    {
        var l = Controle(ReglesCourriels.ControlerListe(saisie));
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        // Parmi TOUTES les listes du greffe, celles des collegues comprises (ListeDistributionMail.GetListeDistributionMail(code)).
        await using (var existe = new SqlCommand("SELECT COUNT(*) FROM soc.ListeDiffusion WITH (UPDLOCK, HOLDLOCK) WHERE Code = @code;", cn, tx))
        {
            existe.Parameters.Add(new SqlParameter("@code", SqlDbType.VarChar, 35) { Value = l.Code });
            if ((int)(await existe.ExecuteScalarAsync(ct))! > 0)
                throw new RefusGestionCourriels(StatusCodes.Status400BadRequest, $"Le code de la liste de distribution existe déjà : « {l.Code} ».");
        }
        int id;
        await using (var cmd = new SqlCommand("""
            INSERT INTO soc.ListeDiffusion (Code, Libelle, Adresses, Rcs, Judiciaire, Prive, Actif)
            OUTPUT inserted.Id VALUES (@code, @libelle, @adresses, @rcs, @jud, @prive, 1);
            """, cn, tx))
        {
            ParametresListe(cmd, l, l.Publique ? RoutesCourriels.ListePublique : moi.CompteWindows);
            id = (int)(await cmd.ExecuteScalarAsync(ct))!;
        }
        var cree = (await LireListeAsync(cn, tx, id, ct))!;
        await JournalGestes.EcrireAsync(cn, tx, moi, saisie.Ecran, Famille, ObjetListe(l.Code), id.ToString(), "a cree", null, cree, ct);
        await tx.CommitAsync(ct);
        return cree;
    }

    public static async Task<ListeDiffusion> ModifierListeAsync(SqlConnection cn, string greffe, Appelant moi, int id, SaisieListe saisie,
                                                              CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var avant = await ListeVisibleAsync(cn, tx, greffe, moi, id, ct);
        if (!string.Equals((saisie.Code ?? "").Trim(), avant.Code, StringComparison.OrdinalIgnoreCase))
            throw new RefusGestionCourriels(StatusCodes.Status400BadRequest, $"Le code d'une liste ne change pas une fois créée (« {avant.Code} »).");
        var l = Controle(ReglesCourriels.ControlerListe(saisie with { Code = avant.Code }));
        var publique = avant.Proprietaire == RoutesCourriels.ListePublique;
        if (publique && !l.Publique)
            throw new RefusGestionCourriels(StatusCodes.Status400BadRequest,
                $"La liste « {avant.Code} » est publique : elle ne redevient pas privée (tous les agents du greffe s'en servent).");
        await using (var cmd = new SqlCommand("""
            UPDATE soc.ListeDiffusion SET Libelle = @libelle, Adresses = @adresses, Rcs = @rcs, Judiciaire = @jud, Prive = @prive WHERE Id = @id;
            """, cn, tx))
        {
            ParametresListe(cmd, l, l.Publique ? RoutesCourriels.ListePublique : avant.Proprietaire);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var apres = (await LireListeAsync(cn, tx, id, ct))!;
        await JournalGestes.EcrireAsync(cn, tx, moi, saisie.Ecran, Famille, ObjetListe(avant.Code), id.ToString(), "a modifie", avant, apres, ct);
        await tx.CommitAsync(ct);
        return apres;
    }

    public static async Task<ListeDiffusion> ActiverListeAsync(SqlConnection cn, string greffe, Appelant moi, int id, SaisieActivation saisie,
                                                             CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var avant = await ListeVisibleAsync(cn, tx, greffe, moi, id, ct);
        var apres = avant;
        if (avant.Actif != saisie.Actif)
        {
            await using (var cmd = new SqlCommand("UPDATE soc.ListeDiffusion SET Actif = @actif WHERE Id = @id;", cn, tx))
            {
                cmd.Parameters.Add(new SqlParameter("@actif", SqlDbType.Bit) { Value = saisie.Actif });
                cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            apres = avant with { Actif = saisie.Actif };
            await JournalGestes.EcrireAsync(cn, tx, moi, saisie.Ecran, Famille, ObjetListe(avant.Code), id.ToString(),
                saisie.Actif ? "a modifie" : "a desactive", new { actif = avant.Actif }, new { actif = saisie.Actif }, ct);
        }
        await tx.CommitAsync(ct);
        return apres;
    }

    // --- Lectures dans la transaction du geste ----------------------------------------------------------------

    /// <summary>Un modele tel qu'en base, verrouille jusqu'a la fin du geste.</summary>
    private sealed record ModeleEnBase(string Type, string? LibelleEcran, string Sujet, string Corps, bool CorpsHtml, string? Expediteur,
        string? Reponse, string? Copie, string? DestinataireDefaut, bool Lot1, bool Lot2, bool? Inscription, bool? Comptabilite,
        bool EnvoiAutomatique, bool ReserveAmitel, bool SignatureActive, string? Signature, bool Actif, int[] ListesParDefaut);

    /// <summary>Le modele <paramref name="type"/> que l'appelant peut toucher ; sinon 404, la reponse de sa lecture (CA05 : un agent du
    /// greffe ne voit pas un modele reserve a Amitel, il ne le modifie donc pas).</summary>
    private static async Task<ModeleEnBase> ModeleVisibleAsync(SqlConnection cn, SqlTransaction tx, string greffe, Appelant moi, string type,
                                                               CancellationToken ct)
        => await LireModeleAsync(cn, tx, type, ct) is { } m && (moi.EstAmitel || !m.ReserveAmitel)
            ? m
            : throw new RefusGestionCourriels(StatusCodes.Status404NotFound, $"Le modèle de courriel « {type} » n'est pas dans le greffe {greffe}.");

    private static async Task<ModeleEnBase?> LireModeleAsync(SqlConnection cn, SqlTransaction tx, string type, CancellationToken ct)
    {
        // UPDLOCK + HOLDLOCK : deux creations du meme nom ne passent pas toutes deux le controle d'unicite.
        await using var cmd = new SqlCommand("""
            SELECT Type, LibelleEcran, Sujet, Corps, CorpsHtml, Expediteur, Reponse, Copie, DestinataireDefaut, Lot1, Lot2, Inscription,
                   Comptabilite, EnvoiAutomatique, ReserveAmitel, SignatureActive, Signature, UtilisableEnSaisie
            FROM soc.ModeleMessage WITH (UPDLOCK, HOLDLOCK) WHERE Type = @type;
            SELECT IdListe FROM soc.ModeleMessageListe WHERE TypeModele = @type ORDER BY IdListe;
            """, cn, tx);
        cmd.Parameters.Add(new SqlParameter("@type", SqlDbType.VarChar, 40) { Value = type });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        string? T(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        bool? B(int i) => r.IsDBNull(i) ? null : r.GetBoolean(i);
        var m = new ModeleEnBase(r.GetString(0), T(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), T(5), T(6), T(7), T(8),
            r.GetBoolean(9), r.GetBoolean(10), B(11), B(12), r.GetBoolean(13), r.GetBoolean(14), r.GetBoolean(15), T(16),
            B(17) != false, []);
        await r.NextResultAsync(ct);
        var listes = new List<int>();
        while (await r.ReadAsync(ct)) listes.Add(r.GetInt32(0));
        return m with { ListesParDefaut = [.. listes] };
    }

    /// <summary>Le modele tel que la liste de l'ecran le montre, apres le geste.</summary>
    private static async Task<ModeleMessage> RelireModeleAsync(SqlConnection cn, string type, CancellationToken ct)
        => (await ServiceCourriels.ModelesVisiblesAsync(cn, estAmitel: true, ct, type)).Single();

    /// <summary>Les listes que l'appelant voit : les publiques et les siennes (ListeDistributionMail.cs:63).</summary>
    private static async Task<HashSet<int>> ListesVisiblesAsync(SqlConnection cn, SqlTransaction tx, Appelant moi, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT Id FROM soc.ListeDiffusion WHERE Prive = @moi OR Prive = @publique;", cn, tx);
        cmd.Parameters.Add(new SqlParameter("@moi", SqlDbType.VarChar, 200) { Value = moi.CompteWindows });
        cmd.Parameters.Add(new SqlParameter("@publique", SqlDbType.VarChar, 200) { Value = RoutesCourriels.ListePublique });
        var ids = new HashSet<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) ids.Add(r.GetInt32(0));
        return ids;
    }

    private static void ExigerVisibles(int[] voulues, HashSet<int> visibles)
    {
        var inconnues = voulues.Where(id => !visibles.Contains(id)).ToList();
        if (inconnues.Count > 0)
            throw new RefusGestionCourriels(StatusCodes.Status400BadRequest,
                $"Liste(s) de distribution n° {string.Join(", ", inconnues)} : ni publique(s) ni à vous dans ce greffe.");
    }

    /// <summary>
    /// Les listes par defaut du modele : parmi celles que l'agent voit, exactement <paramref name="voulues"/> ; un lien vers une
    /// liste qu'il ne voit pas reste. (La V2 effacait tout lien non coche, y compris vers la liste privee d'un collegue, que sa
    /// liste a cocher ne montrait pas : ecart voulu, socle-courriels.md.)
    /// </summary>
    private static async Task LierListesAsync(SqlConnection cn, SqlTransaction tx, string type, int[] voulues, HashSet<int> visibles, CancellationToken ct)
    {
        var liens = new List<(int Id, int IdListe)>();
        await using (var cmd = new SqlCommand("SELECT Id, IdListe FROM soc.ModeleMessageListe WHERE TypeModele = @type;", cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@type", SqlDbType.VarChar, 40) { Value = type });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) liens.Add((r.GetInt32(0), r.GetInt32(1)));
        }
        foreach (var lien in liens.Where(l => visibles.Contains(l.IdListe) && !voulues.Contains(l.IdListe)))
        {
            await using var cmd = new SqlCommand("DELETE FROM soc.ModeleMessageListe WHERE Id = @id;", cn, tx);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = lien.Id });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        foreach (var idListe in voulues.Where(v => !liens.Any(l => l.IdListe == v)))
        {
            await using var cmd = new SqlCommand("INSERT INTO soc.ModeleMessageListe (TypeModele, IdListe) VALUES (@type, @liste);", cn, tx);
            cmd.Parameters.Add(new SqlParameter("@type", SqlDbType.VarChar, 40) { Value = type });
            cmd.Parameters.Add(new SqlParameter("@liste", SqlDbType.Int) { Value = idListe });
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>La liste <paramref name="id"/> si l'appelant la voit (publique ou a lui) ; sinon 404, comme une liste inconnue.</summary>
    private static async Task<ListeDiffusion> ListeVisibleAsync(SqlConnection cn, SqlTransaction tx, string greffe, Appelant moi, int id,
                                                                CancellationToken ct)
        => await LireListeAsync(cn, tx, id, ct, verrou: true) is { } l
           && (l.Proprietaire == RoutesCourriels.ListePublique || string.Equals(l.Proprietaire, moi.CompteWindows, StringComparison.OrdinalIgnoreCase))
            ? l
            : throw new RefusGestionCourriels(StatusCodes.Status404NotFound, $"La liste de distribution n° {id} n'est pas dans le greffe {greffe}.");

    private static async Task<ListeDiffusion?> LireListeAsync(SqlConnection cn, SqlTransaction tx, int id, CancellationToken ct, bool verrou = false)
    {
        await using var cmd = new SqlCommand(
            $"SELECT Id, Code, Libelle, Adresses, Rcs, Judiciaire, Prive, Actif FROM soc.ListeDiffusion{(verrou ? " WITH (UPDLOCK)" : "")} WHERE Id = @id;",
            cn, tx);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new ListeDiffusion(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.GetBoolean(7))
            : null;
    }

    // --- Ecritures ------------------------------------------------------------------------------------------------

    private static void Parametres(SqlCommand cmd, ModeleAEcrire m)
    {
        object Ou(string? v) => (object?)v ?? DBNull.Value;
        cmd.Parameters.Add(new SqlParameter("@type", SqlDbType.VarChar, 40) { Value = m.Type });
        cmd.Parameters.Add(new SqlParameter("@sujet", SqlDbType.VarChar, 200) { Value = m.Sujet });
        cmd.Parameters.Add(new SqlParameter("@corps", SqlDbType.VarChar, 4000) { Value = m.Corps });
        cmd.Parameters.Add(new SqlParameter("@html", SqlDbType.Bit) { Value = m.CorpsHtml });
        cmd.Parameters.Add(new SqlParameter("@de", SqlDbType.VarChar, 200) { Value = m.Expediteur });
        cmd.Parameters.Add(new SqlParameter("@reponse", SqlDbType.VarChar, 200) { Value = Ou(m.Reponse) });
        cmd.Parameters.Add(new SqlParameter("@copie", SqlDbType.VarChar, 200) { Value = Ou(m.Copie) });
        cmd.Parameters.Add(new SqlParameter("@lot1", SqlDbType.Bit) { Value = m.Lot1 });
        cmd.Parameters.Add(new SqlParameter("@lot2", SqlDbType.Bit) { Value = m.Lot2 });
        cmd.Parameters.Add(new SqlParameter("@amitel", SqlDbType.Bit) { Value = m.ReserveAmitel });
        cmd.Parameters.Add(new SqlParameter("@libelle", SqlDbType.VarChar, 100) { Value = Ou(m.LibelleEcran) });
        cmd.Parameters.Add(new SqlParameter("@ins", SqlDbType.Bit) { Value = m.Inscription });
        cmd.Parameters.Add(new SqlParameter("@cpt", SqlDbType.Bit) { Value = m.Comptabilite });
        cmd.Parameters.Add(new SqlParameter("@dest", SqlDbType.VarChar, 200) { Value = Ou(m.DestinataireDefaut) });
        cmd.Parameters.Add(new SqlParameter("@auto", SqlDbType.Bit) { Value = m.EnvoiAutomatique });
        cmd.Parameters.Add(new SqlParameter("@sigActive", SqlDbType.Bit) { Value = m.SignatureActive });
        cmd.Parameters.Add(new SqlParameter("@sig", SqlDbType.VarChar, 2000) { Value = Ou(m.Signature) });
    }

    private static void ParametresListe(SqlCommand cmd, ListeAEcrire l, string? prive)
    {
        cmd.Parameters.Add(new SqlParameter("@code", SqlDbType.VarChar, 35) { Value = l.Code });
        cmd.Parameters.Add(new SqlParameter("@libelle", SqlDbType.VarChar, 200) { Value = l.Libelle });
        cmd.Parameters.Add(new SqlParameter("@adresses", SqlDbType.VarChar, 7000) { Value = l.Adresses });
        cmd.Parameters.Add(new SqlParameter("@rcs", SqlDbType.Bit) { Value = l.Rcs });
        cmd.Parameters.Add(new SqlParameter("@jud", SqlDbType.Bit) { Value = l.Judiciaire });
        cmd.Parameters.Add(new SqlParameter("@prive", SqlDbType.VarChar, 200) { Value = (object?)prive ?? DBNull.Value });
    }

    private static T Controle<T>((T? Valeur, List<string> Erreurs) controle) where T : class
        => controle.Valeur ?? throw new RefusGestionCourriels(StatusCodes.Status400BadRequest, string.Join(" ", controle.Erreurs));

    private static RefusGestionCourriels ReserveAmitel()
        => new(StatusCodes.Status403Forbidden,
            "Refusé : « Modèle modifiable uniquement par Amitel » ne se coche que par un agent d'Amitel (modèle commun à tous les greffes).");

    private static string ObjetModele(string type) => $"le modèle de courriel {type}";

    private static string ObjetListe(string code) => $"la liste de distribution {code}";

    private static object Instantane(ModeleAEcrire m) => new
    {
        m.LibelleEcran, m.Sujet, m.Corps, m.CorpsHtml, m.Expediteur, m.Reponse, m.Copie, m.DestinataireDefaut, m.Lot1, m.Lot2,
        m.Inscription, m.Comptabilite, m.EnvoiAutomatique, m.ReserveAmitel, m.SignatureActive, m.Signature, m.ListesParDefaut,
    };

    private static object Instantane(ModeleEnBase m) => new
    {
        m.LibelleEcran, m.Sujet, m.Corps, m.CorpsHtml, m.Expediteur, m.Reponse, m.Copie, m.DestinataireDefaut, m.Lot1, m.Lot2,
        m.Inscription, m.Comptabilite, m.EnvoiAutomatique, m.ReserveAmitel, m.SignatureActive, m.Signature, m.ListesParDefaut,
    };
}
