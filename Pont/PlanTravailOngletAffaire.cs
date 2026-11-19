namespace RigV3Desktop.Pont;

// ============================================================
// L'AFFAIRE OUVERTE PREND UN ONGLET, comme les ecrans (demande du 19/11/2026) :
// « ca doit etre un onglet comme les autres qui s'appelle {le nom du dossier} ».
// Jusque-la l'affaire recouvrait tout sans onglet, et la maison la fermait.
//   - ouvrir une affaire pose (ou reprend) SON onglet dans la barre du haut,
//     au nom du dossier ; il se glisse et se ferme comme les autres ;
//   - un clic sur un autre onglet la quitte SANS la fermer ; revenir sur le
//     sien la retrouve telle qu'on l'a laissee (arbre, piece, colonnes) ;
//   - sa croix -- celle de l'onglet, celle de l'entete de l'affaire, Echap --
//     la ferme, et l'onglet voisin prend la place ;
//   - la maison ramene a l'accueil et garde l'onglet, comme pour un ecran.
// Un onglet d'affaire ne sort pas dans sa propre fenetre : FenetreVue ne
// montre qu'un ecran de procedure (DetacherOnglet ne connait que ProcsConnues).
// ============================================================
public sealed partial class SessionPlanTravail
{
    /// <summary>Le code d'onglet d'une affaire : « AFFAIRE: » puis son numero. Le prefixe
    /// ne peut pas etre pris pour un code de procedure (PROC_..., OUVERTURE_DOSSIER).</summary>
    private const string PrefixeOngletAffaire = "AFFAIRE:";

    /// <summary>L'affaire dont la vue est CHARGEE (arbre, piece choisie, colonnes) : elle
    /// survit au passage sur un autre onglet, pas a sa fermeture.</summary>
    private Dossier? AffaireChargee;

    internal static string CodeOngletAffaire(string numero) => PrefixeOngletAffaire + numero;

    /// <summary>Le numero d'affaire d'un code d'onglet ; null pour un ecran de procedure.</summary>
    internal static string? NumeroDOngletAffaire(string code)
        => code.StartsWith(PrefixeOngletAffaire, StringComparison.Ordinal) && code.Length > PrefixeOngletAffaire.Length
            ? code[PrefixeOngletAffaire.Length..]
            : null;

    /// <summary>Le titre de l'onglet : le nom du dossier, sinon son numero.</summary>
    private static string LibelleOngletAffaire(Dossier d)
        => string.IsNullOrWhiteSpace(d.Objet) ? d.Numero : d.Objet;

    /// <summary>Pose l'onglet de l'affaire qui s'ouvre, ou revient dessus s'il existe deja.</summary>
    private void PoserOngletAffaire(Dossier d)
    {
        Onglets.Ouvrir(CodeOngletAffaire(d.Numero), LibelleOngletAffaire(d));
        RetenirAgencement();
    }

    /// <summary>La croix de l'affaire (entete, Echap) : son onglet se ferme avec elle,
    /// et l'onglet voisin prend la place.</summary>
    private void FermerAffaire()
    {
        if (DossierOuvert is { } d) Onglets.Fermer(CodeOngletAffaire(d.Numero));
        FermerDossier();
        SuivreOngletActif();
        RetenirAgencement();
    }

    /// <summary>
    /// Accorde la vue a l'onglet ACTIF, apres un geste sur la barre : l'onglet d'une
    /// affaire la montre (retrouvee telle quelle si sa vue est encore chargee, sinon
    /// rouverte par son numero) ; tout autre onglet -- ou aucun -- la quitte sans la
    /// fermer : son onglet reste dans la barre.
    /// </summary>
    private void SuivreOngletActif()
    {
        if (Onglets.Montre is { } onglet && NumeroDOngletAffaire(onglet.Code) is { } numero)
        {
            if (DossierOuvert?.Numero == numero) return;
            if (AffaireChargee?.Numero == numero) DossierOuvert = AffaireChargee;
            else OuvrirParNumero(numero);
        }
        else DossierOuvert = null;
    }

    /// <summary>Au demarrage, l'onglet d'une affaire retenue revient dans la barre sans
    /// l'ouvrir : seule celle de l'onglet actif s'ouvrira (SuivreOngletActif).</summary>
    private void ReposerOngletAffaire(string numero)
        => Onglets.Ouvrir(CodeOngletAffaire(numero), LibelleOngletAffaire(DossierDeNumero(numero)));
}
