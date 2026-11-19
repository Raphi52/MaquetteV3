using System.Text.Json;

namespace RigV3Desktop.Pont;

// ============================================================
// LA VUE du plan de travail et SES GESTES (lot 2 du plan « finir le passage a
// React », 19/11/2026). Ce que le balisage de Home.razor lisait dans son @code
// est calcule ici, UNE fois par rendu, et rendu par web/src/plan/ ; chaque
// @onclick / @oninput / @onpointer... du .razor devient un geste nomme, joue
// par Agir sur les MEMES methodes (Pont/PlanTravail.cs).
//
// Un record par bloc du balisage, memes noms que web/src/plan/types.ts
// (proprietes en camelCase) : un champ renomme d'un cote casse le typecheck de
// l'autre a la relecture.
// ============================================================

/// <summary>Toute la page : l'accueil (lot 1) et le plan de travail.</summary>
/// <param name="Geste">Le numero du dernier geste du plan de travail joue (GestePlan.Numero) : la
/// page n'affiche jamais une vue plus ancienne que son dernier geste.</param>
/// <param name="Barre">Nul dans la fenetre d'un onglet sorti (ni barre, ni accueil).</param>
/// <param name="Onglets">Nul quand la barre d'onglets n'est pas affichee.</param>
/// <param name="FinsDeGlisse">Avance quand le C# termine lui-meme un glisser d'onglet (onglet arrache).</param>
/// <param name="Pagination">Avance quand l'observateur de defilement des etapes doit etre rebranche.</param>
/// <param name="Defilement">Le dernier defilement de la zone des etapes demande par le C#.</param>
public sealed record VueApplication(string Theme, int Geste, VueBarre? Barre, VuePlanAccueil? Accueil,
                                    OngletBarreVue[]? Onglets, int FinsDeGlisse, ProcVue? Proc,
                                    DossierVue? Dossier, ImpressionVue? Impression, EnvoiMailVue? EnvoiMail,
                                    NouveauVue? Nouveau, MenuNoeudVue? MenuNoeud,
                                    int Pagination, DefilementVue? Defilement);

/// <summary>Un geste de la page pour le plan de travail, numerote par la page.</summary>
public sealed record GestePlan(string Nom, JsonElement Valeur, int Numero);

public sealed record OngletBarreVue(string Code, string Libelle, bool Actif, bool Detache, bool Glisse);

public sealed record DefilementVue(int Numero, int Index, bool Doux);

// ---- L'ecran de procedure (div.rig-proc) ----
/// <param name="BoutonFermer">Faux dans la fenetre d'un onglet sorti : sa croix ferme deja la vue.</param>
public sealed record ProcVue(string Code, string Libelle, bool Favori, bool BoutonFermer,
                             SujetVue? Sujet, EcranProcVue Ecran);

/// <summary>Le bandeau du sujet (ou, a defaut, de la tache) : les ecrans voisins.</summary>
public sealed record SujetVue(string Titre, OngletSujetVue[] Onglets);
public sealed record OngletSujetVue(string Code, string Libelle, bool Actif, string? Detail);

/// <summary>Ce que les ecrans recevaient de Home.razor en parametres.</summary>
/// <param name="Valides">Les dossiers des demandes qui ont passe le controle final (EcranDemandes).</param>
/// <param name="CleOuverture">Change avec le SIREN : l'ecran d'ouverture est reconstruit (@key).</param>
/// <param name="Lot">La session de l'ecran des taches en lot, ouverte ici avec l'etat vivant.</param>
/// <param name="Motif">Pourquoi la procedure n'a pas d'ecran (EcranNonMaquette).</param>
public sealed record EcranProcVue(string[] Valides, string? SirenDemande, string CleOuverture,
                                  SessionEcranVue? Lot, string? Motif);

public sealed record SessionEcranVue(string Session, int Version);

// ---- La visionneuse d'affaire (div.rig-vged) ----
public sealed record DossierVue(string Numero, string Objet, string Siren, string Type, string Statut,
                                bool BoutonDocument, SommaireVue Sommaire, BandeauDefautsVue? Bandeau,
                                string StyleCorps, TacheVue? Tache, string Filtre, LigneArbreVue Arbre,
                                bool Poignee, FicheColonneVue? Fiche, EtapesVue? Etapes, bool Poignee2,
                                ApercuColonneVue? Apercu, bool Voile);

public sealed record SommaireVue(string Designation, ChampSommaireVue[] Champs, string Mention);
public sealed record ChampSommaireVue(string Libelle, string Valeur, bool Mono);

public sealed record BandeauDefautsVue(int Nombre, bool Erreur, LienDefautVue[] Liens);
/// <param name="Suffixe">« (Dirigeant 2) » quand l'etape a plusieurs fiches, vide sinon.</param>
public sealed record LienDefautVue(int IndexEtape, int Fiche, bool Refuse, string Titre, string Etape,
                                   string Suffixe, int Nombre);

/// <summary>Le mode tache : bandeau au-dessus de l'arbre, lignes, checklists en pied.</summary>
public sealed record TacheVue(string Code, string Titre, bool ToutLeDossier, string TexteTout,
                              int Faits, int Exiges, int Facultatifs, string? Reste, bool Complete,
                              bool PeutClore, string Raison, LignesTacheVue? Lignes,
                              CaseTacheVue[] Cases, int CasesFaites, int CasesExigees,
                              EffetTacheVue[] Effets, int EffetsTraites, int EffetsObligatoires);
public sealed record LignesTacheVue(string Libelle, LigneTacheVue[] Lignes, string TitreAjout, bool Retrait);
public sealed record LigneTacheVue(int Rang, int Trous, bool Courante);
public sealed record CaseTacheVue(string Libelle, bool Exige, bool Rempli, string Valeur, string Titre);
public sealed record EffetTacheVue(string Code, string Libelle, bool Obligatoire, bool Atteste, bool Coche,
                                   bool Cible, string Titre, bool SansPiece, string TitreLibelle, string Piece);

/// <summary>« Informations du dossier / des formalites / des depots » : la fiche a session.</summary>
/// <param name="Branche">Laquelle des trois fiches du .razor (un composant different par branche).</param>
public sealed record FicheColonneVue(string Branche, string Session, int Version);

/// <summary>La saisie guidee : frise et pages des 7 etapes.</summary>
public sealed record EtapesVue(int Courante, int Total, string Titre, bool Oeil, PasEtapeVue[] Pas,
                               PageEtapeVue[] Pages);
/// <param name="Fiches">« 3 dirigeants » : le nombre, ou 0 quand il ne s'affiche pas.</param>
public sealed record PasEtapeVue(string Etat, bool Courant, string Pastille, string Titre, int Fiches,
                                 string Unite, int Refuses, int Manquants, int Remplis, int Total);
/// <param name="Rendue">Seules l'etape visible et ses deux voisines sont rendues.</param>
public sealed record PageEtapeVue(string Code, bool Courante, bool Rendue, string Titre, string Source,
                                  OngletsFichesVue? Fiches, RecapVue? Recap, ChampSaisieVue[] Champs);
public sealed record OngletsFichesVue(string Unite, int Nombre, int Actif);
public sealed record ChampSaisieVue(string Libelle, string Colonne, string Type, string Exemple,
                                    bool Obligatoire, string Largeur, string[] Choix, string Valeur,
                                    string? Refus, bool Manque);
/// <summary>Le controle final (etape CONTROLE).</summary>
public sealed record RecapVue(bool Valide, int Nombre, string Titre, int AManquer, int Refusees,
                              GroupeRecapVue[] Groupes);
public sealed record GroupeRecapVue(int IndexEtape, int Fiche, string Etape, bool Multi, string Unite,
                                    int Nombre, PointRecapVue[] Points);
public sealed record PointRecapVue(string Champ, string Motif, string Colonne, bool Manquant);

/// <summary>La colonne de droite : documents sortis et apercu de la piece.</summary>
public sealed record ApercuColonneVue(int Detachees, ApercuVue? Apercu);
public sealed record ApercuVue(string Extension, int Pages, string Date, string Libelle, string Message,
                               bool Pdf, string Fichier, VueKbis? Kbis, FeuilleApercuVue[] Feuilles);
public sealed record FeuilleApercuVue(int Numero, int Total, string Texte, ValeurApercuVue[]? Valeurs);
public sealed record ValeurApercuVue(string Libelle, string Valeur);

// ---- Les modales ----
public sealed record ImpressionVue(string Libelle, string[] Imprimantes, string[] Files, string[] Papiers,
                                   string Imprimante, string File, string Papier, int Exemplaires,
                                   bool RectoVerso);
public sealed record EnvoiMailVue(string Libelle, string[] Destinataires, string[] Messages, int Message,
                                  string Corps, string Destinataire, bool PeutEnvoyer);
public sealed record NouveauVue(string Cible, NatureVue[] Natures);
public sealed record NatureVue(string Code, string Titre, string Extension);
public sealed record MenuNoeudVue(string Titre, double X, double Y, bool EstPiece);

public sealed partial class SessionPlanTravail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ================= LA VUE =================

    /// <summary>
    /// La page entiere, calculee UNE fois par rendu de la coquille. Ce que le
    /// .razor faisait apres le rendu (amener la frise sur l'etape d'une piece
    /// ouverte) part avec la vue : la page le joue apres avoir rendu la zone.
    /// </summary>
    public VueApplication Vue()
    {
        var vueAccueil = ModeFenetreVue ? null : Accueil.Vue(ContexteDeLAccueil());

        // Ouvrir une piece amene la frise sur SON etape (OnAfterRenderAsync du .razor).
        if (EtapeADefiler && EtapesAffichees)
        {
            EtapeADefiler = false;
            Defiler(EtapeCourante, doux: false);
        }

        var proc = VueProc();
        // L'ecran des taches en lot et la fiche d'informations sont des ecrans a
        // SESSION ouverts ici, comme leur coquille le faisait : quand ils quittent
        // la page, leur session est fermee.
        if (proc is null || proc.Ecran.Lot is null) FermerSessionLot();
        var dossier = VueDossier();
        if (dossier?.Fiche is null) FermerSessionFiche();

        return new VueApplication(
            Theme,
            DernierGeste,
            vueAccueil?.Barre,
            vueAccueil?.Accueil,
            !ModeFenetreVue && Onglets.Onglets.Count > 0
                ? Onglets.Onglets.Select(o => new OngletBarreVue(
                      o.Code, o.Libelle, ReferenceEquals(Onglets.Actuel, o), o.Detache,
                      GlisseEnCours && GlisseCode == o.Code)).ToArray()
                : null,
            FinsDeGlisse,
            proc,
            dossier,
            ImpressionOuverte && ApercuNoeud is not null
                ? new ImpressionVue(ApercuNoeud.Libelle, ArbreDemo.Imprimantes, ArbreDemo.FilesDifferees,
                                    ArbreDemo.TypesPapier, ImprImprimante, ImprFileDifferee, ImprTypePapier,
                                    ImprExemplaires, ImprRectoVerso)
                : null,
            EnvoiMailOuvert && ApercuNoeud is not null
                ? new EnvoiMailVue(ApercuNoeud.Libelle, MailDestinatairesDemo,
                                   MailMessagesDemo.Select(m => m.Titre).ToArray(), MailMessageIndex,
                                   MailMessagesDemo[MailMessageIndex].Corps, MailDestinataire,
                                   MailDestinataire.Trim().Length > 0)
                : null,
            NouveauOuvert
                ? new NouveauVue(Libelle(DossierCible),
                                 AccesGed.Natures.Select(n => new NatureVue(n.Code, n.Titre, n.Extension)).ToArray())
                : null,
            MenuNoeud is not null
                ? new MenuNoeudVue(Libelle(MenuNoeud), MenuX, MenuY, MenuNoeud.EstPiece)
                : null,
            Pagination,
            Defilement);
    }

    private ProcVue? VueProc()
    {
        if (ProcOuverte is not { } p) return null;

        SujetVue? bandeau = null;
        var sujet = SujetDe(p.Code);
        if (sujet is not null)
        {
            bandeau = new SujetVue(sujet.Titre,
                ProcsDuSujet(sujet).Select(x => new OngletSujetVue(x.Code, x.Libelle, x.Code == p.Code, null)).ToArray());
        }
        else if (TacheDe(p.Code) is { } tache)
        {
            bandeau = new SujetVue(TitreTache(tache),
                tache.Entrees.Select(e => new OngletSujetVue(e.Code, e.Libelle, e.Code == p.Code, e.Detail)).ToArray());
        }

        return new ProcVue(p.Code, p.Libelle, Etat.EstFavori(p.Code), !ModeFenetreVue, bandeau,
            new EcranProcVue(
                JeuDemo.Demandes.Select(d => d.Dossier).Distinct().Where(EstValide).ToArray(),
                SirenAImmatriculer, CleOuverture.ToString() ?? "",
                p.Code == "PROC_TACHES_LOT" ? VueSessionLot() : null,
                MotifNonMaquette(p.Code)));
    }

    // ---- Taches en lot : la session que Components/Ecrans/EcranTachesLot.razor ouvrait ----
    private string? SessionLot;
    private int VersionLot;

    private SessionEcranVue VueSessionLot()
    {
        if (SessionLot is null)
            SessionLot = EcranTachesLot.Ouvrir(Etat, Utilisateur, LotDemande);
        else if (EcranTachesLot.Parametrer(SessionLot, Etat, Utilisateur, LotDemande))
            VersionLot++;
        return new SessionEcranVue(SessionLot, VersionLot);
    }

    private void FermerSessionLot()
    {
        if (SessionLot is null) return;
        EcranTachesLot.Fermer(SessionLot);
        SessionLot = null;
        VersionLot = 0;
    }

    // ---- Fiche d'informations : la session que Components/FicheInformationsDossier.razor ouvrait ----
    private string? SessionFiche;
    private string? BrancheFiche;
    private int VersionFiche;

    private FicheColonneVue VueSessionFiche(string branche, ParametresFiche parametres)
    {
        // Trois branches du .razor = trois composants : changer de branche en
        // fermait un et en creait un autre, donc une session neuve.
        if (SessionFiche is not null && BrancheFiche != branche) FermerSessionFiche();
        if (SessionFiche is null)
        {
            SessionFiche = FicheInformationsDossier.Ouvrir(parametres);
            BrancheFiche = branche;
            VersionFiche = 0;
        }
        else if (FicheInformationsDossier.Parametrer(SessionFiche, parametres))
            VersionFiche++;
        return new FicheColonneVue(branche, SessionFiche, VersionFiche);
    }

    private void FermerSessionFiche()
    {
        if (SessionFiche is null) return;
        FicheInformationsDossier.Fermer(SessionFiche);
        SessionFiche = null;
        BrancheFiche = null;
        VersionFiche = 0;
    }

    /// <summary>Les noeuds de l'arbre rendus au dernier calcul, par identifiant :
    /// un geste de la page les nomme ainsi (l'Index de ArbreGedNoeud.razor).</summary>
    private Dictionary<string, NoeudGed> IndexArbre = new(StringComparer.Ordinal);

    private DossierVue? VueDossier()
    {
        if (DossierOuvert is not { } d) return null;

        var fs = SommaireDu(d);
        // UN SEUL FLUX, pas de grille : chaque champ VIDE disparait --
        // libelle compris (demande du 17/09/2026).
        var champs = new (string Libelle, string Valeur, bool Mono)[]
        {
            ("N° de gestion", d.Numero, true),
            ("SIREN", d.Siren, true),
            ("Type", d.Type, false),
            ("Type de dossier", d.TypeDossier, true),
            ("Forme juridique", fs.FormeJuridique, false),
            ("Sigle", fs.Sigle, false),
            ("Nom commercial", fs.NomCommercial, false),
            ("Civilite", fs.Civilite, false),
            ("Designation", fs.Designation, false),
            ("Adresse", fs.Adresse.Replace("\n", " - "), false),
            ("Immat.", fs.Immatriculation, true),
            ("Cessation", fs.Cessation, true),
            ("Date MAJ", fs.DateMaj, true)
        };
        var sommaire = new SommaireVue(fs.Designation,
            champs.Where(c => c.Valeur is { Length: > 0 })
                  .Select(c => new ChampSommaireVue(c.Libelle, c.Valeur, c.Mono)).ToArray(),
            fs.Mention);

        BandeauDefautsVue? bandeau = null;
        if (!DossierValide && TousLesDefauts() is { Count: > 0 } defautsBandeau)
        {
            bandeau = new BandeauDefautsVue(defautsBandeau.Count, defautsBandeau.Any(x => !x.EstManquant),
                defautsBandeau.GroupBy(x => (x.IndexEtape, x.Etape, x.Fiche, x.Unite))
                              .OrderByDescending(x => x.Count(y => !y.EstManquant))
                              .ThenByDescending(x => x.Count())
                              .ThenBy(x => x.Key.IndexEtape)
                              .Select(g =>
                              {
                                  var refuses = g.Count(y => !y.EstManquant);
                                  var multi = Fiches(Etapes[g.Key.IndexEtape].Code).Count > 1;
                                  return new LienDefautVue(g.Key.IndexEtape, g.Key.Fiche, refuses > 0,
                                      string.Join(" · ", g.Select(y => $"{y.Champ} : {y.Motif}")),
                                      g.Key.Etape, multi ? $" ({g.Key.Unite} {g.Key.Fiche + 1})" : "", g.Count());
                              }).ToArray());
        }

        var index = new Dictionary<string, NoeudGed>(StringComparer.Ordinal);
        var arbre = ArbreGedNoeud.Vue(Arbre, 0, NoeudsOuverts, NoeudSelectionne, Domaines, AjoutsArbre,
                                      MasquesArbre, LibellesArbre, RecetteArbre, NoeudRenomme,
                                      FiltreDeLArbre, index);
        IndexArbre = index;

        FicheColonneVue? fiche = null;
        EtapesVue? etapes = null;
        if (DonneesVisible && NoeudSelectionne is { EstFiche: true })
        {
            fiche = NoeudSelectionne.Libelle == InformationsFormalite.LibelleNoeud
                ? VueSessionFiche("formalites", new ParametresFiche(d.Numero, "Formalites",
                      InformationsFormalite.LibelleNoeud, InformationsFormalite.Sections,
                      InformationsFormalite.ProcessusSources, null, 1, Etat, false))
                : NoeudSelectionne.Libelle == InformationsDepots.LibelleNoeud
                ? VueSessionFiche("depots", new ParametresFiche(d.Numero, "Depots d'actes",
                      InformationsDepots.LibelleNoeud, InformationsDepots.Sections,
                      InformationsDepots.ProcessusSources, null, 1, Etat, false))
                : VueSessionFiche("dossier", new ParametresFiche(d.Numero, "Dossier RCS",
                      InformationsDossier.LibelleNoeud, InformationsDossier.Sections,
                      InformationsDossier.ProcessusSources, RecetteActive, LigneCourante, Etat,
                      RecetteActive is null));
        }
        else if (DonneesVisible)
        {
            etapes = VueEtapes(d);
        }

        return new DossierVue(d.Numero, d.Objet, d.Siren, d.Type, d.Statut,
            !GuideVisible && ApercuNoeud is not null, sommaire, bandeau, StyleCorps, VueTache(),
            FiltreDeLArbre, arbre, DonneesVisible || GuideVisible, fiche, etapes,
            DonneesVisible && GuideVisible,
            GuideVisible ? new ApercuColonneVue(FenetresDetachees.Count, VueApercu(d)) : null,
            Redimensionne);
    }

    private TacheVue? VueTache()
    {
        if (RecetteActive is null) return null;
        var trous = TrousDeLaTache();
        var exiges = CasesExigees();
        var faits = exiges - trous.Length;
        var facultatifs = RecetteActive.Champs.Length - RecetteActive.ChampsObligatoires.Length;

        LignesTacheVue? lignes = null;
        if (RecetteActive.ARepetition)
        {
            var liste = new List<LigneTacheVue>();
            for (var i = 1; i <= NbLignesTache; i++)
                liste.Add(new LigneTacheVue(i, TrousDeLaLigne(i), i == LigneCourante));
            lignes = new LignesTacheVue(RecetteActive.LibelleLigne, liste.ToArray(),
                $"Ajouter un {RecetteActive.LibelleLigne.ToLowerInvariant()} a cette tache", NbLignesTache > 1);
        }

        var cases = CasesDeSaisie().Select(c => new CaseTacheVue(c.Libelle, c.Exige, c.Rempli, c.Valeur,
            $"{c.Champ.Table}.{c.Champ.Colonne}"
            + (c.Rempli
               ? $" -- coche par la valeur ecrite : {c.Valeur}"
               : " -- se cochera des que la valeur sera saisie dans la fiche"))).ToArray();

        var effets = RecetteActive.Effets.Select(e =>
        {
            var atteste = EffetAtteste(e);
            var coche = EffetCoche(e.Canal);
            var cible = EffetCible == e.Canal;
            return new EffetTacheVue(e.Code, e.Libelle, e.Obligatoire, atteste, coche, cible,
                $"Declare en base : {e.Source}"
                + (e.Piece.Length > 0
                   ? atteste
                       ? $" -- coche par la piece deposee : {e.Piece}"
                       : $" -- se cochera des que la piece « {e.Piece} » sera deposee"
                   : " -- aucun document ne l'atteste : a cocher a la main"),
                e.Piece.Length == 0,
                e.Piece.Length == 0
                    ? "Aucune piece a montrer pour cet effet"
                    : cible ? "Revenir a la tache entiere"
                            : $"Montrer dans l'arbre : {e.Piece}",
                e.Piece);
        }).ToArray();

        return new TacheVue(RecetteActive.Code, RecetteActive.Titre, ToutLeDossier,
            ToutLeDossier
                ? "Revenir a la tache"
                : $"Afficher tout le dossier ({BranchesMasquees} branches, {PiecesMasquees} pieces masquees)",
            faits, exiges, facultatifs, trous.Length > 0 ? NommerTrous(trous) : null, trous.Length == 0,
            TachePeutSeClore(), RaisonBlocage(), lignes,
            cases, CasesExigees() - TrousDeLaTache().Length, CasesExigees(),
            effets, RecetteActive.EffetsObligatoires.Length - EffetsRestants().Length,
            RecetteActive.EffetsObligatoires.Length);
    }

    private EtapesVue VueEtapes(Dossier d)
    {
        var pas = new PasEtapeVue[Etapes.Length];
        for (var i = 0; i < Etapes.Length; i++)
        {
            var etape = Etapes[i];
            var (remplis, total) = Avancement(etape);
            var nbFiches = Fiches(etape.Code).Count;
            var (manquants, refuses) = Defauts(etape);
            var etat = refuses > 0 ? "refuse"
                     : total > 0 && remplis == total ? "complet"
                     : i == EtapeCourante ? "encours"
                     : remplis > 0 ? "partiel" : "vide";
            pas[i] = new PasEtapeVue(etat, i == EtapeCourante,
                refuses > 0 ? "!" : total > 0 && remplis == total ? "OK" : (i + 1).ToString(),
                etape.Titre, etape.Multiple && nbFiches > 1 ? nbFiches : 0, etape.Unite.ToLower(),
                refuses, manquants, remplis, total);
        }

        var pages = new PageEtapeVue[Etapes.Length];
        for (var i = 0; i < Etapes.Length; i++)
        {
            var courante = Etapes[i];
            var rendue = Math.Abs(i - EtapeCourante) <= 1;
            if (!rendue)
            {
                pages[i] = new PageEtapeVue(courante.Code, i == EtapeCourante, false, courante.Titre,
                                            courante.Source, null, null, []);
                continue;
            }
            var fiches = Fiches(courante.Code);
            pages[i] = new PageEtapeVue(courante.Code, i == EtapeCourante, true, courante.Titre, courante.Source,
                courante.Multiple ? new OngletsFichesVue(courante.Unite, fiches.Count, IndexFiche(courante)) : null,
                courante.Code == "CONTROLE" ? VueRecap() : null,
                ChampsVue(courante));
        }

        return new EtapesVue(EtapeCourante, Etapes.Length, ApercuNoeud?.Libelle ?? d.Numero, !GuideVisible,
                             pas, pages);
    }

    /// <summary>Les champs visibles d'une etape, sur sa fiche affichee
    /// (ChampsSaisie du .razor : memes regles, meme ordre).</summary>
    private ChampSaisieVue[] ChampsVue(Etape courante)
    {
        var liste = new List<ChampSaisieVue>();
        foreach (var c in courante.Champs)
        {
            if (!Visible(courante, c)) continue;
            liste.Add(new ChampSaisieVue(c.Libelle, c.Colonne, c.Type, c.Exemple, c.Obligatoire, c.Largeur,
                c.Choix, Valeur(courante, c), RefusDe(courante, c), RequisManquant(courante, c)));
        }
        return liste.ToArray();
    }

    private RecapVue VueRecap()
    {
        if (DossierValide) return new RecapVue(true, 0, "", 0, 0, []);
        var defauts = TousLesDefauts();
        return new RecapVue(false, defauts.Count,
            defauts.Count == 0
                ? "Rien ne bloque la validation"
                : defauts.Count == 1
                    ? "1 point bloque la validation"
                    : $"{defauts.Count} points bloquent la validation",
            defauts.Count(x => x.EstManquant), defauts.Count(x => !x.EstManquant),
            defauts.GroupBy(x => (x.IndexEtape, x.Etape, x.Fiche, x.Unite))
                   .Select(g => new GroupeRecapVue(g.Key.IndexEtape, g.Key.Fiche, g.Key.Etape,
                       Fiches(Etapes[g.Key.IndexEtape].Code).Count > 1, g.Key.Unite, g.Count(),
                       g.Select(x => new PointRecapVue(x.Champ, x.Motif, x.Colonne, x.EstManquant)).ToArray()))
                   .ToArray());
    }

    private ApercuVue? VueApercu(Dossier d)
    {
        if (ApercuNoeud is not { } n) return null;
        VueKbis? kbis = null;
        var feuilles = new List<FeuilleApercuVue>();
        if (!n.APdf && EstKbis(n))
        {
            kbis = ApercuKbis.Vue(d.Numero, d.Siren, DenominationAffaire(), LireSaisie);
        }
        else if (!n.APdf)
        {
            var total = Math.Max(1, n.Pages);
            for (var feuille = 1; feuille <= total; feuille++)
            {
                feuilles.Add(new FeuilleApercuVue(feuille, total, TextePage(n, feuille),
                    feuille == 1
                        ? ValeursDocument(n).Select(v => new ValeurApercuVue(v.Libelle, v.Valeur)).ToArray()
                        : null));
            }
        }
        return new ApercuVue(n.Extension, n.Pages, n.Date, n.Libelle, ApercuMessage, n.APdf, n.Fichier,
                             kbis, feuilles.ToArray());
    }

    // ================= LES GESTES =================

    private sealed record GestePoint(double X, double Y);
    private sealed record GesteDebutGlisse(string Code, double X, double Y, MesureBarre? Mesure);
    private sealed record GesteNoeud(string Id, string? Texte, double ClientX, double ClientY);
    private sealed record GesteRedim(int Colonne, double X);
    private sealed record GesteFiche(string Etape, int Index);
    private sealed record GesteDefaut(int Etape, int Fiche);
    private sealed record GesteSaisie(string Etape, string Colonne, string? Valeur);

    private static T Lire<T>(JsonElement valeur)
        => valeur.Deserialize<T>(Json) ?? throw new InvalidOperationException($"Geste sans valeur ({typeof(T).Name}).");

    private static string Texte(JsonElement valeur)
        => valeur.ValueKind == JsonValueKind.String ? valeur.GetString() ?? "" : valeur.ToString();

    private static int Entier(JsonElement valeur)
        => valeur.ValueKind == JsonValueKind.Number ? valeur.GetInt32() : int.Parse(Texte(valeur));

    private Etape EtapeDe(string code)
        => Etapes.FirstOrDefault(x => x.Code == code)
           ?? throw new InvalidOperationException($"Etape inconnue : {code}.");

    private Champ ChampDe(Etape e, string colonne)
        => e.Champs.FirstOrDefault(x => x.Colonne == colonne)
           ?? throw new InvalidOperationException($"Champ inconnu : {e.Code}.{colonne}.");

    private NoeudGed? NoeudDe(string? id) => id is not null && IndexArbre.TryGetValue(id, out var n) ? n : null;

    private EffetRecette? EffetDe(JsonElement valeur)
    {
        var i = Entier(valeur);
        return RecetteActive is not null && i >= 0 && i < RecetteActive.Effets.Length ? RecetteActive.Effets[i] : null;
    }

    /// <summary>Le numero du dernier geste recu de la page (voir VueApplication.Geste).</summary>
    private int DernierGeste;

    /// <summary>Un geste numerote par la page. Le numero est retenu AVANT le geste :
    /// un geste en echec ne laisse pas la page attendre une vue qui ne viendrait pas.</summary>
    public void Jouer(GestePlan g)
    {
        DernierGeste = Math.Max(DernierGeste, g.Numero);
        Agir(g.Nom, g.Valeur);
    }

    /// <summary>
    /// Un geste du plan de travail : chaque @onclick / @oninput / @onpointer...
    /// du .razor, nomme, appelle la MEME methode. La page redessine ensuite (la
    /// coquille relit la vue). Un geste inconnu est une erreur franche.
    /// </summary>
    public void Agir(string geste, JsonElement valeur)
    {
        switch (geste)
        {
            // ---- Clavier et barre d'onglets ----
            case "echap": ToucheEchap(); break;
            case "ongletDebut":
            {
                var g = Lire<GesteDebutGlisse>(valeur);
                if (Onglets.Onglets.FirstOrDefault(o => o.Code == g.Code) is { } onglet)
                    DebutGlisse(onglet, g.X, g.Y, g.Mesure);
                break;
            }
            case "ongletGlisse": { var p = Lire<GestePoint>(valeur); GlisseOnglet(p.X, p.Y); break; }
            case "ongletFin": { var p = Lire<GestePoint>(valeur); FinGlisse(p.X, p.Y); break; }
            case "ongletActiver":
            {
                var code = Texte(valeur);
                if (Onglets.Onglets.FirstOrDefault(o => o.Code == code) is { } onglet) ActiverOnglet(onglet);
                break;
            }
            case "ongletFermer": FermerOnglet(Texte(valeur)); break;

            // ---- Ecran de procedure et ouvertures demandees par les ecrans ----
            case "favori": BasculerFavori(Texte(valeur)); break;
            case "fermerProc": FermerProc(); break;
            case "ouvrirCode": OuvrirCode(Texte(valeur)); break;
            case "ouvrirNumero": OuvrirParNumero(Texte(valeur)); break;
            case "ouvrirUnDossier": OuvrirUnDossierPour(Texte(valeur)); break;

            // ---- Visionneuse : entete, bandeau des defauts, colonnes ----
            case "fermerDossier": FermerDossier(); break;
            case "afficherDocument": GuideVisible = true; break;
            case "allerAuDefautBandeau":
            {
                var g = Lire<GesteDefaut>(valeur);
                AllerAuDefautDepuisBandeau(new Defaut(g.Etape, "", g.Fiche, "", "", "", "", true));
                break;
            }
            case "redimDebut":
            {
                var g = Lire<GesteRedim>(valeur);
                if (g.Colonne == 2) DebutRedim2(g.X); else DebutRedim(g.X);
                break;
            }
            case "redim": PendantRedim(Lire<GestePoint>(valeur).X); break;
            case "redimFin": Redimensionne = false; break;

            // ---- Arbre ----
            case "ouvrirNouveau": OuvrirNouveau(); break;
            case "filtre": FiltreDeLArbre = Texte(valeur); break;
            case "effacerFiltre": FiltreDeLArbre = string.Empty; break;
            case "arbreActiver":
                // Un dossier se replie/se deplie ; une piece se selectionne
                // (ArbreGedNoeud.razor), puis Selectionner = OuvrirApercu.
                if (NoeudDe(Texte(valeur)) is { } active)
                {
                    ArbreGedNoeud.Activer(active, NoeudsOuverts, Domaines, AjoutsArbre, MasquesArbre, RecetteArbre);
                    OuvrirApercu(active);
                }
                break;
            case "arbreRenommer":
            {
                var g = Lire<GesteNoeud>(valeur);
                if (NoeudDe(g.Id) is { } n) ValiderRenommage((n, g.Texte ?? ""));
                break;
            }
            case "arbreMenu":
            {
                var g = Lire<GesteNoeud>(valeur);
                if (NoeudDe(g.Id) is { } n) OuvrirMenuNoeud((n, g.ClientX, g.ClientY));
                break;
            }

            // ---- Mode tache ----
            case "basculerTout": BasculerToutLeDossier(); break;
            case "terminerTache": TerminerTache(); break;
            case "fermerTache": FermerTache(); break;
            case "ligneTache": LigneCourante = Entier(valeur); break;
            case "ajouterLigne": AjouterLigneTache(); break;
            case "retirerLigne": RetirerLigneTache(); break;
            case "basculerEffet": if (EffetDe(valeur) is { } eb) BasculerEffet(eb); break;
            case "viserEffet": if (EffetDe(valeur) is { } ev) ViserEffet(ev); break;

            // ---- Fiche d'informations (rappels du composant) ----
            case "ficheFermer": DonneesVisible = false; break;
            case "toutAffiche": CompterToutAffiche(); break;
            case "champModifie": break; // la vue qui porte la tache recompte au rendu qui suit

            // ---- Saisie guidee ----
            case "fermerEtapes": DonneesVisible = false; break;
            case "allerEtape": AllerEtape(Entier(valeur)); break;
            case "etapeVisible": EtapeVisible(Entier(valeur)); break;
            case "choisirFiche": { var g = Lire<GesteFiche>(valeur); ChoisirFiche(EtapeDe(g.Etape), g.Index); break; }
            case "ajouterFiche": AjouterFiche(EtapeDe(Texte(valeur))); break;
            case "supprimerFiche": SupprimerFiche(EtapeDe(Texte(valeur))); break;
            case "valider": ValiderDossier(); break;
            case "rouvrir": RouvrirDossier(); break;
            case "allerAuDefaut":
            {
                var g = Lire<GesteDefaut>(valeur);
                AllerAuDefaut(new Defaut(g.Etape, "", g.Fiche, "", "", "", "", true));
                break;
            }
            case "saisie":
            {
                var g = Lire<GesteSaisie>(valeur);
                var e = EtapeDe(g.Etape);
                EcrireEnDirect(e, ChampDe(e, g.Colonne), g.Valeur);
                break;
            }
            case "ecrire":
            {
                var g = Lire<GesteSaisie>(valeur);
                var e = EtapeDe(g.Etape);
                Ecrire(e, ChampDe(e, g.Colonne), g.Valeur);
                break;
            }

            // ---- Apercu de la piece ----
            case "rattacher": RattacherDocument(); break;
            case "imprimer": OuvrirImpression(); break;
            case "editer": EditerPiece(); break;
            case "detacher": DetacherDocument(); break;
            case "sauvegarder": Sauvegarder(); break;
            case "envoiMail": OuvrirEnvoiMail(); break;
            case "fermerApercu": FermerApercu(); break;

            // ---- Impression ----
            case "fermerImpression": ImpressionOuverte = false; break;
            case "imprimante": ImprImprimante = Texte(valeur); break;
            case "fileDifferee": ImprFileDifferee = Texte(valeur); break;
            case "papier": ImprTypePapier = Texte(valeur); break;
            case "exemplaires":
                // @bind d'un int : une saisie illisible ne change pas la valeur.
                if (int.TryParse(Texte(valeur), out var exemplaires)) ImprExemplaires = exemplaires;
                break;
            case "rectoVerso": ImprRectoVerso = !ImprRectoVerso; break;
            case "lancerImpression": LancerImpression(); break;

            // ---- Envoi par mail ----
            case "fermerMail": EnvoiMailOuvert = false; break;
            case "destinataire": MailDestinataire = Texte(valeur); break;
            case "message":
                if (int.TryParse(Texte(valeur), out var message)) MailMessageIndex = message;
                break;
            case "envoyerMail": EnvoyerMail(); break;

            // ---- Nouvel element ----
            case "fermerNouveau": NouveauOuvert = false; break;
            case "ajouterElement": AjouterElement(Texte(valeur)); break;

            // ---- Clic droit dans l'arbre ----
            case "fermerMenu": FermerMenu(); break;
            case "menuRenommer": if (MenuNoeud is { } mr) DemarrerRenommage(mr); break;
            case "menuNouveauSous": if (MenuNoeud is { } mn) NouveauSous(mn); break;
            case "menuDeplier": if (MenuNoeud is { } md) ToutDeplier(md); break;
            case "menuReplier": if (MenuNoeud is { } mp) ToutReplier(mp); break;
            case "menuDupliquer": if (MenuNoeud is { } mu) Dupliquer(mu); break;
            case "menuSupprimer": if (MenuNoeud is { } ms) SupprimerNoeud(ms); break;

            default: throw new InvalidOperationException($"Geste inconnu du plan de travail : {geste}.");
        }
    }
}
