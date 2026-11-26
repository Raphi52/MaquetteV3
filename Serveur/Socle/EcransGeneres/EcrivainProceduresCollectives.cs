using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurProceduresCollectives;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Procédure collective » (PROC_MPC) : ENREGISTRER, dans sa fiche, ce que RIG v2 modifie SANS FACTURE sur une PC ou une
/// prevention. Mode d'emploi : EcrivainEcran.cs ; la fiche : LecteurProceduresCollectives.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MPC (modes MPC et MODPR). La PC se modifie dans OPE_PROCEDURE_COLLECTIVE
/// (Rig_Etape_Instance\PROCEDURE_COLLECTIVE\Saisie\ETP_PROCEDURE_COLLECTIVE), sauvee par ProcedureCollective.Sauve ; la
/// prevention dans OPE_PREVENTION (Rig_Etape_Instance\PREVENTION\Saisie), sauvee par Prevention.Sauve.
///
/// CE QUI SE MODIFIE ICI, ET POURQUOI CELA SEUL :
///   - PC : la date de cessation des paiements (ultDateCessationPaiement, OPE_PROCEDURE_COLLECTIVE.cs:202). La date
///     d'ouverture d'une PC NE se modifie PAS ici : dans RIG v2 elle relance le calcul du forfait (abonnement
///     OPE_PROCEDURE_COLLECTIVE.cs:141-152, MAJForfait :690-740), qui peut decocher « PC facturée au forfait » et rendre la
///     modification facturee ;
///   - prevention : la date d'ouverture (ultDateOuverture, OPE_PREVENTION.cs:86) ; la date de cessation n'est pas un champ
///     d'OPE_PREVENTION (seulement d'OPE_PREVENTION_READONLY.cs:122) ;
///   - les autres donnees de MPC (organes, etablissements, dirigeants, chiffres, notification, evenements) passent par une
///     quinzaine d'operations que cette fiche ne porte pas.
///
/// CONTROLES DE RIG v2, dans l'ordre, avant toute ecriture (un refus n'ecrit rien) :
///   1. la PC ou la prevention existe (sinon « Procédure collective introuvable ») ;
///   2. elle n'est pas tenue par une autre demande : RIG v2 pose le verrou a la selection et n'entre pas en saisie sinon
///      (PROC_MPC.cs:141-151) ; le message est celui de RIG v2 (VerrouDemandeV2) ;
///   3. sa modification n'est pas FACTUREE : une PC hors forfait dont le reglage MPC_TYPE_FACTURATION_CLIENT_EN_COMPTE (client
///      en compte) ou _DIVERS (sinon) vaut « F » ou « T » l'est (PROC_MPC.cs:482-500) ; la V3 ne reprend pas la facturation et
///      refuse en le disant (ProcedureLue.MotifFacturee). Une PC au forfait (:483-484) et une prevention (:501-502) ne le sont
///      jamais ;
///   4. seul le champ ci-dessus se modifie (ProcedureLue.ChampModifiable), date lisible ou vide (le champ n'est pas obligatoire) ;
///   5. DoVerifier de l'operation (erreurs bloquantes ; les avertissements de RIG v2 ne bloquent pas,
///      FrmRapportVerification.cs:117) : PC sans adresse -> « L'adresse de la PC n'est pas renseignée. Veuillez saisir au moins
///      un établissement avec une adresse. », sinon PC sans entreprise reconnue -> « L'entreprise de la PC n'a pas été
///      reconnue. Veuillez vérifier la saisie de l'entreprise ou veuillez contacter AMITEL. » (OPE_PROCEDURE_COLLECTIVE.cs:867-872) ;
///      prevention sans adresse, ou sans dossier RCS ni non-inscrit -> « L'adresse de la prévention n'est pas renseignée.
///      Veuillez saisir au moins un établissement avec une adresse. » (OPE_PREVENTION.cs:410-417).
///
/// CE QUI S'ECRIT :
///   - PC (v2.PROCEDURE_COLLECTIVE) : la date ; et comme DoPreparerSauvegarde (OPE_PROCEDURE_COLLECTIVE.cs:1063-1070) puis
///     ProcedureCollective.Sauve (ProcedureCollective.cs:2959-2981) : registre des metiers vide si la PC n'y est pas inscrite,
///     juridiction exterieure vide si la PC n'est pas exterieure, etat PC recalcule (recupEtatPC, :3189-3229), type PC
///     recalcule (recupTypePC, :3236-3266), acte de saisine copie de la premiere instance de PC (type 08) s'il est vide
///     (MAJActeSaisine, :2915-2920), compteur de mises a jour + 1, date de mise a jour, etat info ramene a 1 ou 2 ;
///   - prevention (v2.PREVENTION) : la date ; et comme Prevention.Sauve (Prevention.cs:2393-2402) : acte de procedure copie de
///     la premiere instance s'il est vide, compteur + 1, date de mise a jour ; puis les dates de ses mots directeurs
///     (Prevention.GenererMotDirecteur -> EnregistrerMotDirecteur, Prevention.cs:2549-2551, :2625-2630, :2661-2666) : ouverture,
///     solution et cloture de l'enregistrement v2.RIG_MOTS_DIRECTEUR_PCINST de la prevention, une date vide valant le
///     01/01/1800 (DBDateTime.ValeurNull, DBObject.cs:531, que ToShortDateString ecrit).
///   Trace : famille « affaire », « a modifie », avant et apres de la date (et de l'etat ou du type PC s'ils changent).
///
/// NON REPRIS (nommes) : la demande MPC / MODPR non facturee, une trace remplacee par le journal des gestes (decision du chef) ;
/// le nombre d'etablissements de la PC, recompte quand leur liste est chargee (ProcedureCollective.cs:2971) ; l'historique
/// d'etat PC (PC_ETAT_HISTO, pose par un evenement, :2984-2998) ; les mots directeurs d'une PC, que la date de cessation
/// n'alimente pas (ProcedureCollective.cs:5631-5658) ; pour une prevention, la chaine de mots (inchangee : la date d'ouverture
/// est rangee a part, code « PO ») et la suppression des dates d'audience de ses mots directeurs (Prevention.cs:2676-2680) ;
/// les mentions EDI et BDF, liees aux evenements supprimes (OPE_PREVENTION.cs:530-568).
/// </summary>
internal sealed class EcrivainProceduresCollectives : IEcrivainEcran
{
    /// <summary>Le nom de l'ecran au journal des gestes (le titre du descripteur) : le lecteur le relit pour l'etat.</summary>
    internal const string NomEcran = "Procedure collective";

    public string Procedure => "PROC_MPC";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. La PC ou la prevention, verrouillee en ecriture jusqu'a la fin de la transaction.
        if (geste.Demande.Ids is not [var id] || await LireProcedureAsync(geste.Base, geste.Transaction, id, ct) is not { } p)
            return IssueGeste.Refuser($"Procédure collective introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");

        // 2. Le verrou de RIG v2, relu dans la transaction.
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, p.Pc ? "Procédure collective" : "Prévention",
                p.Pc ? SequenceurPc : SequenceurPrevention, p.Id.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
            return IssueGeste.Refuser(verrou);

        // 3. Une modification facturee n'est pas reprise.
        if (p.MotifFacturee is { } facturee) return IssueGeste.Refuser(facturee);

        // 4. Le seul champ que RIG v2 laisse modifier sans facture.
        var cleModifiable = GesteEcran.Cle(SectionProcedure, p.ChampModifiable);
        foreach (var cle in geste.Demande.Valeurs.Keys.Where(c => c != cleModifiable))
            return IssueGeste.Refuser(cle == GesteEcran.Cle(SectionProcedure, ChampOuverture)
                ? $"La date d'ouverture de {p.EnMots} ne se modifie pas ici : dans RIG v2, elle recalcule le forfait de la PC, donc sa facturation."
                : $"« {cle} » ne se modifie pas sur {p.EnMots} dans RIG v2 depuis cette fiche.");
        if (!geste.Demande.Valeurs.TryGetValue(cleModifiable, out var saisie))
            return IssueGeste.Refuser($"Aucun champ n'est modifié : {p.EnMots} garde ses valeurs.");
        DateTime? date = null;
        if (saisie.Trim() is { Length: > 0 } texte)
        {
            if (!DateTime.TryParseExact(texte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return IssueGeste.Refuser($"« {p.ChampModifiable} » : date illisible ({texte}).");
            date = d;
        }

        // 5. DoVerifier de l'operation (erreurs bloquantes).
        if (p.Pc && !p.AvecAdresse)
            return IssueGeste.Refuser("L'adresse de la PC n'est pas renseignée. Veuillez saisir au moins un établissement avec une adresse.");
        if (p.Pc && !p.AvecEntreprise)
            return IssueGeste.Refuser("L'entreprise de la PC n'a pas été reconnue. Veuillez vérifier la saisie de l'entreprise ou veuillez contacter AMITEL.");
        if (!p.Pc && (!p.AvecAdresse || !p.AvecEntreprise))
            return IssueGeste.Refuser("L'adresse de la prévention n'est pas renseignée. Veuillez saisir au moins un établissement avec une adresse.");

        var avantDate = p.Pc ? p.Cessation : p.Ouverture;
        if (avantDate == date) return IssueGeste.Refuser($"Aucun champ n'est modifié : {p.EnMots} garde ses valeurs.");

        var parametres = new List<SqlParameter>
        {
            new("@id", p.Id), new("@maintenant", geste.Maintenant), new("@date", (object?)date ?? DBNull.Value),
        };
        var avant = new Dictionary<string, string>(StringComparer.Ordinal) { [p.ChampModifiable] = FormeLigne.DateFiche(avantDate) };
        var apres = new Dictionary<string, string>(StringComparer.Ordinal) { [p.ChampModifiable] = FormeLigne.DateFiche(date) };
        if (p.Pc)
        {
            // La cessation des paiements n'entre ni dans l'etat ni dans le type : les recalculer suit RIG v2, qui les corrige a
            // chaque sauvegarde.
            var etat = EtatPc(p);
            var type = TypePc(p);
            if (etat != (p.EtatPc ?? "")) { avant["État PC"] = p.EtatPc ?? ""; apres["État PC"] = etat; }
            if (type != (p.TypePc ?? "")) { avant["Type PC"] = p.TypePc ?? ""; apres["Type PC"] = type; }
            parametres.Add(new SqlParameter("@etat", etat));
            parametres.Add(new SqlParameter("@type", type));
            var touchees = await geste.EcrireAsync("""
                UPDATE v2.PROCEDURE_COLLECTIVE SET
                    PRCLL_DATE_CESS_PAIEMENT = @date,
                    PRCLL_DEPARTEMENT_RM = CASE WHEN PRCLL_INSCRIT_REGISTRE_METIER = 1 THEN PRCLL_DEPARTEMENT_RM END,
                    PRCLL_NUM_RM = CASE WHEN PRCLL_INSCRIT_REGISTRE_METIER = 1 THEN PRCLL_NUM_RM END,
                    PRCLL_RM_DEPENDANT = CASE WHEN PRCLL_INSCRIT_REGISTRE_METIER = 1 THEN PRCLL_RM_DEPENDANT END,
                    PRCLL_JURIDICTION_EXTERIEURE = CASE WHEN PRCLL_EXTERIEUR = 1 THEN PRCLL_JURIDICTION_EXTERIEURE END,
                    PRCLL_ETAT_PC = @etat, PRCLL_TYPE_PC = @type,
                    PRCLL_COMPTEUR_MAJ = ISNULL(PRCLL_COMPTEUR_MAJ, 0) + 1, PRCLL_DATE_MAJ = @maintenant,
                    PRCLL_ETAT_INFO = CASE WHEN PRCLL_ETAT_INFO IN ('1', '2') THEN PRCLL_ETAT_INFO
                                           WHEN PRCLL_ETAT_INFO IN ('3', '4', '5', '6') THEN '2' ELSE '1' END
                WHERE PRCLL_ID_PRCLL = @id
                """, ct, [.. parametres]);
            if (touchees != 1) throw new InvalidOperationException($"PC {p.Id} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");
            // MAJActeSaisine (ProcedureCollective.cs:2915-2920) : la premiere instance de type 08, par date de saisine.
            await geste.EcrireAsync("""
                UPDATE v2.PROCEDURE_COLLECTIVE SET
                    PRCLL_CODE_ACTE_PROCE = (SELECT TOP (1) i.INSTN_CODE_ACTE_PROCE FROM v2.INSTANCE AS i
                                             WHERE i.INSTN_ID_PRCLL = @id AND i.INSTN_TYPE_INSTANCE = '08'
                                             ORDER BY i.INSTN_DATE_SAISINE_GREFFE, i.INSTN_ID_INSTN),
                    PRCLL_DATE_ACTE = (SELECT TOP (1) i.INSTN_DATE_ACTE FROM v2.INSTANCE AS i
                                       WHERE i.INSTN_ID_PRCLL = @id AND i.INSTN_TYPE_INSTANCE = '08'
                                       ORDER BY i.INSTN_DATE_SAISINE_GREFFE, i.INSTN_ID_INSTN)
                WHERE PRCLL_ID_PRCLL = @id AND PRCLL_CODE_ACTE_PROCE IS NULL
                  AND EXISTS (SELECT 1 FROM v2.INSTANCE AS i WHERE i.INSTN_ID_PRCLL = @id AND i.INSTN_TYPE_INSTANCE = '08')
                """, ct, new SqlParameter("@id", p.Id));
        }
        else
        {
            var touchees = await geste.EcrireAsync("""
                UPDATE v2.PREVENTION SET
                    PREVE_DATE_OUVERTURE = @date,
                    PREVE_COMPTEUR_MAJ = ISNULL(PREVE_COMPTEUR_MAJ, 0) + 1, PREVE_DATE_MAJ = @maintenant
                WHERE PREVE_ID_PREVE = @id
                """, ct, [.. parametres]);
            if (touchees != 1) throw new InvalidOperationException($"Prévention {p.Id} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");
            // Prevention.Sauve (Prevention.cs:2394-2397) : la premiere instance de la prevention, par date de saisine.
            await geste.EcrireAsync("""
                UPDATE v2.PREVENTION SET
                    PREVE_CODE_ACTE_PROCE = (SELECT TOP (1) i.INSTN_CODE_ACTE_PROCE FROM v2.INSTANCE AS i WHERE i.INSTN_ID_PREVE = @id
                                             ORDER BY i.INSTN_DATE_SAISINE_GREFFE, i.INSTN_ID_INSTN),
                    PREVE_DATE_ACTE = (SELECT TOP (1) i.INSTN_DATE_ACTE FROM v2.INSTANCE AS i WHERE i.INSTN_ID_PREVE = @id
                                       ORDER BY i.INSTN_DATE_SAISINE_GREFFE, i.INSTN_ID_INSTN)
                WHERE PREVE_ID_PREVE = @id AND PREVE_CODE_ACTE_PROCE IS NULL
                  AND EXISTS (SELECT 1 FROM v2.INSTANCE AS i WHERE i.INSTN_ID_PREVE = @id)
                """, ct, new SqlParameter("@id", p.Id));
            // EnregistrerMotDirecteur, enregistrement existant (Prevention.cs:2661-2666) : les dates « PO », « PS », « PC ».
            await geste.EcrireAsync("""
                UPDATE v2.RIG_MOTS_DIRECTEUR_PCINST SET
                    MDPCI_PCDATE_OUV = ISNULL(@date, '18000101'),
                    MDPCI_PCDATE_SOL = ISNULL(@solution, '18000101'),
                    MDPCI_PCDATE_CLOT = ISNULL(@cloture, '18000101')
                WHERE MDPCI_ID_PREVE = @id
                """, ct, new SqlParameter("@id", p.Id), new SqlParameter("@date", (object?)date ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.DateTime },
                new SqlParameter("@solution", (object?)p.Solution ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.DateTime },
                new SqlParameter("@cloture", (object?)p.Cloture ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.DateTime });
        }

        var idEcran = $"{(p.Pc ? 'P' : 'R')}-{p.Id.ToString(CultureInfo.InvariantCulture)}";
        return IssueGeste.Ecrit($"{(p.Pc ? "PC" : "Prévention")} {p.Numero} enregistrée dans la base du greffe : « {p.ChampModifiable} ».", [idEcran],
            new TraceGeste("affaire", p.EnMots, idEcran, "a modifie", avant, apres));
    }

    /// <summary>recupEtatPC (ProcedureCollective.cs:3189-3229), sur la PC lue : null = colonne NULL (isNull de RIG v2).</summary>
    internal static string EtatPc(ProcedureLue p)
    {
        var cloture = p.CodeCloture ?? "";
        var solution = p.CodeSolution ?? "";
        if (cloture is "CEP" or "FRJDB" or "FSADB") return "5";
        if (p.CodeCloture is not null && p.Cloture is not null && p.Ouverture is null && p.RetablissementProfessionnel is null
            && p.Sauvegarde is null && p.SortieDeCrise is null) return "N";
        if (p.CodeCloture is not null) return cloture == "PCINF" ? "7" : "3";
        if (solution.StartsWith("LIJ", StringComparison.Ordinal) || solution.StartsWith("LJ", StringComparison.Ordinal)
            || solution.StartsWith("ELJ", StringComparison.Ordinal) || solution.StartsWith("CS", StringComparison.Ordinal)) return "2";
        if (p.CodeSolution is not null) return "4";
        if (p.Ouverture is not null) return "1";
        if (p.SortieDeCrise is not null) return "15";
        if (p.Sauvegarde is not null) return "10";
        if (p.RetablissementProfessionnel is not null) return "14";
        // « Si tout est null, PC non ouverte » : le code solution est forcement NULL ici (les branches au-dessus l'ont pris).
        return "N";
    }

    /// <summary>recupTypePC (ProcedureCollective.cs:3236-3266), sur la PC lue.</summary>
    internal static string TypePc(ProcedureLue p)
    {
        var type = "0";
        if (p.Ouverture is { } ouverture)
            type = ouverture < new DateTime(1967, 7, 14) ? "9" : ouverture < new DateTime(1986, 1, 1) ? "6" : "1";
        else if (p.SortieDeCrise is not null) type = "15";
        else if (p.Sauvegarde is not null) type = "10";
        else if (p.RetablissementProfessionnel is not null) type = "14";
        string s1 = p.CodeSolution ?? "", s2 = p.CodeSolution2 ?? "";
        if (s1 is "LIJI" or "LIJIS" || s2 is "LIJI" or "LIJIS" or "ELJ") type = "3";
        else if (s1 is "LJ" or "LJCS" || s2 is "LJ" or "LJCS") type = "2";
        else if (s1 == "LIJIR" || s2 == "LIJIR") type = "5";
        else if (s1 is "LB" or "LBRC") type = "7";
        return type;
    }
}
