using System.Reflection;

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
// La regle dit ce qu'il y a de PROPRE a chaque logiciel, le reste est recopie colonne a colonne : aujourd'hui la table
// V3 d'une fiche partagee est sa copie « telle quelle » du schema v2 (REC-001), memes colonnes, meme cle. Quand la story
// du domaine (REC-012 a REC-019, lot 10) ecrira sa table V3, elle REMPLACERA la ligne de sa table dans ReglesRec002 par
// une regle qui nomme sa table et la correspondance de ses colonnes (une sorte nouvelle de regle, a ecrire avec elle).
// Mode d'emploi : docs/reconstruction/recopie.md, section « La recopie continue ».
// ============================================================

/// <summary>La regle de recopie d'une table partagee.</summary>
/// <param name="TableV2">La table dans la base V2 (dbo).</param>
/// <param name="TableV3">La table dans la base V3 (« v2.CLIENT » tant que la fiche est telle quelle).</param>
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

    public static RegleRecopie TelleQuelle(string tableV2, string story, string libelle) => new(tableV2, "v2." + tableV2, story, libelle);
}

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
/// LES FICHES PARTAGEES DE REC-002 qui ont aujourd'hui une table dans la V3 : les 7 tables « telles quelles » de REC-001
/// que tables_v2 de REC-002 nomme. Les autres (ENTREPRISE, BODACC, DEPOT_RCS, INSCRIPTION_RCS, INSTANCE, NON_INSCRIT,
/// PASSAGE_CFE, BATEAU, CREANCE_END) n'ont pas encore de regle de transfert (lot 8 : regle provisoire ; lot 10 : leur
/// story) : leur ligne s'ajoute ici avec leur table V3. UTILISATEUR est porte par la reprise des comptes (soc.Compte) :
/// sa recopie demande la correspondance de ses colonnes, a ecrire avec la story des agents.
/// Colonnes de transmission : DOSSIER_RCS.sql:85-103 et INSCRIPTION.sql:71-89 de la V2 (le declencheur marque « M » ;
/// l'etat « N », reserve a la migration, le bloque). Dates de mise a jour : relevees dans RIG_DEV le 2026-11-21.
/// </summary>
internal sealed class ReglesRec002 : IReglesRecopie
{
    public IEnumerable<RegleRecopie> Regles =>
    [
        // fix-ok: auteur V2 d'un ecart client toujours vide -- mesure 2026-11-21 par RecopieDeuxSensCheck (REC-004 CA01 rouge :
        // CLIEN_ID_UTLST 1 -> 189 change par la V2, AuteurV2 vide) : la regle ne nommait pas la colonne d'agent de la V2.
        // REC-013. Client.Sauve de la V2 (RigMetier\Facture\Client.cs:522-523) ecrit la date ET l'agent courant ; sa fiche
        // client les affiche (« Derniere modification le ... par ... », PROC_FCLIENT.cs:47). Les 6 autres tables n'ont aucune
        // colonne d'agent (RigDatabase\dbo\Tables\*.sql).
        RegleRecopie.TelleQuelle("CLIENT", "REC-002", "client") with
            { ColonneDateV2 = "CLIEN_DATE_DERNIERE_MODIF", ColonneAuteurV2 = "CLIEN_ID_UTLST", ColonneNom = "CLIEN_NOM", Cible = "ecran:PROC_FCLIENT" },
        // REC-012
        RegleRecopie.TelleQuelle("DOSSIER_RCS", "REC-002", "dossier RCS") with
        {
            ColonnesPropres = ["DSSRC_ETAT_TRANSMISSION", "DSSRC_DATE_MODIF_FLAG_TRANS"],
            CreationV2 = new Dictionary<string, string> { ["DSSRC_ETAT_TRANSMISSION"] = "'N'" },
            ColonneDateV2 = "DSSRC_DATE_MAJ", ColonneNom = "DSSRC_NUM_GESTION", Cible = "dossier:{DSSRC_NUM_GESTION}",
        },
        // REC-014
        RegleRecopie.TelleQuelle("INSCRIPTION", "REC-002", "inscription") with
        {
            ColonnesPropres = ["INEDT_ETAT_TRANSMISSION", "INEDT_DATE_MODIF_FLAG_TRANS"],
            CreationV2 = new Dictionary<string, string> { ["INEDT_ETAT_TRANSMISSION"] = "'N'" },
            ColonneDateV2 = "INEDT_DATE_MAJ", ColonneNom = "INEDT_NUM_INSCRIPTION",
        },
        // REC-016
        RegleRecopie.TelleQuelle("ADRESSE_BASE", "REC-002", "adresse") with { ColonneNom = "ADRBS_NOM_VOIE" },
        // REC-015
        RegleRecopie.TelleQuelle("ACTION_RCS", "REC-002", "action du registre") with { ColonneNom = "ACTRC_NUM_ARRIVEE" },
        // REC-018
        RegleRecopie.TelleQuelle("ARRIVEE", "REC-002", "arrivée") with { ColonneNom = "ARRV_NUM_ARRIVEE" },
        // REC-019
        RegleRecopie.TelleQuelle("CATEGORIE", "REC-002", "catégorie") with { ColonneNom = "CTGR_NOM" },
    ];
}
