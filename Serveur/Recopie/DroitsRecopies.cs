// fix-ok: fichier NEUF du lot 19, redige en plusieurs passes avant toute compilation : la 2e a separe ce que les ROLES de la V3
// donnent de ses droits DIRECTS -- la 1re ecrivait tout droit de la V3 absent des roles de la V2 en menu ou en ancien systeme,
// ce qui aurait masque par un droit direct invente une difference de definition d'un role entre les deux logiciels.
using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Comptes;
using static RigV3.Serveur.Recopie.EcrituresRecopie;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LA RECOPIE V3 -> V2 DES DROITS D'UN COMPTE (SOC-002 T05, CA07 ; fin de REC-017 CA01 « l'agent ouvre la V2 sans autre geste »).
// Lot 19 du plan de reconstruction, phase 2 (2026-11-21). Carnet : ...\refonte-rig\carnet\socle.json et recopie.json.
//
// La V3 fait foi pour les comptes et leurs droits (Q7). Elle tient les droits dans UN modele (soc.CompteRole, soc.RoleFonction,
// soc.CompteFonction, vue soc.Droit, SOC-002_droits.sql) ; la V2 les tient en TROIS origines, consultees dans cet ordre par
// Utilisateur.ALaFonctionnalite (D:\RigApplication\Source\Outils\DLL\RigMetier\Habilitation\Utilisateur.cs:298-347) :
//   1. les roles du compte (HABILIT_UTILISATEUR_ROLE_USER) et leurs fonctions (HABILIT_ROLE_USER_PROCESSUS) ;
//   2. pour une fonction FONCVB6 : l'ancien systeme (UTILISATEUR_A_POUR_ROLE -> COMPOSER_HABILITATION, degre > 2, avec la
//      primaute des categories superieures de GestionDroitRIG.cls:372-455) ;
//   3. pour toute autre fonction : les menus de comptabilite du compte (EXPRESSO_DROITS), y compris sous six anciens noms.
// La reprise (Serveur\Socle\Comptes\RepriseDroits.cs) traduit V2 -> V3 une fois ; ceci est le SENS INVERSE, a chaque changement :
//   - les roles du compte dans la V3 deviennent ses lignes HABILIT_UTILISATEUR_ROLE_USER (ajout et retrait, au nom de role de
//     la V2, avec son compte de session comme la V2 l'ecrit : HabilitationUtilisateurRoleUser.cs:50-55) ;
//   - pour chaque fonction de la V2 que ses roles ne donnent pas, la V3 dit « accordee » ou « refusee » (soc.Droit) ; la
//     reponse est ecrite dans L'ORIGINE QUE LA V2 CONSULTE pour cette fonction : l'ancien systeme pour une FONCVB6, les menus
//     de comptabilite pour une autre. Le type de la fonction decide, comme pour la reprise ; l'origine notee dans la V3
//     (« ancien », « comptabilite ») n'est qu'une trace ;
//   - avant de valider, les droits du compte sont RELUS dans la V2 et la reponse de la V2 recalculee pour chaque fonction de
//     son catalogue (RepriseDroits.ReponsesV2) : un seul ecart avec la V3 et rien n'est ecrit (refus, la fiche attend).
//
// BRANCHE SUR LA RECOPIE EXISTANTE, sans second mecanisme : les droits sont la PARTIE JOINTE de la fiche « compte d'agent »
// (REC-017, FichesV3.cs ; IRecopieJointe, ReglesRecopie.cs). Un changement de soc.CompteRole ou soc.CompteFonction met le
// COMPTE en file (declencheur pose par la mise en service, PontRecopie.DeclencheurJoint, colonnes « (droits) ») ; l'applicateur
// ecrit ses droits dans la transaction du compte. Ils ont donc l'ordre de la file, une transaction, la panne qui arrete le
// passage, le refus qui compte un essai et la mise de cote au 3e (REC-005), la relance, et l'ecart (REC-004).
// Mode d'emploi et choix : docs\reconstruction\socle-droits.md, section « Recopie V3 -> V2 des droits » ; recopie.md.
// ============================================================

/// <summary>Les droits d'un compte, recopies de la V3 dans les trois origines de la V2 (voir l'en-tete du fichier).</summary>
public sealed class DroitsRecopies : IRecopieJointe
{
    public static DroitsRecopies Instance { get; } = new();

    private DroitsRecopies() { }

    public string Libelle => "droits";

    public IReadOnlyList<TableJointe> Tables { get; } = [new("soc.CompteRole", "IdCompte"), new("soc.CompteFonction", "IdCompte")];

    /// <summary>La table de rec.EtatLigne sous laquelle est note l'etat commun des droits d'un compte apres son dernier passage :
    /// c'est contre lui qu'un changement de droits fait dans la V2 se mesure (REC-004).</summary>
    public const string TableEtat = "UTILISATEUR (droits)";

    /// <summary>Le degre que la recopie ecrit pour accorder une fonction par l'ancien systeme : au-dessus de 2
    /// (Habilitation.cs:8-16), celui que la V2 emploie (RIG_DEV, lu le 2026-11-21 : 165 degres a 3, 46 a 0, aucun autre).</summary>
    public const int DegreAccorde = 3;

    /// <summary>Le role de l'ancien systeme propre a un compte (UTILISATEUR_ROLE.UTROL_LIBELLE, varchar(16)). L'ancien systeme
    /// n'a que des roles PARTAGES : changer le degre d'un compte seul demande un role a lui. La premiere fois, il recoit les
    /// degres que le compte avait par tous ses roles (meme reponse partout) et le compte ne garde que lui.</summary>
    public static string RoleAncienDuCompte(int idUtlst) => $"RIGV3 {idUtlst}";

    public const string ChampRoles = "droits : rôles";
    public const string ChampMenus = "droits : menus de comptabilité";
    public const string ChampAncien = "droits : ancien système";

    private static string Cle(string texte) => DonneesV2.Cle(texte);

    public async Task<long?> AppliquerAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee fiche, string idV3, string idV2, ChangementJoint changement,
                                            CancellationToken ct)
    {
        var idCompte = int.Parse(idV3, CultureInfo.InvariantCulture);
        var idUtlst = int.Parse(idV2, CultureInfo.InvariantCulture);
        var b = Sql.Q(fiche.BaseV2) + ".dbo.";

        // 1. LA V3 : les roles du compte, ce qu'ils lui donnent, ses droits directs (soc.Droit, toutes origines).
        var rolesV3 = await ListeAsync(cn, tx, "SELECT Role FROM soc.CompteRole WHERE IdCompte = @c ORDER BY Role;", ct, ("@c", idCompte));
        var droitsV3 = await PairesAsync(cn, tx, "SELECT DISTINCT Fonction, Origine FROM soc.Droit WHERE IdCompte = @c;", ct, ("@c", idCompte));
        var accordees = droitsV3.Select(d => Cle(d.Fonction)).ToHashSet(StringComparer.Ordinal);
        var parRoleV3 = droitsV3.Where(d => d.Origine == "role").Select(d => Cle(d.Fonction)).ToHashSet(StringComparer.Ordinal);
        var directs = droitsV3.Where(d => d.Origine != "role").Select(d => Cle(d.Fonction)).ToHashSet(StringComparer.Ordinal);

        // 2. LA V2 : les droits du compte avant d'ecrire, et leur etat commun au dernier passage.
        var v2 = await DonneesV2.LireCompteAsync(cn, tx, fiche.BaseV2, idUtlst, ct);
        var login = v2.Utilisateurs.SingleOrDefault()?.Login
                    ?? throw new RecopieRefusee($"compte {idV2} absent de la V2 : ses droits ne peuvent pas y etre ecrits");
        var avant = EtatDroits.De(v2, idUtlst);
        var commun = EtatDroits.Lire(await ScalaireAsync(cn, tx, "SELECT Ligne FROM rec.EtatLigne WHERE BaseV2 = @b AND TableV2 = @t AND IdV2 = @id;", ct,
                                                         ("@b", fiche.BaseV2), ("@t", TableEtat), ("@id", idV2)));
        var impossibles = new List<string>();

        // 3. LES ROLES, au nom que la V2 leur donne. Un role que la V2 n'a pas ne peut pas y etre attribue : rejouer n'y changera
        // rien, la fiche est mise de cote d'emblee avec ce motif.
        var nomsV2 = v2.Roles.GroupBy(r => Cle(r.Nom)).ToDictionary(g => g.Key, g => g.First().Nom, StringComparer.Ordinal);
        var voulus = new List<string>();
        foreach (var r in rolesV3)
            if (nomsV2.TryGetValue(Cle(r), out var nomV2)) voulus.Add(nomV2);
            else impossibles.Add($"le rôle « {r} » n'existe pas dans la V2 (HABILIT_ROLE_USER)");
        var actuels = v2.ComptesRoles.Select(x => x.Role).ToList();
        var retraits = actuels.Where(a => !voulus.Any(v => Cle(v) == Cle(a))).ToList();
        var ajouts = voulus.Where(v => !actuels.Any(a => Cle(a) == Cle(v))).ToList();
        v2.ComptesRoles.RemoveAll(x => retraits.Any(r => Cle(r) == Cle(x.Role)));
        v2.ComptesRoles.AddRange(ajouts.Select(r => (idUtlst, r)));

        // 4. CHAQUE DROIT DANS SON ORIGINE. Ce que les roles donnent vient des roles, des deux cotes : une fonction que les roles
        // de la V2 donnent et que la V3 refuse, ou que seuls les roles de la V3 donnent, veut dire que les fonctions d'un meme
        // role different entre les deux logiciels (HABILIT_ROLE_USER_PROCESSUS, soc.RoleFonction) -- un droit direct invente
        // ne doit pas le masquer. Les autres fonctions de la V2 recoivent les droits DIRECTS de la V3 (ancien, comptabilite).
        var parRole = RepriseDroits.FonctionsParUnRoleV2(v2, idUtlst);
        foreach (var f in v2.Fonctions)
        {
            var c = Cle(f.Code);
            if (parRole.Contains(c) && !accordees.Contains(c))
                impossibles.Add($"{f.Code} : la V2 l'accorde par un rôle du compte, la V3 non (fonctions du rôle différentes de soc.RoleFonction)");
            else if (parRoleV3.Contains(c) && !parRole.Contains(c) && !directs.Contains(c))
                impossibles.Add($"{f.Code} : la V3 l'accorde par un rôle du compte, les rôles de la V2 non (fonctions du rôle différentes de HABILIT_ROLE_USER_PROCESSUS)");
        }
        var aDecider = v2.Fonctions.Where(f => !parRole.Contains(Cle(f.Code))).ToList();

        // 5. LES MENUS DE COMPTABILITE, pour une fonction qui n'est pas FONCVB6 (Utilisateur.cs:327-344 : au nom de la fonction,
        // compare a l'identique, ou a son ancien nom). Un menu qui ne donne aucune fonction n'est jamais touche.
        var menusDuCompte = v2.MenusDroits.Where(m => m.IdUtlst == idUtlst).Select(m => m.IdMenu).ToHashSet();
        var menusAjoutes = new SortedSet<int>();
        var menusRetires = new SortedSet<int>();
        foreach (var f in aDecider.Where(f => !RepriseDroits.EstFonctionVb6(f.Type)))
        {
            var ancienNom = RepriseDroits.AnciensNomsDeMenu.GetValueOrDefault(f.Code);
            var donnent = v2.Menus.Where(m => m.Value == f.Code || (ancienNom is not null && m.Value == ancienNom)).Select(m => m.Key).ToList();
            var aLeMenu = donnent.Any(menusDuCompte.Contains);
            if (directs.Contains(Cle(f.Code)) && !aLeMenu)
            {
                var menu = v2.Menus.Where(m => m.Value == f.Code).Select(m => (int?)m.Key).Min()
                           ?? v2.Menus.Where(m => ancienNom is not null && m.Value == ancienNom).Select(m => (int?)m.Key).Min();
                if (menu is { } id) menusAjoutes.Add(id);
                else impossibles.Add($"{f.Code} : aucun menu de comptabilité « {f.Code} »{(ancienNom is null ? "" : $" ni « {ancienNom} »")} dans la V2 (EXPRESSO_MENU), " +
                                     "elle ne l'accorde que par un rôle");
            }
            else if (!directs.Contains(Cle(f.Code)) && aLeMenu)
                foreach (var id in donnent.Where(menusDuCompte.Contains)) menusRetires.Add(id);
        }

        // 6. L'ANCIEN SYSTEME, pour une fonction FONCVB6 : les degres du compte qui donnent exactement ses droits directs.
        var degresAvant = v2.DegresDuCompte(idUtlst);
        var degres = AncienSysteme.Resoudre(v2, degresAvant,
            [.. aDecider.Where(f => RepriseDroits.EstFonctionVb6(f.Type)).Select(f => (f.Code, directs.Contains(Cle(f.Code))))], impossibles);

        if (impossibles.Count > 0)
            throw new RecopieImpossible($"la V2 ne peut pas recevoir les droits du compte {login} tels que la V3 les tient : {Resume(impossibles)}",
                                        idV2, idV3, "droits", Json(avant.Resume), Json(string.Join(" ; ", impossibles)));

        // 7. ECRIRE, dans la transaction du compte.
        foreach (var r in retraits)
            await ExecuterAsync(cn, tx, $"DELETE FROM {b}HABILIT_UTILISATEUR_ROLE_USER WHERE HUROL_ID_UTLST = @u AND HUROL_NOM_ROLE = @r;", ct, ("@u", idUtlst), ("@r", r));
        if (ajouts.Count > 0)
        {
            // HUROL_LOGIN_UTLST : la V2 y ecrit le compte de session (HabilitationUtilisateurRoleUser.cs:54) ; une base qui n'a pas la
            // colonne ne l'a pas.
            var avecLogin = await ScalaireAsync(cn, tx, $"SELECT CONVERT(nvarchar(1), 1) FROM {Sql.Q(fiche.BaseV2)}.sys.columns " +
                                                        $"WHERE object_id = OBJECT_ID(N'{b}HABILIT_UTILISATEUR_ROLE_USER') AND name = N'HUROL_LOGIN_UTLST';", ct) is not null;
            foreach (var r in ajouts)
                await ExecuterAsync(cn, tx, avecLogin
                    ? $"INSERT INTO {b}HABILIT_UTILISATEUR_ROLE_USER (HUROL_ID_UTLST, HUROL_NOM_ROLE, HUROL_LOGIN_UTLST) VALUES (@u, @r, @l);"
                    : $"INSERT INTO {b}HABILIT_UTILISATEUR_ROLE_USER (HUROL_ID_UTLST, HUROL_NOM_ROLE) VALUES (@u, @r);", ct, ("@u", idUtlst), ("@r", r), ("@l", login));
        }
        if (menusRetires.Count > 0)
            await ExecuterAsync(cn, tx, $"DELETE FROM {b}EXPRESSO_DROITS WHERE EXPDR_ID_UTLST = @u AND EXPDR_ID_EXPME IN ({string.Join(", ", menusRetires)});", ct,
                                ("@u", idUtlst));
        foreach (var m in menusAjoutes)
            await ExecuterAsync(cn, tx, $"INSERT INTO {b}EXPRESSO_DROITS (EXPDR_ID_EXPME, EXPDR_ID_UTLST) VALUES (@m, @u);", ct, ("@m", m), ("@u", idUtlst));
        if (!AncienSysteme.Memes(degres, degresAvant))
            await EcrireAncienAsync(cn, tx, b, idUtlst, degres, AncienSysteme.References(v2), ct);

        // 8. VERIFIER : les droits relus dans la V2, la reponse de la V2 pour CHAQUE fonction de son catalogue = celle de la V3.
        var apres = await DonneesV2.LireCompteAsync(cn, tx, fiche.BaseV2, idUtlst, ct);
        var reponses = RepriseDroits.ReponsesV2(apres, idUtlst);
        var differentes = apres.Fonctions.Where(f => reponses[f.Code] != accordees.Contains(Cle(f.Code)))
                                         .Select(f => $"{f.Code} (V3 : {(accordees.Contains(Cle(f.Code)) ? "accordée" : "refusée")})").ToList();
        if (differentes.Count > 0)
            throw new RecopieRefusee($"apres ecriture, la V2 ne repond pas comme la V3 pour le compte {login} : {Resume(differentes)} ; rien n'est ecrit");

        // 9. L'ETAT COMMUN, et l'ecart si la V2 avait change ces droits depuis le dernier passage : la V3 gagne (REC-004 CA04).
        var etat = EtatDroits.De(apres, idUtlst);
        long? ecart = null;
        if (commun is not null && avant != commun)
        {
            var perdus = new List<(string Champ, string? V2, string? V3)>();
            void Comparer(string champ, string v2Avant, string communs, string gardes)
            {
                if (v2Avant != communs && v2Avant != gardes) perdus.Add((champ, Json(v2Avant), Json(gardes)));
            }
            Comparer(ChampRoles, avant.Roles, commun.Roles, etat.Roles);
            Comparer(ChampMenus, avant.Menus, commun.Menus, etat.Menus);
            Comparer(ChampAncien, avant.Ancien, commun.Ancien, etat.Ancien);
            if (perdus.Count > 0) ecart = await EcrireEcartAsync(cn, tx, fiche, idV2, idV3, login, perdus, changement, ct);
        }
        await ExecuterAsync(cn, tx, """
            UPDATE rec.EtatLigne SET Ligne = @l, Le = SYSDATETIME(), Sens = 'V3 vers V2' WHERE BaseV2 = @b AND TableV2 = @t AND IdV2 = @id;
            IF @@ROWCOUNT = 0 INSERT INTO rec.EtatLigne (BaseV2, TableV2, IdV2, Ligne, Sens) VALUES (@b, @t, @id, @l, 'V3 vers V2');
            """, ct, ("@b", fiche.BaseV2), ("@t", TableEtat), ("@id", idV2), ("@l", etat.Ecrire()));
        return ecart;
    }

    public async Task SupprimerAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee fiche, string idV2, CancellationToken ct)
    {
        var b = Sql.Q(fiche.BaseV2) + ".dbo.";
        await ExecuterAsync(cn, tx, $"""
            DELETE FROM {b}HABILIT_UTILISATEUR_ROLE_USER WHERE HUROL_ID_UTLST = @u;
            DELETE FROM {b}EXPRESSO_DROITS WHERE EXPDR_ID_UTLST = @u;
            DECLARE @r int = (SELECT TOP (1) UTROL_ID_UTROL FROM {b}UTILISATEUR_ROLE WHERE UTROL_LIBELLE = @lib ORDER BY UTROL_ID_UTROL);
            DELETE FROM {b}UTILISATEUR_A_POUR_ROLE WHERE X_UTARL_ID_UTLST = @u;
            IF @r IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {b}UTILISATEUR_A_POUR_ROLE WHERE X_UTARL_ID_UTROL = @r)
            BEGIN
                DELETE FROM {b}COMPOSER_HABILITATION WHERE X_CPSHB_ID_UTROL = @r;
                DELETE FROM {b}UTILISATEUR_ROLE WHERE UTROL_ID_UTROL = @r;
            END;
            DELETE FROM rec.EtatLigne WHERE BaseV2 = @base AND TableV2 = @t AND IdV2 = @id;
            """, ct, ("@u", int.Parse(idV2, CultureInfo.InvariantCulture)), ("@lib", RoleAncienDuCompte(int.Parse(idV2, CultureInfo.InvariantCulture))),
            ("@base", fiche.BaseV2), ("@t", TableEtat), ("@id", idV2));
    }

    /// <summary>Le compte ne garde, dans l'ancien systeme, que SON role, porteur de ses degres (aucun degre : aucun role).</summary>
    private static async Task EcrireAncienAsync(SqlConnection cn, SqlTransaction tx, string b, int idUtlst, IReadOnlyDictionary<string, int> degres,
                                                IReadOnlyDictionary<string, string> references, CancellationToken ct)
    {
        var lignes = degres.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => new
        {
            r = references.TryGetValue(d.Key, out var brut) ? brut : d.Key,
            d = d.Value,
        }).ToList();
        if (lignes.FirstOrDefault(l => l.r.Length > 10) is { } longue)
            throw new RecopieRefusee($"ancien systeme : la reference « {longue.r} » depasse les 10 caracteres de COMPOSER_HABILITATION");
        await ExecuterAsync(cn, tx, $"""
            DECLARE @ids TABLE (Id int);
            DECLARE @r int = (SELECT TOP (1) UTROL_ID_UTROL FROM {b}UTILISATEUR_ROLE WHERE UTROL_LIBELLE = @lib ORDER BY UTROL_ID_UTROL);
            DELETE FROM {b}UTILISATEUR_A_POUR_ROLE WHERE X_UTARL_ID_UTLST = @u;
            IF @r IS NOT NULL DELETE FROM {b}COMPOSER_HABILITATION WHERE X_CPSHB_ID_UTROL = @r;
            IF @n = 0
            BEGIN
                IF @r IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {b}UTILISATEUR_A_POUR_ROLE WHERE X_UTARL_ID_UTROL = @r)
                    DELETE FROM {b}UTILISATEUR_ROLE WHERE UTROL_ID_UTROL = @r;
                RETURN;
            END;
            IF @r IS NULL
            BEGIN
                INSERT INTO {b}UTILISATEUR_ROLE (UTROL_LIBELLE, UTROL_CODE_DIRECTION) OUTPUT inserted.UTROL_ID_UTROL INTO @ids VALUES (@lib, NULL);
                SET @r = (SELECT Id FROM @ids);
            END;
            INSERT INTO {b}UTILISATEUR_A_POUR_ROLE (X_UTARL_ID_UTROL, X_UTARL_ID_UTLST) VALUES (@r, @u);
            INSERT INTO {b}COMPOSER_HABILITATION (X_CPSHB_DEGRE, X_CPSHB_REFERENCE_CTGR, X_CPSHB_ID_UTROL)
            SELECT j.d, j.r, @r FROM OPENJSON(@degres) WITH (r varchar(10) '$.r', d tinyint '$.d') AS j;
            """, ct, ("@u", idUtlst), ("@lib", RoleAncienDuCompte(idUtlst)), ("@n", lignes.Count), ("@degres", JsonSerializer.Serialize(lignes)));
    }

    /// <summary>L'ecart d'un compte dont la V2 avait change les droits depuis le dernier passage : chaque origine changee, la
    /// valeur de la V2 perdue et celle de la V3 gardee ; l'agent designe le voit dans « Fiches en conflit » (REC-004 CA01).</summary>
    private static async Task<long> EcrireEcartAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee fiche, string idV2, string idV3, string login,
                                                     List<(string Champ, string? V2, string? V3)> champs, ChangementJoint changement, CancellationToken ct)
    {
        var id = long.Parse((await ScalaireAsync(cn, tx, """
            INSERT INTO rec.Ecart (BaseV2, TableV2, IdV2, TableV3, IdV3, Fiche, Cible, DateV2, AuteurV2, DateV3, AuteurV3)
            OUTPUT CONVERT(nvarchar(20), inserted.Id) VALUES (@b, @t2, @id2, @t3, @id3, LEFT(@f, 300), @c, SYSDATETIME(), NULL, @d3, LEFT(@a3, 128));
            """, ct, ("@b", fiche.BaseV2), ("@t2", fiche.NomV2), ("@id2", idV2), ("@t3", fiche.NomV3), ("@id3", idV3),
            ("@f", $"{fiche.Regle.Libelle} {idV3} — {login} (droits changés dans la V2)"), ("@c", (object?)fiche.Regle.Cible ?? DBNull.Value),
            ("@d3", changement.Le), ("@a3", changement.Auteurs)))!, CultureInfo.InvariantCulture);
        foreach (var (champ, v2, v3) in champs)
            await ExecuterAsync(cn, tx, "INSERT INTO rec.EcartChamp (IdEcart, Champ, ValeurV2, ValeurV3) VALUES (@id, @c, @v2, @v3);", ct,
                                ("@id", id), ("@c", champ), ("@v2", (object?)v2 ?? DBNull.Value), ("@v3", (object?)v3 ?? DBNull.Value));
        return id;
    }

    private static async Task<List<string>> ListeAsync(SqlConnection cn, SqlTransaction tx, string sql, CancellationToken ct, params (string Nom, object Valeur)[] p)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 120 };
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        var liste = new List<string>();
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) liste.Add(l.GetString(0));
        return liste;
    }

    private static async Task<List<(string Fonction, string Origine)>> PairesAsync(SqlConnection cn, SqlTransaction tx, string sql, CancellationToken ct,
                                                                                   params (string Nom, object Valeur)[] p)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 120 };
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        var liste = new List<(string, string)>();
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct)) liste.Add((l.GetString(0), l.GetString(1)));
        return liste;
    }

    private static string Json(string texte) => JsonSerializer.Serialize(texte);

    private static string Resume(List<string> motifs) => string.Join(" ; ", motifs.Take(5)) + (motifs.Count > 5 ? $" ; et {motifs.Count - 5} autre(s)" : "");

    /// <summary>Les droits d'un compte dans la V2, tels que la recopie les compare d'un passage a l'autre.</summary>
    private sealed record EtatDroits(string Roles, string Menus, string Ancien)
    {
        public string Resume => $"rôles : {(Roles.Length == 0 ? "aucun" : Roles)} ; menus : {(Menus.Length == 0 ? "aucun" : Menus)} ; " +
                                $"ancien système : {(Ancien.Length == 0 ? "aucun degré" : Ancien)}";

        public static EtatDroits De(DonneesV2 v2, int idUtlst) => new(
            string.Join(", ", v2.ComptesRoles.Where(r => r.IdUtlst == idUtlst).Select(r => r.Role.TrimEnd()).OrderBy(Cle, StringComparer.Ordinal)),
            string.Join(", ", v2.MenusDuCompte(idUtlst).Order(StringComparer.Ordinal)),
            string.Join(", ", v2.DegresDuCompte(idUtlst).OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => $"{d.Key}={d.Value}")));

        public string Ecrire() => JsonSerializer.Serialize(new Dictionary<string, string> { ["roles"] = Roles, ["menus"] = Menus, ["ancien"] = Ancien });

        public static EtatDroits? Lire(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
            return new EtatDroits(d.GetValueOrDefault("roles") ?? "", d.GetValueOrDefault("menus") ?? "", d.GetValueOrDefault("ancien") ?? "");
        }
    }
}

/// <summary>
/// L'ANCIEN SYSTEME DE DROITS DE LA V2, vu d'un compte : ses degres par reference (le plus haut de ses roles anciens,
/// DonneesV2.DegresDuCompte) suffisent a sa reponse pour chaque fonction FONCVB6 (RepriseDroits.DegreAncien, portage de
/// GestionDroitRIG.cls:291-455). Resoudre cherche, a partir des degres actuels, ceux qui donnent EXACTEMENT les reponses
/// voulues, en changeant le moins possible :
///  - accorder : le degre de la fonction passe a 3 au moins ; une categorie superieure (domaine, activite) dont le degre
///    (2 ou moins) primerait est retiree au compte -- les fonctions voisines qui en dependaient sont reprises au tour suivant ;
///  - refuser : le degre de la fonction est retire s'il suffit, sinon mis a 0 (un degre initial nul refuse toujours,
///    GestionDroitRIG.cls:378) ;
///  - jusqu'a ce que tout reponde comme voulu. Ce que l'ancien systeme ne sait pas donner seul (fonction sans categorie, ou
///    une activite refusee sous un domaine accorde quand l'un de ses processus est accorde) reste « irrepresentable » :
///    nomme, jamais ecrit a peu pres.
/// </summary>
internal static class AncienSysteme
{
    public static Dictionary<string, int> Resoudre(DonneesV2 v2, IReadOnlyDictionary<string, int> actuels, IReadOnlyList<(string Code, bool Voulu)> voulus,
                                                   List<string> irrepresentables)
    {
        var degres = new Dictionary<string, int>(actuels, StringComparer.Ordinal);
        bool Accorde(IReadOnlyDictionary<string, int> d, string code) => RepriseDroits.DegreAncien(v2, d, code) is > 2;
        var sansIssue = new HashSet<string>(StringComparer.Ordinal);
        // Chaque tour corrige au moins une fonction ou s'arrete ; une boucle sans fin (deux fonctions qui se disputent une
        // categorie superieure) est coupee et ses fonctions sont dites irrepresentables.
        for (var tour = 0; tour < 4 * voulus.Count + 8; tour++)
        {
            var fausses = voulus.Where(v => !sansIssue.Contains(v.Code) && Accorde(degres, v.Code) != v.Voulu).ToList();
            if (fausses.Count == 0) break;
            foreach (var (code, voulu) in fausses)
            {
                if (Accorde(degres, code) == voulu) continue;
                if (voulu) { if (!Accorder(v2, degres, code)) sansIssue.Add(code); }
                else Refuser(v2, degres, code);
            }
        }
        foreach (var (code, voulu) in voulus.Where(v => Accorde(degres, v.Code) != v.Voulu))
            irrepresentables.Add(sansIssue.Contains(code)
                ? $"{code} : l'ancien système de la V2 ne peut pas l'accorder (aucune catégorie qui lui donne un degré)"
                : $"{code} : l'ancien système de la V2 ne peut pas {(voulu ? "l'accorder" : "le refuser")} sans changer une autre fonction de sa catégorie");
        return degres;
    }

    private static bool Accorder(DonneesV2 v2, Dictionary<string, int> degres, string code)
    {
        var cle = DonneesV2.Cle(code);
        if (!v2.PremiereCategorie.TryGetValue(cle, out var cat) || cat.Niveau is not (1 or 2 or 4 or 5 or 6)) return false;
        degres[cle] = Math.Max(degres.TryGetValue(cle, out var d) ? d : -1, DroitsRecopies.DegreAccorde);
        if (cat.Niveau is not 1)
        {
            if (cat.IdDomaine is not { } domaine) return false;   // CLng("") : la V2 leve, donc refuse
            Lever(domaine);
        }
        if (cat.Niveau is 4)
        {
            if (cat.IdActivite is not { } activite) return false;
            Lever(activite);
        }
        // Un processus dont le degre depasse celui de son domaine, sans activite, est refuse (GestionDroitRIG.cls:401-411) : 3 juste.
        if (RepriseDroits.DegreAncien(v2, degres, code) is not > 2) degres[cle] = DroitsRecopies.DegreAccorde;
        return true;

        void Lever(int idCategorie)
        {
            if (v2.Categories.TryGetValue(idCategorie, out var sup) && degres.TryGetValue(DonneesV2.Cle(sup.Reference), out var ds) && ds <= 2)
                degres.Remove(DonneesV2.Cle(sup.Reference));
        }
    }

    private static void Refuser(DonneesV2 v2, Dictionary<string, int> degres, string code)
    {
        var cle = DonneesV2.Cle(code);
        var sans = new Dictionary<string, int>(degres, StringComparer.Ordinal);
        sans.Remove(cle);
        if (RepriseDroits.DegreAncien(v2, sans, code) is not > 2) degres.Remove(cle);
        else degres[cle] = 0;
    }

    public static bool Memes(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
        => a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var y) && y == x.Value);

    /// <summary>Cle SQL -> la reference telle que la V2 l'ecrit (degres, categories, fonctions), pour la reecrire a l'identique.</summary>
    public static IReadOnlyDictionary<string, string> References(DonneesV2 v2)
    {
        var r = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var texte in v2.Degres.Select(d => d.Reference).Concat(v2.Categories.Values.Select(c => c.Reference)).Concat(v2.Fonctions.Select(f => f.Code)))
            r.TryAdd(DonneesV2.Cle(texte), texte.TrimEnd());
        return r;
    }
}
