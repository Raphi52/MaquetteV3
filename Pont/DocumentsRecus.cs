// fix-ok: CS0246 HttpRequestException -- un projet WPF n'importe pas System.Net.Http ni System.IO d'office (meme cause que PreferencesServeur.cs).
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

// ============================================================
// LES DOCUMENTS QUE LA PARTIE SERVEUR PREPARE POUR L'AGENT, OUVERTS A L'ECRAN (SOC-013 T03, CA03 et CA04 ;
// lot 4 du plan « Reconstruction de RIG — phase 2 », 2026-11-21).
//
// La V2 ouvrait l'edition dans Word, sur le serveur et dans la session notes a l'ouverture de l'agent
// (LigneEdition.AfficheDocument, LigneEdition.cs:806-823). La V3 l'ouvre dans une FENETRE DE DOCUMENT
// (FenetreDocument, la fenetre que « Detacher » ouvre depuis une affaire) :
//   - a l'ouverture de l'application : les documents qui attendaient l'agent (CA04) ;
//   - a chaque releve : ceux de la session ou il travaille (CA03).
// Le fichier est sur le serveur de la V3 (chez Amitel), que le serveur RDS ne lit pas : son contenu passe par
// la route RoutesPreferences.Document, puis il est range a cote des reglages de l'agent (le dossier des pieces
// jointes, que chaque fenetre lit par l'hote virtuel de Pont/PageWeb.cs).
//
// Ce fichier ne depend que du pont (PreferencesServeur, ClientServeur) et de Metier (PiecesJointes, NoeudGed) :
// le banc tests/SoclePreferencesCheck le joue contre le vrai serveur ; seule l'ouverture de la fenetre (WPF) est
// dans FenetreDocument.xaml.cs (AfficheurFenetres).
// ============================================================

/// <summary>Ce qui montre un document recu : l'app ouvre une fenetre de document (AfficheurFenetres) ; le banc note.</summary>
public interface IAfficheurDocuments
{
    /// <summary>Montre <paramref name="piece"/> dans sa propre fenetre, avec ce qui la decrit (<paramref name="valeurs"/>).</summary>
    void Montrer(NoeudGed piece, IReadOnlyList<(string Libelle, string Valeur)> valeurs);
}

public static class DocumentsRecus
{
    /// <summary>Le sous-dossier des pieces jointes ou les documents recus sont ranges.</summary>
    public const string SousDossier = "documents-recus";

    private static readonly object verrou = new();
    private static IAfficheurDocuments? afficheur;
    private static string dossierPieces = "";

    /// <summary>Les documents se montrent un par un, dans l'ordre d'arrivee : une releve ne double pas l'ouverture.</summary>
    private static Task file = Task.CompletedTask;

    /// <summary>
    /// Ouvre chaque document qui arrive (<see cref="PreferencesServeur.DocumentsArrives"/>) par <paramref name="par"/>, son
    /// contenu range dans <paramref name="dossier"/>. A appeler AVANT l'ouverture de la session : les documents qui attendaient
    /// l'agent arrivent pendant celle-ci. Rappele, il remplace le precedent (jamais deux fenetres pour un document).
    /// </summary>
    public static void Brancher(IAfficheurDocuments par, string dossier)
    {
        lock (verrou)
        {
            afficheur = par;
            dossierPieces = dossier;
        }
        PreferencesServeur.DocumentsArrives -= Arrives;
        PreferencesServeur.DocumentsArrives += Arrives;
    }

    /// <summary>Plus rien ne s'ouvre (fin du banc).</summary>
    public static void Debrancher()
    {
        PreferencesServeur.DocumentsArrives -= Arrives;
        lock (verrou) afficheur = null;
    }

    /// <summary>Attend que les documents deja arrives soient montres.</summary>
    public static Task AttendreAsync()
    {
        lock (verrou) return file;
    }

    private static void Arrives(PreferencesServeur depot, DocumentRecu[] documents)
    {
        lock (verrou)
        {
            if (afficheur is not { } par) return;
            var dossier = dossierPieces;
            file = file.ContinueWith(_ => MontrerAsync(depot, documents, par, dossier), TaskScheduler.Default).Unwrap();
        }
    }

    private static async Task MontrerAsync(PreferencesServeur depot, DocumentRecu[] documents, IAfficheurDocuments par, string dossier)
    {
        foreach (var d in documents) par.Montrer(await PreparerAsync(depot, d, dossier), Valeurs(d));
    }

    /// <summary>
    /// Le document <paramref name="d"/> pret a montrer : son contenu lu sur le serveur, range dans
    /// <paramref name="dossier"/> (<see cref="SousDossier"/>), la piece qui le pointe. Un document qui ne peut pas
    /// s'afficher (serveur muet, fichier disparu, type que la V3 n'affiche pas) s'ouvre QUAND MEME, avec la raison a la
    /// place du contenu : l'agent sait qu'il est arrive.
    /// </summary>
    public static async Task<NoeudGed> PreparerAsync(PreferencesServeur depot, DocumentRecu d, string dossier, CancellationToken ct = default)
    {
        ContenuDocument contenu;
        try { contenu = await depot.LireContenuAsync(d.Id, ct); }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            Trace.WriteLine($"RigV3 SOC-013 : document {d.Id} non lu sur le serveur ({e.Message}).");
            return SansContenu(d, "Il n'a pas pu être lu sur le serveur : " + ((e as ErreurServeur)?.Corps is { Length: > 0 } corps ? corps : e.Message));
        }
        var nom = Path.GetFileName(contenu.Nom);
        // La regle des pieces jointes (type par l'extension ET par le debut du contenu) : la fenetre n'ouvre que ceux-la.
        if (PiecesJointes.Refus(nom, contenu.Contenu) is not null)
            return SansContenu(d, $"Son fichier ({nom}) n'est pas un PDF ni une image JPG ou PNG lisible : la V3 n'affiche que ceux-là.");
        try
        {
            return PiecesJointes.Ranger(dossier, SousDossier, nom, contenu.Contenu, d.DeposeLe) with
            {
                Libelle = d.Titre, Origine = OrigineDe(d),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"RigV3 SOC-013 : document {d.Id} non range dans {dossier} ({e.Message}).");
            return SansContenu(d, $"Il n'a pas pu être rangé sur ce poste ({e.Message}).");
        }
    }

    /// <summary>Ce qui decrit le document dans sa fenetre : sa nature, qui l'a prepare, quand.</summary>
    public static IReadOnlyList<(string Libelle, string Valeur)> Valeurs(DocumentRecu d) =>
    [
        ("Document", d.Nature == NaturesDocument.Relire ? "à relire" : "à afficher"),
        ("Préparé par", d.DeposePar),
        ("Prêt le", d.DeposeLe.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)),
    ];

    private static string OrigineDe(DocumentRecu d) => $"Document reçu ({d.DeposePar})";

    /// <summary>Une piece sans fichier : son apercu dit pourquoi le contenu n'est pas la.</summary>
    private static NoeudGed SansContenu(DocumentRecu d, string raison)
    {
        var extension = Path.GetExtension(d.Fichier).TrimStart('.').ToUpperInvariant();
        return new NoeudGed(d.Titre, "piece", d.DeposeLe.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), 1,
                            extension.Length > 0 ? extension : "DOC", OrigineDe(d),
                            $"{d.Titre}\n\nCe document est arrivé, mais il ne s'affiche pas. {raison}", []);
    }
}
