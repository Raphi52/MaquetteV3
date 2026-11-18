namespace RigV3Desktop;

/// <summary>
/// Ce qu'une paire (table, attribut) du referentiel RIG designe REELLEMENT
/// en base -- ou pourquoi elle ne designe rien.
/// </summary>
/// <param name="Table">La table reelle, prefixe de processus retire.</param>
/// <param name="Colonne">La colonne reelle, prefixe de table AJOUTE.</param>
/// <param name="Motif">Vide si la cible est resolue ; sinon ce qui cloche.</param>
public sealed record CibleColonne(string Table, string Colonne, string Motif)
{
    /// <summary>Vrai quand la paire designe une vraie colonne de la base.</summary>
    public bool Resolue => Motif.Length == 0;

    /// <summary>« INSCRIT_PP.INSPP_PATRONYME », ou le motif de l'echec.</summary>
    public override string ToString()
        => Resolue ? $"{Table}.{Colonne}" : Motif;
}

/// <summary>
/// La correspondance entre le REFERENTIEL d'evenements de RIG et les
/// colonnes REELLES de la base.
///
/// POURQUOI CE FICHIER EXISTE. Le referentiel
/// (ULT_OP_ET_EVENEMENT_RCS) declare qu'un evenement modifie
/// « INSCRIT_PP / PATRONYME ». Mais la colonne, en base, s'appelle
/// « INSPP_PATRONYME » : chaque table prefixe ses colonnes. Chercher
/// l'attribut tel qu'il est ecrit ne ramene RIEN -- et un branchement qui
/// ne trouve rien ne LEVE rien : il rend une valeur vide, la tache parait
/// incomplete, et personne ne comprend pourquoi. C'est exactement le genre
/// de panne muette que ce fichier existe pour rendre bruyante.
///
/// Les prefixes ci-dessous sont LUS sur RIG_AMIENS@SQL-PROD\PROD le
/// 16/09/2026 (INFORMATION_SCHEMA.COLUMNS, premieres colonnes de chaque
/// table), jamais deduits du nom de la table -- la regle n'est pas
/// mecanique : DIRIGEANT donne DRGNT, DOSSIER_RCS donne DSSRC.
/// </summary>
public static class ColonnesRig
{
    // EPREUVE EN CONDITIONS REELLES, 16/09/2026, RIG_AMIENS@SQL-PROD\PROD.
    // Les noms produits par Resoudre() ont ete utilises TELS QUELS dans des
    // requetes sur la base de production, et ils ont ramene des donnees :
    //   INSPM_DENOMINATION_USAGE, INSPM_SIGLE, INSPM_FORME_JURIDIQUE_UTI et
    //     DSSRC_DATE_CESSATION -- lus ensemble sur de vrais dossiers, avec
    //     la jointure DOSSIER_RCS.DSSRC_ID_RPRTR = INSCRIT_PM.INSPM_ID_DSRCS.
    //   ETBLS_NOM_DOMAINE       1 072 etablissements renseignes
    //   INSPP_PATRONYME        46 417 lignes renseignees
    //   DRGNT_FONCTION_DIRIGEANT  194 024 lignes
    //   DRGPP_PATRONYME           172 269 lignes
    //   ADRBS_ID_ADRBS            326 304 lignes
    // Les sept tables des recettes sont donc verifiees de bout en bout : la
    // regle ne produit pas seulement un nom plausible, elle produit un nom
    // QUI LIT. A refaire si un prefixe est ajoute ici.
    //
    // A RETENIR POUR LE PRODUIT : le nom de domaine n'est renseigne que sur
    // 1 072 etablissements. Le champ existe, mais la donnee est rare -- une
    // tache qui l'EXIGE (recette DENO) demandera souvent une saisie neuve.

    /// <summary>
    /// Prefixe reel de chaque table utilisee par les recettes. Une table
    /// ABSENTE d'ici n'est pas devinee : elle est refusee, bruyamment.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Prefixes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ADRESSE_BASE"] = "ADRBS",
            ["DIRIGEANT"] = "DRGNT",
            ["DIRIGEANT_PP"] = "DRGPP",
            ["DOSSIER_RCS"] = "DSSRC",
            ["ETABLISSEMENT"] = "ETBLS",
            ["INSCRIT_PM"] = "INSPM",
            ["INSCRIT_PP"] = "INSPP",
        };

    /// <summary>
    /// Un nom prefixe « # » designe un TITRE de bloc ou un controle
    /// d'ecran, pas une donnee : aucune colonne de la base ne le porte.
    /// </summary>
    public static bool EstBlocEcran(string nom)
        => nom.StartsWith('#');

    /// <summary>
    /// Le « X » de tete d'un nom de table est un prefixe de PROCESSUS v2,
    /// pas une table a part : XINSCRIT_PP designe bien INSCRIT_PP. Il ne se
    /// retire QUE si ce qui reste est une table connue -- sinon on
    /// amputerait une table dont le nom commence vraiment par X.
    /// </summary>
    public static string TableReelle(string table)
        => table.Length > 1 && table[0] == 'X' && Prefixes.ContainsKey(table[1..])
            ? table[1..]
            : table;

    /// <summary>
    /// Ce que (table, attribut) du referentiel designe en base. Rend
    /// TOUJOURS un resultat lisible : soit la colonne reelle, soit le motif
    /// pour lequel il n'y en a pas. Jamais de chaine plausible fabriquee.
    /// </summary>
    public static CibleColonne Resoudre(string table, string attribut)
    {
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(attribut))
            return new("", "", "table ou attribut vide");

        if (EstBlocEcran(table) || EstBlocEcran(attribut))
            return new("", "", $"« {table}/{attribut} » est un bloc d'ecran, pas une colonne");

        var reelle = TableReelle(table);
        if (!Prefixes.TryGetValue(reelle, out var prefixe))
            return new("", "", $"table « {reelle} » inconnue : son prefixe de colonnes n'a pas ete lu en base");

        // Un attribut DEJA prefixe ne se prefixe pas deux fois : le
        // referentiel est irregulier, mieux vaut l'absorber ici qu'ailleurs.
        var colonne = attribut.StartsWith(prefixe + "_", StringComparison.Ordinal)
            ? attribut
            : prefixe + "_" + attribut;
        return new(reelle, colonne, "");
    }

    /// <summary>La colonne reelle d'un champ de recette, ou son motif d'echec.</summary>
    public static CibleColonne Resoudre(ChampRecette champ)
        => Resoudre(champ.Table, champ.Colonne);

    /// <summary>
    /// Les champs de recettes qui ne se resolvent PAS. Doit rester vide :
    /// c'est le controle qui empeche un branchement muet le jour venu.
    /// </summary>
    public static (RecetteTache Recette, ChampRecette Champ, string Motif)[] NonResolus(
        IEnumerable<RecetteTache> recettes)
        => recettes
            .SelectMany(r => r.Champs.Select(c => (Recette: r, Champ: c, Cible: Resoudre(c))))
            .Where(x => !x.Cible.Resolue)
            .Select(x => (x.Recette, x.Champ, x.Cible.Motif))
            .ToArray();
}
