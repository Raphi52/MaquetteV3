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
///   - « Cloturer le groupe » (FIGER, _cloturer, :413+, TraitementEdition.Cloturer, TraitementEdition.cs:224-254).
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
/// editrcs » (un programme externe : refuse, jamais lance) ; « Cloturer le groupe » (refuse : la cloture, ses comptes et
/// l'ouverture du groupe suivant de la file, _CreateEditionOuverte, ne sont pas encore ecrits par la V3).
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
        if (nom == Cloturer)
            return IssueGeste.Refuser("« Cloturer le groupe » n'est pas encore repris par la V3 : rien n'est écrit.");
        if (nom != ToutReimprimer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun groupe coché : rien n'est réimprimé.");

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
}
