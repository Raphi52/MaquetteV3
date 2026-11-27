using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Retours CFE-NET » (PROC_CFENET_RET) : les deux gestes du menu de la ligne de la V2, « Correction greffe » et « Correction Amitel »,
/// sur le retour coche. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurRetoursCfenet.cs. Plan « RIG v3 100 % fonctionnel », lot 15,
/// 27/11/2026. Rien ne part hors du poste : le suivi est ecrit dans le retour, dans la base du greffe.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_CFENET_RET\PROC_CFENET_RET\RESULTAT\OPE_RESULTAT.cs
/// (proc_cfenet_ret.md, Detail OPE « OPE_RESULTAT ») :
///   - le menu s'ouvre sur UNE ligne selectionnee (_OpenContextMenu, l. 136-195) ;
///   - « Correction greffe » (cmsCorrectionGreffe_Click, l. 197-208) est une BASCULE : retour pas encore corrige -> UPDATE CFE_LIASSE_XML
///     SET LIAXML_CFENETRET_COR_GREF_ID_UTLST = l'agent, LIAXML_CFENETRET_COR_GREF_DATE = maintenant ; deja corrige -> les deux a NULL.
///     Un retour corrige sort de la liste (corrections du greffe masquees a l'ouverture) ;
///   - « Correction Amitel » (tmsiCorrectionAmitel_Click, l. 210-245, Bug 16678) : l'entree n'est visible que d'un utilisateur Amitel
///     (l. 184-192) ; pas encore traite -> un commentaire NON VIDE est exige (SaisieBox, AllowEmptyValue = false, l. 218), puis
///     _TRAITE_AMI_ID_UTLST, _TRAITE_AMI_DATE = maintenant, _TRAITE_AMI_COM = le commentaire ; deja traite -> les trois a NULL.
///   La V2 n'agit que sur les lignes de sa grille : le retour doit etre du domaine de la liste (etat renseigne, passage direct CCI).
/// L'agent : son identifiant V2 (soc.Compte.IdV2, repris de UTILISATEUR, SOC-002), celui que la grille relit pour nommer le correcteur
/// (LEFT JOIN UTILISATEUR, PROC_CFENET_RET.cs:66) ; un compte ne en V3 n'en a pas : la colonne reste vide, le journal des gestes le nomme.
/// NON REPRIS (nommes) : « Envoyer vers VGED » (ouvrir un autre ecran, l. 106-135) et l'export Excel : ni l'un ni l'autre n'ecrit ; la
/// V2 n'ecrit rien d'autre (ni demande, ni edition).
/// TRACE : famille « dossier » (le dossier RCS du retour), « a modifie », la correction avant et apres.
/// </summary>
internal sealed class EcrivainRetoursCfenet : IEcrivainEcran
{
    public string Procedure => "PROC_CFENET_RET";
    public string Ecran => "Retours CFE-NET";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes et la saisie du descripteur, au mot pres (le menu de la ligne de la V2).</summary>
    internal const string CorrectionGreffe = "Correction greffe", CorrectionAmitel = "Correction Amitel", Commentaire = "Commentaire Amitel";

    internal const string ReserveAmitel = "La correction Amitel est réservée à l'équipe Amitel.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        if (nom is not (CorrectionGreffe or CorrectionAmitel)) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length != 1) return IssueGeste.Refuser("Cochez un seul retour : RIG v2 ouvre ce menu sur une seule ligne.");
        if (nom == CorrectionAmitel && !geste.Agent.EstAmitel) return IssueGeste.Refuser(ReserveAmitel);
        if (!int.TryParse(ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser($"Retour CFE-NET introuvable dans la base du greffe ({ids[0]}).");

        DateTime? correctionGreffe, correctionAmitel;
        string numero;
        int idDossier;
        await using (var r = await geste.LireAsync($"""
                         SELECT x.LIAXML_CFENETRET_COR_GREF_DATE, x.LIAXML_CFENETRET_TRAITE_AMI_DATE, d.NumeroGestion, d.Id AS IdDossier
                         {LecteurRetoursCfenet.Domaine} AND x.LIAXML_ID_LIAXML = @id
                         """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"Retour CFE-NET introuvable dans la base du greffe ({id}).");
            correctionGreffe = FormeLigne.DateDe(r, "LIAXML_CFENETRET_COR_GREF_DATE");
            correctionAmitel = FormeLigne.DateDe(r, "LIAXML_CFENETRET_TRAITE_AMI_DATE");
            numero = FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion"));
            idDossier = r.GetInt32(r.GetOrdinal("IdDossier"));
        }
        int? idV2;
        await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct, new SqlParameter("@c", geste.Agent.CompteWindows)))
            idV2 = await r.ReadAsync(ct) && r[0] is int v ? v : null;

        var objet = $"le retour CFE-NET {id} du dossier {numero}";
        var dossier = idDossier.ToString(CultureInfo.InvariantCulture);
        if (nom == CorrectionGreffe)
        {
            var poser = correctionGreffe is null;
            await geste.EcrireAsync(poser
                ? "UPDATE v2.CFE_LIASSE_XML SET LIAXML_CFENETRET_COR_GREF_ID_UTLST = @agent, LIAXML_CFENETRET_COR_GREF_DATE = @maintenant WHERE LIAXML_ID_LIAXML = @id"
                : "UPDATE v2.CFE_LIASSE_XML SET LIAXML_CFENETRET_COR_GREF_ID_UTLST = NULL, LIAXML_CFENETRET_COR_GREF_DATE = NULL WHERE LIAXML_ID_LIAXML = @id",
                ct, new SqlParameter("@agent", (object?)idV2 ?? DBNull.Value), new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", id));
            return IssueGeste.Ecrit(poser ? $"Retour {numero} marqué corrigé par le greffe : il quitte la liste des retours à traiter."
                                          : $"Correction du greffe retirée sur le retour {numero}.", [ids[0]],
                new TraceGeste("dossier", objet, dossier, "a modifie",
                    new Dictionary<string, string> { ["Correction greffe"] = poser ? "non" : "oui" },
                    new Dictionary<string, string> { ["Correction greffe"] = poser ? "oui" : "non" }));
        }
        var traiter = correctionAmitel is null;
        var commentaire = (geste.Demande.Valeurs.TryGetValue(Commentaire, out var c) ? c : "").Trim();
        if (traiter && commentaire.Length == 0) return IssueGeste.Refuser($"« {Commentaire} » est obligatoire.");
        if (commentaire.Length > 500) return IssueGeste.Refuser($"« {Commentaire} » : 500 caractères au plus.");
        await geste.EcrireAsync(traiter
            ? "UPDATE v2.CFE_LIASSE_XML SET LIAXML_CFENETRET_TRAITE_AMI_ID_UTLST = @agent, LIAXML_CFENETRET_TRAITE_AMI_DATE = @maintenant, LIAXML_CFENETRET_TRAITE_AMI_COM = @com WHERE LIAXML_ID_LIAXML = @id"
            : "UPDATE v2.CFE_LIASSE_XML SET LIAXML_CFENETRET_TRAITE_AMI_ID_UTLST = NULL, LIAXML_CFENETRET_TRAITE_AMI_DATE = NULL, LIAXML_CFENETRET_TRAITE_AMI_COM = NULL WHERE LIAXML_ID_LIAXML = @id",
            ct, new SqlParameter("@agent", (object?)idV2 ?? DBNull.Value), new SqlParameter("@maintenant", geste.Maintenant),
            new SqlParameter("@com", commentaire), new SqlParameter("@id", id));
        return IssueGeste.Ecrit(traiter ? $"Retour {numero} marqué traité par Amitel : il quitte la liste des retours à traiter."
                                        : $"Traitement Amitel retiré sur le retour {numero}.", [ids[0]],
            new TraceGeste("dossier", objet, dossier, "a modifie",
                new Dictionary<string, string> { ["Correction Amitel"] = traiter ? "non" : "oui" },
                new Dictionary<string, string> { ["Correction Amitel"] = traiter ? $"oui : {commentaire}" : "non" }));
    }
}
