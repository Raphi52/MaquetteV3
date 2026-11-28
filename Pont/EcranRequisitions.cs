using System.Globalization;
using System.Text.Json;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

// ---------------------------------------------------------------- la vue rendue a React

/// <summary>Une commande web de la zone haute, prete a afficher.</summary>
/// <param name="Rang">Sa place dans le jeu de l'ecran : c'est elle qui revient avec un clic.</param>
public sealed record LigneCommandeWebRequi(int Rang, string Classe, string Origine, string Numero, string Recue,
                                           string Demandeur, string Famille, string CodeEdi, string Societe,
                                           string Siren, string Etat);

/// <summary>Une ligne de la grille des resultats (OPE_REQUI_RESULTATS).</summary>
public sealed record LigneEntrepriseRequi(int Rang, bool Radie, string Infobulle, string Id, string Siren,
                                          string Gestion, string Designation, string TypeAdresse, string Adresse,
                                          string Type, bool NonInscrit, int NbInscriptions);

/// <summary>Une commande du panier (OPE_REQUI_PANIER).</summary>
public sealed record LignePanierRequi(int Rang, bool EnCours, int NumCommande, string Siren, string Gestion,
                                      string Designation, string Adresse, string Resume);

/// <summary>Une des 37 cases d'inscription, pour la commande en cours.</summary>
public sealed record CaseArticleRequi(string Libelle, string Titre, bool Cochee, int Qte);

/// <summary>Les criteres de recherche tels que saisis (OPE_REQUI_RECHERCHE).</summary>
public sealed record CriteresRequi(string RaisonSociale, string Siren, string Dirigeant, string Activite,
                                   string NumVoie, string Adresse, string CodePostal, string Ville,
                                   bool Phonetique, bool Etendre, bool TousEtb, bool SupprCompris, bool Guichet,
                                   int LimiteDossiers);

/// <summary>La commande en cours dans le volet « Articles commandes » (OPE_REQUI_ARTICLES).
/// Les dates sont au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
public sealed record ArticlesRequi(int NumCommande, string Designation,
                                   bool KBisPossible, int NbKBis, int NbHistorique, int NbDAS,
                                   bool CnfPossible, int NbCertifNonFaillite, string DateCnf,
                                   int NbCertifNonRcs, string DateCnRcs, int NbCertifNonRsac, string DateCnRsac,
                                   int NbCertifNonOppo, string DateCnOppo,
                                   int NbDjCopie, int NbDjExpedition, int NbNavire,
                                   int NbBeInterne, int NbBeExterne, string NomSci,
                                   string NomRequi, string DateTravail, bool TravailFrais, int AgeTravailJours,
                                   int JoursFraicheur, bool ToutCoche, CaseArticleRequi[] Cases, int NbInscr);

/// <summary>Tout ce que web/src/ecrans/EcranRequisitions.tsx affiche.</summary>
public sealed record VueRequisitions(string Session,
                                     int NbATraiter, bool MasquerTraitees, string? CleTri, bool TriAscendant,
                                     LigneCommandeWebRequi[] CommandesWeb, bool CommandeWebChoisie,
                                     CriteresRequi Criteres, bool RaisonSocialeInvalide,
                                     string Volet, int NbPanier, string Compteur, string Message,
                                     LigneEntrepriseRequi[] Trouvees, bool ARecherche, bool Interrompue,
                                     int LimiteRetenue, LignePanierRequi[] Panier, ArticlesRequi? Articles,
                                     string ModeResultats = "demo", string? MessageResultats = null, bool ZoneCommandesWeb = true);

/// <summary>
/// Pont de l'ecran PROC_REQUI (web/src/ecrans/EcranRequisitions.tsx) : requisitions
/// et commande de documents, fiche 275.
///
/// ECRAN A SESSION (web/src/session.tsx) : l'etat de l'ecran est fait d'objets de
/// Metier/ (CommandeRequi calcule sa fraicheur et ses blocages, CommandeWeb change
/// d'etat). Le @code d'EcranRequisitions.razor est donc deplace TEL QUEL dans
/// <see cref="SessionRequisitions"/> : memes champs, memes regles, memes noms
/// (tests/RequiCheck les relit, tests/TriCheck clique le tri par reflexion).
/// React ne recoit que la vue et remonte les gestes.
/// </summary>
public static class EcranRequisitions
{
    internal static readonly SessionsEcran<SessionRequisitions> Sessions = new();

    [JSInvokable("EcranRequisitions.Ouvrir")]
    public static VueRequisitions Ouvrir() => Ouvrir(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Ouvre l'ecran avec le serveur et le greffe donnes : branchee a la base, sans la zone des commandes web de la
    /// demonstration (EcranRequisitionsBase.cs) ; sans serveur, toute la demonstration (tests/RequiCheck).</summary>
    public static VueRequisitions Ouvrir(ClientServeur? serveur, string? greffe)
    {
        var session = new SessionRequisitions { ZoneCommandesWeb = serveur is null || string.IsNullOrWhiteSpace(greffe) };
        return session.Vue(Sessions.Ouvrir(session));
    }

    /// <summary>Un geste de l'ecran. « Rechercher » lit d'abord les entreprises dans la base du greffe quand un serveur est
    /// branche (EcranRequisitionsBase.cs, lot 6 du plan « RIG v3 100 % fonctionnel ») ; sans serveur, la demonstration.</summary>
    [JSInvokable("EcranRequisitions.Agir")]
    public static Task<VueRequisitions> AgirAsync(string session, string geste, JsonElement valeur)
        => AgirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, session, geste, valeur);

    /// <summary>Le geste avec le serveur et le greffe donnes (null serveur : la demonstration) : tests/EcransMainBaseCheck.</summary>
    public static async Task<VueRequisitions> AgirAsync(ClientServeur? serveur, string? greffe, string session, string geste, JsonElement valeur)
    {
        var s = Sessions.Lire(session);
        if (geste == "Rechercher") await s.ChargerDeLaBaseAsync(serveur, greffe);
        // « Terminer » un panier lu en base : le serveur ecrit la commande (EcritureRequisitions.cs), le pont la relit (lot 7 du plan
        // « RIG v3 complet », 28/11/2026). Un panier de la demonstration garde l'apercu de Terminer().
        // fix-ok: cause mesuree au banc ParcoursCompletCheck du 28/11/2026 -- PROC_REQUI rouge sur son geste : Terminer() n'ecrivait
        // rien en base (apercu seul, TODO #275) ; ce branchement envoie le geste au serveur et relit la commande ecrite.
        if (geste == "Terminer" && serveur is not null && !string.IsNullOrWhiteSpace(greffe) && s.TerminerEnBase() is { } demande)
        {
            var r = await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesTerminerRequisitions.Terminer, greffe), demande);
            var relue = r.Fait && r.Ids is [var numero, ..] ? await LireCommandeAsync(serveur, greffe, numero) : null;
            s.ApresTerminer(r, relue);
        }
        else s.Agir(geste, valeur);
        return s.Vue(session);
    }

    /// <summary>La commande terminee <paramref name="numeroDemande"/> (numero de sa demande REQUI), relue dans la base du greffe.</summary>
    public static Task<LectureEcranMain<CommandeRequiLue>> LireCommandeAsync(ClientServeur? serveur, string? greffe, string numeroDemande)
        => LectureEcransMain.LireAsync(serveur, greffe, "la commande terminée", (s, g) =>
            s.EnvoyerAsync<CommandeRequiDemandee, CommandeRequiLue>(RoutesEcransMain.Pour(RoutesTerminerRequisitions.Commande, g),
                                                                   new CommandeRequiDemandee(numeroDemande)));

    /// <summary>Le geste sur la demonstration seule (sans lecture en base) : tests/RequiCheck le joue directement.</summary>
    public static VueRequisitions Agir(string session, string geste, JsonElement valeur)
    {
        var s = Sessions.Lire(session);
        s.Agir(geste, valeur);
        return s.Vue(session);
    }

    [JSInvokable("EcranRequisitions.Fermer")]
    public static void Fermer(string session) => Sessions.Fermer(session);
}

/// <summary>
/// L'etat et les regles de l'ecran des requisitions : le @code de
/// EcranRequisitions.razor, deplace sans changement de sens. Les membres gardent
/// leur nom et leur visibilite d'origine. Ce qui est neuf : <see cref="Agir"/>
/// (un geste de l'ecran = l'appel de la methode que le .razor branchait) et
/// <see cref="Vue"/> (ce que le balisage lisait, mis a plat pour React).
/// </summary>
public sealed partial class SessionRequisitions
{
    // ---- Criteres (OPE_REQUI_RECHERCHE : proprietes d acces aux criteres) ----
    private string CritRaisonSociale = string.Empty;
    private string CritSiren = string.Empty;
    private string CritDirigeant = string.Empty;
    private string CritActivite = string.Empty;
    private string CritNumVoie = string.Empty;
    private string CritAdresse = string.Empty;
    private string CritCodePostal = string.Empty;
    private string CritVille = string.Empty;
    private bool CritPhonetique, CritEtendre, CritTousEtb, CritSupprCompris, CritGuichet;
    private int CritLimiteDossiers = 500;

    // ---- Zone haute : commandes web (fiche 275) ----
    private readonly CommandeWeb[] CommandesWeb = JeuDemo.CommandesWeb();
    private CommandeWeb? CommandeWebEnCours;
    private bool MasquerTraitees;

    private CommandeWeb[] CommandesWebVisibles
    {
        get
        {
            IEnumerable<CommandeWeb> q = MasquerTraitees
                ? CommandesWeb.Where(c => c.Etat != "Traitee")
                : CommandesWeb;

            // Sans clic sur un en-tete, l'ordre d'arrivee est conserve.
            if (CleTri is null) return q.ToArray();

            Func<CommandeWeb, object> clef = CleTri switch
            {
                "Origine" => c => c.Origine,
                "Numero" => c => c.Numero,
                "Demandeur" => c => c.Demandeur,
                "Famille" => c => c.Famille,
                "CodeEdi" => c => c.CodeEdi,
                "Societe" => c => c.Societe,
                "Siren" => c => c.Siren,
                "Etat" => c => c.Etat,
                _ => c => c.Recue
            };
            return (TriAscendant ? q.OrderBy(clef) : q.OrderByDescending(clef)).ToArray();
        }
    }

    // ---- Tri de la grille des commandes web (en-tetes cliquables) ----
    private string? CleTri;
    private bool TriAscendant = true;

    /// <summary>Premier clic : ordre croissant. Second clic sur la MEME
    /// colonne : ordre inverse.</summary>
    private void BasculerTri(string cle)
    {
        if (CleTri == cle) TriAscendant = !TriAscendant;
        else { CleTri = cle; TriAscendant = true; }
    }

    private int NbATraiter => CommandesWeb.Count(c => c.Etat != "Traitee");

    private string Volet = "resultats";
    private string Message = string.Empty;
    private bool ARecherche;
    private bool Interrompue;
    private int LimiteRetenue = 500;
    private EntrepriseRequi[] Trouvees = [];

    private readonly List<CommandeRequi> Panier = new();
    private CommandeRequi? CommandeEnCours;

    /// <summary>Vrai quand la prochaine ligne ajoutee ouvre une commande NEUVE
    /// sans regroupement (ForcedNewCmd de frmRechLET, porte dans OPE_REQUI_PANIER).</summary>
    private bool ForcerNouvelleCommande;

    /// <summary>Limite ramenee dans 50..500, comme LimiteDossiers de OPE_REQUI_RECHERCHE.</summary>
    private int Limite => Math.Clamp(CritLimiteDossiers, 50, 500);

    /// <summary>AuMoinsUnCritere de OPE_REQUI_RECHERCHE : les cases a cocher ne comptent pas.</summary>
    private bool AuMoinsUnCritere =>
        !string.IsNullOrWhiteSpace(CritRaisonSociale) || !string.IsNullOrWhiteSpace(CritSiren)
        || !string.IsNullOrWhiteSpace(CritDirigeant) || !string.IsNullOrWhiteSpace(CritActivite)
        || !string.IsNullOrWhiteSpace(CritNumVoie) || !string.IsNullOrWhiteSpace(CritAdresse)
        || !string.IsNullOrWhiteSpace(CritCodePostal) || !string.IsNullOrWhiteSpace(CritVille);

    /// <summary>Refus de _ValiderRaisonSociale : au-dela de 6 caracteres, une
    /// designation faite QUE de chiffres est rejetee.</summary>
    private bool RaisonSocialeInvalide =>
        CritRaisonSociale.Length > 6 && int.TryParse(CritRaisonSociale, out _);

    private string Compteur => Volet switch
    {
        "panier" => Panier.Count + " commande(s) au panier",
        "articles" => CommandeEnCours is null ? "aucune commande selectionnee"
                                              : "commande " + CommandeEnCours.NumCommande,
        _ => ARecherche
                ? Trouvees.Length + " ligne(s) / " + NbDossiers + " dossier"
                  + (NbDossiers > 1 ? "s" : "")
                : "recherche non lancee"
    };

    private int NbDossiers => Trouvees.Select(s => s.Gestion).Where(g => g.Length > 0).Distinct().Count();

    private static string MessageNonPorte(string quoi) =>
        quoi + " n est pas encore implementee dans la maquette des requisitions.";

    // L'ecoute de la touche Entree (SurToucheCritere du .razor) est dans le .tsx :
    // elle y lance le geste « Rechercher », qui appelle Rechercher() ci-dessous.

    private void Rechercher()
    {
        Message = string.Empty;
        Interrompue = false;

        if (RaisonSocialeInvalide)
        {
            Message = "Designation invalide : saisissez autre chose que des chiffres.";
            Trouvees = [];
            ARecherche = true;
            return;
        }
        if (!AuMoinsUnCritere)
        {
            Message = "Veuillez saisir un critere de recherche.";
            Trouvees = [];
            ARecherche = false;
            return;
        }

        // Lues en base (EcranRequisitionsBase.cs) : la base a deja applique les criteres qu'elle cherche.
        IEnumerable<EntrepriseRequi> q = FondsBase ?? JeuDemo.EntreprisesRequi;

        if (FondsBase is null)
        {
            if (Filtre(CritRaisonSociale)) q = q.Where(s => Contient(s.Designation, CritRaisonSociale));
            if (Filtre(CritSiren))
            {
                var t = CritSiren.Replace(" ", string.Empty);
                q = q.Where(s => s.Siren.Replace(" ", string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase));
            }
            if (Filtre(CritDirigeant)) q = q.Where(s => Contient(s.Dirigeant, CritDirigeant));
            if (Filtre(CritActivite)) q = q.Where(s => Contient(s.Activite, CritActivite));
            if (Filtre(CritNumVoie)) q = q.Where(s => Contient(s.Adresse, CritNumVoie));
            if (Filtre(CritAdresse)) q = q.Where(s => Contient(s.Adresse, CritAdresse));
            if (Filtre(CritCodePostal)) q = q.Where(s => Contient(s.CodePostal, CritCodePostal));
            if (Filtre(CritVille)) q = q.Where(s => Contient(s.Ville, CritVille));
        }

        // « Tous les etablissements » decoche : seul le siege remonte.
        if (!CritTousEtb) q = q.Where(s => s.TypeAdresse != "Etablissement");
        // « Inclure les supprimes » decoche : les radies sont ecartes.
        if (!CritSupprCompris) q = q.Where(s => !s.Radie);

        LimiteRetenue = Limite;
        var toutes = q.ToArray();
        Trouvees = toutes.Take(LimiteRetenue).ToArray();
        Interrompue = (FondsBase is null ? toutes.Length : TotalBase) > LimiteRetenue;
        ARecherche = true;
        Volet = "resultats";
    }

    private static bool Filtre(string v) => !string.IsNullOrWhiteSpace(v);

    private static bool Contient(string champ, string critere) =>
        champ.Contains(critere.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Infobulle de la grille des resultats : ce que _TooltipDesignation
    /// de OPE_REQUI_RESULTATS affiche (forme, dirigeant, procedure collective).</summary>
    private static string Infobulle(EntrepriseRequi s)
    {
        var lignes = new List<string> { s.Designation };
        if (s.Dirigeant.Length > 0) lignes.Add("Dirigeant : " + s.Dirigeant);
        if (s.Activite.Length > 0) lignes.Add("Activite : " + s.Activite);
        if (s.ProcedureCollective) lignes.Add("Procedure collective en cours");
        if (s.Radie) lignes.Add("Dossier radie");
        if (s.NonInscrit) lignes.Add("Non inscrit au registre");
        return string.Join("\n", lignes);
    }

    /// <summary>AjouterDepuisRecherche de OPE_REQUI_PANIER : on regroupe sous le
    /// MEME numero de commande les lignes de la meme entreprise a la meme adresse,
    /// sauf si « Valider » vient de demander une commande neuve.</summary>
    private void AjouterAuPanier(EntrepriseRequi societe)
    {
        Message = string.Empty;

        var existante = ForcerNouvelleCommande
            ? null
            : Panier.FirstOrDefault(c => c.Societe.Siren == societe.Siren
                                      && c.Societe.Adresse == societe.Adresse);

        if (existante is not null)
        {
            CommandeEnCours = existante;
        }
        else
        {
            // Fiche 275, « Nom de la demande » : quand la commande vient du web, le
            // nom du requi est prerempli avec « Site local ou Infogreffe - N de
            // commande et date ». En saisie manuelle, il reste vide.
            var cmd = new CommandeRequi
            {
                NumCommande = Panier.Count == 0 ? 1 : Panier.Max(c => c.NumCommande) + 1,
                Societe = societe,
                NomRequi = CommandeWebEnCours?.NomDemande ?? string.Empty
            };
            Panier.Add(cmd);
            CommandeEnCours = cmd;
            ForcerNouvelleCommande = false;
        }

        Volet = "articles";
    }

    /// <summary>Selection simple d une commande web : elle devient la commande
    /// courante, sans rien lancer.</summary>
    private void ChoisirCommandeWeb(CommandeWeb web)
    {
        CommandeWebEnCours = web;
        Message = string.Empty;
    }

    /// <summary>Rapprochement : les criteres sont remplis avec ce que le demandeur
    /// a saisi sur le web (siren s il existe, sinon la raison sociale) et la
    /// recherche part. C est le raccordement CMD_WEB -> ecran de la fiche 275.</summary>
    private void RapprocherCommandeWeb(CommandeWeb web)
    {
        CommandeWebEnCours = web;
        Effacer();

        if (!string.IsNullOrWhiteSpace(web.Siren)) CritSiren = web.Siren;
        else CritRaisonSociale = web.Societe;

        Rechercher();
        if (Trouvees.Length == 0 && string.IsNullOrEmpty(Message))
        {
            Message = "Commande " + web.Numero + " : aucun dossier ne correspond a "
                    + web.Societe + ". Cherchez a la main.";
        }
    }

    /// <summary>« Marquer traitee » : la commande web passe a l etat Traitee.
    /// TODO #275 - ce que « valider » declenche vraiment (accuse de reception EDI,
    /// edition des documents, facturation) n est PAS defini par la fiche : ici on
    /// ne change QUE l etat affiche.</summary>
    private void MarquerTraitee()
    {
        if (CommandeWebEnCours is null) return;
        CommandeWebEnCours.Etat = "Traitee";
        Message = "Commande " + CommandeWebEnCours.Numero + " marquee traitee. "
                + "L accuse EDI, l edition et la facturation ne sont pas portes (TODO #275).";
    }

    private void Selectionner(CommandeRequi cmd)
    {
        CommandeEnCours = cmd;
        Message = string.Empty;
    }

    /// <summary>Suppression d une ligne du panier : les commandes en aval sont
    /// renumerotees (RenumeroterCommandes de OPE_REQUI_PANIER).</summary>
    private void Supprimer(CommandeRequi cmd)
    {
        Panier.Remove(cmd);
        if (CommandeEnCours == cmd) CommandeEnCours = null;
        for (var i = 0; i < Panier.Count; i++) Panier[i].NumCommande = i + 1;
    }

    /// <summary>« Valider » : le panier est CONSERVE, seuls les criteres sont vides
    /// et le regroupement repart a zero (btnValider_Click de frmRechLET).</summary>
    private void Valider()
    {
        ForcerNouvelleCommande = true;
        Effacer();
        Message = "Criteres vides : la prochaine ligne ajoutee ouvrira une nouvelle commande.";
    }

    /// <summary>« Terminer » : la maquette montre l apercu des commandes a inserer.
    /// L impression et la facturation restent a porter (TODO #275, points 1 a 5 de
    /// OPE_REQUI_PANIER.Terminer).</summary>
    private void Terminer()
    {
        if (Panier.Count == 0)
        {
            Message = "Aucune commande n a ete trouvee.";
            Volet = "panier";
            return;
        }
        Message = "Apercu des commandes a inserer : "
                + string.Join(" | ", Panier.Select(c => "n" + c.NumCommande + " " + c.Societe.Designation
                                                        + " -> " + Resume(c)))
                + ". L impression et la facturation ne sont pas encore portees (TODO #275).";
        Volet = "panier";
    }

    private void Effacer()
    {
        CritRaisonSociale = string.Empty;
        CritSiren = string.Empty;
        CritDirigeant = string.Empty;
        CritActivite = string.Empty;
        CritNumVoie = string.Empty;
        CritAdresse = string.Empty;
        CritCodePostal = string.Empty;
        CritVille = string.Empty;
        CritPhonetique = CritEtendre = CritTousEtb = CritSupprCompris = CritGuichet = false;
        CritLimiteDossiers = 500;
        Trouvees = [];
        ARecherche = false;
        Interrompue = false;
    }

    private bool ToutCoche =>
        CommandeEnCours is not null
        && CommandeEnCours.Inscriptions.Count == JeuDemo.CasesInscription.Length;

    private void ToutCocher()
    {
        if (CommandeEnCours is null) return;
        if (ToutCoche)
        {
            CommandeEnCours.Inscriptions.Clear();
            CommandeEnCours.QteParInscription.Clear();
        }
        else
        {
            foreach (var c in JeuDemo.CasesInscription)
            {
                if (CommandeEnCours.Inscriptions.Add(c.Libelle)) CommandeEnCours.QteParInscription[c.Libelle] = 1;
            }
        }
        GestionQteInscr();
    }

    private void BasculerInscription(CaseInscription cas)
    {
        if (CommandeEnCours is null) return;
        if (CommandeEnCours.Inscriptions.Add(cas.Libelle))
        {
            CommandeEnCours.QteParInscription[cas.Libelle] = 1;
        }
        else
        {
            CommandeEnCours.Inscriptions.Remove(cas.Libelle);
            CommandeEnCours.QteParInscription.Remove(cas.Libelle);
        }
        GestionQteInscr();
    }

    /// <summary>Quantite demandee pour UN type d inscription (0 si la case n est pas cochee).</summary>
    private int QteInscription(string libelle) =>
        CommandeEnCours is not null && CommandeEnCours.QteParInscription.TryGetValue(libelle, out var n) ? n : 0;

    /// <summary>Saisie d une quantite pour un type d inscription : une valeur illisible
    /// ou negative vaut 0, et le total NbInscr est recalcule derriere.</summary>
    private void ChangerQteInscription(string libelle, string? saisie)
    {
        if (CommandeEnCours is null || !CommandeEnCours.Inscriptions.Contains(libelle)) return;
        CommandeEnCours.QteParInscription[libelle] = int.TryParse(saisie, out var v) && v > 0 ? v : 0;
        GestionQteInscr();
    }

    /// <summary>GestionQteInscr de frmRechLET : des qu une case d inscription STRICTE
    /// est cochee, la quantite passe a 1 si elle valait 0 ; plus aucune cochee, elle
    /// retombe a 0. Les cases non strictes (NJ, FA, ST, VNO, STP, GSD, Parts, CLR,
    /// Fluvial, Maritime, NAIJ, Z) ne comptent pas.</summary>
    private void GestionQteInscr()
    {
        if (CommandeEnCours is null) return;
        var auMoinsUne = JeuDemo.CasesInscription
            .Where(c => c.Stricte)
            .Any(c => CommandeEnCours.Inscriptions.Contains(c.Libelle));

        if (auMoinsUne)
        {
            // La quantite globale est desormais la SOMME des quantites par type ;
            // elle ne peut pas retomber a 0 tant qu une case stricte est cochee.
            var somme = JeuDemo.CasesInscription
                .Where(c => CommandeEnCours.Inscriptions.Contains(c.Libelle))
                .Sum(c => QteInscription(c.Libelle));
            CommandeEnCours.NbInscr = somme > 0 ? somme : 1;
        }
        else
        {
            CommandeEnCours.NbInscr = 0;
        }
    }

    /// <summary>Colonne « Articles commandes » du panier : ce qui a ete demande,
    /// en clair.</summary>
    private static string Resume(CommandeRequi c)
    {
        var parts = new List<string>();
        void Add(int n, string quoi) { if (n > 0) parts.Add(n + " " + quoi); }
        Add(c.NbKBis, "KBIS");
        Add(c.NbHistorique, "Histo");
        Add(c.NbDAS, "DAS");
        Add(c.NbCertifNonFaillite, "Certif. Non Faillite");
        Add(c.NbCertifNonRcs, "Certif. Non RCS");
        Add(c.NbCertifNonRsac, "Certif. Non RSAC");
        Add(c.NbCertifNonOppo, "Certif. Non Opposition");
        Add(c.NbDjCopie, "DJ Copie");
        Add(c.NbDjExpedition, "DJ Expedition");
        Add(c.NbNavire, "Navire");
        Add(c.NbBeInterne, "BE Interne");
        Add(c.NbBeExterne, "BE Externe");
        if (c.NbInscr > 0)
        {
            parts.Add(c.NbInscr + " inscription(s) : "
                      + string.Join(", ", JeuDemo.CasesInscription
                            .Where(x => c.Inscriptions.Contains(x.Libelle))
                            .Select(x => x.Libelle + " x"
                                        + (c.QteParInscription.TryGetValue(x.Libelle, out var q) ? q : 0))));
        }
        if (!string.IsNullOrWhiteSpace(c.NomRequi)) parts.Add("requi " + c.NomRequi);
        parts.Add("travail du " + c.DateTravail.ToString("dd/MM/yyyy")
                  + (c.TravailFrais ? "" : " (perime)"));
        return parts.Count == 0 ? "rien de demande" : string.Join(", ", parts);
    }

    // ================================================================ gestes et vue (pont React)

    /// <summary>
    /// Un geste de l'ecran React : le nom de la methode ou du champ que le .razor
    /// branchait sur l'element, et sa valeur. Un nom inconnu est une erreur franche
    /// (le .tsx et ce fichier ont diverge), jamais un geste ignore en silence.
    /// </summary>
    internal void Agir(string geste, JsonElement valeur)
    {
        switch (geste)
        {
            // ---- zone haute
            case "MasquerTraitees": MasquerTraitees = !MasquerTraitees; break;
            case "BasculerTri": BasculerTri(Texte(valeur)); break;
            case "ChoisirCommandeWeb": ChoisirCommandeWeb(CommandesWeb[valeur.GetInt32()]); break;
            case "RapprocherCommandeWeb": RapprocherCommandeWeb(CommandesWeb[valeur.GetInt32()]); break;
            case "MarquerTraitee": MarquerTraitee(); break;

            // ---- criteres
            case "Critere": PoserCritere(Texte(valeur, "champ"), Texte(valeur, "valeur")); break;
            case "BasculerCritere": BasculerCritere(Texte(valeur)); break;
            case "LimiteDossiers":
                // @bind d'un int : une saisie illisible laisse la valeur d'avant.
                if (int.TryParse(Texte(valeur), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limite))
                    CritLimiteDossiers = limite;
                break;
            case "Rechercher": Rechercher(); break;
            case "Effacer": Effacer(); break;
            case "NonPorte": Message = MessageNonPorte(Texte(valeur)); break;

            // ---- volets
            case "Volet": Volet = Texte(valeur); break;
            case "AjouterAuPanier": AjouterAuPanier(Trouvees[valeur.GetInt32()]); break;
            case "Selectionner": Selectionner(Panier[valeur.GetInt32()]); break;
            case "Supprimer": Supprimer(Panier[valeur.GetInt32()]); break;
            case "Valider": Valider(); break;
            case "Terminer": Terminer(); break;

            // ---- articles de la commande en cours
            case "Article": PoserArticle(Texte(valeur, "champ"), Texte(valeur, "valeur")); break;
            case "ToutCocher": ToutCocher(); break;
            case "BasculerInscription":
                BasculerInscription(JeuDemo.CasesInscription.Single(c => c.Libelle == Texte(valeur)));
                break;
            case "ChangerQteInscription":
                ChangerQteInscription(Texte(valeur, "libelle"), Texte(valeur, "saisie"));
                break;

            default: throw new ArgumentException($"Geste inconnu de l'ecran des requisitions : « {geste} ».", nameof(geste));
        }
    }

    private static string Texte(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
    private static string Texte(JsonElement v, string propriete) =>
        v.TryGetProperty(propriete, out var p) && p.ValueKind != JsonValueKind.Null ? Texte(p) : string.Empty;

    /// <summary>Les champs texte des criteres, @bind:event="oninput" dans le .razor.</summary>
    private void PoserCritere(string champ, string texte)
    {
        switch (champ)
        {
            case nameof(CritRaisonSociale): CritRaisonSociale = texte; break;
            case nameof(CritSiren): CritSiren = texte; break;
            case nameof(CritDirigeant): CritDirigeant = texte; break;
            case nameof(CritActivite): CritActivite = texte; break;
            case nameof(CritNumVoie): CritNumVoie = texte; break;
            case nameof(CritAdresse): CritAdresse = texte; break;
            case nameof(CritCodePostal): CritCodePostal = texte; break;
            case nameof(CritVille): CritVille = texte; break;
            default: throw new ArgumentException($"Critere inconnu : « {champ} ».", nameof(champ));
        }
    }

    /// <summary>Les cinq bascules des criteres (@onclick="() => X = !X" dans le .razor).</summary>
    private void BasculerCritere(string champ)
    {
        switch (champ)
        {
            case nameof(CritPhonetique): CritPhonetique = !CritPhonetique; break;
            case nameof(CritEtendre): CritEtendre = !CritEtendre; break;
            case nameof(CritTousEtb): CritTousEtb = !CritTousEtb; break;
            case nameof(CritSupprCompris): CritSupprCompris = !CritSupprCompris; break;
            case nameof(CritGuichet): CritGuichet = !CritGuichet; break;
            default: throw new ArgumentException($"Bascule inconnue : « {champ} ».", nameof(champ));
        }
    }

    /// <summary>
    /// Les champs de la commande en cours, @bind="CommandeEnCours.X" dans le .razor :
    /// meme conversion que le @bind de Blazor, une saisie illisible (nombre vide,
    /// date effacee) laisse la valeur d'avant.
    /// </summary>
    private void PoserArticle(string champ, string texte)
    {
        if (CommandeEnCours is not { } c) return;
        bool Entier(out int n) => int.TryParse(texte, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
        bool Date(out DateTime d) => DateTime.TryParseExact(texte, FormatDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        int n;
        DateTime d;
        switch (champ)
        {
            case nameof(c.NbKBis): if (Entier(out n)) c.NbKBis = n; break;
            case nameof(c.NbHistorique): if (Entier(out n)) c.NbHistorique = n; break;
            case nameof(c.NbDAS): if (Entier(out n)) c.NbDAS = n; break;
            case nameof(c.NbCertifNonFaillite): if (Entier(out n)) c.NbCertifNonFaillite = n; break;
            case nameof(c.NbCertifNonRcs): if (Entier(out n)) c.NbCertifNonRcs = n; break;
            case nameof(c.NbCertifNonRsac): if (Entier(out n)) c.NbCertifNonRsac = n; break;
            case nameof(c.NbCertifNonOppo): if (Entier(out n)) c.NbCertifNonOppo = n; break;
            case nameof(c.NbDjCopie): if (Entier(out n)) c.NbDjCopie = n; break;
            case nameof(c.NbDjExpedition): if (Entier(out n)) c.NbDjExpedition = n; break;
            case nameof(c.NbNavire): if (Entier(out n)) c.NbNavire = n; break;
            case nameof(c.NbBeInterne): if (Entier(out n)) c.NbBeInterne = n; break;
            case nameof(c.NbBeExterne): if (Entier(out n)) c.NbBeExterne = n; break;
            // Champ en lecture seule a l'ecran (le total) : lie comme dans le .razor.
            case nameof(c.NbInscr): if (Entier(out n)) c.NbInscr = n; break;
            case nameof(c.DateCnf): if (Date(out d)) c.DateCnf = d; break;
            case nameof(c.DateCnRcs): if (Date(out d)) c.DateCnRcs = d; break;
            case nameof(c.DateCnRsac): if (Date(out d)) c.DateCnRsac = d; break;
            case nameof(c.DateCnOppo): if (Date(out d)) c.DateCnOppo = d; break;
            case nameof(c.DateTravail): if (Date(out d)) c.DateTravail = d; break;
            case nameof(c.NomSci): c.NomSci = texte; break;
            case nameof(c.NomRequi): c.NomRequi = texte; break;
            default: throw new ArgumentException($"Article inconnu : « {champ} ».", nameof(champ));
        }
    }

    /// <summary>Le format d'un &lt;input type="date"&gt;, celui du @bind de Blazor.</summary>
    private const string FormatDate = "yyyy-MM-dd";

    private static string EnDate(DateTime d) => d.ToString(FormatDate, CultureInfo.InvariantCulture);

    /// <summary>Ce que le balisage du .razor lisait, mis a plat pour React.</summary>
    internal VueRequisitions Vue(string session)
    {
        var commandesWeb = !ZoneCommandesWeb ? [] : CommandesWebVisibles.Select(web => new LigneCommandeWebRequi(
            Array.IndexOf(CommandesWeb, web),
            web == CommandeWebEnCours ? "rig-table__ligne--alerte" : (web.Etat == "Traitee" ? "rig-table__ligne--inerte" : ""),
            web.Origine, web.Numero, web.Recue.ToString("dd/MM/yyyy HH:mm"), web.Demandeur, web.Famille,
            web.CodeEdi, web.Societe, web.Siren, web.Etat)).ToArray();

        var trouvees = Trouvees.Select((s, i) => new LigneEntrepriseRequi(
            i, s.Radie, Infobulle(s), s.Id, s.Siren, s.Gestion, s.Designation, s.TypeAdresse, s.Adresse,
            s.Type, s.NonInscrit, s.NbInscriptions)).ToArray();

        var panier = Panier.Select((c, i) => new LignePanierRequi(
            i, c == CommandeEnCours, c.NumCommande, c.Societe.Siren, c.Societe.Gestion, c.Societe.Designation,
            c.Societe.Adresse, Resume(c))).ToArray();

        ArticlesRequi? articles = null;
        if (CommandeEnCours is { } a)
        {
            var cases = JeuDemo.CasesInscription.Select(cas => new CaseArticleRequi(
                cas.Libelle,
                string.IsNullOrEmpty(cas.Codes) ? cas.Libelle : "Types d inscription : " + cas.Codes,
                a.Inscriptions.Contains(cas.Libelle),
                QteInscription(cas.Libelle))).ToArray();
            articles = new ArticlesRequi(a.NumCommande, a.Societe.Designation,
                a.KBisPossible, a.NbKBis, a.NbHistorique, a.NbDAS,
                a.CnfPossible, a.NbCertifNonFaillite, EnDate(a.DateCnf),
                a.NbCertifNonRcs, EnDate(a.DateCnRcs), a.NbCertifNonRsac, EnDate(a.DateCnRsac),
                a.NbCertifNonOppo, EnDate(a.DateCnOppo),
                a.NbDjCopie, a.NbDjExpedition, a.NbNavire, a.NbBeInterne, a.NbBeExterne, a.NomSci,
                a.NomRequi, EnDate(a.DateTravail), a.TravailFrais, a.AgeTravailJours, CommandeRequi.JoursFraicheur,
                ToutCoche, cases, a.NbInscr);
        }

        return new VueRequisitions(session,
            ZoneCommandesWeb ? NbATraiter : 0, MasquerTraitees, CleTri, TriAscendant, commandesWeb, CommandeWebEnCours is not null,
            new CriteresRequi(CritRaisonSociale, CritSiren, CritDirigeant, CritActivite, CritNumVoie, CritAdresse,
                              CritCodePostal, CritVille, CritPhonetique, CritEtendre, CritTousEtb, CritSupprCompris,
                              CritGuichet, CritLimiteDossiers),
            RaisonSocialeInvalide, Volet, Panier.Count, Compteur, Message,
            trouvees, ARecherche, Interrompue, LimiteRetenue, panier, articles, ModeResultats, MessageResultats, ZoneCommandesWeb);
    }
}

/// <summary>
/// « Terminer » un panier LU EN BASE (lot 7 du plan « RIG v3 complet, mesuré par le banc du parcours complet », 28/11/2026) : le
/// serveur ecrit la commande de RIG v2 sur sa branche sans facture remise au guichet (Serveur/Socle/EcransMain/EcritureRequisitions.cs,
/// ses sources et ce qu'il ne reprend pas), le pont la relit (LireCommandeAsync). La V3 ne facture pas : la case « Non facturable » de
/// la fenetre d'impression de RIG v2 (frmGestionImprimante.cs:398-400, ouverte a toute commande) vaut d'office ; la case « Guichet »
/// est celle des criteres. Les commandes de la demonstration gardent l'apercu de Terminer().
/// </summary>
public sealed partial class SessionRequisitions
{
    /// <summary>Le geste du serveur pour le panier, quand ses commandes viennent de la base ; null sinon (Terminer() de la session).</summary>
    internal GesteEcran? TerminerEnBase()
    {
        if (ModeResultats != "serveur" || Panier.Count == 0
            || Panier.Any(c => !int.TryParse(c.Societe.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            return null;
        static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        var valeurs = new Dictionary<string, string> { [RoutesTerminerRequisitions.ChampGuichet] = CritGuichet ? "1" : "0" };
        foreach (var c in Panier)
        {
            void Poser(string champ, string valeur) => valeurs[RoutesTerminerRequisitions.Cle(c.NumCommande, champ)] = valeur;
            Poser(RoutesTerminerRequisitions.Dossier, c.Societe.Id);
            Poser(RoutesTerminerRequisitions.Kbis, N(c.NbKBis));
            Poser(RoutesTerminerRequisitions.Historique, N(c.NbHistorique));
            Poser(RoutesTerminerRequisitions.Das, N(c.NbDAS));
            Poser(RoutesTerminerRequisitions.CertifNonFaillite, N(c.NbCertifNonFaillite));
            Poser(RoutesTerminerRequisitions.DateCertifNonFaillite, c.DateCnf.ToString(FormatDate, CultureInfo.InvariantCulture));
            Poser(RoutesTerminerRequisitions.CertifNonRcs, N(c.NbCertifNonRcs));
            Poser(RoutesTerminerRequisitions.CertifNonRsac, N(c.NbCertifNonRsac));
            Poser(RoutesTerminerRequisitions.DateCertifNonRsac, c.DateCnRsac.ToString(FormatDate, CultureInfo.InvariantCulture));
            Poser(RoutesTerminerRequisitions.Inscriptions, N(c.Inscriptions.Count > 0 ? c.NbInscr : 0));
            Poser(RoutesTerminerRequisitions.AutresDocuments,
                  N(c.NbCertifNonOppo + c.NbDjCopie + c.NbDjExpedition + c.NbNavire + c.NbBeInterne + c.NbBeExterne));
        }
        return new GesteEcran(TypeGeste.Creer, [], valeurs) { Nom = "Terminer" };
    }

    /// <summary>
    /// Apres « Terminer » par le serveur : refuse, son message (le panier reste) ; fait, la commande RELUE en base est dite, et l'ecran
    /// repart sur une commande neuve, comme la reponse « Oui » de RIG v2 a « Souhaitez-vous faire une autre commande ? » (RAZ,
    /// frmREQUI.cs:2393-2399).
    /// </summary>
    internal void ApresTerminer(ResultatGeste r, LectureEcranMain<CommandeRequiLue>? relue)
    {
        Volet = "panier";
        if (!r.Fait)
        {
            Message = r.Message;
            return;
        }
        Panier.Clear();
        CommandeEnCours = null;
        ForcerNouvelleCommande = false;
        Effacer();
        Message = r.Message + " " + (relue?.Valeur is { Requi: { } requi } c
            ? $"Relue en base : demande {requi.Numero} « {requi.Nom} » {(requi.Etat == "V" ? "validée" : $"à l'état {requi.Etat}")}, "
              + $"commande {string.Join(", ", c.Lignes.Select(l => $"{l.Type} x{l.Quantite}"))}"
              + string.Concat(c.Demandes.Select(d => $" ; {d.Numero} « {d.Nom} » : {string.Join(", ", d.Editions.Select(e => $"{e.Modele} x{e.Nombre}"))}"))
              + "."
            : $"La commande n'a pas pu être relue : {relue?.Message ?? "aucun numéro de demande rendu"}.");
    }
}
