using System.Globalization;
using System.Text;

namespace RigV3Desktop;

// =============================================================================
// TRAITEMENT DES COMMANDES WEB — l'ecran de NeoRIG porte dans la maquette.
//
// Source : D:\RIG-V3 (NeoRIG), lu le 18/11/2026 sans y toucher :
//   src/RIG.Presentation.ViewModels/ViewModels/CommandesWeb/*.cs  (mise en page)
//   src/RIG.Presentation.ViewModels/Resources/Rcs/CommandesWeb/Libelles.resx
//   src/RIG.Domain/Rcs/CommandesWeb/*.cs                           (regles)
// Les libelles et les regles ci-dessous en sont recopies ; seules les DONNEES
// sont un jeu d'exemple, pris dans le meme fonds que les requisitions
// (JeuDemo.EntreprisesRequi, JeuDemo.CommandesWeb) pour ne pas avoir deux
// verites. Aucune base n'est branchee.
//
// Ce n'est PAS le meme ecran que PROC_REQUI : la V2 traitait les commandes web
// depuis les requisitions (fiche 275) ; NeoRIG leur donne un ecran a elles.
// =============================================================================

/// <summary>Le type de la commande (FamilleCommandeWeb de NeoRIG).</summary>
public enum FamilleCommande
{
    EtatEndettement,
    EtatEndettementNonInscrit,
    EtatEndettementRequisition,
    Faillite,
    NonOpposition,
}

/// <summary>L'etat de la commande dans la table de reception (EtatCommandeWeb).</summary>
public enum EtatCommande { ATraiter, EnAttente, Validee, Rejetee, SaisieManuelle }

/// <summary>D'ou vient la commande ; « Guichet » = saisie manuelle.</summary>
public enum OrigineCommande { SiteLocal, Infogreffe, Guichet }

/// <summary>Les trois decisions du pied de l'ecran.</summary>
public enum ActionCommande { Valider, MettreEnAttente, Rejeter }

/// <summary>Sous-types du certificat de non-opposition (SousTypeNonOpposition).</summary>
public enum SousTypeCno { ReductionCapital, TransmissionUniverselle, FusionScission, InjonctionDePayer }

/// <summary>Les certificats de la famille faillite.</summary>
public enum CertificatFaillite { NonFaillite, NonRcs, NonRsac }

/// <summary>Issue du controle de delivrance d'un certificat de non-opposition.</summary>
public enum IssueDelivrance { Delivrance, Reserve, Rejet }

/// <summary>
/// Une commande recue, avec ce que le greffier y a deja fait (etat, motif,
/// dossier retenu, adresse corrigee). Mutable : la maquette traite en memoire.
/// </summary>
public sealed class CommandeRecue
{
    public int Id { get; init; }
    /// <summary>N° de commande. Plusieurs commandes de meme numero forment une ENVELOPPE.</summary>
    public string Reference { get; set; } = string.Empty;
    public DateTime Date { get; init; }
    public FamilleCommande Famille { get; set; }
    public OrigineCommande Origine { get; init; }
    public EtatCommande Etat { get; set; }
    public string Denomination { get; set; } = string.Empty;
    public string Siren { get; set; } = string.Empty;
    public string Requerant { get; set; } = string.Empty;
    public string NumeroGestion { get; init; } = string.Empty;
    public string NumeroAffaire { get; init; } = string.Empty;
    public string ReferenceClient { get; init; } = string.Empty;
    public string[] AdresseLivraison { get; init; } = [];
    public string Email { get; init; } = string.Empty;
    public SousTypeCno? SousType { get; init; }
    /// <summary>Codes des cases d'inscription commandees (endettement).</summary>
    public string[] TypesRecus { get; init; } = [];

    /// <summary>Motif de la mise en attente ou du rejet.</summary>
    public string Motif { get; set; } = string.Empty;
    /// <summary>Cle de l'entite retenue au rapprochement (EntiteRapprochee.Cle), vide sinon.</summary>
    public string CibleRetenue { get; set; } = string.Empty;
    /// <summary>Adresse corrigee par le greffier ; vide = l'adresse recue fait foi.</summary>
    public string[] AdresseCorrigee { get; set; } = [];
    /// <summary>Les documents edites a la validation (« Ce qui a ete envoye »).</summary>
    public List<DocumentEdite> Envoyes { get; } = [];
    /// <summary>Validee mais pas encore partie : l'enveloppe attend ses autres commandes.</summary>
    public bool EnvoiGroupeEnAttente { get; set; }

    public bool EstTraitee => Etat is EtatCommande.Validee or EtatCommande.Rejetee;
    public string[] AdresseEnVigueur => AdresseCorrigee.Length > 0 ? AdresseCorrigee : AdresseLivraison;
}

/// <summary>Une adresse d'une entite trouvee : une ligne du tableau de rapprochement.</summary>
public sealed record LigneEntite(string Nature, string Adresse, int NbInscriptions);

/// <summary>
/// Une entite trouvee par le rapprochement : un dossier RCS (un n° de gestion)
/// ou un non-inscrit, avec toutes ses adresses connues.
/// </summary>
public sealed record EntiteRapprochee(string Cle, string Gestion, string Designation, string Siren,
                                      string Type, bool NonInscrit, bool Radie, bool ProcedureCollective,
                                      IReadOnlyList<LigneEntite> Lignes)
{
    public int NbInscriptions => Lignes.Sum(l => l.NbInscriptions);
    public bool ASiege => Lignes.Any(l => l.Nature == TraitementCommandes.NatureSiege);
}

/// <summary>Resultat d'une recherche de rapprochement : les entites et le message qui l'accompagne.</summary>
public sealed record ResultatRapprochement(IReadOnlyList<EntiteRapprochee> Entites, string? Message);

/// <summary>Issue du controle de delivrance (DelivranceNonOpposition.Evaluer).</summary>
public sealed record ControleDelivrance(IssueDelivrance Issue, DateOnly? DerniereFormalite, int? Jours,
                                        string? Motif, int Seuil);

/// <summary>Un document que la validation editera (aperçu « Edition &amp; impression »).</summary>
public sealed record DocumentEdite(string Document, string Bac, int? Exemplaires, string Origine);

/// <summary>Les criteres de la carte de recherche ; une valeur vide ne filtre pas.</summary>
public sealed record CriteresCommandes(FamilleCommande? Famille, IReadOnlySet<EtatCommande> Etats,
                                       DateTime? Du, DateTime? Au, string Siren, string Reference,
                                       bool EnAttenteEnvoi);

/// <summary>Un groupe de la liste de gauche : une famille, sa couleur (rang), ses commandes.</summary>
public sealed record GroupeCommandes(FamilleCommande Famille, string Titre, int Rang,
                                     IReadOnlyList<CommandeRecue> Commandes);

/// <summary>Une case de type d'inscription (CasesTypeInscription), simple ou regroupement.</summary>
public sealed record CaseType(string Code, string Libelle, IReadOnlyList<CaseType> Enfants)
{
    public bool EstRegroupement => Enfants.Count > 0;
}

/// <summary>
/// Les REGLES de l'ecran, en logique pure : rejouees par tests/CommandesWebCheck
/// sans fenetre ni navigateur.
/// </summary>
public static class TraitementCommandes
{
    public const string CodeEcran = "PROC_COMMANDES_WEB";

    /// <summary>TransitionsCommandeWeb.LongueurMaxMotif : un motif plus long est refuse, jamais tronque.</summary>
    public const int LongueurMaxMotif = 500;

    /// <summary>RegleDelivranceNonOpposition.SeuilJoursParDefaut.</summary>
    public const int SeuilNonOppositionJours = 31;

    /// <summary>La plieuse n'imprime que 6 lignes de 34 caracteres (CoordonneesDestinataire).</summary>
    public const int LignesMaxAdresse = 6;
    public const int CaracteresMaxLigne = 34;

    public const string NatureSiege = "SIÈGE";
    public const string NatureEtablissement = "ÉTABL.";
    public const string NatureDebiteur = "DÉBITEUR";

    /// <summary>La date de travail du greffe : celle de tout le jeu d'exemple.</summary>
    public static DateOnly DateTravail => DateOnly.FromDateTime(JeuDemo.Aujourdhui);

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ------------------------------------------------------------------ libelles

    public static string Libelle(FamilleCommande f) => f switch
    {
        FamilleCommande.EtatEndettement => "État d'endettement",
        FamilleCommande.EtatEndettementNonInscrit => "État d'endettement — non-inscrit",
        FamilleCommande.EtatEndettementRequisition => "État d'endettement — réquisition",
        FamilleCommande.Faillite => "Certificat de non-faillite",
        FamilleCommande.NonOpposition => "Non-opposition",
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    public static string Libelle(EtatCommande e) => e switch
    {
        EtatCommande.ATraiter => "À traiter",
        EtatCommande.EnAttente => "Mise en attente",
        EtatCommande.Validee => "Validée",
        EtatCommande.Rejetee => "Rejetée",
        EtatCommande.SaisieManuelle => "Saisie manuelle",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    public static string Libelle(OrigineCommande o) => o switch
    {
        OrigineCommande.SiteLocal => "Site local",
        OrigineCommande.Infogreffe => "Infogreffe",
        OrigineCommande.Guichet => "Guichet",
        _ => throw new ArgumentOutOfRangeException(nameof(o)),
    };

    public static string Libelle(SousTypeCno s) => s switch
    {
        SousTypeCno.ReductionCapital => "CNO-RC — Réduction de capital non motivée par les pertes",
        SousTypeCno.TransmissionUniverselle => "CNO-TUP BODACC — Dissolution par transmission universelle de patrimoine",
        SousTypeCno.FusionScission => "CNO-FS — Fusion ou scission",
        SousTypeCno.InjonctionDePayer => "CNO-IP — Injonction de payer — IP suivi",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static string Libelle(CertificatFaillite c) => c switch
    {
        CertificatFaillite.NonFaillite => "Certificat de non faillite",
        CertificatFaillite.NonRcs => "Certificat de non RCS",
        CertificatFaillite.NonRsac => "Certificat de non RSAC",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    /// <summary>Variante d'icone de l'etat, que l'ecran traduit en pastille de couleur (LibellesCommandeWeb.IconeEtat).</summary>
    public static string IconeEtat(EtatCommande e) => e switch
    {
        EtatCommande.ATraiter => "a-traiter",
        EtatCommande.EnAttente => "en-attente",
        EtatCommande.Validee => "validee",
        EtatCommande.Rejetee => "rejetee",
        EtatCommande.SaisieManuelle => "saisie-manuelle",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    /// <summary>SIREN groupe par trois chiffres : « 324 862 614 ».</summary>
    public static string FormaterSiren(string? siren)
    {
        var brut = Chiffres(siren);
        return brut.Length == 9 ? $"{brut[..3]} {brut[3..6]} {brut[6..]}" : (siren ?? string.Empty).Trim();
    }

    /// <summary>« Type · N° IG-90812 » : le titre de l'en-tete de commande (EnTete_Titre).</summary>
    public static string Titre(CommandeRecue c) => $"{Libelle(c.Famille)} · N° {c.Reference}";

    /// <summary>« 07/09/2026 · Infogreffe » : le sous-titre (EnTete_SousTitre).</summary>
    public static string SousTitre(CommandeRecue c) => $"{c.Date.ToString("dd/MM/yyyy", Fr)} · {Libelle(c.Origine)}";

    /// <summary>« IG-90812 · 07/09 · 512 004 331 » : la seconde ligne d'une commande de la liste.</summary>
    public static string Precisions(CommandeRecue c)
    {
        var morceaux = new List<string> { c.Reference, c.Date.ToString("dd/MM", Fr) };
        if (!string.IsNullOrWhiteSpace(c.Siren)) morceaux.Add(FormaterSiren(c.Siren));
        return string.Join(" · ", morceaux);
    }

    // ------------------------------------------------------------ liste et filtre

    /// <summary>Les criteres a l'ouverture : seules les commandes « À traiter ».</summary>
    public static CriteresCommandes CriteresParDefaut() =>
        new(null, new HashSet<EtatCommande> { EtatCommande.ATraiter }, null, null, string.Empty, string.Empty, false);

    /// <summary>Les commandes qui repondent aux criteres de la carte de recherche.</summary>
    public static List<CommandeRecue> Filtrer(IEnumerable<CommandeRecue> commandes, CriteresCommandes c)
    {
        var siren = Chiffres(c.Siren);
        var reference = (c.Reference ?? string.Empty).Trim();
        return commandes.Where(x =>
                (c.Famille is null || x.Famille == c.Famille)
                && (c.Etats.Count == 0 || c.Etats.Contains(x.Etat))
                && (c.Du is null || x.Date.Date >= c.Du.Value.Date)
                && (c.Au is null || x.Date.Date <= c.Au.Value.Date)
                && (siren.Length == 0 || Chiffres(x.Siren).Contains(siren, StringComparison.Ordinal))
                && (reference.Length == 0 || x.Reference.Contains(reference, StringComparison.OrdinalIgnoreCase))
                && (!c.EnAttenteEnvoi || x.EnvoiGroupeEnAttente))
            .ToList();
    }

    /// <summary>Les criteres en une ligne, separes par « · » : l'infobulle de la loupe (ResumerRecherche).</summary>
    public static string Resumer(CriteresCommandes c)
    {
        var pastilles = new List<string>
        {
            c.Famille is { } f ? Libelle(f) : "Toutes",
            c.Etats.Count == 0 ? "Toutes" : string.Join(", ", c.Etats.OrderBy(e => e).Select(Libelle)),
        };
        if (c.Du is not null || c.Au is not null)
            pastilles.Add($"{c.Du?.ToString("dd/MM", Fr)} → {c.Au?.ToString("dd/MM", Fr)}");
        if (!string.IsNullOrWhiteSpace(c.Siren)) pastilles.Add(c.Siren.Trim());
        if (!string.IsNullOrWhiteSpace(c.Reference)) pastilles.Add(c.Reference.Trim());
        return string.Join(" · ", pastilles);
    }

    /// <summary>Les variantes d'endettement se rangent sous l'etat d'endettement (FamilleCommandeWebRegle.Groupe).</summary>
    public static FamilleCommande Groupe(FamilleCommande f) =>
        f is FamilleCommande.EtatEndettementNonInscrit or FamilleCommande.EtatEndettementRequisition
            ? FamilleCommande.EtatEndettement
            : f;

    /// <summary>Endettement, puis non-faillite, puis non-opposition (OrdreGroupe) ; le rang porte la couleur.</summary>
    public static int RangGroupe(FamilleCommande groupe) => groupe switch
    {
        FamilleCommande.EtatEndettement => 1,
        FamilleCommande.Faillite => 4,
        FamilleCommande.NonOpposition => 5,
        _ => 6,
    };

    /// <summary>
    /// Groupe par famille ; dans un groupe, la plus ancienne d'abord, et, case
    /// « Rapprochements directs en tete » cochee, les rapprochements directs devant.
    /// </summary>
    public static List<GroupeCommandes> Regrouper(IEnumerable<CommandeRecue> commandes, bool directsEnTete)
    {
        return commandes
            .GroupBy(c => Groupe(c.Famille))
            .OrderBy(g => RangGroupe(g.Key))
            .Select(g => new GroupeCommandes(g.Key, Libelle(g.Key), RangGroupe(g.Key),
                g.OrderByDescending(c => directsEnTete && EstRapprochementDirect(c))
                 .ThenBy(c => c.Date)
                 .ToList()))
            .ToList();
    }

    /// <summary>« 8 commande(s) · 3 groupe(s) » (Liste_Compteur).</summary>
    public static string Compteur(IReadOnlyCollection<GroupeCommandes> groupes) =>
        $"{groupes.Sum(g => g.Commandes.Count)} commande(s) · {groupes.Count} groupe(s)";

    /// <summary>
    /// La commande a ouvrir apres un traitement : la suivante non traitee dans
    /// l'ordre d'affichage. Nulle quand c'etait la derniere de la liste.
    /// </summary>
    public static CommandeRecue? Suivante(IReadOnlyList<CommandeRecue> ordre, CommandeRecue courante)
    {
        var i = -1;
        for (var k = 0; k < ordre.Count; k++) if (ordre[k].Id == courante.Id) { i = k; break; }
        if (i < 0) return null;
        for (var j = i + 1; j < ordre.Count; j++)
            if (!ordre[j].EstTraitee) return ordre[j];
        return null;
    }

    // ------------------------------------------------------------ rapprochement

    /// <summary>
    /// Le fonds interroge, en ENTITES : les lignes de la recherche de requisition
    /// regroupees par dossier (meme n° de gestion), un non-inscrit restant seul.
    /// </summary>
    public static IReadOnlyList<EntiteRapprochee> Entites { get; } = ConstruireEntites();

    private static List<EntiteRapprochee> ConstruireEntites() =>
        JeuDemo.EntreprisesRequi
            .GroupBy(e => e.NonInscrit || e.Gestion.Length == 0 ? "NI-" + e.Id : e.Gestion + "|" + e.Designation)
            .Select(g =>
            {
                var t = g.First();
                return new EntiteRapprochee(g.Key, t.Gestion, t.Designation, t.Siren, t.Type, t.NonInscrit,
                    g.Any(x => x.Radie), g.Any(x => x.ProcedureCollective),
                    g.Select(x => new LigneEntite(Nature(x), x.Adresse, x.NbInscriptions)).ToList());
            })
            .ToList();

    private static string Nature(EntrepriseRequi e) => e.TypeAdresse switch
    {
        "Siege" => NatureSiege,
        "Etablissement" => NatureEtablissement,
        _ => NatureDebiteur,
    };

    public static EntiteRapprochee? Entite(string? cle) =>
        string.IsNullOrEmpty(cle) ? null : Entites.FirstOrDefault(e => e.Cle == cle);

    /// <summary>
    /// La recherche du rapprochement : le SIREN d'abord, la raison sociale
    /// ensuite. Messages de NeoRIG (Rapprochement_*).
    /// </summary>
    public static ResultatRapprochement Rechercher(string? siren, string? raisonSociale)
    {
        var brut = (siren ?? string.Empty).Trim();
        var chiffres = Chiffres(brut);
        string? message = null;

        if (brut.Length > 0 && (chiffres.Length != brut.Replace(" ", "").Length || chiffres.Length > 9))
        {
            message = $"« {brut} » n'est pas un SIREN (9 chiffres au plus) : il n'est pas utilisé.";
        }
        else if (chiffres.Length > 0)
        {
            var parSiren = Entites.Where(e => Chiffres(e.Siren) == chiffres).ToList();
            if (parSiren.Count > 0) return new(parSiren, null);
            message = $"Aucun dossier pour le SIREN {FormaterSiren(chiffres)} : recherche par raison sociale.";
        }

        var mots = MotsSignificatifs(raisonSociale);
        if (mots.Count == 0)
            return new([], message ?? "Saisissez un SIREN, une raison sociale ou une adresse (voie ou ville), puis lancez la recherche.");

        // Chaque mot cherche doit commencer un mot de la designation : « ERIC »
        // trouve « MARTIN Eric (EURL) », pas « AMERICA ».
        var parNom = Entites.Where(e =>
        {
            var designation = Normaliser(e.Designation).Split(' ');
            return mots.All(m => designation.Any(d => d.StartsWith(m, StringComparison.Ordinal)));
        }).ToList();

        if (parNom.Count == 0)
            message = "Aucun dossier RCS ni non-inscrit ne correspond. La création du non-inscrit se fait dans RIG V2.";
        return new(parNom, message);
    }

    /// <summary>La recherche qu'ouvre la commande : son SIREN et sa raison sociale.</summary>
    public static ResultatRapprochement RechercherPour(CommandeRecue c) => Rechercher(c.Siren, c.Denomination);

    /// <summary>Un seul dossier correspond : la pastille verte « Rapprochement direct ».</summary>
    public static bool EstRapprochementDirect(CommandeRecue c) =>
        !c.EstTraitee && RechercherPour(c).Entites.Count == 1;

    private static int OrdreNature(string nature) => nature switch
    {
        NatureSiege => 1,
        NatureDebiteur => 2,
        _ => 3,
    };

    /// <summary>
    /// Les lignes d'une entite que montrent les cases « Tous les débiteurs » et
    /// « Tous les établissements » (NeoRIG, Documentation/rcs-rapprochement-
    /// filtres-debiteurs-etablissements.md) : le siege toujours (R4), les
    /// debiteurs et les etablissements a la demande (R2, R3), et ceux d'un
    /// dossier sans siege d'office (R3 bis). Siege, puis debiteurs, puis
    /// etablissements.
    /// </summary>
    public static List<LigneEntite> LignesVisibles(EntiteRapprochee e, bool tousDebiteurs, bool tousEtablissements)
    {
        var etablissementsDOffice = !e.ASiege;
        return e.Lignes
            .Where(l => l.Nature == NatureSiege
                     || (l.Nature == NatureDebiteur && tousDebiteurs)
                     || (l.Nature == NatureEtablissement && (tousEtablissements || etablissementsDOffice)))
            .OrderBy(l => OrdreNature(l.Nature))
            .ToList();
    }

    /// <summary>« 3 ligne(s) · 2 PM · 1 NI » (Rapprochement_Resume) : les lignes AFFICHEES, filtres appliques (R7).</summary>
    public static string ResumeRapprochement(IReadOnlyList<EntiteRapprochee> entites, int lignesAffichees) =>
        $"{lignesAffichees} ligne(s) · {entites.Count(e => !e.NonInscrit)} PM · {entites.Count(e => e.NonInscrit)} NI";

    /// <summary>« SIREN 512 004 331 · gestion 004176 · 1 adresse(s) · 6 inscription(s) » (Entite_Resume).</summary>
    public static string ResumeEntite(EntiteRapprochee e) =>
        $"SIREN {(e.Siren.Length > 0 ? FormaterSiren(e.Siren) : "—")} · {(e.NonInscrit ? "NI" : "gestion")} "
        + $"{(e.Gestion.Length > 0 ? e.Gestion : "—")} · {e.Lignes.Count} adresse(s) · {e.NbInscriptions} inscription(s)";

    /// <summary>
    /// Ce qui ne correspond pas entre la commande et l'entite : « Raison sociale
    /// reçu : X ≠ Y » (Alerte_Ecart). Rien quand tout concorde.
    /// </summary>
    public static List<string> Ecarts(CommandeRecue c, EntiteRapprochee e)
    {
        var ecarts = new List<string>();
        if (c.Denomination.Length > 0 && Normaliser(c.Denomination) != Normaliser(e.Designation))
            ecarts.Add($"Raison sociale reçu : {c.Denomination} ≠ {e.Designation}");
        var sirenRecu = Chiffres(c.Siren);
        if (sirenRecu.Length > 0 && Chiffres(e.Siren).Length > 0 && sirenRecu != Chiffres(e.Siren))
            ecarts.Add($"SIREN reçu : {FormaterSiren(c.Siren)} ≠ {FormaterSiren(e.Siren)}");
        return ecarts;
    }

    // ---------------------------------------------------- controles de delivrance

    /// <summary>Dernier passage CFE connu par n° de gestion (jeu d'exemple).</summary>
    private static readonly Dictionary<string, DateOnly> DernieresFormalites = new(StringComparer.Ordinal)
    {
        ["2026B01067"] = new(2026, 9, 8),
        ["004176"] = new(2026, 5, 2),
        ["004151"] = new(2026, 3, 10),
        ["004168"] = new(2026, 7, 15),
        ["004179"] = new(2025, 11, 4),
        ["004144"] = new(2026, 8, 25),
        ["004188"] = new(2026, 6, 30),
    };

    public static DateOnly? DerniereFormalite(EntiteRapprochee? e) =>
        e is not null && DernieresFormalites.TryGetValue(e.Gestion, out var d) ? d : null;

    /// <summary>
    /// Le controle de delivrance d'un certificat de non-opposition, dans l'ordre
    /// de DelivranceNonOpposition.Evaluer : refus d'abord, reserves ensuite.
    /// </summary>
    public static ControleDelivrance ControlerNonOpposition(DateOnly dateTravail, DateOnly? dateCommande,
        string? siren, string? numeroAffaire, bool entrepriseTrouvee, DateOnly? derniereFormalite,
        int seuil = SeuilNonOppositionJours, bool cibleRetenue = false)
    {
        if (!cibleRetenue && string.IsNullOrWhiteSpace(siren) && string.IsNullOrWhiteSpace(numeroAffaire))
            return new(IssueDelivrance.Rejet, null, null,
                "Certificat non délivré : la commande ne porte ni SIREN ni n° d'affaire.", seuil);

        int? jours = derniereFormalite is { } f ? dateTravail.DayNumber - f.DayNumber : null;

        if (dateCommande is { } dc && dc > dateTravail)
            return new(IssueDelivrance.Rejet, derniereFormalite, jours,
                "Certificat non délivré : la date de la commande est postérieure à la date de travail.", seuil);

        if (!entrepriseTrouvee)
            return new(IssueDelivrance.Rejet, derniereFormalite, jours, cibleRetenue
                ? "Certificat non délivré : le dossier retenu ne porte aucune entreprise. Choisissez un autre dossier."
                : string.IsNullOrWhiteSpace(siren)
                    ? "Certificat non délivré : la commande ne porte aucun SIREN, et l'entreprise ne peut pas être retrouvée autrement."
                    : $"Certificat non délivré : aucune entreprise ne correspond au SIREN {siren.Trim()} de la commande.", seuil);

        if (jours is not { } j)
            return new(IssueDelivrance.Reserve, null, null,
                "Aucune formalité n'est enregistrée sur cette entreprise : le délai d'opposition ne peut pas être vérifié.", seuil);

        if (j < 0)
            return new(IssueDelivrance.Reserve, derniereFormalite, j,
                "La dernière formalité est postérieure à la date de travail du greffe.", seuil);

        return j >= seuil
            ? new(IssueDelivrance.Delivrance, derniereFormalite, j, null, seuil)
            : new(IssueDelivrance.Reserve, derniereFormalite, j, "Le délai depuis la dernière formalité n'est pas écoulé.", seuil);
    }

    /// <summary>
    /// Le controle tel que l'ecran le joue, a la date de travail : sur le
    /// dossier retenu s'il y en a un, sinon sur l'entreprise du SIREN recu.
    /// </summary>
    public static ControleDelivrance ControlerNonOpposition(CommandeRecue c)
    {
        var cible = Entite(c.CibleRetenue);
        var parSiren = cible is null ? Entites.Where(e => Chiffres(c.Siren).Length > 0 && Chiffres(e.Siren) == Chiffres(c.Siren)).ToList() : [];
        var entreprise = cible ?? parSiren.FirstOrDefault();
        return ControlerNonOpposition(DateTravail, DateOnly.FromDateTime(c.Date), c.Siren, c.NumeroAffaire,
            entreprise is not null, DerniereFormalite(entreprise), cibleRetenue: cible is not null);
    }

    /// <summary>« 56 j ≥ 31 j », « 15 j &lt; 31 j » ou « formalité postérieure » (Delivrance_Delai_*).</summary>
    public static string Delai(ControleDelivrance c) => c.Jours switch
    {
        null => "—",
        < 0 => "formalité postérieure",
        var j when j >= c.Seuil => $"{j} j ≥ {c.Seuil} j",
        var j => $"{j} j < {c.Seuil} j",
    };

    /// <summary>
    /// Pourquoi ce certificat de la famille faillite ne peut pas etre delivre
    /// pour cette entite ; nul quand il le peut (MotifBlocageNonFaillite,
    /// DelivranceCertificatsRcs).
    /// </summary>
    public static string? MotifBlocage(CertificatFaillite certificat, EntiteRapprochee e) => certificat switch
    {
        CertificatFaillite.NonFaillite when e.ProcedureCollective => MotifProcedureCollective,
        CertificatFaillite.NonFaillite when e.Radie => "Bloqué — dossier radié",
        CertificatFaillite.NonFaillite when !e.NonInscrit && !e.ASiege => "Bloqué — dossier secondaire",
        CertificatFaillite.NonRcs when !e.NonInscrit =>
            "Certificat non délivré : une adresse atteste une immatriculation au registre du commerce.",
        _ => null,
    };

    private const string MotifProcedureCollective = "Bloqué — procédure collective en cours et diffusable";

    /// <summary>
    /// Le motif ouvre-t-il la bascule « Délivrer le certificat de faillite » ?
    /// Seulement s'il ETABLIT la procedure : un autre refus reste un refus.
    /// </summary>
    public static bool OuvreLaBasculeFaillite(string? motif) => motif == MotifProcedureCollective;

    // ---------------------------------------------------------- types d'inscription

    private static CaseType Case(string code, string libelle) => new(code, libelle, []);
    private static CaseType Regroupement(string code, string libelle, params CaseType[] enfants) => new(code, libelle, enfants);

    /// <summary>Les cases dans l'ordre de la maquette NeoRIG (CasesTypeInscription.Toutes).</summary>
    public static IReadOnlyList<CaseType> CasesTypes { get; } =
    [
        Case("PE/CN", "Protêt, non-paiement de chèque"),
        Case("D", "Déclaration de créance"),
        Case("F", "Prêt et délai"),
        Case("W", "Warrant"),
        Case("WA", "Warrants agricoles"),
        Case("IN", "Inaliénabilité"),
        Case("U", "Insalubrité"),
        Case("SP", "Saisie pénale fonds de commerce"),
        Case("X", "Proxénétisme"),
        Case("G/PNOM/K", "Gage sans dépossession, outillage, gage des stocks"),
        Regroupement("ST", "Sécurité sociale et Trésor", Case("SS", "Sécurité sociale"), Case("PT", "Trésor")),
        Regroupement("VN", "Vendeur et nantissements de fonds", Case("PV", "Vendeur"),
            Case("PN/NJ/FA/PNAG", "Nantissements fonds — commerce, judiciaire, artisanal, agricole")),
        Regroupement("Parts", "Parts", Case("NP", "Parts de sociétés, parts SCI"), Case("Z", "Parts SCI judiciaire")),
        Regroupement("CLR", "Crédit bail, location, réserve de propriété", Case("CB", "Crédit bail"),
            Case("L/R", "Location, réserve de propriété")),
        Regroupement("Fluvial", "Fluvial", Case("HF", "Hypothèque fluviale"), Case("JF", "Propriété bateau"),
            Case("SF", "Saisie bateaux")),
        Regroupement("Maritime", "Maritime", Case("Q", "Hypothèque maritime"), Case("SM", "Saisie navires")),
    ];

    /// <summary>
    /// « Commande web : seuls les types commandés sont affichés, pré-cochés » :
    /// les cases simples commandees, et chaque regroupement reduit a ses enfants commandes.
    /// </summary>
    public static List<CaseType> CasesCommandees(CommandeRecue c)
    {
        var recus = c.TypesRecus.ToHashSet(StringComparer.Ordinal);
        var cases = new List<CaseType>();
        foreach (var k in CasesTypes)
        {
            if (k.EstRegroupement)
            {
                var enfants = k.Enfants.Where(e => recus.Contains(e.Code)).ToList();
                if (enfants.Count > 0) cases.Add(k with { Enfants = enfants });
            }
            else if (recus.Contains(k.Code)) cases.Add(k);
        }
        return cases;
    }

    // ----------------------------------------------------------------- edition

    /// <summary>
    /// Les documents que la validation editera (DocumentsValidation.Composer) :
    /// le produit de la famille, puis la lettre d'accompagnement, puis la facture
    /// d'une saisie au guichet.
    /// </summary>
    public static List<DocumentEdite> Documents(CommandeRecue c, int typesCoches, int quantiteInscriptions,
        IReadOnlyDictionary<CertificatFaillite, int> certificats, bool certificatDeFaillite,
        IReadOnlyCollection<SousTypeCno> sousTypes, int quantiteCno, bool certificatDOpposition)
    {
        var docs = new List<DocumentEdite>();
        switch (Groupe(c.Famille))
        {
            case FamilleCommande.EtatEndettement:
                if (typesCoches > 0 && quantiteInscriptions > 0)
                    docs.Add(new("État d'endettement", "Sécurisé", quantiteInscriptions, "types cochés"));
                break;
            case FamilleCommande.Faillite:
                foreach (var cert in Enum.GetValues<CertificatFaillite>())
                {
                    var q = certificats.TryGetValue(cert, out var n) ? n : 0;
                    if (cert == CertificatFaillite.NonFaillite && certificatDeFaillite)
                        docs.Add(new("Certificat de faillite", "Sécurisé", Math.Max(q, 1), "délivré à la place du certificat de non-faillite"));
                    else if (q > 0)
                        docs.Add(new(LibelleDocument(cert), "Sécurisé", q, "certificat commandé"));
                }
                break;
            case FamilleCommande.NonOpposition:
                if (certificatDOpposition)
                    docs.Add(new("Certificat d'opposition", "Sécurisé", Math.Max(quantiteCno, 1), "délivré à la place du certificat de non-opposition"));
                else if (quantiteCno > 0)
                    foreach (var s in sousTypes.Where(s => s != SousTypeCno.InjonctionDePayer).OrderBy(s => s))
                        docs.Add(new(LibelleDocument(s), "Sécurisé", quantiteCno,
                            s == SousTypeCno.TransmissionUniverselle ? "sous-type coché · publication retenue" : "sous-type coché"));
                break;
        }
        docs.Add(new("Lettre d'accompagnement", "Normal", 1, "systématique"));
        if (c.Origine == OrigineCommande.Guichet)
            docs.Add(new("Facture", "Normal", null, "commandes manuelles uniquement"));
        return docs;
    }

    private static string LibelleDocument(CertificatFaillite c) => c switch
    {
        CertificatFaillite.NonFaillite => "Certificat de non-faillite",
        CertificatFaillite.NonRcs => "Certificat de non RCS",
        _ => "Certificat de non RSAC",
    };

    private static string LibelleDocument(SousTypeCno s) => s switch
    {
        SousTypeCno.ReductionCapital => "Certificat de non-opposition (réduction de capital)",
        SousTypeCno.FusionScission => "Certificat de non-opposition (fusion ou scission)",
        _ => "Certificat de non-opposition (TUP — publication au BODACC)",
    };

    /// <summary>« Édition : 2 document(s) » (Edition_Barre).</summary>
    public static string BarreEdition(IReadOnlyCollection<DocumentEdite> docs) => $"Édition : {docs.Count} document(s)";

    // ------------------------------------------------------------- transitions

    /// <summary>
    /// Le refus d'une decision, en clair ; nul quand elle est permise
    /// (TransitionsCommandeWeb, et le motif obligatoire au rejet).
    /// </summary>
    public static string? Refus(CommandeRecue c, ActionCommande action, string? motif)
    {
        if (c.EstTraitee)
            return "Cette commande est déjà traitée : aucune action possible.";
        if (c.Etat == EtatCommande.SaisieManuelle)
            return "Enregistrez la saisie avant de traiter la commande.";
        if (c.Etat == EtatCommande.EnAttente && action == ActionCommande.MettreEnAttente)
            return "Cette commande est déjà en attente.";
        var m = (motif ?? string.Empty).Trim();
        if (action == ActionCommande.Rejeter && m.Length == 0)
            return "Motif obligatoire pour un rejet.";
        if (m.Length > LongueurMaxMotif)
            return $"Le motif dépasse {LongueurMaxMotif} caractères : raccourcissez-le.";
        return null;
    }

    /// <summary>L'enveloppe d'une commande : toutes les commandes de meme numero.</summary>
    public static List<CommandeRecue> Enveloppe(IEnumerable<CommandeRecue> toutes, CommandeRecue c) =>
        toutes.Where(x => x.Reference == c.Reference).OrderBy(x => x.Date).ThenBy(x => x.Id).ToList();

    /// <summary>Retient l'envoi de l'enveloppe : a traiter, ou encore en saisie au guichet.</summary>
    public static bool RetientLEnveloppe(CommandeRecue c) =>
        c.Etat is EtatCommande.ATraiter or EtatCommande.EnAttente or EtatCommande.SaisieManuelle;

    /// <summary>L'etat de l'enveloppe, en une phrase (Lot_Etat_*).</summary>
    public static string EtatEnveloppe(IReadOnlyList<CommandeRecue> enveloppe)
    {
        var restantes = enveloppe.Count(RetientLEnveloppe);
        if (restantes > 0) return $"{restantes} à traiter avant l'envoi.";
        return enveloppe.Any(x => x.EnvoiGroupeEnAttente)
            ? "Toutes les commandes sont traitées, mais l'enveloppe n'est pas partie."
            : "Enveloppe envoyée : toutes les commandes validées de ce numéro ont leurs documents.";
    }

    /// <summary>
    /// Applique la decision et rend le message du bandeau. La validation garde
    /// les documents dans l'enveloppe tant qu'une autre commande du meme numero
    /// reste a traiter ; la derniere fait partir toute l'enveloppe.
    /// </summary>
    public static string Appliquer(List<CommandeRecue> toutes, CommandeRecue c, ActionCommande action,
        string? motif, IReadOnlyList<DocumentEdite> documents, string? mail = null)
    {
        var refus = Refus(c, action, motif);
        if (refus is not null) throw new InvalidOperationException(refus);

        switch (action)
        {
            case ActionCommande.MettreEnAttente:
                c.Etat = EtatCommande.EnAttente;
                c.Motif = (motif ?? string.Empty).Trim();
                return $"Commande {c.Reference} mise en attente.";

            case ActionCommande.Rejeter:
                c.Etat = EtatCommande.Rejetee;
                c.Motif = (motif ?? string.Empty).Trim();
                return $"Commande {c.Reference} rejetée.";
        }

        c.Etat = EtatCommande.Validee;
        c.Motif = string.Empty;
        c.Envoyes.Clear();
        c.Envoyes.AddRange(documents);
        var liste = string.Join(", ", documents.Select(d => d.Exemplaires is { } n ? $"{d.Document} ({n} ex.)" : d.Document));
        var message = $"Commande {c.Reference} validée.";

        var enveloppe = Enveloppe(toutes, c);
        var autres = enveloppe.Count(x => x.Id != c.Id && RetientLEnveloppe(x));
        if (autres > 0)
        {
            c.EnvoiGroupeEnAttente = true;
            message = $"{message} Documents retenus pour l'enveloppe : {liste}.";
            message = autres == 1
                ? $"{message} Ils partiront dans la même enveloppe qu'une autre commande de ce numéro, encore à traiter."
                : $"{message} Ils partiront dans la même enveloppe que les {autres} autres commandes de ce numéro, encore à traiter.";
        }
        else
        {
            var envoyees = enveloppe.Where(x => x.Etat == EtatCommande.Validee).ToList();
            foreach (var x in envoyees) x.EnvoiGroupeEnAttente = false;
            message = $"{message} Documents produits : {liste}.";
            if (envoyees.Count > 1) message = $"{message} Enveloppe envoyée : {envoyees.Count} commandes éditées ensemble.";
        }

        if (!string.IsNullOrWhiteSpace(mail))
            message = $"{message} Les documents partiront aussi par mail à {mail.Trim()}.";
        return message;
    }

    /// <summary>« Envoyer sans attendre » : edite ensemble les commandes deja validees de l'enveloppe.</summary>
    public static string EnvoyerSansAttendre(IEnumerable<CommandeRecue> toutes, CommandeRecue c)
    {
        var pretes = Enveloppe(toutes, c).Where(x => x.EnvoiGroupeEnAttente).ToList();
        foreach (var x in pretes) x.EnvoiGroupeEnAttente = false;
        return pretes.Count == 1
            ? "Enveloppe envoyée : 1 commande éditée."
            : $"Enveloppe envoyée : {pretes.Count} commandes éditées ensemble.";
    }

    // ------------------------------------------------------- saisie au guichet

    /// <summary>« MAN-0001 » : le numero d'une commande saisie au guichet (NumeroCommandeManuelle).</summary>
    public static string NumeroManuel(IEnumerable<CommandeRecue> toutes)
    {
        var max = toutes.Select(c => c.Reference)
            .Where(r => r.StartsWith("MAN-", StringComparison.OrdinalIgnoreCase))
            .Select(r => int.TryParse(r.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return "MAN-" + (max + 1).ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>Cree la commande du guichet, en saisie : elle n'est a traiter qu'une fois enregistree.</summary>
    public static CommandeRecue NouvelleSaisie(List<CommandeRecue> toutes)
    {
        var c = new CommandeRecue
        {
            Id = toutes.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1,
            Reference = NumeroManuel(toutes),
            Date = JeuDemo.Aujourdhui,
            Famille = FamilleCommande.EtatEndettement,
            Origine = OrigineCommande.Guichet,
            Etat = EtatCommande.SaisieManuelle,
        };
        toutes.Add(c);
        return c;
    }

    /// <summary>
    /// Enregistre la saisie : la commande passe « À traiter ». Rend le refus en
    /// clair quand il manque de quoi la designer.
    /// </summary>
    public static string? EnregistrerSaisie(CommandeRecue c)
    {
        if (c.Etat != EtatCommande.SaisieManuelle) return "Cette commande n'est pas en saisie.";
        if (string.IsNullOrWhiteSpace(c.Denomination) && Chiffres(c.Siren).Length == 0)
            return "Saisissez une raison sociale ou un SIREN.";
        if (string.IsNullOrWhiteSpace(c.Requerant))
            return "Saisissez le nom du requérant.";
        c.Etat = EtatCommande.ATraiter;
        return null;
    }

    // -------------------------------------------------- coordonnees du destinataire

    /// <summary>Ce que la plieuse ou la poste reprocheront a l'adresse ; vide quand elle passe.</summary>
    public static List<string> ControlerAdresse(IReadOnlyList<string> lignes, string? pays)
    {
        var avertissements = new List<string>();
        var pleines = lignes.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        if (pleines.Count > LignesMaxAdresse)
            avertissements.Add($"L'adresse tient sur {pleines.Count} lignes : la plieuse n'en imprime que 6.");
        foreach (var l in pleines.Where(l => l.Length > CaracteresMaxLigne))
            avertissements.Add($"Ligne de plus de 34 caractères, la plieuse la coupera : « {l} ».");
        var p = (pays ?? string.Empty).Trim();
        if (p.Length > 0 && !PaysUnionEuropeenne.Contains(Normaliser(p)))
            avertissements.Add($"Destination hors de l'Union européenne ({p}) : vérifiez que l'envoi est attendu.");
        return avertissements;
    }

    private static readonly HashSet<string> PaysUnionEuropeenne = new(StringComparer.Ordinal)
    {
        "FRANCE", "ALLEMAGNE", "AUTRICHE", "BELGIQUE", "BULGARIE", "CHYPRE", "CROATIE", "DANEMARK", "ESPAGNE",
        "ESTONIE", "FINLANDE", "GRECE", "HONGRIE", "IRLANDE", "ITALIE", "LETTONIE", "LITUANIE", "LUXEMBOURG",
        "MALTE", "PAYS BAS", "POLOGNE", "PORTUGAL", "REPUBLIQUE TCHEQUE", "ROUMANIE", "SLOVAQUIE", "SLOVENIE", "SUEDE",
    };

    // ----------------------------------------------------------------- outils

    public static string Chiffres(string? s) => new((s ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>Majuscules sans accents ni ponctuation, espaces simples : pour comparer deux noms.</summary>
    public static string Normaliser(string? s)
    {
        var decompose = (s ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Les mots qui designent : trois lettres au moins, hors formes sociales et articles.</summary>
    private static List<string> MotsSignificatifs(string? s) =>
        Normaliser(s).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(m => m.Length >= 3 && m is not ("SARL" or "SAS" or "EURL" or "SNC" or "LES" or "DES"))
            .ToList();

    // -------------------------------------------------------------- jeu d'exemple

    /// <summary>
    /// Un jeu NEUF a chaque ouverture de l'ecran : les traitements se font en
    /// memoire et ne survivent pas a la fermeture. Les quatre premieres
    /// commandes sont celles des requisitions (JeuDemo.CommandesWeb) : memes
    /// numeros, memes societes, vues par l'ecran NeoRIG.
    /// </summary>
    public static List<CommandeRecue> JeuDemoCommandes()
    {
        var v2 = JeuDemo.CommandesWeb();
        CommandeRecue DepuisV2(int id, int rang, FamilleCommande famille, string[]? types = null, SousTypeCno? sousType = null)
        {
            var w = v2[rang];
            var (adresse, mail) = Destinataire(w.Demandeur);
            return new CommandeRecue
            {
                Id = id, Reference = w.Numero, Date = w.Recue, Famille = famille,
                Origine = w.Origine == "Infogreffe" ? OrigineCommande.Infogreffe : OrigineCommande.SiteLocal,
                Etat = EtatCommande.ATraiter, Denomination = w.Societe, Siren = Chiffres(w.Siren), Requerant = w.Demandeur,
                ReferenceClient = "DOS-" + (4100 + id), AdresseLivraison = adresse, Email = mail,
                TypesRecus = types ?? [], SousType = sousType,
            };
        }

        var fidal = new[] { "CABINET FIDAL AMIENS", "Service immobilier", "3 rue Jules Barni", "80000 AMIENS" };
        var dubois = new[] { "SCP DUBOIS ET ASSOCIES", "14 boulevard Alsace-Lorraine", "80000 AMIENS" };

        var jeu = new List<CommandeRecue>
        {
            DepuisV2(1, 0, FamilleCommande.EtatEndettement, ["SS", "PT", "PV", "PN/NJ/FA/PNAG"]),
            DepuisV2(2, 1, FamilleCommande.Faillite),
            DepuisV2(3, 2, FamilleCommande.NonOpposition, sousType: SousTypeCno.ReductionCapital),
            DepuisV2(4, 3, FamilleCommande.EtatEndettement, ["Q", "SM"]),
            new()
            {
                Id = 5, Reference = "IG-90812", Date = new DateTime(2026, 9, 7, 10, 12, 0),
                Famille = FamilleCommande.EtatEndettement, Origine = OrigineCommande.Infogreffe, Etat = EtatCommande.ATraiter,
                Denomination = "SCI DU PARC", Siren = "512004331", Requerant = "CABINET FIDAL AMIENS",
                ReferenceClient = "FID-2026-311", AdresseLivraison = fidal, Email = "immobilier@fidal-amiens.fr",
                TypesRecus = ["NP", "Z"],
            },
            new()
            {
                Id = 6, Reference = "IG-90812", Date = new DateTime(2026, 9, 7, 10, 12, 0),
                Famille = FamilleCommande.Faillite, Origine = OrigineCommande.Infogreffe, Etat = EtatCommande.ATraiter,
                Denomination = "SCI DU PARC", Siren = "512004331", Requerant = "CABINET FIDAL AMIENS", NumeroGestion = "004176",
                ReferenceClient = "FID-2026-311", AdresseLivraison = fidal, Email = "immobilier@fidal-amiens.fr",
            },
            new()
            {
                Id = 7, Reference = "IG-90977", Date = new DateTime(2026, 9, 4, 16, 40, 0),
                Famille = FamilleCommande.NonOpposition, Origine = OrigineCommande.Infogreffe, Etat = EtatCommande.EnAttente,
                Denomination = "LES TROIS CHENES", Siren = "823445902", Requerant = "SCP DUBOIS ET ASSOCIES", NumeroAffaire = "2026/0418",
                Motif = "Délai d'opposition en cours : à reprendre après le 25/09.",
                AdresseLivraison = dubois, Email = "greffe@scp-dubois.fr", SousType = SousTypeCno.TransmissionUniverselle,
            },
            new()
            {
                Id = 8, Reference = "IG-91004", Date = new DateTime(2026, 9, 5, 11, 3, 0),
                Famille = FamilleCommande.EtatEndettementNonInscrit, Origine = OrigineCommande.Infogreffe, Etat = EtatCommande.ATraiter,
                Denomination = "CABINET LEROY", Requerant = "CREDIT AGRICOLE BRIE PICARDIE",
                AdresseLivraison = ["CREDIT AGRICOLE BRIE PICARDIE", "Service contentieux", "500 rue Saint-Fuscien", "80095 AMIENS CEDEX 3"],
                Email = "contentieux@ca-briepicardie.fr", TypesRecus = ["D", "F"],
            },
            new()
            {
                Id = 9, Reference = "IG-91022", Date = new DateTime(2026, 9, 6, 9, 21, 0),
                Famille = FamilleCommande.EtatEndettementRequisition, Origine = OrigineCommande.SiteLocal, Etat = EtatCommande.ATraiter,
                Denomination = "MARTIN ERIC", Requerant = "MAITRE CARPENTIER",
                // Ligne trop longue pour la plieuse, exprès : l'écran des coordonnées le signale.
                AdresseLivraison = ["MAITRE CARPENTIER", "Huissier de justice — 22 rue Delambre — bâtiment B", "80000 AMIENS"],
                Email = "etude@carpentier-huissier.fr", TypesRecus = ["CB", "L/R", "G/PNOM/K"],
            },
            new()
            {
                Id = 10, Reference = "IG-90655", Date = new DateTime(2026, 9, 2, 8, 50, 0),
                Famille = FamilleCommande.Faillite, Origine = OrigineCommande.Infogreffe, Etat = EtatCommande.Validee,
                Denomination = "BATIPRO SARL", Siren = "790220114", Requerant = "BANQUE DU NORD", CibleRetenue = "004168|BATIPRO SARL",
                AdresseLivraison = ["BANQUE DU NORD", "8 place Gambetta", "80000 AMIENS"],
            },
            new()
            {
                Id = 11, Reference = "IG-90601", Date = new DateTime(2026, 9, 1, 15, 7, 0),
                Famille = FamilleCommande.NonOpposition, Origine = OrigineCommande.Infogreffe, Etat = EtatCommande.Rejetee,
                Denomination = "DUVAL TRANSPORTS", Siren = "999888777", Requerant = "SCP DUBOIS ET ASSOCIES",
                Motif = "Aucune entreprise ne correspond au SIREN 999 888 777 de la commande.",
                AdresseLivraison = dubois, SousType = SousTypeCno.FusionScission,
            },
        };

        // La commande deja validee a ses documents : « Ce qui a été envoyé » les montre.
        var validee = jeu.Single(c => c.Id == 10);
        validee.Envoyes.Add(new("Certificat de non-faillite", "Sécurisé", 1, "certificat commandé"));
        validee.Envoyes.Add(new("Lettre d'accompagnement", "Normal", 1, "systématique"));
        return jeu;
    }

    private static (string[] Adresse, string Mail) Destinataire(string demandeur) => demandeur switch
    {
        "BANQUE DU NORD" => (["BANQUE DU NORD", "Service recouvrement", "8 place Gambetta", "80000 AMIENS"], "recouvrement@banquedunord.fr"),
        "MAITRE CARPENTIER" => (["MAITRE CARPENTIER", "22 rue Delambre", "80000 AMIENS"], "etude@carpentier-huissier.fr"),
        _ => ([demandeur, "14 boulevard Alsace-Lorraine", "80000 AMIENS"], "greffe@scp-dubois.fr"),
    };
}
