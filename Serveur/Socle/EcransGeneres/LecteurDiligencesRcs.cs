using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Diligences RCS » (PROC_MDIL, dans l'ecran reuni « Corriger un dossier RCS ») : les diligences des dossiers du registre,
/// une recherche par dossier, nature et etat de relance, et la FICHE d'une diligence. Modes d'emploi : LecteurEcran.cs,
/// EcrivainDiligencesRcs.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MDIL (D:\RigApplication\Source\RIG\DLL\Processus\SURVEILLANCES\PROC_MDIL\PROC_MDIL, fiche
/// proc_mdil.md) ouvre UN dossier RCS et charge TOUTES ses diligences, une etape OPE_DILIGENCE_RCS chacune (PROC_MDIL.cs:20-31) ;
/// il n'en cree aucune (MetierCardinaliteMaximum = nombre existant, :27).
///
/// FILTRE DE LA LISTE : les diligences d'un dossier, comme DossierRCS.DiligencesRcs (DiligenceRcs.GetDiligencesRcsByDossier,
/// DiligenceRcs.cs:220-241) : rattachees au dossier par leur action (ACTION_RCS.ACTRC_ID_DSRCS, ici rec.ActionRegistre), ou
/// par un exercice (EXERCICE.EXRCC_ID_DSRCS, la diligence de comptes annuels). Aucun autre filtre : MDIL ne restreint ni la
/// nature ni l'etat (CheckTypeDossier rend toujours vide, PROC_MDIL.cs:33-35). ORDRE : numero de gestion du dossier
/// decroissant, puis diligence (identifiant), comme l'ordre de chargement de la V2.
///
/// GROUPES (descripteur) : « Avec relance » = la diligence a au moins une relance (RELANCE_RCS.RLCRC_ID_DLGRC, la liste
/// opeRelanceRCs, OPE_DILIGENCE_RCS.cs:44) ; « Sans relance » sinon.
///
/// REGLE D'ETAT : « Modifiée » (Validee) quand cet ecran a enregistre la diligence (geste « a modifie » au journal des gestes,
/// soc.Journal) ; « À modifier » (AFaire) sinon. « Sans diligence » (un dossier sans diligence, que la V2 ouvre en lecture
/// seule, PROC_MDIL.cs:21-24) n'a pas de ligne : la liste est faite de diligences. La demande MDIL n'est qu'une trace
/// (MetierFacture = false, PROC_MDIL.Designer.cs:64).
///
/// RECHERCHE (IRechercheEcran) : « Numéro de dossier RCS » (numero de gestion, blancs retires) ; « Nature de la diligence »
/// et « État de la relance » : le code ou le libelle (NATURE_DILIGENCE, ETAT_RELANCE). Le champ de recherche cherche la
/// denomination, le SIREN ou le numero de gestion.
///
/// FICHE (IFicheEcran) : les zones de OPE_DILIGENCE_RCS (OPE_DILIGENCE_RCS.cs:28-34, titres du Designer) en codes, telles
/// que la V2 les enregistre ; Paramètres : DILIGENCE_RCS_PARAMETRE de la diligence (OPE_DILIGENCE_RCS_PARAM.cs:29-33) ;
/// Relances : RELANCE_RCS (OPE_RELANCE_RCS.cs:27-33).
///
/// VERROU : aucun. PROC_MDIL ne verrouille pas le dossier (aucun Lock ni GetLock dans PROC_MDIL).
/// </summary>
internal sealed class LecteurDiligencesRcs : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MDIL";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string AvecRelance = "Avec relance", SansRelance = "Sans relance";

    /// <summary>Les criteres du descripteur cherches dans la base.</summary>
    internal const string CritereDossier = "Numéro de dossier RCS", CritereNature = "Nature de la diligence", CritereRelance = "État de la relance";

    /// <summary>Les sections et champs de la fiche, aux titres et libelles du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionDiligence = "Diligence", SectionEtat = "État", SectionRegularisation = "Régularisation",
                          SectionParametres = "Paramètres", SectionRelances = "Relances";

    internal const string ChampOrigine = "Origine de l'information", ChampNature = "Nature de la diligence", ChampMotif = "Motif de la diligence",
                          ChampEtatDiligence = "État de la diligence", ChampEtatRelance = "État de la relance",
                          ChampDateRegularisation = "Date de régularisation", ChampValeurAttendue = "Valeur attendue",
                          ChampParametres = "Paramètres de la nature", ChampRelances = "Relances de la diligence";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDossier, CritereNature, CritereRelance];

    // Le dossier de la diligence : par son action, sinon par un exercice (DiligenceRcs.cs:224-233).
    internal const string Dossier = """
        LEFT JOIN rec.ActionRegistre AS a ON a.Id = g.DLGRC_ID_ACTRC
        OUTER APPLY (SELECT TOP (1) ex.EXRCC_ID_DSRCS FROM v2.EXERCICE AS ex WHERE ex.EXRCC_ID_DLGRC = g.DLGRC_ID_DLGRC
                     ORDER BY ex.EXRCC_ID_EXRCC) AS x
        JOIN rec.DossierRcs AS s ON s.Id = COALESCE(a.IdDossier, x.EXRCC_ID_DSRCS)
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        """;

    private const string Requete = $"""
        SELECT TOP (@plafond) g.DLGRC_ID_DLGRC, g.DLGRC_NATURE_DILIGENCE, n.R_NATUREDILI_LIBELLE_ECRAN, ed.R_ETATDILIGE_LIBELLE_ECRAN,
               er.R_ETATRELANC_LIBELLE_ECRAN, s.NumeroGestion, s.Siren, e.Denomination, rel.Relances,
               CASE WHEN EXISTS (SELECT 1 FROM soc.Journal AS j
                                 WHERE j.Ecran = @ecran AND j.Verbe = 'a modifie' AND j.IdObjet = CONVERT(varchar(40), g.DLGRC_ID_DLGRC))
                    THEN 1 ELSE 0 END AS Modifiee,
               COUNT(*) OVER () AS Total
        FROM v2.DILIGENCE_RCS AS g
        {Dossier}
        LEFT JOIN v2.NATURE_DILIGENCE AS n ON n.R_NATUREDILI_VALEUR = g.DLGRC_NATURE_DILIGENCE
        LEFT JOIN v2.ETAT_DILIGENCE AS ed ON ed.R_ETATDILIGE_VALEUR = g.DLGRC_ETAT_DILIGENCE
        LEFT JOIN v2.ETAT_RELANCE AS er ON er.R_ETATRELANC_VALEUR = g.DLGRC_ETAT_RELANCE
        OUTER APPLY (SELECT COUNT(*) AS Relances
                     FROM v2.RELANCE_RCS AS r WHERE r.RLCRC_ID_DLGRC = g.DLGRC_ID_DLGRC) AS rel
        WHERE 1 = 1 /*recherche*/
        ORDER BY s.NumeroGestion DESC, g.DLGRC_ID_DLGRC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct,
            new SqlParameter("@plafond", plafond), new SqlParameter("@ecran", EcrivainDiligencesRcs.NomEcran));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond), new("@ecran", EcrivainDiligencesRcs.NomEcran) };
        var filtres = new List<string>();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(e.Denomination LIKE @texte OR s.Siren LIKE @texteNumero OR s.NumeroGestion LIKE @texteNumero)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
            parametres.Add(new SqlParameter("@texteNumero", FormeLigne.Contient(recherche.Texte.Replace(" ", "", StringComparison.Ordinal))));
        }
        if (recherche.Critere(CritereDossier) is { } dossier)
        {
            filtres.Add("s.NumeroGestion LIKE @dossier");
            parametres.Add(new SqlParameter("@dossier", FormeLigne.Contient(dossier.Replace(" ", "", StringComparison.Ordinal))));
        }
        if (recherche.Critere(CritereNature) is { } nature)
        {
            filtres.Add("(g.DLGRC_NATURE_DILIGENCE LIKE @nature OR n.R_NATUREDILI_LIBELLE_ECRAN LIKE @nature)");
            parametres.Add(new SqlParameter("@nature", FormeLigne.Contient(nature)));
        }
        if (recherche.Critere(CritereRelance) is { } relance)
        {
            filtres.Add("(g.DLGRC_ETAT_RELANCE LIKE @relance OR er.R_ETATRELANC_LIBELLE_ECRAN LIKE @relance)");
            parametres.Add(new SqlParameter("@relance", FormeLigne.Contient(relance)));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var relances = r["Relances"] is int k ? k : 0;
        var nature = FormeLigne.Texte(r, "R_NATUREDILI_LIBELLE_ECRAN") is { Length: > 0 } libelle ? libelle : FormeLigne.Texte(r, "DLGRC_NATURE_DILIGENCE");
        var precisions = FormeLigne.Joindre(
            $"Dossier RCS {FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"))}", nature,
            FormeLigne.Texte(r, "R_ETATDILIGE_LIBELLE_ECRAN"),
            relances == 0 ? "non relancée" : $"{relances} relance{(relances > 1 ? "s" : "")}");
        var modifiee = Convert.ToInt32(r["Modifiee"], CultureInfo.InvariantCulture) == 1;
        return new LigneLue(FormeRelance.NomDossier(r), precisions, relances > 0 ? AvecRelance : SansRelance, modifiee ? "Validee" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "DLGRC_ID_DLGRC"),
        };
    }

    // ------------------------------------------------------------------ LA FICHE

    /// <summary>La diligence : ses zones (en codes, comme la V2 les enregistre) et son dossier.</summary>
    internal sealed record DiligenceLue(int Id, string Dossier, string Origine, string Nature, string Motif, string EtatDiligence,
                                        string EtatRelance, DateTime? Regularisation, string ValeurAttendue, int? IdParametre)
    {
        /// <summary>Les valeurs de la fiche, par champ, au format d'une fiche.</summary>
        public IReadOnlyDictionary<string, string> Valeurs => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChampOrigine] = Origine, [ChampNature] = Nature, [ChampMotif] = Motif, [ChampEtatDiligence] = EtatDiligence,
            [ChampEtatRelance] = EtatRelance, [ChampDateRegularisation] = FormeLigne.DateFiche(Regularisation),
            [ChampValeurAttendue] = ValeurAttendue,
        };
    }

    /// <summary>La diligence <paramref name="id"/>, lue sur <paramref name="cn"/> (dans la transaction d'un geste si
    /// <paramref name="tx"/>, verrouillee en ecriture jusqu'a sa fin) ; null si elle n'existe pas ou n'a pas de dossier.</summary>
    internal static async Task<DiligenceLue?> LireDiligenceAsync(SqlConnection cn, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var sql = $"""
            SELECT g.DLGRC_ORIGINE_INFO, g.DLGRC_NATURE_DILIGENCE, g.DLGRC_MOTIF_DILIGENCE, g.DLGRC_ETAT_DILIGENCE, g.DLGRC_ETAT_RELANCE,
                   g.DLGRC_DATE_REGULARISATION, g.DLGRC_VALEUR_ATTENDUE, g.DLGRC_ID_DLGPA, s.NumeroGestion
            FROM v2.DILIGENCE_RCS AS g {(tx is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")}
            {Dossier}
            WHERE g.DLGRC_ID_DLGRC = @id
            """;
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new DiligenceLue(id, FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")), FormeLigne.Texte(r, "DLGRC_ORIGINE_INFO"),
            FormeLigne.Texte(r, "DLGRC_NATURE_DILIGENCE"), FormeLigne.Texte(r, "DLGRC_MOTIF_DILIGENCE"), FormeLigne.Texte(r, "DLGRC_ETAT_DILIGENCE"),
            FormeLigne.Texte(r, "DLGRC_ETAT_RELANCE"), FormeLigne.DateDe(r, "DLGRC_DATE_REGULARISATION"),
            r["DLGRC_VALEUR_ATTENDUE"] is string v ? v : "", r["DLGRC_ID_DLGPA"] is int p ? p : null);
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        if (await LireDiligenceAsync(baseGreffe, null, n, ct) is not { } d) return null;
        ChampFiche Champ(string libelle) => new(libelle, d.Valeurs[libelle]) { Modifiable = true };
        return new FicheEcran(Procedure, id, $"Diligence {d.Nature} du dossier {d.Dossier}",
        [
            new SectionFiche(SectionDiligence, [Champ(ChampOrigine), Champ(ChampNature), Champ(ChampMotif)]),
            new SectionFiche(SectionEtat, [Champ(ChampEtatDiligence), Champ(ChampEtatRelance)]),
            new SectionFiche(SectionRegularisation, [Champ(ChampDateRegularisation), Champ(ChampValeurAttendue)]),
            new SectionFiche(SectionParametres, [await ParametresAsync(baseGreffe, d.IdParametre, ct)]),
            new SectionFiche(SectionRelances, [await RelancesAsync(baseGreffe, n, ct)]),
        ]);
    }

    // PARAMETRES : les cases et le taux de OPE_DILIGENCE_RCS_PARAM (OPE_DILIGENCE_RCS_PARAM.cs:29-33), titres du Designer
    // (OPE_DILIGENCE_RCS_PARAM.Designer.cs:83-120) ; aucune ligne sans parametre (opeDiligenceRcsParam masquee, OPE_DILIGENCE_RCS.cs:39-41).
    private static async Task<ChampFiche> ParametresAsync(SqlConnection cn, int? idParametre, CancellationToken ct)
    {
        var lignes = new List<string[]>();
        if (idParametre is { } idp)
        {
            await using var r = await LectureV2.LireAsync(cn, """
                SELECT DLGPA_NON_DISTRIBUE, DLGPA_ASTREINTE, DLGPA_TAUX_ASTREINTE, DLGPA_NPAI, DLGPA_EDITIONS_DIFFEREES
                FROM v2.DILIGENCE_RCS_PARAMETRE WHERE DLGPA_ID_DLGPA = @id
                """, ct, new SqlParameter("@id", idp));
            if (await r.ReadAsync(ct))
            {
                string Ouinon(string c) => r[c] is true ? "oui" : r[c] is false ? "non" : FormeLigne.Texte(r, c);
                lignes.Add(["Si non réclamé, refusé ou incomplet", Ouinon("DLGPA_NON_DISTRIBUE")]);
                lignes.Add(["Avec astreinte", Ouinon("DLGPA_ASTREINTE")]);
                lignes.Add(["Taux d'astreinte", FormeLigne.Texte(r, "DLGPA_TAUX_ASTREINTE")]);
                lignes.Add(["Traitement NPAI", Ouinon("DLGPA_NPAI")]);
                lignes.Add(["Editions en différé", Ouinon("DLGPA_EDITIONS_DIFFEREES")]);
            }
        }
        return new ChampFiche(ChampParametres, "") { Colonnes = ["Paramètre", "Valeur"], Lignes = [.. lignes] };
    }

    // RELANCES : par numero d'ordre (OPE_RELANCE_RCS.cs:27-33, titres du Designer :82-131).
    private static async Task<ChampFiche> RelancesAsync(SqlConnection cn, int id, CancellationToken ct)
    {
        var lignes = new List<string[]>();
        await using var r = await LectureV2.LireAsync(cn, """
            SELECT RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_TYPE_RELANCE, RLCRC_DATE_LIMITE
            FROM v2.RELANCE_RCS WHERE RLCRC_ID_DLGRC = @id ORDER BY RLCRC_NUM_ORDRE, RLCRC_ID_RLCRC
            """, ct, new SqlParameter("@id", id));
        while (await r.ReadAsync(ct))
            lignes.Add([FormeLigne.Texte(r, "RLCRC_NUM_ORDRE"), FormeLigne.Date(FormeLigne.DateDe(r, "RLCRC_DATE_RELANCE")),
                        FormeLigne.Texte(r, "RLCRC_TYPE_RELANCE"), FormeLigne.Date(FormeLigne.DateDe(r, "RLCRC_DATE_LIMITE"))]);
        return new ChampFiche(ChampRelances, "") { Colonnes = ["N° ordre", "Date relance", "Type relance", "Date limite"], Lignes = [.. lignes] };
    }
}
