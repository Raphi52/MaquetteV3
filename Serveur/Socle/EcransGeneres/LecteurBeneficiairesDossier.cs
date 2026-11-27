using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Bénéficiaires d'un dossier » (PROC_BE, dans l'ecran reuni « Corriger un dossier RCS ») : les beneficiaires effectifs des
/// dossiers du registre, une recherche par dossier, nom et type de declaration, et la FICHE d'un beneficiaire. Modes
/// d'emploi : LecteurEcran.cs, EcrivainBeneficiairesDossier.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_BE (D:\RigApplication\Source\RIG\DLL\Processus\PROC_BE, fiche proc_be.md, « Detail OPE »), source
/// Rig (la seule ou l'on modifie un beneficiaire, BeHelper.cs:13-15) : la liste des beneficiaires du dossier, l'identite
/// (OPE_IDENTITE) et les modalites de controle (OPE_MODALITES_CONTROLE) de chacun.
///
/// FILTRE DE LA LISTE : les beneficiaires NON PARTANTS, ceux que la liste de la V2 montre en source Rig
/// (DossierRCS.BeneficiairesNonPartant = BENEF_SUPPRIME faux, DossierRCS.cs:1832-1836 ; OPEGS.cs:65-68), rattaches a leur
/// dossier (BENEF_ID_DSRCS). ORDRE : numero de gestion du dossier decroissant, puis patronyme (« tries par patronyme »,
/// OPEGS.cs:66).
///
/// GROUPES (descripteur) : le type de declaration du dossier (DossierRCS.TypeDeclarationBe, DossierRCS.cs:1866-1872 ;
/// InscritPM.TypeDeclarationBe) : forme juridique de la personne morale (INSCRIT_PM.INSPM_FORME_JURIDIQUE_UTI) dont
/// FORME_JURIDIQUE_UTI.R_FORMEJURID_TYPE_BEN vaut 3 -> « Placement collectif » (FORME_JURIDIQUE_UTI.GetTypeBe : 1 societe,
/// 2 GIE, 3 placement collectif) ; tout autre (societe, GIE ou association, aucune) -> « Déclaration standard ».
///
/// REGLE D'ETAT : « Déclaré » (Validee) quand cet ecran a enregistre le beneficiaire (geste « a modifie » au journal des
/// gestes) ; « À déclarer » (AFaire) sinon. « Consultation seule » (le mode ViewBe, ouvert par un appelant de la V2) n'a pas
/// de ligne : rien dans la base ne le porte. La demande BE n'est qu'une trace (MetierFacture = false, PROC_BE.Designer.cs:89).
///
/// RECHERCHE (IRechercheEcran) : « Dossier RCS » (numero de gestion ou SIREN, blancs retires), « Nom du bénéficiaire »
/// (patronyme, prenoms ou nom d'usage), « Type de déclaration » (dans le nom du groupe). Le champ de recherche cherche le
/// beneficiaire, la denomination ou le numero du dossier.
///
/// FICHE (IFicheEcran) : Identité, les zones d'OPE_IDENTITE (OPE_IDENTITE.cs:52-60, titres du Designer :89-176) que le
/// descripteur porte, le pays de naissance (obligatoire, Designer :144, lu par les controles du departement) et le lieu de
/// naissance (BENEF_LIEU_NAISSANCE : la commune si le pays est la France, le lieu etranger sinon, deux zones titrees « Lieu de
/// naissance » qui ecrivent la meme colonne, Designer :135, :185 ; OPE_IDENTITE.cs:65-66, :118-128), la nationalite
/// (BENEF_NATIONALITE, :59, Designer :155) et l'adresse (BENEF_ID_ADRBS, :60, Designer :176 : la fiche reprise rec.Adresse,
/// l'ADRESSE_BASE de la V2, comme LecteurPartiesInstance et LecteurRepertoire). fix-ok: mesure le 26/11/2026 -- au commit
/// b604f96 l'Identite n'avait ni la nationalite ni l'adresse que _DoChargerDonnees lie (:59-60). Non lus :
/// les modalites de controle (masque genere par type de declaration, OPE_MC.cs:131-193), l'acte depose (GED VidiGED) et les
/// donnees DIBE (service externe) -- ces sections restent absentes de la fiche.
///
/// VERROU : aucun. PROC_BE ne verrouille ni le dossier ni le beneficiaire (aucun Lock dans PROC_BE).
/// </summary>
internal sealed class LecteurBeneficiairesDossier : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_BE";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Standard = "Déclaration standard", PlacementCollectif = "Placement collectif";

    /// <summary>Les criteres du descripteur cherches dans la base.</summary>
    internal const string CritereDossier = "Dossier RCS", CritereNom = "Nom du bénéficiaire", CritereType = "Type de déclaration";

    /// <summary>La section et les champs de la fiche, aux titres et libelles du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionIdentite = "Identité";

    internal const string ChampCivilite = "Civilité", ChampPatronyme = "Patronyme", ChampPrenoms = "Prénoms (état civil)",
                          ChampNomUsage = "Nom d'usage", ChampNaissance = "Date de naissance", ChampDepartement = "Département de naissance",
                          ChampPays = "Pays de naissance", ChampLieu = "Lieu de naissance", ChampNationalite = "Nationalité",
                          ChampAdresse = "Adresse";

    /// <summary>Les champs de l'identite, dans l'ordre du descripteur ; l'adresse se lit seulement.</summary>
    internal static readonly string[] ChampsIdentite = [ChampCivilite, ChampPatronyme, ChampPrenoms, ChampNomUsage, ChampNaissance, ChampDepartement,
                                                        ChampPays, ChampLieu, ChampNationalite, ChampAdresse];

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDossier, CritereNom, CritereType];

    // Le dossier du beneficiaire et son type de declaration (R_FORMEJURID_TYPE_BEN = 3 : placement collectif).
    internal const string Dossier = """
        JOIN rec.DossierRcs AS s ON s.Id = b.BENEF_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        OUTER APPLY (SELECT TOP (1) fj.R_FORMEJURID_TYPE_BEN FROM v2.INSCRIT_PM AS pm
                     JOIN v2.FORME_JURIDIQUE_UTI AS fj ON fj.R_FORMEJURID_VALEUR = pm.INSPM_FORME_JURIDIQUE_UTI
                     WHERE pm.INSPM_ID_DSRCS = s.Id ORDER BY pm.INSPM_ID_INSPM) AS t
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) b.BENEF_ID_BENEF, b.BENEF_PATRONYME, b.BENEF_PRENOM_ETAT_CIVIL, b.BENEF_DATE_NAISSANCE,
               s.NumeroGestion, s.Siren, e.Denomination, t.R_FORMEJURID_TYPE_BEN,
               CASE WHEN EXISTS (SELECT 1 FROM soc.Journal AS j
                                 WHERE j.Ecran = @ecran AND j.Verbe = 'a modifie' AND j.IdObjet = CONVERT(varchar(40), b.BENEF_ID_BENEF))
                    THEN 1 ELSE 0 END AS Declaree,
               COUNT(*) OVER () AS Total
        FROM v2.BENEFICIAIRE_EFFECTIF AS b
        {Dossier}
        WHERE b.BENEF_SUPPRIME = 0 /*recherche*/
        ORDER BY s.NumeroGestion DESC, b.BENEF_PATRONYME, b.BENEF_ID_BENEF
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ecran", EcrivainBeneficiairesDossier.NomEcran));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@ecran", EcrivainBeneficiairesDossier.NomEcran) };
        var filtres = new List<string>();
        const string Nom = "(b.BENEF_PATRONYME LIKE {0} OR b.BENEF_PRENOM_ETAT_CIVIL LIKE {0} OR b.BENEF_NOM_USAGE LIKE {0})";
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add($"({string.Format(CultureInfo.InvariantCulture, Nom, "@texte")} OR e.Denomination LIKE @texte OR s.NumeroGestion LIKE @texteNumero OR s.Siren LIKE @texteNumero)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteNumero", FormeLigne.Contient(recherche.Texte.Replace(" ", "", StringComparison.Ordinal))));
        }
        if (recherche.Critere(CritereDossier) is { } dossier)
        {
            filtres.Add("(s.NumeroGestion LIKE @dossier OR s.Siren LIKE @dossier)");
            parametres.Add(new SqlParameter("@dossier", FormeLigne.Contient(dossier.Replace(" ", "", StringComparison.Ordinal))));
        }
        if (recherche.Critere(CritereNom) is { } nom)
        {
            filtres.Add(string.Format(CultureInfo.InvariantCulture, Nom, "@nom"));
            parametres.Add(new SqlParameter("@nom", FormeLigne.Contient(nom)));
        }
        if (recherche.Critere(CritereType) is { } type)
        {
            bool Trouve(string groupe) => CultureInfo.InvariantCulture.CompareInfo.IndexOf(groupe, type.Trim(),
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
            var placement = "ISNULL(t.R_FORMEJURID_TYPE_BEN, 0) = 3";
            filtres.Add((Trouve(Standard), Trouve(PlacementCollectif)) switch
            {
                (true, true) => "1 = 1",
                (true, false) => $"NOT {placement}",
                (false, true) => placement,
                _ => "1 = 0",
            });
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    /// <summary>Le titre d'un beneficiaire dans la liste de la V2 (BeHelper.GetTitreBeneficiaireEffectif, BeHelper.cs:126-138) :
    /// « PATRONYME Prénoms (jj/mm/aaaa) » -- patronyme en majuscules, chaque prenom avec sa premiere lettre en majuscule, la
    /// date de naissance si elle est connue --, ou « Bénéficiaire effectif » sans nom ni prenom. fix-ok: mesure a la relecture de
    /// BeHelper.cs:126-138 (26/11/2026) -- le premier jet ecrivait « PATRONYME prénoms » sans la date ni les initiales.</summary>
    internal static string Titre(string patronyme, string prenoms, DateTime? naissance)
    {
        if (patronyme.Length + prenoms.Length == 0) return "Bénéficiaire effectif";
        var initiales = string.Join(" ", prenoms.Split(' ').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
        var titre = $"{patronyme.ToUpperInvariant()} {initiales}".Trim();
        return naissance is null ? titre : $"{titre} ({FormeLigne.Date(naissance)})";
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var placement = r["R_FORMEJURID_TYPE_BEN"] is not DBNull && Convert.ToInt32(r["R_FORMEJURID_TYPE_BEN"], CultureInfo.InvariantCulture) == 3;
        var naissance = FormeLigne.DateDe(r, "BENEF_DATE_NAISSANCE");
        var precisions = FormeLigne.Joindre(
            $"Dossier RCS {FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"))}", FormeLigne.Texte(r, "Denomination"));
        var declare = Convert.ToInt32(r["Declaree"], CultureInfo.InvariantCulture) == 1;
        return new LigneLue(Titre(FormeLigne.Texte(r, "BENEF_PATRONYME"), FormeLigne.Texte(r, "BENEF_PRENOM_ETAT_CIVIL"), naissance), precisions,
                            placement ? PlacementCollectif : Standard, declare ? "Validee" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "BENEF_ID_BENEF"),
        };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>Le beneficiaire : son identite au format d'une fiche, par champ, et son dossier.</summary>
    internal sealed record BeneficiaireLu(int Id, string Dossier, IReadOnlyDictionary<string, string> Valeurs)
    {
        public string Nom => Titre(Valeurs[ChampPatronyme], Valeurs[ChampPrenoms], null);
    }

    /// <summary>Le beneficiaire non partant <paramref name="id"/>, lu sur <paramref name="cn"/> (dans la transaction d'un geste si
    /// <paramref name="tx"/>, verrouille en ecriture jusqu'a sa fin) ; null s'il n'existe pas ou n'est pas dans la liste.</summary>
    internal static async Task<BeneficiaireLu?> LireBeneficiaireAsync(SqlConnection cn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var sql = $"""
            SELECT b.BENEF_CIVILITE, b.BENEF_PATRONYME, b.BENEF_PRENOM_ETAT_CIVIL, b.BENEF_NOM_USAGE, b.BENEF_DATE_NAISSANCE,
                   b.BENEF_DEPARTEMENT_NAISS, b.BENEF_PAYS, b.BENEF_LIEU_NAISSANCE, b.BENEF_NATIONALITE, s.NumeroGestion,
                   ad.NumeroVoie, ad.ComplementNumero, ad.TypeVoie, ad.NomVoie, ad.CodePostal, ad.Localite
            FROM v2.BENEFICIAIRE_EFFECTIF AS b {(tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")}
            {Dossier}
            LEFT JOIN rec.Adresse AS ad ON ad.Id = b.BENEF_ID_ADRBS
            WHERE b.BENEF_ID_BENEF = @id AND b.BENEF_SUPPRIME = 0
            """;
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        // Un pays vide s'ouvre sur la France (OPE_IDENTITE.cs:62-64).
        var pays = FormeLigne.Texte(r, "BENEF_PAYS");
        return new BeneficiaireLu(id, FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampCivilite] = FormeLigne.Texte(r, "BENEF_CIVILITE"), [ChampPatronyme] = FormeLigne.Texte(r, "BENEF_PATRONYME"),
            [ChampPrenoms] = FormeLigne.Texte(r, "BENEF_PRENOM_ETAT_CIVIL"), [ChampNomUsage] = FormeLigne.Texte(r, "BENEF_NOM_USAGE"),
            [ChampNaissance] = FormeLigne.DateFiche(FormeLigne.DateDe(r, "BENEF_DATE_NAISSANCE")),
            [ChampDepartement] = FormeLigne.Texte(r, "BENEF_DEPARTEMENT_NAISS"), [ChampPays] = pays.Length == 0 ? "FRA" : pays,
            [ChampLieu] = FormeLigne.Texte(r, "BENEF_LIEU_NAISSANCE"), [ChampNationalite] = FormeLigne.Texte(r, "BENEF_NATIONALITE"),
            [ChampAdresse] = LecteurPartiesInstance.AdresseLue(r),
        });
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        if (await LireBeneficiaireAsync(baseGreffe, null, n, ct) is not { } b) return null;
        return new FicheEcran(Procedure, id, b.Nom,
        [
            new SectionFiche(SectionIdentite, [.. ChampsIdentite.Select(c => new ChampFiche(c, b.Valeurs[c]) { Modifiable = c != ChampAdresse })]),
        ]);
    }
}
