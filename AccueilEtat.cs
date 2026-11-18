using System.IO;
using System.Linq;
using System.Text.Json;

namespace RigV3Desktop;

/// <summary>Pose d'une tuile sur la grille de l'accueil (12 colonnes).</summary>
public sealed class PoseTuile
{
    public int Colonne { get; set; } = 1;
    public int Ligne { get; set; } = 1;
    public int Largeur { get; set; } = 4;
    public int Hauteur { get; set; } = 2;
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Rang d'empilement : la DERNIERE tuile manipulee passe devant les
    /// autres. Sans lui, deux tuiles qui se chevauchent s'empilent dans
    /// l'ordre du code et celle qu'on vient de bouger peut se retrouver
    /// dessous.
    /// </summary>
    public int Ordre { get; set; }
}

/// <summary>Ou se pose une fenetre detachee : coin haut-gauche et taille,
/// en pixels d'ecran.</summary>
public sealed class PoseFenetre
{
    public double Gauche { get; set; }
    public double Haut { get; set; }
    public double Largeur { get; set; }
    public double Hauteur { get; set; }
}

/// <summary>Une ouverture d affaire, horodatee a l instant du clic.</summary>
public sealed class ConsultationAffaire
{
    public string Numero { get; set; } = string.Empty;
    public string Objet { get; set; } = string.Empty;
    public string Siren { get; set; } = string.Empty;
    public string Type { get; set; } = "RCS";
    public string Statut { get; set; } = "neutre";
    public DateTime Quand { get; set; }

    /// <summary>« il y a 4 min », « hier 15:20 » — lu, pas calcule a l ecran.</summary>
    public string Depuis
    {
        get
        {
            var ecart = DateTime.Now - Quand;
            if (ecart.TotalMinutes < 1) return "a l instant";
            if (ecart.TotalMinutes < 60) return $"il y a {(int)ecart.TotalMinutes} min";
            if (ecart.TotalHours < 24) return $"il y a {(int)ecart.TotalHours} h";
            if (ecart.TotalDays < 2) return $"hier {Quand:HH:mm}";
            return Quand.ToString("dd/MM/yyyy HH:mm");
        }
    }
}

/// <summary>Un dossier RCS ouvert depuis l ecran « Ouvrir un dossier ».</summary>
public sealed class DossierOuvert
{
    public string Numero { get; set; } = string.Empty;
    public string Siren { get; set; } = string.Empty;
    public string Denomination { get; set; } = string.Empty;
    public string Forme { get; set; } = string.Empty;
    public DateTime Immatriculation { get; set; }
    public decimal Capital { get; set; }
    public string Monnaie { get; set; } = "EUR";
    public string Origine { get; set; } = string.Empty;
    public string Statut { get; set; } = "action";

    /// <summary>Le libelle sous lequel l affaire apparait dans la recherche.</summary>
    public string Objet => "Immatriculation - " + Denomination;
}

/// <summary>
/// Ce que l'accueil doit retenir d'une session a l'autre : ou sont posees
/// les tuiles, lesquelles sont allumees, et quelles alertes sont ecoutees.
/// Un fichier JSON dans le profil utilisateur suffit : rien ici n'a besoin
/// de la base.
/// </summary>
public sealed class AccueilEtat
{
    /// <summary>Les dossiers RCS ouverts pendant les sessions precedentes, du
    /// plus recent au plus ancien.</summary>
    public List<DossierOuvert> DossiersOuverts { get; set; } = new();

    /// <summary>
    /// Le mode d'affichage choisi, conserve d'une session a l'autre : le
    /// greffier qui travaille en clair ne doit pas rebasculer a chaque
    /// ouverture. Demande du 17/09/2026 : le CLAIR est le mode par defaut,
    /// donc false est bien la valeur d'un profil neuf.
    /// </summary>
    public bool ThemeSombre { get; set; }

    public Dictionary<string, PoseTuile> Tuiles { get; set; } = new();

    /// <summary>
    /// Version de la DISPOSITION par defaut des tuiles. Sans elle, un
    /// accueil.json deja ecrit fige a jamais l'ancienne grille : une tuile
    /// retiree ou une disposition retassee ne serait jamais visible par les
    /// postes qui ont deja ouvert l'application. Quand le code change la
    /// disposition d'origine, il monte ce numero et la grille est reposee
    /// (ce qui est allume ou eteint, lui, n'est pas touche).
    /// </summary>
    public int VersionDisposition { get; set; }

    /// <summary>Disposition preparee choisie en dernier (« bandeau », « lateral »,
    /// « metiers ») : le bouton de reinitialisation y revient.</summary>
    public string Disposition { get; set; } = "lateral";

    public Dictionary<string, bool> Alertes { get; set; } = new();

    /// <summary>Les CATEGORIES d'alertes surveillees (« Arrivees », « DCA »...).
    /// Eteindre une categorie retire d'un coup toutes ses alertes de la tuile
    /// « A traiter » : le greffier qui ne tient pas les comptes annuels ne
    /// decoche pas ses cinq lignes une par une. Absent = surveille.</summary>
    public Dictionary<string, bool> CategoriesAlertes { get; set; } = new();

    /// <summary>Les categories REPLIEES de la tuile « A traiter » : le titre
    /// reste visible avec son compte, les lignes sont masquees. Different de
    /// <see cref="CategoriesAlertes"/>, qui retire la categorie de la
    /// surveillance ; ici on la garde, on la range. Absent = depliee.</summary>
    public Dictionary<string, bool> CategoriesRepliees { get; set; } = new();

    /// <summary>Ce que la tuile Alertes montre : gravite minimale, ordre,
    /// nombre de lignes, detail replie ou non. Conserve d une session a
    /// l autre comme le reste de l accueil.</summary>
    public ReglagesAlertes ReglagesAlertes { get; set; } = new();

    /// <summary>
    /// Ou se posent les fenetres detachees du document, par RANG d'ouverture
    /// (« 1 », « 2 »...). Demande du 14/09/2026 : pour qui travaille sur
    /// plusieurs ecrans, la fenetre du document doit revenir TOUJOURS au meme
    /// endroit. La clef est le rang et non la piece : la 1re fenetre ouverte
    /// retrouve sa place meme pour un document jamais vu.
    /// </summary>
    public Dictionary<string, PoseFenetre> FenetresDocument { get; set; } = new();

    /// <summary>
    /// L AGENCEMENT des onglets de vue : les codes ouverts, DANS L ORDRE de la
    /// barre. Demande du 17/09/2026 (« que ca se rappelle de mon agencement
    /// global »). Initialise non-nul pour qu un accueil.json ecrit avant cette
    /// version se charge sans erreur.
    /// </summary>
    public List<string> OngletsVue { get; set; } = new();

    /// <summary>Le code de la vue montree au moment de quitter, ou vide quand
    /// on etait revenu a l accueil. Les onglets restent ouverts dans les deux
    /// cas : c est l onglet ACTIF qui est retenu ici.</summary>
    public string OngletActif { get; set; } = string.Empty;

    /// <summary>Les vues SORTIES dans leur propre fenetre au moment de quitter :
    /// elles se rouvrent en fenetre au demarrage suivant, chacune a sa place.</summary>
    public List<string> VuesDetachees { get; set; } = new();

    /// <summary>Ou se pose la fenetre de CHAQUE vue sortie, par CODE d ecran
    /// (« PROC_XAFF ») et non par rang : l agencement doit revenir vue par vue,
    /// pas selon l ordre dans lequel on a arrache les onglets.</summary>
    public Dictionary<string, PoseFenetre> FenetresVue { get; set; } = new();

    /// <summary>Au-dela de ce nombre d onglets l agencement n est plus retenu :
    /// sans plafond le fichier de reglages grossirait sans fin.</summary>
    public const int PlafondOnglets = 20;

    /// <summary>Retient la barre d onglets telle qu elle est a l ecran.</summary>
    public static void MemoriserOnglets(IEnumerable<string> codes, string actif,
        IEnumerable<string> detachees)
    {
        var etat = Charger();
        etat.OngletsVue = codes.Where(c => !string.IsNullOrWhiteSpace(c))
                               .Take(PlafondOnglets).ToList();
        etat.OngletActif = actif ?? string.Empty;
        etat.VuesDetachees = detachees.Where(c => !string.IsNullOrWhiteSpace(c))
                                      .Take(PlafondOnglets).ToList();
        etat.Enregistrer();
    }

    /// <summary>La place memorisee pour la fenetre de cette vue, ou null.</summary>
    public static PoseFenetre? PoseVue(string code)
        => Charger().FenetresVue.TryGetValue(code ?? string.Empty, out var p) ? p : null;

    /// <summary>Retient ou la fenetre de cette vue a ete laissee.</summary>
    public static void MemoriserFenetreVue(string code, PoseFenetre pose)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var etat = Charger();
        if (etat.FenetresVue.Count >= PlafondOnglets && !etat.FenetresVue.ContainsKey(code)) return;
        etat.FenetresVue[code] = pose;
        etat.Enregistrer();
    }

    /// <summary>La pose memorisee pour ce rang, ou null la premiere fois.</summary>
    public static PoseFenetre? PoseDocument(int rang)
        => Charger().FenetresDocument.TryGetValue(rang.ToString(), out var p) ? p : null;

    /// <summary>Au-dela de ce rang la place n'est plus retenue : quatre
    /// documents sortis en meme temps est deja beaucoup, et sans plafond le
    /// fichier de reglages grossirait sans fin.</summary>
    public const int PlafondFenetresDocument = 4;

    /// <summary>Retient ou la fenetre de ce rang a ete laissee.</summary>
    public static void MemoriserDocument(int rang, PoseFenetre pose)
    {
        if (rang < 1 || rang > PlafondFenetresDocument) return;
        var etat = Charger();
        etat.FenetresDocument[rang.ToString()] = pose;
        etat.Enregistrer();
    }

    /// <summary>
    /// Une pose retenue est-elle encore utilisable ? Non si elle est
    /// minuscule, non si son CENTRE ne tombe sur aucune zone de travail
    /// presente (portable debranche du dock) : la fenetre s'ouvrirait hors de
    /// vue. Fonction pure, sans WPF, pour etre verifiable a l'execution.
    /// </summary>
    public static bool PoseUtilisable(PoseFenetre? pose,
        IEnumerable<(double X, double Y, double Largeur, double Hauteur)> zones)
    {
        if (pose is null || pose.Largeur < 200 || pose.Hauteur < 150) return false;
        var cx = pose.Gauche + pose.Largeur / 2;
        var cy = pose.Haut + pose.Hauteur / 2;
        foreach (var z in zones)
            if (cx >= z.X && cx <= z.X + z.Largeur && cy >= z.Y && cy <= z.Y + z.Hauteur)
                return true;
        return false;
    }

    /// <summary>
    /// Les ecrans mis en FAVORI depuis le menu de gauche, par code
    /// (« PROC_XAFF »), dans l ordre ou ils ont ete etoiles. Le code et non
    /// le libelle : le libelle peut changer, le favori doit suivre. Un
    /// accueil.json ecrit avant cette version n a pas le champ : la liste
    /// part alors vide, ce qui est l etat d un profil neuf.
    /// </summary>
    // fix-ok: cause mesuree — l'etoile ne survivait pas au redemarrage parce que
    // accueil.json n'avait aucun champ pour la porter. Corrige a la source :
    // propriete publique serialisee (initialisee non-nulle pour qu'un accueil.json
    // ecrit avant cette version se charge sans erreur) + Enregistrer() dans
    // BasculerFavori. Prouve par tests/FavorisCheck (rouge sans l'enregistrement).
    public List<string> VuesFavorites { get; set; } = new();

    /// <summary>Cet ecran est-il en favori ?</summary>
    /// <summary>Signale aux autres fenetres que la liste des favoris a change
    /// (emetteur, nouvelle liste).</summary>
    public static event Action<AccueilEtat, List<string>>? FavorisChanges;

    public bool EstFavori(string code) => VuesFavorites.Contains(code);

    /// <summary>Allume ou eteint l etoile d un ecran, et retient le choix.
    /// Rend l etat APRES le geste (true = desormais favori).</summary>
    public bool BasculerFavori(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var desormais = !VuesFavorites.Remove(code);
        if (desormais) VuesFavorites.Add(code);
        Enregistrer();
        // Chaque fenetre (principale ou vue detachee) a SA copie de l etat :
        // sans ce signal, l etoile cliquee dans une vue detachee n apparaissait
        // pas dans le menu Favoris de la fenetre principale.
        FavorisChanges?.Invoke(this, VuesFavorites.ToList());
        return desormais;
    }

    /// <summary>Mesures du widget performance retenues, par libelle.</summary>
    public Dictionary<string, bool> Mesures { get; set; } = new();

    /// <summary>
    /// Saisie guidee conservee d une session a l autre : par affaire, puis
    /// par etape, puis une LISTE de fiches — une etape comme Dirigeants ou
    /// Etablissements en accepte plusieurs.
    /// </summary>
    public Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>> Saisies { get; set; } = new();

    /// <summary>Affaires dont la saisie a passe le controle final.</summary>
    public HashSet<string> Valides { get; set; } = new();

    /// <summary>
    /// Historique de consultation : les affaires REELLEMENT ouvertes par
    /// l utilisateur, la plus recente en tete. RIG v2 n a pas cette
    /// information (la table AFFAIRE ne porte qu une date de creation) :
    /// elle est donc produite ici, a l instant du clic, et conservee dans
    /// accueil.json.
    /// </summary>
    public List<ConsultationAffaire> Consultations { get; set; } = new();

    /// <summary>Nombre d affaires retenues dans l historique.</summary>
    public const int PlafondConsultations = 20;

    /// <summary>
    /// Fait passer une tuile DEVANT toutes les autres. Appele quand on la
    /// deplace, la redimensionne ou l'allume : le geste le plus recent
    /// gagne. Les rangs sont renumerotes a partir de 1 pour qu'ils ne
    /// grimpent pas indefiniment dans le fichier.
    /// </summary>
    public void Avancer(string id)
    {
        if (!Tuiles.TryGetValue(id, out var cible)) return;
        var rang = 1;
        foreach (var pose in Tuiles.Where(t => t.Key != id)
                                   .OrderBy(t => t.Value.Ordre)
                                   .Select(t => t.Value))
            pose.Ordre = rang++;
        cible.Ordre = rang;
    }

    /// <summary>
    /// Note qu une affaire vient d etre ouverte. Une affaire deja presente
    /// remonte en tete au lieu d etre dupliquee, et l historique est borne :
    /// sans cela le fichier grossirait sans fin.
    /// </summary>
    public void Consulter(string numero, string objet, string siren, string type, string statut)
    {
        if (string.IsNullOrWhiteSpace(numero)) return;
        Consultations.RemoveAll(c => c.Numero == numero);
        Consultations.Insert(0, new ConsultationAffaire
        {
            Numero = numero, Objet = objet, Siren = siren,
            Type = type, Statut = statut, Quand = DateTime.Now
        });
        if (Consultations.Count > PlafondConsultations)
            Consultations.RemoveRange(PlafondConsultations,
                Consultations.Count - PlafondConsultations);
        Enregistrer();
    }

    /// <summary>
    /// Journal VGED par affaire : la commande « Historique VGED » de RIG v2
    /// (ETP_VGED/OPE_VGED.cs) affiche les evenements du dossier. Ici les
    /// lignes sont HORODATEES a l instant de l action reelle, jamais
    /// reconstituees a l affichage.
    /// </summary>
    public Dictionary<string, List<string>> Journal { get; set; } = new();

    /// <summary>
    /// Ajoute une ligne horodatee au journal d une affaire. Une ligne
    /// identique a la precedente n est pas repetee : sinon chaque frappe
    /// dans un champ produirait un evenement et le journal deviendrait
    /// illisible.
    /// </summary>
    public void Journaliser(string affaire, string evenement)
    {
        if (string.IsNullOrWhiteSpace(affaire)) return;
        if (!Journal.TryGetValue(affaire, out var lignes))
        {
            lignes = new List<string>();
            Journal[affaire] = lignes;
        }
        var ligne = $"{DateTime.Now:dd/MM/yyyy HH:mm} · {evenement}";
        if (lignes.Count > 0 && lignes[^1].EndsWith(evenement, StringComparison.Ordinal)) return;
        lignes.Add(ligne);
        Enregistrer();
    }

    /// <summary>La trace des valeurs CHANGEES, affaire par affaire.</summary>
    public Dictionary<string, List<ModificationChamp>> Modifications { get; set; } = new();

    /// <summary>Note une valeur changee. Une valeur identique a la precedente
    /// n entre pas dans la trace : ce n est pas une modification.</summary>
    public void NoterModification(string affaire, ModificationChamp m)
    {
        if (string.IsNullOrWhiteSpace(affaire) || m.Avant == m.Apres) return;
        if (!Modifications.TryGetValue(affaire, out var liste))
        {
            liste = new List<ModificationChamp>();
            Modifications[affaire] = liste;
        }
        liste.Add(m);
        Enregistrer();
    }

    /// <summary>Les taches de recette terminees, par « code/affaire ».</summary>
    public Dictionary<string, TacheClose> TachesFaites { get; set; } = new();

    /// <summary>Les effets d une tache (courrier, publication...) deja
    /// traites, par canal.</summary>
    public Dictionary<string, List<string>> EffetsTraites { get; set; } = new();

    /// <summary>Cet effet a-t-il deja ete traite sur cette tache ?</summary>
    public bool EffetTraite(string code, string affaire, string canal)
        => EffetsTraites.TryGetValue(TacheClose.CleDe(code, affaire), out var l) && l.Contains(canal);

    /// <summary>Coche ou decoche un effet, et retient le geste.</summary>
    public void BasculerEffet(string code, string affaire, string canal)
    {
        var cle = TacheClose.CleDe(code, affaire);
        if (!EffetsTraites.TryGetValue(cle, out var liste))
        {
            liste = new List<string>();
            EffetsTraites[cle] = liste;
        }
        if (!liste.Remove(canal)) liste.Add(canal);
        Enregistrer();
    }

    /// <summary>Le nombre de lignes ouvertes d une tache, par « code/affaire ».</summary>
    public Dictionary<string, int> LignesTache { get; set; } = new();

    /// <summary>Le nombre de lignes ouvertes, au moins une.</summary>
    public int NbLignesTache(string code, string affaire)
        => LignesTache.TryGetValue(TacheClose.CleDe(code, affaire), out var n) && n > 1 ? n : 1;

    /// <summary>Ouvre une ligne de plus. Le geste se defait avec RetirerLigneTache.</summary>
    public int AjouterLigneTache(string code, string affaire)
    {
        var cle = TacheClose.CleDe(code, affaire);
        var n = NbLignesTache(code, affaire) + 1;
        LignesTache[cle] = n;
        Enregistrer();
        return n;
    }

    /// <summary>
    /// Retire la DERNIERE ligne. Ce qui y a ete ecrit reste dans la trace :
    /// on ne reecrit pas l histoire, la ligne cesse seulement d etre exigee.
    /// </summary>
    public int RetirerLigneTache(string code, string affaire)
    {
        var cle = TacheClose.CleDe(code, affaire);
        var n = Math.Max(1, NbLignesTache(code, affaire) - 1);
        LignesTache[cle] = n;
        Enregistrer();
        return n;
    }

    /// <summary>Clot une tache : qui, quoi, quand. Rien n est efface.</summary>
    public void CloreTache(string code, string affaire, string auteur)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(affaire)) return;
        TachesFaites[TacheClose.CleDe(code, affaire)] = new TacheClose
        {
            Code = code, Affaire = affaire, Auteur = auteur, Quand = DateTime.Now
        };
        Enregistrer();
    }

    /// <summary>Vrai si cette tache a deja ete terminee sur cette affaire.</summary>
    public bool TacheFaite(string code, string affaire)
        => TachesFaites.ContainsKey(TacheClose.CleDe(code, affaire));

    /// <summary>
    /// La derniere valeur ECRITE dans un champ d une affaire (cle
    /// « TABLE.COLONNE »), lue dans la trace des modifications. C est ce qui
    /// sert de PREUVE de completude d une tache : la tache se clot sur ce
    /// qui a ete reellement ecrit, pas sur ce que l ecran affiche.
    /// </summary>
    public string? DerniereValeur(string affaire, string cleChamp)
    {
        if (!Modifications.TryGetValue(affaire, out var liste)) return null;
        for (var i = liste.Count - 1; i >= 0; i--)
            if (liste[i].Colonne == cleChamp) return liste[i].Apres;
        return null;
    }

    /// <summary>
    /// Ce que l utilisateur a change dans l arbre GED, par affaire :
    /// elements crees, masques, renommes. Sans cela ces gestes
    /// disparaissaient a la fermeture (ils ne vivaient qu en memoire de
    /// l ecran, indexes par reference d objet).
    /// </summary>
    public Dictionary<string, ArbreGedEnregistre> ArbresGed { get; set; } = new();

    /// <summary>L arbre retenu pour cette affaire, ou null.</summary>
    public ArbreGedEnregistre? ArbreGedDe(string affaire)
        => ArbresGed.TryGetValue(affaire, out var a) ? a : null;

    /// <summary>Retient l arbre d une affaire. Un arbre revenu a l etat
    /// d origine est RETIRE plutot qu ecrit vide.</summary>
    public void EnregistrerArbreGed(string affaire, ArbreGedEnregistre arbre)
    {
        if (string.IsNullOrWhiteSpace(affaire)) return;
        if (arbre.Vide()) ArbresGed.Remove(affaire);
        else ArbresGed[affaire] = arbre;
        Enregistrer();
    }

    // RIGV3_ETAT : chemin de secours du fichier de reglages. Sert aux
    // controles internes, qui ne doivent PAS ecrire dans l'etat reel de
    // l'utilisateur. Vide en usage normal.
    private static string Chemin =>
        Environment.GetEnvironmentVariable("RIGV3_ETAT") is { Length: > 0 } surMesure
            ? surMesure
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RigV3", "accueil.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AccueilEtat Charger()
    {
        try
        {
            if (File.Exists(Chemin))
            {
                var lu = JsonSerializer.Deserialize<AccueilEtat>(File.ReadAllText(Chemin));
                if (lu is not null) return lu;
            }
        }
        catch (Exception)
        {
            // Un etat illisible ne doit jamais empecher l'accueil de s'ouvrir :
            // on repart de la disposition par defaut.
        }
        return new AccueilEtat();
    }

    public void Enregistrer()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Chemin)!);
            File.WriteAllText(Chemin, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            // Idem : ne pas faire tomber l'interface pour un disque en lecture seule.
        }
    }
}
