using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Débiteurs multiples » (PROC_DEBITEURS_MULTIPLES, dans l'ecran reuni « Modifier un lot de PC ») : les debiteurs des
/// procedures collectives, le principal (porte par la PC) et les extensions (DEBITEUR_AUTRE). Mode d'emploi : LecteurEcran.cs ;
/// le geste : EcrivainDebiteursMultiples.cs. Plan « RIG v3 100 % fonctionnel », lot 4, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_DEBITEURS_MULTIPLES (le Detail OPE de proc_debiteurs_multiples.md fait foi). RIG v2 l'ouvre sur UNE
/// PC recue en parametre (IdPC obligatoire, PROC_DEBITEURS_MULTIPLES.cs:36-40) : une etape pour le debiteur principal (la PC
/// elle-meme, :47-49) et une par extension (ListeDebiteursPCAutresWithDeleted, :51-55 ; table DEBITEUR_AUTRE, colonnes DBTAT_*,
/// RigBaseGreffe\GREFFE\DEBITEUR_AUTRE.cs). La V3 liste les PC du greffe, chacune avec son principal puis ses extensions, et
/// ouvre la fiche d'un debiteur. ORDRE : PC par numero decroissant (comme LecteurProceduresCollectives), principal d'abord, puis
/// les extensions par DBTAT_NUM_ORDRE (ETP_DEBITEUR.cs:50-62). Les PC sans extension ont leur seul principal.
///
/// GROUPES (descripteur) : « Débiteur principal » / « Débiteurs additionnels ».
/// REGLE D'ETAT : le principal est « Principal (lecture) » (EnAttente) -- RIG v2 le montre en lecture seule dans cet ecran
/// (MAJAffichage, OPE_DEBITEUR.cs:140-150) ; une extension enregistree (une ligne DEBITEUR_AUTRE : entreprise, nom, adresse, date
/// et lien obligatoires, NOT NULL dans RIG_DEV) est « Enregistré » (Validee). « À compléter » et « En cours » sont l'extension
/// vierge que RIG v2 ouvre pour une saisie (:52-53) : la V3 ne cree pas d'extension (EcrivainDebiteursMultiples).
///
/// RECHERCHE (IRechercheEcran) : RIG v2 n'a pas de recherche (la PC lui est passee). Les deux criteres de l'ecran sont ceux de la
/// recherche de PC de MPC, d'ou part ce processus : « Numéro de PC » (PRCLL_NUM_PC) et « Débiteur » (le nom du principal ou d'une
/// extension) ; le champ de recherche cherche l'un OU l'autre.
///
/// FICHE (IFicheEcran) : les champs de OPE_DEBITEUR, a ses titres (OPE_DEBITEUR.Designer.cs:93-194) : Entreprise, Identité (nom,
/// adresse, date d'extension, lien), Registre des métiers (case, numero, departement, RM dependant) et Enregistrement (le nom de
/// l'affaire et la grappe des debiteurs de la PC, ce que la validation recalcule). Modifiables sur une extension : date, lien et
/// registre des metiers (EcrivainDebiteursMultiples) ; le principal s'ouvre en lecture seule, motif a l'appui.
/// </summary>
internal sealed class LecteurDebiteursMultiples : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_DEBITEURS_MULTIPLES";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Principal = "Débiteur principal", Additionnels = "Débiteurs additionnels";

    internal const string CritereNumero = "Numéro de PC", CritereDebiteur = "Débiteur";

    /// <summary>Les sections et champs du descripteur (ModifierAffaire.cs), aux titres de OPE_DEBITEUR.</summary>
    internal const string SectionEntreprise = "Entreprise", SectionIdentite = "Identité", SectionRm = "Registre des métiers",
                          SectionEnregistrement = "Enregistrement";

    internal const string ChampEntreprise = "Entreprise reliée à la procédure collective", ChampNom = "Nom du débiteur", ChampAdresse = "Adresse",
                          ChampDateExtension = "Date d'extension", ChampLien = "Lien débiteur principal", ChampInscrit = "Inscrit au RM",
                          ChampNumRm = "N° au registre des métiers", ChampDepartement = "Département immat. RM", ChampDependant = "RM dépendant",
                          ChampNomAffaire = "Nom de l'affaire", ChampGrappe = "Débiteurs de la procédure";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero, CritereDebiteur];

    // Le nom du principal : PRCLL_NOM_DEBITEUR_PRINCIPAL, amorce du nom de l'affaire s'il est vide (OPE_DEBITEUR.cs:92-94).
    private const string Debiteurs = """
        SELECT 'P' AS Lettre, p.PRCLL_ID_PRCLL AS Id, p.PRCLL_ID_PRCLL AS IdPc, 0 AS Ordre,
               COALESCE(p.PRCLL_NOM_DEBITEUR_PRINCIPAL, p.PRCLL_NOM_AFFAIRE) AS Nom, NULL AS Lien, NULL AS Extension
        FROM v2.PROCEDURE_COLLECTIVE AS p
        UNION ALL
        SELECT 'D', d.DBTAT_ID_DBTAT, d.DBTAT_ID_PRCLL, d.DBTAT_NUM_ORDRE, d.DBTAT_NOM_DEBITEUR,
               COALESCE(lp.R_LIENPRINCI_LIBELLE_ECRAN, d.DBTAT_LIEN_PRINCIPALE), d.DBTAT_DATE_EXTENSION
        FROM v2.DEBITEUR_AUTRE AS d
        LEFT JOIN v2.LIEN_PRINCIPALE AS lp ON lp.R_LIENPRINCI_VALEUR = d.DBTAT_LIEN_PRINCIPALE
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) x.Lettre, x.Id, x.Nom, x.Lien, x.Extension, p.PRCLL_NUM_PC AS NumPc,
               (SELECT COUNT(*) FROM v2.DEBITEUR_AUTRE AS n WHERE n.DBTAT_ID_PRCLL = p.PRCLL_ID_PRCLL) AS Extensions,
               COUNT(*) OVER () AS Total
        FROM ({Debiteurs}) AS x
        JOIN v2.PROCEDURE_COLLECTIVE AS p ON p.PRCLL_ID_PRCLL = x.IdPc
        WHERE 1 = 1 /*recherche*/
        ORDER BY p.PRCLL_NUM_PC DESC, p.PRCLL_ID_PRCLL DESC, x.Ordre, x.Id
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        static string Numero(string v) => FormeLigne.Contient(v.Replace(" ", "", StringComparison.Ordinal));
        // Le nom d'un debiteur de la PC (principal ou extension) : la ligne de la PC entiere repond.
        const string NomDansLaPc = """
            (COALESCE(p.PRCLL_NOM_DEBITEUR_PRINCIPAL, p.PRCLL_NOM_AFFAIRE) LIKE {0}
             OR EXISTS (SELECT 1 FROM v2.DEBITEUR_AUTRE AS n WHERE n.DBTAT_ID_PRCLL = p.PRCLL_ID_PRCLL AND n.DBTAT_NOM_DEBITEUR LIKE {0}))
            """;
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add($"(p.PRCLL_NUM_PC LIKE @texteNumero OR {string.Format(CultureInfo.InvariantCulture, NomDansLaPc, "@texteNom")})");
            parametres.Add(new SqlParameter("@texteNumero", Numero(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteNom", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereNumero) is { } numero)
        {
            filtres.Add("p.PRCLL_NUM_PC LIKE @numero");
            parametres.Add(new SqlParameter("@numero", Numero(numero)));
        }
        if (recherche.Critere(CritereDebiteur) is { } debiteur)
        {
            filtres.Add(string.Format(CultureInfo.InvariantCulture, NomDansLaPc, "@debiteur"));
            parametres.Add(new SqlParameter("@debiteur", FormeLigne.Contient(debiteur)));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var principal = FormeLigne.Texte(r, "Lettre") == "P";
        var numPc = FormeLigne.Texte(r, "NumPc");
        var id = FormeLigne.Texte(r, "Id");
        var extensions = Convert.ToInt32(r["Extensions"], CultureInfo.InvariantCulture);
        var pc = numPc.Length > 0 ? $"PC {numPc}" : null;
        var precisions = principal
            ? FormeLigne.Joindre(pc, "débiteur principal", extensions > 0 ? $"{extensions} extension{(extensions > 1 ? "s" : "")}" : "sans extension")
            : FormeLigne.Joindre(pc, FormeLigne.Texte(r, "Lien"), FormeLigne.DateDe(r, "Extension") is { } d ? $"étendue le {FormeLigne.Date(d)}" : null);
        var nom = FormeLigne.Texte(r, "Nom") is { Length: > 0 } n ? n : principal ? $"PC n° {(numPc.Length > 0 ? numPc : id)}" : $"Débiteur n° {id}";
        return new LigneLue(nom, precisions, principal ? Principal : Additionnels, principal ? "EnAttente" : "Validee", [])
        {
            Id = $"{(principal ? 'P' : 'D')}-{id}",
        };
    }

    // ------------------------------------------------------------------ LE DEBITEUR ET SA FICHE

    /// <summary>Un debiteur tel que la fiche et l'ecrivain le lisent : sa PC, et pour une extension ses colonnes DBTAT_*.</summary>
    internal sealed record DebiteurLu(bool Principal, int Id, int IdPc, string NumPc, string NomAffaire, string Nom, string Entreprise, string Adresse,
                                      DateTime? Extension, string Lien, bool InscritRm, string NumRm, string Departement, string Dependant)
    {
        /// <summary>« le débiteur ALPHA de la PC 2026P00012 », pour les messages et le journal.</summary>
        public string EnMots => $"le débiteur {Nom} de la PC {(NumPc.Length > 0 ? NumPc : $"n° {IdPc}")}";
    }

    // L'entreprise : le dossier RCS (rec.DossierRcs, REC-012) ou le non-inscrit (v2.NON_INSCRIT) relie. L'adresse : rec.Adresse
    // (ADRESSE_BASE, REC-016), en une ligne.
    private const string Adresse = """
        LTRIM(RTRIM(CONCAT(a.NumeroVoie, ' ', a.TypeVoie, ' ', a.NomVoie, ' ', a.CodePostal, ' ', a.Localite)))
        """;

    private const string FichePrincipal = $"""
        SELECT p.PRCLL_ID_PRCLL AS Id, p.PRCLL_ID_PRCLL AS IdPc, p.PRCLL_NUM_PC AS NumPc, p.PRCLL_NOM_AFFAIRE AS NomAffaire,
               COALESCE(p.PRCLL_NOM_DEBITEUR_PRINCIPAL, p.PRCLL_NOM_AFFAIRE) AS Nom, NULL AS Dossier, NULL AS NonInscrit,
               {Adresse} AS Adresse, NULL AS Extension, NULL AS Lien, p.PRCLL_INSCRIT_REGISTRE_METIER AS InscritRm,
               p.PRCLL_NUM_RM AS NumRm, p.PRCLL_DEPARTEMENT_RM AS Departement, p.PRCLL_RM_DEPENDANT AS Dependant
        FROM v2.PROCEDURE_COLLECTIVE AS p
        LEFT JOIN rec.Adresse AS a ON a.Id = p.PRCLL_ID_ADRBS
        WHERE p.PRCLL_ID_PRCLL = @id
        """;

    private const string FicheExtension = $"""
        SELECT d.DBTAT_ID_DBTAT AS Id, d.DBTAT_ID_PRCLL AS IdPc, p.PRCLL_NUM_PC AS NumPc, p.PRCLL_NOM_AFFAIRE AS NomAffaire,
               d.DBTAT_NOM_DEBITEUR AS Nom, dr.NumeroGestion AS Dossier, ni.NINSC_NUM_NON_INSCRIT AS NonInscrit,
               {Adresse} AS Adresse, d.DBTAT_DATE_EXTENSION AS Extension,
               COALESCE(lp.R_LIENPRINCI_LIBELLE_ECRAN, d.DBTAT_LIEN_PRINCIPALE) AS Lien, d.DBTAT_INSCRIT_REGISTRE_METIER AS InscritRm,
               d.DBTAT_NUM_RM AS NumRm, d.DBTAT_DEPARTEMENT_RM AS Departement, d.DBTAT_RM_DEPENDANT AS Dependant
        FROM v2.DEBITEUR_AUTRE AS d /*verrou*/
        JOIN v2.PROCEDURE_COLLECTIVE AS p ON p.PRCLL_ID_PRCLL = d.DBTAT_ID_PRCLL
        LEFT JOIN v2.LIEN_PRINCIPALE AS lp ON lp.R_LIENPRINCI_VALEUR = d.DBTAT_LIEN_PRINCIPALE
        LEFT JOIN rec.DossierRcs AS dr ON dr.Id = d.DBTAT_ID_DSRCS
        LEFT JOIN v2.NON_INSCRIT AS ni ON ni.NINSC_ID_NINSC = d.DBTAT_ID_NINSC
        LEFT JOIN rec.Adresse AS a ON a.Id = d.DBTAT_ID_ADRBS
        WHERE d.DBTAT_ID_DBTAT = @id
        """;

    /// <summary>Le debiteur <paramref name="id"/> (« P-12 » le principal de la PC 12, « D-7 » l'extension 7) ; null si absent.
    /// Dans la transaction d'un geste (<paramref name="tx"/>), une extension est verrouillee en ecriture jusqu'a sa fin.</summary>
    internal static async Task<DebiteurLu?> LireDebiteurAsync(SqlConnection cn, SqlTransaction? tx, string id, CancellationToken ct)
    {
        if (id.Length < 3 || id[1] != '-' || id[0] is not ('P' or 'D')
            || !int.TryParse(id[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        var principal = id[0] == 'P';
        var sql = principal ? FichePrincipal : FicheExtension.Replace("/*verrou*/", tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)");
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", n);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var dossier = FormeLigne.Texte(r, "Dossier");
        var nonInscrit = FormeLigne.Texte(r, "NonInscrit");
        var entreprise = dossier.Length > 0 ? $"Dossier RCS {FormeLigne.NumeroGestion(dossier)}" : nonInscrit.Length > 0 ? $"Non-inscrit {nonInscrit}" : "";
        return new DebiteurLu(principal, n, (int)r["IdPc"], FormeLigne.Texte(r, "NumPc"), FormeLigne.Texte(r, "NomAffaire"), FormeLigne.Texte(r, "Nom"),
            entreprise, FormeLigne.Texte(r, "Adresse"), FormeLigne.DateDe(r, "Extension"), FormeLigne.Texte(r, "Lien"),
            r["InscritRm"] is true, FormeLigne.Texte(r, "NumRm"), FormeLigne.Texte(r, "Departement"), FormeLigne.Texte(r, "Dependant"));
    }

    // La grappe des debiteurs de la PC, telle que la validation de RIG v2 la relit pour le nom de l'affaire
    // (UpdateNomAffaire_FromDebiteursAutresChanged, ProcedureCollective.cs:3602-3693).
    private const string Grappe = """
        SELECT d.DBTAT_NUM_ORDRE AS Ordre, d.DBTAT_NOM_DEBITEUR AS Nom, COALESCE(lp.R_LIENPRINCI_LIBELLE_ECRAN, d.DBTAT_LIEN_PRINCIPALE) AS Lien,
               d.DBTAT_DATE_EXTENSION AS Extension
        FROM v2.DEBITEUR_AUTRE AS d
        LEFT JOIN v2.LIEN_PRINCIPALE AS lp ON lp.R_LIENPRINCI_VALEUR = d.DBTAT_LIEN_PRINCIPALE
        WHERE d.DBTAT_ID_PRCLL = @pc
        ORDER BY d.DBTAT_NUM_ORDRE, d.DBTAT_ID_DBTAT
        """;

    /// <summary>Le motif de la lecture seule du principal (MAJAffichage, OPE_DEBITEUR.cs:140-150 ; son nom reecrit le nom de
    /// l'affaire, ProcedureCollective.cs:3609-3614).</summary>
    internal const string MotifPrincipal =
        "le débiteur principal se lit ici sans se modifier : RIG v2 n'y laisse changer que son nom, qui réécrit le nom de l'affaire, " +
        "ce que la V3 ne reprend pas";

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (await LireDebiteurAsync(baseGreffe, null, id, ct) is not { } d) return null;
        var grappe = new List<string[]>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, Grappe, ct, new SqlParameter("@pc", d.IdPc)))
            while (await r.ReadAsync(ct))
                grappe.Add([FormeLigne.Texte(r, "Ordre"), FormeLigne.Texte(r, "Nom"), FormeLigne.Texte(r, "Lien"), FormeLigne.Date(FormeLigne.DateDe(r, "Extension"))]);
        var extension = !d.Principal;
        ChampFiche Champ(string libelle, string valeur, bool modifiable = false) => new(libelle, valeur) { Modifiable = modifiable && extension };
        return new FicheEcran(Procedure, id, d.Nom.Length > 0 ? d.Nom : $"Débiteur n° {d.Id}",
        [
            new SectionFiche(SectionEntreprise, [Champ(ChampEntreprise, d.Entreprise)]),
            new SectionFiche(SectionIdentite,
            [
                Champ(ChampNom, d.Nom), Champ(ChampAdresse, d.Adresse),
                Champ(ChampDateExtension, FormeLigne.DateFiche(d.Extension), modifiable: true), Champ(ChampLien, d.Lien, modifiable: true),
            ]),
            new SectionFiche(SectionRm,
            [
                Champ(ChampInscrit, FormeLigne.Case(d.InscritRm), modifiable: true), Champ(ChampNumRm, d.NumRm, modifiable: true),
                Champ(ChampDepartement, d.Departement, modifiable: true), Champ(ChampDependant, d.Dependant, modifiable: true),
            ]),
            new SectionFiche(SectionEnregistrement,
            [
                Champ(ChampNomAffaire, d.NomAffaire),
                new(ChampGrappe, "") { Colonnes = ["N° d'ordre", "Nom du débiteur", "Lien", "Date d'extension"], Lignes = [.. grappe] },
            ]),
        ])
        {
            LectureSeule = d.Principal,
            Motif = d.Principal ? MotifPrincipal : null,
        };
    }
}
