// fix-ok: ajout du lot 19 (SOC-002 T05), pas une correction : la partie jointe d'une fiche (IRecopieJointe, RegleRecopie.Jointe),
// posee en trois editions (l'interface, la propriete, l'espace de noms SqlClient qu'elle demande) avant toute compilation.
// fix-ok: lot 11 -- RemonteeSeule et NumerosV2 ajoutes (REC-010), puis le conflit de fusion avec le lot 19 tranche en gardant les deux.
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LES REGLES DE LA RECOPIE CONTINUE, TABLE PAR TABLE (REC-002 T03, REC-003 T03, REC-004 T04).
//
// Une fiche partagee (client, dossier, inscription, adresse, action du registre, arrivee, categorie...) vit dans les
// DEUX bases d'un greffe tant que la V2 tourne (Q15) : la V3 fait foi et recopie ses changements dans la V2 (Q7,
// REC-002) ; ce que la V2 ecrit encore pour ses anciennes affaires remonte dans la V3 (REC-003) ; une fiche changee des
// deux cotes entre deux passages garde la version de la V3 et l'ecart est signale (REC-004).
//
// Chaque table partagee a UNE regle de recopie, declaree par sa story et TROUVEE SEULE (comme les regles du transfert,
// Regles.cs) : un lot ajoute sa classe IReglesRecopie, aucun fichier commun. Une table declaree deux fois arrete la
// recopie en nommant les deux classes.
//
// Deux sortes de regles :
//   - « telle quelle » : la table V3 est la copie du schema v2 (v2.ARRIVEE), memes colonnes, meme cle ; la regle ne dit que
//     ce qu'il y a de PROPRE a chaque logiciel, le reste est recopie colonne a colonne sous le meme nom ;
//   - « colonne par colonne » (REC-012 a REC-019, lot 10) : la fiche a SA table dans la V3 (rec.Client, soc.Compte...), et
//     sa regle donne la correspondance de chaque colonne (RegleRecopie.Correspondance), la cle de la V3, les traductions
//     (un agent par son compte de session, un service) et ce qui ne passe que dans un sens (FichesV3.cs).
// Dans les deux cas la recopie lit et compare une fiche SOUS LES NOMS DE LA V2 (LigneJson) : la table V3 est lue « AS » ses
// noms V2 et ecrite par eux (FichesRecopiees.cs) -- applicateur, remontee, ecarts et controle de nuit n'ont qu'une ecriture.
// Mode d'emploi : docs/reconstruction/recopie.md, sections « La recopie continue » et « Les fiches partagees ».
// ============================================================

/// <summary>Une colonne d'une fiche qui a sa table dans la V3 : son nom dans la V3, son nom dans la V2 (REC-012 a REC-019).</summary>
public sealed record ColonneFiche(string V3, string V2)
{
    /// <summary>La valeur ne passe pas telle quelle : elle designe une fiche tenue des deux cotes sous des identifiants
    /// differents, que ni la correspondance des fiches partagees ni leurs liens ne relient (un agent, un service).</summary>
    public Traduction? Traduction { get; init; }
}

/// <summary>
/// La traduction d'une valeur d'une base a l'autre : deux expressions SQL scalaires jouees sur la base V3, ou « @v » est la
/// valeur a traduire et « {V2} » la base V2 du greffe sur la meme instance (Q4). Exemple : l'agent de rattachement d'un
/// client, retrouve par son COMPTE DE SESSION, jamais par son identifiant numerique (REC-013 CA02). Une valeur renseignee
/// sans traduction est un refus (la fiche attend, l'exploitation le voit) : jamais une valeur inventee ni recopiee brute.
/// </summary>
/// <param name="Par">Ce par quoi la valeur est retrouvee, pour le journal et les refus (« le compte de session »).</param>
public sealed record Traduction(string Par, string VersV2, string VersV3)
{
    public string SqlVersV2(string baseV2) => VersV2.Replace("{V2}", Sql.Q(baseV2) + ".dbo", StringComparison.Ordinal);
    public string SqlVersV3(string baseV2) => VersV3.Replace("{V2}", Sql.Q(baseV2) + ".dbo", StringComparison.Ordinal);
}

/// <summary>Une valeur qu'une base tient UNIQUE (le code abrege d'un client dans la V3, le nom de session d'un compte dans
/// les deux) : une fiche recue qui la prendrait a une AUTRE fiche de cette base n'y est pas ecrite -- elle est mise de cote
/// d'emblee, avec son motif, et l'ecart est montre a l'agent designe (REC-013 CA03, REC-017 CA04). Rejouer ne servirait a
/// rien : seul un agent peut dire laquelle des deux garde la valeur.</summary>
/// <param name="ColonneV2">La colonne (nom V2).</param>
/// <param name="Libelle">Le nom pour l'agent (« nom de session »).</param>
public sealed record CleMetier(string ColonneV2, string Libelle, bool UniqueDansV2, bool UniqueDansV3);

/// <summary>La regle de recopie d'une table partagee.</summary>
/// <param name="TableV2">La table dans la base V2 (dbo).</param>
/// <param name="TableV3">La table dans la base V3 : « v2.ARRIVEE » pour une fiche telle quelle, « rec.Client », « soc.Compte »
/// pour une fiche qui a sa table (<see cref="Correspondance"/>).</param>
/// <param name="Story">La story qui en repond.</param>
/// <param name="Libelle">Le nom de la fiche pour l'agent (« client », « dossier RCS ») : ecran des ecarts, journal.</param>
public sealed record RegleRecopie(string TableV2, string TableV3, string Story, string Libelle)
{
    /// <summary>Les colonnes PROPRES a chaque logiciel : jamais recopiees, dans aucun sens, et remises a leur valeur
    /// d'avant dans la V2 si un declencheur de la V2 les change pendant une recopie. C'est la neutralisation des
    /// declencheurs de transmission (REC-002 CA04) : un changement recopie ne marque jamais une fiche « a transmettre »
    /// dans la V2 -- seule la V3 envoie ses changements aux partenaires.</summary>
    public IReadOnlyList<string> ColonnesPropres { get; init; } = [];

    /// <summary>Valeur d'une colonne propre pour une fiche CREEE dans la V3 et recue par la V2 (expression SQL,
    /// ex. « 'N' »). Sans valeur, la V2 recoit celle de la V3.</summary>
    public IReadOnlyDictionary<string, string> CreationV2 { get; init; } = new Dictionary<string, string>();

    /// <summary>La colonne qui date un changement cote V2 (REC-004 T04) ; sans elle, l'ecart est date du moment ou la
    /// recopie l'a vu. Le conflit, lui, se mesure toujours sur la ligne entiere (son etat au dernier passage).</summary>
    public string? ColonneDateV2 { get; init; }

    /// <summary>La colonne ou la V2 note l'agent qui a enregistre la fiche (un UTLST_ID_UTLST de dbo.UTILISATEUR), ecrite
    /// avec <see cref="ColonneDateV2"/> (REC-004 CA01). L'ecart en prend le nom, tel que la V2 l'affiche, quand le changement
    /// de la V2 l'a notee ; sans elle, ou sans changement note, l'auteur V2 reste « non note ».</summary>
    public string? ColonneAuteurV2 { get; init; }

    /// <summary>La colonne qui nomme la fiche pour l'agent (« DUPONT », « 2026B00012 »).</summary>
    public string? ColonneNom { get; init; }

    /// <summary>Ou mene le clic sur un ecart : « dossier:{COLONNE} » (l'affaire), « ecran:CODE » ; null = aucun ecran.</summary>
    public string? Cible { get; init; }

    /// <summary>La correspondance colonne par colonne d'une fiche qui a sa table dans la V3 ; null pour une table telle
    /// quelle (memes noms des deux cotes). Une colonne de la V2 absente ici n'est jamais touchee par la V3 (elle est propre a
    /// la V2 : imprimante d'un poste, deroulement d'un type) ; une colonne de la V3 absente ici n'existe que dans la V3.</summary>
    public IReadOnlyList<ColonneFiche>? Correspondance { get; init; }

    /// <summary>La cle primaire de la table V3 (« Id ») quand elle ne porte pas le nom de celle de la V2.</summary>
    public string? CleV3 { get; init; }

    /// <summary>Les liens vers une autre fiche partagee qu'une base V2 peut tenir sans cle etrangere : colonne V2 -> table V2
    /// visee. Ex. CLIAD_ID_ADRBS -> ADRESSE_BASE, sans cle sur RIG_DEV mais avec une cle active sur RIG_RECETTE (lu le
    /// 2026-11-21) : la base qui a la cle donne le meme lien par son catalogue, la declaration couvre celle qui ne l'a pas.
    /// Les liens portes par une cle de la V2 sont lus dans son catalogue.</summary>
    public IReadOnlyDictionary<string, string> LiensSansCle { get; init; } = new Dictionary<string, string>();

    /// <summary>Une fiche creee dans la V3 n'est PAS creee dans la V2 : elle n'y a pas d'equivalent, et la V3 le sait (le
    /// type de demande cree seulement dans la V3, REC-019 CA03 : liste, jamais range sous un autre type).</summary>
    public bool SansCreationV2 { get; init; }

    /// <summary>La valeur qu'une base tient unique (<see cref="CleMetier"/>).</summary>
    public CleMetier? Unique { get; init; }

    /// <summary>
    /// La table ne se recopie que de la V2 vers la V3 (REC-010 T02) : une affaire et ses demandes se traitent dans UN seul
    /// logiciel (Q5, Q14). Ses lignes de la V2 (anciennes affaires) remontent et restent a jour dans la V3, en lecture ; une
    /// ligne creee dans la V3 n'est jamais mise en file vers la V2 -- elle n'y va qu'au retour d'un greffe beta (REC-007).
    /// La mise en service pose sur sa table V3 le declencheur de garde de <see cref="GardeAffaires"/> au lieu du declencheur
    /// de file : refus d'ecrire dans une ancienne affaire (REC-010 CA03), de creer apres le retour (REC-007 CA03), blocages
    /// entre demandes des deux logiciels (REC-011 CA01, CA02).
    /// </summary>
    public bool RemonteeSeule { get; init; }

    /// <summary>Les colonnes (nom V2 -> table V2 visee) d'une table telle quelle dont la valeur reste le NUMERO DE LA V2 dans les
    /// anciennes lignes de la copie de la V3, meme quand elles visent une fiche recopiee : le transfert les a copiees telles
    /// quelles (v2.DEMANDE.DMND_ID_CTGR est un numero de CATEGORIE, alors que rec.TypeDemande a ses propres identifiants,
    /// REC-019 CA01 ; DMND_ID_UTLST un numero d'UTILISATEUR, soc.Compte les siens). Elles ne sont pas des liens : ni traduites
    /// a la remontee, ni lues par la correspondance au controle de nuit -- sinon une ligne remontee et une ligne transferee
    /// n'auraient pas le meme sens. Une ligne creee dans la V3 y porte l'identifiant de la V3 (le type, le compte) : le retour
    /// (REC-007) le traduit par la correspondance de la table visee.</summary>
    public IReadOnlyDictionary<string, string> NumerosV2 { get; init; } = new Dictionary<string, string>();

    /// <summary>Ce que la fiche porte dans d'AUTRES tables de la V3 et qui passe dans la V2 avec elle (<see cref="IRecopieJointe"/> :
    /// les droits d'un compte, SOC-002 T05) ; null pour une fiche sans partie jointe.</summary>
    public IRecopieJointe? Jointe { get; init; }

    /// <summary>La marque des lignes de file qu'un changement de la partie jointe ecrit (colonne Colonnes : « (droits) »).</summary>
    public string? MarqueJointe => Jointe is null ? null : "(" + Jointe.Libelle + ")";

    public bool ColonneParColonne => Correspondance is not null;

    public static RegleRecopie TelleQuelle(string tableV2, string story, string libelle) => new(tableV2, "v2." + tableV2, story, libelle);

    /// <summary>La meme fiche dans sa copie telle quelle du schema v2 : la regle d'une base transferee avant que la fiche n'ait
    /// sa table (ses colonnes propres, sa date, son auteur et son nom sont des noms V2, gardes).</summary>
    public RegleRecopie EnTelleQuelle() => this with
    {
        TableV3 = "v2." + TableV2, Correspondance = null, CleV3 = null, SansCreationV2 = false, Unique = null,
        LiensSansCle = new Dictionary<string, string>(), Jointe = null,
    };
}

/// <summary>
/// CE QU'UNE FICHE PORTE DANS D'AUTRES TABLES DE LA V3 ET QUI PASSE DANS LA V2 AVEC ELLE (SOC-002 T05 : les droits d'un compte,
/// soc.CompteRole et soc.CompteFonction, ecrits dans HABILIT_UTILISATEUR_ROLE_USER, l'ancien systeme et EXPRESSO_DROITS --
/// Serveur\Recopie\DroitsRecopies.cs). Ce n'est pas un second mecanisme : un changement d'une de ces tables met LA FICHE en file
/// (declencheur pose par la mise en service, PontRecopie, colonnes « (libelle) »), et l'applicateur ecrit la partie jointe dans
/// la transaction de la fiche. Elle a donc tout ce que la file donne a la fiche : l'ordre, une transaction, panne ou refus, la
/// mise de cote au 3e essai et la relance (REC-005), l'ecart (REC-004).
/// </summary>
public interface IRecopieJointe
{
    /// <summary>Le nom pour l'agent et la marque des lignes de file (« droits »).</summary>
    string Libelle { get; }

    /// <summary>Les tables de la V3 et la colonne qui y designe la fiche par son identifiant V3.</summary>
    IReadOnlyList<TableJointe> Tables { get; }

    /// <summary>Ecrit dans la V2 la partie jointe telle que la V3 la tient, dans la transaction de la fiche ; rend l'ecart
    /// ecrit si la V2 l'avait changee depuis le dernier passage (REC-004), sinon null. Leve <see cref="RecopieRefusee"/> ou
    /// <see cref="RecopieImpossible"/> quand la V2 ne peut pas la recevoir.</summary>
    Task<long?> AppliquerAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee fiche, string idV3, string idV2, ChangementJoint changement,
                               CancellationToken ct);

    /// <summary>Retire de la V2 la partie jointe d'une fiche supprimee dans la V3, AVANT la fiche (ses cles etrangeres).</summary>
    Task SupprimerAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee fiche, string idV2, CancellationToken ct);
}

/// <summary>Une table de la partie jointe : « soc.CompteRole », et sa colonne qui designe la fiche (« IdCompte »).</summary>
public sealed record TableJointe(string TableV3, string ColonneFiche);

/// <summary>Le changement de la V3 en cours d'application : la date de sa premiere ligne de file, ses agents (pour l'ecart).</summary>
public sealed record ChangementJoint(DateTime Le, string Auteurs);

/// <summary>Une classe qui declare des regles de recopie (constructeur sans parametre, trouvee seule).</summary>
public interface IReglesRecopie
{
    IEnumerable<RegleRecopie> Regles { get; }
}

public static class ReglesRecopie
{
    public static IReadOnlyList<RegleRecopie> Decouvrir() => Decouvrir(typeof(ReglesRecopie).Assembly);

    public static IReadOnlyList<RegleRecopie> Decouvrir(Assembly assemblage)
    {
        var sources = assemblage.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IReglesRecopie).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .SelectMany(t => ((IReglesRecopie)Activator.CreateInstance(t)!).Regles.Select(r => (Source: t.Name, Regle: r)))
            .ToList();
        var doublons = sources.GroupBy(s => s.Regle.TableV2, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (doublons.Count > 0)
            throw new InvalidOperationException("Regles de recopie en double : " + string.Join(" ; ",
                doublons.Select(g => $"{g.Key} declaree par {string.Join(" et ", g.Select(s => s.Source))}")) +
                ". Une table a une seule regle : modifier la declaration existante.");
        return sources.Select(s => s.Regle).ToList();
    }
}

/// <summary>
/// LA FICHE PARTAGEE DE REC-002 QUI RESTE TELLE QUELLE : l'arrivee (ARRIVEE -> v2.ARRIVEE), dont la story (REC-018) est au
/// lot 11. Les six autres du lot 9 (client, dossier, inscription, adresse, action du registre, categorie) ont leur table
/// dans la V3 depuis le lot 10 : leur regle colonne par colonne est dans FichesV3.cs, avec les fiches que REC-012 a REC-019
/// y ajoutent (entreprise, adresses et contacts d'un client, provisions reprises, creance, bateau, comptes, objets du
/// registre et radiations).
/// </summary>
internal sealed class ReglesRec002 : IReglesRecopie
{
    public IEnumerable<RegleRecopie> Regles =>
    [
        // REC-018
        RegleRecopie.TelleQuelle("ARRIVEE", "REC-002", "arrivée") with { ColonneNom = "ARRV_NUM_ARRIVEE" },
    ];
}
