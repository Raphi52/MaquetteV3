using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace RigV3Desktop.Pont.EcransGeneres;

// ============================================================
// LE DESCRIPTEUR D'UN ECRAN GENERIQUE (plan « les 125 ecrans manquants du menu, au
// style Commandes web », lot 1, 23/11/2026).
//
// Une procedure du menu qui n'a pas son ecran a part est decrite ICI, en donnees :
// web/src/ecrans/EcranGenere.tsx la met en page, et la branche par defaut de
// web/src/plan/Aiguillage.tsx la lui passe. Aucun ecran React par procedure. La vue de
// procedure (Pont/PlanTravailVue.cs) le porte.
//
// LA FORME (plan « RIG v3 100 % fonctionnel », lot 1, 25/11/2026). Jusque-la, toutes les
// procedures etaient mises en page en ESCALIER (tuiles, etapes « Terminer l'etape »,
// Valider n/m) : une recherche, une table de reglage ou un journal aussi -- Raphael, sur
// « Corriger une affaire » : absurde pour corriger une fiche. Chaque descripteur porte
// desormais sa FORME (FormeEcran), choisie d'apres le role de sa fiche RIG v2 ; l'ecran
// a un composant par forme (web/src/ecrans/formes/), EcranGenere.tsx n'est plus que
// l'aiguilleur. Seule la forme Saisie se fait en etapes (Marches) ; les autres posent leurs
// parties en SECTIONS (meme record, sans ordre a franchir). Operation enchaine ses trois
// temps fixes (choisir, voir les consequences, confirmer) sur ses sections.
//
// Une procedure = un descripteur, range dans le fichier de SON verbe du menu
// (Pont/EcransGeneres/Ouvrir.cs...). CatalogueGenere le trouve par reflexion : aucune
// liste partagee a editer.
//
// Le contenu est FONDE sur la fiche RIG v2 de la procedure (proc_<code>.md, « Detail
// OPE » faisant foi) : les etapes ou sections sont ses parties, les consignes ses regles,
// les champs ceux qu'elle nomme. Les lignes sont une demonstration ANONYME (noms fictifs),
// jamais de donnees reelles. Branche sur le serveur, l'ecran les remplace par celles de
// la base du greffe quand la procedure a un lecteur (Serveur/Socle/EcransGeneres/
// LecteurEcran.cs, pont : Pont/EcranGenere.cs) ; sinon il les garde, marquees « exemple ».
//
// Conventions que l'ecran et le banc tests/EcransGeneresCheck tiennent :
//   - trois etats portent les gestes : AFaire, EnCours, Validee (EtatsEcran.Trois) ;
//     les autres etats (EnAttente, Rejetee...) sont figes : aucun geste sur la ligne ;
//   - AFaire : aucune partie franchie ; EnCours : au moins une ; Validee : toutes les
//     parties OBLIGATOIRES franchies (les parties : Marches, ou Sections sans etapes) ;
//   - le titre est le libelle du menu, tel quel ;
//   - un ECRAN REUNI (24/11/2026, conv-2142) n'est pas ecrit a la main : CatalogueGenere le
//     fabrique a partir de l'entree du menu qui reunit plusieurs procedures (MenuAccueil,
//     EntreeMenu.Reunit) et des descripteurs de ces procedures (Membres) ; il prend leur
//     forme, ou passe en onglets (EnOnglets) quand elles n'en ont pas une liste commune.
// ============================================================

/// <summary>La forme d'interface d'un ecran : ce que l'agent y FAIT. Une par descripteur, choisie
/// d'apres le role de la fiche RIG v2 de la procedure ; un composant React par forme
/// (web/src/ecrans/formes/Forme&lt;Nom&gt;.tsx).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FormeEcran>))]
public enum FormeEcran
{
    /// <summary>File de travail : liste a etats (tuiles qui filtrent), cases a cocher, actions en lot.
    /// Relances, injonctions, rejets, retours, files d'edition et d'impression, surveillances.</summary>
    File = 1,
    /// <summary>Criteres, resultats, fiche en lecture seule par sections. Les ecrans « Trouver ».</summary>
    Consultation,
    /// <summary>Retrouver un objet, le corriger dans un formulaire par sections, Enregistrer ou Annuler.</summary>
    Fiche,
    /// <summary>Formulaire de creation, en etapes aux vrais champs, puis Creer.</summary>
    Saisie,
    /// <summary>Grille editable : ajouter, modifier en ligne, supprimer, rechercher. Tables de reglage.</summary>
    Referentiel,
    /// <summary>Parametres (periode...), tableau de resultats et totaux, Imprimer ou Exporter.</summary>
    Etat,
    /// <summary>Choisir une audience, les affaires au role, un geste par affaire, imprimer le role.</summary>
    Audience,
    /// <summary>Visionneuse : PDF, image, pages a pivoter, page web, aide.</summary>
    Document,
    /// <summary>Geste ponctuel sur un ou deux objets : choisir, voir les consequences, confirmer.</summary>
    Operation,
}

/// <summary>Le type d'un champ : ce que l'ecran pose pour le saisir ou l'afficher.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TypeChamp>))]
public enum TypeChamp
{
    /// <summary>Une ligne de texte.</summary>
    Texte,
    /// <summary>Un texte long (note, motif, commentaire, mention).</summary>
    Zone,
    Date,
    /// <summary>Deux dates : du, au.</summary>
    Periode,
    Nombre,
    Montant,
    /// <summary>Une case a cocher (oui / non).</summary>
    Case,
    /// <summary>Un choix parmi <see cref="ChampEcran.Options"/> (les valeurs citees par la fiche).</summary>
    Liste,
    /// <summary>Une sous-liste (les parties, les evenements...) : lue avec l'objet, pas une saisie d'une ligne.</summary>
    Tableau,
}

/// <summary>Un champ nomme par la fiche RIG v2 : son libelle, son type, et les valeurs d'une liste.
/// Un libelle seul (« Nom courant ») se convertit en champ : son type se deduit du libelle par
/// <see cref="Deviner"/> ; les cases, listes et sous-listes se declarent par <see cref="Champ"/>.</summary>
public sealed record ChampEcran(string Libelle, TypeChamp Type = TypeChamp.Texte)
{
    /// <summary>Les valeurs d'une <see cref="TypeChamp.Liste"/>, dans l'ordre de la fiche ; vide sinon.</summary>
    public string[] Options { get; init; } = [];

    /// <summary>Un champ que la fiche RIG v2 ne MONTRE que quand une case de SA section est cochee : le libelle de cette case ;
    /// null sinon. La case decochee, le champ disparait et se vide, comme dans la V2. PROC_GTABREF, un etat de PC : « Résolution du
    /// plan » sous « Ouverture », « Code clôture » sous « Résolution du plan » (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:135-156).</summary>
    public string? VisibleSi { get; init; }

    /// <summary>Un champ que la fiche RIG v2 BLOQUE (montre sans le laisser changer) quand une case de SA section est cochee : le
    /// libelle de cette case ; null sinon. PROC_GTABREF : « À sauver » sous « Mise à jour de la PC » (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:116-119).</summary>
    // fix-ok: controle final du lot 14 (27/11/2026, 4e reprise) -- la grille montrait et laissait saisir ces trois champs en toutes
    // circonstances, faute de toute regle d'affichage dans le descripteur ; ReferentielParGroupe.test.tsx est rouge (3 tests) sans elles.
    public string? BloqueSi { get; init; }

    public static implicit operator ChampEcran(string libelle) => new(libelle, Deviner(libelle));

    /// <summary>Le type qu'annonce le libelle de la fiche, UNE regle pour tous les ecrans : « Date de fin » est une
    /// date, « Période » deux dates, « Montant TTC » un montant, « Nombre d'exemplaires » un nombre, « Note sur
    /// l'instance » un texte long. Tout autre libelle est un texte : une case, une liste ou une sous-liste ne se
    /// devinent pas, elles se declarent (<see cref="Champ"/>) d'apres ce que la fiche en dit.</summary>
    public static TypeChamp Deviner(string libelle)
    {
        var mot = SansAccents(libelle).ToLowerInvariant().Trim();
        if (mot.StartsWith("periode", StringComparison.Ordinal)) return TypeChamp.Periode;
        if (mot.StartsWith("date", StringComparison.Ordinal) || mot.StartsWith("echeance", StringComparison.Ordinal))
            return TypeChamp.Date;
        if (mot.StartsWith("montant", StringComparison.Ordinal) || mot.StartsWith("solde", StringComparison.Ordinal)
            || mot.StartsWith("total", StringComparison.Ordinal))
            return TypeChamp.Montant;
        if (mot.StartsWith("nombre", StringComparison.Ordinal)) return TypeChamp.Nombre;
        if (mot.StartsWith("note", StringComparison.Ordinal) || mot.StartsWith("commentaire", StringComparison.Ordinal)
            || mot.StartsWith("motif", StringComparison.Ordinal) || mot.StartsWith("observation", StringComparison.Ordinal))
            return TypeChamp.Zone;
        return TypeChamp.Texte;
    }

    private static string SansAccents(string texte)
    {
        var decompose = texte.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>Les champs qu'un libelle ne dit pas : declares d'apres la fiche (une case qu'elle dit cochee, une liste
/// dont elle cite les valeurs, une sous-liste de l'objet). Et les types qu'on force quand le libelle ne les annonce pas.</summary>
public static class Champ
{
    public static ChampEcran Texte(string libelle) => new(libelle, TypeChamp.Texte);
    public static ChampEcran Zone(string libelle) => new(libelle, TypeChamp.Zone);
    public static ChampEcran Date(string libelle) => new(libelle, TypeChamp.Date);
    public static ChampEcran Periode(string libelle) => new(libelle, TypeChamp.Periode);
    public static ChampEcran Nombre(string libelle) => new(libelle, TypeChamp.Nombre);
    public static ChampEcran Montant(string libelle) => new(libelle, TypeChamp.Montant);
    public static ChampEcran Case(string libelle) => new(libelle, TypeChamp.Case);
    public static ChampEcran Liste(string libelle, params string[] options) => new(libelle, TypeChamp.Liste) { Options = options };
    public static ChampEcran Tableau(string libelle) => new(libelle, TypeChamp.Tableau);
}

/// <summary>Un ecran generique : ce que la mise en page affiche pour une procedure.</summary>
public sealed record DescripteurEcran
{
    /// <summary>Le code de procedure (PROC_XAFF) : cle de l'aiguillage, jamais affichee.</summary>
    public required string Code { get; init; }

    /// <summary>Le nom de l'ecran : le libelle du menu, tel quel.</summary>
    public required string Titre { get; init; }

    /// <summary>La forme d'interface de l'ecran (voir <see cref="FormeEcran"/>). Un ecran reuni EN ONGLETS
    /// (<see cref="EnOnglets"/>) porte celle de son premier onglet, celui qui s'ouvre.</summary>
    public required FormeEcran Forme { get; init; }

    /// <summary>Ce qu'il traite, au pluriel (« instances judiciaires »). Le compte s'en sert.</summary>
    public required string Sujet { get; init; }

    /// <summary>Le texte vide du champ de recherche.</summary>
    public required string Recherche { get; init; }

    /// <summary>Les criteres de la fiche RIG v2 : le panneau de recherche (Consultation, Fiche, Operation).</summary>
    public required string[] Criteres { get; init; }

    /// <summary>Le libelle du bouton RIG v2 qui conclut (le geste en lot d'une File quand elle n'en nomme pas
    /// d'autre, la confirmation d'une Operation). Une Fiche enregistre, une Saisie cree.</summary>
    public required string Valider { get; init; }

    /// <summary>La teinte du verbe du menu : la classe rig-tcw__rang-N de rig-accueil.css.
    /// 1 = Ouvrir (seules les teintes 1, 4 et 5 ont une classe aujourd'hui).</summary>
    public int Rang { get; init; } = 1;

    /// <summary>Les tuiles chiffrees, une par etat, dans l'ordre d'affichage.</summary>
    public required EtatEcran[] Etats { get; init; }

    /// <summary>Les groupes des lignes (le type de ligne), dans l'ordre d'affichage.</summary>
    public required string[] Groupes { get; init; }

    /// <summary>Les ETAPES de la fiche, a franchir dans l'ordre : la forme Saisie seule (un enchainement de sections
    /// est legitime pour creer). Vide pour toute autre forme.</summary>
    public MarcheEcran[] Marches { get; init; } = [];

    /// <summary>Les SECTIONS : les parties de l'objet, sans ordre a franchir (une fiche en lecture, un formulaire, les
    /// regles d'une file, les consequences d'une operation). Vide pour une Saisie, qui les tient en etapes.</summary>
    public MarcheEcran[] Sections { get; init; } = [];

    /// <summary>Les colonnes : la grille d'un Referentiel, le tableau de resultats d'un Etat (un montant ou un nombre
    /// y est totalise).</summary>
    public ChampEcran[] Colonnes { get; init; } = [];

    /// <summary>Les parametres saisis avant de lancer : la periode d'un Etat, la date et la chambre d'une Audience.</summary>
    public ChampEcran[] Parametres { get; init; } = [];

    /// <summary>Les gestes nommes par la fiche : actions en lot d'une File, gestes par affaire d'une Audience, outils
    /// d'un Document, d'un Etat ou d'une Consultation, boutons de confirmation d'une Operation (« Générer » ou « Supprimer le
    /// bordereau »), gestes du menu de ligne d'un Referentiel en plus de Modifier et Supprimer (« Dupliquer »). Vide : le
    /// bouton <see cref="Valider"/> seul.</summary>
    public string[] Gestes { get; init; } = [];

    /// <summary>Les lignes de demonstration (6 a 15 ; un ecran reuni porte celles de ses membres).</summary>
    public required LigneEcran[] Lignes { get; init; }

    /// <summary>Un ECRAN REUNI (entree du menu qui reunit plusieurs procedures) : les
    /// descripteurs de ses procedures, un groupe chacun, titre = groupe. Chaque ligne suit les
    /// parties et le bouton de SA procedure ; <see cref="Marches"/> et <see cref="Sections"/> restent vides.
    /// Vide pour l'ecran d'une seule procedure. Fabrique par <see cref="CatalogueGenere"/>.</summary>
    public DescripteurEcran[] Membres { get; init; } = [];

    /// <summary>Un ecran reuni dont les procedures n'ont pas une liste commune (formes differentes, ou une forme qui a
    /// ses propres colonnes, parametres ou champs) : un onglet par procedure, chacun dans SA forme.</summary>
    public bool EnOnglets { get; init; }

    /// <summary>Une <see cref="FormeEcran.Operation"/> qui vise UN seul objet, celui que la fiche RIG v2 RECOIT en
    /// parametre d'un autre ecran (PROC_IA : le numero de la liasse affichee par la visionneuse de ses pieces,
    /// OPE_DOC_DEMAT.cs:818). Aucune liste ou le choisir : il se designe par son numero, dans le seul critere de
    /// l'ecran. Faux : l'operation se choisit dans une liste et peut viser plusieurs objets (les filles d'une
    /// jonction).</summary>
    public bool ObjetUnique { get; init; }

    /// <summary>Un <see cref="FormeEcran.Referentiel"/> dont la fiche RIG v2 AJOUTE une ligne (vrai par defaut). Faux : la
    /// grille n'offre pas « Ajouter » -- PROC_GESTDATE ne montre que des blocs de dates existantes (ETP_CATEGORIE.cs:10-12).
    /// Un geste sans equivalent V2 ne s'invente pas (plan « RIG v3 100 % fonctionnel », lot 14).</summary>
    public bool Ajout { get; init; } = true;

    /// <summary>Un <see cref="FormeEcran.Referentiel"/> dont la fiche RIG v2 SUPPRIME une ligne (vrai par defaut). Faux : le
    /// menu de ligne n'offre pas « Supprimer » -- PROC_GESTDATE et PROC_CABINET n'ont aucune suppression.</summary>
    public bool Suppression { get; init; } = true;

    /// <summary>Un <see cref="FormeEcran.Referentiel"/> dont la fiche RIG v2 DUPLIQUE un enregistrement : le menu de ligne offre
    /// « Dupliquer », qui ouvre une ligne NEUVE pre-remplie des cellules de la ligne copiee, sauf la premiere (la cle, que la
    /// V2 ne copie pas : ChampsNonDuplicables) ; « Enregistrer » l'envoie en <c>Ajouter</c>, Ids = [la ligne copiee], et
    /// l'ecrivain recopie le reste. PROC_GTABREF : OPE_TABLE_REF_BUTTONS_BAR._Dupliquer (l. 335, 355-370), puis SetNewMetierBase (l. 247-249) et
    /// SQLBase_Handler.Copie. Faux par defaut. A distinguer d'un geste de ligne nomme dans <see cref="Gestes"/> (« Dupliquer »
    /// un document ocerise, lot 11), qui part tel quel a l'ecrivain sans rien saisir.</summary>
    public bool Duplication { get; init; }

    /// <summary>Un <see cref="FormeEcran.Referentiel"/> : par groupe, les titres des <see cref="Sections"/> dont les champs se
    /// SAISISSENT sous une ligne neuve de ce groupe (« Ajouter » dans son onglet, ou « Dupliquer » une de ses lignes), envoyes
    /// sous la cle « Section › Libelle ». PROC_GTABREF : une zone se cree avec ses champs obligatoires, que la grille ne porte
    /// pas (OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.Designer.cs:78-96, SousOPE_ENREGISTREMENT.Designer.cs:81-111,
    /// SousOPE_EDITION.Designer.cs:83-92). Vide : une ligne neuve n'a que ses colonnes.</summary>
    public IReadOnlyDictionary<string, string[]> SectionsAjout { get; init; } = new Dictionary<string, string[]>();

    /// <summary>Un <see cref="FormeEcran.Referentiel"/> dont les lignes n'ont pas toutes le meme formulaire RIG v2 : par groupe,
    /// les titres des <see cref="Sections"/> d'une ligne de ce groupe. Sous une ligne ouverte en modification, la grille ne montre
    /// QUE celles-la, et y fait saisir les champs que la fiche lue dit modifiables (hors colonnes, que la ligne porte deja) ;
    /// Enregistrer envoie ceux qui changent sous la cle « Section › Libelle ». PROC_GTABREF : un etat de PC, une zone et un type
    /// de depot ont chacun leur formulaire (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:64-82, _PC_ETAT_ZONE.cs:75-95,
    /// _DEMAT_CFEC_AJMJ_TYPE_DEPOT.cs:44-52) ; la fiche d'une zone montrait les deux autres « Non lu » (controle final du lot 14,
    /// 27/11/2026). Groupe absent : toutes les sections, en lecture, comme avant.</summary>
    public IReadOnlyDictionary<string, string[]> SectionsParGroupe { get; init; } = new Dictionary<string, string[]>();

    /// <summary>Un <see cref="FormeEcran.Referentiel"/> : les groupes dont la fiche RIG v2 ne fait que MODIFIER une ligne -- ni
    /// « Ajouter » dans leur onglet, ni « Supprimer » ni « Dupliquer » au menu de leurs lignes. PROC_GTABREF : le type de depot
    /// AJMJ, dont la V2 cache les boutons NOUVEAU, SUPPRIMER et DUPLIQUER (OPE_AFFICHE_REF_CFEC_AJMJ_TYPE_DEPOT.cs:31-33).</summary>
    public string[] GroupesModificationSeule { get; init; } = [];

    /// <summary>Le RESULTAT d'une operation sur un objet unique : les sections que le geste produit, montrees APRES lui
    /// (2e temps), et des l'ouverture d'un objet deja traite. PROC_IA : lancement puis restitution (proc_ia.md:151-156) ;
    /// une liasse deja analysee affiche son resultat sans relancer (PROC_IA.cs:60-64). Vide pour toute autre forme.</summary>
    public MarcheEcran[] Resultat { get; init; } = [];

    /// <summary>Le titre de la section que l'ecran dessine en EXTRAIT KBIS (web/src/composants/ApercuKbis.tsx) au lieu de ses
    /// champs : sa fiche lue devient l'apercu par Pont/ApercuKbisDossier.cs. PROC_XEX (edition interne d'un Kbis, lot 13 du plan
    /// « RIG v3 100 % fonctionnel ») ; null pour tout autre ecran.</summary>
    public string? ApercuKbis { get; init; }

    /// <summary>Les parties que comptent les <see cref="LigneEcran.Faites"/> d'une ligne : les etapes d'une Saisie,
    /// les sections des autres formes. Pas envoye a l'ecran (il les a deja).</summary>
    [JsonIgnore]
    public MarcheEcran[] Parties => Marches.Length > 0 ? Marches : Sections;
}

/// <summary>Une tuile : son code, son libelle et son icone de l'accueil (a-traiter,
/// saisie-manuelle, validee, en-attente, rejetee).</summary>
public sealed record EtatEcran(string Code, string Libelle, string Icone);

/// <summary>Une partie de la fiche -- etape d'une Saisie ou section des autres formes : son titre, la regle de la
/// fiche, et ses champs.</summary>
public sealed record MarcheEcran(string Titre, string Consigne, bool Obligatoire, ChampEcran[] Champs);

/// <summary>Une ligne de demonstration. <paramref name="Groupe"/> est un titre de
/// <see cref="DescripteurEcran.Groupes"/>, <paramref name="Faites"/> les rangs des parties
/// deja franchies (a partir de 0, <see cref="DescripteurEcran.Parties"/>).</summary>
public sealed record LigneEcran(string Nom, string Precisions, string Groupe, string Etat, int[] Faites)
{
    /// <summary>Les cellules de la ligne, dans l'ordre des <see cref="DescripteurEcran.Colonnes"/> (Referentiel, Etat) ;
    /// vide (toute ligne lue dans la base, LigneLue) : le nom dans la premiere colonne, ses precisions sur les autres
    /// (web/src/ecrans/formes/FormeReferentiel.tsx, CasesSansValeurs). Un montant s'ecrit en
    /// nombre invariant (« 180.00 »).</summary>
    public string[] Valeurs { get; init; } = [];

    /// <summary>L'identifiant de l'objet dans la base du greffe (LigneLue.Id, plan « RIG v3 100 % fonctionnel », lot 2) :
    /// sa fiche et ses gestes le visent (Pont/EcranGenere.cs). Null pour une ligne d'exemple : rien ne s'y ecrit.</summary>
    public string? Id { get; init; }
}

/// <summary>Un domaine = un verbe du menu, dans son fichier de Pont/EcransGeneres/.
/// Decouvert par reflexion par <see cref="CatalogueGenere"/>.</summary>
public interface IDomaineEcrans
{
    IEnumerable<DescripteurEcran> Ecrans();
}

/// <summary>Les trois etats qui portent les gestes de l'ecran, dans l'ordre des tuiles.</summary>
public static class EtatsEcran
{
    public static EtatEcran[] Trois(string aFaire, string enCours, string valide) =>
    [
        new("AFaire", aFaire, "a-traiter"),
        new("EnCours", enCours, "saisie-manuelle"),
        new("Validee", valide, "validee"),
    ];
}
