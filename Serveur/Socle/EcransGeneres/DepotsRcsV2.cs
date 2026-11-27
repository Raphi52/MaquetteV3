using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// DEPOSER AU REGISTRE COMME RIG v2 : ce que « Dépôts d'actes RCS » (PROC_DAS) et « Dépôts bénéficiaires » (PROC_DASBEN)
/// partagent (plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026) -- trouver le dossier RCS ou le non-inscrit, lire les
/// saisies de depot, et ecrire un depot VALIDE : l'action du registre, le depot numerote, son evenement RCS, son acte depose
/// et la demande. Lecteurs : LecteurDepotsActes.cs, LecteurDepotsBeneficiaires.cs ; ecrivains : EcrivainDepotsActes.cs,
/// EcrivainDepotsBeneficiaires.cs.
///
/// DANS LA BASE V3 : DOSSIER_RCS est rec.DossierRcs, ENTREPRISE rec.Entreprise, ACTION_RCS rec.ActionRegistre (fiches de
/// Serveur/Recopie/FichesV3.cs) ; NON_INSCRIT, DEPOT_RCS, EVENEMENT_RCS, ACTE_DEPOSE, CODE_EVENEMENT_RCS et ABREGE_PIECE sont
/// les tables V2 telles quelles (v2.*).
///
/// SOURCE (ancien RIG) : la validation ecrit le depot, jamais un brouillon (« Seule la validation génère un dépôt »,
/// PROC_DAS\ETP_DEPOT\ETP_DEPOT.cs:401-438) :
///   - l'action du registre : date d'arrivee, etat 2 (validee), etat info 2 (saisie reprise), code processus, numero
///     d'arrivee RA1 de l'annee d'arrivee (ETP_DEPOT.cs:343-349 ; ActionRcs.cs:32-43), rattachee au dossier ou au non-inscrit
///     (:350-354) ;
///   - le depot, numerote selon le type RCS (_GenererNumeroDepot, ETP_DEPOT.cs:524-573) -- ou dans la serie RDB, categorie N
///     pour un depot de beneficiaires (PROC_DASBEN.cs:1057-1077) ;
///   - l'evenement RCS du depot (EVNRC_OBJET_DEPOT, :407-414) et l'acte depose (ordre 1, :425-430) ;
///   - la demande : numeros = numero du depot, dossier ou non-inscrit, nom, indicatif, action, validee (:401-455).
/// </summary>
internal static class DepotsRcsV2
{
    // ------------------------------------------------------------ LE DOSSIER OU LE NON-INSCRIT

    /// <summary>Le dossier RCS (rec.DossierRcs) ou le non-inscrit (v2.NON_INSCRIT) qui recoit le depot.</summary>
    internal sealed record EntiteRcs(bool EstDossier, int Id, string Numero, string TypeRcs, bool Eirl, bool PersonnePhysique,
                                     string Designation, DateTime? Immatriculation);

    /// <summary>
    /// L'entite de numero <paramref name="numero"/> : un dossier RCS par son numero de gestion, sinon un non-inscrit par son
    /// numero (l'etape de recherche de RIG v2 cherche l'un puis l'autre, OpeRechercheDossierRcs.SetNumMetier, PROC_DAS.cs:409-414).
    /// La designation est DesignationAvecCivilite (DossierRCS.cs:615, NonInscrit.cs:312) : la denomination d'une personne morale,
    /// sinon « civilite patronyme prenom ». Personne physique : un non-inscrit sans NINSC_PM (NonInscrit.cs:245) ; un dossier
    /// dont l'entreprise a un patronyme (lecture V3 de DossierRCS.IsPP, DossierRCS.cs:703 : l'inscrit PP n'est pas repris en V3).
    /// </summary>
    internal static async Task<EntiteRcs?> TrouverAsync(GesteEnCours geste, string numero, CancellationToken ct)
    {
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) d.Id, d.NumeroGestion, d.TypeRcs, CONVERT(bit, ISNULL(d.Eirl, 0)) AS Eirl, d.DateImmatriculation,
                   d.DatePremiereImmatriculation, e.Civilite, e.Denomination, e.Patronyme, e.Prenom
            FROM rec.DossierRcs AS d LEFT JOIN rec.Entreprise AS e ON e.Id = d.IdEntreprise
            WHERE d.NumeroGestion = @n ORDER BY d.Id
            """, ct, new SqlParameter("@n", numero)))
            if (await r.ReadAsync(ct))
            {
                var patronyme = FormeLigne.Texte(r, "Patronyme");
                return new EntiteRcs(true, Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "NumeroGestion"),
                    FormeLigne.Texte(r, "TypeRcs"), r["Eirl"] is true, patronyme.Length > 0,
                    Designation(patronyme.Length > 0 ? "" : FormeLigne.Texte(r, "Denomination"), FormeLigne.Texte(r, "Civilite"), patronyme,
                        FormeLigne.Texte(r, "Prenom")),
                    FormeLigne.DateDe(r, "DatePremiereImmatriculation") ?? FormeLigne.DateDe(r, "DateImmatriculation"));
            }
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) n.NINSC_ID_NINSC, n.NINSC_NUM_NON_INSCRIT, n.NINSC_TYPE_RCS, CONVERT(bit, ISNULL(n.NINSC_PM, 0)) AS Pm,
                   n.NINSC_DENOMINATION, n.NINSC_CIVILITE, n.NINSC_PATRONYME
            FROM v2.NON_INSCRIT AS n WHERE n.NINSC_NUM_NON_INSCRIT = @n ORDER BY n.NINSC_ID_NINSC
            """, ct, new SqlParameter("@n", numero)))
            if (await r.ReadAsync(ct))
            {
                var pm = r["Pm"] is true;
                return new EntiteRcs(false, Convert.ToInt32(r["NINSC_ID_NINSC"], CultureInfo.InvariantCulture),
                    FormeLigne.Texte(r, "NINSC_NUM_NON_INSCRIT"), FormeLigne.Texte(r, "NINSC_TYPE_RCS"), false, !pm,
                    pm ? FormeLigne.Texte(r, "NINSC_DENOMINATION")
                       : Designation("", FormeLigne.Texte(r, "NINSC_CIVILITE"), FormeLigne.Texte(r, "NINSC_PATRONYME"), ""),
                    null);
            }
        return null;
    }

    // fix-ok: mesure le 26/11/2026 (lot 5) -- rec.Entreprise.Denomination est NOT NULL (REC-012_fiches-partagees.sql:66) : une
    // personne physique a donc toujours une denomination, et la V2 la designe par l'inscrit PP (« civilite patronyme prenom »),
    // pas par elle ; la famille de trace « registre » n'existe pas dans soc.FamilleJournal (migrations lues), « dossier » oui.
    private static string Designation(string denomination, string civilite, string patronyme, string prenom)
        => denomination.Length > 0 ? denomination : string.Join(" ", new[] { civilite, patronyme, prenom }.Where(s => s.Length > 0));

    /// <summary>Le refus d'un numero qui ne designe rien dans la base du greffe.</summary>
    internal static string Introuvable(string numero)
        => $"Aucun dossier RCS ni non-inscrit ne porte le numéro « {numero} » dans la base du greffe.";

    // ------------------------------------------------------------ LE DEPOT

    /// <summary>
    /// La categorie et la serie du numero de depot d'un depot d'actes (_GenererNumeroDepot, ETP_DEPOT.cs:530-573) : B1 a D2 →
    /// A / RD1 ; A1, A2 → D / RD2 ; AC → F / RD3 (envoi INPI) ; I1, I2 → E / RD4 (envoi INPI) ; sinon EIRL → E / RD4, personne
    /// physique → D / RD2, autre → A / RD1. Le cas « BenX » (RDB) est celui de DASBEN, qui a sa propre serie.
    /// </summary>
    internal static (string Categorie, string Serie, bool EnvoiInpi) NumerotationDepotActes(EntiteRcs e) => e.TypeRcs switch
    {
        "B1" or "C1" or "D1" or "B2" or "C2" or "D2" => ("A", "RD1", false),
        "A1" or "A2" => ("D", "RD2", false),
        "AC" => ("F", "RD3", true),
        "I1" or "I2" => ("E", "RD4", true),
        _ when e.EstDossier && e.Eirl => ("E", "RD4", false),
        _ when e.PersonnePhysique => ("D", "RD2", false),
        _ => ("A", "RD1", false),
    };

    /// <summary>Ce qu'un depot valide ecrit (ETP_DEPOT.cs:343-455, PROC_DASBEN.cs:1034-1077).</summary>
    internal sealed record DepotSaisi(string CodeProcessus, EntiteRcs Entite, DateTime DateArrivee, DateTime DateDepot,
        string Categorie, string Serie, bool EnvoiInpi, bool Gratuit, bool SaisieSimplifiee, string? EtatVax,
        string CodeEvenement, DateTime? DateEffet, string? TexteEvenement, string Piece, DateTime? DateActe, string? DescriptionActe)
    {
        /// <summary>Les colonnes du depot qu'un depot de comptes annuels ecrit en plus (exercice, confidentialite :
        /// ActionRcs.AddDepotRcs, ActionRcs.cs:220-245 -- EcrivainComptesAnnuels.cs) ; aucune pour un depot d'actes.</summary>
        public IReadOnlyDictionary<string, object?>? DepotEnPlus { get; init; }

        /// <summary>Les pieces deposees, dans l'ordre, a la place de la seule <see cref="Piece"/> : celles d'un depot de comptes
        /// annuels (BILA, CONFI, RESUL : DossierRCS.AddDCA, DossierRCS.cs:243-258). Null : la piece saisie, ordre 1.</summary>
        public IReadOnlyList<PieceDeposee>? Pieces { get; init; }
    }

    // fix-ok: extension, pas un correctif -- le depot de comptes annuels de RIG v2 (DossierRCS.AddDCA, DossierRCS.cs:205-258 ;
    // ActionRcs.AddDepotRcs, ActionRcs.cs:189-245) lie le depot a l'exercice, porte sa confidentialite et 1 a 3 pieces sans date ;
    // DepotSaisi n'avait qu'une piece datee. Les depots d'actes (DAS, DASBEN) gardent exactement leur ecriture (Pieces et
    // DepotEnPlus nuls). Lot 16 du plan « RIG v3 100 % fonctionnel », 27/11/2026.
    /// <summary>Une piece deposee sans date ni description (DepotRcs.AddActeDepose, DepotRcs.cs:100-108) : abrege, ordre, commandable.</summary>
    internal sealed record PieceDeposee(string Abrege, int Ordre, bool Commandable);

    /// <summary>Le depot ecrit : ses numeros et sa demande.</summary>
    internal sealed record DepotEcrit(int IdDepot, string NumeroDepot, string NumeroArrivee, int IdDemande);

    /// <summary>
    /// ECRIT le depot valide <paramref name="s"/> : numero d'arrivee RA1 puis numero de depot (un numero refuse : rien
    /// d'ecrit, la transaction du geste annule tout), l'action, le depot, l'evenement RCS, l'acte depose, la demande validee.
    /// </summary>
    internal static async Task<(DepotEcrit? Depot, string? Refus)> EcrireAsync(GesteEnCours geste, DepotSaisi s, CancellationToken ct)
    {
        var (arrivee, refusArrivee) = await NumeroAsync(geste, "RA1", s.DateArrivee.Year, ct);
        if (arrivee is null) return (null, refusArrivee);
        var (numero, refusDepot) = await NumeroAsync(geste, s.Serie, s.DateDepot.Year, ct);
        if (numero is null) return (null, refusDepot);

        var (demande, refusDemande) = await CreerDemandeAsync(geste, s.CodeProcessus, "V", ct);
        if (demande is null) return (null, refusDemande);

        // L'etat de transmission n'est pas pose ici : le DEFAULT de RIG_DEV (« M », CreationV2.DefautsRigDev) le donne, et la
        // recopie vers la V2 pose le sien a la creation (FichesV3.cs, ReglesObjetsDuRegistre : DPTRC_ETAT_TRANSMISSION 'N').
        var colonnesDepot = new Dictionary<string, object?>
        {
            ["DPTRC_NUM_DEPOT"] = numero, ["DPTRC_DATE"] = s.DateDepot, ["DPTRC_CATEGORIE_DEPOT"] = s.Categorie,
            ["DPTRC_ENVOI_INPI"] = s.EnvoiInpi, ["DPTRC_GRATUIT"] = s.Gratuit, ["DPTRC_NUMERISE"] = false,
            ["DPTRC_SAISIE_SIMPLIFIEE"] = s.SaisieSimplifiee, ["DPTRC_ETAT_INFO"] = "1",
        };
        if (s.EtatVax is not null) colonnesDepot["DPTRC_ETAT_VAX"] = s.EtatVax;
        foreach (var (colonne, valeur) in s.DepotEnPlus ?? new Dictionary<string, object?>()) colonnesDepot[colonne] = valeur;
        var depot = await InsererAsync(geste, "v2.DEPOT_RCS", "DPTRC_ID_DPTRC", colonnesDepot, ct);
        var action = await geste.InsererAsync("rec.ActionRegistre", "Id", new Dictionary<string, object?>
        {
            ["IdDossier"] = s.Entite.EstDossier ? s.Entite.Id : null, ["IdNonInscrit"] = s.Entite.EstDossier ? null : s.Entite.Id,
            ["IdDepot"] = depot, ["NumeroArrivee"] = arrivee, ["DateArrivee"] = s.DateArrivee, ["CodeProcessus"] = s.CodeProcessus,
            ["EtatInfo"] = "2", ["EtatAction"] = "2", ["SurNotification"] = false,
        }, ct);
        await InsererAsync(geste, "v2.EVENEMENT_RCS", "EVNRC_ID_EVNRC", new Dictionary<string, object?>
        {
            ["EVNRC_ID_ACTRC"] = action, ["EVNRC_NUM_ORDRE"] = (short)1, ["EVNRC_CODE_EVENEMENT_RCS"] = s.CodeEvenement,
            ["EVNRC_DATE_EFFET"] = s.DateEffet, ["EVNRC_OBJET_DEPOT"] = true,
            ["EVNRC_TEXTE_EVENEMENT"] = string.IsNullOrWhiteSpace(s.TexteEvenement) ? null : s.TexteEvenement,
        }, ct);
        if (s.Pieces is null)
            await InsererAsync(geste, "v2.ACTE_DEPOSE", "ACTDP_ID_ACDPS", new Dictionary<string, object?>
            {
                ["ACTDP_ID_DPTRC"] = depot, ["ACTDP_NUM_ORDRE"] = 1, ["ACTDP_ABREGE_PIECE"] = s.Piece, ["ACTDP_DATE"] = s.DateActe,
                ["ACTDP_DESCRIPTION"] = string.IsNullOrWhiteSpace(s.DescriptionActe) ? null : s.DescriptionActe,
            }, ct);
        else
            // AddActeDepose (DepotRcs.cs:100-108) : nombre « 1 », commandable selon l'abrege, etat INPI « 1 » (DEFAULT de CreationV2).
            foreach (var p in s.Pieces)
                await InsererAsync(geste, "v2.ACTE_DEPOSE", "ACTDP_ID_ACDPS", new Dictionary<string, object?>
                {
                    ["ACTDP_ID_DPTRC"] = depot, ["ACTDP_NUM_ORDRE"] = p.Ordre, ["ACTDP_ABREGE_PIECE"] = p.Abrege, ["ACTDP_NOMBRE"] = "1",
                    ["ACTDP_COMMANDABLE"] = p.Commandable,
                }, ct);
        await geste.EcrireAsync("""
            UPDATE v2.DEMANDE SET DMND_NOM = LEFT(@nom, 255), DMND_NUM_INDICATIF = LEFT(@indicatif, 14), DMND_LES_NUMEROS = @numero,
                   DMND_ID_ACTRC = @action, DMND_ID_DSRCS = @dossier, DMND_ID_AUTRE = @autre
            WHERE DMND_ID_DMND = @id
            """, ct, new SqlParameter("@nom", s.Entite.Designation), new SqlParameter("@indicatif", s.Entite.Numero),
            new SqlParameter("@numero", numero), new SqlParameter("@action", action),
            new SqlParameter("@dossier", s.Entite.EstDossier ? s.Entite.Id : DBNull.Value),
            new SqlParameter("@autre", s.Entite.EstDossier ? DBNull.Value : s.Entite.Id), new SqlParameter("@id", demande.Id));
        return (new DepotEcrit(depot, numero, arrivee, demande.Id), null);
    }

    /// <summary>Le code evenement RCS <paramref name="code"/> existe-t-il (v2.CODE_EVENEMENT_RCS, la liste de l'etape) ?</summary>
    internal static async Task<bool> CodeEvenementExisteAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT 1 FROM v2.CODE_EVENEMENT_RCS WHERE R_CODEEVENEM_VALEUR = @c", ct, new SqlParameter("@c", code));
        return await r.ReadAsync(ct);
    }

    /// <summary>L'abrege de piece <paramref name="piece"/> existe-t-il (v2.ABREGE_PIECE, combo de ultAbregePiece, proc_das.md:339) ?</summary>
    internal static async Task<bool> PieceExisteAsync(GesteEnCours geste, string piece, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT 1 FROM v2.ABREGE_PIECE WHERE R_ABREGEPIEC_VALEUR = @p", ct, new SqlParameter("@p", piece));
        return await r.ReadAsync(ct);
    }

    /// <summary>La trace du depot ecrit (famille « dossier » du journal des gestes, soc.FamilleJournal : celle des dossiers du registre).</summary>
    internal static TraceGeste Trace(string objet, DepotEcrit d, EntiteRcs e, string code) =>
        new("dossier", $"{objet} {d.NumeroDepot}", d.IdDepot.ToString(CultureInfo.InvariantCulture), "a cree", null,
            new { Depot = d.NumeroDepot, Arrivee = d.NumeroArrivee, Entite = e.Numero, e.Designation, CodeEvenement = code });

    // ------------------------------------------------------------ LA LISTE DES SAISIES

    /// <summary>
    /// Les saisies de depot de code processus <paramref name="code"/> : les demandes, la plus recente d'abord, avec leur action,
    /// leur depot, leur evenement et leur acte. Ecartees : supprimees (X) et terminees sans depot (T, le chemin d'echec
    /// d'une facture sans depot, OPE_ENTITE_JUD_RECHERCHE_DEPPCM.cs:421-427).
    /// </summary>
    internal static string Requete(string code) => $"""
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NUM_DEMANDE, d.DMND_ETAT_DEMANDE, CONVERT(bit, ISNULL(d.DMND_EN_COURS, 0)) AS EnCours,
               d.DMND_NOM, d.DMND_NUM_INDICATIF, d.DMND_ID_DSRCS, d.DMND_ID_AUTRE, p.DPTRC_NUM_DEPOT, p.DPTRC_DATE,
               CASE WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_RCS AS ev WHERE ev.EVNRC_ID_ACTRC = a.Id) THEN 1 ELSE 0 END AS Evenement,
               CASE WHEN EXISTS (SELECT 1 FROM v2.ACTE_DEPOSE AS ad WHERE ad.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC) THEN 1 ELSE 0 END AS Acte,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        LEFT JOIN rec.ActionRegistre AS a ON a.Id = d.DMND_ID_ACTRC
        LEFT JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = a.IdDepot
        WHERE d.DMND_CODE_PROSS = '{code}' AND d.DMND_ETAT_DEMANDE NOT IN ('X', 'T')
        ORDER BY d.DMND_ID_DMND DESC
        """;

    /// <summary>Les groupes des deux descripteurs, au mot pres : le dossier RCS, ou le non-inscrit (DMND_ID_AUTRE, ETP_DEPOT.cs:446-449).</summary>
    internal const string GroupeDossier = "Dossier RCS", GroupeNonInscrit = "Non-inscrit";

    /// <summary>
    /// Une saisie de depot. REGLE D'ETAT : validee (V) → « Enregistré », toutes les marches obligatoires <paramref name="obligatoires"/> ;
    /// refus (O) → « Refusé » (DAS seulement, <paramref name="refusee"/>), marches lues ; sinon (interrompue, en reclamation,
    /// qualifiee, en cours) les marches lues : <paramref name="entite"/> le dossier ou le non-inscrit, <paramref name="depot"/> le
    /// depot, <paramref name="evenement"/> son evenement, <paramref name="acte"/> son acte -- aucune : « À saisir ».
    /// </summary>
    internal static LigneLue Ligne(SqlDataReader r, int[] obligatoires, int entite, int depot, int evenement, int acte, string? refusee)
    {
        var etatV2 = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var nonInscrit = r["DMND_ID_AUTRE"] is not DBNull;
        var aEntite = nonInscrit || r["DMND_ID_DSRCS"] is not DBNull;
        var numeroDepot = FormeLigne.Texte(r, "DPTRC_NUM_DEPOT");
        var lues = new List<int>();
        if (aEntite) lues.Add(entite);
        if (numeroDepot.Length > 0) lues.Add(depot);
        if (Convert.ToInt32(r["Evenement"], CultureInfo.InvariantCulture) == 1) lues.Add(evenement);
        if (acte >= 0 && Convert.ToInt32(r["Acte"], CultureInfo.InvariantCulture) == 1) lues.Add(acte);
        var (etat, faites) = etatV2 switch
        {
            "V" => ("Validee", obligatoires),
            "O" when refusee is not null => (refusee, lues.ToArray()),
            _ => lues.Count == 0 ? ("AFaire", Array.Empty<int>()) : ("EnCours", lues.ToArray()),
        };
        var indicatif = FormeLigne.Texte(r, "DMND_NUM_INDICATIF");
        var dateDepot = FormeLigne.DateDe(r, "DPTRC_DATE");
        var precisions = FormeLigne.Joindre(
            indicatif.Length == 0 ? $"demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}" : (nonInscrit ? $"Non-inscrit {indicatif}" : $"Dossier RCS {indicatif}"),
            numeroDepot.Length > 0 ? $"dépôt {numeroDepot}" + (dateDepot is null ? "" : $" du {FormeLigne.Date(dateDepot)}") : null,
            etatV2 switch { "O" => "refusé", "N" => "en réclamation", "I" => "interrompu", _ => null });
        var nom = FormeLigne.Texte(r, "DMND_NOM");
        return new LigneLue(nom.Length > 0 ? nom : $"Demande {FormeLigne.Texte(r, "DMND_NUM_DEMANDE")}", precisions,
            nonInscrit ? GroupeNonInscrit : GroupeDossier, etat, faites)
        { Id = FormeLigne.Texte(r, "DMND_ID_DMND") };
    }
}
