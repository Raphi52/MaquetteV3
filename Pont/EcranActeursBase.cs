using RigV3.Contrats.EcransGeneres;
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
/// de RIG v2 lus dans la base du greffe (Serveur/Socle/EcransMain/LectureActeurs.cs : tables, colonnes et filtres V2 cites). SANS serveur :
/// mode « demo », aucun acteur (l'ecran garde ses exemples, marques). En panne : le motif et aucun acteur.
///
/// « Enregistrer les coordonnées » (<see cref="EnregistrerCoordonneesAsync"/>) : le telephone, le fax et le courriel de la fiche, le
/// « Valider » de la fiche d'edition de RIG v2, ecrit par le serveur (Serveur/Socle/EcransMain/EcritureActeurs.cs, ses controles V2 cites) ;
/// les autres champs restent en lecture. Sans serveur, rien n'est ecrit (plan « RIG v3 complet, mesuré par le banc du parcours complet »,
/// lot 9, 28/11/2026).
/// </summary>
public static class EcranActeurs
{
    /// <summary>« Enregistrer les coordonnées » de l'acteur <paramref name="cle"/> (« Juge-12 », la Cle de sa ligne).</summary>
    [JSInvokable("EcranActeurs.EnregistrerCoordonnees")]
    public static Task<ResultatGeste> EnregistrerCoordonnees(string cle, string tel, string fax, string mail)
        => EnregistrerCoordonneesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, cle, tel, fax, mail);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration ».</summary>
    public static async Task<ResultatGeste> EnregistrerCoordonneesAsync(ClientServeur? serveur, string? greffe, string cle, string? tel, string? fax,
                                                                        string? mail)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesCoordonneesActeur.Enregistrer, greffe),
            new GesteEcran(TypeGeste.Enregistrer, [cle], new Dictionary<string, string>
            {
                [RoutesCoordonneesActeur.ChampTelephone] = tel?.Trim() ?? "",
                [RoutesCoordonneesActeur.ChampFax] = fax?.Trim() ?? "",
                [RoutesCoordonneesActeur.ChampMail] = mail?.Trim() ?? "",
            }));
    }

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
