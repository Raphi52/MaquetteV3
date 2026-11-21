namespace RigV3Desktop.Pont;

/// <summary>
/// Une des QUATRE natures d'affaire du greffe. Une affaire est l'une d'elles et
/// une seule : elles n'ont ni les memes etapes, ni les memes delais, ni les
/// memes interlocuteurs. Le code sert de suffixe de classe CSS (couleur).
/// </summary>
public sealed record NatureAffaire(string Code, string Nom);

/// <summary>Les criteres saisis a gauche de l'ecran (memes noms que l'ancien @code).</summary>
public sealed record CriteresAffaires(string CritNumero, string CritLibelle, string CritType, string CritNature,
                                      string CritIdInstance, string CritRole, string CritCodeAffaire,
                                      string CritNom, string CritEtat, DateOnly? CritSaisineDu,
                                      DateOnly? CritSaisineAu);

/// <summary>Une ligne de la grille, dates et solde deja mis en forme comme Blazor les affichait.</summary>
public sealed record LigneAffaire(string Numero, string IdInstance, string Role, string CodeAffaire,
                                  string Nature, string NomNature, string Libelle, string Nom, string Etat,
                                  string Siren, string Type, string Saisine, string Creation, string Solde);

/// <summary>L'instance choisie pour la suppression XXAFF.</summary>
public sealed record AffaireChoisie(string Numero, string IdInstance, string Role);

/// <summary>Ce que l'ecran affiche, calcule d'un seul coup sur les memes criteres.</summary>
/// <param name="Resume">« 7 instance, 3 procedure collective, … » : les natures presentes.</param>
/// <param name="AucunCritere">Aucun critere pose : le tableau vide ne veut alors RIEN dire.</param>
/// <param name="SirenCherche">Le SIREN a immatriculer si la recherche reste vide, sinon « ».</param>
/// <param name="Choisie">L'affaire selectionnee, cherchee dans TOUT le fonds (filtre ou pas).</param>
public sealed record VueAffaires(NatureAffaire[] Natures, string[] Etats, LigneAffaire[] Lignes,
                                 string Resume, bool AucunCritere, string SirenCherche,
                                 AffaireChoisie? Choisie);

/// <summary>
/// Pont de l'ecran PROC_RECHAFF (web/src/ecrans/EcranRechAffaires.tsx) : la
/// recherche d'affaires.
///
/// Le fonds interrogeable (jeu de demonstration + dossiers ouverts pendant la
/// session par « Ouvrir un dossier »), le filtre, le tri des colonnes et les
/// deux calculs du bas de grille (aucun critere, SIREN cherche) vivaient dans le
/// @code d'EcranRechAffaires.razor ; ils sont recopies ici a l'identique. Le tri
/// en particulier reste celui de .NET (OrderBy sur des chaines = culture du
/// poste) : le refaire en JavaScript rangerait autrement les noms accentues.
/// Metier/ n'est pas modifie.
/// </summary>
public static class EcranRechAffaires
{
    private sealed record Affaire(string Numero, string Libelle, string Siren, string Type,
                                  string Nature, DateTime Creation, decimal Solde,
                                  string IdInstance, string Role, string CodeAffaire,
                                  string Nom, string Etat, DateTime Saisine);

    private static readonly NatureAffaire[] Natures =
    [
        new("instance",   "Instance"),
        new("collective", "Procedure collective"),
        new("prevention", "Prevention"),
        new("mesure",     "Mesure")
    ];

    private static readonly string[] Etats = ["En cours", "Radiee", "Cloturee"];

    private static string NomNature(string code)
        => Natures.FirstOrDefault(n => n.Code == code)?.Nom ?? code;

    private static readonly Affaire[] AffairesDemo =
    [
        new("2026B01067", "Affaire de demonstration - AMIENS BILLARDS", "109 678 235", "RCS", "instance",   new(2026, 9, 1),  0m,      "55339", "2026F00301", "010", "AMIENS BILLARDS",     "En cours",  new(2026, 8, 30)),
        new("004176",  "Modification - SCI DU PARC",            "812 445 906", "RCS", "instance",   new(2026, 8, 24), 142.50m, "55210", "2026F00244", "010", "SCI DU PARC",           "En cours",  new(2026, 8, 20)),
        new("004182",  "Immatriculation - AMIENS BILLARDS",     "109 678 235", "RCS", "instance",   new(2026, 8, 28), 0m,      "55288", "2026F00262", "011", "AMIENS BILLARDS",       "En cours",  new(2026, 8, 26)),
        new("004179",  "Radiation - MARTIN EURL",               "521 004 332", "RCS", "instance",   new(2026, 8, 12), 68.30m,  "55102", "2026F00198", "012", "MARTIN EURL",           "Radiee",    new(2026, 8, 5)),
        new("004168",  "Immatriculation - BATIPRO SARL",        "440 927 115", "RCS", "instance",   new(2026, 7, 30), 0m,      "54990", "2026F00151", "011", "BATIPRO SARL",          "Cloturee",  new(2026, 7, 25)),
        new("J-00931", "Depot de bilan - TRANSNORD SA",         "318 552 007", "JUD", "collective", new(2026, 6, 18), 415.00m, "54771", "2026F00087", "300", "TRANSNORD SA",          "En cours",  new(2026, 6, 10)),
        new("J-00944", "Liquidation judiciaire - COMPTOIR DU NORD", "402 118 776", "JUD", "collective", new(2026, 8, 5), 230.00m, "55044", "2026F00173", "310", "COMPTOIR DU NORD",   "En cours",  new(2026, 7, 29)),
        new("P-00218", "Mandat ad hoc - MENUISERIE PICARDE",    "789 220 431", "JUD", "prevention", new(2026, 8, 19), 0m,      "55177", "2026F00221", "400", "MENUISERIE PICARDE",    "En cours",  new(2026, 8, 14)),
        new("P-00221", "Conciliation - SOMME LOGISTIQUE",       "654 901 228", "JUD", "prevention", new(2026, 7, 22), 90.00m,  "54931", "2026F00139", "410", "SOMME LOGISTIQUE",      "Cloturee",  new(2026, 7, 18)),
        new("M-00102", "Mesure d'instruction - BATIPRO SARL",   "440 927 115", "JUD", "mesure",     new(2026, 8, 30), 0m,      "55301", "2026F00277", "500", "BATIPRO SARL",          "En cours",  new(2026, 8, 27)),
        // Affaires PUREMENT judiciaires (demande du 16/09/2026) : aucune n a
        // de dossier RCS au greffe — partie non immatriculee, personne
        // physique, ou societe immatriculee dans un AUTRE greffe. Leur
        // colonne Siren est donc vide, et la visionneuse n ouvre que la
        // branche « Judiciaire » (v2 : OPE_VGED.cs, _InitTreeview n ajoute
        // « Dossier RCS » que si l affaire A un dossier RCS).
        // Enrolement d une affaire contentieuse : processus CJUD.
        new("2026J00087",  "Assignation en paiement - DUPONT Jean",          "", "JUD", "instance",   new(2026, 3, 12), 88.00m,  "55402", "2026F00312", "010", "DUPONT Jean",           "En cours",  new(2026, 3, 5)),
        // Liquidation judiciaire d une entreprise individuelle : NPC puis CLOT.
        new("2026P00031",  "Liquidation judiciaire - MOREAU Entreprise individuelle", "", "JUD", "collective", new(2026, 2, 4),  310.00m, "55388", "2026F00305", "310", "MOREAU EI",   "En cours",  new(2026, 1, 28)),
        // Injonction de payer contre un particulier : IPE puis IPSUIVI.
        new("2026IP00214", "Injonction de payer - SOMME ELECTRICITE / LEROY", "", "JUD", "instance",   new(2026, 5, 20), 35.50m,  "55421", "2026F00330", "020", "LEROY Michel",          "En cours",  new(2026, 5, 18)),
        // Mesure d instruction avant tout proces : MESIN.
        new("2026M00045",  "Expertise avant proces - GARAGE CENTRAL",        "", "JUD", "mesure",     new(2026, 4, 8),  0m,      "55410", "2026F00321", "500", "GARAGE CENTRAL",        "En cours",  new(2026, 4, 2)),
        // Conciliation confidentielle, hors RCS : prevention.
        new("2026C00059",  "Conciliation - ATELIER DU MEUBLE",               "", "JUD", "prevention", new(2026, 6, 15), 0m,      "55433", "2026F00341", "410", "ATELIER DU MEUBLE",     "En cours",  new(2026, 6, 10))
    ];

    /// <summary>
    /// Les affaires interrogeables : le jeu de demonstration PLUS les dossiers
    /// ouverts pendant la session par l'ecran « Ouvrir un dossier ». Les
    /// colonnes que l'ouverture ne connait pas (solde, instance, saisine) sont
    /// laissees a vide plutot qu'inventees.
    /// </summary>
    private static Affaire[] Affaires =>
    [
        .. AffairesOuvertes.Toutes.Select(d =>
               new Affaire(d.Numero, d.Objet, d.Siren, d.Type, "instance",
                           JeuDemo.Aujourdhui, 0m, "", "", "011",
                           d.Objet.Replace("Immatriculation - ", ""),
                           "En cours", JeuDemo.Aujourdhui)),
        .. AffairesDemo
    ];

    /// <summary>La nature d'une affaire du fonds (code de <see cref="Natures"/> : instance, collective,
    /// prevention, mesure), ou null si elle n'y est pas. La barre du dossier y choisit les courriers
    /// divers qu'elle propose (Pont/PlanTravailActions.cs, lot 9 du plan « Vue du dossier »).</summary>
    public static string? NatureDe(string numero) => Affaires.FirstOrDefault(a => a.Numero == numero)?.Nature;

    /// <summary>La date de creation d'une affaire du fonds, ou null si elle n'y est pas. L'aiguillage la compare a
    /// la date de passage du greffe pour dire si l'affaire se traite dans l'ancien RIG (SOC-014, SOC-015 :
    /// Pont/AncienRig.cs). Ce fichier reste sans dependance : GedCheck, OuvertureCheck et TriCheck le compilent seul.</summary>
    public static DateTime? CreationDe(string numero) => Affaires.FirstOrDefault(a => a.Numero == numero)?.Creation;

    /// <param name="cleTri">La colonne cliquee ; null = ordre d'origine (du plus recent au plus ancien).</param>
    /// <param name="selection">Le numero de la ligne selectionnee, ou null.</param>
    [JSInvokable("EcranRechAffaires.Trouver")]
    public static VueAffaires Trouver(CriteresAffaires criteres, string? cleTri, bool ascendant, string? selection)
    {
        var trouvees = Trouvees(criteres, cleTri, ascendant);
        var choisie = Affaires.FirstOrDefault(a => a.Numero == selection);

        return new VueAffaires(
            Natures,
            Etats,
            trouvees.Select(a => new LigneAffaire(
                    a.Numero, a.IdInstance, a.Role, a.CodeAffaire, a.Nature, NomNature(a.Nature),
                    a.Libelle, a.Nom, a.Etat, a.Siren, a.Type,
                    a.Saisine.ToString("dd/MM/yyyy"), a.Creation.ToString("dd/MM/yyyy"),
                    a.Solde.ToString("N2")))
                .ToArray(),
            string.Join(", ", Natures
                .Select(n => new { n.Nom, Nb = trouvees.Count(a => a.Nature == n.Code) })
                .Where(x => x.Nb > 0)
                .Select(x => $"{x.Nb} {x.Nom.ToLowerInvariant()}")),
            AucunCritere(criteres),
            SirenCherche(criteres),
            choisie is null ? null : new AffaireChoisie(choisie.Numero, choisie.IdInstance, choisie.Role));
    }

    /// <summary>Vrai tant qu'aucun critere n'est pose : le tableau vide ne veut
    /// alors RIEN dire, et proposer d'ouvrir un dossier serait du bruit.</summary>
    private static bool AucunCritere(CriteresAffaires c)
        => string.IsNullOrWhiteSpace(c.CritNumero) && string.IsNullOrWhiteSpace(c.CritLibelle)
        && string.IsNullOrEmpty(c.CritType) && string.IsNullOrEmpty(c.CritNature)
        && string.IsNullOrWhiteSpace(c.CritIdInstance) && string.IsNullOrWhiteSpace(c.CritRole)
        && string.IsNullOrWhiteSpace(c.CritCodeAffaire) && string.IsNullOrWhiteSpace(c.CritNom)
        && string.IsNullOrEmpty(c.CritEtat) && c.CritSaisineDu is null && c.CritSaisineAu is null;

    /// <summary>Le SIREN reellement cherche : il se saisit dans « Libelle ou
    /// SIREN » (le meme champ sert aux deux), donc on ne le reprend que s'il
    /// ressemble a un SIREN — neuf chiffres.</summary>
    private static string SirenCherche(CriteresAffaires c)
    {
        foreach (var brut in new[] { c.CritLibelle, c.CritNom, c.CritNumero })
        {
            var chiffres = new string((brut ?? string.Empty).Where(char.IsDigit).ToArray());
            if (chiffres.Length == 9 && (brut ?? "").All(ch => char.IsDigit(ch) || ch == ' '))
                return chiffres;
        }
        return string.Empty;
    }

    private static Affaire[] Trouvees(CriteresAffaires c, string? CleTri, bool TriAscendant)
    {
        IEnumerable<Affaire> q = Affaires;
        if (!string.IsNullOrWhiteSpace(c.CritNumero))
            q = q.Where(a => a.Numero.Contains(c.CritNumero.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(c.CritLibelle))
        {
            var t = c.CritLibelle.Trim();
            q = q.Where(a => a.Libelle.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || a.Siren.Replace(" ", "").Contains(t.Replace(" ", ""),
                                StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrEmpty(c.CritType)) q = q.Where(a => a.Type == c.CritType);
        if (!string.IsNullOrEmpty(c.CritNature)) q = q.Where(a => a.Nature == c.CritNature);
        if (!string.IsNullOrWhiteSpace(c.CritIdInstance))
            q = q.Where(a => a.IdInstance.Contains(c.CritIdInstance.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(c.CritRole))
            q = q.Where(a => a.Role.Contains(c.CritRole.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(c.CritCodeAffaire))
            q = q.Where(a => a.CodeAffaire.Contains(c.CritCodeAffaire.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(c.CritNom))
        {
            var t = c.CritNom.Trim();
            q = q.Where(a => a.Nom.Contains(t, StringComparison.OrdinalIgnoreCase)
                          || a.Libelle.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrEmpty(c.CritEtat)) q = q.Where(a => a.Etat == c.CritEtat);
        if (c.CritSaisineDu is { } du) q = q.Where(a => DateOnly.FromDateTime(a.Saisine) >= du);
        if (c.CritSaisineAu is { } au) q = q.Where(a => DateOnly.FromDateTime(a.Saisine) <= au);
        // Sans clic sur un en-tete, la grille reste rangee du plus recent au
        // plus ancien : c'est l'ordre attendu a l'ouverture.
        if (CleTri is null) return q.OrderByDescending(a => a.Creation).ToArray();

        Func<Affaire, object> clef = CleTri switch
        {
            "Numero" => a => a.Numero,
            "IdInstance" => a => a.IdInstance,
            "Role" => a => a.Role,
            "CodeAffaire" => a => a.CodeAffaire,
            "Nature" => a => a.Nature,
            "Libelle" => a => a.Libelle,
            "Nom" => a => a.Nom,
            "Etat" => a => a.Etat,
            "Siren" => a => a.Siren,
            "Type" => a => a.Type,
            "Saisine" => a => a.Saisine,
            "Solde" => a => a.Solde,
            _ => a => a.Creation
        };
        return (TriAscendant ? q.OrderBy(clef) : q.OrderByDescending(clef)).ToArray();
    }
}
