using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relancer un fichier » (PROC_RELANCEFILE) : les gestes de RIG v2 sur un groupe (traitement d'edition) d'une file d'edition.
/// « Tout réimprimer » RELANCE l'etat des editions du groupe dans la base ; rien ne sort du poste. Mode d'emploi : EcrivainEcran.cs ;
/// la liste et ses regles : LecteurTraitementsEdition.cs. Plan « RIG v3 100 % fonctionnel », lot 9, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_RELANCEFILE\ETP_EDITGRP_\OPE_EDITGRP.cs (proc_relancefile.md, Detail OPE) :
///   - « Tout réimprimer » (bouton IMPRIMER_AUTRE, actif sur un groupe CLOTURE seulement, _enabledButtons :205 ; _basculerEtatP(true),
///     :254-355) : pour chaque edition du groupe (Groupe.SelectLigneEdition(false, false) -- DEMANDE x LIGNE_EDITION x
///     INFO_DESTINATAIRE du groupe, sans autre filtre, TraitementEdition.cs:384-393) que RIG v2 imprime (LGEDT_IMPRIMER, :295-297) :
///     SetEstNonImprime (LGEDT_ETAT_EDT = 'I', LGEDT_ETAT = '0', LGEDT_DATE_EDT vide, LigneEdition.cs:1294-1298), LGEDT_DIFFERE = 0
///     et LGEDT_DESTINATION_PHYSIQUE = l'imprimante du groupe (TRTED_IMPRIMANTE1), :319-321 ;
///   - « Relancer via editrcs » (RELANCER, _relancer, :237-240) lance le programme externe editrcs, sans parametre ;
///   - « Cloturer le groupe » (FIGER, actif sur un groupe OUVERT, :202 ; _cloturer, :413-431) : refus « Ce groupe est déjà
///     clôturé. » (:418-421) et « Il faut choisir une imprimante avant de clôturer le groupe. » (:422-425 ; la liste est liee a
///     l'imprimante du groupe, TRTED_IMPRIMANTE1, :120 -- la V3 n'a pas de liste, elle prend celle du groupe) ; puis
///     TraitementEdition.Cloturer (TraitementEdition.cs:224-254) : un groupe sans destinataire (GetNombreEdition : ses
///     INFO_DESTINATAIRE, :275-283) n'est PAS clos -- RIG v2 sort sans rien dire, la V3 le dit et n'ecrit rien ; sinon
///     TRTED_FIN_TRANSMISSION = 1 si aucun destinataire n'attend sa transmission (INFDS_LOT_TRANSMIS = 0, :285-287),
///     TRTED_CLOTURE = 1, TRTED_DATE_CLOTURE = maintenant si vide ; et si la file n'a plus de groupe ouvert (TRTED_CLOTURE = 0,
///     _GetTraitementEditionOuverte :190-195), le groupe suivant s'ouvre (_CreateEditionOuverte, :196-222) : ouvert maintenant,
///     ni en cours, ni transmis, ni imprime, l'impression automatique et l'imprimante de la FILE (FLEDT_IMPRESSION_AUTO,
///     FLEDT_HEURE_IMPRESSION_AUTO -- demain a cette heure, ou dans 24 h sans heure --, FLEDT_IMPRIMANTE1), et la file le
///     designe (FLEDT_ID_TRTED). Tout dans la transaction du geste, comme la transaction « clotureTraitementEdition ».
/// CONTROLES, avant toute ecriture (RIG v2 grise ses boutons, sans message) :
///   1. un geste connu, au moins un groupe coche ;
///   2. chaque groupe existe, dans une file que l'agent voit (pas une file Amitel, ETP_LISTVIEW_\OPE_RECHERCHELOT.cs:38) ;
///   3. Tout reimprimer : groupe clos (IsCloture, TraitementEdition.cs:487-490 : date de cloture ET TRTED_CLOTURE) -- un seul
///      groupe hors regle et rien n'est ecrit, pas meme pour les autres ;
///   4. au moins une edition du groupe que RIG v2 imprime (LGEDT_IMPRIMER) : sans elle, RIG v2 ne change aucune ligne (sa
///      boucle les saute toutes, :295-297) et la V3 n'aurait rien a ecrire -- elle le dit au lieu d'annoncer une reimpression.
/// Trace : famille « demande » (les editions sont celles des demandes), « a modifie », editions non imprimees avant / apres.
///
/// NON REPRIS (nomme) : la generation des documents (GenererDocumentEnBatch, :298-317), le controle de leur presence sur le disque
/// et l'envoi a l'impression (Demande.PrintDemandeReprise / PrintFacture, :332-336) -- rien ne part du poste, l'etat V2 « non
/// imprimee » ecrit est ce que la chaine d'impression relit ; les deux questions de RIG v2 (« plus de 100 documents, etes-vous
/// sur ? », :259-264, et « forcer l'impression et ignorer les erreurs de generation ? », :280-284) : la V3 ne pose pas de
/// question, fait le « Oui » de la premiere, et la seconde porte sur une generation qu'elle ne fait pas ; « Relancer via
/// editrcs » (un programme externe : refuse, jamais lance) ; pour « Cloturer le groupe », la presence de l'imprimante parmi
/// celles du poste (ValeurExiste de la liste de RIG v2) : la V3 n'en connait pas la liste, elle exige seulement un nom.
/// </summary>
internal sealed class EcrivainTraitementsEdition : IEcrivainEcran
{
    public string Procedure => "PROC_RELANCEFILE";
    public string Ecran => "Relancer un fichier";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur (Surveiller.cs), au mot pres (les boutons de OPE_EDITGRP).</summary>
    internal const string Relancer = "Relancer via editrcs", Cloturer = "Cloturer le groupe", ToutReimprimer = "Tout réimprimer";

    private const string Groupe = """
        SELECT t.TRTED_ID_TRTED, ISNULL(NULLIF(LTRIM(RTRIM(f.FLEDT_NOM_RIG)), ''), f.FLEDT_LIBELLE) AS NomFile, t.TRTED_IMPRIMANTE1,
               CASE WHEN t.TRTED_DATE_CLOTURE IS NOT NULL AND t.TRTED_CLOTURE = 1 THEN 1 ELSE 0 END AS Clos
        FROM v2.TRAITEMENT_EDITION AS t JOIN v2.FILE_EDITION AS f ON f.FLEDT_ID_FLEDT = t.TRTED_ID_FLEDT
        WHERE t.TRTED_ID_TRTED = @t AND UPPER(ISNULL(f.FLEDT_NOM_RIG, '')) NOT LIKE 'AMI%'
        """;

    /// <summary>Les editions du groupe (SelectLigneEdition(false, false)) : demandes, editions imprimees par RIG v2, et parmi
    /// elles celles deja « non imprimees ».</summary>
    private const string Editions = """
        SELECT COUNT(DISTINCT d.DMND_ID_DMND) AS Demandes,
               ISNULL(SUM(CASE WHEN l.LGEDT_IMPRIMER = 1 THEN 1 ELSE 0 END), 0) AS AImprimer,
               ISNULL(SUM(CASE WHEN l.LGEDT_ETAT_EDT = 'I' THEN 1 ELSE 0 END), 0) AS NonImprimees
        FROM v2.DEMANDE AS d
        JOIN v2.LIGNE_EDITION AS l ON l.LGEDT_ID_DMND = d.DMND_ID_DMND
        JOIN v2.INFO_DESTINATAIRE AS i ON i.INFDS_ID_INFDS = l.LGEDT_ID_INFDS
        WHERE i.INFDS_ID_TRTED = @t
        """;

    /// <summary>SetEstNonImprime, LGEDT_DIFFERE = 0 et l'imprimante du groupe, sur les editions que RIG v2 imprime.</summary>
    private const string Remettre = """
        UPDATE v2.LIGNE_EDITION
        SET LGEDT_ETAT_EDT = 'I', LGEDT_ETAT = '0', LGEDT_DATE_EDT = NULL, LGEDT_DIFFERE = 0, LGEDT_DESTINATION_PHYSIQUE = @imprimante
        WHERE LGEDT_IMPRIMER = 1
          AND LGEDT_ID_INFDS IN (SELECT i.INFDS_ID_INFDS FROM v2.INFO_DESTINATAIRE AS i WHERE i.INFDS_ID_TRTED = @t)
          AND LGEDT_ID_DMND IN (SELECT d.DMND_ID_DMND FROM v2.DEMANDE AS d)
        """;

    private sealed record Ligne(string Id, string File, string? Imprimante, int Demandes, int NonImprimees);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        // 1. Un geste connu, une selection.
        if (nom == Relancer)
            return IssueGeste.Refuser("« Relancer via editrcs » lance le programme editrcs de RIG v2 : la V3 ne lance aucun programme externe, rien n'est relancé.");
        if (nom != ToutReimprimer && nom != Cloturer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(nom == Cloturer ? "Aucun groupe coché : rien n'est clôturé." : "Aucun groupe coché : rien n'est réimprimé.");
        if (nom == Cloturer) return await CloturerAsync(geste, ids, ct);

        // 2. a 4. Chaque groupe : visible, clos, avec au moins une edition que RIG v2 imprime.
        var lignes = new List<Ligne>();
        foreach (var id in ids)
        {
            if (!int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var numero))
                return IssueGeste.Refuser($"Groupe d'édition introuvable dans la base du greffe ({id}).");
            string? file = null, imprimante = null;
            var clos = false;
            await using (var r = await geste.LireAsync(Groupe, ct, new SqlParameter("@t", numero)))
                if (await r.ReadAsync(ct))
                {
                    file = FormeLigne.Texte(r, "NomFile");
                    imprimante = r["TRTED_IMPRIMANTE1"] as string;
                    clos = Convert.ToInt32(r["Clos"], System.Globalization.CultureInfo.InvariantCulture) == 1;
                }
            if (file is null) return IssueGeste.Refuser($"Groupe d'édition introuvable dans la base du greffe ({id}).");
            if (!clos)
                return IssueGeste.Refuser($"Le groupe {id} de la file {file} est ouvert : RIG v2 ne réimprime qu'un groupe clôturé.");
            int demandes, aImprimer, nonImprimees;
            await using (var r = await geste.LireAsync(Editions, ct, new SqlParameter("@t", numero)))
            {
                await r.ReadAsync(ct);
                demandes = Convert.ToInt32(r["Demandes"], System.Globalization.CultureInfo.InvariantCulture);
                aImprimer = Convert.ToInt32(r["AImprimer"], System.Globalization.CultureInfo.InvariantCulture);
                nonImprimees = Convert.ToInt32(r["NonImprimees"], System.Globalization.CultureInfo.InvariantCulture);
            }
            if (aImprimer == 0)
                return IssueGeste.Refuser($"Le groupe {id} de la file {file} n'a aucune édition à imprimer : rien n'est remis à imprimer.");
            lignes.Add(new Ligne(id, file, imprimante, demandes, nonImprimees));
        }

        var traces = new List<TraceGeste>();
        var remises = 0;
        foreach (var l in lignes)
        {
            var n = await geste.EcrireAsync(Remettre, ct,
                new SqlParameter("@imprimante", (object?)l.Imprimante ?? DBNull.Value), new SqlParameter("@t", int.Parse(l.Id, System.Globalization.CultureInfo.InvariantCulture)));
            remises += n;
            int apres;
            await using (var r = await geste.LireAsync(Editions, ct, new SqlParameter("@t", int.Parse(l.Id, System.Globalization.CultureInfo.InvariantCulture))))
            {
                await r.ReadAsync(ct);
                apres = Convert.ToInt32(r["NonImprimees"], System.Globalization.CultureInfo.InvariantCulture);
            }
            traces.Add(new TraceGeste("demande", $"le groupe d'édition {l.Id} de la file {l.File}", l.Id, "a modifie",
                new Dictionary<string, string> { ["Éditions non imprimées"] = l.NonImprimees.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new Dictionary<string, string>
                {
                    ["Éditions non imprimées"] = apres.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Imprimante"] = l.Imprimante ?? "",
                }));
        }

        var demandesTotal = lignes.Sum(l => l.Demandes);
        var editions = remises > 1 ? $"{remises} éditions remises" : "1 édition remise";
        var message = $"{editions} à imprimer ({(demandesTotal > 1 ? $"{demandesTotal} demandes" : "1 demande")}). "
                    + "Les documents ne sont ni générés ni envoyés à l'impression : rien ne sort du poste.";
        return IssueGeste.Ecrit(message, [.. lignes.Select(l => l.Id)], [.. traces]);
    }

    // ------------------------------------------------------------ « Cloturer le groupe » (_cloturer, TraitementEdition.Cloturer)
    // fix-ok: mesure au controle final du lot 9 (27/11/2026) -- ce geste etait refuse « pas encore repris » alors que RIG v2 l'a
    // (OPE_EDITGRP.cs:413-431) : la V3 le joue maintenant, ses deux refus au mot pres, PROC_RELANCEFILE.gestes blocs 6 a 10.

    private const string GroupeACloturer = """
        SELECT t.TRTED_ID_FLEDT, ISNULL(NULLIF(LTRIM(RTRIM(f.FLEDT_NOM_RIG)), ''), f.FLEDT_LIBELLE) AS NomFile, t.TRTED_IMPRIMANTE1,
               CASE WHEN t.TRTED_DATE_CLOTURE IS NOT NULL AND t.TRTED_CLOTURE = 1 THEN 1 ELSE 0 END AS Clos,
               (SELECT COUNT(*) FROM v2.INFO_DESTINATAIRE AS i WHERE i.INFDS_ID_TRTED = t.TRTED_ID_TRTED) AS Destinataires,
               (SELECT COUNT(*) FROM v2.INFO_DESTINATAIRE AS i WHERE i.INFDS_ID_TRTED = t.TRTED_ID_TRTED AND i.INFDS_LOT_TRANSMIS = 0) AS NonTransmis
        FROM v2.TRAITEMENT_EDITION AS t JOIN v2.FILE_EDITION AS f ON f.FLEDT_ID_FLEDT = t.TRTED_ID_FLEDT
        WHERE t.TRTED_ID_TRTED = @t AND UPPER(ISNULL(f.FLEDT_NOM_RIG, '')) NOT LIKE 'AMI%'
        """;

    // Cloturer (TraitementEdition.cs:227-233) : la fin de transmission si aucun destinataire n'attend, l'imprimante choisie.
    private const string Clore = """
        UPDATE v2.TRAITEMENT_EDITION
        SET TRTED_CLOTURE = 1, TRTED_DATE_CLOTURE = ISNULL(TRTED_DATE_CLOTURE, @maintenant), TRTED_IMPRIMANTE1 = @imprimante,
            TRTED_FIN_TRANSMISSION = CASE WHEN @nonTransmis = 0 THEN 1 ELSE TRTED_FIN_TRANSMISSION END
        WHERE TRTED_ID_TRTED = @t
        """;

    // _GetTraitementEditionOuverte (TraitementEdition.cs:190-195) : un groupe de la file encore ouvert.
    private const string Ouvert = "SELECT COUNT(*) FROM v2.TRAITEMENT_EDITION WHERE TRTED_ID_FLEDT = @f AND TRTED_CLOTURE = 0";

    private const string FileDuGroupe = """
        SELECT FLEDT_IMPRESSION_AUTO, FLEDT_HEURE_IMPRESSION_AUTO, FLEDT_IMPRIMANTE1 FROM v2.FILE_EDITION WHERE FLEDT_ID_FLEDT = @f
        """;

    private sealed record AClore(int Id, int File, string NomFile, string Imprimante, int NonTransmis);

    private async Task<IssueGeste> CloturerAsync(GesteEnCours geste, string[] ids, CancellationToken ct)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        // Les controles de _cloturer et de Cloturer, pour chaque groupe, avant toute ecriture.
        var groupes = new List<AClore>();
        foreach (var id in ids)
        {
            if (!int.TryParse(id, System.Globalization.NumberStyles.None, inv, out var numero))
                return IssueGeste.Refuser($"Groupe d'édition introuvable dans la base du greffe ({id}).");
            AClore? g = null;
            bool clos = false;
            var destinataires = 0;
            await using (var r = await geste.LireAsync(GroupeACloturer, ct, new SqlParameter("@t", numero)))
                if (await r.ReadAsync(ct))
                {
                    clos = Convert.ToInt32(r["Clos"], inv) == 1;
                    destinataires = Convert.ToInt32(r["Destinataires"], inv);
                    g = new AClore(numero, Convert.ToInt32(r["TRTED_ID_FLEDT"], inv), FormeLigne.Texte(r, "NomFile"),
                        (r["TRTED_IMPRIMANTE1"] as string ?? "").Trim(), Convert.ToInt32(r["NonTransmis"], inv));
                }
            if (g is null) return IssueGeste.Refuser($"Groupe d'édition introuvable dans la base du greffe ({id}).");
            if (clos) return IssueGeste.Refuser("Ce groupe est déjà clôturé.");                                       // :418-421
            if (g.Imprimante.Length == 0) return IssueGeste.Refuser("Il faut choisir une imprimante avant de clôturer le groupe."); // :422-425
            if (destinataires == 0)                                                                                  // Cloturer :225-226
                return IssueGeste.Refuser($"Le groupe {id} de la file {g.NomFile} n'a aucun destinataire : RIG v2 ne le clôture pas, rien n'est écrit.");
            groupes.Add(g);
        }

        var traces = new List<TraceGeste>();
        var ouverts = new List<string>();
        foreach (var g in groupes)
        {
            await geste.EcrireAsync(Clore, ct, new SqlParameter("@maintenant", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant },
                new SqlParameter("@imprimante", g.Imprimante), new SqlParameter("@nonTransmis", g.NonTransmis), new SqlParameter("@t", g.Id));
            traces.Add(new TraceGeste("demande", $"le groupe d'édition {g.Id} de la file {g.NomFile}", g.Id.ToString(inv), "a modifie",
                new Dictionary<string, string> { ["Clôturé"] = "non" },
                new Dictionary<string, string>
                {
                    ["Clôturé"] = "oui", ["Imprimante"] = g.Imprimante,
                    ["Transmission"] = g.NonTransmis == 0 ? "finie" : $"{g.NonTransmis} destinataire{(g.NonTransmis > 1 ? "s" : "")} à transmettre",
                }));

            // Le groupe suivant de la file, s'il n'en reste aucun d'ouvert (Cloturer :241-244, _CreateEditionOuverte :196-222).
            int encoreOuverts;
            await using (var r = await geste.LireAsync(Ouvert, ct, new SqlParameter("@f", g.File)))
            {
                await r.ReadAsync(ct);
                encoreOuverts = r.GetInt32(0);
            }
            if (encoreOuverts > 0) continue;
            bool auto;
            string? heure;
            string imprimanteFile;
            await using (var r = await geste.LireAsync(FileDuGroupe, ct, new SqlParameter("@f", g.File)))
            {
                await r.ReadAsync(ct);
                auto = r["FLEDT_IMPRESSION_AUTO"] is bool b && b;
                heure = r["FLEDT_HEURE_IMPRESSION_AUTO"] as string;
                imprimanteFile = r["FLEDT_IMPRIMANTE1"] as string ?? "";
            }
            DateTime? impressionAuto = null;
            if (auto)
                impressionAuto = string.IsNullOrWhiteSpace(heure) || !TimeSpan.TryParse(heure, inv, out var h)
                    ? geste.Maintenant.AddDays(1)                      // :205-206 (une heure illisible : Convert.ToDateTime leverait en V2)
                    : geste.Maintenant.Date.Add(h).AddDays(1);         // :207-211
            var neuf = await geste.InsererAsync("v2.TRAITEMENT_EDITION", "TRTED_ID_TRTED", new Dictionary<string, object?>
            {
                ["TRTED_ID_FLEDT"] = g.File, ["TRTED_CLOTURE"] = false, ["TRTED_DATE_OUVERTURE"] = geste.Maintenant,
                ["TRTED_EN_COURS"] = false, ["TRTED_FIN_TRANSMISSION"] = false, ["TRTED_IMPRESSION"] = false,
                ["TRTED_IMPRESSION_AUTO"] = auto, ["TRTED_DATE_IMPRESSION_AUTO"] = impressionAuto,
                ["TRTED_IMPRIMANTE1"] = imprimanteFile, ["TRTED_REPARTIR"] = false,
            }, ct);
            await geste.EcrireAsync("UPDATE v2.FILE_EDITION SET FLEDT_ID_TRTED = @n WHERE FLEDT_ID_FLEDT = @f", ct,
                new SqlParameter("@n", neuf), new SqlParameter("@f", g.File));
            ouverts.Add($"groupe {neuf} ouvert dans la file {g.NomFile}");
            traces.Add(new TraceGeste("demande", $"le groupe d'édition {neuf} de la file {g.NomFile}", neuf.ToString(inv), "a cree",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { ["Imprimante"] = imprimanteFile, ["Impression automatique"] = auto ? FormeLigne.Date(impressionAuto) : "non" }));
        }

        var clotures = groupes.Count > 1 ? $"{groupes.Count} groupes clôturés" : $"Groupe {groupes[0].Id} clôturé";
        var message = ouverts.Count == 0 ? $"{clotures}." : $"{clotures} ; {string.Join(", ", ouverts)}.";
        return IssueGeste.Ecrit(message + " Rien n'est imprimé ni envoyé : rien ne sort du poste.", [.. groupes.Select(g => g.Id.ToString(inv))], [.. traces]);
    }
}
