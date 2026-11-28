using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « GED judiciaire » (PROC_GEDJUD) : le geste MODIFIER UN DOCUMENT -- son etat, son motif de rejet et son commentaire --, ecrit dans
/// la base du greffe. Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 8, 28/11/2026. Route : ModuleGedJud.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_GEDJUD\PROC_GED_MODIF\ETP_GED_MODIF) :
///   - l'etat : liste de VIDIGED_PARAMETRAGE de type « ETAT » (ULT_COMBO_ETAT.cs:17-25), obligatoire (OPE_GED_MODIF_ETAT.Designer.cs:80) ;
///   - le motif de rejet : obligatoire quand l'etat est « R », vide sinon (OPE_GED_MODIF_ETAT.cs, ult_cbb_etat2_ChangementValeur :
///     ResetText du motif lie a VGIDX_MOTIF_REJET) ; varchar(1000) dans RIG_DEV ;
///   - le commentaire (operation « Commentaire », champ « Libellé », lie a VGIDX_COMMENTAIRE, OPE_GED_MODIF_COMMENTAIRE.cs:22) ;
///   - l'enregistrement (ETP_GED_MODIF.DoSauveAfterOperation, ETP_GED_MODIF.cs:493-510, :579) : chaque champ change ajoute son mot a
///     « Modification(s) - Commentaire - Changement d'état - Motif rejet », puis l'historique VIDIGED_HISTO recoit le GUID du
///     document, le login de l'agent, l'instant et ce texte (VidigedHisto.CreateVidigedHisto, :582-586).
/// NON REPRIS, nomme : le libelle reconstruit a chaque enregistrement (numero d'entite - nature - deposant - date, :465-473 : la V3
/// garde le libelle) ; le changement de liaison, de nature, de date ou de deposant (autres operations de l'etape) ; le nom et
/// l'indicatif de la demande du processus (PROC_GEDJUD.SetDemande, :588-592) ; l'ajout d'un document (mode ajout : fichier archive
/// dans Vidiged, service externe). Un geste qui ne change rien est refuse (message de la V3) : RIG v2 n'ecrirait aucune trace.
/// </summary>
internal static class EcritureGedJud
{
    /// <summary>Le nom de l'ecran au journal des gestes.</summary>
    public const string Ecran = "GED judiciaire";

    /// <summary>La longueur du motif de rejet et du commentaire (varchar(1000) de VIDIGED_INDEX dans RIG_DEV).</summary>
    public const int Longueur = 1000;

    /// <summary>Les gestes ecrits : tout autre type est refuse par la route (GesteFaitMain, rien d'ecrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Enregistrer];

    /// <summary>Les etats de la liste (ULT_COMBO_ETAT.cs:17-26 : VIDIGED_PARAMETRAGE de type ETAT), LECTURE SEULE ; par code (RIG v2 n'en
    /// donne pas l'ordre).</summary>
    public static async Task<EtatGedLu[]> EtatsAsync(SqlConnection baseGreffe, CancellationToken ct)
    {
        var etats = new List<EtatGedLu>();
        await using var r = await LectureV2.LireAsync(baseGreffe,
            "SELECT VGPRM_CODE, ISNULL(VGPRM_LIBELLE, '') AS VGPRM_LIBELLE FROM v2.VIDIGED_PARAMETRAGE WHERE VGPRM_TYPE = 'ETAT' ORDER BY VGPRM_CODE", ct);
        while (await r.ReadAsync(ct)) etats.Add(new EtatGedLu(FormeLigne.Texte(r, "VGPRM_CODE").Trim(), FormeLigne.Texte(r, "VGPRM_LIBELLE").Trim()));
        return [.. etats];
    }

    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Ids is not [var texte] || !int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser("Choisissez un document de la liste.");
        string etatAvant, motifAvant, commentaireAvant, libelle;
        Guid? guid;
        await using (var r = await geste.LireAsync("""
            SELECT VGIDX_ETAT, VGIDX_MOTIF_REJET, VGIDX_COMMENTAIRE, VGIDX_LIBELLE, VGIDX_GUID_DOCUMENT FROM v2.VIDIGED_INDEX WHERE VGIDX_ID_VGIDX = @id
            """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser("Ce document n'est plus dans la base du greffe : relisez la liste.");
            etatAvant = FormeLigne.Texte(r, "VGIDX_ETAT").Trim();
            motifAvant = FormeLigne.Texte(r, "VGIDX_MOTIF_REJET");
            commentaireAvant = FormeLigne.Texte(r, "VGIDX_COMMENTAIRE");
            libelle = FormeLigne.Texte(r, "VGIDX_LIBELLE").Trim();
            guid = r["VGIDX_GUID_DOCUMENT"] as Guid?;
        }

        var etat = (geste.Valeur(ModificationDocumentGed.SectionEtat, ModificationDocumentGed.Etat) ?? etatAvant).Trim();
        if (etat.Length == 0) return IssueGeste.Refuser(Obligatoire(ModificationDocumentGed.Etat));
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.VIDIGED_PARAMETRAGE WHERE VGPRM_TYPE = 'ETAT' AND VGPRM_CODE = @e", ct,
                         new SqlParameter("@e", etat)))
        {
            await r.ReadAsync(ct);
            if (r.GetInt32(0) == 0) return IssueGeste.Refuser($"« {etat} » n'est pas un état de la liste (paramétrage de la GED, type ETAT).");
        }
        var motif = etat == "R" ? (geste.Valeur(ModificationDocumentGed.SectionEtat, ModificationDocumentGed.MotifRejet) ?? motifAvant).Trim() : "";
        if (etat == "R" && motif.Length == 0) return IssueGeste.Refuser(Obligatoire(ModificationDocumentGed.MotifRejet));
        var commentaire = geste.Valeur(ModificationDocumentGed.SectionCommentaire, ModificationDocumentGed.Commentaire) ?? commentaireAvant;
        if (motif.Length > Longueur) return IssueGeste.Refuser($"« {ModificationDocumentGed.MotifRejet} » : {Longueur} caractères au plus.");
        if (commentaire.Length > Longueur) return IssueGeste.Refuser($"« {ModificationDocumentGed.Commentaire} » : {Longueur} caractères au plus.");

        // L'ordre et les mots de ETP_GED_MODIF.cs:493-510.
        var mots = new List<string>();
        if (commentaire != commentaireAvant) mots.Add("Commentaire");
        if (etat != etatAvant) mots.Add("Changement d'état");
        if (etat != etatAvant && etat == "R" && motif != motifAvant) mots.Add("Motif rejet");
        if (mots.Count == 0 && motif == motifAvant.Trim()) return IssueGeste.Refuser("Aucune modification à enregistrer.");
        // Seul le motif d'un document deja rejete change : RIG v2 l'enregistre (champ lie) sans mot ni historique (:497-510).
        string? action = mots.Count == 0 ? null : "Modification(s) - " + string.Join(" - ", mots);

        await geste.EcrireAsync("""
            UPDATE v2.VIDIGED_INDEX SET VGIDX_ETAT = @etat, VGIDX_MOTIF_REJET = @motif, VGIDX_COMMENTAIRE = @commentaire WHERE VGIDX_ID_VGIDX = @id
            """, ct, new SqlParameter("@etat", etat), new SqlParameter("@motif", motif.Length > 0 ? motif : DBNull.Value),
            new SqlParameter("@commentaire", commentaire.Length > 0 ? commentaire : DBNull.Value), new SqlParameter("@id", id));
        int? histo = action is null ? null : await geste.InsererAsync("v2.VIDIGED_HISTO", "VGHIS_ID_VGHIS", new Dictionary<string, object?>
        {
            ["VGHIS_GUID_DOCUMENT"] = guid, ["VGHIS_LOGIN"] = geste.Agent.CompteWindows, ["VGHIS_DATE"] = geste.Maintenant, ["VGHIS_ACTION"] = action,
        }, ct);

        var objet = libelle.Length > 0 ? $"le document « {libelle} »" : $"le document n° {id}";
        return IssueGeste.Ecrit($"Document enregistré{(action is null ? " (motif de rejet)" : $" : {action}")}.", [id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("affaire", objet, id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Etat = etatAvant, MotifRejet = motifAvant, Commentaire = commentaireAvant },
                new { Etat = etat, MotifRejet = motif, Commentaire = commentaire, Historique = histo }));
    }
}
