namespace RigV3Desktop;

/// <summary>Un onglet de vue ouvert dans la barre, comme dans un navigateur.</summary>
public sealed class OngletVue
{
    public OngletVue(string code, string libelle)
    {
        Code = code;
        Libelle = libelle;
    }

    public string Code { get; }
    public string Libelle { get; set; }

    /// <summary>Vrai quand l'onglet a ete SORTI dans sa propre fenetre : il
    /// reste dans la barre (grise) pour qu'on sache ou il est parti, mais son
    /// contenu n'est plus rendu dans la fenetre principale.</summary>
    public bool Detache { get; set; }
}

/// <summary>
/// La barre d'onglets, en logique PURE : ouvrir, activer, fermer, reordonner,
/// arracher. Aucune dependance a WPF ni au navigateur, donc rejouable par les
/// controles internes (tests/OngletsCheck) -- c'est la partie ou un defaut se
/// verrait a l'ecran sans qu'aucune compilation ne le signale.
/// </summary>
public sealed class EtatOnglets
{
    private readonly List<OngletVue> _onglets = new();

    public IReadOnlyList<OngletVue> Onglets => _onglets;

    /// <summary>Index de l'onglet montre ; -1 quand aucun ne l'est (on est
    /// revenu a l'accueil, les onglets restent ouverts).</summary>
    public int Actif { get; private set; } = -1;

    public OngletVue? Actuel => Actif >= 0 && Actif < _onglets.Count ? _onglets[Actif] : null;

    /// <summary>La vue MONTREE dans la fenetre principale : un onglet sorti
    /// dans sa propre fenetre n'est plus rendu ici.</summary>
    public OngletVue? Montre => Actuel is { Detache: false } o ? o : null;

    public int IndexDe(string code) => _onglets.FindIndex(o => o.Code == code);

    /// <summary>Ouvre la vue, ou revient dessus si elle est deja ouverte : un
    /// second clic sur la meme entree de menu ne cree PAS un doublon.</summary>
    public OngletVue Ouvrir(string code, string libelle)
    {
        var index = IndexDe(code);
        if (index < 0)
        {
            _onglets.Add(new OngletVue(code, libelle));
            index = _onglets.Count - 1;
        }
        else _onglets[index].Libelle = libelle;
        Actif = index;
        return _onglets[index];
    }

    public void Activer(string code)
    {
        var index = IndexDe(code);
        if (index >= 0) Actif = index;
    }

    /// <summary>Quitte la vue courante SANS fermer les onglets (retour a
    /// l'accueil). Les onglets restent la, comme un navigateur sur sa page
    /// de demarrage.</summary>
    public void Masquer() => Actif = -1;

    /// <summary>Ferme un onglet. L'onglet actif suivant est le VOISIN, jamais
    /// l'accueil tant qu'il reste des onglets : fermer le 2e de 3 onglets ne
    /// doit pas vider l'ecran.</summary>
    public void Fermer(string code)
    {
        var index = IndexDe(code);
        if (index < 0) return;
        var etaitActif = index == Actif;
        _onglets.RemoveAt(index);
        if (_onglets.Count == 0) { Actif = -1; return; }
        if (etaitActif) Actif = Math.Min(index, _onglets.Count - 1);
        else if (index < Actif) Actif--;
    }

    /// <summary>Deplace un onglet a une nouvelle place (glisser dans la barre).
    /// L'onglet ACTIF reste le meme onglet, pas le meme index.</summary>
    public void Deplacer(int de, int vers)
    {
        if (de < 0 || de >= _onglets.Count) return;
        vers = Math.Clamp(vers, 0, _onglets.Count - 1);
        if (de == vers) return;
        var suivi = Actuel;
        var onglet = _onglets[de];
        _onglets.RemoveAt(de);
        _onglets.Insert(vers, onglet);
        if (suivi is not null) Actif = _onglets.IndexOf(suivi);
    }

    /// <summary>Marque l'onglet comme SORTI dans sa fenetre. Rend false si
    /// l'onglet est inconnu ou deja sorti (deux fenetres pour la meme vue
    /// afficheraient deux fois la meme chose).</summary>
    public bool Detacher(string code)
    {
        var index = IndexDe(code);
        if (index < 0 || _onglets[index].Detache) return false;
        _onglets[index].Detache = true;
        if (Actif == index) Actif = -1;
        return true;
    }

    /// <summary>La fenetre sortie est fermee : l'onglet revient dans la barre
    /// et redevient la vue montree.</summary>
    public void Rattacher(string code)
    {
        var index = IndexDe(code);
        if (index < 0) return;
        _onglets[index].Detache = false;
        Actif = index;
    }

    /// <summary>Les codes, dans l'ordre de la barre : c'est ce qui est retenu
    /// d'une session a l'autre.</summary>
    public List<string> Codes() => _onglets.Select(o => o.Code).ToList();

    /// <summary>
    /// Sur quelle place tombe le pointeur pendant le glisser, connaissant les
    /// rectangles des onglets mesures dans la page. Fonction pure : c'est elle
    /// qui decide de l'ordre, pas le navigateur.
    /// </summary>
    public static int IndexCible(IReadOnlyList<(double Gauche, double Droite)> cases, double x)
    {
        if (cases is null || cases.Count == 0) return 0;
        // fix-ok: cause mesuree (tests/OngletsCheck) — la premiere version
        // rendait la place D'INSERTION (« avant l'onglet dont on n'a pas
        // depasse le milieu »), alors que l'appelant attend la place FINALE de
        // l'onglet deplace : lache sur la 2e moitie de l'onglet 2, l'onglet
        // partait en 3e place au lieu de prendre celle de l'onglet survole.
        // On rend donc l'index de l'onglet SOUS le pointeur.
        for (var i = 0; i < cases.Count; i++)
            if (x >= cases[i].Gauche && x <= cases[i].Droite) return i;

        // Entre deux onglets (l'espace qui les separe) ou hors de la barre :
        // la place la plus proche.
        // Avant : tout ce qui n'etait pas sur un onglet partait en DERNIERE
        // place -- passer sur l'espace de 2 px entre deux onglets envoyait
        // l'onglet au bout de la barre puis le ramenait : il sautait.
        if (x < cases[0].Gauche) return 0;
        for (var i = 1; i < cases.Count; i++)
            if (x < cases[i].Gauche)
                return x - cases[i - 1].Droite <= cases[i].Gauche - x ? i - 1 : i;
        return cases.Count - 1;
    }
}
