namespace RigV3Desktop.Pont;

/// <summary>Un dossier deja ouvert, date et capital mis en forme comme Blazor les affichait.</summary>
public sealed record LigneDossierOuvert(string Numero, string Siren, string Denomination, string Forme,
                                        string Immatriculation, string Capital, string Monnaie, string Origine);

/// <summary>Ce que l'ecran montre a l'ouverture : la date proposee et le tableau des dossiers ouverts.</summary>
/// <param name="Aujourdhui">La date d'immatriculation proposee, au format d'un champ date (aaaa-mm-jj).</param>
/// <param name="Mode">« serveur » (le tableau vient de la base du greffe), « demo » (sans serveur : l'ecran marque
/// « exemple »), ou « sans-greffe », « refuse », « erreur » avec leur <paramref name="Message"/> (Pont/LectureEcranMain.cs).</param>
/// <param name="Total">Le nombre de dossiers du greffe (en base) ; celui du tableau en demonstration.</param>
public sealed record VueOuverture(string Aujourdhui, LigneDossierOuvert[] Ouverts, string Mode = "demo", string? Message = null,
                                  int Total = 0);

/// <summary>La fiche saisie a gauche (les champs que l'ouverture enregistre).</summary>
public sealed record FicheOuverture(string Origine, string Siren, DateOnly? DateImmat, string Denomination,
                                    string FormeJuridique, decimal Capital, string Monnaie);

/// <summary>Le compte rendu d'une ouverture : la phrase a afficher, et le tableau a jour.</summary>
/// <param name="Bloquant">Vrai pour un refus : la phrase s'affiche en attente, la fiche n'est pas videe.</param>
public sealed record ResultatOuverture(string Message, bool Bloquant, LigneDossierOuvert[] Ouverts);

/// <summary>
/// Pont de l'ecran « Ouvrir un dossier » (web/src/ecrans/EcranOuvertureDossier.tsx) :
/// faire NAITRE une affaire RCS pour une societe. Sans origine RIG v2 : aucun
/// ecran de l'ancienne application ne portait ce geste.
///
/// Les controles (SIREN a neuf chiffres, denomination obligatoire, un seul
/// dossier par SIREN) et les phrases de compte rendu vivaient dans le @code
/// d'EcranOuvertureDossier.razor ; ils sont recopies ici a l'identique. Le
/// dossier rejoint le registre PARTAGE (Metier/AffairesOuvertes.cs), celui que
/// la recherche d'affaires et la visionneuse interrogent. Metier/ n'est pas modifie.
///
/// Ce registre est celui de la DEMONSTRATION : branche au serveur, l'ecran lit les
/// dossiers de la base du greffe et n'ouvre rien (EcranOuvertureDossierBase.cs, plan
/// « RIG v3 100 % fonctionnel », lot 17). Ce fichier se compile seul dans OuvertureCheck,
/// qui execute la demonstration (VueExemple, OuvrirExemple).
/// </summary>
public static partial class EcranOuvertureDossier
{
    /// <summary>La demonstration : la date du jeu et les dossiers du registre partage.</summary>
    public static VueOuverture VueExemple()
    {
        var ouverts = Ouverts();
        return new(JeuDemo.Aujourdhui.ToString("yyyy-MM-dd"), ouverts, Total: ouverts.Length);
    }

    /// <summary>La demonstration : ouvrir le dossier dans le registre partage, avec les controles de l'ecran.</summary>
    public static ResultatOuverture OuvrirExemple(FicheOuverture fiche)
    {
        // Un SIREN est fait de neuf chiffres ; les espaces de saisie sont admis.
        var sirenNormalise = new string((fiche.Siren ?? string.Empty).Where(char.IsDigit).ToArray());
        var denomination = fiche.Denomination ?? string.Empty;

        if (sirenNormalise.Length != 9)
            return Refus("Le SIREN doit compter neuf chiffres.");

        if (string.IsNullOrWhiteSpace(denomination))
            return Refus("La denomination est obligatoire pour ouvrir un dossier.");

        var deja = AffairesOuvertes.ParSiren(sirenNormalise);
        if (deja is not null)
            return Refus("Ce SIREN a deja un dossier ouvert : " + deja.Numero);

        var immat = fiche.DateImmat is { } d ? d.ToDateTime(TimeOnly.MinValue) : JeuDemo.Aujourdhui;

        // Le dossier rejoint le registre PARTAGE : c'est lui que la recherche
        // d'affaires et la visionneuse interrogent. Sans cela, l'ouverture ne
        // serait visible que dans le tableau de droite.
        var ne = AffairesOuvertes.Ouvrir(sirenNormalise, denomination, immat, null,
                                         fiche.FormeJuridique, fiche.Capital, fiche.Monnaie, fiche.Origine);
        if (ne is null)
            return Refus("L'ouverture a ete refusee : verifiez le SIREN et la denomination.");

        return new ResultatOuverture(
            "Dossier " + ne.Numero + " ouvert pour " + denomination.Trim()
            + " : il est desormais retrouvable dans « Mes affaires » par son SIREN.",
            Bloquant: false,
            Ouverts());
    }

    private static ResultatOuverture Refus(string message) => new(message, Bloquant: true, Ouverts());

    /// <summary>Les dossiers deja ouverts : ils viennent du registre partage,
    /// donc du disque — le tableau n'est pas vide au demarrage suivant.</summary>
    private static LigneDossierOuvert[] Ouverts()
        => AffairesOuvertes.Details
            .Select(d => new LigneDossierOuvert(d.Numero, d.Siren, d.Denomination, d.Forme,
                                                d.Immatriculation.ToString("dd/MM/yyyy"),
                                                d.Capital.ToString("N0"), d.Monnaie, d.Origine))
            .ToArray();
}
