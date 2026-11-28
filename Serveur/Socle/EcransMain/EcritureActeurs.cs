using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Acteurs » (PROC_ACTEUR_JUD, écran fait main web/src/ecrans/EcranActeurs.tsx) : le geste ENREGISTRER LES COORDONNÉES d'un acteur
/// (téléphone, fax, courriel), le « Valider » de sa fiche d'édition de RIG v2, pour les six types de la V2, écrit dans la base du greffe.
/// Joué par la route POST RoutesCoordonneesActeur.Enregistrer (ModuleActeurs.cs) dans la transaction et le journal des gestes du socle
/// (EcrivainsEcrans.JouerAsync). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026 ; la lecture :
/// LectureActeurs.cs (la clé d'une ligne, « Type-numéro », est celle du pont Pont/EcranActeursBase.cs).
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_ACTEUR_JUD\Edition) : une fiche par type (ETP_EDITION_ACTEUR.cs:31-52)
/// -- OPE_EDITION_GJUGE, GMANDPC, GPARQU, GGREFFIER, GINTERV, EXPERT. Son « Valider » (OPE_EDITION_GJUGE.cs:125-148, GMANDPC.cs:120-133,
/// GPARQU.cs:91-100, GGREFFIER.cs:86-95, GINTERV.cs:96-105, EXPERT.cs:104-132) vérifie TOUTE la fiche (Processus.Verifier), puis la
/// sauve ; la V3 rejoue sur la fiche de la base les contrôles que ses champs ne font pas changer :
///   - les OBLIGATOIRES de chaque fiche (Designer.cs, MetierObligatoire) : civilité et nom ; l'abrégé (sauf le juge) ; le « Qualifiant
///     profession » de l'intervenant -- « Champ obligatoire » et le titre du champ (RigAutomate\ULT\Ult.moteur.cs:14, :516-517) ;
///   - le JUGE (OPE_EDITION_GJUGE.DoVerifier, :89-119) : ni abrégé ni prénom -> « Saisir le prénom du juge » ; une date de début après la
///     date de fin -> refus (message de la V2 au mot près) ;
///   - l'INTERVENANT (OPE_EDITION_GINTERV.DoVerifier) : nom de coffre et date de démarrage du coffre vont ensemble ;
///   - le TÉLÉPHONE et le FAX (ULT_TEXT_TELEPHONE : RigTools\TOOLS\RegEx.cs:33, « Numéro de téléphone invalide ») ; le COURRIEL
///     (ULT_TEXT_MULTIMAIL : plusieurs adresses séparées par « ; », une virgule corrigée en « ; » (les espaces autour d'une adresse retirés : écart V3), chacune RegEx.Email,
///     « Courriel ({0}) invalide ») : contrôles bloquants de Ult_Text_Internal.DoVerifier (:342-362) ;
///   - ÉCRITURE : les trois colonnes ; le juge, le parquetier et le greffier repassent « à diffuser » à Experian (XXX_TOP_EXPERIAN = 0,
///     OPE_EDITION_GJUGE.cs:135, GPARQU.cs:99, GGREFFIER.cs:94).
/// Les contrôles qui ne portent que sur un champ CHANGÉ (abrégé unique, rôles du juge ou du mandataire mis à « non ») ne s'appliquent pas :
/// le geste ne change que les coordonnées.
/// ÉCART NOMMÉ : RIG v2 tronque en silence une valeur trop longue (TronqueChaines) ; la V3 la refuse avec la longueur de la colonne (RIG_DEV).
/// NON REPRIS (nommés) : les autres champs de la fiche (identité, rôles, statut, chambre, coffre...), que l'écran V3 montre en lecture ; la
/// DEMANDE « Modification du juge » etc. (Demande.GestionDemandeForProcessus) -- une trace, que le journal des gestes remplace (décision du
/// plan) ; la mise à jour des participants cochés d'un expert (sa fiche « Experts », EcritureExperts.cs, le fait) ; le vidage des caches de
/// la V2 (CacheSqlBase.Clear), sans objet en V3.
/// TRACE : famille « reglage » (un acteur du greffe), « a modifie ».
/// </summary>
public static class EcritureActeurs
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Acteurs";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Enregistrer];

    /// <summary>Un type d'acteur de RIG v2 : sa table, ses colonnes, ses obligatoires (colonne, titre du champ), et sa colonne Experian.</summary>
    private sealed record TypeActeur(string Type, string Libelle, string Table, string Cle, string Prefixe, int TailleMail,
                                     (string Colonne, string Titre)[] Obligatoires, bool TopExperian);

    private static readonly (string, string) Civ = ("CIVILITE", "Civilité"), Nom = ("NOM", "Nom"), Abr = ("ABREGE", "Abrégé"),
                                              Qualif = ("QUALIF_PROFESSION", "Qualifiant profession");

    /// <summary>Les six types, aux noms de l'écran (LectureActeurs.cs) ; colonnes et tailles de RIG_DEV (copie de développement, 28/11/2026).</summary>
    private static readonly TypeActeur[] Types =
    [
        new("Juge", "le juge", "v2.JUGE", "JUG_ID_JUG", "JUG", 70, [Civ, Nom], true),
        new("Mandataire", "le mandataire PC", "v2.MANDATAIRE_PC", "MNDPC_ID_MNDPC", "MNDPC", 1000, [Civ, Abr, Nom], false),
        new("Parquetier", "le parquetier", "v2.PARQUETIER", "PRQTR_ID_PRQTR", "PRQTR", 70, [Civ, Abr, Nom], true),
        new("Greffier", "le greffier assermenté", "v2.GREFFIER_ASSERMENTE", "GRASS_ID_GRASS", "GRASS", 70, [Civ, Abr, Nom], true),
        new("Intervenant", "l'intervenant", "v2.INTERVENANT_PC_REPER", "INTRPR_ID_INTRPR", "INTRPR", 70, [Civ, Qualif, Abr, Nom], false),
        new("Expert", "l'expert", "v2.EXPERT", "EXPRT_ID_EXPRT", "EXPRT", 70, [Civ, Abr, Nom], false),
    ];

    private sealed record Coordonnees(string Telephone, string Fax, string Mail);

    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var cle = geste.Demande.Ids.FirstOrDefault() ?? "";
        var tiret = cle.LastIndexOf('-');
        var t = tiret > 0 ? Types.FirstOrDefault(x => x.Type == cle[..tiret]) : null;
        if (t is null || !int.TryParse(cle[(tiret + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser($"Acteur introuvable dans la base du greffe ({cle}).");

        // La fiche de la base, verrouillée pour le geste.
        var colonnes = t.Obligatoires.Select(o => $"{t.Prefixe}_{o.Colonne}").ToList();
        if (t.Type == "Juge") colonnes.AddRange(["JUG_ABREGE", "JUG_PRENOM", "JUG_DATE_DEBUT", "JUG_DATE_FIN"]);
        if (t.Type == "Intervenant") colonnes.AddRange(["INTRPR_NOM_COFFRE", "INTRPR_DATE_DEMARRAGE_COFFRE"]);
        var mail = t.Prefixe == "EXPRT" ? "EXPRT_ADRESSE_MAIL" : $"{t.Prefixe}_ADRESSE_MAIL";
        colonnes.AddRange([$"{t.Prefixe}_TELEPHONE", $"{t.Prefixe}_FAX", mail]);
        var fiche = new Dictionary<string, object?>(StringComparer.Ordinal);
        await using (var r = await geste.LireAsync($"SELECT {string.Join(", ", colonnes.Distinct())} FROM {t.Table} WITH (UPDLOCK) WHERE {t.Cle} = @id",
                         ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Acteur introuvable dans la base du greffe ({cle}).");
            for (var i = 0; i < r.FieldCount; i++) fiche[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        }
        string Texte(string c) => fiche.GetValueOrDefault(c) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim() : "";
        var avant = new Coordonnees(Texte($"{t.Prefixe}_TELEPHONE"), Texte($"{t.Prefixe}_FAX"), Texte(mail));

        // Les coordonnées saisies (un champ absent garde la base) ; le courriel corrigé comme ULT_TEXT_MULTIMAIL.
        string Saisi(string champ, string base_) => geste.Demande.Valeurs.TryGetValue(champ, out var v) ? v?.Trim() ?? "" : base_;
        var mailSaisi = string.Join(";", Saisi(RoutesCoordonneesActeur.ChampMail, avant.Mail)
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var apres = new Coordonnees(Saisi(RoutesCoordonneesActeur.ChampTelephone, avant.Telephone), Saisi(RoutesCoordonneesActeur.ChampFax, avant.Fax), mailSaisi);
        if (apres == avant) return IssueGeste.Refuser("Rien à enregistrer : aucune valeur ne change.");

        // Verifier, sur toute la fiche : les obligatoires, puis les contrôles propres au type, puis les formats.
        foreach (var (colonne, titre) in t.Obligatoires)
            if (Texte($"{t.Prefixe}_{colonne}").Length == 0)
                return IssueGeste.Refuser($"{ControlesSaisieV2.ChampObligatoire} : {titre} (à compléter sur la fiche {De(t.Libelle)}).");
        if (t.Type == "Juge")
        {
            if (Texte("JUG_ABREGE").Length == 0 && Texte("JUG_PRENOM").Length == 0)
                return IssueGeste.Refuser("Saisir le prénom du juge : Quand l'abrégé n'est pas renseigné, le prénom est obligatoire.");
            if (fiche.GetValueOrDefault("JUG_DATE_DEBUT") is DateTime debut && fiche.GetValueOrDefault("JUG_DATE_FIN") is DateTime fin && debut > fin)
                return IssueGeste.Refuser("Date : La date de début ne peut pas être inférieure à la date de fin.");
        }
        if (t.Type == "Intervenant")
        {
            var nomCoffre = Texte("INTRPR_NOM_COFFRE");
            var dateCoffre = fiche.GetValueOrDefault("INTRPR_DATE_DEMARRAGE_COFFRE") is DateTime;
            if (nomCoffre.Length == 0 && dateCoffre) return IssueGeste.Refuser("Saisir un nom de coffre : Vous devez saisir un nom de coffre.");
            if (nomCoffre.Length > 0 && !dateCoffre)
                return IssueGeste.Refuser("Saisir une date de démarrage du coffre : Vous devez saisir une date de démarrage du coffre.");
        }
        foreach (var (champ, valeur, taille) in new[] { (RoutesCoordonneesActeur.ChampTelephone, apres.Telephone, 15),
                     (RoutesCoordonneesActeur.ChampFax, apres.Fax, 15), (RoutesCoordonneesActeur.ChampMail, apres.Mail, t.TailleMail) })
            if (valeur.Length > taille) return IssueGeste.Refuser($"« {champ} » : {valeur.Length} caractères, la colonne en porte {taille}.");
        if (apres.Telephone.Length > 0 && !ControlesSaisieV2.Telephone.IsMatch(apres.Telephone))
            return IssueGeste.Refuser($"« {RoutesCoordonneesActeur.ChampTelephone} » : {ControlesSaisieV2.TelephoneInvalide}.");
        if (apres.Fax.Length > 0 && !ControlesSaisieV2.Telephone.IsMatch(apres.Fax))
            return IssueGeste.Refuser($"« {RoutesCoordonneesActeur.ChampFax} » : {ControlesSaisieV2.TelephoneInvalide}.");
        if (apres.Mail.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(m => !ControlesSaisieV2.Courriel.IsMatch(m)) is { } faux)
            return IssueGeste.Refuser($"« {RoutesCoordonneesActeur.ChampMail} » : Courriel ({faux}) invalide.");

        await geste.EcrireAsync($"""
            UPDATE {t.Table} SET {t.Prefixe}_TELEPHONE = @tel, {t.Prefixe}_FAX = @fax, {mail} = @mail{(t.TopExperian ? $", {t.Prefixe}_TOP_EXPERIAN = 0" : "")}
            WHERE {t.Cle} = @id
            """, ct, ControlesSaisieV2.OuNull("@tel", apres.Telephone), ControlesSaisieV2.OuNull("@fax", apres.Fax), ControlesSaisieV2.OuNull("@mail", apres.Mail),
            new SqlParameter("@id", id));
        var nom = Texte($"{t.Prefixe}_NOM");
        return IssueGeste.Ecrit($"Coordonnées {De(t.Libelle)} {nom} enregistrées.", [cle],
            new TraceGeste("reglage", $"{t.Libelle} {nom}", cle, "a modifie", avant, apres));
    }

    /// <summary>« le juge » -> « du juge », « l'expert » -> « de l'expert ».</summary>
    private static string De(string libelle) => libelle.StartsWith("le ", StringComparison.Ordinal) ? "du " + libelle[3..] : "de " + libelle;
}
