namespace RigV3Desktop.Pont.EcransGeneres;

// LE CATALOGUE DES ECRANS GENERIQUES : tous les domaines (IDomaineEcrans) range dans
// Pont/EcransGeneres/, trouves par reflexion. Ajouter un verbe = ajouter son fichier : ce
// catalogue ne se modifie pas. Une procedure decrite deux fois est une erreur de
// construction, levee au premier acces (on ne garde pas un doublon en silence).
//
// Deuxieme passe (24/11/2026, conv-2142) : les ECRANS REUNIS. Une entree du menu qui reunit
// plusieurs procedures (Pont/MenuAccueil.cs, EntreeMenu.Reunit) recoit ici un descripteur
// fabrique a partir de ceux de ses procedures : rien n'est ecrit deux fois, rien n'est invente.
// Une procedure reunie sans descripteur est, elle aussi, une erreur de construction.
public static class CatalogueGenere
{
    /// <summary>Les tuiles d'un ecran reuni : ses procedures nomment chacune « a faire » a leur
    /// facon (« A relancer », « A composer ») ; la tuile dit ce qui leur est commun. Chaque ligne
    /// garde, elle, le libelle de sa procedure. Declare AVANT _parCode : les champs statiques
    /// s'initialisent dans l'ordre du texte, et Charger() le lit.</summary>
    public static readonly EtatEcran[] EtatsReunis = EtatsEcran.Trois("À traiter", "En cours", "Terminé");

    private static readonly Dictionary<string, DescripteurEcran> _parCode = Charger();

    /// <summary>Tous les descripteurs, dans l'ordre de decouverte (les ecrans reunis en dernier).</summary>
    public static IReadOnlyCollection<DescripteurEcran> Tous => _parCode.Values;

    /// <summary>Le descripteur d'une procedure, ou nul : son ecran reste « a venir ».</summary>
    public static DescripteurEcran? Descripteur(string code) => _parCode.GetValueOrDefault(code);

    private static Dictionary<string, DescripteurEcran> Charger()
    {
        var parCode = new Dictionary<string, DescripteurEcran>();
        var domaines = typeof(CatalogueGenere).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, Namespace: "RigV3Desktop.Pont.EcransGeneres" }
                        && typeof(IDomaineEcrans).IsAssignableFrom(t));
        foreach (var type in domaines)
        {
            var domaine = (IDomaineEcrans)Activator.CreateInstance(type)!;
            foreach (var ecran in domaine.Ecrans())
            {
                if (!parCode.TryAdd(ecran.Code, ecran))
                    throw new InvalidOperationException($"{ecran.Code} est decrit deux fois (domaine {type.Name}).");
            }
        }

        foreach (var entree in MenuAccueil.Moments.SelectMany(m => m.Sous).SelectMany(s => s.Entrees)
                                                  .Where(e => e.Membres.Length > 0).DistinctBy(e => e.Code))
        {
            if (!parCode.TryAdd(entree.Code, Reunir(entree, parCode)))
                throw new InvalidOperationException($"{entree.Code} est a la fois decrit et reuni par le menu.");
        }
        return parCode;
    }

    /// <summary>Le descripteur d'un ecran reuni : un groupe par procedure (son titre), les lignes
    /// de chacune (leur ancien groupe passe en tete de la precision), les criteres communs, et
    /// les procedures elles-memes en <see cref="DescripteurEcran.Membres"/> pour leurs marches.</summary>
    private static DescripteurEcran Reunir(EntreeMenu entree, IReadOnlyDictionary<string, DescripteurEcran> parCode)
    {
        var membres = entree.Membres
            .Select(m => parCode.GetValueOrDefault(m.Code)
                         ?? throw new InvalidOperationException($"{entree.Code} reunit {m.Code}, qui n'a pas de descripteur."))
            .ToArray();
        return new DescripteurEcran
        {
            Code = entree.Code,
            Titre = entree.Libelle,
            Sujet = "lignes",
            Recherche = "Rechercher un nom, un numéro, une procédure…",
            Criteres = [.. membres.SelectMany(m => m.Criteres).Distinct().Take(6)],
            Valider = "Valider",
            Rang = membres[0].Rang,
            Etats = [.. EtatsReunis, .. membres.SelectMany(m => m.Etats)
                                               .Where(e => EtatsReunis.All(r => r.Code != e.Code))
                                               .DistinctBy(e => e.Code)],
            Groupes = [.. membres.Select(m => m.Titre)],
            Marches = [],
            Lignes = [.. membres.SelectMany(m => m.Lignes.Select(l => l with { Groupe = m.Titre, Precisions = AvecAncienGroupe(l) }))],
            Membres = membres,
        };
    }

    /// <summary>La precision d'une ligne reunie : son ancien groupe en tete, pour ne pas le perdre.
    /// Beaucoup de procedures le portent DEJA en tete de leur precision (Cabinets : « Salles fixes ·
    /// enregistree ») -- le repeter afficherait « Salles fixes · Salles fixes » (vu le 24/11/2026).</summary>
    private static string AvecAncienGroupe(LigneEcran l)
    {
        if (string.IsNullOrEmpty(l.Groupe) || l.Precisions.StartsWith(l.Groupe, StringComparison.Ordinal))
            return l.Precisions;
        return string.IsNullOrEmpty(l.Precisions) ? l.Groupe : $"{l.Groupe} · {l.Precisions}";
    }
}
