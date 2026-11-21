using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Comptes;

/// <summary>
/// CREER, MODIFIER, DESACTIVER, SUPPRIMER UN COMPTE ET DONNER SES ROLES (SOC-004 T02, SOC-005 T02).
///
/// Chaque geste est UNE transaction : le compte, ses roles, le journal des gestes (SOC-016), le journal
/// des droits et l'evenement 130 (SOC-005) sont ecrits ensemble ou pas du tout.
/// Regles de la V2, relues avant de coder :
///  - compte deja enregistre (sans tenir compte des majuscules) : « Ce login existe deja : X »
///    (OPE_EDITION_UTILISATEUR.cs:156-159) ;
///  - abrege = les 16 premiers caracteres du compte, code greffe 9999 quand un agent d'Amitel cree le
///    compte, sinon celui du greffe (OPE_EDITION_UTILISATEUR.cs:165-167) ;
///  - un responsable hors Amitel ne voit pas les comptes marques administrateur
///    (OPE_RECHERCHE_UTILISATEUR.cs:276) et ne touche pas un compte Amitel (OPE_EDITION_UTILISATEUR.cs:301).
/// Ecart retenu par la story (SOC-004 CA04, « a valider ») : la V2 supprime un compte en reattribuant
/// ses demandes a un autre (OPE_SUPPRESSION_UTILISATEUR.cs:38-58) ; la V3 refuse de supprimer un compte
/// qui a agi et propose de le desactiver -- l'auteur d'un geste ne change jamais.
///
/// Chaque methode travaille sur <c>cn</c>, la base V3 du greffe de la requete que la route a ouverte par le
/// catalogue commun (lot 9, <see cref="ServiceDroits.OuvrirBaseAsync"/>) : un compte cree dans un greffe n'existe
/// que dans la base de ce greffe.
/// </summary>
public sealed class ServiceComptes
{
    /// <summary>Le code greffe d'un compte cree par un agent d'Amitel (V2 : OPE_EDITION_UTILISATEUR.cs:165).</summary>
    public const string CodeGreffeAmitel = "9999";

    private const string Famille = "compte";

    // ---------------------------------------------------------------- lecture

    /// <summary>Ce que l'ecran affiche, filtre selon l'appelant (SOC-002 CA03).</summary>
    public async Task<VueComptes> VueAsync(SqlConnection cn, Appelant appelant, CancellationToken ct)
    {
        var comptes = (await LireComptesAsync(cn, null, null, ct)).Where(c => appelant.EstAmitel || !c.EstAdministrateur).ToArray();
        var services = new List<ServiceGreffe>();
        await using (var cmd = new SqlCommand("""
            SELECT Id, Abrege, Nom, CodeDirection, DistributionAuto, DistributionSpecifique, HeureDistribution
            FROM soc.Service ORDER BY Abrege;
            """, cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct))
                services.Add(new ServiceGreffe(l.GetInt32(0), l.GetString(1), l.IsDBNull(2) ? null : l.GetString(2),
                    l.IsDBNull(3) ? null : l.GetString(3), l.GetBoolean(4), l.GetBoolean(5),
                    l.IsDBNull(6) ? null : l.GetDateTime(6).ToString("HH:mm")));
        var roles = (await LireRolesAsync(cn, null, ct)).Where(r => appelant.Voit(r.ReserveAmitel, r.ReserveAdministrateur)).ToArray();
        return new VueComptes(comptes, [.. services], roles, appelant.NomAffiche);
    }

    // ---------------------------------------------------------------- gestes

    /// <summary>SOC-004 CA01, CA02, CA07 : creer un compte actif, unique sans majuscules, avec son abrege et son code greffe.</summary>
    /// <param name="greffe">Le greffe de la requete (son code au catalogue) : le code greffe d'un compte que le greffe cree,
    /// comme le RIG.Common.CodeGreffeRig de la V2 (OPE_EDITION_UTILISATEUR.cs:165).</param>
    public async Task<ResultatCompte> CreerAsync(SqlConnection cn, Appelant appelant, string greffe, DemandeCompte d, CancellationToken ct)
    {
        if (Valider(d) is { } invalide) return Refus(invalide);
        var login = d.CompteWindows.Trim();
        var codeGreffe = appelant.EstAmitel ? CodeGreffeAmitel : greffe;

        if (await CompteParLoginAsync(cn, null, login, ct) is not null) return Refus(MessageDejaPris(login));
        var roles = (d.Roles ?? []).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var connus = await LireRolesAsync(cn, null, ct);
        foreach (var r in roles)
            if (connus.FirstOrDefault(x => string.Equals(x.Nom, r, StringComparison.OrdinalIgnoreCase)) is not { } role || !appelant.Voit(role.ReserveAmitel, role.ReserveAdministrateur))
                return Refus(MessageRoleNonAttribuable(r));

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        int id;
        try
        {
            await using var cmd = new SqlCommand("""
                INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, DateCreation)
                OUTPUT INSERTED.Id
                VALUES (@login, @nom, @prenom, @abrege, @civilite, @greffe, @service, 0, GETDATE());
                """, cn, tx);
            cmd.Parameters.Add(new SqlParameter("@login", SqlDbType.VarChar, 35) { Value = login });
            cmd.Parameters.Add(new SqlParameter("@nom", SqlDbType.VarChar, 35) { Value = d.Nom.Trim() });
            cmd.Parameters.Add(new SqlParameter("@prenom", SqlDbType.VarChar, 35) { Value = d.Prenom.Trim() });
            cmd.Parameters.Add(new SqlParameter("@abrege", SqlDbType.VarChar, 16) { Value = Abrege(login) });
            cmd.Parameters.Add(new SqlParameter("@civilite", SqlDbType.VarChar, 7) { Value = d.Civilite.Trim() });
            cmd.Parameters.Add(new SqlParameter("@greffe", SqlDbType.VarChar, 4) { Value = codeGreffe });
            cmd.Parameters.Add(new SqlParameter("@service", SqlDbType.Int) { Value = d.IdService!.Value });
            id = (int)(await cmd.ExecuteScalarAsync(ct))!;
        }
        catch (SqlException e) when (e.Number is 2627 or 2601)
        {
            // Deux creations du meme compte au meme instant : l'unicite de la base tranche.
            await tx.RollbackAsync(ct);
            return Refus(MessageDejaPris(login));
        }
        var cree = (await LireComptesAsync(cn, tx, id, ct))[0];
        await JournalGestes.EcrireAsync(cn, tx, appelant, d.Ecran, Famille, $"le compte {login}", id.ToString(), "a cree", null, Instantane(cree), ct);
        foreach (var r in roles)
            await DonnerRoleAsync(cn, tx, appelant, d.Ecran, cree, connus.First(x => string.Equals(x.Nom, r, StringComparison.OrdinalIgnoreCase)).Nom, ajouter: true, ct);
        await tx.CommitAsync(ct);
        return new ResultatCompte(true, null, (await LireComptesAsync(cn, null, id, ct))[0]);
    }

    /// <summary>SOC-004 : corriger le compte, le nom, le prenom, la civilite ou le service d'un compte.</summary>
    public async Task<ResultatCompte> ModifierAsync(SqlConnection cn, Appelant appelant, DemandeCompte d, CancellationToken ct)
    {
        if (d.Id is not { } id) return Refus("Aucun compte designe.");
        if (Valider(d) is { } invalide) return Refus(invalide);
        var login = d.CompteWindows.Trim();
        if (await CompteGerableAsync(cn, appelant, id, ct) is not { } avant) return RefusIntrouvable();
        // V2 : le doublon n'est cherche que si le compte change (OPE_EDITION_UTILISATEUR.cs:156).
        if (!string.Equals(login, avant.CompteWindows, StringComparison.Ordinal)
            && await CompteParLoginAsync(cn, null, login, ct) is { } autre && autre.Id != id)
            return Refus(MessageDejaPris(login));

        var apres = avant with { CompteWindows = login, Nom = d.Nom.Trim(), Prenom = d.Prenom.Trim(), Civilite = d.Civilite.Trim(), Abrege = Abrege(login), IdService = d.IdService };
        if (Instantane(apres).Equals(Instantane(avant)))
            return new ResultatCompte(true, "Aucun changement.", avant);

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = new SqlCommand("""
                UPDATE soc.Compte SET CompteWindows = @login, Nom = @nom, Prenom = @prenom, Civilite = @civilite,
                                      Abrege = @abrege, IdService = @service
                WHERE Id = @id;
                """, cn, tx);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            cmd.Parameters.Add(new SqlParameter("@login", SqlDbType.VarChar, 35) { Value = login });
            cmd.Parameters.Add(new SqlParameter("@nom", SqlDbType.VarChar, 35) { Value = apres.Nom });
            cmd.Parameters.Add(new SqlParameter("@prenom", SqlDbType.VarChar, 35) { Value = apres.Prenom });
            cmd.Parameters.Add(new SqlParameter("@civilite", SqlDbType.VarChar, 7) { Value = apres.Civilite });
            cmd.Parameters.Add(new SqlParameter("@abrege", SqlDbType.VarChar, 16) { Value = apres.Abrege });
            cmd.Parameters.Add(new SqlParameter("@service", SqlDbType.Int) { Value = d.IdService!.Value });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException e) when (e.Number is 2627 or 2601)
        {
            await tx.RollbackAsync(ct);
            return Refus(MessageDejaPris(login));
        }
        var relu = (await LireComptesAsync(cn, tx, id, ct))[0];
        await JournalGestes.EcrireAsync(cn, tx, appelant, d.Ecran, Famille, $"le compte {login}", id.ToString(), "a modifie",
            Instantane(avant), Instantane(relu), ct);
        await tx.CommitAsync(ct);
        return new ResultatCompte(true, null, relu);
    }

    /// <summary>SOC-004 CA03 : desactiver un compte ; il n'ouvre plus RIG, ses gestes gardent son nom.</summary>
    public async Task<ResultatCompte> DesactiverAsync(SqlConnection cn, Appelant appelant, DemandeSurCompte d, CancellationToken ct)
    {
        if (await CompteGerableAsync(cn, appelant, d.Id, ct) is not { } avant) return RefusIntrouvable();
        if (avant.Desactive) return new ResultatCompte(false, $"Le compte {avant.CompteWindows} est déjà désactivé.", avant);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await using (var cmd = new SqlCommand("UPDATE soc.Compte SET Desactive = 1 WHERE Id = @id;", cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = d.Id });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var relu = (await LireComptesAsync(cn, tx, d.Id, ct))[0];
        await JournalGestes.EcrireAsync(cn, tx, appelant, d.Ecran, Famille, $"le compte {avant.CompteWindows}", d.Id.ToString(), "a desactive",
            new { desactive = false }, new { desactive = true }, ct);
        await tx.CommitAsync(ct);
        return new ResultatCompte(true, null, relu);
    }

    /// <summary>
    /// SOC-004 CA04 : seul un compte qui n'a JAMAIS agi se supprime. A agi : il est l'auteur d'une ligne
    /// d'un journal de la V3, ou il vient de la V2 (sa trace -- demandes, factures -- y est encore).
    /// </summary>
    public async Task<ResultatCompte> SupprimerAsync(SqlConnection cn, Appelant appelant, DemandeSurCompte d, CancellationToken ct)
    {
        if (await CompteGerableAsync(cn, appelant, d.Id, ct) is not { } avant) return RefusIntrouvable();
        if (await AAgiAsync(cn, d.Id, ct))
            return new ResultatCompte(false,
                $"Le compte {avant.CompteWindows} a déjà agi : il ne peut pas être supprimé, son nom doit rester l'auteur de ses gestes. Désactivez-le.",
                avant, ProposerDesactivation: true);

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        foreach (var role in avant.Roles)
            await JournalGestes.EcrireDroitAsync(cn, tx, appelant, d.Ecran, avant.Id, avant.CompteWindows, role, ajout: false, ct);
        await using (var cmd = new SqlCommand("""
            DELETE FROM soc.CompteRole WHERE IdCompte = @id;
            DELETE FROM soc.CompteFonction WHERE IdCompte = @id;
            DELETE FROM soc.Compte WHERE Id = @id;
            """, cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = d.Id });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await JournalGestes.EcrireAsync(cn, tx, appelant, d.Ecran, Famille, $"le compte {avant.CompteWindows}", d.Id.ToString(), "a supprime",
            Instantane(avant), null, ct);
        await tx.CommitAsync(ct);
        return new ResultatCompte(true, null, null);
    }

    /// <summary>SOC-005 CA01, CA02 et SOC-002 CA03 : donner ou retirer un role que l'appelant peut attribuer.</summary>
    public async Task<ResultatCompte> RoleAsync(SqlConnection cn, Appelant appelant, DemandeRole d, CancellationToken ct)
    {
        if (await CompteGerableAsync(cn, appelant, d.IdCompte, ct) is not { } compte) return RefusIntrouvable();
        var role = (await LireRolesAsync(cn, null, ct)).FirstOrDefault(r => string.Equals(r.Nom, (d.Role ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (role is null || !appelant.Voit(role.ReserveAmitel, role.ReserveAdministrateur)) return Refus(MessageRoleNonAttribuable(d.Role ?? ""), compte);
        var aDeja = compte.Roles.Contains(role.Nom, StringComparer.OrdinalIgnoreCase);
        if (d.Ajouter && aDeja) return new ResultatCompte(false, $"Le compte {compte.CompteWindows} a déjà le rôle « {role.Nom} ».", compte);
        if (!d.Ajouter && !aDeja) return new ResultatCompte(false, $"Le compte {compte.CompteWindows} n'a pas le rôle « {role.Nom} ».", compte);

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await DonnerRoleAsync(cn, tx, appelant, d.Ecran, compte, role.Nom, d.Ajouter, ct);
        await tx.CommitAsync(ct);
        return new ResultatCompte(true, null, (await LireComptesAsync(cn, null, d.IdCompte, ct))[0]);
    }

    /// <summary>Le changement de role, ses deux journaux et, s'il touche la comptabilite, l'evenement 130.</summary>
    private static async Task DonnerRoleAsync(SqlConnection cn, SqlTransaction tx, Appelant appelant, string? ecran,
                                              CompteAgent compte, string role, bool ajouter, CancellationToken ct)
    {
        await using (var cmd = new SqlCommand(ajouter
            ? "INSERT INTO soc.CompteRole (IdCompte, Role) VALUES (@id, @role);"
            : "DELETE FROM soc.CompteRole WHERE IdCompte = @id AND Role = @role;", cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = compte.Id });
            cmd.Parameters.Add(new SqlParameter("@role", SqlDbType.VarChar, 40) { Value = role });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await JournalGestes.EcrireDroitAsync(cn, tx, appelant, ecran, compte.Id, compte.CompteWindows, role, ajouter, ct);
        var apres = ajouter ? [.. compte.Roles, role] : compte.Roles.Where(r => !string.Equals(r, role, StringComparison.OrdinalIgnoreCase)).ToArray();
        await JournalGestes.EcrireAsync(cn, tx, appelant, ecran, Famille, $"les rôles du compte {compte.CompteWindows}", compte.Id.ToString(), "a modifie",
            new { roles = compte.Roles.Order(StringComparer.OrdinalIgnoreCase) }, new { roles = apres.Order(StringComparer.OrdinalIgnoreCase) }, ct);

        // SOC-005 CA02 : le role ouvre-t-il une fonction de facturation ou d'encaissement (domaine Comptabilite de la V2) ?
        await using var compta = new SqlCommand("""
            SELECT TOP (5) f.Code FROM soc.RoleFonction rf JOIN soc.Fonction f ON f.Code = rf.Fonction
            WHERE rf.Role = @role AND f.Comptabilite = 1 ORDER BY f.Code;
            """, cn, tx);
        compta.Parameters.Add(new SqlParameter("@role", SqlDbType.VarChar, 40) { Value = role });
        var fonctions = new List<string>();
        await using (var l = await compta.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct)) fonctions.Add(l.GetString(0));
        if (fonctions.Count > 0)
            await JournalGestes.EcrireEvenementAsync(cn, tx, 130, "Habilitation d'utilisateur/Gestion des droits",
                $"{(ajouter ? "Ajout" : "Retrait")} du rôle {role} au compte {compte.CompteWindows} par {appelant.NomAffiche} ; fonctions de facturation ou d'encaissement : {string.Join(", ", fonctions)}{(fonctions.Count == 5 ? "…" : "")}",
                appelant, ct);
    }

    // ---------------------------------------------------------------- regles et lectures

    /// <summary>V2 (OPE_EDITION_UTILISATEUR.cs:166-167) : plus de 15 caracteres -> les 16 premiers, sinon le compte entier.</summary>
    public static string Abrege(string login) => login.Length > 15 ? login[..16] : login;

    /// <summary>Le texte de la V2 (OPE_EDITION_UTILISATEUR.cs:157).</summary>
    public static string MessageDejaPris(string login) => $"Ce login existe déjà : {login}";

    private static string MessageRoleNonAttribuable(string role) => $"Le rôle « {role.Trim()} » n'est pas attribuable par ce compte.";

    private static string? Valider(DemandeCompte d)
    {
        var login = (d.CompteWindows ?? "").Trim();
        if (login.Length == 0) return "Le compte Windows est obligatoire.";
        if (login.Length > 35) return "Le compte Windows a 35 caractères au plus.";
        if (login.Contains('\\') || login.Contains('@')) return "Le compte Windows s'écrit sans son domaine (ex. jdupont).";
        if (string.IsNullOrWhiteSpace(d.Nom) || d.Nom.Trim().Length > 35) return "Le nom est obligatoire (35 caractères au plus).";
        if (string.IsNullOrWhiteSpace(d.Prenom) || d.Prenom.Trim().Length > 35) return "Le prénom est obligatoire (35 caractères au plus).";
        if (string.IsNullOrWhiteSpace(d.Civilite) || d.Civilite.Trim().Length > 7) return "La civilité est obligatoire (7 caractères au plus).";
        if (d.IdService is null) return "Le service est obligatoire : un compte sans service n'ouvre pas RIG.";
        return null;
    }

    private static ResultatCompte Refus(string message, CompteAgent? compte = null) => new(false, message, compte);

    private static ResultatCompte RefusIntrouvable()
        => new(false, "Ce compte n'existe pas, ou il est géré par Amitel.", null);

    /// <summary>Le compte, s'il existe et si l'appelant peut le gerer (regles Amitel de la V2) ; sinon null.</summary>
    private static async Task<CompteAgent?> CompteGerableAsync(SqlConnection cn, Appelant appelant, int id, CancellationToken ct)
    {
        var lus = await LireComptesAsync(cn, null, id, ct);
        if (lus.Length == 0) return null;
        var c = lus[0];
        if (!appelant.EstAmitel && (c.EstAmitel || c.EstAdministrateur)) return null;
        return c;
    }

    private static async Task<bool> AAgiAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM soc.Compte WHERE Id = @id AND IdV2 IS NOT NULL)
                          OR EXISTS (SELECT 1 FROM soc.Journal WHERE IdAgent = @id)
                          OR EXISTS (SELECT 1 FROM soc.JournalDroits WHERE IdAuteur = @id)
                          OR EXISTS (SELECT 1 FROM soc.EvenementTechnique WHERE IdAuteur = @id)
                        THEN 1 ELSE 0 END;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        return (int)(await cmd.ExecuteScalarAsync(ct))! == 1;
    }

    private static async Task<CompteAgent?> CompteParLoginAsync(SqlConnection cn, SqlTransaction? tx, string login, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT Id FROM soc.Compte WHERE CompteWindows = @login;", cn, tx);
        cmd.Parameters.Add(new SqlParameter("@login", SqlDbType.VarChar, 35) { Value = login });
        return await cmd.ExecuteScalarAsync(ct) is int id ? (await LireComptesAsync(cn, tx, id, ct)).FirstOrDefault() : null;
    }

    /// <summary>Les comptes (un seul si <paramref name="id"/> est pose), avec leurs roles.</summary>
    internal static async Task<CompteAgent[]> LireComptesAsync(SqlConnection cn, SqlTransaction? tx, int? id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT c.Id, c.CompteWindows, c.Nom, c.Prenom, c.Civilite, c.Abrege, c.CodeGreffe, c.IdService,
                   COALESCE(s.Nom, s.Abrege, ''), c.Desactive, c.EstAmitel, c.EstAdministrateur,
                   (SELECT STRING_AGG(r.Role, '|') WITHIN GROUP (ORDER BY r.Role) FROM soc.CompteRole r WHERE r.IdCompte = c.Id)
            FROM soc.Compte c LEFT JOIN soc.Service s ON s.Id = c.IdService
            WHERE @id IS NULL OR c.Id = @id
            ORDER BY c.Nom, c.Prenom, c.CompteWindows;
            """, cn, tx);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = (object?)id ?? DBNull.Value });
        var comptes = new List<CompteAgent>();
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
            comptes.Add(new CompteAgent(l.GetInt32(0), l.GetString(1), l.GetString(2), l.GetString(3), l.GetString(4), l.GetString(5),
                l.IsDBNull(6) ? null : l.GetString(6), l.IsDBNull(7) ? null : l.GetInt32(7), l.GetString(8),
                l.GetBoolean(9), l.GetBoolean(10), l.GetBoolean(11),
                l.IsDBNull(12) ? [] : l.GetString(12).Split('|')));
        return [.. comptes];
    }

    /// <summary>Tous les roles, avec leurs reserves et leur lien a la comptabilite.</summary>
    internal static async Task<RoleVisible[]> LireRolesAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT r.Nom, r.Libelle, r.ReserveAmitel, r.ReserveAdministrateur,
                   CASE WHEN EXISTS (SELECT 1 FROM soc.RoleFonction rf JOIN soc.Fonction f ON f.Code = rf.Fonction
                                     WHERE rf.Role = r.Nom AND f.Comptabilite = 1) THEN 1 ELSE 0 END
            FROM soc.Role r ORDER BY r.Nom;
            """, cn, tx);
        var roles = new List<RoleVisible>();
        await using var l = await cmd.ExecuteReaderAsync(ct);
        while (await l.ReadAsync(ct))
            roles.Add(new RoleVisible(l.GetString(0), l.GetString(1), l.GetBoolean(2), l.GetBoolean(3), l.GetInt32(4) == 1));
        return [.. roles];
    }

    /// <summary>Les valeurs d'un compte qu'un geste peut changer : l'avant et l'apres du journal (SOC-016 CA01).</summary>
    private static InstantaneCompte Instantane(CompteAgent c)
        => new(c.CompteWindows, c.Nom, c.Prenom, c.Civilite, c.Abrege, c.IdService, c.Service, c.CodeGreffe, c.Desactive);

    private sealed record InstantaneCompte(string Compte, string Nom, string Prenom, string Civilite, string Abrege,
                                           int? IdService, string Service, string? CodeGreffe, bool Desactive);
}
