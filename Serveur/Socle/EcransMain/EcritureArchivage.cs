using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Archivage / radiation » (PROC_ARCHIRAD, écran fait main web/src/ecrans/EcranArchivage.tsx) : le geste PRÉPARER LE BORDEREAU de RIG v2,
/// « Un seul bordereau de numérisation par dossier » -- le choix par défaut d'OPE_ARCHRTOT (Operations\OPE_ARCHRTOT.cs:39-41). Joué par la
/// route POST RoutesArchivage.Bordereau (ModuleArchivage.cs) dans la transaction et le journal des gestes du socle (GesteFaitMain), sur les
/// dossiers de la recherche de l'écran (LectureArchivage.Requete). Lot 11 du plan « RIG v3 complet, mesuré par le banc du parcours
/// complet » (28/11/2026).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ARCHIRAD.
///   - LES CONTRÔLES : les critères obligatoires (LectureArchivage.CriteresManquants) ; VALIDER n'est permis que pour 1 à 149 dossiers
///     (« nbTotal > 0 &amp; nbTotal &lt; 150 », Operations\OPE_ARCHRSEL.cs:110, :137).
///   - LA DEMANDE DU PROCESSUS, validée, nommée « Editions de bordereaux. Sélection du &lt;date mini&gt; au &lt;date maxi&gt; »
///     (PROC_ARCHIRAD.DoValider, PROC_ARCHIRAD.cs:59).
///   - PAR DOSSIER, dans l'ordre du traitement (traitementBordereauUnique, :155-217) : le bordereau est à éditer si une formalité
///     (action portant une inscription, de la plus récente à la plus ancienne) n'a pas de passage CFE, ou un passage sans liasse
///     VIDIGED (PSSGC_VIDI_GUID_LIASSE vide), ou si l'immatriculation est d'une année antérieure à la plus ancienne formalité (sans
///     formalité : l'année du jour) (:171-186). Alors, sans annexe « Historique des FORMALITES » (:188-194 ; la recherche les a déjà
///     écartées) : l'annexe GED sur la PREMIÈRE action du dossier (ACRGA_IS_DEMAT faux, l'agent, maintenant, :195-203), et la demande du
///     dossier (Demande.CreateDemande(désignation, n° de gestion, ARCHIRAD), validée, liée à l'action et au dossier, :205-210) avec sa
///     ligne d'édition BGEDANNEX_1_IMP, bac NORMAL, un exemplaire, sceau « aucun » (:206 ; LignesEditionV2), imprimée en lot sous la
///     demande du processus (ImpressionImmediateEnBatch, :211 : la demande mère).
///   - Un dossier SANS action : RIG v2 lève sur ActionsRcs[0] (:184), l'erreur est seulement journalisée et les dossiers suivants ne
///     sont pas traités (:214-216) ; la V3 le refuse en le nommant, rien n'est écrit.
/// Le dossier doté de son annexe sort de la recherche (LectureArchivage, SansHistorique) : c'est ce que l'écran relit.
/// NON REPRIS (nommés) : le mode « Un bordereau de numérisation par formalité » (traitementBordereauMultiple, :72-153), que l'écran V3 ne
/// propose pas ; le choix de l'imprimante (OPE_ARCHRTOT.choixImprimante : la ligne attend la sienne au tableau des éditions).
/// TRACE : famille « dossier », « a cree », une par dossier.
/// </summary>
public static class EcritureArchivage
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Archivage / radiation";

    /// <summary>Le code processus de RIG v2 (PROCESSUS_ET_FONCTIONALITE de RIG_DEV : ARCHIRAD).</summary>
    public const string CodeProcessus = "ARCHIRAD";

    /// <summary>Le libellé de l'annexe GED (PROC_ARCHIRAD.cs:198).</summary>
    public const string Annexe = "Historique des FORMALITES";

    /// <summary>Le modèle du bordereau d'annexe (PROC_ARCHIRAD.cs:206).</summary>
    public const string Bordereau = "BGEDANNEX_1_IMP";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Agir];

    private sealed record Formalite(int IdAction, DateTime? DateFormalite, bool AvecPassage, bool Numerisee);

    /// <summary>Le bordereau des dossiers de la recherche du geste (Valeurs : dates, type, ou liste).</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string V(string cle) => geste.Demande.Valeurs.TryGetValue(cle, out var v) ? v?.Trim() ?? "" : "";
        DateOnly? Jour(string texte) => DateOnly.TryParseExact(texte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var recherche = new RechercheArchivage(Jour(V(RoutesArchivage.ChampDu)), Jour(V(RoutesArchivage.ChampAu)), V(RoutesArchivage.ChampType),
                                               V(RoutesArchivage.ChampListe));
        // Toute la recherche (le plafond du bordereau + 1 : au-delà, la V2 grise VALIDER).
        var (sql, parametres, motif) = LectureArchivage.Requete(recherche, RoutesArchivage.MaxBordereau + 1);
        if (motif is not null) return IssueGeste.Refuser(motif);
        DossiersArchivageLus lus;
        await using (var r = await geste.LireAsync(sql, ct, parametres)) lus = await LectureArchivage.LignesAsync(r, ct);
        if (lus.Total is 0 or >= RoutesArchivage.MaxBordereau)
            return IssueGeste.Refuser($"{lus.Total} dossier(s) à archiver : RIG v2 ne prépare le bordereau que pour 1 à {RoutesArchivage.MaxBordereau - 1} " +
                                      "dossiers (VALIDER reste grisé). Rien n'est enregistré.");

        var (idProcessus, _, refusProcessus) = await EditionsV2.DemandeProcessusValideeAsync(geste, CodeProcessus,
            $"Editions de bordereaux. Sélection du {recherche.Du:dd/MM/yyyy} au {recherche.Au:dd/MM/yyyy}", ct);
        if (refusProcessus is not null) return IssueGeste.Refuser($"{refusProcessus} Rien n'est enregistré.");

        var nomAgent = geste.Agent.NomAffiche.Length > 50 ? geste.Agent.NomAffiche[..50] : geste.Agent.NomAffiche;
        var traces = new List<TraceGeste>();
        var faits = new List<DossierArchivageLu>();
        foreach (var dossier in lus.Lignes)
        {
            DateTime immatriculation;
            await using (var r = await geste.LireAsync("SELECT DateImmatriculation FROM rec.DossierRcs WHERE Id = @d", ct, new SqlParameter("@d", dossier.Id)))
            {
                await r.ReadAsync(ct);
                immatriculation = r.GetDateTime(0);
            }
            var actions = new List<int>();
            var formalites = new List<Formalite>();
            await using (var r = await geste.LireAsync("""
                SELECT a.Id, i.INSRC_DATE_FORMALITE, CONVERT(bit, CASE WHEN p.PSSGC_ID_PSSCF IS NULL THEN 0 ELSE 1 END) AS AvecPassage,
                       CONVERT(bit, CASE WHEN p.PSSGC_VIDI_GUID_LIASSE IS NULL THEN 0 ELSE 1 END) AS Numerisee,
                       CONVERT(bit, CASE WHEN i.INSRC_ID_INSRC IS NULL THEN 0 ELSE 1 END) AS AvecInscription
                FROM rec.ActionRegistre AS a
                LEFT JOIN v2.INSCRIPTION_RCS AS i ON i.INSRC_ID_INSRC = a.IdInscriptionRegistre
                LEFT JOIN v2.PASSAGE_CFE AS p ON p.PSSGC_ID_PSSCF = a.IdPassageCfe
                WHERE a.IdDossier = @d ORDER BY a.Id
                """, ct, new SqlParameter("@d", dossier.Id)))
                while (await r.ReadAsync(ct))
                {
                    actions.Add(r.GetInt32(0));
                    if (r["AvecInscription"] is true)
                        formalites.Add(new Formalite(r.GetInt32(0), FormeLigne.DateDe(r, "INSRC_DATE_FORMALITE"), r["AvecPassage"] is true, r["Numerisee"] is true));
                }

            // :171-186 -- les formalités de la plus récente à la plus ancienne ; la dernière lue donne la plus ancienne date.
            var editer = false;
            var plusAncienne = geste.Maintenant;
            foreach (var f in formalites.OrderByDescending(f => f.DateFormalite))
            {
                if (!f.AvecPassage || !f.Numerisee) editer = true;
                plusAncienne = f.DateFormalite ?? plusAncienne;
            }
            if (immatriculation.Year < plusAncienne.Year)
            {
                if (actions.Count == 0)
                    return IssueGeste.Refuser($"Le dossier {dossier.Numero} n'a aucune action au registre : RIG v2 s'arrête sur lui sans bordereau " +
                                              "(PROC_ARCHIRAD.cs:184, ActionsRcs[0]). Rien n'est enregistré.");
                editer = true;
            }
            if (!editer) continue;

            var premiere = actions[0];
            var annexe = await geste.InsererAsync("v2.ACTION_RCS_GED_ANNEXES", "ACRGA_ID_ACRGA", new Dictionary<string, object?>
            {
                ["ACRGA_ID_ACTRC"] = premiere, ["ACRGA_IS_DEMAT"] = false, ["ACRGA_LIBELLE_PIECE"] = Annexe,
                ["ACRGA_NOM_UTILISATEUR_CREATION"] = nomAgent, ["ACRGA_DATE_CREATION"] = geste.Maintenant,
            }, ct);
            var (idDemande, numero, refus) = await EditionsV2.DemandeProcessusValideeAsync(geste, CodeProcessus, dossier.Designation, ct);
            if (refus is not null) return IssueGeste.Refuser($"{refus} Rien n'est enregistré.");
            await geste.EcrireAsync("""
                UPDATE v2.DEMANDE SET DMND_NUM_INDICATIF = LEFT(@n, 14), DMND_ID_ACTRC = @a, DMND_ID_DSRCS = @d WHERE DMND_ID_DMND = @id
                """, ct, new SqlParameter("@n", dossier.Numero), new SqlParameter("@a", premiere), new SqlParameter("@d", dossier.Id),
                new SqlParameter("@id", idDemande));
            await EditionsV2.LierAsync(geste, idDemande, idProcessus, [], ct);
            var ligne = await LignesEditionV2.AjouterAsync(geste, idDemande, Bordereau, 1, null, LignesEditionV2.SansSceau, ct);
            faits.Add(dossier);
            traces.Add(new TraceGeste("dossier", $"le dossier {dossier.Numero}", EditionsV2.Texte(dossier.Id), "a cree", null, new
            {
                Dossier = dossier.Numero, Annexe = Annexe, IdAnnexe = annexe, Demande = numero, Edition = Bordereau, IdLigneEdition = ligne,
            }));
        }
        if (faits.Count == 0)
            return IssueGeste.Refuser($"Aucun bordereau à préparer : les formalités des {lus.Total} dossier(s) sont déjà numérisées " +
                                      "(PROC_ARCHIRAD.cs:171-186). Rien n'est enregistré.");
        return IssueGeste.Ecrit($"Bordereau préparé pour {faits.Count} dossier(s) ({string.Join(", ", faits.Select(d => d.Numero))}) : annexe " +
                                $"« {Annexe} » et bordereau {Bordereau} à imprimer.",
            [.. faits.Select(d => EditionsV2.Texte(d.Id))], [.. traces]);
    }
}
