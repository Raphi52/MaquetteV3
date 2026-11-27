using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Envoi JAL / BODACC » (PROC_ENV_JAL, forme Operation) : « Generer le bordereau » envoie au journal d'annonces legales les
/// publicites des evenements coches, comme le mode EDI de RIG v2. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurEnvoiJal.cs
/// (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ENV_JAL\Operations\OPE_A_ENVOYER.cs (cite « :ligne ») ;
/// les publicites generees : Outils\DLL\RigMetier\JUDICIAIRE\GenerationXML IPubliciteJudiciaire\LigneJal.cs (GetPubtoEdit,
/// :264-330) ; le bordereau : Outils\DLL\RigMetier\JUDICIAIRE\BordereauJal.cs (CreateBordereauXml, :82-95).
///
/// LE MODE : en EDI (reglage JAL_ENVOIE_EDI = « 1 », PROC_ENV_JAL.cs:68 ; c'est le reglage de RIG_DEV), la V2 fait deux temps :
/// « Generer » (:189-334) ecrit le fichier XML dans le dossier d'archives SANS rien ecrire en base, puis « Envoyer » (EnvoyerJAL,
/// :1071-1170) ecrit le bordereau et les publicites. Le geste de la V3 fait les deux d'un coup : la confirmation de l'operation
/// tient lieu du second bouton. Hors EDI, la V2 assemble un document Word depuis la GED (GenererJALWord_OLDVersion, :493-597) :
/// non repris, le geste est refuse en le disant (aucun cas sur le banc : le reglage du banc est EDI).
///
/// CONTROLES, avant toute ecriture, au mot pres de la V2 (un saut de ligne de la V2 devient une espace) :
///   1. aucune ligne cochee : « Vous devez sélectionner au moins une ligne pour pouvoir générer. » (:197-200) ;
///   2. une ligne deja envoyee : la V2 ne la propose pas (elle est dans l'onglet des deja envoyees, OPE_RECH_ANNONCES.cs:451-453,
///      :485-487) ; la V3 le dit ;
///   3. EDI, une publicite cochee sans journal (PBLCT_DESTINATAIRE vide) : « Impossible de générer ..., le nom du JAL n'est pas
///      rempli pour ... ci-dessous : <numeros> Vous devez allez en MPC pour compléter le nom du destinataire JAL. » (:216-224 ; la
///      V2 accorde « l'annonce » au nombre de lignes de sa grille, la V3 au nombre de lignes cochees) ;
///   4. EDI, plusieurs journaux dans la selection : « Impossible de générer les annonces car vous avez plusieurs destinataires JAL
///      dans votre sélection. Vous devez faire un envoi par destinataire JAL. » (:226-229) ;
///   5. les PUBLICITES GENEREES sont celles des EVENEMENTS coches (lstIdEvePro, :236-247), a envoyer (PBLCT_DATE_ENVOI_ELECTRONIQUE
///      NULL), dont la date d'envoi prevue est dans la periode du bordereau (GetPubtoEdit : >= debut et <= fin, telle quelle, :298) ;
///      aucune : « Aucun élément à éditer. Veuillez modifier vos critères. » (:330) ;
///   6. le numero de bordereau, serie RBOE de l'annee (Numerotation.Generer, :1094 ; geste.AttribuerNumeroAsync) ; pas de numero :
///      « Problème de création du numéro métier. Veuillez contacter AMITEL. » (:1162) ;
///   7. une publicite generee dont l'etat n'est pas « 1 » (a envoyer) : « Problème avec la Publicité d'identifiant <id> pour la
///      procédure <numero>. L 'état n'est pas 'A envoyer' mais l'annonce n'a pas de numéro de bordereau. Veuillez contacter
///      AMITEL » et la V2 annule tout (:1139-1145).
/// ECRIT (EnvoyerJAL, :1094-1150), dans la transaction du geste :
///   - le BORDEREAU_JAL : numero, nombre d'annonces generees, periode (BRDRJ_DATE_MIN / MAX), creation et generation a maintenant,
///     etat XML « 2 », journal (CreateBordereauXml) ;
///   - chaque publicite generee : son bordereau (id et numero), la date d'envoi electronique (maintenant), PBLCT_ETAT_PUBLICITE et
///     PBLCT_ETAT_XML a « 2 », et le nom du fichier d'envoi (PBLCT_DOC_ENVOI_ELECTRONIQUE) : « Envoi <journal> du <debut> au <fin>.
///     Le <maintenant> <login> », « / » et « : » en « - », 86 caracteres au plus (:345-349), suivi du numero de bordereau (:1112-1118).
/// LA PERIODE du bordereau : la section « Periode du bordereau » ; vide, celle que la V2 pre-remplit (JAL_DATE_DEBUT_ENVOI, puis
///   aujourd'hui), comme la liste (LecteurEnvoiJal.cs). Le journal du nom de fichier : celui de la selection (la V2 prend sa liste
///   deroulante, qui vaut ce journal quand il est choisi).
/// SIMULE, ETIQUETE : le FICHIER XML de l'envoi (GenerationXml, :421-490) n'est pas produit et rien n'est transmis au journal --
///   BRDRJ_DONNEES_XML reste vide, le message le dit. La transmission elle-meme est faite par la V2 hors de cet ecran.
/// NON REPRIS (nomme) : la demande V2 « BORD_JUD » creee et validee d'un coup, sans facture (:1097, :1153-1157) -- une trace, que
///   le journal des gestes remplace (decision du chef, plan-676a4b19) ; les deux questions oui/non (annonces sans activite :204-208,
///   annonces non editees avant la periode :250-264), dont la confirmation de l'operation tient lieu ; le choix d'une affectation du
///   patrimoine d'un EIRL (:272-302) ; l'ecriture des reglages JAL_DATE_DEBUT_ENVOI / JAL_DATE_FIN_ENVOI apres generation (:315-328 :
///   des reglages du greffe, soc.Reglage, qu'un geste n'ecrit pas) ; l'edition recapitulative Word (:1175-1279) ; la suppression d'un
///   bordereau (OPE_DEJA_ENVOYER.cs:297-376), un second geste qu'une Operation ne porte pas.
/// </summary>
internal sealed class EcrivainEnvoiJal : IEcrivainEcran
{
    public string Procedure => "PROC_ENV_JAL";

    public string Ecran => "Envoi JAL / BODACC";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    /// <summary>Le bouton et la section du descripteur, au mot pres.</summary>
    internal const string Generer = "Générer le bordereau", Section = "Période du bordereau", Debut = "Date de début", Fin = "Date de fin";

    /// <summary>Les messages de RIG v2, au mot pres (en tete du fichier).</summary>
    internal const string SansLigne = "Vous devez sélectionner au moins une ligne pour pouvoir générer.";
    internal const string PlusieursJournaux = "Impossible de générer les annonces car vous avez plusieurs destinataires JAL dans votre sélection. " +
                                              "Vous devez faire un envoi par destinataire JAL.";
    internal const string AucunElement = "Aucun élément à éditer. Veuillez modifier vos critères.";
    internal const string SansNumero = "Problème de création du numéro métier. Veuillez contacter AMITEL.";

    private sealed record Publicite(int Id, int? Evenement, string Numero, string Journal, string Etat, DateTime? Envoyee);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Generer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Select(i => int.TryParse(i, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
                       .Where(n => n > 0).Distinct().ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(SansLigne);

        var reglages = await ReglagesAsync(geste, ct);
        if (reglages.GetValueOrDefault("JAL_ENVOIE_EDI") != "1")
            return IssueGeste.Refuser("Envoi au JAL par document Word (réglage JAL_ENVOIE_EDI différent de 1) : RIG v3 n'assemble pas ce " +
                                      "document ; générer dans RIG v2.");

        var cochees = new List<Publicite>();
        foreach (var id in ids)
            if (await PubliciteAsync(geste, id, ct) is { } p) cochees.Add(p);
            else return IssueGeste.Refuser($"La publicité {id} n'est pas dans la base du greffe.");
        if (cochees.FirstOrDefault(p => p.Envoyee is not null || p.Evenement is null) is { } deja)
            return IssueGeste.Refuser($"La publicité {deja.Id} de la procédure {deja.Numero} est déjà envoyée : RIG v2 ne propose à la " +
                                      "génération que les annonces à envoyer.");
        var sansJournal = cochees.Where(p => p.Journal.Length == 0).Select(p => p.Numero).Distinct().ToList();
        if (sansJournal.Count > 0)
            return IssueGeste.Refuser($"Impossible de générer {(cochees.Count == 1 ? "l'annonce" : "les annonces")}, le nom du JAL n'est pas " +
                                      $"rempli pour {(sansJournal.Count == 1 ? "la procédure" : "les procédures")} ci-dessous : " +
                                      $"{string.Join(", ", sansJournal)} Vous devez allez en MPC pour compléter le nom du destinataire JAL.");
        var journaux = cochees.Select(p => p.Journal).Distinct(StringComparer.Ordinal).ToList();
        if (journaux.Count > 1) return IssueGeste.Refuser(PlusieursJournaux);
        var journal = journaux[0];

        // La periode du bordereau : la section, sinon les dates que la V2 pre-remplit (RecuperationDesDates).
        var du = CriteresConsultation.Date(geste.Valeur(Section, Debut))
                 ?? (DateTime.TryParse(reglages.GetValueOrDefault("JAL_DATE_DEBUT_ENVOI"), CultureInfo.GetCultureInfo("fr-FR"),
                                       DateTimeStyles.None, out var d) ? d.Date : geste.Maintenant.Date);
        var au = CriteresConsultation.Date(geste.Valeur(Section, Fin)) ?? geste.Maintenant.Date;

        // Les publicites generees : celles des evenements coches, a envoyer, dans la periode (GetPubtoEdit, :295-301).
        var evenements = cochees.Select(p => p.Evenement!.Value).Distinct().ToArray();
        var generees = new List<Publicite>();
        await using (var r = await geste.LireAsync($"""
            SELECT p.PBLCT_ID_PBLCT FROM v2.PUBLICITE AS p
            WHERE p.PBLCT_ID_EVNPR IN ({string.Join(", ", evenements.Select(e => e.ToString(CultureInfo.InvariantCulture)))})
              AND p.PBLCT_DATE_ENVOI >= @du AND p.PBLCT_DATE_ENVOI <= @au AND p.PBLCT_DATE_ENVOI_ELECTRONIQUE IS NULL
            ORDER BY p.PBLCT_ID_PBLCT
            """, ct, new SqlParameter("@du", du), new SqlParameter("@au", au)))
        {
            var idsGeneres = new List<int>();
            while (await r.ReadAsync(ct)) idsGeneres.Add(r.GetInt32(0));
            await r.CloseAsync();
            foreach (var id in idsGeneres) generees.Add((await PubliciteAsync(geste, id, ct))!);
        }
        if (generees.Count == 0) return IssueGeste.Refuser(AucunElement);
        if (generees.FirstOrDefault(p => p.Etat != "1") is { } horsEtat)
            return IssueGeste.Refuser($"Problème avec la Publicité d'identifiant {horsEtat.Id} pour la procédure {horsEtat.Numero}. L 'état " +
                                      "n'est pas 'A envoyer' mais l'annonce n'a pas de numéro de bordereau. Veuillez contacter AMITEL");

        var (numero, _) = await CreationV2.NumeroAsync(geste, "RBOE", geste.Maintenant.Year, ct);
        if (numero is null) return IssueGeste.Refuser(SansNumero);

        var maintenant = geste.Maintenant;
        var bordereau = await geste.InsererAsync("v2.BORDEREAU_JAL", "BRDRJ_ID_BRDRJ", new Dictionary<string, object?>
        {
            ["BRDRJ_NUM_BORDEREAU"] = numero, ["BRDRJ_NB_ANNONCES"] = generees.Count, ["BRDRJ_DATE_MIN"] = du, ["BRDRJ_DATE_MAX"] = au,
            ["BRDRJ_DATE_CREATION"] = maintenant, ["BRDRJ_DATE_GENERATION"] = maintenant, ["BRDRJ_DONNEES_XML"] = null,
            ["BRDRJ_ETAT_XML"] = "2", ["BRDRJ_DESTINATAIRE"] = journal,
        }, ct);
        var fichier = NomFichier(journal, du, au, maintenant, geste.Agent.CompteWindows, numero);
        foreach (var p in generees)
            await geste.EcrireAsync("""
                UPDATE v2.PUBLICITE SET PBLCT_ID_BRDRJ = @b, PBLCT_NUM_BORDEREAU = @n, PBLCT_DATE_ENVOI_ELECTRONIQUE = @m,
                       PBLCT_ETAT_PUBLICITE = '2', PBLCT_ETAT_XML = '2', PBLCT_DOC_ENVOI_ELECTRONIQUE = @f
                WHERE PBLCT_ID_PBLCT = @id
                """, ct, new SqlParameter("@b", bordereau), new SqlParameter("@n", numero), new SqlParameter("@m", maintenant),
                new SqlParameter("@f", fichier), new SqlParameter("@id", p.Id));

        var n = generees.Count;
        return IssueGeste.Ecrit(
            $"Bordereau {numero} pour {journal} : {n} annonce{(n > 1 ? "s" : "")} envoyée{(n > 1 ? "s" : "")} ({fichier}). Fichier XML EDI : " +
            "non produit et rien n'est transmis au journal (simulé, RIG v3 n'envoie rien hors du poste).",
            [.. generees.Select(p => p.Id.ToString(CultureInfo.InvariantCulture))],
            new TraceGeste("affaire", $"le bordereau JAL {numero}", bordereau.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new { Bordereau = numero, Journal = journal, Du = FormeLigne.Date(du), Au = FormeLigne.Date(au), Publicites = generees.Select(p => p.Id).ToArray(),
                      Fichier = fichier, Xml = "non produit (simulé)" }));
    }

    /// <summary>Le nom du fichier d'envoi tel que la V2 l'ecrit dans PBLCT_DOC_ENVOI_ELECTRONIQUE (:345-349, puis :1112-1118).</summary>
    internal static string NomFichier(string journal, DateTime du, DateTime au, DateTime maintenant, string login, string numero)
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var nom = $"Envoi {journal} du {du.ToString("d", fr)} au {au.ToString("d", fr)}. Le {maintenant.ToString(fr)} {login}";
        nom = nom.Replace('/', '-').Replace(':', '-');
        if (nom.Length > 86) nom = nom[..86];
        return $"{nom} {numero}".Replace('/', '-').Replace(':', '-');
    }

    /// <summary>Une publicite, son evenement, le numero de sa PC ou de sa prevention (celui de la liste), son journal, son etat.</summary>
    private static async Task<Publicite?> PubliciteAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT p.PBLCT_ID_PBLCT, p.PBLCT_ID_EVNPR, ISNULL(pc.PRCLL_NUM_PC, pv.PREVE_NUM_PRV) AS NUM, p.PBLCT_DESTINATAIRE,
                   p.PBLCT_ETAT_PUBLICITE, p.PBLCT_DATE_ENVOI_ELECTRONIQUE
            FROM v2.PUBLICITE AS p
            LEFT JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_ID_EVNPR = p.PBLCT_ID_EVNPR
            LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = e.EVNPR_ID_PRCLL
            LEFT JOIN v2.PREVENTION AS pv ON pv.PREVE_ID_PREVE = e.EVNPR_ID_PREVE
            WHERE p.PBLCT_ID_PBLCT = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Publicite(r.GetInt32(0), r["PBLCT_ID_EVNPR"] is int e ? e : null, FormeLigne.Texte(r, "NUM"),
            FormeLigne.Texte(r, "PBLCT_DESTINATAIRE").Trim(), FormeLigne.Texte(r, "PBLCT_ETAT_PUBLICITE").Trim(),
            FormeLigne.DateDe(r, "PBLCT_DATE_ENVOI_ELECTRONIQUE"));
    }

    /// <summary>Les reglages du greffe que ce geste lit (PARAMETRAGE de RIG v2, repris dans soc.Reglage).</summary>
    private static async Task<Dictionary<string, string>> ReglagesAsync(GesteEnCours geste, CancellationToken ct)
    {
        var reglages = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var r = await geste.LireAsync(
            "SELECT Code, Valeur FROM soc.Reglage WHERE Code IN ('JAL_ENVOIE_EDI', 'JAL_DATE_DEBUT_ENVOI')", ct);
        while (await r.ReadAsync(ct)) reglages[FormeLigne.Texte(r, "Code")] = FormeLigne.Texte(r, "Valeur").Trim();
        return reglages;
    }
}
