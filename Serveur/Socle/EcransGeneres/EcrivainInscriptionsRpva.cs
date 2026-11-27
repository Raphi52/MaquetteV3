using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPVA » (PROC_EDI_RPVAINS) : REFUSER une demande d'inscription recue par le reseau RPVA, sur la ligne cochee. Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurInscriptionsRpva.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026. Rien ne part hors du
/// poste : la demande recue est lue dans la base du greffe (DEMAT_RPJC_INS), son etat y est ecrit, comme la V2.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDI_RPVAINS.
///   - l'ouverture de la demande (FormRechercheDemandes\OPE_RESULTAT_EDI_RPVAINS.cs:74-99) refuse, au mot pres : une demande de
///     MODIFICATION (RPJCI_TYPE_MAJ = 'M', l. 77-80) ; une demande deja refusee (RPJCI_ETAT_TRAITEMENT_RIG = 'R', l. 82-85, avec son
///     motif) ; une demande qu'un agent tient (RPJCI_USER_EN_COURS renseigne, l. 89-95 : la V2 refuse alors tout le monde) ;
///   - le motif est obligatoire (FormSaisieRejet\OPE_SAISIE_REJET.Designer.cs:71, MetierObligatoire), 300 caracteres au plus (la
///     colonne RPJCI_MOTIF_TRAITEMENT_RIG de RIG_DEV) ;
///   - DataProcDemandeInscr.Refuse (FormRechercheDemandes\DataProcDemandeInscr.cs:213-229) n'agit que sur une demande a l'etat 1, et
///     ecrit RPJCI_ETAT = '2', RPJCI_DATE_TRAITEMENT_RIG = maintenant, RPJCI_ETAT_TRAITEMENT_RIG = 'R', le motif, RPJCI_USER_TRAITEMENT_RIG
///     = l'agent ; sinon « Echec du traitement. » (FormTraitement\PROC_TRAITEMENT.cs:186) ; reussi : « Demande d'inscription rejetée
///     avec succès. » (l. 173). La demande quitte la liste (etat 2).
/// NON REPRIS (nommes) : « Traiter » (FormTraitement\PROC_TRAITEMENT.cs et FormListerMandataires\ : rattacher le numero CNBF, le SIREN et
/// le courriel aux mandataires candidats choisis un a un, remplacer leurs courriels, creer un mandataire au besoin) -- un parcours en
/// plusieurs fenetres, que la forme File de l'ecran ne porte pas : il reste a l'ecran de la V2 ; le nom de la demande du processus,
/// renomme « Demande d'inscription de X refusée » (PROC_TRAITEMENT.cs:175-178) -- une trace, que le journal des gestes remplace
/// (decision du plan).
/// TRACE : famille « reglage » (les inscriptions des avocats du greffe), « a modifie », l'etat avant et apres.
/// </summary>
internal sealed class EcrivainInscriptionsRpva : IEcrivainEcran
{
    public string Procedure => "PROC_EDI_RPVAINS";
    public string Ecran => "EDI RPVA";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste et la saisie du descripteur, au mot pres.</summary>
    internal const string Refuser = "Refuser", Motif = "Motif du refus";

    internal const string Modification = "Cette demande de modification a déjà été traitée. Vous ne pouvez pas re-traiter une demande de modification déjà traitée.";
    internal const string Echec = "Echec du traitement.";
    internal const string Reussi = "Demande d'inscription rejetée avec succès.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Refuser) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // La V2 traite une demande a la fois (double-clic sur UNE ligne, OPE_RESULTAT_EDI_RPVAINS.cs:74).
        if (ids.Length != 1) return IssueGeste.Refuser("Cochez une seule demande : RIG v2 refuse les demandes une à une.");
        var motif = (geste.Demande.Valeurs.TryGetValue(Motif, out var m) ? m : "").Trim();
        if (motif.Length == 0) return IssueGeste.Refuser($"« {Motif} » est obligatoire.");
        if (motif.Length > 300) return IssueGeste.Refuser($"« {Motif} » : 300 caractères au plus.");
        if (!int.TryParse(ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser($"Demande introuvable dans la base du greffe ({ids[0]}).");

        string etat, typeMaj, etatTraitement, motifAncien, tenuePar, nom;
        await using (var r = await geste.LireAsync("""
                         SELECT RPJCI_ETAT, RPJCI_TYPE_MAJ, RPJCI_ETAT_TRAITEMENT_RIG, RPJCI_MOTIF_TRAITEMENT_RIG, RPJCI_USER_EN_COURS,
                                LTRIM(RTRIM(CONCAT(RPJCI_NOM, ' ', RPJCI_PRENOM))) AS Nom, RPJCI_DENOMINATION
                         FROM v2.DEMAT_RPJC_INS WITH (UPDLOCK) WHERE RPJCI_ID_RPJCI = @id
                         """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Demande introuvable dans la base du greffe ({id}).");
            etat = FormeLigne.Texte(r, "RPJCI_ETAT");
            typeMaj = FormeLigne.Texte(r, "RPJCI_TYPE_MAJ");
            etatTraitement = FormeLigne.Texte(r, "RPJCI_ETAT_TRAITEMENT_RIG");
            motifAncien = FormeLigne.Texte(r, "RPJCI_MOTIF_TRAITEMENT_RIG");
            tenuePar = FormeLigne.Texte(r, "RPJCI_USER_EN_COURS");
            nom = FormeLigne.Texte(r, "Nom") is { Length: > 0 } n ? n : FormeLigne.Texte(r, "RPJCI_DENOMINATION");
        }
        // Les refus de l'ouverture (OPE_RESULTAT_EDI_RPVAINS.cs:77-95), dans l'ordre de la V2 ; leurs « \n » en blancs.
        if (typeMaj == "M") return IssueGeste.Refuser(Modification);
        if (etatTraitement == "R")
            return IssueGeste.Refuser($"Vous ne pouvez pas traiter une demande d'inscription déjà refusée. Cette demande d'inscription a été refusée pour le motif suivant : {motifAncien}");
        if (tenuePar.Length > 0) return IssueGeste.Refuser($"Cette demande d'inscription est en cours de traitement par l'utilisateur {tenuePar}");
        // Refuse n'agit que sur l'etat 1 (DataProcDemandeInscr.cs:216), sinon « Echec du traitement. ».
        if (etat != "1") return IssueGeste.Refuser(Echec);

        var compte = geste.Agent.CompteWindows.Length > 255 ? geste.Agent.CompteWindows[..255] : geste.Agent.CompteWindows;
        await geste.EcrireAsync("""
            UPDATE v2.DEMAT_RPJC_INS SET RPJCI_ETAT = '2', RPJCI_DATE_TRAITEMENT_RIG = @maintenant, RPJCI_ETAT_TRAITEMENT_RIG = 'R',
                RPJCI_MOTIF_TRAITEMENT_RIG = @motif, RPJCI_USER_TRAITEMENT_RIG = @agent
            WHERE RPJCI_ID_RPJCI = @id
            """, ct, new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@motif", motif), new SqlParameter("@agent", compte),
            new SqlParameter("@id", id));
        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit(Reussi, [idTexte],
            new TraceGeste("reglage", $"la demande d'inscription RPVA n° {idTexte} ({nom})", idTexte, "a modifie",
                new Dictionary<string, string> { ["Etat"] = "1 (en attente)" },
                new Dictionary<string, string> { ["Etat"] = "2 (refusée)", ["Motif"] = motif }));
    }
}
