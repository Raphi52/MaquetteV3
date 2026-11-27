using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Un expert, aux champs de l'ecran (web/src/ecrans/EcranExperts.tsx, Expert) ; <paramref name="Cle"/> = son numero.</summary>
public sealed record ExpertVue(string Abrege, string Civilite, string Nom, string ComplementNom, string Naissance, string Adresse, string Tel, string Fax,
                               string Mail, string Qualif, bool Actif, string Cle);

/// <summary>Une mesure ou l'expert participe (MesureExpert de l'ecran) ; <paramref name="Expert"/> = la cle de l'expert.</summary>
public sealed record MesureExpertVue(string Expert, string NumMesure, string Civilite, string NomParticipant, string ComplementNom, string QualifProf,
                                     string Adresse, bool EnCours);

/// <summary>Ce que l'ecran Experts affiche, et d'ou (Pont/LectureEcranMain.cs).</summary>
public sealed record VueExperts(string Mode, string? Message, ExpertVue[] Experts, MesureExpertVue[] Mesures, int Total);

/// <summary>
/// L'entree de l'ecran « Experts » (PROC_EXPERT). Plan « RIG v3 100 % fonctionnel », lot 14 (27/11/2026), sur la marche du lot 17. L'ecran
/// n'avait pas de pont : ses experts et leurs mesures de demonstration sont dans le .tsx. Branche au serveur, il montre les experts de la
/// base du greffe et les mesures ou ils participent (Serveur/Socle/EcransMain/LectureExperts.cs, sources V2 citees) ; il n'ecrit rien.
/// SANS serveur : mode « demo », aucun expert (l'ecran garde ses exemples, marques). En panne : le motif et aucun expert.
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
                m.Adresse, m.EnCours))],
            v.Total);
    }
}
