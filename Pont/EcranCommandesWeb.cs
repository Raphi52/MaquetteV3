using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;

namespace RigV3Desktop.Pont;

// ---------------------------------------------------------------- la vue rendue a React
// Les enums partent sous leur NOM (« ATraiter », « Rejeter ») : c'est aussi ce que
// le .tsx renvoie avec un geste. Les textes « — » des valeurs vides sont poses ici,
// avec le string.IsNullOrWhiteSpace du .razor.

public sealed record OngletCw(string Etat, string Libelle, int Nombre, bool Actif);
public sealed record OptionCw(string Valeur, string Libelle);
public sealed record MessageCw(string Texte, string Severite);

/// <summary>Les criteres tels que saisis dans la carte de recherche (dates : aaaa-mm-jj ou vide).</summary>
public sealed record RechercheCw(string Famille, OptionCw[] Familles, string Du, string Au, string Siren,
                                 string Reference, bool EnAttenteEnvoi, string DateTravail);

/// <summary>Une ligne de la liste a plat ; <c>Id</c> revient avec le clic.</summary>
public sealed record LigneCw(int Id, int Rang, bool Active, string Nom, string Precisions, string Type, bool Direct);

public sealed record EtatCw(string Icone, string Libelle);
public sealed record MembreCw(int Id, string Titre, string Icone, string Libelle);
public sealed record EnveloppeCw(string Reference, MembreCw[] Membres, string Etat, bool EnvoiEnAttente);
public sealed record ChampCw(string Libelle, string Valeur);
public sealed record DocumentCw(string Document, string Bac, string Exemplaires, string Origine);
public sealed record SaisieCw(string Famille, OptionCw[] Familles, string Denomination, string Siren, string Requerant);
public sealed record TraiteeCw(bool Rejetee, string Motif, string Reference, DocumentCw[] Envoyes);

public sealed record LigneEntiteCw(string Nature, string Adresse, int NbInscriptions);
public sealed record EntiteCw(string Cle, bool NonInscrit, string Gestion, string Designation, string Siren,
                              string Type, string Resume, bool Radie, bool Retenue, LigneEntiteCw[] Lignes,
                              string[] Ecarts);
public sealed record RetenueCw(string Code, string Designation, bool Radie, string Detail, string[] Ecarts);

/// <summary>La zone de rapprochement (debiteur ou dossier concerne).</summary>
public sealed record RapprochementCw(bool Debiteur, bool Faillite, bool ARetenue, bool Deplie, bool Traitee,
                                     RetenueCw? Retenue, string RechSiren, string RechRaison,
                                     bool TousDebiteurs, bool TousEtablissements, string? Message,
                                     EntiteCw[] Entites, string ResumeRapprochement);

public sealed record ItemTypeCw(bool Regroupement, string Code, string Libelle, bool Enfant, bool Coche);
public sealed record TypesCw(string DateTravail, bool ToutCoche, ItemTypeCw[] Items, int Quantite);

public sealed record SousTypeCw(string Valeur, string Libelle, bool Inerte, bool Coche);
public sealed record ControleCw(string DerniereFormalite, string Delai, int Seuil, string Issue, string? Motif);
public sealed record NonOppositionCw(SousTypeCw[] SousTypes, int Quantite, bool QuantiteActive,
                                     bool CertificatDOpposition, ControleCw? Controle);

public sealed record CertificatCw(string Valeur, string Libelle, int Quantite, bool Actif, bool Bloque, string? Motif);
public sealed record CertificatsCw(string DateTravail, CertificatCw[] Lignes, bool BasculeFaillite, bool CertificatDeFaillite);

public sealed record BandeCw(bool Rejet, string MotifSaisi, bool ConfirmerRefuse, int LongueurMax);
public sealed record PiedCw(BandeCw? Bande, string Barre, DocumentCw[] Documents, bool EnvoiMail, string AdresseMail,
                            bool AttenteRefusee, string? Blocage);

/// <summary>La commande ouverte en panneau.</summary>
public sealed record PanneauCw(int Id, string Titre, int Rang, string Type, EtatCw? Etat, string Nom, string Reference,
                               EnveloppeCw? Enveloppe, SaisieCw? Saisie, ChampCw[] DonneesRecues, TraiteeCw? Traitee,
                               string? MotifAttente, RapprochementCw? Rapprochement, TypesCw? Types,
                               NonOppositionCw? NonOpposition, CertificatsCw? Certificats, PiedCw? Pied);

public sealed record ChampAdresseCw(string Cle, string Libelle, bool Large, string Valeur);
public sealed record CoordonneesCw(bool LectureSeule, ChampAdresseCw[] Champs, string[] Lignes, string[] Alertes,
                                   bool AdresseCorrigee);

/// <summary>Tout ce que web/src/ecrans/EcranCommandesWeb.tsx affiche.</summary>
public sealed record VueCommandesWeb(string Session, OngletCw[] Onglets, bool RechercheOuverte, bool RechercheAffinee,
                                     string ResumeCriteres, RechercheCw Recherche, MessageCw? Message, string Compteur,
                                     bool DirectsEnTete, LigneCw[] Lignes, PanneauCw? Panneau, CoordonneesCw? Coordonnees);

/// <summary>
/// Pont de l'ecran PROC_COMMANDES_WEB (web/src/ecrans/EcranCommandesWeb.tsx) :
/// « Traitement des commandes » de NeoRIG, mise en page « Boite de tri » (conv-1984).
///
/// ECRAN A SESSION (web/src/session.tsx) : les commandes de l'ecran sont des
/// CommandeRecue que Metier/TraitementCommandes.cs modifie lui-meme (Appliquer,
/// NouvelleSaisie, EnregistrerSaisie). Le @code d'EcranCommandesWeb.razor est donc
/// deplace TEL QUEL dans <see cref="SessionCommandesWeb"/> : l'ecran joue toujours
/// les regles de TraitementCommandes, il n'en a pas de copie (tests/CommandesWebCheck).
/// </summary>
public static class EcranCommandesWeb
{
    internal static readonly SessionsEcran<SessionCommandesWeb> Sessions = new();

    [JSInvokable("EcranCommandesWeb.Ouvrir")]
    public static VueCommandesWeb Ouvrir()
    {
        var session = new SessionCommandesWeb();
        return session.Vue(Sessions.Ouvrir(session));
    }

    [JSInvokable("EcranCommandesWeb.Agir")]
    public static VueCommandesWeb Agir(string session, string geste, JsonElement valeur)
    {
        var s = Sessions.Lire(session);
        s.Agir(geste, valeur);
        return s.Vue(session);
    }

    [JSInvokable("EcranCommandesWeb.Fermer")]
    public static void Fermer(string session) => Sessions.Fermer(session);
}

/// <summary>
/// L'etat et les regles de l'ecran des commandes web : le @code de
/// EcranCommandesWeb.razor, deplace sans changement de sens. Les RenderFragment
/// (ZoneRapprochement, ZoneTypes...) sont devenus les methodes Vue* qui rendent
/// ce qu'ils lisaient ; les gestes arrivent par <see cref="Agir"/>.
/// </summary>
public sealed class SessionCommandesWeb
{
    private readonly List<CommandeRecue> Toutes = TraitementCommandes.JeuDemoCommandes();

    // ------------------------------------------------------------ recherche
    // L'etat ne fait plus partie de la carte de recherche : c'est l'onglet.
    private string FamilleChoisie = string.Empty;
    private DateTime? Du;
    private DateTime? Au;
    private string SirenCherche = string.Empty;
    private string ReferenceCherchee = string.Empty;
    private bool EnAttenteEnvoi;
    private CriteresCommandes Appliques = TraitementCommandes.CriteresParDefaut();
    private bool RechercheOuverte;

    // ------------------------------------------------------------ onglets et liste
    private EtatCommande OngletActif = EtatCommande.ATraiter;
    private List<(EtatCommande Etat, int Nombre)> Onglets = [];
    private List<GroupeCommandes> Groupes = [];
    private List<CommandeRecue> Ordre = [];
    private bool DirectsEnTete;
    private CommandeRecue? Selection;
    private bool RapprochementDeplie;

    private (string Texte, string Severite)? Message;

    // ------------------------------------------------------------ detail
    private string RechSiren = string.Empty;
    private string RechRaison = string.Empty;
    private ResultatRapprochement Resultat = new([], null);
    private bool TousDebiteurs;
    private bool TousEtablissements;
    private readonly HashSet<string> TypesCoches = new(StringComparer.Ordinal);
    private int QuantiteInscriptions = 1;
    private readonly HashSet<SousTypeCno> SousTypes = [];
    private int QuantiteCno = 1;
    private bool CertificatDOpposition;
    private readonly Dictionary<CertificatFaillite, int> Certificats = new();
    private bool CertificatDeFaillite;
    private bool EnvoiMail;
    private string AdresseMail = string.Empty;
    private ActionCommande? BandeOuverte;
    private string MotifSaisi = string.Empty;

    // ------------------------------------------------------------ coordonnees
    private bool CoordonneesOuvertes;
    private readonly Dictionary<string, string> Adresse = new(StringComparer.Ordinal);
    private sealed record ChampAdresse(string Cle, string Libelle, bool Large);
    private static readonly ChampAdresse[] ChampsAdresse =
    [
        new("Nom", "Nom", true),
        new("Complement", "Complément (service, contact)", true),
        new("Voie", "N° et voie", true),
        new("LieuDit", "Lieu-dit, bâtiment", false),
        new("BoitePostale", "Boîte postale", false),
        new("CodePostal", "Code postal", false),
        new("Localite", "Localité", false),
        new("Pays", "Pays", true),
    ];

    private enum DocumentConsulte { Kbis, EtatSynthese, KbisHorsRessort, SyntheseRne }

    // OnInitialized du .razor : la liste est lue a l'ouverture.
    public SessionCommandesWeb() => Rafraichir();

    // ================================================================ recherche

    // SurToucheRecherche (Entree dans la carte de recherche) est dans le .tsx :
    // il y lance le geste « Rechercher », qui appelle Rechercher() ci-dessous.

    private void Rechercher()
    {
        FamilleCommande? famille = Enum.TryParse<FamilleCommande>(FamilleChoisie, out var f) ? f : null;
        Appliques = new CriteresCommandes(famille, new HashSet<EtatCommande> { OngletActif }, Du, Au,
                                          SirenCherche, ReferenceCherchee, EnAttenteEnvoi);
        Selection = null;
        Message = null;
        Rafraichir();
    }

    private void ChoisirOnglet(EtatCommande etat)
    {
        if (etat == OngletActif) return;
        PoserOnglet(etat);
        Selection = null;
        Message = null;
        Rafraichir();
    }

    private void PoserOnglet(EtatCommande etat)
    {
        OngletActif = etat;
        Appliques = TraitementCommandes.SurOnglet(Appliques, etat);
    }

    /// <summary>
    /// Relit les onglets et la liste selon les criteres appliques. Un onglet
    /// « Saisie manuelle » vide disparait : on revient alors sur « À traiter ».
    /// </summary>
    private void Rafraichir()
    {
        Onglets = TraitementCommandes.Onglets(Toutes, Appliques);
        if (Onglets.All(o => o.Etat != OngletActif)) PoserOnglet(EtatCommande.ATraiter);
        Groupes = TraitementCommandes.Regrouper(TraitementCommandes.Filtrer(Toutes, Appliques), DirectsEnTete);
        Ordre = Groupes.SelectMany(g => g.Commandes).ToList();
    }

    private static int Rang(CommandeRecue c) => TraitementCommandes.RangGroupe(TraitementCommandes.Groupe(c.Famille));

    private void BasculerDirects()
    {
        DirectsEnTete = !DirectsEnTete;
        Rafraichir();
    }

    // ================================================================ selection

    private void FermerPanneau()
    {
        Selection = null;
        BandeOuverte = null;
        MotifSaisi = string.Empty;
        CoordonneesOuvertes = false;
    }

    private void Choisir(CommandeRecue c)
    {
        Selection = c;
        // Comme NeoRIG, une commande choisie replie la recherche : le panneau
        // garde toute la hauteur (capture en theme clair du 18/11/2026).
        RechercheOuverte = false;
        RapprochementDeplie = false;
        BandeOuverte = null;
        MotifSaisi = string.Empty;
        CoordonneesOuvertes = false;

        RechSiren = c.Siren;
        RechRaison = c.Denomination;
        TousDebiteurs = false;
        TousEtablissements = false;
        Resultat = TraitementCommandes.RechercherPour(c);

        TypesCoches.Clear();
        foreach (var k in TraitementCommandes.CasesCommandees(c))
            foreach (var code in k.EstRegroupement ? k.Enfants.Select(e => e.Code) : [k.Code])
                TypesCoches.Add(code);
        QuantiteInscriptions = 1;

        SousTypes.Clear();
        if (c.SousType is { } st && st != SousTypeCno.InjonctionDePayer) SousTypes.Add(st);
        QuantiteCno = 1;
        CertificatDOpposition = false;

        Certificats.Clear();
        Certificats[CertificatFaillite.NonFaillite] = c.Famille == FamilleCommande.Faillite ? 1 : 0;
        Certificats[CertificatFaillite.NonRcs] = 0;
        Certificats[CertificatFaillite.NonRsac] = 0;
        CertificatDeFaillite = false;

        // Une commande web porte deja son adresse mail : la case est cochee par defaut (#2294).
        AdresseMail = c.Email;
        EnvoiMail = c.Origine != OrigineCommande.Guichet && c.Email.Length > 0;
    }

    private static List<(string Libelle, string Valeur)> DonneesRecues(CommandeRecue c)
    {
        var champs = new List<(string, string)>();
        switch (TraitementCommandes.Groupe(c.Famille))
        {
            case FamilleCommande.NonOpposition:
                champs.Add(("Sous-type reçu", c.SousType is { } st ? TraitementCommandes.Libelle(st) : ""));
                champs.Add(("N° affaire", c.NumeroAffaire));
                champs.Add(("Dénomination", c.Denomination));
                champs.Add(("SIREN", TraitementCommandes.FormaterSiren(c.Siren)));
                champs.Add(("Réf. client", c.ReferenceClient));
                champs.Add(("Adresse de livraison", string.Join(", ", c.AdresseLivraison)));
                champs.Add(("Email", c.Email));
                break;
            case FamilleCommande.Faillite:
                champs.Add(("N° de gestion", c.NumeroGestion));
                champs.Add(("Raison sociale", c.Denomination));
                champs.Add(("SIREN", TraitementCommandes.FormaterSiren(c.Siren)));
                break;
            default:
                champs.Add(("Raison sociale", c.Denomination));
                champs.Add(("SIREN", TraitementCommandes.FormaterSiren(c.Siren)));
                champs.Add(("Non Inscrit", c.Famille == FamilleCommande.EtatEndettementNonInscrit ? "Oui" : "Non"));
                champs.Add(("Adresse", ""));
                break;
        }
        champs.Add(("Nom du requérant", c.Requerant));
        return champs;
    }

    // ================================================================ zones

    /// <summary>Le compteur « − n + » ; « Un clic met 1, un autre remet 0 » au clic sur la valeur.
    /// Le .razor posait ces valeurs depuis les boutons du RenderFragment Compteur.</summary>
    private static int Compter(int valeur, string sens) => sens switch
    {
        "moins" => Math.Max(0, valeur - 1),
        "valeur" => valeur == 0 ? 1 : 0,
        "plus" => Math.Min(99, valeur + 1),
        _ => throw new ArgumentException($"Sens de compteur inconnu : « {sens} ».", nameof(sens)),
    };

    private bool TousTypesCoches(List<CaseType> cases) =>
        cases.SelectMany(k => k.EstRegroupement ? k.Enfants : [k]).All(k => TypesCoches.Contains(k.Code));

    private void CocherTous(List<CaseType> cases, bool cocher)
    {
        foreach (var k in cases.SelectMany(k => k.EstRegroupement ? k.Enfants : [k]))
            if (cocher) TypesCoches.Add(k.Code); else TypesCoches.Remove(k.Code);
    }

    private void RelancerRapprochement() => Resultat = TraitementCommandes.Rechercher(RechSiren, RechRaison);

    private void Retenir(EntiteRapprochee e)
    {
        if (Selection is null) return;
        Selection.CibleRetenue = e.Cle;
        // Le dossier retenu se resume en une ligne ; « Changer de dossier » rouvre la recherche.
        RapprochementDeplie = false;
        // Changer de cible change les certificats : les motifs de la cible quittee n'ont plus cours.
        CertificatDeFaillite = false;
    }

    // ================================================================ edition et decisions

    private List<DocumentEdite> DocumentsEnCours(CommandeRecue c)
    {
        var cible = TraitementCommandes.Entite(c.CibleRetenue);
        // Un certificat bloque ne se commande pas : son compteur compte pour zero.
        var certificats = Certificats.ToDictionary(kv => kv.Key,
            kv => cible is not null && TraitementCommandes.MotifBlocage(kv.Key, cible) is not null ? 0 : kv.Value);
        if (CertificatDeFaillite) certificats[CertificatFaillite.NonFaillite] = Math.Max(1, Certificats[CertificatFaillite.NonFaillite]);
        var typesCoches = TraitementCommandes.CasesCommandees(c)
            .SelectMany(k => k.EstRegroupement ? k.Enfants : [k]).Count(k => TypesCoches.Contains(k.Code));
        return TraitementCommandes.Documents(c, typesCoches, QuantiteInscriptions, certificats, CertificatDeFaillite,
                                             SousTypes, QuantiteCno, CertificatDOpposition);
    }

    /// <summary>Pourquoi Valider reste ferme ; nul quand la commande se valide.</summary>
    private string? BlocageValider(CommandeRecue c, IReadOnlyList<DocumentEdite> docs)
    {
        if (TraitementCommandes.Refus(c, ActionCommande.Valider, null) is { } refus) return refus;
        var groupe = TraitementCommandes.Groupe(c.Famille);
        if (TraitementCommandes.Entite(c.CibleRetenue) is null)
            return groupe == FamilleCommande.EtatEndettement
                ? "Retenez le débiteur dans le rapprochement pour rattacher la commande."
                : "Sélectionnez le dossier concerné pour rattacher le certificat.";
        // Seule la lettre d'accompagnement : il ne reste rien a delivrer.
        var produits = docs.Count(d => d.Document != "Lettre d'accompagnement" && d.Document != "Facture");
        if (groupe == FamilleCommande.NonOpposition && !CertificatDOpposition)
        {
            if (SousTypes.Count == 0) return "Cochez un type de certificat pour lancer le contrôle de délivrance.";
            var controle = TraitementCommandes.ControlerNonOpposition(c);
            if (controle.Issue == IssueDelivrance.Rejet) return controle.Motif;
        }
        if (produits == 0)
            return groupe switch
            {
                FamilleCommande.Faillite => "Aucun certificat n'est délivrable : la commande doit être rejetée.",
                FamilleCommande.EtatEndettement => "Cochez au moins un type d'inscription, avec une quantité.",
                _ => "Aucun certificat n'est commandé : la quantité est à zéro.",
            };
        return null;
    }

    private void OuvrirBande(ActionCommande action)
    {
        BandeOuverte = action;
        MotifSaisi = string.Empty;
    }

    private void FermerBande()
    {
        BandeOuverte = null;
        MotifSaisi = string.Empty;
    }

    private void Decider(ActionCommande action)
    {
        if (Selection is not { } c) return;
        var docs = DocumentsEnCours(c);
        if (action == ActionCommande.Valider && BlocageValider(c, docs) is { } blocage)
        {
            Message = (blocage, "avertissement");
            return;
        }
        if (TraitementCommandes.Refus(c, action, MotifSaisi) is { } refus)
        {
            Message = (refus, "avertissement");
            return;
        }

        var ordre = Ordre;
        var texte = TraitementCommandes.Appliquer(Toutes, c, action, MotifSaisi, docs,
                                                   action == ActionCommande.Valider && EnvoiMail ? AdresseMail : null);
        BandeOuverte = null;
        MotifSaisi = string.Empty;

        // Valider, Mettre en attente et Rejeter enchainent seuls sur la suivante.
        var suivante = TraitementCommandes.Suivante(ordre, c);
        Selection = null;
        Rafraichir();
        if (suivante is not null && Ordre.Any(x => x.Id == suivante.Id))
        {
            Choisir(suivante);
        }
        else
        {
            texte = $"{texte} C'était la dernière commande de la liste.";
        }
        Message = (texte, "succes");
    }

    private void EnvoyerSansAttendre()
    {
        if (Selection is null) return;
        Message = (TraitementCommandes.EnvoyerSansAttendre(Toutes, Selection), "succes");
        Rafraichir();
    }

    private void Consulter(DocumentConsulte document)
    {
        if (Selection is not { } c) return;
        var cible = TraitementCommandes.Entite(c.CibleRetenue);
        var siren = TraitementCommandes.FormaterSiren(c.Siren);
        Message = document switch
        {
            DocumentConsulte.Kbis or DocumentConsulte.EtatSynthese when cible is null || cible.NonInscrit =>
                ("Retenez un dossier RCS pour afficher ce document.", "avertissement"),
            DocumentConsulte.Kbis or DocumentConsulte.EtatSynthese =>
                ("Kbis et état de synthèse ne sont pas encore portés en V3.", "info"),
            DocumentConsulte.KbisHorsRessort when c.Siren.Length == 0 =>
                ("Le Kbis hors ressort se demande sur le SIREN de la commande : celle-ci n'en porte aucun.", "avertissement"),
            DocumentConsulte.KbisHorsRessort =>
                ($"Extrait RCS demandé pour le SIREN {siren} au greffe qui détient l'immatriculation — à distinguer du Kbis du dossier de ce greffe.", "info"),
            _ when c.Siren.Length == 0 =>
                ("La synthèse RNE se demande sur le SIREN de la commande : celle-ci n'en porte aucun.", "avertissement"),
            _ =>
                ($"Fiche nationale au Registre National des Entreprises, demandée à l'INPI pour le SIREN {siren} — à distinguer de l'état de synthèse du dossier de ce greffe.", "info"),
        };
    }

    // ================================================================ saisie au guichet

    private void NouvelleCommande()
    {
        var c = TraitementCommandes.NouvelleSaisie(Toutes);
        // La saisie en cours a son propre onglet : on s'y place pour la voir.
        PoserOnglet(EtatCommande.SaisieManuelle);
        Rafraichir();
        Choisir(c);
        Message = ($"Commande {c.Reference} créée : complétez les informations saisies, puis enregistrez.", "info");
    }

    /// <summary>Le choix du type d'une saisie au guichet (l'@onchange du select du .razor).</summary>
    private void ChoisirTypeSaisi(string? valeur)
    {
        if (Selection is not null && Enum.TryParse<FamilleCommande>(valeur, out var f)) Selection.Famille = f;
    }

    private void EnregistrerSaisie()
    {
        if (Selection is not { } c) return;
        if (TraitementCommandes.EnregistrerSaisie(c) is { } refus)
        {
            Message = (refus, "avertissement");
            return;
        }
        // Enregistree, elle passe « À traiter » : on la suit dans son onglet.
        PoserOnglet(EtatCommande.ATraiter);
        Rafraichir();
        Choisir(c);
        Message = ($"Commande {c.Reference} enregistrée.", "succes");
    }

    private void AnnulerSaisie()
    {
        if (Selection is not { } c) return;
        Toutes.Remove(c);
        Selection = null;
        Rafraichir();
        Message = ($"Commande {c.Reference} annulée.", "succes");
    }

    // ================================================================ coordonnees

    private void OuvrirCoordonnees()
    {
        if (Selection is null) return;
        RemplirAdresse(Selection.AdresseEnVigueur);
        CoordonneesOuvertes = true;
    }

    private void FermerCoordonnees() => CoordonneesOuvertes = false;

    /// <summary>Repartit les lignes d'une adresse dans les champs : nom en tete, code postal et ville en dernier.</summary>
    private void RemplirAdresse(IReadOnlyList<string> lignes)
    {
        foreach (var ch in ChampsAdresse) Adresse[ch.Cle] = string.Empty;
        if (lignes.Count == 0) return;
        Adresse["Nom"] = lignes[0];
        var reste = lignes.Skip(1).ToList();
        var derniere = reste.LastOrDefault() ?? string.Empty;
        var cp = System.Text.RegularExpressions.Regex.Match(derniere, @"^(\d{5})\s+(.+)$");
        if (cp.Success)
        {
            Adresse["CodePostal"] = cp.Groups[1].Value;
            Adresse["Localite"] = cp.Groups[2].Value;
            reste.RemoveAt(reste.Count - 1);
        }
        if (reste.Count >= 2) { Adresse["Complement"] = reste[0]; Adresse["Voie"] = string.Join(" ", reste.Skip(1)); }
        else if (reste.Count == 1) Adresse["Voie"] = reste[0];
    }

    /// <summary>Les lignes que la plieuse imprimera, dans l'ordre de l'enveloppe.</summary>
    private List<string> LignesEnveloppe()
    {
        string V(string cle) => Adresse.TryGetValue(cle, out var v) ? v.Trim() : string.Empty;
        var bp = V("BoitePostale");
        var pays = V("Pays");
        return new[]
        {
            V("Nom"), V("Complement"), V("Voie"), V("LieuDit"), bp.Length > 0 ? "BP " + bp : "",
            $"{V("CodePostal")} {V("Localite")}".Trim(),
            pays.Length > 0 && TraitementCommandes.Normaliser(pays) != "FRANCE" ? pays.ToUpperInvariant() : "",
        }.Where(l => l.Length > 0).ToList();
    }

    private void EnregistrerAdresse()
    {
        if (Selection is null) return;
        Selection.AdresseCorrigee = LignesEnveloppe().ToArray();
        CoordonneesOuvertes = false;
        Message = ("Adresse du destinataire enregistrée.", "succes");
    }

    private void RevenirAdresseRecue()
    {
        if (Selection is null) return;
        Selection.AdresseCorrigee = [];
        CoordonneesOuvertes = false;
        Message = ("Les documents seront envoyés à l'adresse reçue avec la commande.", "succes");
    }

    // ================================================================ gestes (pont React)

    /// <summary>
    /// Un geste de l'ecran React : le nom de la methode ou du champ que le .razor
    /// branchait sur l'element, et sa valeur. Un nom inconnu est une erreur franche
    /// (le .tsx et ce fichier ont diverge), jamais un geste ignore en silence.
    /// </summary>
    internal void Agir(string geste, JsonElement valeur)
    {
        switch (geste)
        {
            // ---- onglets, recherche, liste
            case "ChoisirOnglet": ChoisirOnglet(Enum.Parse<EtatCommande>(Texte(valeur))); break;
            case "Rechercher": Rechercher(); break;
            case "Famille": FamilleChoisie = Texte(valeur); break;
            case "Du": PoserDate(ref Du, Texte(valeur)); break;
            case "Au": PoserDate(ref Au, Texte(valeur)); break;
            case "Texte": PoserTexte(Texte(valeur, "champ"), Texte(valeur, "valeur")); break;
            case "Basculer": Basculer(Texte(valeur)); break;
            case "FermerMessage": Message = null; break;
            case "BasculerDirects": BasculerDirects(); break;
            case "NouvelleCommande": NouvelleCommande(); break;
            case "Choisir": Choisir(Commande(valeur.GetInt32())); break;
            case "FermerPanneau": FermerPanneau(); break;

            // ---- en-tete du panneau
            case "OuvrirCoordonnees": OuvrirCoordonnees(); break;
            case "Consulter": Consulter(Enum.Parse<DocumentConsulte>(Texte(valeur))); break;
            case "EnvoyerSansAttendre": EnvoyerSansAttendre(); break;

            // ---- saisie au guichet
            case "ChoisirTypeSaisi": ChoisirTypeSaisi(Texte(valeur)); break;
            case "AnnulerSaisie": AnnulerSaisie(); break;
            case "EnregistrerSaisie": EnregistrerSaisie(); break;

            // ---- zones
            case "RelancerRapprochement": RelancerRapprochement(); break;
            case "Retenir": Retenir(TraitementCommandes.Entite(Texte(valeur))
                                    ?? throw new ArgumentException($"Entite inconnue : « {Texte(valeur)} ».")); break;
            case "CocherTous":
                if (Selection is { } s) CocherTous(TraitementCommandes.CasesCommandees(s), valeur.GetBoolean());
                break;
            case "CocherType":
                var code = Texte(valeur, "code");
                if (Coche(valeur)) TypesCoches.Add(code); else TypesCoches.Remove(code);
                break;
            case "SousType":
                var st = Enum.Parse<SousTypeCno>(Texte(valeur, "valeur"));
                if (Coche(valeur)) SousTypes.Add(st); else SousTypes.Remove(st);
                break;
            case "CertificatDOpposition": CertificatDOpposition = valeur.GetBoolean(); break;
            case "CertificatDeFaillite": CertificatDeFaillite = valeur.GetBoolean(); break;
            case "Compteur": PoserCompteur(Texte(valeur, "cible"), Texte(valeur, "sens")); break;

            // ---- pied : bande et decisions
            case "OuvrirBande": OuvrirBande(Enum.Parse<ActionCommande>(Texte(valeur))); break;
            case "FermerBande": FermerBande(); break;
            case "Decider": Decider(Enum.Parse<ActionCommande>(Texte(valeur))); break;

            // ---- coordonnees du destinataire
            case "FermerCoordonnees": FermerCoordonnees(); break;
            case "Adresse": Adresse[Texte(valeur, "cle")] = Texte(valeur, "valeur"); break;
            case "EnregistrerAdresse": EnregistrerAdresse(); break;
            case "RevenirAdresseRecue": RevenirAdresseRecue(); break;

            default: throw new ArgumentException($"Geste inconnu de l'ecran des commandes web : « {geste} ».", nameof(geste));
        }
    }

    private static string Texte(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
    private static string Texte(JsonElement v, string propriete) =>
        v.TryGetProperty(propriete, out var p) && p.ValueKind != JsonValueKind.Null ? Texte(p) : string.Empty;
    private static bool Coche(JsonElement v) => v.TryGetProperty("coche", out var p) && p.ValueKind == JsonValueKind.True;

    private CommandeRecue Commande(int id) =>
        Toutes.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException($"Commande inconnue : {id}.");

    /// <summary>@bind d'un DateTime? : vide = aucune date, illisible = la valeur d'avant.</summary>
    private static void PoserDate(ref DateTime? cible, string texte)
    {
        if (texte.Length == 0) cible = null;
        else if (DateTime.TryParseExact(texte, FormatDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) cible = d;
    }

    /// <summary>Les champs texte que le .razor liait par @bind:event="oninput".</summary>
    private void PoserTexte(string champ, string texte)
    {
        switch (champ)
        {
            case nameof(SirenCherche): SirenCherche = texte; break;
            case nameof(ReferenceCherchee): ReferenceCherchee = texte; break;
            case nameof(RechSiren): RechSiren = texte; break;
            case nameof(RechRaison): RechRaison = texte; break;
            case nameof(MotifSaisi): MotifSaisi = texte; break;
            case nameof(AdresseMail): AdresseMail = texte; break;
            // Les trois champs de la saisie au guichet : c.Denomination, c.Siren, c.Requerant.
            case nameof(CommandeRecue.Denomination): if (Selection is { } d) d.Denomination = texte; break;
            case nameof(CommandeRecue.Siren): if (Selection is { } s) s.Siren = texte; break;
            case nameof(CommandeRecue.Requerant): if (Selection is { } r) r.Requerant = texte; break;
            default: throw new ArgumentException($"Champ inconnu : « {champ} ».", nameof(champ));
        }
    }

    /// <summary>Les bascules que le .razor ecrivait @onclick="() => X = !X".</summary>
    private void Basculer(string champ)
    {
        switch (champ)
        {
            case nameof(RechercheOuverte): RechercheOuverte = !RechercheOuverte; break;
            case nameof(EnAttenteEnvoi): EnAttenteEnvoi = !EnAttenteEnvoi; break;
            case nameof(RapprochementDeplie): RapprochementDeplie = !RapprochementDeplie; break;
            case nameof(TousDebiteurs): TousDebiteurs = !TousDebiteurs; break;
            case nameof(TousEtablissements): TousEtablissements = !TousEtablissements; break;
            case nameof(EnvoiMail): EnvoiMail = !EnvoiMail; break;
            default: throw new ArgumentException($"Bascule inconnue : « {champ} ».", nameof(champ));
        }
    }

    /// <summary>Les trois usages du compteur du .razor : inscriptions, non-opposition, certificats.</summary>
    private void PoserCompteur(string cible, string sens)
    {
        switch (cible)
        {
            case nameof(QuantiteInscriptions): QuantiteInscriptions = Compter(QuantiteInscriptions, sens); break;
            case nameof(QuantiteCno): QuantiteCno = Compter(QuantiteCno, sens); break;
            default:
                var cert = Enum.Parse<CertificatFaillite>(cible);
                Certificats[cert] = Compter(Certificats[cert], sens);
                break;
        }
    }

    // ================================================================ vue (ce que le balisage lisait)

    /// <summary>Le format d'un &lt;input type="date"&gt;, celui du @bind de Blazor.</summary>
    private const string FormatDate = "yyyy-MM-dd";

    private static string Tiret(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? "—" : valeur;

    private static OptionCw[] Familles() =>
        Enum.GetValues<FamilleCommande>().Select(f => new OptionCw(f.ToString(), TraitementCommandes.Libelle(f))).ToArray();

    private static DocumentCw[] Documents(IEnumerable<DocumentEdite> docs) =>
        docs.Select(d => new DocumentCw(d.Document, d.Bac, d.Exemplaires?.ToString() ?? "—", d.Origine)).ToArray();

    internal VueCommandesWeb Vue(string session)
    {
        var onglets = Onglets.Select(o => new OngletCw(o.Etat.ToString(), TraitementCommandes.Libelle(o.Etat),
                                                       o.Nombre, OngletActif == o.Etat)).ToArray();
        var recherche = new RechercheCw(FamilleChoisie, Familles(),
            Du?.ToString(FormatDate, CultureInfo.InvariantCulture) ?? string.Empty,
            Au?.ToString(FormatDate, CultureInfo.InvariantCulture) ?? string.Empty,
            SirenCherche, ReferenceCherchee, EnAttenteEnvoi,
            TraitementCommandes.DateTravail.ToString("dd/MM/yyyy"));
        var lignes = Ordre.Select(cmd => new LigneCw(cmd.Id, Rang(cmd), cmd.Id == Selection?.Id, Tiret(cmd.Denomination),
                                                     TraitementCommandes.Precisions(cmd),
                                                     TraitementCommandes.Libelle(cmd.Famille),
                                                     TraitementCommandes.EstRapprochementDirect(cmd))).ToArray();

        return new VueCommandesWeb(session, onglets, RechercheOuverte, TraitementCommandes.RechercheAffinee(Appliques),
            TraitementCommandes.Resumer(Appliques), recherche,
            Message is { } m ? new MessageCw(m.Texte, m.Severite) : null,
            TraitementCommandes.Compteur(Groupes), DirectsEnTete, lignes,
            Selection is { } c ? VuePanneau(c) : null,
            CoordonneesOuvertes && Selection is not null ? VueCoordonnees(Selection) : null);
    }

    private PanneauCw VuePanneau(CommandeRecue c)
    {
        var enveloppe = TraitementCommandes.Enveloppe(Toutes, c);
        EnveloppeCw? vueEnveloppe = enveloppe.Count > 1
            ? new EnveloppeCw(c.Reference,
                enveloppe.Where(x => x.Id != c.Id).Select(mb => new MembreCw(mb.Id,
                    $"{TraitementCommandes.Libelle(mb.Famille)} : {TraitementCommandes.Libelle(mb.Etat)}",
                    TraitementCommandes.IconeEtat(mb.Etat), TraitementCommandes.Libelle(mb.Famille))).ToArray(),
                TraitementCommandes.EtatEnveloppe(enveloppe),
                enveloppe.Any(x => x.EnvoiGroupeEnAttente))
            : null;

        var saisie = c.Etat == EtatCommande.SaisieManuelle
            ? new SaisieCw(c.Famille.ToString(), Familles(), c.Denomination, c.Siren, c.Requerant)
            : null;
        var donnees = c.Etat == EtatCommande.SaisieManuelle
            ? []
            : DonneesRecues(c).Select(d => new ChampCw(d.Libelle, Tiret(d.Valeur))).ToArray();
        var traitee = c.EstTraitee
            ? new TraiteeCw(c.Etat == EtatCommande.Rejetee, c.Motif, c.Reference, Documents(c.Envoyes))
            : null;

        // Les zones du detail, selon le groupe du type (le @switch du .razor).
        var enCours = !c.EstTraitee && c.Etat != EtatCommande.SaisieManuelle;
        RapprochementCw? rapprochement = null;
        TypesCw? types = null;
        NonOppositionCw? nonOpposition = null;
        CertificatsCw? certificats = null;
        if (enCours)
        {
            switch (TraitementCommandes.Groupe(c.Famille))
            {
                case FamilleCommande.EtatEndettement:
                    rapprochement = VueRapprochement(c, debiteur: true);
                    types = VueTypes(c);
                    break;
                case FamilleCommande.NonOpposition:
                    rapprochement = VueRapprochement(c, debiteur: false);
                    nonOpposition = VueNonOpposition(c);
                    break;
                case FamilleCommande.Faillite:
                    rapprochement = VueRapprochement(c, debiteur: false);
                    certificats = VueCertificats(c);
                    break;
            }
        }

        PiedCw? pied = null;
        if (enCours)
        {
            var docs = DocumentsEnCours(c);
            var blocage = BlocageValider(c, docs);
            var bande = BandeOuverte is { } b
                ? new BandeCw(b == ActionCommande.Rejeter, MotifSaisi,
                              TraitementCommandes.Refus(c, b, MotifSaisi) is not null, TraitementCommandes.LongueurMaxMotif)
                : null;
            pied = new PiedCw(bande, TraitementCommandes.BarreEdition(docs), Documents(docs), EnvoiMail, AdresseMail,
                              TraitementCommandes.Refus(c, ActionCommande.MettreEnAttente, "") is not null, blocage);
        }

        return new PanneauCw(c.Id, TraitementCommandes.Titre(c), Rang(c), TraitementCommandes.Libelle(c.Famille),
            c.Etat != EtatCommande.ATraiter ? new EtatCw(TraitementCommandes.IconeEtat(c.Etat), TraitementCommandes.Libelle(c.Etat)) : null,
            Tiret(c.Denomination), TraitementCommandes.LigneReference(c), vueEnveloppe, saisie, donnees, traitee,
            enCours && c.Etat == EtatCommande.EnAttente && c.Motif.Length > 0 ? c.Motif : null,
            rapprochement, types, nonOpposition, certificats, pied);
    }

    /// <summary>Ce que lisait le RenderFragment ZoneRapprochement du .razor.</summary>
    private RapprochementCw VueRapprochement(CommandeRecue c, bool debiteur)
    {
        var retenue = TraitementCommandes.Entite(c.CibleRetenue);
        var entites = Resultat.Entites;
        var affichees = entites.Select(e => (Entite: e, Lignes: debiteur
                ? TraitementCommandes.LignesVisibles(e, TousDebiteurs, TousEtablissements)
                : e.Lignes.ToList())).ToList();

        RetenueCw? vueRetenue = null;
        if (retenue is not null)
        {
            // Le dossier retenu en UNE ligne (maquette « Boite de tri ») : son
            // n° de gestion, son nom, la pastille, et l'adresse du siege.
            var siege = TraitementCommandes.LignesVisibles(retenue, false, false).FirstOrDefault();
            vueRetenue = new RetenueCw(retenue.Gestion.Length > 0 ? retenue.Gestion : "NI", retenue.Designation,
                retenue.Radie,
                (siege is null ? "" : $"{siege.Nature} · {siege.Adresse} · ") + TraitementCommandes.ResumeEntite(retenue),
                TraitementCommandes.Ecarts(c, retenue).ToArray());
        }

        var vueEntites = affichees.Select(a =>
        {
            var e = a.Entite;
            var estRetenue = e.Cle == c.CibleRetenue;
            return new EntiteCw(e.Cle, e.NonInscrit, e.Gestion, e.Designation, TraitementCommandes.FormaterSiren(e.Siren),
                e.Type, TraitementCommandes.ResumeEntite(e), e.Radie, estRetenue,
                a.Lignes.Select(l => new LigneEntiteCw(l.Nature, l.Adresse, l.NbInscriptions)).ToArray(),
                estRetenue ? TraitementCommandes.Ecarts(c, e).ToArray() : []);
        }).ToArray();

        return new RapprochementCw(debiteur, c.Famille == FamilleCommande.Faillite, retenue is not null,
            RapprochementDeplie, c.EstTraitee, vueRetenue, RechSiren, RechRaison, TousDebiteurs, TousEtablissements,
            Resultat.Message, vueEntites,
            entites.Count > 0 ? TraitementCommandes.ResumeRapprochement(entites, affichees.Sum(a => a.Lignes.Count)) : "");
    }

    /// <summary>Ce que lisait le RenderFragment ZoneTypes (et CaseCochable) du .razor.</summary>
    private TypesCw? VueTypes(CommandeRecue c)
    {
        var cases = TraitementCommandes.CasesCommandees(c);
        var dt = TraitementCommandes.DateTravail.ToString("dd/MM/yyyy");
        var items = new List<ItemTypeCw>();
        foreach (var k in cases)
        {
            if (k.EstRegroupement)
            {
                items.Add(new ItemTypeCw(true, k.Code, k.Libelle, false, false));
                foreach (var enfant in k.Enfants)
                    items.Add(new ItemTypeCw(false, enfant.Code, enfant.Libelle, true, TypesCoches.Contains(enfant.Code)));
            }
            else
            {
                items.Add(new ItemTypeCw(false, k.Code, k.Libelle, false, TypesCoches.Contains(k.Code)));
            }
        }
        return new TypesCw(dt, TousTypesCoches(cases), items.ToArray(), QuantiteInscriptions);
    }

    /// <summary>Ce que lisait le RenderFragment ZoneNonOpposition du .razor.</summary>
    private NonOppositionCw VueNonOpposition(CommandeRecue c)
    {
        var controle = SousTypes.Count > 0 ? TraitementCommandes.ControlerNonOpposition(c) : null;
        var sousTypes = Enum.GetValues<SousTypeCno>().Select(st => new SousTypeCw(st.ToString(), TraitementCommandes.Libelle(st),
            st == SousTypeCno.InjonctionDePayer, SousTypes.Contains(st))).ToArray();
        var vueControle = controle is null ? null : new ControleCw(
            controle.DerniereFormalite?.ToString("dd/MM/yyyy") ?? "—", TraitementCommandes.Delai(controle),
            controle.Seuil, controle.Issue.ToString(), controle.Motif);
        return new NonOppositionCw(sousTypes, QuantiteCno, !CertificatDOpposition, CertificatDOpposition, vueControle);
    }

    /// <summary>Ce que lisait le RenderFragment ZoneCertificats du .razor.</summary>
    private CertificatsCw VueCertificats(CommandeRecue c)
    {
        var cible = TraitementCommandes.Entite(c.CibleRetenue);
        var motifNonFaillite = cible is null ? null : TraitementCommandes.MotifBlocage(CertificatFaillite.NonFaillite, cible);
        var lignes = Enum.GetValues<CertificatFaillite>().Select(cert =>
        {
            var motif = cible is null ? null : TraitementCommandes.MotifBlocage(cert, cible);
            var substitue = cert == CertificatFaillite.NonFaillite && CertificatDeFaillite;
            return new CertificatCw(cert.ToString(), TraitementCommandes.Libelle(cert), Certificats[cert],
                                    motif is null, motif is not null && !substitue, motif);
        }).ToArray();
        return new CertificatsCw(TraitementCommandes.DateTravail.ToString("dd/MM/yyyy"), lignes,
                                 TraitementCommandes.OuvreLaBasculeFaillite(motifNonFaillite), CertificatDeFaillite);
    }

    private CoordonneesCw VueCoordonnees(CommandeRecue selection)
    {
        var lignes = LignesEnveloppe();
        return new CoordonneesCw(selection.EstTraitee,
            ChampsAdresse.Select(ch => new ChampAdresseCw(ch.Cle, ch.Libelle, ch.Large, Adresse[ch.Cle])).ToArray(),
            lignes.ToArray(),
            TraitementCommandes.ControlerAdresse(lignes, Adresse["Pays"]).ToArray(),
            selection.AdresseCorrigee.Length > 0);
    }
}
