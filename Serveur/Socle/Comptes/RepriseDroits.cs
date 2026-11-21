// fix-ok: ajout du lot 19 (SOC-002 T05), pas une correction : la lecture des droits d'UN compte (LireCompteAsync) et la reponse
// de la V2 (ReponsesV2), ecrites en trois passes avant toute compilation -- la 2e a remplace une reponse fonction par fonction
// par une reponse calculee une fois par compte, la verification de la recopie l'appelant pour les 876 fonctions du catalogue de
// RIG_DEV (lu le 2026-11-20, SOC-002 sources).
using System.Data;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.Comptes;

/// <summary>Ce que la reprise a ecrit dans la base V3 (SOC-005 CA03 : un seul evenement qui le dit).</summary>
public sealed record BilanReprise(int Services, int Comptes, int Roles, int Fonctions, int LiensRoleFonction,
                                  int RolesAttribues, int DroitsAncien, int DroitsComptabilite)
{
    /// <summary>Les droits repris, toutes origines : roles attribues et droits directs.</summary>
    public int Droits => RolesAttribues + DroitsAncien + DroitsComptabilite;
}

/// <summary>
/// LA REPRISE DES COMPTES ET DES DROITS AU PASSAGE (SOC-002 T04, SOC-004 T04, SOC-005 T04, SOC-016 T04).
///
/// Lit la base V2 d'un greffe EN LECTURE SEULE (rien que des SELECT, connexion en intention lecture
/// seule) et ecrit sa base V3, en UNE transaction :
///  - les services avec leur distribution, les comptes (desactives compris, sans mot de passe), chacun
///    rattache au meme service (SOC-004 CA08) ;
///  - les roles, les fonctions, les fonctions de chaque role, les roles de chaque compte ;
///  - les deux autres origines de droits de la V2, TRADUITES en droits directs (SOC-002 CA02) :
///    l'ancien systeme pour une fonction FONCVB6 (degre final > 2), les menus de comptabilite pour toute
///    autre fonction, y compris sous leurs anciens noms. La V2 ne consulte l'ancien systeme QUE pour une
///    fonction FONCVB6 et les menus QUE pour une autre (Utilisateur.cs:321-341) : la traduction suit
///    exactement ce partage, sinon la V3 accorderait un droit que la V2 refuse ;
///  - UN evenement 210 avec les nombres repris, et non un par droit (SOC-005 CA03) ;
///  - la ligne qui ouvre le journal de la V3 ; le journal AUDIT de la V2 n'est pas copie (SOC-016 CA05).
/// Une base V3 deja reprise (evenement 210 present) ou qui a deja des comptes est refusee.
/// </summary>
public static class RepriseDroits
{
    /// <summary>Le code CODE_JET de la V2 pour une reprise (lu dans RIG_DEV le 2026-11-20).</summary>
    public const short CodeEvenementReprise = 210;

    /// <summary>
    /// Les anciens noms des menus de comptabilite, que la V2 accepte encore pour la fonction nouvelle
    /// (Utilisateur.cs:329-336).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AnciensNomsDeMenu = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GESTDROITSEXP"] = "DROITS",
        ["STAT_EXP"] = "STATS",
        ["COMPTA_EDIT"] = "EDITION",
        ["COMPTA_FOURN"] = "FOURNISSEUR",
        ["REMISBQ"] = "BORDEREAU",
        ["FIGER_EXP"] = "FIGEAGE",
    };

    /// <summary>
    /// La fonction passe-t-elle par l'ancien systeme ? V2 : PROCF_TYPE « OLD » ou « VB6 » est converti en
    /// FONCVB6 (Utilisateur.cs:317-320), et TypeProc lit le type par Enum.TryParse, sensible a la casse
    /// (ProcessusEtFonctionalite.cs:186-192). Tout autre type passe par les menus de comptabilite.
    /// </summary>
    public static bool EstFonctionVb6(string typeV2) => typeV2 is "FONCVB6" or "OLD" or "VB6";

    /// <summary>
    /// LA REPONSE DE LA V2 pour un compte et une fonction de son catalogue, sur des donnees lues : Utilisateur.ALaFonctionnalite
    /// (Utilisateur.cs:298-347) dans son ordre -- un role du compte qui porte la fonction (jointure SQL de la V2 : majuscules
    /// et espaces de fin ignores) ; sinon, pour une fonction FONCVB6, l'ancien systeme au-dessus du degre 2 ; sinon un menu de
    /// comptabilite au nom de la fonction ou a son ancien nom (compare a l'identique, C#). C'est le partage que la reprise
    /// traduit ; la recopie V3 -> V2 (Serveur\Recopie\DroitsRecopies.cs) s'en sert pour ecrire chaque droit dans SON origine,
    /// puis pour verifier, avant de valider, que la V2 repond comme la V3.
    /// </summary>
    /// <returns>Code de chaque fonction du catalogue de la V2 -> autorisee ou refusee.</returns>
    public static Dictionary<string, bool> ReponsesV2(DonneesV2 v2, int idUtlst)
    {
        var parRole = FonctionsParUnRoleV2(v2, idUtlst);
        var degres = v2.DegresDuCompte(idUtlst);
        var menus = v2.MenusDuCompte(idUtlst);
        var reponses = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var f in v2.Fonctions)
            reponses[f.Code] = parRole.Contains(DonneesV2.Cle(f.Code)) || (EstFonctionVb6(f.Type)
                ? DegreAncien(v2, degres, f.Code) is > 2
                : menus.Contains(f.Code) || (AnciensNomsDeMenu.TryGetValue(f.Code, out var ancienNom) && menus.Contains(ancienNom)));
        return reponses;
    }

    /// <summary>Les fonctions (cle SQL, <see cref="DonneesV2.Cle"/>) que les roles du compte lui donnent dans la V2
    /// (HABILIT_UTILISATEUR_ROLE_USER puis HABILIT_ROLE_USER_PROCESSUS, joints comme SQL Server les joint).</summary>
    public static HashSet<string> FonctionsParUnRoleV2(DonneesV2 v2, int idUtlst)
    {
        var roles = v2.ComptesRoles.Where(r => r.IdUtlst == idUtlst).Select(r => DonneesV2.Cle(r.Role)).ToHashSet(StringComparer.Ordinal);
        return v2.RoleFonctions.Where(rf => roles.Contains(DonneesV2.Cle(rf.Role))).Select(rf => DonneesV2.Cle(rf.Code)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Lit la V2, puis ecrit la V3. Rend ce qui a ete repris.</summary>
    public static async Task<BilanReprise> ReprendreAsync(string connexionV2, string connexionV3, CancellationToken ct = default)
    {
        var v2 = await DonneesV2.LireAsync(connexionV2, ct);
        return await EcrireAsync(v2, connexionV3, ct);
    }

    /// <summary>Le degre final de l'ancien systeme pour un compte et une fonction, ou null si la V2 leverait
    /// une erreur (avalee par Habilitation.cs:14 : refus).</summary>
    internal static int? DegreAncien(DonneesV2 v2, IReadOnlyDictionary<string, int> degresDuCompte, string code)
    {
        // GestionDroitRIG.cls:291-325 : la PREMIERE categorie de cette reference (SQL sans ORDER BY : la V2
        // prend la premiere ligne rendue, celle de la cle primaire la plus basse). Sans categorie, ou a un
        // niveau sans cle (3, 7, 8), la fonction n'a pas de degre : 0.
        if (!v2.PremiereCategorie.TryGetValue(DonneesV2.Cle(code), out var cat)) return 0;
        string[] cle = cat.Niveau switch
        {
            1 => [cat.Id.ToString(), "0", "0"],
            2 => [Texte(cat.IdDomaine), Texte(cat.IdActivite), "0"],
            4 => [Texte(cat.IdDomaine), Texte(cat.IdActivite), cat.Id.ToString()],
            5 or 6 => [Texte(cat.IdDomaine), cat.Id.ToString(), "0"],
            _ => [],
        };
        if (cle.Length == 0) return 0;

        // GestionDroitRIG.cls:327-345 : le plus haut degre des roles du compte sur cette reference, -1 sans aucun.
        var initial = degresDuCompte.TryGetValue(DonneesV2.Cle(code), out var d) ? d : -1;

        // m_DeterminerDegreSQL (GestionDroitRIG.cls:372-455) : le degre des categories superieures prime.
        if (initial == 0) return 0;
        int? Superieur(string id)
        {
            // CLng("") leve en VB6 (identifiant de categorie NULL) : erreur, donc refus.
            if (!int.TryParse(id, out var idCat)) return null;
            return v2.Categories.TryGetValue(idCat, out var sup) && degresDuCompte.TryGetValue(DonneesV2.Cle(sup.Reference), out var ds) ? ds : -1;
        }
        switch (cat.Niveau)
        {
            case 1:
                return initial;
            case 2:
            case 5:
            case 6:
            {
                if (Superieur(cle[0]) is not { } dom) return null;
                return dom == -1 ? initial : dom;
            }
            case 4:
            {
                if (Superieur(cle[0]) is not { } dom) return null;
                if (dom == -1) dom = initial;
                if (Superieur(cle[1]) is not { } act) return null;
                if (act == -1 && dom >= initial) return initial;
                return act > dom ? dom : act;
            }
            default:
                return 0;
        }

        static string Texte(int? id) => id?.ToString() ?? string.Empty;
    }

    private static async Task<BilanReprise> EcrireAsync(DonneesV2 v2, string connexionV3, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);

        await using (var deja = new SqlCommand(
            $"SELECT (SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = {CodeEvenementReprise}) + (SELECT COUNT(*) FROM soc.Compte);", cn, tx))
            if ((int)(await deja.ExecuteScalarAsync(ct))! > 0)
                throw new InvalidOperationException("Base V3 deja reprise ou deja peuplee : la reprise des comptes et des droits ne se joue qu'une fois, sur une base neuve.");

        // Services, puis comptes rattaches, par leur identifiant V2.
        var services = Table(("IdV2", typeof(int)), ("CodeGreffe", typeof(string)), ("Abrege", typeof(string)), ("Nom", typeof(string)),
            ("CodeDirection", typeof(string)), ("DistributionSpecifique", typeof(bool)), ("DistributionAuto", typeof(bool)), ("HeureDistribution", typeof(DateTime)));
        foreach (var s in v2.Services)
            services.Rows.Add(s.Id, s.CodeGreffe, s.Abrege, (object?)s.Nom ?? DBNull.Value, (object?)s.CodeDirection ?? DBNull.Value,
                s.DistributionSpecifique, s.DistributionAuto, (object?)s.HeureDistribution ?? DBNull.Value);
        await Copier(cn, tx, "soc.Service", services, ct);
        var servicesV3 = await Correspondance(cn, tx, "soc.Service", ct);

        var comptes = Table(("IdV2", typeof(int)), ("CompteWindows", typeof(string)), ("Nom", typeof(string)), ("Prenom", typeof(string)),
            ("Abrege", typeof(string)), ("Civilite", typeof(string)), ("CodeGreffe", typeof(string)), ("IdService", typeof(int)),
            ("Desactive", typeof(bool)), ("EstAmitel", typeof(bool)), ("EstAdministrateur", typeof(bool)), ("EstAssistance", typeof(bool)),
            ("DateCreation", typeof(DateTime)));
        foreach (var u in v2.Utilisateurs)
            comptes.Rows.Add(u.Id, u.Login, u.Nom, u.Prenom, u.Abrege, u.Civilite, (object?)u.CodeGreffe ?? DBNull.Value,
                u.IdService is { } ids && servicesV3.TryGetValue(ids, out var idV3) ? idV3 : DBNull.Value,
                u.Desactive, u.EstAmitel, u.EstAdministrateur, u.EstAssistance, (object?)u.DateCreation ?? DBNull.Value);
        await Copier(cn, tx, "soc.Compte", comptes, ct);
        var comptesV3 = await Correspondance(cn, tx, "soc.Compte", ct);

        // Roles : la migration a deja pose GestionDesHabilitations ; la V2 donne son libelle.
        foreach (var r in v2.Roles)
        {
            await using var cmd = new SqlCommand("""
                UPDATE soc.Role SET Libelle = @libelle, ReserveAmitel = @amitel, ReserveAdministrateur = @admin WHERE Nom = @nom;
                IF @@ROWCOUNT = 0 INSERT INTO soc.Role (Nom, Libelle, ReserveAmitel, ReserveAdministrateur) VALUES (@nom, @libelle, @amitel, @admin);
                """, cn, tx);
            cmd.Parameters.Add(new SqlParameter("@nom", SqlDbType.VarChar, 40) { Value = r.Nom });
            cmd.Parameters.Add(new SqlParameter("@libelle", SqlDbType.VarChar, 80) { Value = r.Libelle });
            cmd.Parameters.Add(new SqlParameter("@amitel", SqlDbType.Bit) { Value = r.ReserveAmitel });
            cmd.Parameters.Add(new SqlParameter("@admin", SqlDbType.Bit) { Value = r.ReserveAdministrateur });
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var fonctions = Table(("Code", typeof(string)), ("Libelle", typeof(string)), ("TypeV2", typeof(string)), ("Comptabilite", typeof(bool)),
            ("ReserveAmitel", typeof(bool)), ("ReserveAdministrateur", typeof(bool)));
        var dejaLa = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand("SELECT Code FROM soc.Fonction;", cn, tx))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct)) dejaLa.Add(l.GetString(0));
        foreach (var f in v2.Fonctions.Where(f => !dejaLa.Contains(f.Code)))
            // Domaine Comptabilite de la V2 : le bit 16 (ProcessusEtFonctionalite.cs:42-43).
            fonctions.Rows.Add(f.Code, f.Libelle, f.Type, ((f.Domaine ?? 0) & 16) != 0, f.ReserveAmitel, f.ReserveAdministrateur);
        await Copier(cn, tx, "soc.Fonction", fonctions, ct);

        var roleFonction = Table(("Role", typeof(string)), ("Fonction", typeof(string)));
        foreach (var (role, code) in v2.RoleFonctions) roleFonction.Rows.Add(role, code);
        await Copier(cn, tx, "soc.RoleFonction", roleFonction, ct);

        var compteRole = Table(("IdCompte", typeof(int)), ("Role", typeof(string)));
        foreach (var (idUtlst, role) in v2.ComptesRoles) compteRole.Rows.Add(comptesV3[idUtlst], role);
        await Copier(cn, tx, "soc.CompteRole", compteRole, ct);

        // Les deux autres origines, traduites compte par compte.
        var directs = Table(("IdCompte", typeof(int)), ("Fonction", typeof(string)), ("Origine", typeof(string)), ("Detail", typeof(string)));
        int ancien = 0, compta = 0;
        foreach (var u in v2.Utilisateurs)
        {
            var degres = v2.DegresDuCompte(u.Id);
            var menus = v2.MenusDuCompte(u.Id);
            foreach (var f in v2.Fonctions)
            {
                if (EstFonctionVb6(f.Type))
                {
                    if (DegreAncien(v2, degres, f.Code) is > 2 and var degre)
                    {
                        directs.Rows.Add(comptesV3[u.Id], f.Code, "ancien", $"degre {degre} (ancien systeme)");
                        ancien++;
                    }
                }
                else if (menus.Contains(f.Code))
                {
                    directs.Rows.Add(comptesV3[u.Id], f.Code, "comptabilite", $"menu {f.Code}");
                    compta++;
                }
                else if (AnciensNomsDeMenu.TryGetValue(f.Code, out var ancienNom) && menus.Contains(ancienNom))
                {
                    directs.Rows.Add(comptesV3[u.Id], f.Code, "comptabilite", $"menu {ancienNom} (ancien nom)");
                    compta++;
                }
            }
        }
        await Copier(cn, tx, "soc.CompteFonction", directs, ct);

        var bilan = new BilanReprise(v2.Services.Count, v2.Utilisateurs.Count, v2.Roles.Count, v2.Fonctions.Count, v2.RoleFonctions.Count,
            v2.ComptesRoles.Count, ancien, compta);
        await JournalGestes.EcrireEvenementAsync(cn, tx, CodeEvenementReprise, "Reprise de données d'autres logiciels suite à changement de systèmes",
            $"Reprise des comptes et des droits de la V2 : {bilan.Comptes} comptes, {bilan.Droits} droits repris " +
            $"({bilan.RolesAttribues} rôles attribués, {bilan.DroitsAncien} de l'ancien système, {bilan.DroitsComptabilite} de la comptabilité) ; " +
            $"{bilan.Services} services, {bilan.Roles} rôles, {bilan.Fonctions} fonctions.", null, ct);
        await JournalGestes.EcrireOuvertureAsync(cn, tx, ct);
        await tx.CommitAsync(ct);
        return bilan;
    }

    private static DataTable Table(params (string Nom, Type Type)[] colonnes)
    {
        var t = new DataTable();
        foreach (var (nom, type) in colonnes) t.Columns.Add(nom, type);
        return t;
    }

    private static async Task Copier(SqlConnection cn, SqlTransaction tx, string table, DataTable lignes, CancellationToken ct)
    {
        if (lignes.Rows.Count == 0) return;
        using var copie = new SqlBulkCopy(cn, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, tx) { DestinationTableName = table, BulkCopyTimeout = 600 };
        foreach (DataColumn c in lignes.Columns) copie.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await copie.WriteToServerAsync(lignes, ct);
    }

    private static async Task<Dictionary<int, int>> Correspondance(SqlConnection cn, SqlTransaction tx, string table, CancellationToken ct)
    {
        var ids = new Dictionary<int, int>();
        await using var cmd = new SqlCommand($"SELECT IdV2, Id FROM {table} WHERE IdV2 IS NOT NULL;", cn, tx);
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) ids[l.GetInt32(0)] = l.GetInt32(1);
        return ids;
    }
}

/// <summary>
/// Les tables de droits d'une base V2, lues une fois, en lecture seule. Le comparateur de la reprise
/// (tests\SocleDroitsCheck) relit les MEMES tables a sa facon, independamment de cette classe.
/// </summary>
public sealed class DonneesV2
{
    public sealed record Service(int Id, string? CodeDirection, string CodeGreffe, string Abrege, string? Nom,
                                 bool DistributionSpecifique, bool DistributionAuto, DateTime? HeureDistribution);
    public sealed record Utilisateur(int Id, int? IdService, string? CodeGreffe, string Abrege, string Civilite, string Login,
                                     string Nom, string Prenom, bool Desactive, DateTime? DateCreation, bool EstAmitel, bool EstAdministrateur, bool EstAssistance);
    public sealed record Role(string Nom, string Libelle, bool ReserveAmitel, bool ReserveAdministrateur);
    public sealed record Fonction(string Code, string Type, string Libelle, int? Domaine, bool ReserveAmitel, bool ReserveAdministrateur);
    public sealed record Categorie(int Id, string Reference, int Niveau, int? IdDomaine, int? IdActivite);

    public required List<Service> Services { get; init; }
    public required List<Utilisateur> Utilisateurs { get; init; }
    public required List<Role> Roles { get; init; }
    public required List<Fonction> Fonctions { get; init; }
    public required List<(string Role, string Code)> RoleFonctions { get; init; }
    public required List<(int IdUtlst, string Role)> ComptesRoles { get; init; }
    public required Dictionary<int, Categorie> Categories { get; init; }
    /// <summary>Reference (cle SQL) -> la categorie de plus petit identifiant.</summary>
    public required Dictionary<string, Categorie> PremiereCategorie { get; init; }
    /// <summary>Ancien systeme : (role ancien, reference, degre) et (role ancien, compte).</summary>
    public required List<(int IdRole, string Reference, int Degre)> Degres { get; init; }
    public required List<(int IdRole, int IdUtlst)> RolesAnciens { get; init; }
    /// <summary>Comptabilite : (menu, cle) et (menu, compte).</summary>
    public required Dictionary<int, string> Menus { get; init; }
    public required List<(int IdMenu, int IdUtlst)> MenusDroits { get; init; }

    /// <summary>Une chaine comparee comme SQL Server la compare dans RIG_DEV (collation CI, espaces de fin ignores).</summary>
    public static string Cle(string texte) => texte.TrimEnd().ToUpperInvariant();

    /// <summary>Reference -> plus haut degre des roles anciens du compte.</summary>
    public IReadOnlyDictionary<string, int> DegresDuCompte(int idUtlst)
    {
        var roles = RolesAnciens.Where(r => r.IdUtlst == idUtlst).Select(r => r.IdRole).ToHashSet();
        return Degres.Where(d => roles.Contains(d.IdRole))
            .GroupBy(d => Cle(d.Reference))
            .ToDictionary(g => g.Key, g => g.Max(d => d.Degre));
    }

    /// <summary>Les cles des menus de comptabilite du compte, comparees a l'identique (C#, Utilisateur.cs:325).</summary>
    public HashSet<string> MenusDuCompte(int idUtlst)
        => MenusDroits.Where(m => m.IdUtlst == idUtlst && Menus.ContainsKey(m.IdMenu)).Select(m => Menus[m.IdMenu]).ToHashSet(StringComparer.Ordinal);

    /// <summary>Lit les tables de la base V2 (rien que des SELECT).</summary>
    public static async Task<DonneesV2> LireAsync(string connexionV2, CancellationToken ct = default)
    {
        var cs = new SqlConnectionStringBuilder(connexionV2) { ApplicationIntent = ApplicationIntent.ReadOnly };
        await using var cn = new SqlConnection(cs.ConnectionString);
        await cn.OpenAsync(ct);
        return await LireAsync(cn, null, null, null, ct);
    }

    /// <summary>
    /// Les droits d'UN compte de la V2 et le catalogue qui les explique (fonctions, roles et leurs fonctions, categories, menus),
    /// lus sur une connexion a la base V3 dans SA transaction : la base V2 est nommee sur la meme instance (Q4). La recopie
    /// V3 -> V2 des droits (Serveur\Recopie\DroitsRecopies.cs) relit ainsi ce qu'elle vient d'ecrire avant de valider.
    /// </summary>
    public static Task<DonneesV2> LireCompteAsync(SqlConnection cn, SqlTransaction? tx, string baseV2, int idUtlst, CancellationToken ct)
        => LireAsync(cn, tx, baseV2, idUtlst, ct);

    /// <param name="baseV2">La base V2 nommee depuis une autre base de l'instance ; null = la base de la connexion.</param>
    /// <param name="idUtlst">Un seul compte (ses roles, son ancien systeme, ses menus) ; null = tous.</param>
    private static async Task<DonneesV2> LireAsync(SqlConnection cn, SqlTransaction? tx, string? baseV2, int? idUtlst, CancellationToken ct)
    {
        var b = baseV2 is null ? "dbo." : "[" + baseV2.Replace("]", "]]") + "].dbo.";
        string Filtre(string colonne) => idUtlst is null ? "" : $" WHERE {colonne} = @u";
        async Task<List<T>> Lire<T>(string sql, Func<SqlDataReader, T> ligne)
        {
            await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 300 };
            if (idUtlst is { } u) cmd.Parameters.Add(new SqlParameter("@u", SqlDbType.Int) { Value = u });
            await using var l = await cmd.ExecuteReaderAsync(ct);
            var liste = new List<T>();
            while (await l.ReadAsync(ct)) liste.Add(ligne(l));
            return liste;
        }
        static int? Int(SqlDataReader l, int i) => l.IsDBNull(i) ? null : Convert.ToInt32(l.GetValue(i));
        static string? Str(SqlDataReader l, int i) => l.IsDBNull(i) ? null : l.GetString(i);

        var categories = await Lire($"SELECT CTGR_ID_CTGR, CTGR_REFERENCE, CTGR_NIVEAU, CTGR_ID_CTGR_DOMAINE, CTGR_ID_CTGR_ACTIVITE FROM {b}CATEGORIE;",
            l => new Categorie(l.GetInt32(0), l.GetString(1), Convert.ToInt32(l.GetValue(2)), Int(l, 3), Int(l, 4)));
        return new DonneesV2
        {
            Services = await Lire($"""
                SELECT SRVC_ID_SRVC, SRVC_CODE_DIRECTION, SRVC_CODE_GREFFE, SRVC_ABREGE, SRVC_NOM, SRVC_DISTRI_SPECIFIQUE,
                       SRVC_DISTRIBUTION_AUTO, SRVC_HEURE_DISTRIBUTION FROM {b}SERVICE;
                """, l => new Service(l.GetInt32(0), Str(l, 1), l.GetString(2), l.GetString(3), Str(l, 4), l.GetBoolean(5), l.GetBoolean(6),
                    l.IsDBNull(7) ? null : l.GetDateTime(7))),
            // Sans UTLST_PASSWORD : la colonne n'est ni lue ni copiee (SOC-001 CA08).
            Utilisateurs = await Lire($"""
                SELECT UTLST_ID_UTLST, UTLST_ID_SRVC, UTLST_CODE_GREFFE, UTLST_ABREGE, UTLST_CIVILITE, UTLST_LOGIN, UTLST_NOM, UTLST_PRENOM,
                       UTLST_DESACTIVE, UTLST_DATE_CREATION, UTLST_ISAMITEL, UTLST_ISADMIN, UTLST_IS_HOTLINE FROM {b}UTILISATEUR{Filtre("UTLST_ID_UTLST")};
                """, l => new Utilisateur(l.GetInt32(0), Int(l, 1), Str(l, 2), l.GetString(3), l.GetString(4), l.GetString(5), l.GetString(6),
                    l.GetString(7), l.GetBoolean(8), l.IsDBNull(9) ? null : l.GetDateTime(9), l.GetBoolean(10), l.GetBoolean(11), l.GetBoolean(12))),
            Roles = await Lire($"SELECT HROLE_NOM_ROLE, HROLE_LIBELLE, HROLE_ONLYAMITEL, HROLE_ONLYADMIN FROM {b}HABILIT_ROLE_USER;",
                l => new Role(l.GetString(0), l.GetString(1), l.GetBoolean(2), l.GetBoolean(3))),
            Fonctions = await Lire($"""
                SELECT PROCF_CODE_PROC, PROCF_TYPE, PROCF_LIBELLE, PROCF_DOMAINE, PROCF_ONLYAMITEL, PROCF_ONLYADMIN
                FROM {b}PROCESSUS_ET_FONCTIONALITE;
                """, l => new Fonction(l.GetString(0), l.GetString(1), l.GetString(2), Int(l, 3), l.GetBoolean(4), l.GetBoolean(5))),
            RoleFonctions = await Lire($"SELECT HRFON_NOM_ROLE, HRFON_CODE_PROC FROM {b}HABILIT_ROLE_USER_PROCESSUS;",
                l => (l.GetString(0), l.GetString(1))),
            ComptesRoles = await Lire($"SELECT HUROL_ID_UTLST, HUROL_NOM_ROLE FROM {b}HABILIT_UTILISATEUR_ROLE_USER{Filtre("HUROL_ID_UTLST")};",
                l => (l.GetInt32(0), l.GetString(1))),
            Categories = categories.ToDictionary(c => c.Id),
            PremiereCategorie = categories.GroupBy(c => Cle(c.Reference)).ToDictionary(g => g.Key, g => g.MinBy(c => c.Id)!),
            Degres = await Lire($"SELECT X_CPSHB_ID_UTROL, X_CPSHB_REFERENCE_CTGR, X_CPSHB_DEGRE FROM {b}COMPOSER_HABILITATION" +
                                (idUtlst is null ? ";" : $" WHERE X_CPSHB_ID_UTROL IN (SELECT X_UTARL_ID_UTROL FROM {b}UTILISATEUR_A_POUR_ROLE WHERE X_UTARL_ID_UTLST = @u);"),
                l => (l.GetInt32(0), l.GetString(1), Convert.ToInt32(l.GetValue(2)))),
            RolesAnciens = await Lire($"SELECT X_UTARL_ID_UTROL, X_UTARL_ID_UTLST FROM {b}UTILISATEUR_A_POUR_ROLE{Filtre("X_UTARL_ID_UTLST")};",
                l => (l.GetInt32(0), l.GetInt32(1))),
            Menus = (await Lire($"SELECT EXPME_ID_EXPME, EXPME_CLE FROM {b}EXPRESSO_MENU WHERE EXPME_CLE IS NOT NULL;",
                l => (l.GetInt32(0), l.GetString(1)))).ToDictionary(m => m.Item1, m => m.Item2),
            MenusDroits = await Lire($"SELECT EXPDR_ID_EXPME, EXPDR_ID_UTLST FROM {b}EXPRESSO_DROITS{Filtre("EXPDR_ID_UTLST")};",
                l => (l.GetInt32(0), l.GetInt32(1))),
        };
    }
}
