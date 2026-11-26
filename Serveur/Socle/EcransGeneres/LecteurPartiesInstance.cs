using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Parties d'une instance » (PROC_PARTIES, dans l'ecran reuni « Corriger une affaire judiciaire ») : les parties des instances
/// judiciaires, une recherche par instance, qualite et aide juridictionnelle, et la FICHE d'une partie. Modes d'emploi :
/// LecteurEcran.cs, EcrivainPartiesInstance.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_PARTIES (D:\RigApplication\Source\RIG\DLL\Processus\PROC_PARTIES, fiche proc_parties.md, le
/// « Detail OPE » fait foi) ouvre UNE instance (parametre IdInstance) et range ses parties en trois etapes ; la saisie d'une
/// partie est OPE_PARTIE (Rig_Etape_Instance\INSTANCE\Saisie\ETP_PARTIE\OPE_PARTIE.cs).
///
/// FILTRE DE LA LISTE : les parties que l'instance montre, Instance.ListeParties (Instance.cs:1232-1256) : une designation
/// (PRT_DESIGNATION non nulle) et une qualite (PRT_QUALIF_PARTIE non vide) ; reparties par la PREMIERE lettre de la qualite
/// (Instance.cs:1264-1282) : « D » demandeurs (DOIP compris), « F » defendeurs (FOIP compris), « I » intervenants. Toute
/// instance : PROC_PARTIES ne filtre pas le type de l'instance qu'il recoit. ORDRE : numero de role decroissant (comme la
/// recherche d'instance, FormRechercheInstance.cs:633), puis demandeurs, defendeurs, intervenants (l'ordre des etapes,
/// PROC_PARTIES.Designer.cs:27-29), puis PRT_NUM_ORDRE.
///
/// GROUPES (descripteur) : « Demandeurs », « Défendeurs », « Intervenants » -- l'etape de la partie.
///
/// REGLE D'ETAT : « Enregistrée » (Validee) quand cet ecran a enregistre la partie (un geste « a modifie » au journal des
/// gestes, soc.Journal) ; « À compléter » (AFaire) sinon. La V2 n'a pas d'etat par partie (la demande PARTIES n'est
/// qu'une trace de l'instance entiere, MetierFacture = false, PROC_PARTIES.Designer.cs:83) ; « En cours » n'a pas de ligne :
/// PARTIES n'interrompt pas (MetierInterrompable = false, :84).
///
/// RECHERCHE (IRechercheEcran) : « Numéro de rôle de l'instance » (INSTN_NUM_ROLE, blancs retires comme FormateNumRole,
/// FormRechercheInstance.cs:996) ; « Qualité de la partie » cherchee dans le nom de l'etape (« déf » trouve les
/// defendeurs) ; « Aide juridictionnelle » : « oui » = AJ totale ou partielle (la case AJ que OPE_PARTIE coche,
/// OPE_PARTIE.cs:421-428), « non » = ni l'une ni l'autre. Le champ de recherche cherche la designation OU le numero de role.
///
/// FICHE (IFicheEcran) : Identité (le nom = la designation, txtDesignation -> PRT_DESIGNATION, OPE_PARTIE.cs:387 ; le titre de
/// la fiche la porte avec sa civilite comme la recherche d'instance l'ecrit, FormRechercheInstance.cs:318-333 ; adresse : PRT_ID_ADRBS, l'ADRESSE_BASE de la V2 reprise en rec.Adresse, FichesV3.cs --
/// fix-ok: mesure le 26/11/2026, fiche 4938 sur la copie de dev en erreur technique : la base V3 n'a pas de v2.ADRESSE_BASE), Mandataire et répertorié (le mandataire
/// postulant PRT_ID_MNDTR_POS ; la case « Répertorié » = PRT_ID_PRTRP renseigne), Aide juridictionnelle (OPE_PARTIE.cs:390-428).
/// Le nom et l'aide juridictionnelle se modifient (EcrivainPartiesInstance, qui dit pourquoi le reste de OPE_PARTIE ne l'est pas).
///
/// VERROU : aucun. PROC_PARTIES ne verrouille pas l'instance (aucun Lock ni GetLock dans PROC_PARTIES ni ETP_PARTIE).
/// </summary>
internal sealed class LecteurPartiesInstance : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_PARTIES";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Demandeurs = "Demandeurs", Defendeurs = "Défendeurs", Intervenants = "Intervenants";

    /// <summary>Les criteres du descripteur cherches dans la base.</summary>
    internal const string CritereRole = "Numéro de rôle de l'instance", CritereQualite = "Qualité de la partie", CritereAj = "Aide juridictionnelle";

    /// <summary>Les sections et champs de la fiche, aux titres et libelles du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionIdentite = "Identité", SectionMandataire = "Mandataire et répertorié", SectionAj = "Aide juridictionnelle";

    internal const string ChampNom = "Nom", ChampAdresse = "Adresse", ChampMandataire = "Mandataire", ChampRepertorie = "Répertorié",
                          ChampAj = "Aide juridictionnelle", ChampPartielle = "Partielle", ChampTotale = "Totale", ChampNumeroAj = "Numéro d'AJ";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereRole, CritereQualite, CritereAj];

    internal static string Groupe(string qualite) => qualite.StartsWith('D') ? Demandeurs : qualite.StartsWith('F') ? Defendeurs : Intervenants;

    private static string Qualite(string qualite) => qualite.StartsWith('D') ? "Demandeur" : qualite.StartsWith('F') ? "Défendeur" : "Intervenant";

    private const string Requete = """
        SELECT TOP (@plafond) p.PRT_ID_PRT, p.PRT_CIVILITE, p.PRT_DESIGNATION, p.PRT_QUALIF_PARTIE, p.PRT_AIDE_JURIDIC_TOTALE,
               p.PRT_AIDE_JURIDIC_PARTIELLE, p.PRT_ID_PRTRP, i.INSTN_NUM_ROLE,
               CASE WHEN EXISTS (SELECT 1 FROM soc.Journal AS j
                                 WHERE j.Ecran = @ecran AND j.Verbe = 'a modifie' AND j.IdObjet = CONVERT(varchar(40), p.PRT_ID_PRT))
                    THEN 1 ELSE 0 END AS Enregistree,
               COUNT(*) OVER () AS Total
        FROM v2.PARTIE AS p
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = p.PRT_ID_INSTN
        WHERE p.PRT_DESIGNATION IS NOT NULL AND LEFT(p.PRT_QUALIF_PARTIE, 1) IN ('D', 'F', 'I') /*recherche*/
        ORDER BY i.INSTN_NUM_ROLE DESC, i.INSTN_ID_INSTN DESC, CHARINDEX(LEFT(p.PRT_QUALIF_PARTIE, 1), 'DFI'), p.PRT_NUM_ORDRE, p.PRT_ID_PRT
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ecran", EcrivainPartiesInstance.NomEcran));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@ecran", EcrivainPartiesInstance.NomEcran) };
        var filtres = new List<string>();
        string Role(string v) => FormeLigne.Contient(v.Replace(" ", "", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(p.PRT_DESIGNATION LIKE @texteNom OR i.INSTN_NUM_ROLE LIKE @texteRole)");
            parametres.Add(new SqlParameter("@texteNom", FormeLigne.Contient(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteRole", Role(recherche.Texte)));
        }
        if (recherche.Critere(CritereRole) is { } role)
        {
            filtres.Add("i.INSTN_NUM_ROLE LIKE @role");
            parametres.Add(new SqlParameter("@role", Role(role)));
        }
        if (recherche.Critere(CritereQualite) is { } qualite)
        {
            var lettres = new[] { ('D', Demandeurs), ('F', Defendeurs), ('I', Intervenants) }
                .Where(q => CultureInfo.InvariantCulture.CompareInfo.IndexOf(q.Item2, qualite.Trim(),
                            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                .Select(q => $"'{q.Item1}'").ToList();
            filtres.Add(lettres.Count == 0 ? "1 = 0" : $"LEFT(p.PRT_QUALIF_PARTIE, 1) IN ({string.Join(", ", lettres)})");
        }
        if (recherche.Critere(CritereAj) is { } aj)
        {
            var avecAj = "(p.PRT_AIDE_JURIDIC_TOTALE = 1 OR p.PRT_AIDE_JURIDIC_PARTIELLE = 1)";
            filtres.Add(aj.Trim().ToLowerInvariant() is "oui" or "o" or "1" ? avecAj : $"NOT {avecAj}");
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    /// <summary>La designation comme la recherche d'instance l'ecrit : civilite (sauf PM et ND) puis designation.</summary>
    internal static string Nom(string civilite, string designation)
        => civilite is "" or "PM" or "ND" ? designation : $"{civilite} {designation}";

    private static string Aj(bool totale, bool partielle) => totale ? "AJ totale" : partielle ? "AJ partielle" : "";

    private static LigneLue Ligne(SqlDataReader r)
    {
        var qualite = FormeLigne.Texte(r, "PRT_QUALIF_PARTIE");
        var precisions = FormeLigne.Joindre(
            $"{Qualite(qualite)} · instance {FormeLigne.Texte(r, "INSTN_NUM_ROLE")}",
            Aj(r["PRT_AIDE_JURIDIC_TOTALE"] is true, r["PRT_AIDE_JURIDIC_PARTIELLE"] is true),
            r["PRT_ID_PRTRP"] is DBNull ? null : "répertorié");
        var enregistree = Convert.ToInt32(r["Enregistree"], CultureInfo.InvariantCulture) == 1;
        return new LigneLue(Nom(FormeLigne.Texte(r, "PRT_CIVILITE"), FormeLigne.Texte(r, "PRT_DESIGNATION")), precisions,
                            Groupe(qualite), enregistree ? "Validee" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "PRT_ID_PRT"),
        };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>La partie et ce que l'ecrivain doit savoir d'elle.</summary>
    internal sealed record PartieLue(int Id, int IdInstance, string Role, string Qualite, string Nom, string Adresse, string Mandataire,
                                     bool Repertorie, bool Totale, bool Partielle, string NumeroAj)
    {
        /// <summary>La designation seule (PRT_DESIGNATION), sans la civilite que <see cref="Nom"/> lui ajoute.</summary>
        public string Designation { get; init; } = "";

        /// <summary>La case AJ telle que OPE_PARTIE la coche au chargement : AJ totale ou partielle (OPE_PARTIE.cs:421-428).</summary>
        public bool Aj => Totale || Partielle;
    }

    /// <summary>La partie <paramref name="id"/>, lue sur <paramref name="cn"/> (dans la transaction d'un geste si
    /// <paramref name="tx"/>, verrouillee en ecriture jusqu'a sa fin) ; null si elle n'existe pas ou n'est pas dans la liste.</summary>
    internal static async Task<PartieLue?> LirePartieAsync(SqlConnection cn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var sql = $"""
            SELECT p.PRT_ID_INSTN, i.INSTN_NUM_ROLE, p.PRT_QUALIF_PARTIE, p.PRT_CIVILITE, p.PRT_DESIGNATION, p.PRT_ID_PRTRP,
                   p.PRT_AIDE_JURIDIC_TOTALE, p.PRT_AIDE_JURIDIC_PARTIELLE, p.PRT_AIDE_JURIDIC_NUM,
                   ad.NumeroVoie, ad.ComplementNumero, ad.TypeVoie, ad.NomVoie, ad.CodePostal, ad.Localite,
                   m.MNDTR_ABREGE, m.MNDTR_NOM
            FROM v2.PARTIE AS p {(tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")}
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = p.PRT_ID_INSTN
            LEFT JOIN rec.Adresse AS ad ON ad.Id = p.PRT_ID_ADRBS
            LEFT JOIN v2.MANDATAIRE AS m ON m.MNDTR_ID_MNDTR = p.PRT_ID_MNDTR_POS
            WHERE p.PRT_ID_PRT = @id AND p.PRT_DESIGNATION IS NOT NULL AND LEFT(p.PRT_QUALIF_PARTIE, 1) IN ('D', 'F', 'I')
            """;
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var voie = string.Join(" ", new[] { "NumeroVoie", "ComplementNumero", "TypeVoie", "NomVoie" }
            .Select(c => FormeLigne.Texte(r, c)).Where(t => t.Length > 0));
        var ville = string.Join(" ", new[] { "CodePostal", "Localite" }.Select(c => FormeLigne.Texte(r, c)).Where(t => t.Length > 0));
        return new PartieLue(id, Convert.ToInt32(r["PRT_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
            FormeLigne.Texte(r, "PRT_QUALIF_PARTIE"), Nom(FormeLigne.Texte(r, "PRT_CIVILITE"), FormeLigne.Texte(r, "PRT_DESIGNATION")),
            FormeLigne.Joindre(voie, ville), FormeLigne.Joindre(FormeLigne.Texte(r, "MNDTR_ABREGE"), FormeLigne.Texte(r, "MNDTR_NOM")),
            r["PRT_ID_PRTRP"] is not DBNull, r["PRT_AIDE_JURIDIC_TOTALE"] is true, r["PRT_AIDE_JURIDIC_PARTIELLE"] is true,
            FormeLigne.Texte(r, "PRT_AIDE_JURIDIC_NUM")) { Designation = FormeLigne.Texte(r, "PRT_DESIGNATION") };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        if (await LirePartieAsync(baseGreffe, null, n, ct) is not { } p) return null;
        ChampFiche Aj(string libelle, string valeur) => new(libelle, valeur) { Modifiable = true };
        return new FicheEcran(Procedure, id, p.Nom,
        [
            new SectionFiche(SectionIdentite, [new(ChampNom, p.Designation) { Modifiable = true }, new(ChampAdresse, p.Adresse)]),
            new SectionFiche(SectionMandataire, [new(ChampMandataire, p.Mandataire), new(ChampRepertorie, FormeLigne.Case(p.Repertorie))]),
            new SectionFiche(SectionAj,
            [
                Aj(ChampAj, FormeLigne.Case(p.Aj)), Aj(ChampPartielle, FormeLigne.Case(p.Partielle)),
                Aj(ChampTotale, FormeLigne.Case(p.Totale)), Aj(ChampNumeroAj, p.NumeroAj),
            ]),
        ]);
    }
}
