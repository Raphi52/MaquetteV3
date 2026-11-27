using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurRemplacementActeur;
using static RigV3.Serveur.Socle.EcransGeneres.LiaisonsActeur;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Remplacer un acteur » (PROC_SUPP_ACTEUR_JUD) : CONFIRMER le remplacement d'UN acteur (juge, parquetier, greffier assermente,
/// mandataire PC, intervenant PC) par un autre du meme type -- ses liens passent au remplacant, la trace va dans ACTEUR_SUPP, puis il est
/// supprime. Mode d'emploi : EcrivainEcran.cs ; les lignes et l'apercu : LecteurRemplacementActeur.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : OPE_SUPP_ACTEUR_JUD (D:\RigApplication\Source\RIG\DLL\Processus\PROC_SUPP_ACTEUR_JUD), messages au mot pres :
///   - UN acteur a la fois : la V2 s'ouvre sur l'acteur qu'on supprime (parametres typeActeur et idActeur, PROC_SUPP_ACTEUR_JUD.cs:32-37) ;
///   - VERIFIER (OnProcessusEventBeforeChild, DoVerifier, l. 115-176) : remplacant vide -> « Vous n'avez pas sélectionné de juge » (de
///     mandataire PC, de parquetier, de greffier assermenté, d'intervenant PC reper) ; le meme acteur -> « Vous ne pouvez pas sélectionner
///     le même juge que celui que vous allez supprimer » ; un remplacant supprime entre-temps -> « Le parquetier que vous avez choisi
///     n'existe plus, il a été supprimé » (sauf le juge, que la V2 ne revoit pas). La liste de la V2 ne propose que des acteurs actifs
///     (CboUtilisableEnSaisie, Juge.cs:715, GreffierAssermente.cs:383) : un remplacant inactif est refuse ;
///   - LA REPRISE (_UpdateLink, l. 796-1036), dans UNE transaction : chaque liaison (LiaisonsActeur.cs) passe au remplacant ; la trace
///     ACTEUR_SUPP (_CreateActeurSupp, l. 475-531 : la table, l'id supprime, l'id du remplacant, la requete « update <table> set <colonne> =
///     <remplacant> where <cle> in (...) » de chaque liaison -- ActeurSupp.GetSqlUpdate / CloseIN --, la date, l'auteur, les deux noms
///     « NomComplet (abrege) ») ; puis l'acteur est supprime (l. 1003-1025). « Aucune mise à jour à faire » si rien n'etait lie (l. 791-793).
/// NON REPRIS (nommes) : la regeneration des destinataires d'edition de la procedure collective apres la reprise d'un juge-commissaire ou
/// d'un mandataire PC (destinataireEditionLot2.Sauve, l. 869-875, 913-919 : le XML DESED_XML_DESTEDIT est recalcule depuis toute la
/// procedure par RIG v2, calcul que la V3 n'a pas) -- les editions suivantes de ces procedures citent encore l'ancien acteur jusqu'a leur
/// prochaine mise a jour dans RIG v2 ; le nettoyage « anticipe » du juge (instances et decisions remises a vide, l. 990-1002), sans effet
/// apres la reprise ; la demande « Rapprochement <type> : <supprime> par <remplacant> » (_CreerDemande, l. 1038-1046), une trace que le
/// journal des gestes remplace (decision du plan) ; les caches de RIG v2 (l. 1004-1025).
/// TRACE : famille « reglage », « a supprime » (avant : l'acteur et ses liaisons ; apres : le remplacant).
/// </summary>
internal sealed class EcrivainRemplacementActeur : IEcrivainEcran
{
    public string Procedure => "PROC_SUPP_ACTEUR_JUD";
    public string Ecran => "Remplacer un acteur";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    internal const string AucuneMiseAJour = "Aucune mise à jour à faire";

    private sealed record Acteur(int Id, string Nom, string Abrege, bool Actif)
    {
        public string NomComplet => NomAbrege(Nom, Abrege);
    }

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids;
        if (ids.Length == 0) return IssueGeste.Refuser("Cochez l'acteur à remplacer.");
        if (ids.Length > 1) return IssueGeste.Refuser("Un seul acteur à la fois : RIG v2 remplace l'acteur que l'on supprime.");
        if (Lire(ids[0]) is not { } lu) return IssueGeste.Refuser($"« {ids[0]} » n'est pas un acteur à remplacer (un remplacement déjà fait ne se rejoue pas).");
        var (t, idSupprime) = lu;
        if (await ActeurAsync(geste, t, idSupprime, ct) is not { } supprime)
            return IssueGeste.Refuser($"{t.Libelle} introuvable dans la base du greffe ({idSupprime}).");

        var saisie = geste.Valeur(SectionRemplacant, ChampRemplacant)?.Trim() ?? "";
        if (saisie.Length == 0) return IssueGeste.Refuser($"Vous n'avez pas sélectionné {t.Aucun}");
        var idRemplacant = await ResoudreAsync(geste, t, saisie, ct);
        if (idRemplacant is null)
            return IssueGeste.Refuser(t.Disparu ?? $"« {ChampRemplacant} » : aucun {t.Libelle.ToLowerInvariant()} ne porte le numéro ou l'abrégé « {saisie} ».");
        if (idRemplacant == idSupprime) return IssueGeste.Refuser($"Vous ne pouvez pas sélectionner le {t.LeMeme} que celui que vous allez supprimer");
        var remplacant = (await ActeurAsync(geste, t, idRemplacant.Value, ct))!;
        if (!remplacant.Actif)
            return IssueGeste.Refuser($"{remplacant.NomComplet} est inactif : la liste de RIG v2 ne propose que des acteurs actifs.");

        // _UpdateLink (l. 796-1036) : chaque liaison passe au remplacant ; la requete de la trace, liaison par liaison (ActeurSupp.cs:162-177).
        var requete = new StringBuilder();
        var reprises = new List<string>();
        foreach (var liaison in t.Liaisons)
        {
            var lignes = new List<string>();
            await using (var r = await geste.LireAsync($"SELECT {liaison.Cle} FROM v2.{liaison.Table} WHERE {liaison.Condition("@id")} ORDER BY {liaison.Cle}", ct,
                             new SqlParameter("@id", idSupprime)))
                while (await r.ReadAsync(ct)) lignes.Add(Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!);
            if (lignes.Count == 0) continue;
            await geste.EcrireAsync($"UPDATE v2.{liaison.Table} SET {liaison.Colonne} = @remplacant WHERE {liaison.Condition("@id")}", ct,
                new SqlParameter("@remplacant", idRemplacant.Value), new SqlParameter("@id", idSupprime));
            requete.Append(CultureInfo.InvariantCulture, $"update {liaison.Table} set {liaison.Colonne} = {idRemplacant} where {liaison.Cle} in ({string.Join(", ", lignes)})\r\n");
            reprises.Add($"{liaison.Libelle} ({lignes.Count})");
        }
        if (requete.Length == 0) requete.Append(AucuneMiseAJour);
        await geste.InsererAsync("v2.ACTEUR_SUPP", "ACTSUP_ID_ACTSUP", new Dictionary<string, object?>
        {
            ["ACTSUP_TABLE_SUPP"] = t.Table, ["ACTSUP_ID_SUPP"] = idSupprime, ["ACTSUP_ID_REMPL"] = idRemplacant.Value, ["ACTSUP_REQUETE"] = requete.ToString(),
            ["ACTSUP_DATE_CREATION"] = geste.Maintenant, ["ACTSUP_AUTEUR"] = await AuteurAsync(geste, ct),
            ["ACTSUP_SUPP_DESIGNATION"] = Couper(supprime.NomComplet), ["ACTSUP_REMPL_DESIGNATION"] = Couper(remplacant.NomComplet),
        }, ct);
        await geste.EcrireAsync($"DELETE FROM v2.{t.Table} WHERE {t.Cle} = @id", ct, new SqlParameter("@id", idSupprime));
        return IssueGeste.Ecrit($"{supprime.NomComplet} est remplacé par {remplacant.NomComplet}"
                                + (reprises.Count == 0 ? " : aucune liaison à reprendre." : $" : {string.Join(", ", reprises)} lui sont reportés."),
            [$"{t.Code}-{idRemplacant}"],
            new TraceGeste("reglage", $"le {t.Libelle.ToLowerInvariant()} {supprime.NomComplet}", idSupprime.ToString(CultureInfo.InvariantCulture), "a supprime",
                new { Acteur = supprime.NomComplet, Liaisons = reprises }, new { RemplacePar = remplacant.NomComplet, IdRemplacant = idRemplacant }));
    }

    private static string Couper(string texte) => texte.Length <= 200 ? texte : texte[..200];

    /// <summary>Le remplacant saisi : son numero, ou son abrege (le premier actif s'il y en a plusieurs) ; null s'il n'existe pas.</summary>
    private static async Task<int?> ResoudreAsync(GesteEnCours geste, TypeActeur t, string saisie, CancellationToken ct)
    {
        // fix-ok: mesure au banc le 27/11/2026 -- l'intervenant PC n'a pas de colonne « actif » (Actif = « 1 », toujours proposable) et
        // une constante ne se trie pas (« A constant expression was encountered in the ORDER BY list », erreur 500 des blocs 7-8) ; l'autre
        // retouche de ce fichier : GesteEcran.Ids est un tableau (Length, pas Count, erreur de compilation).
        var actifsDabord = t.Actif == "1" ? "" : $"CAST({t.Actif} AS int) DESC, ";
        await using var r = await geste.LireAsync($"""
            SELECT TOP (1) a.{t.Cle} FROM v2.{t.Table} AS a
            WHERE CAST(a.{t.Cle} AS varchar(12)) = @s OR {t.Abrege} = @s
            ORDER BY CASE WHEN CAST(a.{t.Cle} AS varchar(12)) = @s THEN 0 ELSE 1 END, {actifsDabord}a.{t.Cle}
            """, ct, new SqlParameter("@s", saisie));
        return await r.ReadAsync(ct) ? r.GetInt32(0) : null;
    }

    private static async Task<Acteur?> ActeurAsync(GesteEnCours geste, TypeActeur t, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync($"""
            SELECT {t.Nom} AS Nom, ISNULL({t.Abrege}, '') AS Abrege, CAST({t.Actif} AS bit) AS Actif FROM v2.{t.Table} AS a WITH (UPDLOCK) WHERE a.{t.Cle} = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Acteur(id, FormeLigne.Texte(r, "Nom"), FormeLigne.Texte(r, "Abrege"), r.GetBoolean(2));
    }

    /// <summary>L'auteur de la trace (Utilisateur.NomPrenomAbregeUtilisateur, l. 528) : « Nom Prénom (ABREGE) » de l'agent, sinon son compte.</summary>
    private static async Task<string> AuteurAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT CONCAT(Nom, ' ', Prenom, ' (', Abrege, ')') FROM soc.Compte WHERE CompteWindows = @c", ct,
            new SqlParameter("@c", geste.Agent.CompteWindows));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetString(0) : geste.Agent.CompteWindows;
    }
}
