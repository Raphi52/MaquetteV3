namespace RigV3Desktop.Pont.EcransGeneres;

// ============================================================
// LE DESCRIPTEUR D'UN ECRAN GENERIQUE (plan « les 125 ecrans manquants du menu, au
// style Commandes web », lot 1, 23/11/2026).
//
// Une procedure du menu qui n'a pas son ecran a part est decrite ICI, en donnees :
// web/src/ecrans/EcranGenere.tsx la met en page dans le style « Proue » de Commandes
// web, et la branche par defaut de web/src/plan/Aiguillage.tsx la lui passe. Aucun
// ecran React par procedure. La vue de procedure (Pont/PlanTravailVue.cs) le porte.
//
// Une procedure = un descripteur, range dans le fichier de SON verbe du menu
// (Pont/EcransGeneres/Ouvrir.cs...). CatalogueGenere le trouve par reflexion : aucune
// liste partagee a editer.
//
// Le contenu est FONDE sur la fiche RIG v2 de la procedure (proc_<code>.md, « Detail
// OPE » faisant foi) : les etapes sont les marches, les regles sont les consignes, les
// controles de validation sont les messages. Les lignes sont une demonstration
// ANONYME (noms fictifs), jamais de donnees reelles.
//
// Conventions que l'ecran et le banc tests/EcransGeneresCheck tiennent :
//   - trois etats portent les gestes : AFaire, EnCours, Validee (EtatsEcran.Trois) ;
//     les autres etats (EnAttente, Rejetee...) sont figes : aucun geste sur la ligne ;
//   - AFaire : aucune marche franchie ; EnCours : au moins une ; Validee : toutes les
//     marches OBLIGATOIRES franchies ;
//   - le titre est le libelle du menu, tel quel.
// ============================================================

/// <summary>Un ecran generique : ce que la mise en page affiche pour une procedure.</summary>
public sealed record DescripteurEcran
{
    /// <summary>Le code de procedure (PROC_XAFF) : cle de l'aiguillage, jamais affichee.</summary>
    public required string Code { get; init; }

    /// <summary>Le nom de l'ecran : le libelle du menu, tel quel.</summary>
    public required string Titre { get; init; }

    /// <summary>Ce qu'il traite, au pluriel (« instances judiciaires »). Le compte s'en sert.</summary>
    public required string Sujet { get; init; }

    /// <summary>Le texte vide de la pilule de recherche.</summary>
    public required string Recherche { get; init; }

    /// <summary>Les criteres de la fiche RIG v2, affiches sous la pilule.</summary>
    public required string[] Criteres { get; init; }

    /// <summary>Le libelle du bouton qui valide la ligne ouverte.</summary>
    public required string Valider { get; init; }

    /// <summary>La teinte du verbe du menu : la classe rig-tcw__rang-N de rig-accueil.css.
    /// 1 = Ouvrir (seules les teintes 1, 4 et 5 ont une classe aujourd'hui).</summary>
    public int Rang { get; init; } = 1;

    /// <summary>Les tuiles chiffrees, une par etat, dans l'ordre d'affichage.</summary>
    public required EtatEcran[] Etats { get; init; }

    /// <summary>Les groupes de l'accordeon (le type de ligne), dans l'ordre d'affichage.</summary>
    public required string[] Groupes { get; init; }

    /// <summary>L'escalier : les etapes de la fiche, communes a toutes les lignes de l'ecran.</summary>
    public required MarcheEcran[] Marches { get; init; }

    /// <summary>Les lignes de demonstration (6 a 15).</summary>
    public required LigneEcran[] Lignes { get; init; }
}

/// <summary>Une tuile : son code, son libelle et son icone de l'accueil (a-traiter,
/// saisie-manuelle, validee, en-attente, rejetee).</summary>
public sealed record EtatEcran(string Code, string Libelle, string Icone);

/// <summary>Une marche de l'escalier : ce qui se fait, la regle de la fiche, et les champs.</summary>
public sealed record MarcheEcran(string Titre, string Consigne, bool Obligatoire, string[] Champs);

/// <summary>Une ligne de demonstration. <paramref name="Groupe"/> est un titre de
/// <see cref="DescripteurEcran.Groupes"/>, <paramref name="Faites"/> les rangs des marches
/// deja franchies (a partir de 0).</summary>
public sealed record LigneEcran(string Nom, string Precisions, string Groupe, string Etat, int[] Faites);

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
