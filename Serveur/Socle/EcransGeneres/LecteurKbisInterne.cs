using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Édition interne d'un Kbis » (PROC_XEX) : le dossier RCS choisi par son numero de gestion, et les Kbis internes deja edites. Mode
/// d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026. Le geste : EcrivainKbisInterne.cs.
///
/// SOURCE (ancien RIG) : PROC_XEX (D:\RigApplication\Source\RIG\DLL\Processus\PROC_XEX ; le Detail OPE de proc_xex.md fait foi) : un
/// processus mince sur l'etape ETP_RCS, recherche du dossier PAR SON NUMERO DE GESTION, sans numero d'identification (MetierAllowNI
/// = false, ETP_RCS.cs:21, proc_xex.md:134), radie admis (MetierTestEntiteRadiee = false). La demande XEX que la V2 valide porte le
/// dossier (DMND_ID_DSRCS), son numero de gestion (DMND_NUM_INDICATIF) et sa designation (DMND_NOM) (PROC_XEX.cs:21-30) : c'est la
/// trace de chaque Kbis interne edite.
/// Dans la base V3 : DOSSIER_RCS = rec.DossierRcs, ENTREPRISE = rec.Entreprise (Serveur/Recopie/FichesV3.cs).
///
/// LA LISTE A L'OUVERTURE : RIG v2 n'a pas de liste (son numero se saisit). La V3 rend les dossiers qui ont deja un Kbis interne
/// (une demande XEX), le plus recent d'abord : c'est l'historique de l'ecran ; le numero saisi cherche, lui, tout dossier.
/// RECHERCHE (IRechercheEcran) : « Numéro de gestion du dossier RCS » (DSSRC_NUM_GESTION, blancs retires), son seul critere ; le
/// champ de recherche cherche aussi le numero.
///
/// UNE LIGNE = un dossier : son nom est son numero de gestion (l'ecran d'une operation sur UN objet le retrouve par ce numero).
/// GROUPE : « Dossiers RCS ». REGLE D'ETAT : aucune demande XEX -> AFaire « À rechercher » ; au moins une -> EnCours « Dossier
/// choisi », pour qu'un dossier deja edite puisse l'etre encore (RIG v2 valide autant de demandes XEX qu'on en edite).
///
/// FICHE (IFicheEcran), Id = DSSRC_ID_DSRCS : « Dossier RCS » (numero, designation) et « Demande » (ce que la demande XEX recevra :
/// designation, numero de gestion, identifiant interne, PROC_XEX.cs:24-27), et « Extrait Kbis » : ce que l'apercu Kbis de la V3
/// imprime (Pont/ApercuKbis.cs, web/src/composants/ApercuKbis.tsx ; l'ecran la dessine en extrait, Pont/ApercuKbisDossier.cs), lu
/// dans les tables de RIG v2 dont l'apercu nomme les colonnes : DOSSIER_RCS (SIREN, immatriculation), INSCRIT_PM (denomination
/// statutaire, forme juridique UTI, la plus recente ; la designation et la forme de la fiche rec.Entreprise a defaut), CAPITAL de cette inscription (le
/// plus recent par date d'effet), l'ETABLISSEMENT siege (SPM ou HRM, non supprime, le premier : DossierRCS.cs:874-892, comme
/// InjonctionsIsurv.cs:253) et son adresse (rec.Adresse), le premier DIRIGEANT non supprime (DRGNT_NUM_ORDRE), la premiere ACTIVITE
/// non supprimee (ACTVT_SUPPRIM_DATE_FIN vide) ; codes par le libelle de leur table (FORME_JURIDIQUE_UTI, FONCTION_DIRIGEANT,
/// ORIGINE_FONDS, MODE_EXPLOITATION), le code s'il n'en a pas. L'apercu V3 n'a qu'UNE ligne de dirigeant et d'activite : le premier,
/// a defaut des autres (l'extrait de RIG v2 les liste tous, XSL du Kbis).
/// fix-ok: mesure par le controle final du lot 13 (27/11/2026) -- grep « ApercuKbis » : seul Pont/PlanTravailVue.cs:692 l'appelait ;
/// l'edition interne n'imprimait aucun extrait du dossier choisi, faute de lire ce que l'extrait montre.
/// </summary>
internal sealed class LecteurKbisInterne : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_XEX";

    internal const string Groupe = "Dossiers RCS";
    internal const string CritereNumero = "Numéro de gestion du dossier RCS";
    internal const string SectionDossier = "Dossier RCS", SectionDemande = "Demande";
    internal const string ChampNumero = "Numéro de gestion", ChampDesignation = "Désignation", ChampIdentifiant = "Identifiant interne";

    /// <summary>La section que l'ecran dessine en extrait Kbis (DescripteurEcran.ApercuKbis) et ses champs, aux libelles du descripteur.</summary>
    internal const string SectionKbis = "Extrait Kbis";
    internal const string KbisSiren = "SIREN", KbisCodeGreffe = "Code greffe", KbisImmatriculation = "Date d'immatriculation",
                          KbisDenomination = "Dénomination", KbisForme = "Forme juridique", KbisCapital = "Capital",
                          KbisCapitalVariable = "Capital variable", KbisMonnaie = "Monnaie", KbisAdresse = "Adresse du siège",
                          KbisDebutSiege = "Date de début", KbisOrigine = "Origine du fonds", KbisExploitation = "Mode d'exploitation",
                          KbisQualite = "Qualité du dirigeant", KbisEntree = "Entrée en fonction", KbisSirenDirigeant = "SIREN du dirigeant",
                          KbisActivite = "Activité", KbisDebutActivite = "Début d'activité", KbisNaf = "Code NAF";

    // L'extrait d'UN dossier (@id) : une ligne, chaque table de RIG v2 par OUTER APPLY (absente : ses colonnes vides).
    private const string RequeteKbis = $"""
        SELECT s.Siren, s.DateImmatriculation, COALESCE(NULLIF(LTRIM(RTRIM(pm.INSPM_DENOMINATION_STATUT)), ''), {Designation}) AS Denomination,
               COALESCE(fj.R_FORMEJURID_LIBELLE_ECRAN, NULLIF(pm.INSPM_FORME_JURIDIQUE_UTI, ''), e.FormeJuridique) AS Forme,
               cp.CPTL_MONTANT, cp.CPTL_VARIABLE, cp.CPTL_MONNAIE,
               et.ETBLS_CODE_GREFFE, et.ETBLS_DATE_DEBUT, {LecteurDossierRcs.Adresse} AS Adresse,
               COALESCE(ofd.R_ORIGINEFON_LIBELLE_ECRAN, et.ETBLS_ORIGINE_FONDS) AS Origine,
               COALESCE(me.R_MODEEXPLOI_LIBELLE_ECRAN, et.ETBLS_MODE_EXPLOITATION) AS Exploitation,
               COALESCE(fd.R_FONCTIONDI_LIBELLE_ECRAN, dg.DRGNT_FONCTION_DIRIGEANT) AS Qualite, dg.DRGNT_DATE_ENTREE, dg.DRGNT_NUM_SIREN,
               ac.ACTVT_ACTIVITE_DECLAREE, ac.ACTVT_DATE_DEBUT, ac.ACTVT_CODE_NAF
        FROM rec.DossierRcs AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        OUTER APPLY (SELECT TOP (1) p.INSPM_ID_INSPM, p.INSPM_DENOMINATION_STATUT, p.INSPM_FORME_JURIDIQUE_UTI FROM v2.INSCRIT_PM AS p
                     WHERE p.INSPM_ID_DSRCS = s.Id ORDER BY p.INSPM_ID_INSPM DESC) AS pm
        LEFT JOIN v2.FORME_JURIDIQUE_UTI AS fj ON fj.R_FORMEJURID_VALEUR = COALESCE(NULLIF(pm.INSPM_FORME_JURIDIQUE_UTI, ''), e.FormeJuridique)
        OUTER APPLY (SELECT TOP (1) c.CPTL_MONTANT, c.CPTL_VARIABLE, c.CPTL_MONNAIE FROM v2.CAPITAL AS c
                     WHERE c.CPTL_ID_INSPM = pm.INSPM_ID_INSPM ORDER BY c.CPTL_DATE_EFFET DESC, c.CPTL_ID_CPTL DESC) AS cp
        OUTER APPLY (SELECT TOP (1) x.ETBLS_CODE_GREFFE, x.ETBLS_DATE_DEBUT, x.ETBLS_ID_ADRBS, x.ETBLS_ORIGINE_FONDS, x.ETBLS_MODE_EXPLOITATION
                     FROM v2.ETABLISSEMENT AS x WHERE x.ETBLS_ID_DSRCS = s.Id AND x.ETBLS_TYPE_ADRESSE IN ('SPM', 'HRM') AND x.ETBLS_SUPPRIME = 0
                     ORDER BY x.ETBLS_ID_ETBLS) AS et
        LEFT JOIN rec.Adresse AS a ON a.Id = et.ETBLS_ID_ADRBS
        LEFT JOIN v2.ORIGINE_FONDS AS ofd ON ofd.R_ORIGINEFON_VALEUR = et.ETBLS_ORIGINE_FONDS
        LEFT JOIN v2.MODE_EXPLOITATION AS me ON me.R_MODEEXPLOI_VALEUR = et.ETBLS_MODE_EXPLOITATION
        OUTER APPLY (SELECT TOP (1) d.DRGNT_FONCTION_DIRIGEANT, d.DRGNT_DATE_ENTREE, d.DRGNT_NUM_SIREN FROM v2.DIRIGEANT AS d
                     WHERE d.DRGNT_ID_DSRCS = s.Id AND d.DRGNT_SUPPRIME = 0 ORDER BY d.DRGNT_NUM_ORDRE, d.DRGNT_ID_DRGNT) AS dg
        LEFT JOIN v2.FONCTION_DIRIGEANT AS fd ON fd.R_FONCTIONDI_VALEUR = dg.DRGNT_FONCTION_DIRIGEANT
        OUTER APPLY (SELECT TOP (1) t.ACTVT_ACTIVITE_DECLAREE, t.ACTVT_DATE_DEBUT, t.ACTVT_CODE_NAF FROM v2.ACTIVITE AS t
                     WHERE t.ACTVT_ID_DSRCS = s.Id AND t.ACTVT_SUPPRIM_DATE_FIN IS NULL ORDER BY t.ACTVT_ID_ACTVT) AS ac
        WHERE s.Id = @id
        """;

    /// <summary>La designation d'un dossier : la denomination de l'entreprise, sinon le nom et le prenom de la personne.</summary>
    internal const string Designation = "COALESCE(NULLIF(LTRIM(RTRIM(e.Denomination)), ''), LTRIM(RTRIM(CONCAT(e.Patronyme, ' ', e.Prenom))))";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero];

    private const string Requete = $"""
        SELECT TOP (@plafond) s.Id, s.NumeroGestion, s.Siren, s.DateRadiation, {Designation} AS Designation,
               x.NbKbis, x.Dernier, COUNT(*) OVER () AS Total
        FROM rec.DossierRcs AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        OUTER APPLY (SELECT COUNT(*) AS NbKbis, MAX(COALESCE(d.DMND_DATE_VALIDATION, d.DMND_DATE_CREATION)) AS Dernier
                     FROM v2.DEMANDE AS d WHERE d.DMND_ID_DSRCS = s.Id AND d.DMND_CODE_PROSS = 'XEX') AS x
        WHERE /*filtres*/
        ORDER BY x.Dernier DESC, s.NumeroGestion, s.Id
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, "x.NbKbis > 0", plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var numero = recherche.Critere(CritereNumero) ?? recherche.Texte;
        if (string.IsNullOrWhiteSpace(numero)) return LireAsync(baseGreffe, aujourdhui, plafond, ct);
        return Lire(baseGreffe, "s.NumeroGestion = @numero", plafond, ct, new SqlParameter("@numero", CriteresConsultation.Numero(numero)));
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, string filtre, int plafond, CancellationToken ct, params SqlParameter[] p)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", filtre, StringComparison.Ordinal), Ligne, ct,
            [new SqlParameter("@plafond", plafond), .. p]);

    private static LigneLue Ligne(SqlDataReader r)
    {
        var n = CriteresConsultation.EntierDe(r, "NbKbis") ?? 0;
        var radie = FormeLigne.DateDe(r, "DateRadiation") is { } d ? $"radié le {FormeLigne.Date(d)}" : null;
        var historique = n == 0 ? "aucun Kbis interne édité"
            : $"{n} Kbis interne{(n > 1 ? "s" : "")}, le dernier le {FormeLigne.Date(FormeLigne.DateDe(r, "Dernier"))}";
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "Designation"), FormeLigne.Siren(FormeLigne.Texte(r, "Siren")), radie, historique);
        return new LigneLue(FormeLigne.Texte(r, "NumeroGestion"), precisions, Groupe, n == 0 ? "AFaire" : "EnCours", []) { Id = FormeLigne.Texte(r, "Id") };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, $"""
            SELECT s.Id, s.NumeroGestion, {Designation} AS Designation FROM rec.DossierRcs AS s
            LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise WHERE s.Id = @id
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var numero = FormeLigne.Texte(r, "NumeroGestion");
        var designation = FormeLigne.Texte(r, "Designation");
        await r.CloseAsync();
        return new FicheEcran("PROC_XEX", id, numero,
        [
            new SectionFiche(SectionDossier, [new(ChampNumero, numero), new(ChampDesignation, designation)]),
            new SectionFiche(SectionDemande, [new(ChampDesignation, designation), new(ChampNumero, numero), new(ChampIdentifiant, id)]),
            await ExtraitKbisAsync(baseGreffe, n, ct),
        ]);
    }

    /// <summary>La section « Extrait Kbis » du dossier <paramref name="id"/> (format des valeurs : Contrats/EcransGeneres/FicheEcran.cs).</summary>
    private static async Task<SectionFiche> ExtraitKbisAsync(SqlConnection baseGreffe, int id, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(baseGreffe, RequeteKbis, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return new SectionFiche(SectionKbis, []);
        string T(string c) => FormeLigne.Texte(r, c).Trim();
        string D(string c) => FormeLigne.DateFiche(FormeLigne.DateDe(r, c));
        var capital = r["CPTL_MONTANT"] is DBNull ? "" : Convert.ToDecimal(r["CPTL_MONTANT"], CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture);
        return new SectionFiche(SectionKbis,
        [
            new(KbisSiren, FormeLigne.Siren(T("Siren"))), new(KbisCodeGreffe, T("ETBLS_CODE_GREFFE")), new(KbisImmatriculation, D("DateImmatriculation")),
            new(KbisDenomination, T("Denomination")), new(KbisForme, T("Forme")), new(KbisCapital, capital),
            new(KbisCapitalVariable, FormeLigne.Case(r["CPTL_VARIABLE"] is bool v && v)), new(KbisMonnaie, T("CPTL_MONNAIE")),
            new(KbisAdresse, T("Adresse")), new(KbisDebutSiege, D("ETBLS_DATE_DEBUT")), new(KbisOrigine, T("Origine")),
            new(KbisExploitation, T("Exploitation")), new(KbisQualite, T("Qualite")), new(KbisEntree, D("DRGNT_DATE_ENTREE")),
            new(KbisSirenDirigeant, T("DRGNT_NUM_SIREN")), new(KbisActivite, T("ACTVT_ACTIVITE_DECLAREE")),
            new(KbisDebutActivite, D("ACTVT_DATE_DEBUT")), new(KbisNaf, T("ACTVT_CODE_NAF")),
        ]);
    }
}
