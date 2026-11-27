using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Un client, aux champs des ecrans (web/src/ecrans/EcranClients.tsx, ClientRcs) : statut et type aux libelles de l'ecran.</summary>
public sealed record ClientVue(int Id, string Compte, string Abrege, string Nom, string Groupe, string Statut, string Type);

/// <summary>Ce que l'ecran Clients affiche, et d'ou (Pont/LectureEcranMain.cs).</summary>
public sealed record VueClients(string Mode, string? Message, ClientVue[] Clients, int Total);

/// <summary>
/// L'entree de l'ecran « Clients » (PROC_GCLIENT), et la liste du choix de la « Fiche client ». Plan « RIG v3 100 % fonctionnel », lot 14
/// (27/11/2026), sur la marche du lot 17. L'ecran n'avait pas de pont : ses clients de demonstration sont dans le .tsx. Branche au serveur,
/// il montre les clients en compte de la base du greffe (Serveur/Socle/EcransMain/LectureClients.cs, requete d'OPE_CRITERES citee) ; il
/// n'ecrit rien. SANS serveur : mode « demo », aucun client (l'ecran garde ses exemples, marques). En panne : le motif et aucun client.
/// </summary>
public static class EcranClients
{
    [JSInvokable("EcranClients.Clients")]
    public static Task<VueClients> Clients() => ClientsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Le statut de RIG v2 (CLIEN_STATUT A, B, S : OPE_CRITERES.cs:53-63) au libelle des criteres de l'ecran.</summary>
    internal static string Statut(string code) => code switch { "A" => "Actif", "B" => "Bloque", "S" => "En sommeil", _ => code };

    /// <summary>Le type de RIG v2 (DgvRow.GetTypeClient) au libelle des vues de l'ecran (« Prevention » sans accent, comme ses criteres).</summary>
    internal static string Type(string type) => type == "Prévention" ? "Prevention" : type;

    public static async Task<VueClients> ClientsAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les clients", (s, g) =>
            s.LireAsync<ClientsLus>(RoutesEcransMain.Pour(RoutesClients.Clients, g)));
        if (lu.Valeur is not { } v) return new VueClients(lu.Mode, lu.Message, [], 0);
        return new VueClients(lu.Mode, null, [.. v.Lignes.Select(c => new ClientVue(c.Id, c.Compte, c.Abrege, c.Nom, c.Groupe, Statut(c.Statut), Type(c.Type)))],
            v.Total);
    }
}
