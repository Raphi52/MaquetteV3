using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Recopie;
using RigV3.Serveur.Socle.Chaines;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Passage;

/// <summary>Les etapes du passage d'un greffe, dans l'ordre ou elles se jouent (SOC-024 CA02).</summary>
public static class EtapesPassage
{
    /// <summary>Lecture seule : catalogue, deux bases, base V3 neuve et a jour, programmes de la V2 d'origine,
    /// demandes en cours relevees. Un refus ici ne touche rien.</summary>
    public const string ControlesAvant = "controles-avant";
    /// <summary>Les 7 chaines signees de la V3 ouvertes (SOC-018) — AVANT les reprises (decision du lot 5).</summary>
    public const string Chaines = "chaines";
    /// <summary>Le transfert initial de la base V2 (REC-001) : reprises de la phase 1 et tables telles quelles.</summary>
    public const string Transfert = "transfert";
    /// <summary>Les trois programmes de numerotation de la V2 remplaces (Base\v2\bascule.sql, NUM-002).</summary>
    public const string Bascule = "bascule";
    /// <summary>Les comparateurs : transfert fini, chaines ouvertes, compteurs egaux, programmes remplaces, demandes en cours intactes.</summary>
    public const string ControlesApres = "controles-apres";
    /// <summary>La date de passage inscrite au catalogue (SOC-007 CA05) : la creation d'affaires passe a la V3 a cette heure.</summary>
    public const string Catalogue = "catalogue";
    /// <summary>La regle de SOC-014 jouee sur la fiche relue : une affaire nouvelle va a la V3, une ancienne a la V2.</summary>
    public const string Aiguillage = "aiguillage";
    /// <summary>Hors beta : le corps d'origine des programmes archive (Base\v2\fin-de-beta.sql), plus de retour (NUM-022 CA03).</summary>
    public const string FinDeGarde = "fin-de-garde";
    /// <summary>Le retour a l'etat d'avant, joue seulement si une etape echoue (SOC-024 CA03).</summary>
    public const string Retour = "retour";

    public static readonly IReadOnlyList<string> Ordre =
        [ControlesAvant, Chaines, Transfert, Bascule, ControlesApres, Catalogue, Aiguillage, FinDeGarde];
}

/// <summary>Ce que devient un greffe au bout de son passage.</summary>
public static class IssuesPassage
{
    /// <summary>Passe a la V3 (code 0).</summary>
    public const string Passe = "passe";
    /// <summary>Une etape a echoue ; le greffe est resté entierement sur la V2 (code 1).</summary>
    public const string RevenuV2 = "revenu-v2";
    /// <summary>Refuse aux controles d'entree, rien n'a ete touche (code 2).</summary>
    public const string Refuse = "refuse";
    /// <summary>Une etape a echoue ET le retour n'a pas pu se faire : intervention de l'exploitant (code 4).</summary>
    public const string EchecSansRetour = "echec-sans-retour";

    public static int Code(string issue) => issue switch { Passe => 0, RevenuV2 => 1, Refuse => 2, _ => 4 };
}

/// <summary>Une etape jouee : son nom, son debut (UTC), sa duree, son etat (faite, echec, non jouee) et ce qu'elle a constate.</summary>
public sealed record EtapeJouee(string Nom, DateTime DebutUtc, long DureeMs, string Etat, string Detail);

/// <summary>Un ecart trouve par un comparateur (comptes, droits, reglages, donnees, numeros, demandes) : SOC-025 CA02.</summary>
public sealed record EcartPassage(string Comparateur, string Detail);

/// <summary>
/// Le rapport du passage d'un greffe : la ligne du tableau de bord du jour J (SOC-024 CA06) et la ligne d'une
/// repetition generale (SOC-025 CA02). Ecrit en JSON apres chaque etape quand un fichier est donne.
/// </summary>
public sealed class RapportPassage
{
    public string Greffe { get; set; } = "";
    public string Nom { get; set; } = "";
    public bool EstBeta { get; set; }
    public bool EstOutreMer { get; set; }
    public string? Fuseau { get; set; }
    public string? Serveur { get; set; }
    public string? BaseV2 { get; set; }
    public string? BaseV3 { get; set; }
    /// <summary>L'heure de passage, heure LOCALE du greffe, et la meme en UTC.</summary>
    public DateTime? PassageLocal { get; set; }
    public DateTime? PassageUtc { get; set; }
    public DateTime DebutUtc { get; set; }
    public DateTime? FinUtc { get; set; }
    public long DureeMs { get; set; }
    public string Issue { get; set; } = "";
    public string? Motif { get; set; }
    public List<EtapeJouee> Etapes { get; set; } = [];
    public List<EcartPassage> Ecarts { get; set; } = [];
    public List<string> Erreurs { get; set; } = [];
    /// <summary>Ce qui reste a faire bien que le greffe soit passe (ex. la garde des programmes non levee).</summary>
    public List<string> Reserves { get; set; } = [];
    /// <summary>La base V3 a ete ecrite (chaines ouvertes, transfert commence) : la recreer avant de repasser.</summary>
    public bool BaseV3ARecreer { get; set; }
    public int? IdTransfert { get; set; }
    public long? LignesTransferees { get; set; }

    /// <summary>La derniere etape faite : ce que montre le tableau de bord du jour J.</summary>
    public string? DerniereEtapeFaite => Etapes.LastOrDefault(e => e.Etat == EtatsEtape.Faite && e.Nom != EtapesPassage.Retour)?.Nom;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string EnJson() => JsonSerializer.Serialize(this, Json);
    public static RapportPassage Lire(string fichier) => JsonSerializer.Deserialize<RapportPassage>(File.ReadAllText(fichier), Json)
        ?? throw new InvalidDataException($"{fichier} : rapport de passage vide.");
}

public static class EtatsEtape
{
    public const string Faite = "faite";
    public const string Echec = "echec";
    public const string NonJouee = "non jouee";
}

/// <summary>Une etape a echoue : son motif, en clair.</summary>
public sealed class EchecEtape(string message) : Exception(message);

/// <param name="Commune">La base commune du site (catalogue des greffes, SOC-007) : c'est la qu'on trouve les deux
/// bases du greffe, leur instance, son fuseau, s'il est beta.</param>
/// <param name="CodeGreffe">Le greffe qui passe.</param>
public sealed record OptionsPassage(string Commune, string CodeGreffe)
{
    /// <summary>Comment s'authentifier aupres de l'instance du greffe (Greffes:Authentification de la partie serveur).</summary>
    public string Authentification { get; init; } = CatalogueGreffes.AuthentificationParDefaut;
    /// <summary>La cle de signature des chaines du greffe (SOC-018) : celle du compte de service de la partie serveur.</summary>
    public required ICoffreCles Coffre { get; init; }
    /// <summary>Le dossier de bascule.sql, retour.sql, fin-de-beta.sql (Base\v2) ; par defaut, la copie qui voyage avec l'outil.</summary>
    public string DossierScripts { get; init; } = Path.Combine(AppContext.BaseDirectory, "scripts-v2");
    /// <summary>L'heure de passage, heure LOCALE du greffe ; null = maintenant (a la minute).</summary>
    public DateTime? PassageLocal { get; init; }
    /// <summary>Greffe beta : la fin de la beta (heure du serveur SQL) ; jusque-la, le retour a la V2 reste possible (NUM-022).</summary>
    public DateTime? FinBeta { get; init; }
    /// <summary>La session d'evenements de l'instance qui compte les echecs de numerotation (NUM-007 CA05).</summary>
    public string SessionEchecs { get; init; } = "rigv3_numerotation_echecs";
    public string Auteur { get; init; } = $@"{Environment.UserDomainName}\{Environment.UserName}";
    /// <summary>Sans lui, le catalogue et l'instance du greffe doivent etre sur CE poste : un agent qui lance l'outil d'ici
    /// n'ecrit jamais sur SQL-DEV, SQL-PROD ni un serveur de greffe.</summary>
    public bool Exploitation { get; init; }
    /// <summary>Repetition generale seulement (SOC-024 T06) : l'etape qui echoue expres, pour prouver le retour.</summary>
    public string? EchecSimule { get; init; }
    /// <summary>Ce qui se passe dans la V2 pendant le passage (agents qui travaillent), joue apres chaque etape faite :
    /// le banc et la repetition y font numeroter, saisir, garder une session ouverte (SOC-024 CA04).</summary>
    public Func<string, CancellationToken, Task>? ApresEtape { get; init; }
    public Action<string>? Suivre { get; init; }
    /// <summary>Ecrit le rapport JSON apres chaque etape (tableau de bord du jour J, SOC-024 CA06).</summary>
    public string? FichierRapport { get; init; }
    public int TailleLot { get; init; } = 50_000;
}

/// <summary>
/// LE PASSAGE D'UN GREFFE A LA V3 (SOC-024 T02) : les etapes dans l'ordre, chacune verifiee, et le retour a l'etat
/// d'avant pour ce seul greffe si l'une echoue (CA03).
///
///   controles-avant  lecture seule ; un refus ne touche rien
///   chaines          ServiceChaines.OuvrirAsync (SOC-018) : avant les reprises, decision du lot 5
///   transfert        MoteurTransfert.TransfererAsync (REC-001) : la V2 lue dans un instantane, compteurs en dernier
///   bascule          Base\v2\bascule.sql, Reprise=faite, avec une garde : le corps d'origine des programmes est
///                    garde jusqu'a la fin du passage (hors beta) ou de la beta, pour que le retour reste possible
///   controles-apres  transfert fini, 260 en premier maillon, compteurs V2 = V3, programmes remplaces,
///                    demandes en cours d'avant toutes presentes
///   catalogue        CatalogueGreffes.SaisirPassageAsync : le point de non-retour, en dernier
///   aiguillage       RegleAiguillage sur la fiche relue
///   fin-de-garde     hors beta : fin-de-beta.sql (plus de retour, NUM-022 CA03) ; un echec ici est une reserve
/// Echec d'une etape : retour.sql si la bascule est en service, date du catalogue retiree si le passage l'avait
/// posee et que l'heure n'est pas atteinte, puis verification que la V2 est revenue a l'identique. La base V3 n'est
/// ni supprimee ni videe (geste de l'exploitant) : le rapport dit qu'elle est a recreer.
/// La V2 reste ouverte tout du long : le transfert lit un instantane, la bascule ne bloque la table des compteurs
/// que le temps de sa transaction (SOC-024 CA04).
/// </summary>
public static class MoteurPassage
{
    /// <summary>Le format des dates passees aux scripts (variable FinBeta de bascule.sql).</summary>
    private const string FormatScript = "yyyy-MM-ddTHH:mm:ss";

    public static async Task<RapportPassage> PasserAsync(OptionsPassage o, CancellationToken ct = default)
    {
        var chrono = Stopwatch.StartNew();
        var r = new RapportPassage { Greffe = o.CodeGreffe, DebutUtc = DateTime.UtcNow };
        var catalogue = Catalogue(o);
        var etat = new EtatPassage();

        async Task Etape(string nom, Func<Task<string>> action)
        {
            var debut = DateTime.UtcNow;
            var sw = Stopwatch.StartNew();
            o.Suivre?.Invoke($"{debut:HH:mm:ss} {nom} ...");
            try
            {
                if (string.Equals(o.EchecSimule, nom, StringComparison.OrdinalIgnoreCase))
                    throw new EchecEtape($"echec simule a l'etape « {nom} » (repetition generale, SOC-024 T06)");
                var detail = await action();
                r.Etapes.Add(new EtapeJouee(nom, debut, sw.ElapsedMilliseconds, EtatsEtape.Faite, detail));
                o.Suivre?.Invoke($"{DateTime.UtcNow:HH:mm:ss} {nom} : faite — {detail}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                r.Etapes.Add(new EtapeJouee(nom, debut, sw.ElapsedMilliseconds, EtatsEtape.Echec, e.Message));
                o.Suivre?.Invoke($"{DateTime.UtcNow:HH:mm:ss} {nom} : ECHEC — {e.Message}");
                throw new EchecEtape($"{nom} : {e.Message}");
            }
            finally { Ecrire(o, r); }
            // Ce qui se passe dans la V2 pendant le passage : s'il echoue, le passage s'arrete et revient, comme pour une etape.
            if (o.ApresEtape is not null)
                try { await o.ApresEtape(nom, ct); }
                catch (Exception e) when (e is not OperationCanceledException and not EchecEtape)
                {
                    throw new EchecEtape($"apres {nom} : l'activite jouee dans la V2 pendant le passage a echoue : {e.Message}");
                }
        }

        // ---------------------------------------------------------------------------------------------
        // 1. Controles d'entree : lecture seule. Un refus ici laisse tout tel quel.
        // ---------------------------------------------------------------------------------------------
        try
        {
            await Etape(EtapesPassage.ControlesAvant, () => ControlerAvantAsync(o, catalogue, r, etat, ct));
        }
        catch (EchecEtape e)
        {
            return Finir(r, chrono, IssuesPassage.Refuse, e.Message, o);
        }

        // ---------------------------------------------------------------------------------------------
        // 2 a 7. Les etapes qui ecrivent. Un echec : retour a l'etat d'avant, pour ce seul greffe.
        // ---------------------------------------------------------------------------------------------
        try
        {
            await Etape(EtapesPassage.Chaines, async () =>
            {
                etat.V3Ecrite = true;
                await using var v3 = await catalogue.OuvrirBaseV3Async(o.CodeGreffe, ct);
                var ouverture = await new ServiceChaines(o.Coffre).OuvrirAsync(v3, o.CodeGreffe, etat.Fiche!.Fuseau!, o.Auteur, null, ct);
                return $"7 chaines ouvertes le {ouverture.OuverteLe:yyyy-MM-dd HH:mm:ss}, evenement 260 en premier maillon, cle {ouverture.EmpreinteCle}";
            });

            await Etape(EtapesPassage.Transfert, async () =>
            {
                var t = await MoteurTransfert.TransfererAsync(new OptionsTransfert(etat.ConnexionV2!, etat.ConnexionV3!)
                {
                    TailleLot = o.TailleLot,
                    Suivre = e => o.Suivre?.Invoke($"           {e.Genre,-10} {e.Message}"),
                }, ct);
                r.IdTransfert = t.IdTransfert;
                r.LignesTransferees = t.Tables.Sum(l => l.Ecrites ?? 0);
                var rejetees = t.Tables.Sum(l => l.Rejetees ?? 0);
                if (rejetees > 0)
                    r.Ecarts.Add(new EcartPassage("donnees", $"{rejetees} ligne(s) rejetee(s) par le transfert {t.IdTransfert} (rec.Rejet de la base V3)"));
                if (t.CodeSortie != 0)
                    throw new EchecEtape($"transfert {t.IdTransfert} {t.Etat} : {t.Motif}");
                return $"transfert {t.IdTransfert} fini : {t.Tables.Count} tables, {r.LignesTransferees} lignes ecrites, {rejetees} rejetee(s), " +
                       $"{t.Tables.Sum(l => l.LiensCasses ?? 0)} lien(s) casse(s) garde(s), etat fige du {t.InstantaneLe:yyyy-MM-dd HH:mm:ss}";
            });

            await Etape(EtapesPassage.Bascule, async () =>
            {
                var garde = etat.Fiche!.EstBeta ? o.FinBeta!.Value : await HeureServeurAsync(etat.Master!, ct) + TimeSpan.FromDays(1);
                var messages = await ScriptSqlcmd.JouerAsync(etat.Master!, Path.Combine(o.DossierScripts, "bascule.sql"),
                    VariablesScripts(etat, o, garde.ToString(FormatScript, System.Globalization.CultureInfo.InvariantCulture)), ct);
                return (etat.Fiche.EstBeta ? $"greffe beta : retour possible jusqu'au {garde:yyyy-MM-dd HH:mm}. " : $"garde du passage jusqu'au {garde:yyyy-MM-dd HH:mm}. ")
                       + (messages.LastOrDefault() ?? "");
            });

            await Etape(EtapesPassage.ControlesApres, () => ControlerApresAsync(o, r, etat, ct));

            await Etape(EtapesPassage.Catalogue, async () =>
            {
                if (etat.Fiche!.PassageLocal is { } deja)
                    return $"date de passage deja au catalogue : {deja:yyyy-MM-dd HH:mm} (heure locale)";
                var apres = await catalogue.SaisirPassageAsync(o.CodeGreffe, etat.Passage, o.Auteur, null, ct);
                etat.CatalogueEcrit = true;
                return $"passage inscrit : {apres.PassageLocal:yyyy-MM-dd HH:mm} heure locale ({apres.Fuseau})" + (apres.PassageFait ? ", heure atteinte" : "");
            });

            await Etape(EtapesPassage.Aiguillage, async () =>
            {
                var fiche = await catalogue.LireAsync(o.CodeGreffe, ct) ?? throw new GreffeInconnu(o.CodeGreffe);
                var nouvelle = RegleAiguillage.Aiguiller("nouvelle affaire", fiche.PassageLocal, fiche.PassageLocal, fiche.PassageLocal);
                var ancienne = RegleAiguillage.Aiguiller("affaire d'avant", fiche.PassageLocal, fiche.PassageLocal?.AddMinutes(-1), fiche.PassageLocal);
                if (nouvelle.Logiciel != Logiciels.V3 || ancienne.Logiciel != Logiciels.V2)
                    throw new EchecEtape($"aiguillage inattendu : {nouvelle.Motif} / {ancienne.Motif}");
                return $"{nouvelle.Motif} ; {ancienne.Motif}";
            });
        }
        catch (EchecEtape e)
        {
            foreach (var reste in EtapesPassage.Ordre.SkipWhile(n => r.Etapes.Any(x => x.Nom == n)))
                r.Etapes.Add(new EtapeJouee(reste, DateTime.UtcNow, 0, EtatsEtape.NonJouee, ""));
            r.Erreurs.Add(e.Message);
            var issue = await RevenirAsync(o, catalogue, r, etat, ct);
            return Finir(r, chrono, issue, e.Message, o);
        }

        // ---------------------------------------------------------------------------------------------
        // 8. Hors beta : la garde levee. Le greffe est passe ; un echec ici est une reserve, pas un retour.
        // ---------------------------------------------------------------------------------------------
        var debutGarde = DateTime.UtcNow;
        var swGarde = Stopwatch.StartNew();
        if (etat.Fiche!.EstBeta)
            r.Etapes.Add(new EtapeJouee(EtapesPassage.FinDeGarde, debutGarde, 0, EtatsEtape.Faite,
                $"greffe beta : corps d'origine des programmes garde jusqu'a la fin de la beta ({o.FinBeta:yyyy-MM-dd HH:mm}), retour possible (retour)"));
        else
        {
            try
            {
                var m = await ScriptSqlcmd.JouerAsync(etat.Master!, Path.Combine(o.DossierScripts, "fin-de-beta.sql"),
                    new Dictionary<string, string> { ["BaseV2"] = etat.Fiche.BaseV2 }, ct);
                r.Etapes.Add(new EtapeJouee(EtapesPassage.FinDeGarde, debutGarde, swGarde.ElapsedMilliseconds, EtatsEtape.Faite,
                    "hors beta : " + (m.LastOrDefault() ?? "corps d'origine archive")));
            }
            catch (Exception e) when (e is SqlException or InvalidOperationException or IOException)
            {
                r.Etapes.Add(new EtapeJouee(EtapesPassage.FinDeGarde, debutGarde, swGarde.ElapsedMilliseconds, EtatsEtape.Echec, e.Message));
                r.Reserves.Add($"garde des programmes non levee ({e.Message}) : rejouer fin-de-beta.sql avant le {DateTime.Now.AddDays(1):yyyy-MM-dd}, " +
                               "sinon un retour a la V2 resterait possible jusque-la");
            }
        }
        return Finir(r, chrono, IssuesPassage.Passe, null, o);
    }

    /// <summary>
    /// Le retour d'un greffe passe a la V2 (beta, NUM-022) : retour.sql rend la numerotation a la V2 ; la date du
    /// catalogue est retiree si son heure n'est pas atteinte. Une heure atteinte ne change plus (SOC-007 CA05) : les
    /// affaires creees dans la V3 depuis le passage y restent, leur recopie vers la V2 est REC-007 (lot 11).
    /// </summary>
    public static async Task<RapportPassage> RevenirAsync(OptionsPassage o, CancellationToken ct = default)
    {
        var chrono = Stopwatch.StartNew();
        var r = new RapportPassage { Greffe = o.CodeGreffe, DebutUtc = DateTime.UtcNow };
        var catalogue = Catalogue(o);
        FicheGreffe fiche;
        try
        {
            ExigerPoste(o, null);
            fiche = await catalogue.LireAsync(o.CodeGreffe, ct) ?? throw new EchecEtape($"le greffe « {o.CodeGreffe} » n'est pas au catalogue commun");
            Remplir(r, fiche);
            ExigerPoste(o, fiche);
        }
        catch (EchecEtape e)
        {
            r.Erreurs.Add(e.Message);
            return Finir(r, chrono, IssuesPassage.Refuse, e.Message, o);
        }
        var etat = new EtatPassage { Fiche = fiche, Master = catalogue.ConnexionBase(fiche.Serveur, "master") };
        var debut = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();
        try
        {
            var m = await ScriptSqlcmd.JouerAsync(etat.Master, Path.Combine(o.DossierScripts, "retour.sql"), VariablesScripts(etat, o, null), ct);
            r.Etapes.Add(new EtapeJouee(EtapesPassage.Retour, debut, sw.ElapsedMilliseconds, EtatsEtape.Faite, m.LastOrDefault() ?? "retour fait"));
        }
        catch (SqlException e)
        {
            r.Etapes.Add(new EtapeJouee(EtapesPassage.Retour, debut, sw.ElapsedMilliseconds, EtatsEtape.Echec, e.Message));
            r.Erreurs.Add(e.Message);
            return Finir(r, chrono, IssuesPassage.Refuse, e.Message, o);
        }
        if (fiche.PassageLocal is { } p)
        {
            if (!fiche.PassageFait && await RetirerDateAsync(o, fiche, p, ct))
                r.Etapes.Add(new EtapeJouee(EtapesPassage.Catalogue, DateTime.UtcNow, 0, EtatsEtape.Faite,
                    $"date de passage du {p:yyyy-MM-dd HH:mm} retiree du catalogue (heure non atteinte) : les nouvelles affaires restent dans la V2"));
            else
                r.Reserves.Add($"la date de passage du {p:yyyy-MM-dd HH:mm} (heure locale) est atteinte : elle ne change plus (SOC-007 CA05), " +
                               "les affaires creees dans la V3 depuis y restent ; les rendre a la V2 est REC-007 (lot 11), sur decision");
        }
        return Finir(r, chrono, IssuesPassage.RevenuV2, null, o);
    }

    // =================================================================================================
    // Les etapes
    // =================================================================================================

    private sealed class EtatPassage
    {
        public FicheGreffe? Fiche;
        public string? ConnexionV2, ConnexionV3, Master;
        public DateTime Passage;
        public bool V3Ecrite, CatalogueEcrit;
        public List<long> DemandesEnCours = [];
    }

    private static CatalogueGreffes Catalogue(OptionsPassage o) => new(new BaseRig(o.Commune),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CatalogueGreffes.CleAuthentification] = o.Authentification,
        }).Build());

    /// <summary>Sans --exploitation : le catalogue (<paramref name="fiche"/> null, AVANT de s'y connecter), puis l'instance du
    /// greffe (avant toute connexion a ses bases) doivent etre sur ce poste.</summary>
    // fix-ok: relecture du 2026-11-21 -- le catalogue etait LU avant ce controle (une connexion a une base commune distante sans
    // --exploitation), et RevenirAsync laissait l'EchecEtape de ce controle sortir de l'outil sans rapport : les deux refusent ici, avant.
    private static void ExigerPoste(OptionsPassage o, FicheGreffe? fiche)
    {
        if (o.Exploitation) return;
        if (!BaseJetable.EstServeurLocal(o.Commune))
            throw new EchecEtape($"le catalogue ({new SqlConnectionStringBuilder(o.Commune).DataSource}) n'est pas sur ce poste : le passage d'un " +
                                 "greffe se lance chez Amitel, avec --exploitation");
        if (fiche is not null && !BaseJetable.EstServeurLocal($"Server={fiche.Serveur}"))
            throw new EchecEtape($"l'instance du greffe ({fiche.Serveur}) n'est pas sur ce poste : le passage d'un greffe " +
                                 "se lance chez Amitel, avec --exploitation");
    }

    private static void Remplir(RapportPassage r, FicheGreffe f)
    {
        r.Nom = f.Nom; r.EstBeta = f.EstBeta; r.EstOutreMer = f.EstOutreMer; r.Fuseau = f.Fuseau;
        r.Serveur = f.Serveur; r.BaseV2 = f.BaseV2; r.BaseV3 = f.BaseV3;
    }

    private static async Task<string> ControlerAvantAsync(OptionsPassage o, CatalogueGreffes catalogue, RapportPassage r, EtatPassage etat,
                                                          CancellationToken ct)
    {
        ExigerPoste(o, null);
        var fiche = await catalogue.LireAsync(o.CodeGreffe, ct)
                    ?? throw new EchecEtape($"le greffe « {o.CodeGreffe} » n'est pas au catalogue commun");
        etat.Fiche = fiche;
        Remplir(r, fiche);
        ExigerPoste(o, fiche);
        if (fiche.Fuseau is null)
            throw new EchecEtape($"le greffe {fiche.Code} n'a pas de fuseau : l'heure de son passage ne se lit pas (SOC-007 T04)");
        if (fiche.PassageFait)
            throw new EchecEtape($"le greffe {fiche.Code} est deja passe le {fiche.PassageLocal:yyyy-MM-dd HH:mm} (heure locale)");
        var demandee = o.PassageLocal is { } pl ? new DateTime(pl.Year, pl.Month, pl.Day, pl.Hour, pl.Minute, 0) : (DateTime?)null;
        if (fiche.PassageLocal is { } prevu && demandee is { } d && prevu != d)
            throw new EchecEtape($"le catalogue porte deja le passage du {prevu:yyyy-MM-dd HH:mm}, pas celui du {d:yyyy-MM-dd HH:mm} demande");
        var maintenant = Fuseaux.MaintenantLocal(fiche.Fuseau);
        etat.Passage = fiche.PassageLocal ?? demandee ?? new DateTime(maintenant.Year, maintenant.Month, maintenant.Day, maintenant.Hour, maintenant.Minute, 0);
        r.PassageLocal = etat.Passage;
        r.PassageUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(etat.Passage, DateTimeKind.Unspecified),
                                                     TimeZoneInfo.FindSystemTimeZoneById(fiche.Fuseau));
        if (fiche.EstBeta && o.FinBeta is null)
            throw new EchecEtape($"le greffe {fiche.Code} est beta : donner la fin de sa beta (--fin-beta), jusqu'a laquelle le retour reste possible");
        if (!fiche.EstBeta && o.FinBeta is not null)
            throw new EchecEtape($"le greffe {fiche.Code} n'est pas beta : il n'a pas de fin de beta (pas de retour apres son passage, NUM-022 CA03)");

        etat.ConnexionV2 = catalogue.ConnexionBase(fiche.Serveur, fiche.BaseV2);
        etat.ConnexionV3 = catalogue.ConnexionBase(fiche.Serveur, fiche.BaseV3);
        etat.Master = catalogue.ConnexionBase(fiche.Serveur, "master");
        if (fiche.EstBeta && o.FinBeta <= await HeureServeurAsync(etat.Master, ct))
            throw new EchecEtape($"la fin de beta {o.FinBeta:yyyy-MM-dd HH:mm} est deja passee a l'heure du serveur");

        // La base V3 : a jour des migrations dont le passage depend, et neuve.
        await using (var v3 = await catalogue.OuvrirBaseV3Async(fiche.Code, ct))
        {
            var manque = new List<string>();
            foreach (var (objet, migration) in new[] { ("soc.Certification", "SOC-018"), ("rec.Transfert", "REC-001"), ("num.Reprendre", "NUM-005"),
                                                        ("num.Attribuer", "NUM-001"), ("soc.Compte", "SOC-001") })
                if (await ScalaireAsync<int?>(v3, $"SELECT OBJECT_ID(N'{objet}');", ct) is null) manque.Add($"{objet} ({migration})");
            if (manque.Count > 0)
                throw new EchecEtape($"la base V3 {fiche.BaseV3} n'est pas a jour des migrations : manque {string.Join(", ", manque)}");
            var transferts = await ScalaireAsync<int>(v3, "SELECT COUNT(*) FROM rec.Transfert;", ct);
            var ouvertes = await ScalaireAsync<int>(v3, "SELECT COUNT(*) FROM soc.Certification;", ct);
            if (transferts > 0 || ouvertes > 0)
                throw new EchecEtape($"la base V3 {fiche.BaseV3} a deja servi ({transferts} transfert(s), chaines {(ouvertes > 0 ? "ouvertes" : "fermees")}) : " +
                                     "la recreer (RigV3.Migrations --creer) avant le passage");
        }

        // La base V2 : en lecture seule. Programmes d'origine, demandes en cours relevees (CA04).
        await using (var v2 = await catalogue.OuvrirBaseV2Async(fiche.Code, ct))
        {
            var programmes = Convert.ToInt32(await LectureV2.ValeurAsync(v2,
                "SELECT COUNT(*) FROM sys.objects WHERE schema_id = SCHEMA_ID(N'dbo') AND type = 'P' " +
                "AND name IN (N'GENERER_NUMERO', N'CHERCHER_NUMERO', N'RESTITUER_NUMERO')", ct));
            if (programmes != 3)
                throw new EchecEtape($"{fiche.BaseV2} n'a pas ses trois programmes de numerotation ({programmes} sur 3)");
            if (await ProgrammesBasculesAsync(v2, ct) > 0)
                throw new EchecEtape($"les programmes de numerotation de {fiche.BaseV2} demandent deja leurs numeros a la V3");
            etat.DemandesEnCours = await DemandesEnCoursAsync(v2, ct);
        }
        return $"{fiche.Nom} ({fiche.Code}, {(fiche.EstBeta ? "beta" : fiche.EstOutreMer ? "outre-mer" : "metropole")}, {fiche.Fuseau}) : " +
               $"{fiche.BaseV2} et {fiche.BaseV3} sur {fiche.Serveur}, base V3 neuve et a jour, programmes d'origine, " +
               $"{etat.DemandesEnCours.Count} demande(s) en cours relevee(s) ; passage a {etat.Passage:yyyy-MM-dd HH:mm} heure locale ({r.PassageUtc:yyyy-MM-dd HH:mm} UTC)";
    }

    private static async Task<string> ControlerApresAsync(OptionsPassage o, RapportPassage r, EtatPassage etat, CancellationToken ct)
    {
        var fiche = etat.Fiche!;
        var problemes = new List<string>();
        await using (var v3 = new SqlConnection(etat.ConnexionV3))
        {
            await v3.OpenAsync(ct);
            if (await ScalaireAsync<string?>(v3, "SELECT Etat FROM rec.Transfert WHERE Id = (SELECT MAX(Id) FROM rec.Transfert);", ct) != "fini")
                problemes.Add("le transfert n'est pas fini");
            var premier = await ScalaireAsync<short?>(v3, "SELECT TOP (1) Code FROM soc.EvenementTechnique WHERE Chaine = 'JNP' ORDER BY Seq;", ct);
            if (premier != ServiceChaines.CodeInitialisation)
                problemes.Add($"le premier maillon du journal non purgeable est {premier?.ToString() ?? "absent"}, pas l'evenement 260");
            var ecarts = await ScalaireAsync<int>(v3, "SELECT COUNT(*) FROM num.EcartsV2 WHERE Ecart <> 'compteur V3 absent de la V2';", ct);
            if (ecarts > 0)
            {
                r.Ecarts.Add(new EcartPassage("numeros", $"{ecarts} compteur(s) differents entre {fiche.BaseV2} et {fiche.BaseV3} (num.EcartsV2)"));
                problemes.Add($"{ecarts} ecart(s) de compteurs");
            }
        }
        await using (var v2 = new SqlConnection(new SqlConnectionStringBuilder(etat.ConnexionV2) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString))
        {
            await v2.OpenAsync(ct);
            var bascules = await ProgrammesBasculesAsync(v2, ct);
            if (bascules != 3) problemes.Add($"{bascules} programme(s) de numerotation sur 3 demandent leurs numeros a la V3");
            var pont = Convert.ToString(await LectureV2.ValeurAsync(v2,
                "SELECT BaseV3 FROM rigv3.Pont WHERE Id = 1 AND RetourLe IS NULL", ct));
            if (!string.Equals(pont, fiche.BaseV3, StringComparison.OrdinalIgnoreCase))
                problemes.Add($"le pont de {fiche.BaseV2} vise « {pont} », pas {fiche.BaseV3}");
            // Par paquets de 1000 identifiants : on recompte celles d'avant, sans relire toute la table DEMANDE d'une vraie base.
            var presentes = 0;
            foreach (var paquet in etat.DemandesEnCours.Chunk(1000))
                presentes += Convert.ToInt32(await LectureV2.ValeurAsync(v2,
                    $"SELECT COUNT(*) FROM dbo.DEMANDE WHERE DMND_ID_DMND IN ({string.Join(",", paquet)})", ct));
            var perdues = etat.DemandesEnCours.Count - presentes;
            if (perdues > 0)
            {
                r.Ecarts.Add(new EcartPassage("demandes", $"{perdues} demande(s) en cours avant le passage absente(s) de {fiche.BaseV2}"));
                problemes.Add($"{perdues} demande(s) en cours perdue(s)");
            }
        }
        if (problemes.Count > 0) throw new EchecEtape(string.Join(" ; ", problemes));
        return $"transfert fini, evenement 260 en premier maillon, compteurs de {fiche.BaseV2} egaux a ceux de {fiche.BaseV3}, 3 programmes remplaces, " +
               $"les {etat.DemandesEnCours.Count} demande(s) en cours d'avant toujours la";
    }

    /// <summary>Le retour a l'etat d'avant apres un echec (SOC-024 CA03) ; rend l'issue.</summary>
    private static async Task<string> RevenirAsync(OptionsPassage o, CatalogueGreffes catalogue, RapportPassage r, EtatPassage etat, CancellationToken ct)
    {
        var fiche = etat.Fiche!;
        r.BaseV3ARecreer = etat.V3Ecrite;
        var debut = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();
        var faits = new List<string>();
        try
        {
            // La bascule se juge sur la base, pas sur l'etape : un echec apres son COMMIT la laisse en service.
            int bascules;
            await using (var v2 = new SqlConnection(new SqlConnectionStringBuilder(etat.ConnexionV2) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString))
            {
                await v2.OpenAsync(ct);
                bascules = await ProgrammesBasculesAsync(v2, ct);
            }
            if (bascules > 0)
            {
                var m = await ScriptSqlcmd.JouerAsync(etat.Master!, Path.Combine(o.DossierScripts, "retour.sql"), VariablesScripts(etat, o, null), ct);
                faits.Add(m.LastOrDefault() ?? "retour.sql joue");
            }
            if (etat.CatalogueEcrit)
            {
                if (!await RetirerDateAsync(o, fiche, etat.Passage, ct))
                    throw new EchecEtape($"la date de passage du {etat.Passage:yyyy-MM-dd HH:mm} est atteinte : le catalogue ne la rend plus (SOC-007 CA05)");
                faits.Add("date de passage retiree du catalogue");
            }

            // La V2 est-elle revenue a l'identique ? Programmes d'origine, catalogue sans passage.
            await using (var v2 = new SqlConnection(new SqlConnectionStringBuilder(etat.ConnexionV2) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString))
            {
                await v2.OpenAsync(ct);
                if (await ProgrammesBasculesAsync(v2, ct) > 0) throw new EchecEtape("des programmes de la V2 demandent encore leurs numeros a la V3");
            }
            var relue = await catalogue.LireAsync(fiche.Code, ct);
            if (relue?.PassageLocal != fiche.PassageLocal) throw new EchecEtape("la date de passage du catalogue n'est pas revenue a celle d'avant");
            faits.Add($"{fiche.BaseV2} numerote seule avec ses programmes d'origine, catalogue sans passage : le greffe reste entierement sur la V2");
            if (etat.V3Ecrite) faits.Add($"base V3 {fiche.BaseV3} a recreer avant de repasser (chaines ouvertes ou transfert commence)");
            r.Etapes.Add(new EtapeJouee(EtapesPassage.Retour, debut, sw.ElapsedMilliseconds, EtatsEtape.Faite, string.Join(" ; ", faits)));
            return IssuesPassage.RevenuV2;
        }
        catch (Exception e) when (e is SqlException or EchecEtape or InvalidOperationException or IOException)
        {
            r.Etapes.Add(new EtapeJouee(EtapesPassage.Retour, debut, sw.ElapsedMilliseconds, EtatsEtape.Echec, e.Message));
            r.Erreurs.Add($"retour impossible : {e.Message}");
            return IssuesPassage.EchecSansRetour;
        }
    }

    /// <summary>
    /// Retire une date de passage dont l'heure n'est pas atteinte. CatalogueGreffes n'a pas ce geste : l'UPDATE est
    /// garde par le declencheur soc.TR_Greffe_Regles (heure atteinte : refus 51703 ; auteur et date obligatoires ;
    /// historique ecrit). Rend faux si l'heure est atteinte.
    /// </summary>
    private static async Task<bool> RetirerDateAsync(OptionsPassage o, FicheGreffe fiche, DateTime passage, CancellationToken ct)
    {
        await using var cn = new SqlConnection(o.Commune);
        await cn.OpenAsync(ct);
        try
        {
            await using var cmd = new SqlCommand(
                "UPDATE soc.Greffe SET PassageLocal = NULL, ModifiePar = @a, ModifieLe = SYSDATETIME() WHERE CodeGreffe = @c AND PassageLocal = @p;", cn);
            cmd.Parameters.Add(new SqlParameter("@a", SqlDbType.VarChar, 128) { Value = o.Auteur });
            cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.VarChar, 20) { Value = fiche.Code });
            cmd.Parameters.Add(new SqlParameter("@p", SqlDbType.DateTime2) { Value = passage });
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }
        catch (SqlException e) when (e.Number == 51703)
        {
            return false;
        }
    }

    private static Dictionary<string, string> VariablesScripts(EtatPassage etat, OptionsPassage o, string? finBeta) => new()
    {
        ["BaseV2"] = etat.Fiche!.BaseV2,
        ["BaseV3"] = etat.Fiche.BaseV3,
        ["CodeGreffe"] = etat.Fiche.Code,
        ["Reprise"] = "faite",
        ["FinBeta"] = finBeta ?? "non",
        ["SessionEchecs"] = o.SessionEchecs,
    };

    /// <summary>Combien des trois programmes de la V2 demandent leurs numeros a la V3 (corps qui nomme rigv3.).</summary>
    private static async Task<int> ProgrammesBasculesAsync(SqlConnection v2, CancellationToken ct)
        => Convert.ToInt32(await LectureV2.ValeurAsync(v2,
            "SELECT COUNT(*) FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
            "WHERE o.schema_id = SCHEMA_ID(N'dbo') AND o.name IN (N'GENERER_NUMERO', N'CHERCHER_NUMERO', N'RESTITUER_NUMERO') " +
            "AND m.definition LIKE N'%rigv3.%'", ct));

    /// <summary>Les demandes en cours de la V2 (DMND_EN_COURS = 1) : celles qu'aucun passage ne doit perdre (SOC-024 CA04).</summary>
    private static async Task<List<long>> DemandesEnCoursAsync(SqlConnection v2, CancellationToken ct)
    {
        var ids = new List<long>();
        await using var l = await LectureV2.LireAsync(v2, "SELECT CAST(DMND_ID_DMND AS bigint) FROM dbo.DEMANDE WHERE DMND_EN_COURS = 1", ct);
        while (await l.ReadAsync(ct)) ids.Add(l.GetInt64(0));
        return ids;
    }

    private static async Task<DateTime> HeureServeurAsync(string master, CancellationToken ct)
    {
        await using var cn = new SqlConnection(master);
        await cn.OpenAsync(ct);
        return await ScalaireAsync<DateTime>(cn, "SELECT CAST(SYSDATETIME() AS datetime2(0));", ct);
    }

    private static async Task<T> ScalaireAsync<T>(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        var v = await cmd.ExecuteScalarAsync(ct);
        if (v is null or DBNull) return default!;
        var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)(t.IsInstanceOfType(v) ? v : Convert.ChangeType(v, t, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static RapportPassage Finir(RapportPassage r, Stopwatch chrono, string issue, string? motif, OptionsPassage o)
    {
        r.Issue = issue;
        r.Motif = motif;
        r.FinUtc = DateTime.UtcNow;
        r.DureeMs = chrono.ElapsedMilliseconds;
        Ecrire(o, r);
        return r;
    }

    private static void Ecrire(OptionsPassage o, RapportPassage r)
    {
        if (o.FichierRapport is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.FichierRapport))!);
        File.WriteAllText(o.FichierRapport, r.EnJson());
    }
}
