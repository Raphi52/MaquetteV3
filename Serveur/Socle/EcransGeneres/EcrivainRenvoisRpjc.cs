using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPJC renvois » (PROC_EDI_RPJC_REN) : « Accepter » ou « Rejeter » la demande de renvoi d'audience cochee. Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurRenvoisRpjc.cs (Id = la demande, RPJCR_ID_RPJCR). Plan « RIG v3 100 % fonctionnel », lot 16,
/// 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDI_RPJC_REN (fiche : proc_edi_rpjc_ren.md).
///   L'OUVERTURE (ETP_RESULTAT_RECHERCHE\OPE_RESULTAT_RECHERCHE.cs) : UNE demande a la fois, la ligne double-cliquee (l. 241-262,
///     _Traiter l. 380-390) ; refusee si elle n'est plus « en attente » (RPJCR_ETAT relu <> '1', l. 251-258) ou si un agent la tient
///     (RPJCR_USER_EN_COURS, l. 386-387). Puis Demande Renvoi\Operation\OPE_INFO_DEMANDE.cs, _Initialiser : l'avocat est le
///     mandataire ACTIF, avec abrege, au CNBF de la demande (Mandataire.GetMandataireAbregeActifByCNBF, RigMetier\JUDICIAIRE\
///     Mandataire.cs:353-361, le plus grand id) ; sans lui, la demande ne s'ouvre pas (l. 103-108). La V2 pose ensuite son nom dans
///     RPJCR_USER_EN_COURS le temps de la saisie (l. 89-91) et l'efface a l'enregistrement : la V3 n'ouvre rien, elle l'ecrit NULL.
///   LA DECISION (OPE_AUDIENCE : deux cases exclusives « Accepter » / « Rejeter », Designer l. 83, 92 ; Valider, OPE_INFO_DEMANDE.cs:456-545,
///     dans une transaction) :
///   - d'abord l'audience initiale (l. 458-464) : posterieure a maintenant, rien n'est ecrit ;
///   - « Accepter » : l'audience de renvoi choisie (AudienceCabinet.GetAudienceCabinet de l'audience selectionnee, l. 481) ; sans elle,
///     refus (l. 483-485) ; deja acceptee sur la meme audience, « Aucun changement » (l. 496-499) ; sinon RPJCR_ETAT = '2',
///     RPJCR_DATE_TRAITEMENT_RIG = maintenant, RPJCR_ETAT_TRAITEMENT_RIG = 'A', RPJCR_USER_TRAITEMENT_RIG = l'agent (son login,
///     AmiEnvironment.UserName), RPJCR_ID_AUDIENCE_CHOISIE = l'audience, RPJCR_USER_EN_COURS = NULL (l. 488-494). Rien d'autre : la
///     sauvegarde de l'appel d'affaire est en commentaire (l. 495-496). Ici l'audience se designe par son numero (AUDNC_ID_ADNC, celui
///     des ecrans d'audience), saisi dans « Audience de renvoi » ;
///   - « Rejeter » : le motif est obligatoire (ult_MotifRejet, Designer l. 101-102, MetierObligatoire « Motif rejet ») ; la date de
///     prononce l'est quand le motif l'impose (RPJCRMR_DATE_OBLIGATOIRE : OPE_AUDIENCE.cs:59-66 la montre, Designer l. 136-137
///     MetierObligatoire « Date de prononcé »), et ne s'ecrit que dans ce cas (l. 527-528). Un motif hors de la table s'accepte
///     (MetierValeurInexistanteErreur = false, Designer l. 103) : il n'impose pas de date (l. 512-513). Deja rejetee, « Aucun
///     changement » (l. 532-535) ; sinon RPJCR_ETAT = '2', date, 'R', l'agent, RPJCR_ID_AUDIENCE_CHOISIE = NULL, RPJCR_CODE_REJET_RIG =
///     le motif, la date de prononce, RPJCR_MOTIF_REJET_RIG = l'observation (« Observation rejet », Text.Trim()), RPJCR_USER_EN_COURS =
///     NULL (l. 518-531). Une observation vide s'ecrit NULL, comme le DBString de la V2 (RigTools\SQL\DBObject.cs:165-166).
///   Les messages sont ceux de la V2 au mot pres ; un saut de ligne de la V2 (« \r\n ») s'y ecrit en espace, comme EcrivainInscriptionsRpva.
/// L'ENVOI de la reponse a l'avocat : l'etat 2 (« traite par le greffe », DematEtat.cs) est ce que le traitement d'envoi de RIG v2
///   exporte vers Extelia (etats 3, 4, 8, 9) ; la V3 n'envoie rien (partie externe, simulee : le message le dit) et ecrit l'etat V2.
/// NON REPRIS (nommes) : le recapitulatif Word de la demande (_CreerRecapitulatif, l. 300-440, un document a l'ecran) ; le
///   choix de l'audience dans la grille des audiences futures de l'affaire (OPE_APP_AFFAIRE) : l'agent donne son numero.
/// Traces : famille « affaire », la demande de renvoi du role en mots, son id, « a modifie », l'etat avant et apres.
/// </summary>
internal sealed class EcrivainRenvoisRpjc : IEcrivainEcran
{
    public string Procedure => "PROC_EDI_RPJC_REN";
    public string Ecran => "EDI RPJC renvois";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes et saisies du descripteur, au mot pres (OPE_AUDIENCE.Designer.cs l. 83, 92, 102, 126, 137).</summary>
    internal const string Accepter = "Accepter", Rejeter = "Rejeter", AudienceRenvoi = "Audience de renvoi",
        MotifRejet = "Motif rejet", ObservationRejet = "Observation rejet", DatePrononce = "Date de prononcé";

    /// <summary>Les messages de RIG v2, au mot pres.</summary>
    internal const string Aucune = "Vous devez sélectionner une demande à traiter",                                  // OPE_RESULTAT_RECHERCHE.cs:382
        DejaTraitee = "Attention cette demande a déjà été traité, veuillez rafraîchir votre recherche.",          // l. 257
        EnCoursPar = "Cette demande de renvoi est en cours de traitement par l'utilisateur ",                     // l. 387
        AudienceAVenir = "La date d'audience initiale est supérieure à la date du jour. Voulez ne pouvez pas traiter cette demande pour le moment.", // OPE_INFO_DEMANDE.cs:460
        SansAudience = "Vous devez sélectionner une audience de renvoi.",                                          // l. 484
        AucunChangement = "Vous n'avez modifié ni l'état ni l'audience choisie.";                                  // l. 497, 533

    private sealed record Demande(int Id, string Etat, string? Decision, int? Audience, string Role, string Cnbf, DateTime? Initiale,
                                  string? TenuePar);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nomGeste = geste.Demande.Nom;
        if (nomGeste != Accepter && nomGeste != Rejeter) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nomGeste}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // L'ouverture : une demande, encore en attente, que personne ne tient, d'un avocat retrouve.
        if (ids.Length == 0) return IssueGeste.Refuser(Aucune);
        if (ids.Length > 1) return IssueGeste.Refuser("RIG v2 traite une demande de renvoi à la fois : cochez-en une seule.");
        if (!int.TryParse(ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || await LireAsync(geste, id, ct) is not { } d)
            return IssueGeste.Refuser($"Demande de renvoi introuvable dans la base du greffe ({ids[0]}).");
        if (d.Etat != "1") return IssueGeste.Refuser(DejaTraitee);
        if (d.TenuePar is not null) return IssueGeste.Refuser(EnCoursPar + d.TenuePar);
        if (!await AvocatActifAsync(geste, d.Cnbf, ct))
            return IssueGeste.Refuser($"Problème avec le CNBF : {d.Cnbf}. Impossible trouver de mandataire actif avec ce CNBF.");
        // Valider : l'audience initiale d'abord.
        if (d.Initiale is { } initiale && initiale > geste.Maintenant) return IssueGeste.Refuser(AudienceAVenir);

        var compte = geste.Agent.CompteWindows.Length > 255 ? geste.Agent.CompteWindows[..255] : geste.Agent.CompteWindows;
        var avant = new Dictionary<string, string?> { ["Etat"] = d.Etat, ["Decision"] = d.Decision, ["Audience choisie"] = d.Audience?.ToString(CultureInfo.InvariantCulture) };
        var objet = $"la demande de renvoi du rôle {d.Role}";
        if (nomGeste == Accepter)
        {
            var saisie = Saisi(geste, AudienceRenvoi);
            if (!int.TryParse(saisie, NumberStyles.None, CultureInfo.InvariantCulture, out var audience) || await AudienceAsync(geste, audience, ct) is not { } libelle)
                return IssueGeste.Refuser(SansAudience);
            if (d.Audience == audience && d.Decision == "A") return IssueGeste.Refuser(AucunChangement);
            await geste.EcrireAsync("""
                UPDATE v2.DEMAT_RPJC_REN SET RPJCR_ETAT = '2', RPJCR_DATE_TRAITEMENT_RIG = @maintenant, RPJCR_ETAT_TRAITEMENT_RIG = 'A',
                       RPJCR_USER_TRAITEMENT_RIG = @agent, RPJCR_ID_AUDIENCE_CHOISIE = @audience, RPJCR_USER_EN_COURS = NULL
                WHERE RPJCR_ID_RPJCR = @id
                """, ct, new SqlParameter("@maintenant", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant },
                new SqlParameter("@agent", compte), new SqlParameter("@audience", audience), new SqlParameter("@id", d.Id));
            return IssueGeste.Ecrit(
                $"Renvoi accepté pour le rôle {d.Role} : audience de renvoi {libelle}. La réponse à l'avocat part par le traitement d'envoi " +
                "de RIG v2 vers Extelia, que la V3 n'appelle pas (envoi simulé).",
                [ids[0]],
                new TraceGeste("affaire", objet, ids[0], "a modifie", avant,
                    new Dictionary<string, string?> { ["Etat"] = "2", ["Decision"] = "A", ["Audience choisie"] = audience.ToString(CultureInfo.InvariantCulture) }));
        }

        // Rejeter.
        var motif = Saisi(geste, MotifRejet);
        if (motif.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(MotifRejet));
        if (motif.Length > 100) return IssueGeste.Refuser($"« {MotifRejet} » : 100 caractères au plus (RPJCR_CODE_REJET_RIG).");
        var dateObligatoire = await DateObligatoireAsync(geste, motif, ct);
        DateTime? prononce = null;
        if (dateObligatoire)
        {
            prononce = CreationV2.Date(Saisi(geste, DatePrononce));
            if (prononce is null) return IssueGeste.Refuser(CreationV2.Obligatoire(DatePrononce));
        }
        if (d.Decision == "R") return IssueGeste.Refuser(AucunChangement);
        var observation = Saisi(geste, ObservationRejet);
        await geste.EcrireAsync("""
            UPDATE v2.DEMAT_RPJC_REN SET RPJCR_ETAT = '2', RPJCR_DATE_TRAITEMENT_RIG = @maintenant, RPJCR_ETAT_TRAITEMENT_RIG = 'R',
                   RPJCR_USER_TRAITEMENT_RIG = @agent, RPJCR_ID_AUDIENCE_CHOISIE = NULL, RPJCR_CODE_REJET_RIG = @motif,
                   RPJCR_DATE_PRONONCE_REJET_RIG = CASE WHEN @avecDate = 1 THEN @prononce ELSE RPJCR_DATE_PRONONCE_REJET_RIG END,
                   RPJCR_MOTIF_REJET_RIG = @observation, RPJCR_USER_EN_COURS = NULL
            WHERE RPJCR_ID_RPJCR = @id
            """, ct, new SqlParameter("@maintenant", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant },
            new SqlParameter("@agent", compte), new SqlParameter("@motif", motif), new SqlParameter("@avecDate", dateObligatoire),
            new SqlParameter("@prononce", System.Data.SqlDbType.DateTime) { Value = (object?)prononce ?? DBNull.Value },
            new SqlParameter("@observation", observation.Length == 0 ? DBNull.Value : (object)observation), new SqlParameter("@id", d.Id));
        return IssueGeste.Ecrit(
            $"Renvoi rejeté pour le rôle {d.Role} (motif {motif}). La réponse à l'avocat part par le traitement d'envoi de RIG v2 vers " +
            "Extelia, que la V3 n'appelle pas (envoi simulé).",
            [ids[0]],
            new TraceGeste("affaire", objet, ids[0], "a modifie", avant,
                new Dictionary<string, string?> { ["Etat"] = "2", ["Decision"] = "R", ["Motif"] = motif, ["Observation"] = observation,
                    ["Date de prononcé"] = prononce?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) }));
    }

    private static string Saisi(GesteEnCours geste, string libelle) => geste.Demande.Valeurs.TryGetValue(libelle, out var v) ? v.Trim() : "";

    /// <summary>La demande <paramref name="id"/>, verrouillee pour le geste ; null si elle n'est pas dans la base.</summary>
    private static async Task<Demande?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT RPJCR_ID_RPJCR, RPJCR_ETAT, RPJCR_ETAT_TRAITEMENT_RIG, RPJCR_ID_AUDIENCE_CHOISIE, RPJCR_NUMERO_AFFAIRE, RPJCR_CNBF_DEMANDE,
                   RPJCR_DATE_AUDIENCE_INITIALE, RPJCR_USER_EN_COURS
            FROM v2.DEMAT_RPJC_REN WITH (UPDLOCK) WHERE RPJCR_ID_RPJCR = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        string? Opt(string colonne) => FormeLigne.Texte(r, colonne) is { Length: > 0 } s ? s : null;
        return new Demande(id, FormeLigne.Texte(r, "RPJCR_ETAT"), Opt("RPJCR_ETAT_TRAITEMENT_RIG"),
            r["RPJCR_ID_AUDIENCE_CHOISIE"] is int a ? a : (int?)null, FormeLigne.Texte(r, "RPJCR_NUMERO_AFFAIRE"),
            FormeLigne.Texte(r, "RPJCR_CNBF_DEMANDE"), FormeLigne.DateDe(r, "RPJCR_DATE_AUDIENCE_INITIALE"), Opt("RPJCR_USER_EN_COURS"));
    }

    /// <summary>Mandataire.GetMandataireAbregeActifByCNBF (Mandataire.cs:357-359) : un mandataire actif, avec abrege, au CNBF.</summary>
    private static async Task<bool> AvocatActifAsync(GesteEnCours geste, string cnbf, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) MNDTR_ID_MNDTR FROM v2.MANDATAIRE
            WHERE MNDTR_ABREGE IS NOT NULL AND MNDTR_ACTIF = 1 AND MNDTR_NUMERO_CNBF = @cnbf ORDER BY MNDTR_ID_MNDTR DESC
            """, ct, new SqlParameter("@cnbf", cnbf));
        return await r.ReadAsync(ct);
    }

    /// <summary>L'audience <paramref name="id"/> (AudienceCabinet.GetAudienceCabinet), en mots ; null si elle n'est pas dans la base.</summary>
    private static async Task<string?> AudienceAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT AUDNC_DATE, AUDNC_HEURE FROM v2.AUDIENCE_CABINET WHERE AUDNC_ID_ADNC = @id", ct,
            new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        var heure = FormeLigne.Texte(r, "AUDNC_HEURE");
        var jour = $"n° {id} du {FormeLigne.Date(FormeLigne.DateDe(r, "AUDNC_DATE"))}";
        return heure.Length > 0 ? $"{jour} à {heure}" : jour;
    }

    /// <summary>RPJCRMR_DATE_OBLIGATOIRE du motif (OPE_AUDIENCE.cs:59-66) ; un motif hors de la table n'impose pas de date.</summary>
    private static async Task<bool> DateObligatoireAsync(GesteEnCours geste, string motif, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT RPJCRMR_DATE_OBLIGATOIRE FROM v2.DEMAT_RPJC_REN_MOTIF_RENVOI WHERE RPJCRMR_CODE = @code", ct,
            new SqlParameter("@code", motif));
        return await r.ReadAsync(ct) && r[0] is true;
    }
}
