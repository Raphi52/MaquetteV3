using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Consultation judiciaire » (PROC_CJUD, mode CJUDINFOG) : le geste TRAITER UN ENROLEMENT recu par le RPVA, ecrit dans la base
/// du greffe. Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 8, 28/11/2026. Route : ModuleConsultationJud.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_CJUD) :
///   - l'operation « Saisie de la demande » (CJUDINFOG\OPE_CJUDINFOG.cs) lie ses champs a l'enregistrement DEMAT_RPJC_ENR de la
///     demande (:83-102) : l'etat de traitement (« A » / « R », obligatoire, Designer :109), le type de rejet (MQPIECE « Manque
///     pièce », PAIEMENT « Paiement incorrect », :87-90, obligatoire, Designer :120) et le motif de rejet (300 caracteres,
///     obligatoire, Designer :133-135), montres seulement pour un refus (:220-231) ; l'etat du flux passe a « 2 » (Traité par le
///     greffe, :96-98) et la date de traitement est l'instant du geste (:102-104) ; DoSauve ecrit l'enregistrement (:252-255) ;
///   - REFUSER (PROC_CJUD.cs:1370-1375, :1388-1390) : ni tableau d'editions, ni facture, ni instance (etP_INSTANCE1.DontSave) ; la
///     demande est validee (Demande.Cloturer : DMND_ETAT_DEMANDE = 'V', DMND_EN_COURS = 0, Demande.cs:738-741).
/// ACCEPTER n'est PAS repris, nomme : il cree l'instance depuis le XML (Instance.CreateInstanceFromXmlRPJCEnr, PROC_CJUD.cs:461), la
/// met en GED avec la piece recue par la base EDI du greffe (:1411-1420), qui n'est pas dans la base V3 : refuse, rien d'ecrit.
/// ECART NOMME : RIG v2 exige d'avoir visualise les pieces jointes avant de valider (OPE_CJUDINFOG.cs:238-247) ; elles viennent du
/// flux entrant de la base EDI (FluxEntrant.GetFluxEntrant, :106, :152), absente de la base V3 : la V3 ne peut pas l'exiger.
/// Un enrolement deja traite n'est plus a traiter : RIG v2 l'ouvre par SA demande, validee et close une fois traitee (message de la V3).
/// </summary>
internal static class EcritureConsultationJud
{
    /// <summary>Le nom de l'ecran au journal des gestes.</summary>
    public const string Ecran = "Consultation judiciaire";

    /// <summary>Les types de rejet de la liste de RIG v2 (OPE_CJUDINFOG.cs:87-90), par code.</summary>
    internal static readonly IReadOnlyDictionary<string, string> TypesRejet = new Dictionary<string, string>
    {
        ["MQPIECE"] = "Manque pièce", ["PAIEMENT"] = "Paiement incorrect",
    };

    /// <summary>La longueur du motif de rejet (MetierMaxLength, OPE_CJUDINFOG.Designer.cs:133).</summary>
    public const int MotifMax = 300;

    /// <summary>Le refus de l'acceptation, que RIG v3 ne reprend pas.</summary>
    public const string AccepterNonRepris = "Accepter l'enrôlement crée l'instance depuis son XML et met en GED la pièce reçue par la base EDI "
                                          + "du greffe : RIG v3 ne le reprend pas, rien n'est enregistré. Acceptez-le dans RIG v2.";

    /// <summary>Les gestes ecrits : tout autre type est refuse par la route (GesteFaitMain, rien d'ecrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Enregistrer];

    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var texte] || !int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Choisissez un enrôlement de la liste.");
        string traitement, etatFlux, reference;
        int? idDemande;
        await using (var r = await geste.LireAsync("""
            SELECT r.RPJCE_ETAT_TRAITEMENT_RIG, r.RPJCE_ETAT, r.RPJCE_REFERENCE_DEMANDE, r.RPJCE_ID_DMND
            FROM v2.DEMAT_RPJC_ENR AS r WHERE r.RPJCE_ID_RPJCE = @id
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser("Cet enrôlement n'est plus dans la base du greffe : relisez la liste.");
            traitement = FormeLigne.Texte(r, "RPJCE_ETAT_TRAITEMENT_RIG").Trim();
            etatFlux = FormeLigne.Texte(r, "RPJCE_ETAT").Trim();
            reference = FormeLigne.Texte(r, "RPJCE_REFERENCE_DEMANDE").Trim();
            idDemande = r["RPJCE_ID_DMND"] is int d ? d : null;
        }
        if (traitement.Length > 0)
            return IssueGeste.Refuser($"Cet enrôlement est déjà traité ({(traitement == "A" ? "Accepté" : traitement == "R" ? "Refusé" : traitement)}) : sa demande est close.");

        var etat = (geste.Valeur(TraitementEnrolement.Section, TraitementEnrolement.Etat) ?? "").Trim();
        if (etat.Length == 0) return IssueGeste.Refuser(Obligatoire(TraitementEnrolement.Etat));
        if (etat == "A") return IssueGeste.Refuser(AccepterNonRepris);
        if (etat != "R") return IssueGeste.Refuser($"« {etat} » n'est pas un état de traitement de la liste (A Accepté, R Refusé).");
        var type = (geste.Valeur(TraitementEnrolement.Section, TraitementEnrolement.TypeRejet) ?? "").Trim();
        if (type.Length == 0) return IssueGeste.Refuser(Obligatoire(TraitementEnrolement.TypeRejet));
        if (!TypesRejet.ContainsKey(type)) return IssueGeste.Refuser($"« {type} » n'est pas un type de rejet de la liste ({string.Join(", ", TypesRejet.Keys)}).");
        var motif = (geste.Valeur(TraitementEnrolement.Section, TraitementEnrolement.MotifRejet) ?? "").Trim();
        if (motif.Length == 0) return IssueGeste.Refuser(Obligatoire(TraitementEnrolement.MotifRejet));
        if (motif.Length > MotifMax) return IssueGeste.Refuser($"« {TraitementEnrolement.MotifRejet} » : {MotifMax} caractères au plus.");

        await geste.EcrireAsync("""
            UPDATE v2.DEMAT_RPJC_ENR SET RPJCE_ETAT_TRAITEMENT_RIG = 'R', RPJCE_TYPE_REJET_RIG = @type, RPJCE_MOTIF_REJET_RIG = @motif,
                   RPJCE_ETAT = '2', RPJCE_DATE_TRAITEMENT_RIG = @maintenant
            WHERE RPJCE_ID_RPJCE = @id
            """, ct, new SqlParameter("@type", type), new SqlParameter("@motif", motif), new SqlParameter("@maintenant", geste.Maintenant),
            new SqlParameter("@id", id));
        var demandeValidee = idDemande is { } dmnd && await geste.EcrireAsync(
            "UPDATE v2.DEMANDE SET DMND_ETAT_DEMANDE = 'V', DMND_EN_COURS = 0 WHERE DMND_ID_DMND = @d", ct, new SqlParameter("@d", dmnd)) > 0;

        var objet = reference.Length > 0 ? $"l'enrôlement RPVA {reference}" : $"l'enrôlement n° {id}";
        return IssueGeste.Ecrit($"Enrôlement refusé ({TypesRejet[type]}){(demandeValidee ? " : sa demande est validée" : "")}.",
            [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("affaire", objet, id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Etat = etatFlux, Traitement = "" },
                new { Etat = "2", Traitement = "R", TypeRejet = type, MotifRejet = motif, DemandeValidee = demandeValidee }));
    }
}
