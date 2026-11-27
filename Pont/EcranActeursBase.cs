using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Un acteur, aux champs de l'ecran (web/src/ecrans/EcranActeurs.tsx, Acteur) ; dates aaaa-mm-jj, null sans date.</summary>
public sealed record ActeurVue(string Abrege, string Civilite, string Nom, string Prenom, string ComplementNom, string Adresse, string Tel, string Fax,
                               string Mail, string Qualif, string Statut, string Chambre, string Section, string Debut, string? Fin, string Bureau,
                               string NomCoffre, bool Actif, string Type, string Siren, string Utilisateur, string Cle);

/// <summary>Ce que l'ecran Acteurs affiche, et d'ou (Pont/LectureEcranMain.cs).</summary>
public sealed record VueActeurs(string Mode, string? Message, ActeurVue[] Acteurs, int Total);

/// <summary>
/// L'entree de l'ecran « Acteurs » (PROC_ACTEUR_JUD). Plan « RIG v3 100 % fonctionnel », lot 14 (27/11/2026), sur la marche du lot 17.
/// L'ecran n'avait pas de pont : ses acteurs de demonstration sont dans le .tsx. Branche au serveur, il montre les acteurs des six types
/// de RIG v2 lus dans la base du greffe (Serveur/Socle/EcransMain/LectureActeurs.cs : tables, colonnes et filtres V2 cites) ; il n'ecrit
/// rien (ses champs sont en lecture). SANS serveur : mode « demo », aucun acteur (l'ecran garde ses exemples, marques). En panne : le
/// motif et aucun acteur.
/// </summary>
public static class EcranActeurs
{
    [JSInvokable("EcranActeurs.Acteurs")]
    public static Task<VueActeurs> Acteurs() => ActeursAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    public static async Task<VueActeurs> ActeursAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les acteurs", (s, g) =>
            s.LireAsync<ActeursLus>(RoutesEcransMain.Pour(RoutesActeurs.Acteurs, g)));
        if (lu.Valeur is not { } v) return new VueActeurs(lu.Mode, lu.Message, [], 0);
        // La cle d'une ligne : son type et son numero (deux tables peuvent porter le meme abrege).
        return new VueActeurs(lu.Mode, null, [.. v.Lignes.Select(a => new ActeurVue(a.Abrege, a.Civilite, a.Nom, a.Prenom, a.ComplementNom, a.Adresse,
            a.Telephone, a.Fax, a.Mail, a.Qualification, a.Statut, a.Chambre, a.Section, a.Debut ?? "", a.Fin, a.Bureau, a.NomCoffre, a.Actif,
            a.Type, a.Siren, a.Utilisateur, $"{a.Type}-{a.Id}"))], v.Total);
    }
}
