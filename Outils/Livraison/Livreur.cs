using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Livraison;

// ============================================================
// LA LIVRAISON D'UNE VERSION, VAGUE PAR VAGUE (SOC-009 T02, T03, T04 ; lot 12, 2026-11-21).
//
// Pour chaque greffe de la vague choisie (catalogue commun, CatalogueGreffes : la seule
// facon de trouver la base d'un greffe), l'un apres l'autre, par code :
//
//   1. CONTROLE D'ECART AVANT (CA03). La base doit etre exactement la version qu'elle dit
//      porter : son schema est compare a celui inscrit a sa derniere livraison
//      (rigv3.SchemaLivre), et son journal des migrations a celui du paquet (une migration
//      jouee ici et absente du paquet = un objet que la version livree n'a pas). Un ecart
//      est liste, objet par objet, et la livraison S'ARRETE sur cette base, avant d'y
//      toucher. Une base plus recente que le paquet est refusee de meme.
//   2. INSTANTANE de la base (CREATE DATABASE ... AS SNAPSHOT OF) : son etat d'avant.
//   3. MIGRATIONS du paquet qui n'y sont pas encore (Migrateur, chacune dans sa transaction).
//   4. CONTROLE D'ECART APRES (CA03) : le schema doit etre celui que le paquet a mesure.
//   5. VERSION INSCRITE (CA01, rigv3.Livraison + rigv3.SchemaLivre) et EVENEMENT 250 / 255
//      (CA06, soc.EvenementTechnique), dans une seule transaction.
//   Un echec en 3, 4 ou 5 (CA04) : la base REVIENT a l'instantane -- les migrations deja
//   validees de cette tentative comprises --, l'echec s'inscrit au journal APRES le retour,
//   et la livraison s'arrete : les greffes suivants de la vague ne sont pas touches.
//   L'instantane est supprime dans tous les cas.
//
// Pourquoi un instantane et pas une transaction unique : le Migrateur valide chaque
// migration separement (un script peut contenir ce qu'une transaction refuse), et le
// retour doit couvrir TOUT ce que la tentative a fait. Pourquoi pas une sauvegarde : un
// instantane ne copie que les pages modifiees (secondes, pas une copie de la base entiere)
// et ne laisse aucun fichier. Disponible dans toutes les editions depuis SQL Server 2016
// SP1, donc sur les instances V3 (2017 au moins, SOC-008 CA05). Essaye le 2026-11-21 sur le
// SQL Server de developpement (2022, Linux) ; limites : docs/reconstruction/socle-exploitation.md.
// ============================================================

/// <summary>Ce qui est arrive a une base pendant la livraison.</summary>
public static class Resultats
{
    /// <summary>La version est livree et inscrite.</summary>
    public const string Livree = "livree";
    /// <summary>La base portait deja cette version (meme paquet) : rien n'a ete fait.</summary>
    public const string DejaAJour = "deja a jour";
    /// <summary>Controle seul : la base est conforme et recevrait la version.</summary>
    public const string ALivrer = "a livrer";
    /// <summary>Ecart de schema ou de version : la livraison s'arrete sur cette base.</summary>
    public const string Ecart = "ecart";
    /// <summary>Echec d'une migration ou de l'inscription : base revenue a sa version precedente.</summary>
    public const string Echec = "echec";
    /// <summary>Apres l'arret : la base n'a pas ete touchee.</summary>
    public const string NonTouchee = "non touchee";
}

/// <summary>Une base de la vague dans le rapport.</summary>
/// <param name="Ecarts">« en trop : … » / « manquant : … », objet par objet (CA03).</param>
/// <param name="Revenue">Vrai si la base est revenue a son instantane (CA04).</param>
public sealed record EtatBase(string Greffe, string Nom, string Serveur, string Base, string Resultat,
                              string? VersionAvant, string? VersionApres, IReadOnlyList<string> Ecarts,
                              string? Erreur, bool Revenue, IReadOnlyList<string> Jouees);

/// <summary>Le rapport d'une livraison (ou d'un controle) sur une vague.</summary>
/// <param name="ArretSur">Le greffe sur lequel la livraison s'est arretee ; null si elle est allee au bout.</param>
public sealed record RapportLivraison(string Version, string Paquet, string Vague, bool ControleSeul, DateTime Debut, DateTime Fin,
                                      IReadOnlyList<EtatBase> Bases, string? ArretSur)
{
    public bool Reussie => ArretSur is null;

    /// <summary>Le rapport lisible, une ligne par base puis le detail des ecarts et des echecs.</summary>
    public string Texte()
    {
        var t = new StringBuilder();
        t.AppendLine($"{(ControleSeul ? "Controle" : "Livraison")} de la version {Version} (paquet {Paquet[..12]}), vague « {Vague} », " +
                     $"{Bases.Count} base(s), du {Debut:dd/MM/yyyy HH:mm:ss} au {Fin:HH:mm:ss} UTC.");
        foreach (var b in Bases)
            t.AppendLine($"  {b.Greffe,-6} {b.Base,-28} {b.Resultat,-12} {b.VersionAvant ?? "(aucune)"} -> {b.VersionApres ?? "(inchangee)"}" +
                         (b.Revenue ? "  [revenue a sa version precedente]" : ""));
        foreach (var b in Bases.Where(b => b.Ecarts.Count > 0 || b.Erreur is not null))
        {
            t.AppendLine($"{b.Greffe} ({b.Base} sur {b.Serveur}) :");
            if (b.Erreur is not null) t.AppendLine($"  {b.Erreur}");
            foreach (var e in b.Ecarts) t.AppendLine($"  {e}");
        }
        t.AppendLine(Reussie
            ? "Vague terminee : aucune base en ecart ni en echec."
            : $"ARRET sur le greffe {ArretSur} : les bases suivantes de la vague n'ont pas ete touchees.");
        return t.ToString();
    }
}

public sealed class Livreur(CatalogueGreffes catalogue, Action<string>? journal = null)
{
    /// <summary>Livre <paramref name="paquet"/> a la vague <paramref name="vague"/> ; avec
    /// <paramref name="controleSeul"/>, ne fait que le controle d'ecart (rien n'est ecrit).</summary>
    public async Task<RapportLivraison> LivrerAsync(PaquetLivraison paquet, Vague vague, bool controleSeul = false,
                                                    CancellationToken ct = default)
    {
        var debut = DateTime.UtcNow;
        var greffes = Vagues.Greffes(vague, await catalogue.ListerAsync(ct));
        var etats = new List<EtatBase>();
        string? arret = null;
        foreach (var g in greffes)
        {
            if (arret is not null)
            {
                etats.Add(new EtatBase(g.Code, g.Nom, g.Serveur, g.BaseV3, Resultats.NonTouchee, null, null, [], null, false, []));
                continue;
            }
            journal?.Invoke($"{g.Code} {g.BaseV3} sur {g.Serveur} ...");
            var etat = await LivrerBaseAsync(paquet, vague, g, controleSeul, ct);
            journal?.Invoke($"{g.Code} : {etat.Resultat}" + (etat.Erreur is null ? "" : $" ({etat.Erreur})"));
            etats.Add(etat);
            if (etat.Resultat is Resultats.Ecart or Resultats.Echec) arret = g.Code;
        }
        return new RapportLivraison(paquet.Version, paquet.Empreinte, Vagues.Libelle(vague), controleSeul, debut, DateTime.UtcNow, etats, arret);
    }

    private async Task<EtatBase> LivrerBaseAsync(PaquetLivraison paquet, Vague vague, FicheGreffe g, bool controleSeul, CancellationToken ct)
    {
        var debut = DateTime.UtcNow;
        EtatBase Etat(string resultat, string? avant, string? apres, IReadOnlyList<string>? ecarts = null, string? erreur = null,
                      bool revenue = false, IReadOnlyList<string>? jouees = null)
            => new(g.Code, g.Nom, g.Serveur, g.BaseV3, resultat, avant, apres, ecarts ?? [], erreur, revenue, jouees ?? []);

        var connexionMaster = catalogue.ConnexionBase(g.Serveur, "master");
        var connexionBase = catalogue.ConnexionBase(g.Serveur, g.BaseV3);
        try
        {
            await using (var master = await OuvrirAsync(connexionMaster, ct))
                if (await ScalaireAsync(master, "SELECT DB_ID(@b);", ct, ("@b", g.BaseV3)) is null or DBNull)
                    return Etat(Resultats.Echec, null, null, erreur:
                        $"la base {g.BaseV3} n'existe pas sur {g.Serveur} : la creer d'abord (SOC-008, BaseGreffe.CreerAsync).");
        }
        catch (SqlException e)
        {
            return Etat(Resultats.Echec, null, null, erreur: $"instance {g.Serveur} injoignable : {e.Message}");
        }

        // --- 1. Controle d'ecart AVANT -------------------------------------------------
        LivraisonInscrite? derniere;
        IReadOnlyList<string> schemaInscrit;
        IReadOnlyList<string> jouees;
        await using (var cn = await OuvrirAsync(connexionBase, ct))
        {
            if (!controleSeul) await JournalLivraisons.CreerAsync(cn, ct);
            derniere = await JournalLivraisons.DerniereReussieAsync(cn, null, ct);
            schemaInscrit = await JournalLivraisons.SchemaLivreAsync(cn, ct);
        }
        jouees = await MigrationsJoueesAsync(connexionBase, ct);
        var avant = derniere?.Version;

        if (avant is not null && VersionV3.Comparer(paquet.Version, avant) < 0)
            return await RefuserAsync(Etat(Resultats.Ecart, avant, null, erreur:
                $"la base porte deja la version {avant}, plus recente que le paquet {paquet.Version} : une version ne se livre pas a rebours."));
        if (avant is not null && VersionV3.Egales(paquet.Version, avant))
        {
            if (!string.Equals(derniere!.Paquet, paquet.Empreinte, StringComparison.OrdinalIgnoreCase))
                return await RefuserAsync(Etat(Resultats.Ecart, avant, null, erreur:
                    $"la version {avant} a deja ete livree ici avec un autre contenu (paquet {derniere.Paquet[..12]}, " +
                    $"celui-ci {paquet.Empreinte[..12]}) : un paquet change de contenu = un nouveau numero."));
            var ecartsMeme = await EcartsAsync(connexionBase, paquet.Manifeste.Schema, paquet.Manifeste.Collation, ct);
            return ecartsMeme.Count == 0
                ? Etat(Resultats.DejaAJour, avant, avant)
                : await RefuserAsync(Etat(Resultats.Ecart, avant, null, ecartsMeme,
                    $"la base dit porter la version {avant} mais s'en ecarte ({ecartsMeme.Count} objet(s))."));
        }

        var ecarts = new List<string>();
        var parPaquet = paquet.Manifeste.Migrations.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        ecarts.AddRange(jouees.Where(id => !parPaquet.Contains(id)).Select(id => $"migration en trop : {id} (jouee sur cette base, absente de la version {paquet.Version})"));
        if (schemaInscrit.Count > 0)
            ecarts.AddRange(await EcartsAsync(connexionBase, schemaInscrit, paquet.Manifeste.Collation, ct));
        if (ecarts.Count > 0)
            return await RefuserAsync(Etat(Resultats.Ecart, avant, null, ecarts,
                $"controle avant livraison : la base s'ecarte de sa version {avant ?? "(aucune livraison inscrite)"} ({ecarts.Count} objet(s)) ; elle n'a pas ete touchee."));
        if (controleSeul) return Etat(Resultats.ALivrer, avant, paquet.Version);

        // --- 2 a 5. Instantane, migrations, controle apres, inscription ------------------------------
        string instantane;
        try { instantane = await CreerInstantaneAsync(connexionMaster, g.BaseV3, ct); }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            return await RefuserAsync(Etat(Resultats.Echec, avant, null, erreur: $"instantane impossible, base non touchee : {e.Message}"));
        }

        var faites = new List<string>();
        try
        {
            var rapport = await Migrateur.AppliquerAsync(connexionBase, paquet.Migrations, l => journal?.Invoke($"  {l}"), ct);
            faites.AddRange(rapport.Jouees);
            var apres = await EcartsAsync(connexionBase, paquet.Manifeste.Schema, paquet.Manifeste.Collation, ct);
            if (apres.Count > 0) throw new EcartApres(apres);

            var schema = await BaseGreffe.SchemaAsync(connexionBase, ct);
            await using var cn = await OuvrirAsync(connexionBase, ct);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            await JournalLivraisons.InscrireAsync(cn, tx, paquet.Version, Vagues.Code(vague), paquet.Empreinte, debut, JournalLivraisons.Livree,
                $"{faites.Count} migration(s) jouee(s)", schema, ct);
            await EcrireEvenementAsync(cn, tx, paquet, vague, avant, ct);
            await tx.CommitAsync(ct);
            return Etat(Resultats.Livree, avant, paquet.Version, jouees: faites);
        }
        catch (Exception e) when (e is SqlException or ErreurMigration or EcartApres or InvalidOperationException)
        {
            // CA04 : tout ce que la tentative a fait est defait, migrations validees comprises.
            var ecart = e as EcartApres;
            var cause = (ecart is null ? "mise a jour en echec" : "controle apres livraison en ecart") + $" : {e.Message}";
            // Le Migrateur ne rend son rapport qu'en cas de succes : ce que la tentative a valide se lit au journal,
            // AVANT le retour (qui l'efface), pour que le rapport dise ce qui a ete defait.
            try { faites = [.. (await MigrationsJoueesAsync(connexionBase, CancellationToken.None)).Except(jouees)]; }
            catch (SqlException) { /* base muette : le rapport garde la cause principale, ci-dessous */ }
            try { await RevenirAsync(connexionMaster, g.BaseV3, instantane); }
            catch (SqlException retour)
            {
                // Le pire cas, dit tel quel : la base n'a pas pu revenir. L'instantane est garde pour l'exploitant.
                instantane = "";
                return Etat(Resultats.Echec, avant, null, ecart?.Ecarts,
                    $"{cause} ; RETOUR IMPOSSIBLE ({retour.Message}) : la base est dans un etat intermediaire, " +
                    "l'instantane de livraison est garde pour un retour a la main (RESTORE DATABASE ... FROM DATABASE_SNAPSHOT).",
                    jouees: faites);
            }
            return await RefuserAsync(Etat(ecart is null ? Resultats.Echec : Resultats.Ecart, avant, null, ecart?.Ecarts,
                $"{cause} ; base revenue a sa version {avant ?? "precedente"}", revenue: true, jouees: faites));
        }
        finally
        {
            if (instantane.Length > 0) await SupprimerInstantaneAsync(connexionMaster, instantane);
        }

        // Inscrit au journal de la base un refus ou un echec (apres le retour : il y reste).
        async Task<EtatBase> RefuserAsync(EtatBase etat)
        {
            if (controleSeul) return etat;
            try
            {
                await using var cn = await OuvrirAsync(connexionBase, ct);
                await JournalLivraisons.CreerAsync(cn, ct);
                var detail = etat.Erreur + (etat.Ecarts.Count > 0 ? " | " + string.Join(" | ", etat.Ecarts.Take(20)) : "");
                await JournalLivraisons.InscrireAsync(cn, null, paquet.Version, Vagues.Code(vague), paquet.Empreinte, debut,
                    etat.Resultat == Resultats.Echec ? JournalLivraisons.Echec : JournalLivraisons.Ecart, detail, null, ct);
            }
            catch (SqlException e)
            {
                // Le rapport garde l'etat ; la base ne peut pas l'inscrire (elle ne repond plus) : on le dit.
                return etat with { Erreur = etat.Erreur + $" (journal de la base non ecrit : {e.Message})" };
            }
            return etat;
        }
    }

    /// <summary>
    /// Le controle d'ecart (CA03) : ce que la base porte en trop et ce qui lui manque, par rapport au schema
    /// <paramref name="attendu"/>. Le classement de la base est ramene a celui du schema attendu avant de comparer
    /// (une base V3 prend celui de sa base V2, SOC-008 : on compare « au meme classement »).
    /// </summary>
    public static async Task<IReadOnlyList<string>> EcartsAsync(string connexionBase, IReadOnlyList<string> attendu,
                                                                string collationAttendue, CancellationToken ct = default)
    {
        var schema = await BaseGreffe.SchemaAsync(connexionBase, ct);
        var collation = schema.FirstOrDefault(l => l.StartsWith("classement de la base ", StringComparison.Ordinal))?["classement de la base ".Length..];
        if (collation is not null && collation != collationAttendue)
            schema = [.. schema.Select(l => l.Replace(collation, collationAttendue, StringComparison.Ordinal))];
        return [.. schema.Except(attendu, StringComparer.Ordinal).Select(l => "en trop : " + l)
                   .Concat(attendu.Except(schema, StringComparer.Ordinal).Select(l => "manquant : " + l))];
    }

    // --- Evenement d'installation (CA06) ------------------------------------------------------------

    private static async Task EcrireEvenementAsync(SqlConnection cn, SqlTransaction tx, PaquetLivraison paquet, Vague vague,
                                                   string? avant, CancellationToken ct)
    {
        var majeure = VersionV3.EstMajeure(avant, paquet.Version);
        await using var cmd = new SqlCommand(
            "INSERT INTO soc.EvenementTechnique (Code, Libelle, Detail, IdAuteur, Auteur) " +
            "VALUES (@code, @libelle, @detail, NULL, LEFT(N'Livraison ' + SUSER_SNAME(), 80));", cn, tx);
        cmd.Parameters.Add(new SqlParameter("@code", SqlDbType.SmallInt) { Value = majeure ? VersionV3.CodeMajeure : VersionV3.CodeMineure });
        cmd.Parameters.Add(new SqlParameter("@libelle", SqlDbType.VarChar, 120) { Value = majeure ? VersionV3.LibelleMajeure : VersionV3.LibelleMineure });
        cmd.Parameters.Add(new SqlParameter("@detail", SqlDbType.NVarChar, 1000)
        {
            Value = $"Version {paquet.Version} livree (vague {Vagues.Libelle(vague)}, version precedente {avant ?? "aucune"}, " +
                    $"paquet {paquet.Empreinte[..12]})",
        });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // --- Instantane (CA04) ---------------------------------------------------------------------------

    /// <summary>Cree l'instantane de livraison de la base ; refuse s'il en reste un (un retour exige d'etre le seul).</summary>
    private static async Task<string> CreerInstantaneAsync(string connexionMaster, string nomBase, CancellationToken ct)
    {
        await using var master = await OuvrirAsync(connexionMaster, ct);
        await using (var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE source_database_id = DB_ID(@b);", master))
        {
            cmd.Parameters.AddWithValue("@b", nomBase);
            if (await cmd.ExecuteScalarAsync(ct) is string reste)
                throw new InvalidOperationException(
                    $"un instantane de {nomBase} existe deja ({reste}) : une livraison precedente s'est interrompue. " +
                    "Le supprimer (DROP DATABASE) apres avoir verifie l'etat de la base, puis relancer.");
        }
        var nom = $"{nomBase}_livraison_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var fichiers = new List<string>();
        await using (var cmd = new SqlCommand("SELECT name, physical_name FROM sys.master_files WHERE database_id = DB_ID(@b) AND type = 0;", master))
        {
            cmd.Parameters.AddWithValue("@b", nomBase);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                fichiers.Add($"(NAME = N'{Litteral(r.GetString(0))}', FILENAME = N'{Litteral(r.GetString(1))}.{Litteral(nom)}.ss')");
        }
        await ExecuterAsync(master, $"CREATE DATABASE {Crochets(nom)} ON {string.Join(", ", fichiers)} AS SNAPSHOT OF {Crochets(nomBase)};", ct);
        return nom;
    }

    private static async Task RevenirAsync(string connexionMaster, string nomBase, string instantane)
    {
        SqlConnection.ClearAllPools();   // les connexions du pool tiennent la base : on les lache
        await using var master = await OuvrirAsync(connexionMaster, CancellationToken.None);
        await ExecuterAsync(master, $"ALTER DATABASE {Crochets(nomBase)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;", CancellationToken.None);
        try
        {
            await ExecuterAsync(master, $"RESTORE DATABASE {Crochets(nomBase)} FROM DATABASE_SNAPSHOT = N'{Litteral(instantane)}';",
                CancellationToken.None);
        }
        finally
        {
            // Retour reussi ou non, la base ne reste pas reservee a cette connexion.
            await ExecuterAsync(master, $"ALTER DATABASE {Crochets(nomBase)} SET MULTI_USER;", CancellationToken.None);
        }
    }

    private static async Task SupprimerInstantaneAsync(string connexionMaster, string instantane)
    {
        await using var master = await OuvrirAsync(connexionMaster, CancellationToken.None);
        await ExecuterAsync(master, $"IF DB_ID(N'{Litteral(instantane)}') IS NOT NULL DROP DATABASE {Crochets(instantane)};", CancellationToken.None);
    }

    // --- Outils SQL ------------------------------------------------------------------------------------

    // fix-ok: mesure au premier banc (2026-11-21) -- Migrateur.JoueesAsync leve « Invalid object name 'rigv3.Migration' »
    // sur une base VIDE (premiere livraison, SOC-009 T05) : il lit son journal sans verifier qu'il existe.
    // Une base sans journal n'a joue aucune migration.
    private static async Task<IReadOnlyList<string>> MigrationsJoueesAsync(string connexionBase, CancellationToken ct)
    {
        await using (var cn = await OuvrirAsync(connexionBase, ct))
            if ((await Migrateur.EtatJournalAsync(cn, ct)).Nombre == 0) return [];
        return await Migrateur.JoueesAsync(connexionBase, ct);
    }

    private sealed class EcartApres(IReadOnlyList<string> ecarts)
        : Exception($"{ecarts.Count} objet(s) different(s) du schema livre")
    {
        public IReadOnlyList<string> Ecarts { get; } = ecarts;
    }

    private static async Task<SqlConnection> OuvrirAsync(string connexion, CancellationToken ct)
    {
        var cn = new SqlConnection(connexion);
        try { await cn.OpenAsync(ct); return cn; }
        catch { await cn.DisposeAsync(); throw; }
    }

    private static async Task<object?> ScalaireAsync(SqlConnection cn, string sql, CancellationToken ct, params (string Nom, object Valeur)[] parametres)
    {
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (nom, valeur) in parametres) cmd.Parameters.AddWithValue(nom, valeur);
        return await cmd.ExecuteScalarAsync(ct);
    }

    private static async Task ExecuterAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Crochets(string nom) => "[" + nom.Replace("]", "]]") + "]";
    private static string Litteral(string texte) => texte.Replace("'", "''");
}
