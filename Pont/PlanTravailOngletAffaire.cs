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
//   - la maison ramene a l'accueil et garde l'onglet, comme pour un ecran ;
//   - glisse HORS de la barre, il sort dans sa propre fenetre (FenetreVue),
//     comme l'onglet d'un ecran (demande du 19/11/2026). Fermer cette fenetre
//     ramene l'affaire dans son onglet ; la croix de l'affaire, ou Echap, DANS
//     la fenetre, ferme l'affaire : la fenetre et l'onglet s'en vont ensemble.
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
        if (ModeFenetreVue && DossierOuvert is not null)
        {
            // Dans la fenetre d'un onglet sorti, il n'y a qu'elle : fermer l'affaire
            // ferme sa fenetre, et la fenetre principale ferme son onglet (OuvrirFenetreVue).
            AffaireFermee = true;
            FermerFenetre?.Invoke();
            return;
        }
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
            if (AffaireChargee?.Numero == numero)
            {
                DossierOuvert = AffaireChargee;
                // La page remonte la zone des etapes a neuf, en haut : sans defilement demande,
                // elle rapportait l'etape du haut (etapeVisible) et l'affaire repartait a 1 / 6
                // (parcours du lot 6, 20/11/2026). Elle revient sur SON etape.
                EtapeADefiler = true;
            }
            else OuvrirParNumero(numero);
        }
        else DossierOuvert = null;
    }

    /// <summary>Au demarrage, l'onglet d'une affaire retenue revient dans la barre sans
    /// l'ouvrir : seule celle de l'onglet actif s'ouvrira (SuivreOngletActif).</summary>
    private void ReposerOngletAffaire(string numero)
        => Onglets.Ouvrir(CodeOngletAffaire(numero), LibelleOngletAffaire(DossierDeNumero(numero)));

    // ---- L'onglet d'affaire SORTI dans sa propre fenetre ----

    /// <summary>Vrai, dans la fenetre d'une affaire sortie, quand l'affaire y a ete
    /// fermee (sa croix, Echap) : la fenetre principale ferme alors son onglet au
    /// lieu de l'y ramener.</summary>
    public bool AffaireFermee { get; private set; }

    /// <summary>La session demande a sa fenetre de se fermer (FenetreVue s'y abonne) :
    /// l'affaire sortie vient d'y etre fermee.</summary>
    public event Action? FermerFenetre;

    /// <summary>Le titre de la fenetre d'une affaire qu'on sort : celui de son onglet.
    /// Null pour un code qui n'est pas celui d'une affaire.</summary>
    private string? LibelleAffaireASortir(string code)
        => NumeroDOngletAffaire(code) is null ? null
            : Onglets.Onglets.FirstOrDefault(o => o.Code == code)?.Libelle;

    /// <summary>L'affaire part dans sa propre fenetre : celle-ci cesse de la montrer, et
    /// oublie sa vue chargee. Rattachee, elle se rouvre a neuf -- avec ce qu'on a fait
    /// dans l'autre fenetre (pieces ajoutees, saisie), qui a sa propre session.</summary>
    private void QuitterAffaireSortie(string code)
    {
        if (NumeroDOngletAffaire(code) is not { } numero) return;
        if (DossierOuvert?.Numero == numero) FermerDossier();
        else if (AffaireChargee?.Numero == numero) AffaireChargee = null;
    }

    /// <summary>La fenetre d'un onglet sorti s'ouvre sur son code : l'ecran, ou l'affaire.</summary>
    private void OuvrirVueInitiale(string code)
    {
        if (NumeroDOngletAffaire(code) is { } numero) OuvrirParNumero(numero);
        else OuvrirCode(code);
    }
}
