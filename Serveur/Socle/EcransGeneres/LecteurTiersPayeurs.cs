using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Tiers payeurs » (PROC_GESTTIERSPAYEURS) : les payeurs PRINCIPAUX du greffe, la table PAYEUR. Mode d'emploi : LecteurEcran.cs.
/// Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026. Ajouter, Modifier, Supprimer : EcrivainTiersPayeurs.cs.
///
/// SOURCE (ancien RIG) : PROC_GESTTIERSPAYEURS (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GESTTIERSPAYEURS ; Detail OPE de
/// proc_gesttierspayeurs.md, qui fait foi). La liste : Payeur.GetPayeursPrincipaux, « ... FROM PAYEUR WHERE PYEUR_IS_PRINCIPAL = 1 »
/// (Outils\DLL\RigMetier\Facture\Payeur.cs:176-179), triee par abrege (OPE_TIERSPAYEURSLIST.cs:67) : les payeurs generes pour une
/// procedure collective (non principaux, CreateAndSauveTiersPayeur_ForPC, Payeur.cs:191) n'y sont pas. La grille de la V2 montre
/// l'abrege, la designation, l'adresse, le courriel et « actif » (TiersPayeursRigDataGridView.cs) ; le detail lie l'abrege, le
/// libelle, la ligne et la suite d'adresse, le code postal, la localite, le courriel, le mandataire PC et « actif »
/// (OPE_TIERSPAYEURSDETAIL.cs:57-66).
/// RECHERCHE : le champ cherche l'abrege, la designation ou la ville (la V2 n'a pas de recherche : sa liste est complete).
/// GROUPES : « Actifs » / « Inactifs » (PYEUR_IS_ACTIF, la colonne « actif » de la grille).
/// ETAT : designation vide -> AFaire « Sans désignation » (le libelle est obligatoire, MetierObligatoire,
/// OPE_TIERSPAYEURSDETAIL.Designer.cs:103) ; ni code postal ni localite -> EnCours « Adresse à compléter » ; sinon Validee « Enregistré ».
/// VALEURS : dans l'ordre des Colonnes (Abrégé, Désignation, Adresse, Complément, Code postal, Ville, Courriel, Actif).
/// </summary>
internal sealed class LecteurTiersPayeurs : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_GESTTIERSPAYEURS";

    internal const string Actifs = "Actifs", Inactifs = "Inactifs";
    internal const string ColAbrege = "Abrégé", ColDesignation = "Désignation", ColAdresse = "Adresse", ColComplement = "Complément",
                          ColCodePostal = "Code postal", ColVille = "Ville", ColCourriel = "Courriel", ColActif = "Actif";

    public IReadOnlyCollection<string> Criteres { get; } = [ColAbrege, ColDesignation, ColVille];

    private const string Requete = """
        SELECT TOP (@plafond) p.PYEUR_ID_PYEUR, p.PYEUR_ABREGE, p.PYEUR_LIBELLE, p.PYEUR_LIGNE_ADRESSE, p.PYEUR_SUITE_ADRESSE,
               p.PYEUR_CODE_POSTAL, p.PYEUR_LOCALITE_POSTALE, p.PYEUR_EMAIL, p.PYEUR_IS_ACTIF, COUNT(*) OVER () AS Total
        FROM v2.PAYEUR AS p
        WHERE p.PYEUR_IS_PRINCIPAL = 1 /*filtres*/
        ORDER BY p.PYEUR_ABREGE, p.PYEUR_ID_PYEUR
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("p.PYEUR_ABREGE LIKE @texte OR p.PYEUR_LIBELLE LIKE @texte OR p.PYEUR_LOCALITE_POSTALE LIKE @texte",
                ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(ColAbrege) is { } abrege) f.Ajouter("p.PYEUR_ABREGE LIKE @abrege", ("@abrege", FormeLigne.Contient(abrege)));
        if (recherche.Critere(ColDesignation) is { } libelle) f.Ajouter("p.PYEUR_LIBELLE LIKE @libelle", ("@libelle", FormeLigne.Contient(libelle)));
        if (recherche.Critere(ColVille) is { } ville) f.Ajouter("p.PYEUR_LOCALITE_POSTALE LIKE @ville", ("@ville", FormeLigne.Contient(ville)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var abrege = FormeLigne.Texte(r, "PYEUR_ABREGE");
        var libelle = FormeLigne.Texte(r, "PYEUR_LIBELLE");
        var cp = FormeLigne.Texte(r, "PYEUR_CODE_POSTAL");
        var ville = FormeLigne.Texte(r, "PYEUR_LOCALITE_POSTALE");
        var actif = Convert.ToBoolean(r["PYEUR_IS_ACTIF"], CultureInfo.InvariantCulture);
        var etat = libelle.Length == 0 ? "AFaire" : cp.Length == 0 && ville.Length == 0 ? "EnCours" : "Validee";
        var precisions = FormeLigne.Joindre($"abrégé {abrege}", $"{cp} {ville}".Trim() is { Length: > 0 } lieu ? lieu : "adresse à compléter",
            FormeLigne.Texte(r, "PYEUR_EMAIL"), actif ? null : "inactif");
        return new LigneLue(libelle.Length > 0 ? libelle : abrege, precisions, actif ? Actifs : Inactifs, etat, [])
        {
            Id = FormeLigne.Texte(r, "PYEUR_ID_PYEUR"),
            Valeurs = [abrege, libelle, FormeLigne.Texte(r, "PYEUR_LIGNE_ADRESSE"), FormeLigne.Texte(r, "PYEUR_SUITE_ADRESSE"), cp, ville,
                       FormeLigne.Texte(r, "PYEUR_EMAIL"), FormeLigne.Case(actif)],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT p.PYEUR_ABREGE, p.PYEUR_LIBELLE, p.PYEUR_LIGNE_ADRESSE, p.PYEUR_SUITE_ADRESSE, p.PYEUR_CODE_POSTAL, p.PYEUR_LOCALITE_POSTALE,
                   p.PYEUR_EMAIL, p.PYEUR_IS_ACTIF, CONCAT_WS(' ', m.MNDPC_NOM, m.MNDPC_COMPLEMENT_NOM) AS Mandataire,
                   (SELECT COUNT(*) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_PYEUR = p.PYEUR_ID_PYEUR) AS Ventilations,
                   (SELECT COUNT(*) FROM v2.FACTURE AS f WHERE f.FACTU_ID_PYEUR = p.PYEUR_ID_PYEUR) AS Factures
            FROM v2.PAYEUR AS p LEFT JOIN v2.MANDATAIRE_PC AS m ON m.MNDPC_ID_MNDPC = p.PYEUR_ID_MNDPC
            WHERE p.PYEUR_ID_PYEUR = @id AND p.PYEUR_IS_PRINCIPAL = 1
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        static ChampFiche M(string libelle, string valeur) => new(libelle, valeur) { Modifiable = true };
        var abrege = FormeLigne.Texte(r, "PYEUR_ABREGE");
        var libelle = FormeLigne.Texte(r, "PYEUR_LIBELLE");
        return new FicheEcran(Procedure, id, libelle.Length > 0 ? libelle : abrege,
        [
            new("Abrégé", [M(ColAbrege, abrege)]),
            new("Identité", [M(ColDesignation, libelle), new ChampFiche("Mandataire rattaché", FormeLigne.Texte(r, "Mandataire"))]),
            new("Adresse", [M(ColAdresse, FormeLigne.Texte(r, "PYEUR_LIGNE_ADRESSE")), M(ColComplement, FormeLigne.Texte(r, "PYEUR_SUITE_ADRESSE")),
                            M(ColCodePostal, FormeLigne.Texte(r, "PYEUR_CODE_POSTAL")), M(ColVille, FormeLigne.Texte(r, "PYEUR_LOCALITE_POSTALE")),
                            M(ColCourriel, FormeLigne.Texte(r, "PYEUR_EMAIL"))]),
            new("Enregistrement", [M(ColActif, FormeLigne.Case(Convert.ToBoolean(r["PYEUR_IS_ACTIF"], CultureInfo.InvariantCulture))),
                                   new ChampFiche("Utilisations", $"{FormeLigne.Texte(r, "Ventilations")} ventilation(s), {FormeLigne.Texte(r, "Factures")} facture(s)")]),
        ]);
    }
}
