using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;

namespace RigV3Desktop.Pont;

/// <summary>Un bouton de type : son code, son libelle, son filet (couleur) et son infobulle.</summary>
public sealed record TypeActualiteVue(string Code, string Libelle, string Filet, string Aide);

/// <summary>Une ligne du tableau des actualites publiees, deja mise en forme comme Blazor l'affichait.</summary>
/// <param name="Retire">La date de retrait « dd/MM », ou « — » pour une actualite epinglee.</param>
public sealed record LigneActualite(string Filet, string Libelle, string Titre, string Message,
                                    string Auteur, string Horodatage, string Retire);

/// <summary>Ce que l'ecran affiche : le formulaire, le compte rendu et la liste publiee.</summary>
/// <param name="Jours">La duree saisie, en texte (le champ ne se reecrit que si elle change).</param>
/// <param name="StatutRendu">Le filet du compte rendu : « ok » ou « alerte ».</param>
public sealed record VueActualites(string Session, string Titre, string Message, string Type, string Auteur,
                                   string Jours, bool PeutPublier, TypeActualiteVue[] Types,
                                   string CompteRendu, string StatutRendu, string CheminPartage,
                                   LigneActualite[] Liste);

/// <summary>
/// Pont de l'ecran PROC_ACTUALITES (web/src/ecrans/EcranActualites.tsx) : la
/// REDACTION des actualites affichees sur l'accueil de tout le monde. Ecran A
/// SESSION (web/src/session.tsx) : le texte part dans le fichier PARTAGE
/// (DepotActualites) lu par les autres postes, et la liste de l'ecran est celle
/// que ce depot ecrit -- elle vit donc ici, avec le depot.
/// </summary>
public static class EcranActualites
{
    internal static readonly SessionsEcran<SessionActualites> Sessions = new();

    [JSInvokable("EcranActualites.Ouvrir")]
    public static VueActualites Ouvrir() => Ouvrir(new DepotActualites());

    /// <summary>Ouvre une session sur un depot donne (les bancs le pointent sur un dossier jetable).</summary>
    public static VueActualites Ouvrir(DepotActualites depot)
    {
        var session = new SessionActualites(depot);
        return session.Vue(Sessions.Ouvrir(session));
    }

    [JSInvokable("EcranActualites.Agir")]
    public static VueActualites Agir(string session, string geste, JsonElement valeur)
    {
        var s = Sessions.Lire(session);
        s.Agir(geste, valeur);
        return s.Vue(session);
    }

    [JSInvokable("EcranActualites.Fermer")]
    public static void Fermer(string session) => Sessions.Fermer(session);
}

/// <summary>
/// L'etat de l'ecran des actualites : le @code d'EcranActualites.razor, deplace
/// TEL QUEL (memes champs, memes regles de publication, de retrait et de compte
/// rendu). Metier/ n'est pas modifie.
/// </summary>
public sealed class SessionActualites
{
    private readonly DepotActualites Depot;

    private List<Actualite> Liste = new();

    private string Titre = string.Empty;
    private string Message = string.Empty;
    private string Statut = "attente";
    private string Type = TypeActualite.Info;
    private string Auteur = "Greffe";
    private int Jours = 7;
    private string Compte_rendu = string.Empty;
    private string StatutRendu = "ok";

    internal SessionActualites(DepotActualites depot)
    {
        Depot = depot;
        Recharger();
    }

    internal VueActualites Vue(string session) => new(
        session, Titre, Message, Type, Auteur,
        Jours.ToString(CultureInfo.InvariantCulture),
        !string.IsNullOrWhiteSpace(Titre),
        TypeActualite.Tous.Select(t => new TypeActualiteVue(t.Code, t.Libelle, t.Filet, Aide(t))).ToArray(),
        Compte_rendu, StatutRendu, Depot.CheminPartage,
        Liste.Select(b => new LigneActualite(
                b.Fiche.Filet, b.Fiche.Libelle, b.Titre, b.Message, b.Auteur, b.Horodatage,
                b.Expire is null ? "—" : b.Expire.Value.ToString("dd/MM")))
             .ToArray());

    internal void Agir(string geste, JsonElement valeur)
    {
        switch (geste)
        {
            case "Titre": Titre = Texte(valeur); break;
            case "Message": Message = Texte(valeur); break;
            case "Auteur": Auteur = Texte(valeur); break;
            // Le @bind d'un int : une saisie illisible garde la derniere valeur lisible.
            case "Jours":
                if (int.TryParse(Texte(valeur), NumberStyles.Number, CultureInfo.InvariantCulture, out var jours))
                    Jours = jours;
                break;
            case "ChoisirType":
                ChoisirType(TypeActualite.Tous.First(t => t.Code == Texte(valeur)));
                break;
            case "Publier": Publier(); break;
            case "Retirer": Retirer(Liste[valeur.GetInt32()]); break;
            default: throw new ArgumentException($"Geste inconnu de l'ecran des actualites : {geste}.", nameof(geste));
        }
    }

    private static string Texte(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();

    /// <summary>Choisir un type REPOSITIONNE la duree : c'est le sens du type.
    /// Le redacteur garde la main dessus juste apres, le champ reste libre.</summary>
    private void ChoisirType(TypeActualite t)
    {
        Type = t.Code;
        Jours = t.JoursDeVie;
        Statut = t.Code switch
        {
            TypeActualite.Fraude => "alerte",
            TypeActualite.Panne => "alerte",
            TypeActualite.Procedure => "action",
            TypeActualite.Retabli => "ok",
            _ => "attente"
        };
    }

    /// <summary>L'infobulle d'un type : ce qu'il veut dire ET combien de temps
    /// il tient. Sans elle, le code couleur reste a deviner.</summary>
    private static string Aide(TypeActualite t) => t.Code switch
    {
        TypeActualite.Fraude => "Vigilance nominative — reste affichee jusqu'a retrait",
        TypeActualite.Panne => "Un service tiers est hors service — 30 jours",
        TypeActualite.Procedure => "Consigne de travail temporaire — 14 jours",
        TypeActualite.Retabli => "Fin d'une panne — 1 jour",
        _ => "Simple information — 7 jours"
    };

    private void Recharger()
    {
        var tous = Depot.Tous();
        // Depot vierge : on part des deux actualites dictees par le metier
        // (CNG, INPI) plutot que d'un ecran vide -- le redacteur voit tout de
        // suite la forme attendue, et peut les retirer d'un clic.
        Liste = (tous.Length > 0 ? tous : DepotActualites.Exemples(DateTime.Now))
                .OrderByDescending(b => b.Publie).ToList();
    }

    private void Publier()
    {
        if (string.IsNullOrWhiteSpace(Titre)) return;
        var maintenant = DateTime.Now;
        Liste.Insert(0, new Actualite(
            Id: Guid.NewGuid().ToString("N")[..8],
            Titre: Titre.Trim(),
            Message: Message.Trim(),
            Statut: Actualite.Statuts.Contains(Statut) ? Statut : "attente",
            Auteur: string.IsNullOrWhiteSpace(Auteur) ? "Greffe" : Auteur.Trim(),
            Publie: maintenant,
            Expire: Jours > 0 ? maintenant.AddDays(Jours) : null,
            Type: Type));
        Ecrire("Actualite publiee : elle s'affichera sur l'accueil des postes.");
        Titre = string.Empty;
        Message = string.Empty;
    }

    private void Retirer(Actualite b)
    {
        Liste.Remove(b);
        Ecrire("Actualite retiree de l'accueil.");
    }

    // L'echec d'ecriture ne se tait pas : sans ce retour, le redacteur croit
    // avoir preveni tout le greffe alors que rien n'est parti.
    private void Ecrire(string reussite)
    {
        if (Depot.Ecrire(Liste))
        {
            Compte_rendu = reussite;
            StatutRendu = "ok";
        }
        else
        {
            Compte_rendu = "ECHEC : la source partagee n'a pas pu etre ecrite. "
                         + "Rien n'est parti vers les autres postes.";
            StatutRendu = "alerte";
        }
    }
}
