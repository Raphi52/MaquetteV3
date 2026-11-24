using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Serveur.Recopie;

namespace RigV3.Repetition;

/// <summary>Ce que la preparation des donnees de developpement fait (commande « donnees-dev »).</summary>
public sealed record OptionsDonneesDev
{
    /// <summary>La base V2 lue (SELECT seul) : RIG_DEV sur SQL-DEV\DEV par defaut.</summary>
    public required string Source { get; init; }
    /// <summary>Une source hors de ce poste : son nom de base (SOC-021 CA07).</summary>
    public string? Accord { get; init; }
    /// <summary>Copie reduite et fermee sur ses cles (200 : ~19 min pour RIG_DEV ; les tables du socle, elles, sont copiees en entier).</summary>
    public long? MaxLignes { get; init; } = 200;
    /// <summary>Le code du greffe inscrit au catalogue : 3801 (Grenoble) pour RIG_DEV, celui de ses agents.</summary>
    public string Greffe { get; init; } = "3801";
    public required string DossierRapport { get; init; }
    public required string DossierMigrations { get; init; }
    /// <summary>Le fichier qui dit quelles bases servent (relu par donnees-dev.ps1).</summary>
    public required string Fichier { get; init; }
    /// <summary>Refaire les bases meme si celles du fichier sont la (les anciennes sont supprimees APRES la reussite).</summary>
    public bool Refaire { get; init; }
    public TextWriter? Sortie { get; init; }
}

/// <summary>Les bases de developpement pretes, et l'agent sous lequel l'app les ouvre. Ecrit dans
/// <see cref="OptionsDonneesDev.Fichier"/>, relu par donnees-dev.ps1 pour lancer le serveur et poser les variables de l'app.</summary>
/// <param name="Serveur">Le serveur SQL de ce poste (Docker), tel que le catalogue l'ecrit (Data Source).</param>
/// <param name="Commune">La base commune du serveur V3 (ConnectionStrings:RigV3) : le catalogue y porte le greffe.</param>
/// <param name="Compte">Le compte de l'agent (pseudonyme de la copie) : RIGV3_COMPTE, ecoute par un serveur en Development seulement.</param>
public sealed record DonneesDev(
    string Serveur, string Copie, string BaseV3, string Commune, string Greffe, string NomGreffe,
    string Compte, string NomAgent, string Source, long? MaxLignes, DateTime PrepareesLe, string? Rapport)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

/// <summary>
/// LES DONNEES DE DEVELOPPEMENT : l'app branchee sur une copie ANONYMISEE de RIG_DEV au lieu de Metier/JeuDemo.cs.
///  1. La repetition du transfert, gardee (<see cref="Repetition.RepeterAsync"/>) : copie anonymisee de la source sur ce
///     poste (SOC-021, la source est LUE), son controle, puis le transfert dans une base V3 neuve (comptes, droits,
///     reglages, compteurs : les reprises de la phase 1) et le controle des deux bases.
///  2. La base commune du serveur, a jour des migrations, et le greffe inscrit a son catalogue (SOC-007) : serveur local,
///     base V2 = la copie, base V3 = la base transferee.
///  3. L'agent : un compte actif de la base V3, administrateur d'Amitel, avec un service et des roles (les quatre controles
///     de SOC-001), qui gere les droits si possible (ecran « Comptes des agents »). Ses identifiants sont des pseudonymes :
///     la session Windows du poste n'est pas dans la copie, l'app l'ouvre donc par RIGV3_COMPTE (serveur en Development).
///     Il est aussi inscrit dans la base commune, ou les routes communes (aiguillage, supervision) le jugent.
///  4. Le fichier <see cref="OptionsDonneesDev.Fichier"/>. Les bases d'une preparation precedente sont supprimees APRES la
///     reussite : un echec laisse l'ancien jeu en service.
/// Deja pretes (fichier lu, ses trois bases presentes) et sans <see cref="OptionsDonneesDev.Refaire"/> : rien n'est refait.
/// Tout s'ecrit sur le serveur des bancs de ce poste (<see cref="BaseJetable.ExigerServeurLocal"/>), jamais ailleurs.
/// </summary>
public static class PreparationDonneesDev
{
    /// <summary>Le fichier par defaut : %LOCALAPPDATA%\RigV3\donnees-dev.json (un reglage du poste, pas du depot).</summary>
    public static string FichierParDefaut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RigV3", "donnees-dev.json");

    /// <summary>Prefixe de la base commune : celui des bases de repetition, que « supprimer » accepte.</summary>
    public const string PrefixeCommune = "rigv3_rep_dev_commun_";

    public static async Task<DonneesDev> PreparerAsync(OptionsDonneesDev o, CancellationToken ct = default)
    {
        void Noter(string ligne) => o.Sortie?.WriteLine($"{DateTime.Now:HH:mm:ss} {ligne}");
        var serveur = BaseJetable.ServeurDesBancs();
        var precedentes = Lire(o.Fichier);
        if (precedentes is not null && !o.Refaire)
        {
            var manque = await BaseManquanteAsync(serveur, precedentes, ct);
            if (manque is null)
            {
                Noter($"donnees de developpement deja pretes ({precedentes.PrepareesLe:dd/MM/yyyy HH:mm}) : {precedentes.BaseV3}, agent {precedentes.Compte} ; --refaire pour les refaire");
                return precedentes;
            }
            Noter($"la base {manque} du jeu precedent n'existe plus : le jeu est refait");
        }

        // 1. La repetition du transfert, gardee : la copie anonymisee et la base V3 servent de donnees.
        var rep = await Repetition.RepeterAsync(new OptionsRepetition
        {
            Sources = [o.Source],
            Accord = o.Accord,
            DossierRapport = o.DossierRapport,
            DossierMigrations = o.DossierMigrations,
            MaxLignes = o.MaxLignes,
            // Les tables que les reprises de la phase 1 lisent (comptes, droits, reglages, compteurs, categories, anciens numeros : 21 au 24/11), EN
            // ENTIER : reduites a 200 lignes, un agent perdait ses roles (HABILIT_UTILISATEUR_ROLE_USER : 2 900 lignes dans
            // RIG_DEV) et les compteurs leurs valeurs (NUMERO_METIER : 26 561). Mesure du 2026-11-24.
            TablesEntieres = TablesDuSocle(),
            Garder = true,
            Sortie = o.Sortie,
        }, ct);
        var b = rep.Bases.Single();
        var chaine = (string nom) => new SqlConnectionStringBuilder(serveur) { InitialCatalog = nom }.ConnectionString;
        if (b.Etat != "fini" || b.Copie is null || b.BaseV3 is null)
        {
            foreach (var nom in new[] { b.BaseV3, b.Copie }.OfType<string>()) await Repetition.SupprimerAsync(chaine(nom));
            throw new InvalidOperationException($"la repetition du transfert s'est arretee ({b.Etat}) : {b.Motif}");
        }
        var commune = $"{PrefixeCommune}{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        try
        {
            // 2. La base commune et le catalogue.
            await using (var master = new SqlConnection(chaine("master")))
            {
                await master.OpenAsync(ct);
                await using var cmd = new SqlCommand($"CREATE DATABASE [{commune}] COLLATE {BaseJetable.Collation};", master);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await Migrateur.AppliquerAsync(chaine(commune), o.DossierMigrations, ct: ct);

            // 3. L'agent, lu dans la base V3 transferee.
            var (compte, nomAgent, idCompte) = await ChoisirAgentAsync(chaine(b.BaseV3), ct);
            var nomGreffe = $"{new SqlConnectionStringBuilder(o.Source).InitialCatalog} anonymisée";
            await InscrireDansLaCommuneAsync(chaine(commune), o.Greffe, nomGreffe, b.Copie, b.BaseV3, idCompte, ct);
            Noter($"base commune {commune} : greffe {o.Greffe} « {nomGreffe} » au catalogue (base V3 {b.BaseV3}, base V2 {b.Copie}) ; agent {compte} ({nomAgent})");

            // 4. Le fichier, puis l'ancien jeu supprime.
            var pretes = new DonneesDev(new SqlConnectionStringBuilder(serveur).DataSource, b.Copie, b.BaseV3, commune, o.Greffe, nomGreffe,
                compte, nomAgent, $"{new SqlConnectionStringBuilder(o.Source).DataSource} / {new SqlConnectionStringBuilder(o.Source).InitialCatalog}",
                o.MaxLignes, DateTime.Now, Directory.EnumerateFiles(o.DossierRapport, "repetition-*.md").OrderBy(f => f).LastOrDefault());
            Directory.CreateDirectory(Path.GetDirectoryName(o.Fichier)!);
            await File.WriteAllTextAsync(o.Fichier, JsonSerializer.Serialize(pretes, DonneesDev.Json), ct);
            if (precedentes is not null)
                foreach (var nom in new[] { precedentes.Commune, precedentes.BaseV3, precedentes.Copie }.Where(EstBaseDeRepetition))
                {
                    await Repetition.SupprimerAsync(chaine(nom));
                    Noter($"ancien jeu : {nom} supprimee");
                }
            return pretes;
        }
        catch
        {
            foreach (var nom in new[] { commune, b.BaseV3, b.Copie }) await Repetition.SupprimerAsync(chaine(nom));
            throw;
        }
    }

    /// <summary>
    /// Le compte sous lequel l'app ouvre le greffe : il doit passer les quatre controles de la V2 (SOC-001 : connu, un role,
    /// actif, un service) ; parmi eux, un administrateur d'Amitel (il voit tout), qui gere les droits si possible.
    /// </summary>
    public static async Task<(string Compte, string Nom, int Id)> ChoisirAgentAsync(string baseV3, CancellationToken ct)
    {
        await using var cn = new SqlConnection(baseV3);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) c.CompteWindows, LTRIM(RTRIM(c.Prenom + ' ' + c.Nom)), c.Id
            FROM soc.Compte c
            WHERE c.Desactive = 0 AND c.IdService IS NOT NULL AND c.EstAmitel = 1 AND c.EstAdministrateur = 1
              AND EXISTS (SELECT 1 FROM soc.CompteRole r WHERE r.IdCompte = c.Id)
            ORDER BY CASE WHEN EXISTS (SELECT 1 FROM soc.CompteRole r WHERE r.IdCompte = c.Id AND r.Role = @gestion) THEN 0 ELSE 1 END, c.Id;
            """, cn);
        cmd.Parameters.AddWithValue("@gestion", RigV3.Contrats.Socle.RoutesDroits.RoleGestionDesDroits);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        if (!await l.ReadAsync(ct))
            throw new InvalidOperationException("aucun compte actif de la base V3 n'est administrateur d'Amitel avec un service et un role : " +
                                                "l'app ne pourrait s'ouvrir sous aucun agent qui voit tous les ecrans");
        return (l.GetString(0), l.GetString(1), l.GetInt32(2));
    }

    /// <summary>
    /// La base commune, dans une transaction : le greffe au catalogue (serveur = celui de la commune, base V2 = la copie,
    /// base V3 = <paramref name="baseV3"/>, sur le meme serveur), puis l'agent <paramref name="idCompte"/> de la base V3 avec
    /// son service et ses roles -- les routes communes (aiguillage, supervision) le jugent la. Un role que les migrations
    /// ont deja pose (GestionDesHabilitations, SOC-002) n'est pas recopie.
    /// </summary>
    public static async Task InscrireDansLaCommuneAsync(string commune, string greffe, string nomGreffe, string copie, string baseV3,
                                                       int idCompte, CancellationToken ct)
    {
        BaseJetable.ExigerServeurLocal(commune);
        if (!EstNomSimple(baseV3)) throw new InvalidOperationException($"nom de base V3 refuse : « {baseV3} »");
        var v3 = $"[{baseV3}]";
        await using var cn = new SqlConnection(commune);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await using var cmd = new SqlCommand($"""
            INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, BaseV3CreeeLe, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe)
            VALUES (@greffe, @nomGreffe, @serveur, @copie, @v3, SYSDATETIME(), 0, 0, 'Romance Standard Time', @auteur, SYSDATETIME());

            DECLARE @service int;
            INSERT INTO soc.Service (CodeGreffe, Abrege, Nom)
            SELECT s.CodeGreffe, s.Abrege, s.Nom FROM {v3}.soc.Service s JOIN {v3}.soc.Compte c ON c.IdService = s.Id WHERE c.Id = @id;
            SET @service = SCOPE_IDENTITY();
            INSERT INTO soc.Role (Nom, Libelle, ReserveAmitel, ReserveAdministrateur)
            SELECT r.Nom, r.Libelle, r.ReserveAmitel, r.ReserveAdministrateur
            FROM {v3}.soc.Role r
            WHERE r.Nom IN (SELECT cr.Role FROM {v3}.soc.CompteRole cr WHERE cr.IdCompte = @id)
              AND NOT EXISTS (SELECT 1 FROM soc.Role x WHERE x.Nom = r.Nom);
            INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur, EstAssistance)
            SELECT c.CompteWindows, c.Nom, c.Prenom, c.Abrege, c.Civilite, c.CodeGreffe, @service, 0, c.EstAmitel, c.EstAdministrateur, c.EstAssistance
            FROM {v3}.soc.Compte c WHERE c.Id = @id;
            DECLARE @compte int = SCOPE_IDENTITY();
            INSERT INTO soc.CompteRole (IdCompte, Role)
            SELECT @compte, cr.Role FROM {v3}.soc.CompteRole cr WHERE cr.IdCompte = @id;
            """, cn, tx);
        cmd.Parameters.AddWithValue("@greffe", greffe);
        cmd.Parameters.AddWithValue("@nomGreffe", nomGreffe);
        cmd.Parameters.AddWithValue("@serveur", new SqlConnectionStringBuilder(commune).DataSource);
        cmd.Parameters.AddWithValue("@copie", copie);
        cmd.Parameters.AddWithValue("@v3", baseV3);
        cmd.Parameters.AddWithValue("@auteur", $@"{Environment.UserDomainName}\{Environment.UserName}");
        cmd.Parameters.AddWithValue("@id", idCompte);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static bool EstNomSimple(string nom) => nom.Length > 0 && nom.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>Les tables V2 que lisent les reprises du transfert (IRepriseTransfert : comptes et droits, reglages,
    /// compteurs), trouvees comme le transfert les trouve : une reprise ajoutee y entre seule.</summary>
    public static IReadOnlyCollection<string> TablesDuSocle()
        => [.. ReglesTransfert.Decouvrir().Reprises.SelectMany(r => r.Regles).Select(r => r.Table).Distinct(StringComparer.OrdinalIgnoreCase)];

    public static DonneesDev? Lire(string fichier)
        => File.Exists(fichier) ? JsonSerializer.Deserialize<DonneesDev>(File.ReadAllText(fichier), DonneesDev.Json) : null;

    /// <summary>La premiere des trois bases du jeu qui n'existe pas sur le serveur des bancs ; null si les trois sont la.</summary>
    private static async Task<string?> BaseManquanteAsync(string serveur, DonneesDev d, CancellationToken ct)
    {
        await using var cn = new SqlConnection(new SqlConnectionStringBuilder(serveur) { InitialCatalog = "master" }.ConnectionString);
        await cn.OpenAsync(ct);
        foreach (var nom in new[] { d.Copie, d.BaseV3, d.Commune })
        {
            await using var cmd = new SqlCommand("SELECT DB_ID(@n);", cn);
            cmd.Parameters.AddWithValue("@n", nom);
            if (await cmd.ExecuteScalarAsync(ct) is null or DBNull) return nom;
        }
        return null;
    }

    /// <summary>Seules les bases de repetition se suppriment ici (comme la commande « supprimer »).</summary>
    private static bool EstBaseDeRepetition(string nom)
        => nom.StartsWith("rigv3_rep_", StringComparison.OrdinalIgnoreCase) && EstNomSimple(nom);
}
