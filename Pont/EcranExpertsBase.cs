using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Un expert, aux champs de l'ecran (web/src/ecrans/EcranExperts.tsx, Expert) ; <paramref name="Cle"/> = son numero.</summary>
public sealed record ExpertVue(string Abrege, string Civilite, string Nom, string ComplementNom, string Naissance, string Adresse, string Tel, string Fax,
                               string Mail, string Qualif, bool Actif, string Cle);

/// <summary>Une mesure ou l'expert participe (MesureExpert de l'ecran) ; <paramref name="Expert"/> = la cle de l'expert ;
/// <paramref name="Participant"/> = la ligne PARTICIPANT, celle que « Enregistrer la fiche » met a jour quand elle est cochee.</summary>
public sealed record MesureExpertVue(string Expert, string NumMesure, string Civilite, string NomParticipant, string ComplementNom, string QualifProf,
                                     string Adresse, bool EnCours, string Participant = "");

/// <summary>Ce que l'ecran Experts affiche, et d'ou (Pont/LectureEcranMain.cs).</summary>
public sealed record VueExperts(string Mode, string? Message, ExpertVue[] Experts, MesureExpertVue[] Mesures, int Total);

/// <summary>Ce que la fiche envoie a « Enregistrer » : ses champs (null = inchange) et les participants coches dans les listes de mesures.</summary>
public sealed record FicheExpert(string? Civilite = null, string? Abrege = null, string? Nom = null, string? ComplementNom = null, string? Tel = null,
                                 string? Fax = null, string? Mail = null, string? Qualif = null, bool? Actif = null, string[]? Participants = null);

/// <summary>
/// L'entree de l'ecran « Experts » (PROC_EXPERT). Plan « RIG v3 100 % fonctionnel », lot 14 (27/11/2026), sur la marche du lot 17. L'ecran
/// n'avait pas de pont : ses experts et leurs mesures de demonstration sont dans le .tsx. Branche au serveur, il montre les experts de la
/// base du greffe et les mesures ou ils participent (Serveur/Socle/EcransMain/LectureExperts.cs, sources V2 citees). SANS serveur : mode
/// « demo », aucun expert (l'ecran garde ses exemples, marques). En panne : le motif et aucun expert.
///
/// « Enregistrer la fiche » (<see cref="EnregistrerAsync"/>) : le « Valider » de la fiche OPE_EDITION_EXPERT de RIG v2, ecrit par le serveur
/// (Serveur/Socle/EcransMain/EcritureExperts.cs, ses controles V2 cites), avec les participants coches qui reprennent la fiche. Sans serveur,
/// rien n'est ecrit (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026).
/// </summary>
public static class EcranExperts
{
    [JSInvokable("EcranExperts.Experts")]
    public static Task<VueExperts> Experts() => ExpertsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    public static async Task<VueExperts> ExpertsAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les experts", (s, g) =>
            s.LireAsync<ExpertsLus>(RoutesEcransMain.Pour(RoutesExperts.Experts, g)));
        if (lu.Valeur is not { } v) return new VueExperts(lu.Mode, lu.Message, [], [], 0);
        static string Cle(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new VueExperts(lu.Mode, null,
            [.. v.Experts.Select(e => new ExpertVue(e.Abrege, e.Civilite, e.Nom, e.ComplementNom, e.Naissance, e.Adresse, e.Telephone, e.Fax, e.Mail,
                e.Qualification, e.Actif, Cle(e.Id)))],
            [.. v.Mesures.Select(m => new MesureExpertVue(Cle(m.IdExpert), m.NumMesure, m.Civilite, m.NomParticipant, m.ComplementNom, m.Qualification,
                m.Adresse, m.EnCours, Cle(m.IdParticipant)))],
            v.Total);
    }

    /// <summary>« Enregistrer la fiche » de l'expert <paramref name="cle"/> : les champs changes et les participants coches.</summary>
    [JSInvokable("EcranExperts.Enregistrer")]
    public static Task<ResultatGeste> Enregistrer(string cle, FicheExpert fiche) => EnregistrerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, cle, fiche);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration ».</summary>
    public static async Task<ResultatGeste> EnregistrerAsync(ClientServeur? serveur, string? greffe, string cle, FicheExpert f)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        var valeurs = new Dictionary<string, string>();
        void Poser(string champ, string? valeur) { if (valeur is not null) valeurs[champ] = valeur.Trim(); }
        Poser(RoutesFicheExpert.ChampCivilite, f.Civilite);
        Poser(RoutesFicheExpert.ChampAbrege, f.Abrege);
        Poser(RoutesFicheExpert.ChampNom, f.Nom);
        Poser(RoutesFicheExpert.ChampComplementNom, f.ComplementNom);
        Poser(RoutesFicheExpert.ChampTelephone, f.Tel);
        Poser(RoutesFicheExpert.ChampFax, f.Fax);
        Poser(RoutesFicheExpert.ChampMail, f.Mail);
        Poser(RoutesFicheExpert.ChampQualification, f.Qualif);
        Poser(RoutesFicheExpert.ChampActif, f.Actif is { } a ? (a ? "oui" : "non") : null);
        Poser(RoutesFicheExpert.ChampParticipants, f.Participants is { Length: > 0 } p ? string.Join(";", p) : null);
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesFicheExpert.Enregistrer, greffe),
            new GesteEcran(TypeGeste.Enregistrer, [cle], valeurs));
    }
}
