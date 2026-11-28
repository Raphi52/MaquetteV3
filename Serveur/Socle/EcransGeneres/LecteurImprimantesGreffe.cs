using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Imprimantes du greffe » (PROC_IMP_WIN) : les imprimantes LOGIQUES du greffe, celles de l'onglet « Lots » de RIG v2 -- lot 1
/// (IMPRIMANTE_PS) et lot 2 (IMPRIMANTE_EDITION, ses types de papier dans TAG_POSTSCRIPT), en grille. Mode d'emploi : LecteurEcran.cs.
/// Ajouter et Modifier : EcrivainImprimantesGreffe.cs. Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 6,
/// 28/11/2026.
///
/// SOURCE (ancien RIG) : Outils\Impression\PROC_IMP_WIN, etape ETP_IMP_LOTS. Lot 1 : OPE_IMP_LOT1.ChargerDonnees (:62-70),
/// ImprimanteLot1.GetAllImprimanteLot1 (RigMetier\Edition\ImprimanteLot1.cs:95-107, « SELECT ... FROM IMPRIMANTE_PS », sans filtre),
/// grille « Nom imprimante », « Redirigée vers l'imprimante physique », « Différé », « Active » (OPE_IMP_LOT1.cs:77-101). Lot 2 :
/// OPE_IMP_LOT2.ChargerDonnees (:63-70), ImprimanteLot2.GetAllImprimanteLot2 (ImprimanteLot2.cs:262, « FROM IMPRIMANTE_EDITION »), grille
/// « Nom imprimante », « Redirigée vers l'imprimante physique », « Types de papier » (OPE_IMP_LOT2.cs:79-96) ; les types de papier
/// sont les tags « BAC_* » de l'imprimante dans TAG_POSTSCRIPT, en majuscules (_LoadTag, ImprimanteLot2.cs:65), sans le prefixe
/// (ImprimanteLot2.TypesPapier, :133-140), dans l'ordre du nom du tag.
/// Les deux listes sont dans la BASE DU GREFFE (SqlRigConnectionReadOnly(codeGreffe)) : reprises en v2.* par la V3.
///
/// NON LU (nomme) : les onglets « Imprimantes greffe », « Imprimantes manquantes » et « Imprimantes physiques » -- les imprimantes
/// Windows, leurs bacs et leurs reglages nommes sont les tables IMPRIMANTE_WINDOWS, IMPRIMANTE_BAC, IMPRIMANTE_DEVMOD et
/// RIG_IMPRIMANTE_WINDOWS de la base COMMUNE (COMMUN_RIG, PROC_IMP_WIN.cs:4-7), par serveur d'impression, et les pilotes viennent du
/// serveur Windows (PrinterManagement) : ni l'une ni l'autre ne sont dans la base V3 du greffe (lot 16 du plan precedent, 27/11/2026).
///
/// GROUPES : « Lot 1 » / « Lot 2 ». ETAT : sans imprimante physique -> AFaire « À rediriger » (la V2 l'exige : ultCboNomPhysique
/// .MetierObligatoire, OPE_SAISIE_IMP_LOT1.Designer.cs:93, OPE_SAISIE_IMP_LOT2.Designer.cs:95) ; lot 1 non active
/// (R_IMPRIMANTE_ACTIVATION = 0) -> EnCours « Inactive » ; sinon Validee « Active ». ORDRE : lot, puis nom.
/// VALEURS, dans l'ordre des Colonnes du descripteur : Nom, Redirigée vers l'imprimante physique, Utilisable pour les différés,
/// Active, Types de papier, Lot. ID : « 1:<nom> » ou « 2:<nom> » (le nom est la cle de la table, PK_IMPRIMANTE_PS / _EDITION).
/// </summary>
internal sealed class LecteurImprimantesGreffe : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_IMP_WIN";

    internal const string Lot1 = "Lot 1", Lot2 = "Lot 2";

    /// <summary>Les colonnes et champs du descripteur (FonctionsRetrouvees.cs), au mot pres ; les titres de la V2.</summary>
    internal const string ColNom = "Nom", ColPhysique = "Redirigée vers l'imprimante physique", ColDifferee = "Utilisable pour les différés",
                          ColActive = "Active", ColTypes = "Types de papier", ColLot = "Lot";

    internal const string SectionImprimante = "Imprimante", SectionLot1 = "Lot 1 : différés et activation", SectionLot2 = "Lot 2 : types de papier";

    internal const string CritereLot = "Lot", CriterePhysique = "Imprimante physique";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereLot, CriterePhysique];

    /// <summary>Une imprimante logique, telle que les deux grilles de la V2 la montrent.</summary>
    internal sealed record Imprimante(string Lot, string Nom, string Physique, bool Differee, bool Active, string TypesPapier, bool ReserveeAmitel)
    {
        public string Id => $"{(Lot == Lot1 ? "1" : "2")}:{Nom}";
    }

    /// <summary>Les deux listes en une, avec les types de papier du lot 2 (les tags BAC_*, sans le prefixe, par nom).</summary>
    private const string Requete = """
        SELECT TOP (@plafond) x.Lot, x.Nom, x.Physique, x.Differee, x.Active, x.Amitel, x.Types, COUNT(*) OVER () AS Total
        FROM (
            SELECT 'Lot 1' AS Lot, p.R_IMPRIMANTE_VALEUR AS Nom, p.R_IMPRIMANTE_EMPLACEMENT_IMP AS Physique,
                   p.R_IMPRIMANTE_DIFFEREE AS Differee, p.R_IMPRIMANTE_ACTIVATION AS Active, CAST(0 AS bit) AS Amitel, CAST('' AS varchar(max)) AS Types
            FROM v2.IMPRIMANTE_PS AS p
            UNION ALL
            SELECT 'Lot 2', e.IMPED_NOM_LOGIQUE, e.IMPED_NOM_PHYSIQUE, CAST(0 AS bit), CAST(1 AS bit), e.IMPED_ONLY_AMITEL,
                   ISNULL(STUFF((SELECT ', ' + SUBSTRING(UPPER(t.TAGPS_NOM_TAG), 5, 255) FROM v2.TAG_POSTSCRIPT AS t
                                 WHERE t.TAGPS_NOM_IMPRIMANTE = e.IMPED_NOM_LOGIQUE AND t.TAGPS_NOM_TAG LIKE 'BAC[_]%'
                                 ORDER BY UPPER(t.TAGPS_NOM_TAG) FOR XML PATH(''), TYPE).value('.', 'varchar(max)'), 1, 2, ''), '')
            FROM v2.IMPRIMANTE_EDITION AS e
        ) AS x
        WHERE 1 = 1 /*filtres*/
        ORDER BY x.Lot, x.Nom
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("(x.Nom LIKE @texte OR x.Physique LIKE @texte)", ("@texte", FormeLigne.Contient(recherche.Texte)));
        if (recherche.Critere(CritereLot) is { } lot) f.Ajouter("x.Lot = @lot", ("@lot", lot.Trim()));
        if (recherche.Critere(CriterePhysique) is { } physique) f.Ajouter("x.Physique LIKE @physique", ("@physique", FormeLigne.Contient(physique)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), r => Ligne(Lue(r)), ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static Imprimante Lue(SqlDataReader r) => new(FormeLigne.Texte(r, "Lot"), FormeLigne.Texte(r, "Nom"), FormeLigne.Texte(r, "Physique"),
        r["Differee"] is true, r["Active"] is true, FormeLigne.Texte(r, "Types"), r["Amitel"] is true);

    /// <summary>La regle d'etat (en tete du fichier).</summary>
    internal static string Etat(Imprimante i) => i.Physique.Length == 0 ? "AFaire" : !i.Active ? "EnCours" : "Validee";

    private static LigneLue Ligne(Imprimante i)
    {
        var precisions = FormeLigne.Joindre(
            i.Physique.Length > 0 ? $"vers {i.Physique}" : "sans imprimante physique",
            i.Lot == Lot1 ? (i.Differee ? "utilisable pour les différés" : null) : i.TypesPapier.Length > 0 ? $"papier {i.TypesPapier}" : "aucun type de papier",
            i.Lot == Lot1 && !i.Active ? "inactive" : null, i.ReserveeAmitel ? "réservée à Amitel" : null);
        return new LigneLue(i.Nom, precisions, i.Lot, Etat(i), [])
        {
            Id = i.Id,
            Valeurs = [i.Nom, i.Physique, i.Lot == Lot1 ? FormeLigne.Case(i.Differee) : "", i.Lot == Lot1 ? FormeLigne.Case(i.Active) : "", i.TypesPapier, i.Lot],
        };
    }

    /// <summary>L'imprimante <paramref name="id"/> (« 1:<nom> » ou « 2:<nom> ») ; null si rien ne le porte. Lue par l'ecrivain dans la
    /// transaction du geste (<paramref name="lire"/>), sous verrou.</summary>
    internal static async Task<Imprimante?> LireAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, string id)
    {
        if (id.Length < 3 || id[1] != ':' || id[0] is not ('1' or '2')) return null;
        var lot = id[0] == '1' ? Lot1 : Lot2;
        await using var r = await lire(Requete.Replace("TOP (@plafond) ", "", StringComparison.Ordinal)
                .Replace("FROM v2.IMPRIMANTE_PS AS p", "FROM v2.IMPRIMANTE_PS AS p WITH (UPDLOCK)", StringComparison.Ordinal)
                .Replace("FROM v2.IMPRIMANTE_EDITION AS e", "FROM v2.IMPRIMANTE_EDITION AS e WITH (UPDLOCK)", StringComparison.Ordinal)
                .Replace("/*filtres*/", " AND x.Lot = @lot AND x.Nom = @nom", StringComparison.Ordinal),
            [new SqlParameter("@lot", lot), new SqlParameter("@nom", id[2..])]);
        return await r.ReadAsync() ? Lue(r) : null;
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (await LireAsync(async (sql, p) => await LectureV2.LireAsync(baseGreffe, sql, ct, p), id) is not { } i) return null;
        // Le nom ne se change pas (ultNomImprimante.MetierReadOnly = Imprimante != null, OPE_SAISIE_IMP_LOT1.cs:110,
        // OPE_SAISIE_IMP_LOT2.cs:121) ; le lot est la table de la ligne. Lot 1 : differe et active ; lot 2 : les types de papier.
        static ChampFiche M(string libelle, string valeur, bool modifiable) => new(libelle, valeur) { Modifiable = modifiable };
        var lot1 = i.Lot == Lot1;
        return new FicheEcran(Procedure, id, i.Nom,
        [
            new(SectionImprimante, [M(ColNom, i.Nom, false), M(ColLot, i.Lot, false), M(ColPhysique, i.Physique, true)]),
            new(SectionLot1, [M(ColDifferee, lot1 ? FormeLigne.Case(i.Differee) : "", lot1), M(ColActive, lot1 ? FormeLigne.Case(i.Active) : "", lot1)]),
            new(SectionLot2, [M(ColTypes, i.TypesPapier, !lot1)]),
        ]);
    }
}
