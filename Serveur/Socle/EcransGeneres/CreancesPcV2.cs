using System.Globalization;
using Microsoft.Data.SqlClient;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// LES CREANCES DES PROCEDURES COLLECTIVES COMME RIG v2 : ce que « Ajouter une créance » (PROC_ADDCREANCE) et « Intégrer des
/// créances » (PROC_INTEGCREANCE) partagent -- l'INTEGRATION d'une creance transitoire en ADMISSION (plan « RIG v3 100 %
/// fonctionnel », lot 12, 27/11/2026). « Modifier une créance » (PROC_MODCREANCE) relit et modifie ce qu'elle cree.
///
/// SOURCE (ancien RIG) : IntegrationCreances.GenereCreances (D:\RigApplication\Source\RIG\DLL\Processus\PROC_INTEGCREANCE\INTCREA\
/// 1 Integration des Creances Transitoires\IntegrationCreances.cs:75-539), puis CreanceTransitoire.SauveImportation
/// (Outils\DLL\RigMetier\JUDICIAIRE\Creances\CreanceTransitoire.cs:1063-1160). Pour UNE creance transitoire en admission :
///   1. la PC par son numero (CRTRA_NUM_PC = PRCLL_NUM_PC, CreanceTransitoire.cs:59-73) ; absente -> « PC absente en base » (:110) ;
///   2. une affaire « Intégration auto de créances » (:97, Affaire.CreateAffaire) et sa demande INTCREA, qualifiee (« Q »),
///      nommee « Admission créance &lt;créancier&gt; » (:127-136, Demande.CreateDemandeSeule) -- CreationV2.CreerDemandeAsync ; ses
///      numeros = le numero de la PC (SauveImportation, :1074-1079) ;
///   3. la creance PC (:149-169) : numero d'ordre = nombre de creances de la PC + 1 (ProcedureCollective.AddCreancePC,
///      ProcedureCollective.cs:1899), les montants declares et admis, la nature admise, les observations, l'etat et le rejete
///      COPIES de la transitoire, CRNPC_NATURE_CREANCE = « Nature », monnaie EUR (DEFAULT de RIG_DEV, CREANCE_PC.sql:6) ;
///   4. son creancier PC (:171-185) : civilite, nom, SIREN, references, mode de presentation « RA » si un mandataire le
///      represente, « A » sinon ; le mandataire et l'adresse de la transitoire (CRTRA_ID_MNDTR, CRTRA_ID_ADRBS_CR) s'ils existent ;
///   5. la PC sauvee (SauveImportation :1070 -> ProcedureCollective.Sauve, ProcedureCollective.cs:2943-2970) : compteur de mise
///      a jour + 1, date de mise a jour, etat info (1-2 garde, 3-6 -> 2, sinon 1) ; et le nom d'affaire du fichier = le debiteur
///      de la transitoire s'il differe (IntegrationCreances.cs:526-533) ;
///   6. la transitoire integree : CRTRA_DATE_INSERTION_RIG = maintenant (ImportationTerminee, CreanceTransitoire.cs:975-977).
/// CE QUI CREE UNE INSTANCE N'EST PAS REPRIS (nomme, refuse) : une contestation, un defaut de reponse, une admission a l'etat 6
/// (ordonnance sans debat) ou sur une PC ouverte avant le 15/02/2009 (InstanceACreer, CreanceTransitoire.cs:961-965 ;
/// EditionAvisPC :947-951) creent, dans la V2, une instance numerotee, ses parties, l'appel, la decision, les evenements et le
/// depot (IntegrationCreances.cs:186-500). La V3 refuse ces cas, sans rien ecrire.
/// NON REPRIS (nomme) : la ligne de rappel general de la demande (Demande.CreateLigneRappGene), sa categorie (DMND_ID_CTGR), un
/// mandataire NOUVEAU (non enregistre) et l'adresse saisie en lignes (la fiche rec.Adresse n'est pas creee : les lignes restent
/// sur la transitoire), le recalcul de l'etat et du type de la PC par sa sauvegarde (une admission ne les change pas), les editions
/// (avis d'admission) et le courriel des erreurs (Outils.SendMailJudHL).
/// </summary>
internal static class CreancesPcV2
{
    /// <summary>L'etat d'une PC ouverte avant cette date : la V2 n'edite pas d'avis, elle cree une instance (EditionAvisPC).</summary>
    internal static readonly DateTime DebutAvis = new(2009, 2, 15);

    /// <summary>Les montants d'une creance (transitoire ou PC), dans l'ordre des colonnes de la V2 (CREANCE_TRANSITOIRE.sql).</summary>
    internal static readonly (string Libelle, string Suffixe)[] Montants =
    [
        ("Privilégié échu", "ADMIS_PRIVI_ECHU"), ("Privilégié non échu", "ADMIS_PRIVI_NONECHU"), ("Privilégié provisionnel", "ADMIS_PRIVI_PROVISIONNEL"),
        ("Chirographaire échu", "ADMIS_CHIRO_ECHU"), ("Chirographaire non échu", "ADMIS_CHIRO_NONECHU"), ("Chirographaire provisionnel", "ADMIS_CHIRO_PROVISIONNEL"),
        ("Super privilégié", "ADMIS_SUPER_PRIVI_ECHU"), ("Rejeté", "REJETE"),
    ];

    /// <summary>Le refus de la V2 sans montant (OPE_CREANCE.cs:315-320), au mot pres.</summary>
    internal const string MontantObligatoire =
        "Au moins un montant doit être saisi pour cette créance. Veuillez renseigner au moins un montant de créance dans la ou les cases concernées.";

    /// <summary>Le refus de la V2 quand l'integration echoue (PROC_ADDCREANCE.cs:405-407), ses retours a la ligne en espaces.</summary>
    internal static string EchecIntegration(string erreur)
        => $"Impossible d'intégrer la créance à cause de l'erreur suivante : {erreur}. Veuillez corriger les problèmes pour pouvoir valider l'ajout de la créance.";

    /// <summary>Le refus de la V3 pour ce qui cree une instance (en tete).</summary>
    internal static string InstanceNonReprise(string cas)
        => $"{cas} : RIG v2 crée alors une instance (numéro de rôle, parties, décision, événements), que la V3 ne crée pas encore. Rien n'est enregistré.";

    internal sealed record Transitoire(int Id, string? NumPc, string Nom, string? Civilite, string? Siren, string? References, int? IdMandataire,
        int? IdAdresse, bool Contestee, bool DefautReponse, string? Etat, string? NomDebiteur, IReadOnlyDictionary<string, decimal?> Admis,
        IReadOnlyDictionary<string, decimal?> Declares, string? NatureAdmise, string? Observations);

    internal sealed record CreanceIntegree(int IdCreancePc, int NumeroOrdre, int IdCreancier, int IdPc, string NumPc, DemandeCreee Demande);

    private static readonly string[] Declares = ["DECLARE_PRIVI_ECHU", "DECLARE_PRIVI_NONECHU", "DECLARE_CHIRO_ECHU", "DECLARE_CHIRO_NONECHU"];

    /// <summary>La creance transitoire <paramref name="id"/>, sous verrou ; null si absente ou deja integree.</summary>
    internal static async Task<Transitoire?> TransitoireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        var colonnes = string.Join(", ", Montants.Select(m => "CRTRA_" + m.Suffixe).Concat(Declares.Select(d => "CRTRA_" + d)));
        await using var r = await geste.LireAsync($"""
            SELECT CRTRA_ID_CRTRA, CRTRA_NUM_PC, CRTRA_NOM_CR, CRTRA_CIVILITE_CR, CRTRA_SIREN, CRTRA_REFERENCES_CR, CRTRA_ID_MNDTR, CRTRA_ID_ADRBS_CR,
                   CRTRA_CREANCE_IS_CONTESTE, CRTRA_DEFAUT_REPONSE, CRTRA_ETAT_CREANCE, CRTRA_NOM_DEBITEUR, CRTRA_NATURE_CREANCE_ADM,
                   CRTRA_OBSERVATIONS, {colonnes}
            FROM v2.CREANCE_TRANSITOIRE WITH (UPDLOCK, HOLDLOCK)
            WHERE CRTRA_ID_CRTRA = @id AND CRTRA_DATE_INSERTION_RIG IS NULL
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        string? T(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } t ? t : null;
        return new Transitoire(id, T("CRTRA_NUM_PC"), FormeLigne.Texte(r, "CRTRA_NOM_CR"), T("CRTRA_CIVILITE_CR"), T("CRTRA_SIREN"),
            T("CRTRA_REFERENCES_CR"), CriteresConsultation.EntierDe(r, "CRTRA_ID_MNDTR"), CriteresConsultation.EntierDe(r, "CRTRA_ID_ADRBS_CR"),
            CriteresConsultation.Vrai(r, "CRTRA_CREANCE_IS_CONTESTE"), CriteresConsultation.Vrai(r, "CRTRA_DEFAUT_REPONSE"), T("CRTRA_ETAT_CREANCE"),
            T("CRTRA_NOM_DEBITEUR"), Montants.ToDictionary(m => m.Suffixe, m => CriteresConsultation.Decimal(r, "CRTRA_" + m.Suffixe)),
            Declares.ToDictionary(d => d, d => CriteresConsultation.Decimal(r, "CRTRA_" + d)), T("CRTRA_NATURE_CREANCE_ADM"), T("CRTRA_OBSERVATIONS"));
    }

    private sealed record Pc(int Id, string Numero, DateTime? DateSauvOuv, string? NomAffaireFichier, string? EtatInfo);

    /// <summary>
    /// Integre <paramref name="t"/> en admission (en tete). Rend la creance PC creee, ou l'erreur de la V2 (CRTRA_NATURE_ERREUR) ou le
    /// cas non repris, a refuser par l'ecrivain (le socle annule alors tout ce que le geste a ecrit).
    /// </summary>
    internal static async Task<(CreanceIntegree? Creance, string? Erreur, string? NatureErreurV2)> IntegrerAdmissionAsync(GesteEnCours geste, Transitoire t,
        CancellationToken ct)
    {
        if (t.Contestee) return (null, InstanceNonReprise("Une contestation de créance"), null);
        if (t.DefautReponse) return (null, InstanceNonReprise("Un défaut de réponse"), null);
        if (t.Etat?.Trim() == "6") return (null, InstanceNonReprise("Une admission à l'état 6 (ordonnance sans débat)"), null);

        Pc? pc = null;
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) PRCLL_ID_PRCLL, PRCLL_NUM_PC, ISNULL(PRCLL_DATE_SAUVEGARDE, PRCLL_DATE_OUVERTURE) AS DateSauvOuv,
                   PRCLL_NOM_AFFAIRE_FICHIER, PRCLL_ETAT_INFO
            FROM v2.PROCEDURE_COLLECTIVE WITH (UPDLOCK, HOLDLOCK) WHERE PRCLL_NUM_PC = @num ORDER BY PRCLL_ID_PRCLL
            """, ct, new SqlParameter("@num", (object?)t.NumPc ?? DBNull.Value)))
            if (await r.ReadAsync(ct))
                pc = new Pc(r.GetInt32(0), FormeLigne.Texte(r, "PRCLL_NUM_PC"), FormeLigne.DateDe(r, "DateSauvOuv"),
                    r["PRCLL_NOM_AFFAIRE_FICHIER"] as string, r["PRCLL_ETAT_INFO"] as string);
        if (pc is null) return (null, EchecIntegration("PC absente en base"), "PC absente en base");
        // EditionAvisPC (CreanceTransitoire.cs:947-951) : une PC ouverte (ou sauvegardee) avant le 15/02/2009 n'a pas d'avis.
        // ForcerEditionAvis ne vaut que pour Villefranche (6903) et Aurillac (1501), ParametrageCreance.cs:437-441.
        if (pc.DateSauvOuv is not { } ouverture || ouverture < DebutAvis)
            return (null, InstanceNonReprise($"Une admission sur la PC {pc.Numero}, ouverte avant le 15/02/2009 (sans avis d'admission)"), null);

        var (demande, refus) = await CreerDemandeAsync(geste, "INTCREA", "Q", ct);
        if (demande is null) return (null, refus, null);
        await geste.EcrireAsync("UPDATE v2.AFFAIRE SET AFFRE_LIBELLE = @libelle WHERE AFFRE_ID_AFFRE = @id", ct,
            new SqlParameter("@libelle", "Intégration auto de créances"), new SqlParameter("@id", demande.IdAffaire));
        await geste.EcrireAsync("""
            UPDATE v2.DEMANDE SET DMND_NOM = LEFT(@nom, 255), DMND_LES_NUMEROS = @num, DMND_NUM_INDICATIF = @num WHERE DMND_ID_DMND = @id
            """, ct, new SqlParameter("@nom", "Admission créance " + t.Nom), new SqlParameter("@num", pc.Numero), new SqlParameter("@id", demande.Id));

        var creancier = await geste.InsererAsync("v2.CREANCIER_PC", "CREPC_ID_CREPC", new Dictionary<string, object?>
        {
            ["CREPC_CIVILITE"] = t.Civilite, ["CREPC_NOM"] = t.Nom, ["CREPC_SIREN"] = t.Siren, ["CREPC_REFERENCES"] = t.References,
            ["CREPC_MODE_PRESENTATION"] = t.IdMandataire is null ? "A" : "RA", ["CREPC_ID_MNDTR"] = t.IdMandataire,
            ["CREPC_ID_ADRBS"] = t.IdAdresse, ["CREPC_SUBROGE"] = false,
        }, ct);
        int ordre;
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.CREANCE_PC WITH (UPDLOCK, HOLDLOCK) WHERE CRNPC_ID_PRCLL = @pc", ct,
                         new SqlParameter("@pc", pc.Id)))
        {
            await r.ReadAsync(ct);
            ordre = r.GetInt32(0) + 1;
        }
        var valeurs = new Dictionary<string, object?>
        {
            ["CRNPC_ID_PRCLL"] = pc.Id, ["CRNPC_NUM_ORDRE"] = (short)ordre, ["CRNPC_ID_CREPC"] = creancier, ["CRNPC_MONNAIE"] = "EUR",
            ["CRNPC_NATURE_CREANCE"] = "Nature", ["CRNPC_NATURE_CREANCE_ADM"] = t.NatureAdmise, ["CRNPC_OBSERVATIONS"] = t.Observations,
            ["CRNPC_ETAT_CREANCE"] = t.Etat ?? "0",
        };
        foreach (var (_, suffixe) in Montants) valeurs["CRNPC_" + suffixe] = t.Admis[suffixe];
        foreach (var d in Declares) valeurs["CRNPC_" + d] = t.Declares[d];
        var creance = await geste.InsererAsync("v2.CREANCE_PC", "CRNPC_ID_CRNPC", valeurs, ct);

        await geste.EcrireAsync("""
            UPDATE v2.PROCEDURE_COLLECTIVE SET
                PRCLL_COMPTEUR_MAJ = ISNULL(PRCLL_COMPTEUR_MAJ, 0) + 1, PRCLL_DATE_MAJ = @maintenant,
                PRCLL_ETAT_INFO = CASE WHEN PRCLL_ETAT_INFO IN ('1', '2') THEN PRCLL_ETAT_INFO
                                       WHEN PRCLL_ETAT_INFO IN ('3', '4', '5', '6') THEN '2' ELSE '1' END,
                PRCLL_NOM_AFFAIRE_FICHIER = CASE WHEN @debiteur IS NOT NULL AND @debiteur <> PRCLL_NOM_AFFAIRE
                                                 AND ISNULL(PRCLL_NOM_AFFAIRE_FICHIER, '') <> @debiteur THEN LEFT(@debiteur, 250)
                                                 ELSE PRCLL_NOM_AFFAIRE_FICHIER END
            WHERE PRCLL_ID_PRCLL = @id
            """, ct, new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@debiteur", (object?)t.NomDebiteur ?? DBNull.Value),
            new SqlParameter("@id", pc.Id));
        await geste.EcrireAsync("UPDATE v2.CREANCE_TRANSITOIRE SET CRTRA_DATE_INSERTION_RIG = @maintenant, CRTRA_NATURE_ERREUR = NULL WHERE CRTRA_ID_CRTRA = @id",
            ct, new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", t.Id));
        return (new CreanceIntegree(creance, ordre, creancier, pc.Id, pc.Numero, demande), null, null);
    }

    /// <summary>La trace d'une admission integree : la creance PC, ses montants admis.</summary>
    internal static TraceGeste Trace(Transitoire t, CreanceIntegree c) =>
        new("affaire", $"la créance n° {c.NumeroOrdre} de la PC {c.NumPc} ({t.Nom})", c.IdCreancePc.ToString(CultureInfo.InvariantCulture), "a cree", null,
            new
            {
                Transitoire = t.Id, Pc = c.NumPc, Creancier = t.Nom, Demande = c.Demande.Numero,
                Montants = Montants.Where(m => t.Admis[m.Suffixe] is not null).ToDictionary(m => m.Libelle, m => t.Admis[m.Suffixe]),
            });
}
