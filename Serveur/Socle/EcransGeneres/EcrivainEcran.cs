using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

// ============================================================
// LES GESTES DES ECRANS GENERES, ECRITS DANS LA BASE DU GREFFE (plan « RIG v3 100 % fonctionnel », lot 2, 26/11/2026).
//
// Un ecran genere (Pont/EcransGeneres/) a une FORME (Fiche, Saisie, Referentiel, File, Operation, Audience...) ; ses
// gestes partent de l'ecran (web/src/ecrans/formes/), passent par le pont (Pont/EcranGenere.cs, EcranGenere.Geste) et la
// route POST RoutesEcransGeneres.Gestes (ModuleEcransGeneres), et arrivent ICI, a l'ecrivain de la procedure. Sans
// ecrivain, rien n'est ecrit et l'ecran le dit : « Demonstration : rien n'est enregistre ».
//
// MODE D'EMPLOI -- BRANCHER LES GESTES D'UN ECRAN = AJOUTER UN FICHIER, ICI, ET RIEN D'AUTRE :
//   1. creer Serveur/Socle/EcransGeneres/Ecrivain<Sujet>.cs, une classe (internal sealed suffit) qui implemente
//      IEcrivainEcran, avec un constructeur sans parametre. ModuleEcransGeneres la trouve par reflexion au demarrage
//      (EcrivainsEcrans.Decouvrir) : aucune liste a completer, ni ici, ni dans le pont, ni dans l'ecran. Deux ecrivains
//      pour la meme procedure arretent le demarrage en se nommant ;
//   2. Procedure = le code du descripteur (« PROC_MJUD ») ; Ecran = le nom que le journal des gestes porte
//      (« Instance judiciaire ») ; Gestes = les types de geste qu'il sait ecrire (TypeGeste : Enregistrer pour une Fiche,
//      Creer pour une Saisie, Ajouter/Modifier/Supprimer pour un Referentiel, Agir pour une File, Confirmer pour une
//      Operation, Decider pour une Audience). Un type absent = « Demonstration » a l'ecran, rien d'ecrit ;
//   3. JouerAsync recoit le geste en cours (GesteEnCours) : la base V3 du greffe DEJA ouverte, et UNE transaction
//      ouverte par le socle. L'ecrivain lit par geste.LireAsync / ValeurAsync et ecrit par geste.EcrireAsync, qui
//      REFUSE toute ecriture hors des tables de l'ancien RIG recopiees (v2.*) et des fiches reprises (rec.*) : jamais
//      soc.*, jamais une autre base. Un UPDATE nomme sa table (« UPDATE v2.INSTANCE SET ... »), sans alias ;
//   4. les CONTROLES de RIG v2 passent AVANT toute ecriture, chacun cite (fichier:ligne du code V2 ou fiche
//      proc_<code>.md), son message AU MOT PRES de la V2. Un controle qui refuse rend IssueGeste.Refuser(message) : le
//      socle annule la transaction, RIEN n'est ecrit, l'ecran affiche le message. L'objet verrouille par RIG v2
//      (VerrouDemandeV2.MotifAsync, relu DANS la transaction) est un refus comme un autre ;
//   5. un geste reussi rend IssueGeste.Ecrit(message, traces) : au moins UNE trace (TraceGeste : famille de
//      soc.FamilleJournal, l'objet en mots, son id, le verbe du journal, avant, apres). Le socle l'ecrit au journal des
//      gestes (Serveur/Socle/Comptes/JournalGestes.cs) au nom de l'agent, DANS la meme transaction, puis valide : un
//      geste ecrit est trace, un geste annule ne l'est pas. Sans trace, le socle annule et leve (faute du lot) ;
//   6. ce que RIG v2 ecrit en plus de la valeur (date de mise a jour, etat de transmission...) s'ecrit aussi, cite ; ce
//      que la V3 ne reprend pas (une demande V2, une edition, un service externe) se NOMME dans le commentaire en tete
//      de l'ecrivain, jamais en silence ;
//   7. le banc tests/EcransGeneresBaseCheck trouve tout ecrivain et le joue par ses jeux : <PROCEDURE>.gestes dans
//      tests/EcransGeneresBaseCheck/Jeux/ (format en tete du Program.cs du banc) -- un geste, puis les lignes relues,
//      ou le refus attendu et les lignes inchangees. Un ecrivain sans jeu .gestes rend le banc rouge.
//
// Le droit : la route exige un agent admis dans le greffe (PolitiquesSocle.AgentAdmis), comme la lecture, et
// l'identifie dans la base du greffe (ServiceDroits.AppelantAsync) pour le journal. Un droit par procedure (fonction V2)
// n'est pas encore exige : a decider avec le referent Socle (meme reserve que LecteurEcran.cs).
// ============================================================

/// <summary>L'ecrivain des gestes d'UNE procedure (un fichier par procedure, trouve par reflexion).</summary>
public interface IEcrivainEcran
{
    /// <summary>Le code de la procedure du menu, celui de son descripteur (« PROC_MJUD »).</summary>
    string Procedure { get; }

    /// <summary>Le nom de l'ecran au journal des gestes (« Instance judiciaire »).</summary>
    string Ecran { get; }

    /// <summary>Les types de geste que cet ecrivain ecrit ; tout autre est une demonstration.</summary>
    IReadOnlyCollection<TypeGeste> Gestes { get; }

    /// <summary>Joue <paramref name="geste"/> : controles de RIG v2, puis ecriture dans la transaction du geste.</summary>
    Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct);
}

/// <summary>Une ligne du journal des gestes (soc.Journal) : ce que le geste a fait, en mots, avec avant et apres.</summary>
/// <param name="Famille">Une famille de soc.FamilleJournal (« affaire », « dossier », « reglage »...).</param>
/// <param name="Objet">L'objet, en mots (« l'instance 2026J00201 »).</param>
/// <param name="IdObjet">Son identifiant dans la base (l'id V2).</param>
/// <param name="Verbe">Un verbe du journal (JournalGestes.Verbes : « a modifie », « a cree »...).</param>
/// <param name="Avant">Les valeurs avant le geste (null pour une creation).</param>
/// <param name="Apres">Les valeurs apres le geste (null pour une suppression).</param>
public sealed record TraceGeste(string Famille, string Objet, string? IdObjet, string Verbe, object? Avant, object? Apres);

/// <summary>Ce qu'un ecrivain rend : ecrit (avec ses traces), ou refuse (rien d'ecrit).</summary>
public abstract record IssueGeste
{
    private IssueGeste() { }

    /// <summary>Ecrit : le message de l'ecran et les lignes du journal (au moins une).</summary>
    public sealed record Fait(string Message, TraceGeste[] Traces, string[] Ids) : IssueGeste;

    /// <summary>Refuse par un controle de RIG v2 : son message ; le socle annule la transaction.</summary>
    public sealed record Refus(string Motif) : IssueGeste;

    /// <summary>Un controle de RIG v2 refuse : <paramref name="motif"/>, au mot pres de la V2.</summary>
    public static IssueGeste Refuser(string motif) => new Refus(motif);

    /// <summary>Le geste est ecrit sur <paramref name="ids"/> ; <paramref name="traces"/> vont au journal des gestes.</summary>
    public static IssueGeste Ecrit(string message, string[] ids, params TraceGeste[] traces) => new Fait(message, traces, ids);
}

/// <summary>
/// Le geste en cours, tel que le socle le donne a l'ecrivain : la base V3 du greffe ouverte, la transaction du geste, la
/// demande de l'ecran, l'agent et l'instant. Lire et ecrire passent par ici (mode d'emploi, point 3).
/// </summary>
public sealed partial class GesteEnCours(SqlConnection baseGreffe, SqlTransaction transaction, GesteEcran demande, Appelant agent, DateTime maintenant)
{
    /// <summary>La base V3 du greffe de l'adresse (jamais une autre).</summary>
    public SqlConnection Base { get; } = baseGreffe;

    /// <summary>La transaction du geste : le socle la valide (geste ecrit) ou l'annule (refus, erreur).</summary>
    public SqlTransaction Transaction { get; } = transaction;

    /// <summary>Ce que l'ecran demande.</summary>
    public GesteEcran Demande { get; } = demande;

    /// <summary>L'agent qui joue le geste, admis dans le greffe.</summary>
    public Appelant Agent { get; } = agent;

    /// <summary>L'instant du geste (le DateTime.Now de la V2) : fixe par un banc.</summary>
    public DateTime Maintenant { get; } = maintenant;

    /// <summary>La valeur SAISIE du champ <paramref name="libelle"/> de la section <paramref name="section"/> ; null si
    /// l'ecran ne l'envoie pas (une Fiche n'envoie que ce qui change).</summary>
    public string? Valeur(string section, string libelle)
        => Demande.Valeurs.TryGetValue(GesteEcran.Cle(section, libelle), out var v) ? v : null;

    /// <summary>Une lecture (un SELECT seul) dans la transaction du geste.</summary>
    public async Task<SqlDataReader> LireAsync(string sql, CancellationToken ct, params SqlParameter[] parametres)
    {
        LectureV2.ExigerLecture(sql);
        var cmd = new SqlCommand(sql, Base, Transaction) { CommandTimeout = 120 };
        cmd.Parameters.AddRange(parametres);
        return await cmd.ExecuteReaderAsync(ct);
    }

    /// <summary>
    /// Une ecriture (INSERT, UPDATE, DELETE, MERGE) dans la transaction du geste ; rend le nombre de lignes touchees.
    /// Leve si une table ecrite n'est pas v2.* ou rec.* (mode d'emploi, point 3).
    /// </summary>
    public async Task<int> EcrireAsync(string sql, CancellationToken ct, params SqlParameter[] parametres)
    {
        ExigerEcritureV2(sql);
        await using var cmd = new SqlCommand(sql, Base, Transaction) { CommandTimeout = 120 };
        cmd.Parameters.AddRange(parametres);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Leve si <paramref name="sql"/> ecrit ailleurs que dans v2.* ou rec.*, ou change la structure de la base.</summary>
    public static void ExigerEcritureV2(string sql)
    {
        if (MotDeStructure().IsMatch(sql))
            throw new InvalidOperationException($"Geste refuse par le socle : une ecriture ne change pas la structure de la base (« {Couper(sql)} »).");
        var cibles = Cible().Matches(sql).Select(m => m.Groups["table"].Value.Replace("[", "").Replace("]", "")).ToList();
        if (cibles.Count == 0)
            throw new InvalidOperationException($"Geste refuse par le socle : aucune table ecrite reconnue (INSERT INTO, UPDATE, DELETE FROM, MERGE INTO) dans « {Couper(sql)} ».");
        var interdites = cibles.Where(t => !t.StartsWith("v2.", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("rec.", StringComparison.OrdinalIgnoreCase)).ToList();
        if (interdites.Count > 0)
            throw new InvalidOperationException($"Geste refuse par le socle : un ecran genere n'ecrit que v2.* et rec.* de la base du greffe (ecrit : {string.Join(", ", interdites)}).");
    }

    // « UPDATE SET » (la branche WHEN MATCHED d'un MERGE) n'est pas une table : sa cible est celle du MERGE INTO.
    [GeneratedRegex(@"\b(?:INSERT\s+INTO|UPDATE(?!\s+SET\b)|DELETE\s+FROM|MERGE\s+INTO)\s+(?<table>[\[\]\w]+(?:\.[\[\]\w]+)*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Cible();

    [GeneratedRegex(@"\b(CREATE|ALTER|DROP|TRUNCATE|EXEC|EXECUTE|GRANT|DENY|REVOKE|USE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MotDeStructure();

    private static string Couper(string s) => s.Length <= 80 ? s : s[..80] + "…";
}

/// <summary>Les ecrivains trouves dans le serveur, par procedure.</summary>
public sealed class EcrivainsEcrans(IReadOnlyDictionary<string, IEcrivainEcran> parProcedure)
{
    /// <summary>Les ecrivains par code de procedure.</summary>
    public IReadOnlyDictionary<string, IEcrivainEcran> ParProcedure { get; } = parProcedure;

    /// <summary>Toute classe concrete de <paramref name="assemblage"/> qui implemente <see cref="IEcrivainEcran"/>. Deux
    /// ecrivains pour la meme procedure levent en se nommant (comme deux lecteurs, LecteursEcrans.Decouvrir).</summary>
    public static EcrivainsEcrans Decouvrir(Assembly assemblage)
    {
        var parProcedure = new Dictionary<string, IEcrivainEcran>(StringComparer.Ordinal);
        foreach (var type in assemblage.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEcrivainEcran).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var ecrivain = (IEcrivainEcran)(Activator.CreateInstance(type, nonPublic: true)
                ?? throw new InvalidOperationException($"Ecrivain {type.FullName} : impossible de le creer."));
            if (!parProcedure.TryAdd(ecrivain.Procedure, ecrivain))
                throw new InvalidOperationException(
                    $"Procedure {ecrivain.Procedure} ecrite par deux ecrivains : {parProcedure[ecrivain.Procedure].GetType().FullName} et {type.FullName}.");
        }
        return new EcrivainsEcrans(parProcedure);
    }

    /// <summary>
    /// Joue <paramref name="demande"/> par <paramref name="ecrivain"/> sur <paramref name="baseGreffe"/> (deja ouverte), en
    /// UNE transaction : refus -> annulee, rien d'ecrit ; ecrit -> les traces au journal des gestes au nom de
    /// <paramref name="agent"/>, puis validee. La route et le banc passent tous deux par ici.
    /// </summary>
    public static async Task<ResultatGeste> JouerAsync(IEcrivainEcran ecrivain, SqlConnection baseGreffe, GesteEcran demande, Appelant agent,
                                                       DateTime maintenant, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await baseGreffe.BeginTransactionAsync(ct);
        IssueGeste issue;
        try
        {
            issue = await ecrivain.JouerAsync(new GesteEnCours(baseGreffe, tx, demande, agent, maintenant), ct);
            if (issue is IssueGeste.Fait fait)
            {
                if (fait.Traces.Length == 0)
                    throw new InvalidOperationException($"Ecrivain {ecrivain.GetType().Name} : un geste ecrit sans trace au journal des gestes (mode d'emploi, point 5).");
                foreach (var t in fait.Traces)
                {
                    if (!JournalGestes.Verbes.ContainsKey(t.Verbe))
                        throw new InvalidOperationException($"Ecrivain {ecrivain.GetType().Name} : verbe « {t.Verbe} » absent du journal des gestes ({string.Join(", ", JournalGestes.Verbes.Keys)}).");
                    await JournalGestes.EcrireAsync(baseGreffe, tx, agent, ecrivain.Ecran, t.Famille, t.Objet, t.IdObjet, t.Verbe, t.Avant, t.Apres, ct);
                }
            }
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
        switch (issue)
        {
            case IssueGeste.Fait f:
                await tx.CommitAsync(ct);
                return new ResultatGeste(true, f.Message) { Ids = f.Ids };
            case IssueGeste.Refus r:
                await tx.RollbackAsync(CancellationToken.None);
                return new ResultatGeste(false, r.Motif);
            default:
                await tx.RollbackAsync(CancellationToken.None);
                throw new InvalidOperationException($"Ecrivain {ecrivain.GetType().Name} : issue inconnue {issue.GetType().Name}.");
        }
    }
}
