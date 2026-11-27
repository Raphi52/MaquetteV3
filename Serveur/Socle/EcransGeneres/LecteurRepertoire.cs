using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Répertoire » (PROC_MODIF_REPER, dans l'ecran reuni « Corriger une affaire judiciaire ») : les repertories du greffe (les
/// tiers recurrents des parties), une recherche par abrege, designation et activite, et la FICHE d'un repertorie. Modes
/// d'emploi : LecteurEcran.cs, EcrivainRepertoire.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MODIF_REPER (D:\RigApplication\Source\RIG\DLL\Processus\PROC_MODIF_REPER, fiche
/// proc_modif_reper.md, le « Detail OPE » fait foi) : une liste de recherche (OPE_RECHERCHE_MODIF_REPER), la fiche d'UN
/// repertorie dessous (OPE_EDITION_MODIF_REPER) et la civilite des parties liees (OPE_EDITION_PARTIES). LA FORME RESTE FICHE
/// (revue demandee par le chef) : la V2 modifie un repertorie a la fois dans sa fiche sous la liste, comme une Fiche de la V3 ;
/// et l'ecran reuni « Corriger une affaire judiciaire » est une liste commune de Fiches (tests/EcransGeneresCheck,
/// Program.cs:493).
///
/// FILTRE DE LA LISTE : tous les repertories (Repertorie.GetAllRepertorie, OPE_RECHERCHE_MODIF_REPER.cs:180), par abrege
/// (:181).
///
/// GROUPES (descripteur, alignes sur la V2) : « Actifs » / « Inactifs » -- PRTRP_ACTIF, la seule distinction que la liste de
/// la V2 fait (case « actifs uniquement », OPE_RECHERCHE_MODIF_REPER.cs:278-281).
///
/// REGLE D'ETAT : « Enregistré » (Validee) quand cet ecran a enregistre le repertorie (geste « a modifie » au journal des
/// gestes) ; « À corriger » (AFaire) sinon. La V2 n'a pas d'etat : sa demande MODIFREPER est creee et cloturee d'un coup
/// (GestionDemande, OPE_EDITION_MODIF_REPER.cs:314-318).
///
/// RECHERCHE (IRechercheEcran) : « Abrégé » et « Désignation » sont les deux colonnes que la V2 indexe
/// (PRTRP_ABREGE + " " + PRTRP_DESIGNATION, :153) ; la V3 les cherche en LIKE (la V2 en plein texte en memoire, :273) ;
/// « Répertoriés actifs seulement » (« oui ») garde les actifs (:278-281). Le champ de recherche cherche l'un OU l'autre.
///
/// FICHE (IFicheEcran) : Fiche (abrege, designation, adresse mail, civilite, actif, adresse, mode de presentation, client et
/// son nom, texte d'assistance, puis les dix autres zones -- edition jugement, forme juridique, organe representatif, N° Siren,
/// date de naissance, nationalite, profession, telephone, fax, agissant en qualite de : OPE_EDITION_MODIF_REPER.cs:401-420, dans
/// l'ordre de saisie de la V2 (TabIndex du Designer) ; mandataire postulant : ope_postulant, OPE_POSTULANT.cs:67 --
/// l'adresse est la fiche reprise rec.Adresse, le client rec.Client, comme LecteurPartiesInstance ; le client et le
/// mandataire par leur abrege, la valeur que l'on saisit pour les changer),
/// Civilité des parties (les parties liees, PartieRepertorie.GetPartiesParRepertorieAvecNumeroAffaire, PartieRepertorie.cs:52-61,
/// colonnes de DataGridViewEditionParties.cs:23-54 ; la civilite que la V2 leur appliquerait, celle du repertorie, :67-68).
///
/// VERROU : aucun. PROC_MODIF_REPER ne verrouille pas le repertorie (aucun Lock ni GetLock).
/// </summary>
internal sealed class LecteurRepertoire : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MODIF_REPER";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Actifs = "Actifs", Inactifs = "Inactifs";

    /// <summary>Les criteres du descripteur cherches dans la base.</summary>
    internal const string CritereAbrege = "Abrégé", CritereDesignation = "Désignation", CritereActifs = "Répertoriés actifs seulement";

    /// <summary>Les sections et champs de la fiche, aux titres et libelles du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionRepertorie = "Fiche", SectionParties = "Civilité des parties";

    internal const string ChampAbrege = "Abrégé", ChampDesignation = "Désignation", ChampMail = "Adresse mail", ChampCivilite = "Civilité",
                          ChampActif = "Actif", ChampParties = "Parties liées";

    // Titres du Designer de la V2 (OPE_EDITION_MODIF_REPER.Designer.cs:123, :141, :264, :275, :285) ; le mandataire postulant est
    // l'ope_postulant (Designer :291-299, « Mandataire répertorié » dans OPE_MANDATAIRE.designer.cs:211).
    internal const string ChampAdresse = "Adresse", ChampMode = "Mode présentation", ChampClient = "Client", ChampNomClient = "Nom client",
                          ChampAssistance = "Texte assistance", ChampMandataire = "Mandataire postulant";

    // Les dix autres zones de la fiche V2, liees a leur colonne par LinkDonnees (OPE_EDITION_MODIF_REPER.cs:401-412) ; titres du
    // Designer (:244, :235, :149, :226, :158, :216, :166, :200, :191, :174 -- « agissant en qualité de », capitale ajoutee).
    // fix-ok: mesure le 26/11/2026 -- au commit b604f96 ces dix zones n'etaient ni lues ni enregistrees, alors que Valider
    // (:236, _rep.Sauve) les ecrit comme les autres.
    internal const string ChampEditionJugement = "Edition jugement", ChampFormeJuridique = "Forme juridique", ChampOrgane = "Organe représentatif",
                          ChampSiren = "N° Siren", ChampNaissance = "Date de naissance", ChampNationalite = "Nationalité",
                          ChampProfession = "Profession", ChampTelephone = "Téléphone", ChampFax = "Fax", ChampQualite = "Agissant en qualité de";

    /// <summary>Les champs de la section « Fiche », dans l'ordre du descripteur ; l'adresse et le nom du client se lisent seulement.</summary>
    internal static readonly string[] ChampsFiche = [ChampAbrege, ChampDesignation, ChampMail, ChampCivilite, ChampActif, ChampAdresse, ChampMode,
                                                     ChampClient, ChampNomClient, ChampAssistance, ChampMandataire, ChampEditionJugement,
                                                     ChampFormeJuridique, ChampOrgane, ChampSiren, ChampNaissance, ChampNationalite,
                                                     ChampProfession, ChampTelephone, ChampFax, ChampQualite];

    public IReadOnlyCollection<string> Criteres { get; } = [CritereAbrege, CritereDesignation, CritereActifs];

    private const string Requete = """
        SELECT TOP (@plafond) r.PRTRP_ID_PRTRP, r.PRTRP_ABREGE, r.PRTRP_DESIGNATION, r.PRTRP_ADRESSE_MAIL, r.PRTRP_ACTIF,
               m.MNDTR_ABREGE, m.MNDTR_NOM,
               CASE WHEN EXISTS (SELECT 1 FROM soc.Journal AS j
                                 WHERE j.Ecran = @ecran AND j.Verbe = 'a modifie' AND j.IdObjet = CONVERT(varchar(40), r.PRTRP_ID_PRTRP))
                    THEN 1 ELSE 0 END AS Enregistre,
               COUNT(*) OVER () AS Total
        FROM v2.REPERTORIE AS r
        LEFT JOIN v2.MANDATAIRE AS m ON m.MNDTR_ID_MNDTR = r.PRTRP_ID_MNDTR_POS
        WHERE 1 = 1 /*recherche*/
        ORDER BY r.PRTRP_ABREGE, r.PRTRP_ID_PRTRP
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ecran", EcrivainRepertoire.NomEcran));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@ecran", EcrivainRepertoire.NomEcran) };
        var filtres = new List<string>();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(r.PRTRP_ABREGE LIKE @texte OR r.PRTRP_DESIGNATION LIKE @texte)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereAbrege) is { } abrege)
        {
            filtres.Add("r.PRTRP_ABREGE LIKE @abrege");
            parametres.Add(new SqlParameter("@abrege", FormeLigne.Contient(abrege)));
        }
        if (recherche.Critere(CritereDesignation) is { } designation)
        {
            filtres.Add("r.PRTRP_DESIGNATION LIKE @designation");
            parametres.Add(new SqlParameter("@designation", FormeLigne.Contient(designation)));
        }
        if (recherche.Critere(CritereActifs) is { } actifs && actifs.Trim().ToLowerInvariant() is "oui" or "o" or "1")
            filtres.Add("r.PRTRP_ACTIF = 1");
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var actif = r["PRTRP_ACTIF"] is true;
        // Les colonnes de la liste de la V2 (OPE_RECHERCHE_MODIF_REPER, InitColumn) : designation, avocat, adresse mail, actif.
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "PRTRP_DESIGNATION"),
            FormeLigne.Texte(r, "MNDTR_ABREGE") is { Length: > 0 } avocat ? $"avocat {FormeLigne.Joindre(avocat, FormeLigne.Texte(r, "MNDTR_NOM"))}" : null,
            FormeLigne.Texte(r, "PRTRP_ADRESSE_MAIL"), actif ? "actif" : "inactif");
        var enregistre = Convert.ToInt32(r["Enregistre"], CultureInfo.InvariantCulture) == 1;
        return new LigneLue(FormeLigne.Texte(r, "PRTRP_ABREGE"), precisions, actif ? Actifs : Inactifs, enregistre ? "Validee" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "PRTRP_ID_PRTRP"),
        };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>Le repertorie : ses champs au format d'une fiche, et ce que la sauvegarde de la V2 lit en plus.</summary>
    internal sealed record RepertorieLu(int Id, IReadOnlyDictionary<string, string> Valeurs);

    /// <summary>Le repertorie <paramref name="id"/>, lu sur <paramref name="cn"/> (dans la transaction d'un geste si
    /// <paramref name="tx"/>, verrouille en ecriture jusqu'a sa fin) ; null s'il n'existe pas.</summary>
    internal static async Task<RepertorieLu?> LireRepertorieAsync(SqlConnection cn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var sql = $"""
            SELECT r.PRTRP_ABREGE, r.PRTRP_DESIGNATION, r.PRTRP_ADRESSE_MAIL, r.PRTRP_CIVILITE, r.PRTRP_ACTIF, r.PRTRP_MODE_PRESENTATION,
                   r.PRTRP_TEXTE_ASSISTANCE, c.Abrege AS ClientAbrege, c.Nom AS ClientNom, m.MNDTR_ABREGE, m.MNDTR_NOM,
                   ad.NumeroVoie, ad.ComplementNumero, ad.TypeVoie, ad.NomVoie, ad.CodePostal, ad.Localite,
                   r.PRTRP_EDITION_JUGEMENT, r.PRTRP_FORME_JURIDIQUE_UTI, r.PRTRP_ORGANE, r.PRTRP_SIREN, r.PRTRP_DATE_NAISSANCE,
                   r.PRTRP_NATIONALITE, r.PRTRP_PROFESSION, r.PRTRP_TELEPHONE, r.PRTRP_FAX, r.PRTRP_TEXTE_ESQUALITE
            FROM v2.REPERTORIE AS r {(tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")}
            LEFT JOIN rec.Client AS c ON c.Id = r.PRTRP_ID_CLIEN
            LEFT JOIN v2.MANDATAIRE AS m ON m.MNDTR_ID_MNDTR = r.PRTRP_ID_MNDTR_POS
            LEFT JOIN rec.Adresse AS ad ON ad.Id = r.PRTRP_ID_ADRBS
            WHERE r.PRTRP_ID_PRTRP = @id
            """;
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new RepertorieLu(id, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampAbrege] = FormeLigne.Texte(r, "PRTRP_ABREGE"), [ChampDesignation] = FormeLigne.Texte(r, "PRTRP_DESIGNATION"),
            [ChampMail] = FormeLigne.Texte(r, "PRTRP_ADRESSE_MAIL"), [ChampCivilite] = FormeLigne.Texte(r, "PRTRP_CIVILITE"),
            [ChampActif] = FormeLigne.Case(r["PRTRP_ACTIF"] is true),
            [ChampAdresse] = LecteurPartiesInstance.AdresseLue(r), [ChampMode] = FormeLigne.Texte(r, "PRTRP_MODE_PRESENTATION"),
            [ChampClient] = FormeLigne.Texte(r, "ClientAbrege"), [ChampNomClient] = FormeLigne.Texte(r, "ClientNom"),
            [ChampAssistance] = FormeLigne.Texte(r, "PRTRP_TEXTE_ASSISTANCE"),
            // Un mandataire sans abrege se nomme par son nom (la liste de la V2 le montre, OPE_MANDATAIRE.cs:515-519).
            [ChampMandataire] = FormeLigne.Texte(r, "MNDTR_ABREGE") is { Length: > 0 } abrege ? abrege : FormeLigne.Texte(r, "MNDTR_NOM"),
            [ChampEditionJugement] = FormeLigne.Texte(r, "PRTRP_EDITION_JUGEMENT"), [ChampFormeJuridique] = FormeLigne.Texte(r, "PRTRP_FORME_JURIDIQUE_UTI"),
            // Le Siren tel que la base le garde (9 chiffres, Siren.DeFormat) : la valeur que l'on saisit, comme la V2 a l'entree
            // dans la zone (ULT_TEXT_SIREN.OnEnter, :73-76).
            [ChampOrgane] = FormeLigne.Texte(r, "PRTRP_ORGANE"), [ChampSiren] = FormeLigne.Texte(r, "PRTRP_SIREN"),
            [ChampNaissance] = FormeLigne.DateFiche(FormeLigne.DateDe(r, "PRTRP_DATE_NAISSANCE")), [ChampNationalite] = FormeLigne.Texte(r, "PRTRP_NATIONALITE"),
            [ChampProfession] = FormeLigne.Texte(r, "PRTRP_PROFESSION"), [ChampTelephone] = FormeLigne.Texte(r, "PRTRP_TELEPHONE"),
            [ChampFax] = FormeLigne.Texte(r, "PRTRP_FAX"), [ChampQualite] = FormeLigne.Texte(r, "PRTRP_TEXTE_ESQUALITE"),
        });
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        if (await LireRepertorieAsync(baseGreffe, null, n, ct) is not { } rep) return null;
        var parties = new List<string[]>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT i.INSTN_NUM_ROLE, p.PRT_CIVILITE, p.PRT_QUALIF_PARTIE, p.PRT_DESIGNATION
            FROM v2.PARTIE AS p LEFT JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = p.PRT_ID_INSTN
            WHERE p.PRT_ID_PRTRP = @id ORDER BY i.INSTN_NUM_ROLE DESC, p.PRT_ID_PRT
            """, ct, new SqlParameter("@id", n)))
            while (await r.ReadAsync(ct))
                parties.Add([FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "PRT_CIVILITE"),
                             FormeLigne.Texte(r, "PRT_QUALIF_PARTIE"), FormeLigne.Texte(r, "PRT_DESIGNATION")]);
        return new FicheEcran(Procedure, id, rep.Valeurs[ChampAbrege],
        [
            new SectionFiche(SectionRepertorie,
                [.. ChampsFiche.Select(c => new ChampFiche(c, rep.Valeurs[c]) { Modifiable = c is not (ChampAdresse or ChampNomClient) })]),
            new SectionFiche(SectionParties,
            [
                new(ChampParties, "") { Colonnes = ["Numéro affaire", "Civilité", "Qualifiant", "Nom partie"], Lignes = [.. parties] },
                new(ChampCivilite, rep.Valeurs[ChampCivilite]),
            ]),
        ]);
    }
}
