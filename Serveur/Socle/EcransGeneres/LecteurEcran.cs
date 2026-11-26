using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

// ============================================================
// LES LIGNES DES ECRANS GENERES, LUES DANS LA BASE DU GREFFE (plan « ecrans retrouves branches sur la
// base », lot 1, 24/11/2026).
//
// Un ecran genere (Pont/EcransGeneres/, un descripteur par procedure du menu) est mis en page par
// web/src/ecrans/EcranGenere.tsx. Sans lecteur, il montre les lignes d'exemple de son descripteur, marquees
// « exemple ». Avec un lecteur ici, il montre les lignes de la base V3 du greffe ouvert : les tables de
// l'ancien RIG recopiees telles quelles (v2.*) et les fiches deja reprises (rec.DossierRcs, rec.Entreprise,
// rec.ActionRegistre : Serveur/Recopie/FichesV3.cs dit quelle table V2 est devenue quelle fiche).
//
// MODE D'EMPLOI -- BRANCHER UN ECRAN DE PLUS = AJOUTER UN FICHIER, ICI, ET RIEN D'AUTRE :
//   1. creer Serveur/Socle/EcransGeneres/Lecteur<Sujet>.cs, une classe (internal sealed suffit) qui
//      implemente ILecteurEcran, avec un constructeur sans parametre. ModuleEcransGeneres la trouve par
//      reflexion au demarrage (LecteursEcrans.Decouvrir) : aucune liste a completer, ni ici, ni dans le
//      pont, ni dans l'ecran. Deux lecteurs pour la meme procedure arretent le demarrage en se nommant ;
//   2. Procedure = le code du descripteur (« PROC_DCA ») ;
//   3. LireAsync lit la base du greffe en LECTURE SEULE : passer par LectureV2.LireAsync, qui refuse tout
//      ce qui n'est pas un SELECT seul. Les filtres sont ceux de l'ancien RIG, CITES en commentaire avec
//      leur source (fichier:ligne du code V2 ou fiche proc_*.md) ; une date « aujourd'hui » se lit dans le
//      parametre aujourdhui (jamais GETDATE()), pour qu'un banc la fixe ;
//   4. chaque ligne porte un Groupe et un Etat du descripteur, AU MOT PRES, et des Faites (rangs des marches)
//      tenus par la regle des ecrans generes (Pont/EcransGeneres/Descripteur.cs) : AFaire = aucune marche,
//      EnCours = au moins une, Validee = toutes les marches obligatoires. La regle qui donne l'etat d'une
//      ligne est ECRITE et commentee dans le lecteur (quelle colonne, quelle valeur, quel etat) ;
//   5. au plus `plafond` lignes, dans l'ordre d'affichage ; Total = toutes les lignes qui repondent aux
//      filtres (COUNT(*) OVER () dans la meme requete) ;
//   6. le banc tests/EcransGeneresBaseCheck verifie TOUT lecteur trouve contre son descripteur (groupes,
//      etats, marches, regle des Faites). Pour ses filtres et sa regle d'etat, deux fichiers neufs dans
//      tests/EcransGeneresBaseCheck/Jeux/ : <PROCEDURE>.sql (les lignes controlees) et <PROCEDURE>.attendu
//      (les lignes rendues, dans l'ordre) -- format en tete du Program.cs du banc, qui n'est pas a modifier.
//
// La route (ModuleEcransGeneres) exige un agent admis dans le greffe, comme la lecture des compteurs
// (RoutesCompteur.Compteurs) : le menu filtre deja les ecrans par profil (Metier/PerimetreProfil.cs). Un
// droit par procedure (fonction V2) n'est pas encore exige : a decider avec le referent Socle.
//
// CHERCHER ET OUVRIR UNE FICHE (plan « RIG v3 100 % fonctionnel », lot 2, 26/11/2026) -- deux capacites de plus, sur la
// MEME classe que le lecteur (un fichier par procedure, toujours) :
//   7. IRechercheEcran : ChercherAsync applique DANS LA BASE le texte du champ de recherche et les criteres remplis
//      (RechercheEcran : libelles du descripteur, au mot pres, declares dans Criteres). Les filtres de la liste de
//      LireAsync restent ; la recherche s'y AJOUTE. Chaque critere cite sa source V2 (la recherche de l'ancien RIG). Un
//      texte se cherche en LIKE parametre (FormeLigne.Contient, jamais concatene) ; sans recherche, la route appelle
//      LireAsync. L'ECRAN EST DEJA BRANCHE pour les cinq formes qui retrouvent (web/src/ecrans/formes/commun.tsx,
//      useRecherche) : Consultation, Fiche et Operation envoient le texte et leurs Criteres, un Referentiel son texte, une
//      Audience son texte et ses Parametres (date, chambre : declares eux aussi dans Criteres), une Operation sur UN objet
//      son numero (son seul critere) ;
//   8. IFicheEcran : LireFicheAsync lit UN objet, par l'Id que ses lignes portent (LigneLue.Id : posez-le), et rend ses
//      sections aux titres et libelles du DESCRIPTEUR (format des valeurs : Contrats/EcransGeneres/FicheEcran.cs). Un
//      champ que l'ecrivain sait enregistrer est Modifiable. Un objet que RIG v2 tient verrouille s'ouvre LectureSeule,
//      avec le message de RIG v2 (VerrouDemandeV2.cs). L'ecran la lit DEJA, selon sa forme : Fiche (le formulaire),
//      Consultation (en lecture), Referentiel (la ligne ouverte en modification : chaque colonne prend le champ de MEME
//      libelle, la fiche entiere se lit dessous), Audience et Operation (la ligne depliee ; sur UN objet, ses sections et
//      son Resultat, que la fiche peut aussi porter). fix-ok: mesure par le controle final du lot 2 (26/11/2026) -- ce
//      mode d'emploi promettait recherche et fiche alors que Referentiel, Audience et Operation n'appelaient ni
//      EcranGenere.Chercher ni EcranGenere.Fiche ; branches depuis, 7 tests d'EcranGenere.test.tsx le tiennent ;
//   9. le banc verifie que les criteres declares et les sections et champs rendus existent dans le descripteur, et que
//      chaque ligne d'un jeu qui porte un Id ouvre sa fiche. Les gestes : EcrivainEcran.cs.
// ============================================================

/// <summary>Le lecteur des lignes d'UNE procedure (un fichier par procedure, trouve par reflexion).</summary>
public interface ILecteurEcran
{
    /// <summary>Le code de la procedure du menu, celui de son descripteur (« PROC_DCA »).</summary>
    string Procedure { get; }

    /// <summary>Les lignes de la base V3 du greffe <paramref name="baseGreffe"/>, au plus <paramref name="plafond"/>.</summary>
    /// <param name="aujourdhui">La date du jour (le GETDATE() de la V2) : fixee par un banc, maintenant sinon.</param>
    Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct);
}

/// <summary>Un lecteur qui CHERCHE dans la base (point 7 du mode d'emploi) : la meme classe que son lecteur.</summary>
public interface IRechercheEcran : ILecteurEcran
{
    /// <summary>Les criteres du descripteur que la base sait chercher, au libelle pres ; le texte se cherche toujours.</summary>
    IReadOnlyCollection<string> Criteres { get; }

    /// <summary>Les lignes de <see cref="ILecteurEcran.LireAsync"/> qui repondent EN PLUS a <paramref name="recherche"/>.</summary>
    Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct);
}

/// <summary>Un lecteur qui OUVRE une fiche (point 8 du mode d'emploi) : la meme classe que son lecteur.</summary>
public interface IFicheEcran : ILecteurEcran
{
    /// <summary>La fiche de l'objet <paramref name="id"/> (<see cref="LigneLue.Id"/>) ; null si rien ne le porte.</summary>
    /// <param name="maintenant">L'instant du jour (une audience passee ou future) : fixe par un banc, maintenant sinon.</param>
    Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct);
}

/// <summary>Ce qu'un lecteur rend : ses lignes (plafonnees) et le nombre de lignes de la base qui repondent.</summary>
public sealed record LignesLues(IReadOnlyList<LigneLue> Lignes, int Total);

/// <summary>Les lecteurs trouves dans le serveur, par procedure.</summary>
public sealed class LecteursEcrans(IReadOnlyDictionary<string, ILecteurEcran> parProcedure)
{
    /// <summary>Les lecteurs par code de procedure.</summary>
    public IReadOnlyDictionary<string, ILecteurEcran> ParProcedure { get; } = parProcedure;

    /// <summary>
    /// Toute classe concrete de <paramref name="assemblage"/> qui implemente <see cref="ILecteurEcran"/>. Deux
    /// lecteurs pour la meme procedure levent en se nommant (comme deux modules pour la meme route, ServeurRig).
    /// </summary>
    public static LecteursEcrans Decouvrir(Assembly assemblage)
    {
        var parProcedure = new Dictionary<string, ILecteurEcran>(StringComparer.Ordinal);
        foreach (var type in assemblage.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ILecteurEcran).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var lecteur = (ILecteurEcran)(Activator.CreateInstance(type, nonPublic: true)
                ?? throw new InvalidOperationException($"Lecteur {type.FullName} : impossible de le creer."));
            if (!parProcedure.TryAdd(lecteur.Procedure, lecteur))
                throw new InvalidOperationException(
                    $"Procedure {lecteur.Procedure} lue par deux lecteurs : {parProcedure[lecteur.Procedure].GetType().FullName} et {type.FullName}.");
        }
        return new LecteursEcrans(parProcedure);
    }
}

/// <summary>Les mises en forme communes des lignes : les memes ecritures d'un ecran a l'autre.</summary>
public static partial class FormeLigne
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« 31/12/2025 » ; vide sans date.</summary>
    public static string Date(DateTime? d) => d is { } v ? v.ToString("dd/MM/yyyy", Francais) : "";

    /// <summary>« 900 300 001 » ; le texte tel quel s'il n'a pas neuf chiffres.</summary>
    public static string Siren(string? siren)
    {
        var s = siren?.Trim() ?? "";
        return s.Length == 9 && s.All(char.IsAsciiDigit) ? $"{s[..3]} {s[3..6]} {s[6..]}" : s;
    }

    /// <summary>Le numero de gestion comme le registre l'ecrit : « 2001B00411 » -> « 2001 B 00411 ».</summary>
    public static string NumeroGestion(string? numero)
    {
        var n = numero?.Trim() ?? "";
        var m = FormeGestion().Match(n);
        return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}" : n;
    }

    /// <summary>Les morceaux non vides, separes par « · » (la separation des lignes d'exemple).</summary>
    public static string Joindre(params string?[] morceaux)
        => string.Join(" · ", morceaux.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!.Trim()));

    /// <summary>Le texte d'une colonne (vide si NULL).</summary>
    public static string Texte(SqlDataReader r, string colonne)
        => r[colonne] is string s ? s.Trim() : r[colonne] is DBNull ? "" : Convert.ToString(r[colonne], CultureInfo.InvariantCulture) ?? "";

    /// <summary>La date d'une colonne (null si NULL).</summary>
    public static DateTime? DateDe(SqlDataReader r, string colonne) => r[colonne] is DateTime d ? d : null;

    /// <summary>Une date au format d'un champ date de l'ecran et d'une fiche (« 2026-03-31 ») ; vide sans date.</summary>
    public static string DateFiche(DateTime? d) => d is { } v ? v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";

    /// <summary>Une case de fiche : « oui » cochee, « » sinon (le format de l'ecran).</summary>
    public static string Case(bool? coche) => coche == true ? "oui" : "";

    /// <summary>Un motif LIKE qui cherche <paramref name="texte"/> tel quel : ses caracteres speciaux (%, _, [) echappes.</summary>
    public static string Contient(string texte)
        => "%" + texte.Trim().Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal)
                          .Replace("_", "[_]", StringComparison.Ordinal) + "%";

    [GeneratedRegex(@"^(\d{4})([A-Z])(\d{5})$")]
    private static partial Regex FormeGestion();
}

/// <summary>La lecture commune : une requete SELECT seule (LectureV2), ses lignes et le total.</summary>
public static class LectureEcran
{
    /// <summary>
    /// Execute <paramref name="sql"/> (un SELECT seul dont une colonne « Total » porte COUNT(*) OVER ()) et rend une
    /// ligne par enregistrement, fabriquee par <paramref name="ligne"/>. Total = 0 sans enregistrement.
    /// </summary>
    public static async Task<LignesLues> LireAsync(SqlConnection baseGreffe, string sql, Func<SqlDataReader, LigneLue> ligne,
                                                   CancellationToken ct, params SqlParameter[] parametres)
    {
        var lignes = new List<LigneLue>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, sql, ct, parametres);
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            lignes.Add(ligne(r));
        }
        return new LignesLues(lignes, total);
    }
}
