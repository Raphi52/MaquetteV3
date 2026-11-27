using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les gestes de « Mesures » (PROC_SURVMES) sur les evenements echus que LecteurSurveillanceMesures.cs liste. Mode d'emploi :
/// EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 9, 27/11/2026.
///
/// « Valider » -- OPE_AFFICHAGE_MES.cs (Source\RIG\DLL\Processus\PROC_SURVMES\ETP_AFFICHAGE_MES) :
///   - DoVerifier (:83-120) : « Aucune ligne cochée » (:86) ; une ligne dont le code n'a pas de code « expire » est une erreur (:97) ;
///     les lignes se groupent par MESURE, et chaque mesure est verrouillee par une demande SURVMES (CreateAndLockDemande,
///     Mesure.cs:925-937, sequenceur 2F :948) : une mesure tenue par une autre demande est un AVERTISSEMENT (:111) -- elle
///     n'est pas traitee, les autres le sont ;
///   - DoPreparerTableauEditionEtClientPourFacturation (:255-285) : pour chaque ligne, PasserEnExpirerEveProFuturWithParent(true)
///     (EvenementProcedure.cs:2090-2113) -- un evenement ni survenu ni expire passe EVNPR_DELAI_EXPIRE = 1, sinon « Impossible
///     d'expirer l'évènement. On est pas dans un evènement futur » ; puis creationEvenementSurvenuDepuisExpire (:1816-1846) : son
///     numero source (GUID s'il n'en a pas) ; sur la mesure, un evenement SURVENU du code « expire » (R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE),
///     date du jour, lie par EVNPR_NUMERO_SOURCE_EP_LIE (AddEvenementProcedureSurvenu, IEntiteJudiciaire.cs:132-151 ;
///     CreateEvenementProcedure, EvenementProcedure.cs:1721-1757 : type du code, numero source, piece numerisee si le code la
///     demande pour une instance ou une mesure, GererPiecesNum :2120-2149 pour le deposant) ; et l'evenement FUTUR du code survenu
///     (creationEvenementFutur, :1769-1806) : code R_CODEEVEPRO_CODE_EVE_PRO_FUTUR, dates maxi / mini / fixe = date du survenu plus
///     les mois, sinon les jours, sinon les semaines du code (Outils.cs:495-567), une seule date gardee -- fixe, sinon maxi, sinon
///     mini ; aucune : pas de futur (AddEvenementProcedureFuturOuExpire, IEntiteJudiciaire.cs:159-201) -- lie au survenu ;
///   - le rang : la liste des evenements de la mesure est renumerotee 1..n a chaque lecture (Mesure.cs:242-247) et un evenement
///     ajoute prend le rang n + 1 (AddEvenementProcedure, :338-352) ;
///   - DoSauver (:287-293) : la mesure, ses evenements, et la demande cloturee « validée » (:290).
/// « Editer les lignes cochées » et « Editer tout » (EditerLigne, :157) ouvrent un apercu Word a l'ecran : refuses avec ce motif.
///
/// CE QUE LA V3 NE REPREND PAS (nomme, avec la raison) :
///   - la demande SURVMES : creee, verrouillee et validee dans le meme geste, sans facture -- une trace, que le journal des gestes
///     remplace (decision du chef du plan) ; le verrou d'une AUTRE demande, lui, est respecte (VerrouDemandeV2) ;
///   - les editions du lot (DoGenererLignesEdition, :228-) : la V3 ne calcule pas les editions d'un evenement ;
///   - le texte de l'evenement (ConstructionTexte, que RIG v2 refait a la sauvegarde) : celui de l'evenement expire est garde tel
///     quel, les evenements crees n'en ont pas -- comme le lot 7 (EcrivainRetourAudience.cs) ;
///   - le courriel a la hotline d'un code mal parametre (plusieurs types de date, IEntiteJudiciaire.cs:168-181) : service externe ;
///     la priorite fixe / maxi / mini, elle, est appliquee.
/// fix-ok: retouches de la creation (27/11/2026) -- numeros de ligne cites relus par grep -n sur OPE_AFFICHAGE_MES.cs,
/// IEntiteJudiciaire.cs et EvenementProcedure.cs (DoPreparer :255, DoSauver :287, GererPiecesNum :2120, « == 3 » :182) ; un code
/// expire ou futur absent de CODE_EVE_PRO sautait la ligne en silence apres l'expiration : il refuse maintenant le geste.
/// </summary>
internal sealed class EcrivainSurveillanceMesures : IEcrivainEcran
{
    public string Procedure => "PROC_SURVMES";
    public string Ecran => "Mesures";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur, au mot pres (FORM_SURVMES.cs:23-29).</summary>
    internal const string Valider = "Valider", EditerCochees = "Editer les lignes cochées", EditerTout = "Editer tout";

    /// <summary>Le sequenceur d'une mesure dans DOSSIER_DEMANDE (Mesure.Sequenceur, Mesure.cs:948).</summary>
    internal const string SequenceurMesure = "2F";

    private const string Evenement = """
        SELECT e.EVNPR_ID_EVNPR, e.EVNPR_ID_MSINS, e.EVNPR_CODE_EVE_PRO, e.EVNPR_NUMERO_SOURCE,
               CONVERT(bit, ISNULL(e.EVNPR_SURVENU, 0)) AS Survenu, CONVERT(bit, ISNULL(e.EVNPR_DELAI_EXPIRE, 0)) AS Expire,
               m.MSINS_NUM_MESURE, ISNULL(c.R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE, '') AS CodeExpire
        FROM v2.EVENEMENT_PROCEDURE AS e
        JOIN v2.MESURE AS m ON m.MSINS_ID_MSINS = e.EVNPR_ID_MSINS
        LEFT JOIN v2.CODE_EVE_PRO AS c ON c.R_CODEEVEPRO_VALEUR = e.EVNPR_CODE_EVE_PRO
        WHERE e.EVNPR_ID_EVNPR = @id
        """;

    private const string Code = """
        SELECT R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_TYPE_EVE_PRO, CONVERT(bit, ISNULL(R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES, 0)) AS Piece,
               R_CODEEVEPRO_NATURE_DOC_GED, R_CODEEVEPRO_TYPE_DEPOSANT_GED, R_CODEEVEPRO_NOM_DEPOSANT_GED, R_CODEEVEPRO_CODE_EVE_PRO_FUTUR,
               R_CODEEVEPRO_MOIS_DATE_MAXI, R_CODEEVEPRO_JOURS_DATE_MAXI, R_CODEEVEPRO_SEMAINES_DATE_MAXI,
               R_CODEEVEPRO_MOIS_DATE_MINI, R_CODEEVEPRO_JOURS_DATE_MINI, R_CODEEVEPRO_SEMAINES_DATE_MINI,
               R_CODEEVEPRO_MOIS_DATE_FIXE, R_CODEEVEPRO_JOURS_DATE_FIXE, R_CODEEVEPRO_SEMAINES_DATE_FIXE
        FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c
        """;

    private sealed record Ligne(int Id, int Mesure, string NumMesure, string CodeEve, string CodeExpire, string? NumeroSource, bool Futur);

    /// <summary>Un code d'evenement : ce que CreateEvenementProcedure et creationEvenementFutur en lisent.</summary>
    private sealed record CodeEve(string Valeur, string? Type, bool Piece, string? Nature, string? TypeDeposant, string? NomDeposant, string? Futur,
                                  (int? Mois, int? Jours, int? Semaines) Maxi, (int? Mois, int? Jours, int? Semaines) Mini,
                                  (int? Mois, int? Jours, int? Semaines) Fixe);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        if (nom is EditerCochees or EditerTout)
            return IssueGeste.Refuser($"« {nom} » ouvre un aperçu Word de RIG v2 (EditerLigne, OPE_AFFICHAGE_MES.cs:157) : la V3 n'a pas d'édition Word, rien n'est édité.");
        if (nom != Valider) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune ligne cochée");

        // DoVerifier : chaque ligne, son code « expire », sa mesure.
        var lignes = new List<Ligne>();
        foreach (var id in ids)
        {
            Ligne? l = null;
            await using (var r = await geste.LireAsync(Evenement, ct, new SqlParameter("@id", int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1)))
                if (await r.ReadAsync(ct))
                    l = new Ligne(Convert.ToInt32(r["EVNPR_ID_EVNPR"], CultureInfo.InvariantCulture), Convert.ToInt32(r["EVNPR_ID_MSINS"], CultureInfo.InvariantCulture),
                                  FormeLigne.Texte(r, "MSINS_NUM_MESURE"), FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO"), FormeLigne.Texte(r, "CodeExpire"),
                                  FormeLigne.Texte(r, "EVNPR_NUMERO_SOURCE") is { Length: > 0 } s ? s : null, r["Survenu"] is false && r["Expire"] is false);
            if (l is null) return IssueGeste.Refuser($"Événement de mesure introuvable dans la base du greffe ({id}).");
            if (l.CodeExpire.Length == 0) return IssueGeste.Refuser($"La mesure n°{l.NumMesure} ne contient pas de code événement pro expiré");
            if (!l.Futur) return IssueGeste.Refuser("Impossible d'expirer l'évènement. On est pas dans un evènement futur");
            lignes.Add(l);
        }

        // Le verrou de chaque mesure (CreateAndLockDemande) : une mesure tenue par une autre demande n'est pas traitee.
        var bloquees = new List<string>();
        var parMesure = new List<IGrouping<int, Ligne>>();
        foreach (var mesure in lignes.GroupBy(l => l.Mesure))
        {
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Mesure", SequenceurMesure,
                    mesure.Key.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
                bloquees.Add($"mesure n°{mesure.First().NumMesure} : {verrou}");
            else
                parMesure.Add(mesure);
        }
        if (parMesure.Count == 0) return IssueGeste.Refuser($"Rien n'est validé : {string.Join(" ; ", bloquees)}.");

        var traces = new List<TraceGeste>();
        var faits = new List<string>();
        int survenus = 0, futurs = 0;
        foreach (var mesure in parMesure)
        {
            var rang = await RenumeroterAsync(geste, mesure.Key, ct);
            foreach (var l in mesure)
            {
                // PasserEnExpirerEveProFuturWithParent : le delai expire, le numero source garde (GUID s'il manquait).
                var source = l.NumeroSource ?? NouveauGuid();
                await geste.EcrireAsync("UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_DELAI_EXPIRE = 1, EVNPR_NUMERO_SOURCE = @s WHERE EVNPR_ID_EVNPR = @id",
                    ct, new SqlParameter("@s", source), new SqlParameter("@id", l.Id));
                traces.Add(new TraceGeste("affaire", $"l'événement {l.CodeEve} de la mesure {l.NumMesure}", l.Id.ToString(CultureInfo.InvariantCulture),
                    "a modifie", new { DelaiExpire = "non" }, new { DelaiExpire = "oui" }));
                faits.Add(l.Id.ToString(CultureInfo.InvariantCulture));

                // creationEvenementSurvenuDepuisExpire : le survenu du code « expire », date du jour, lie a l'expire.
                // Un code « expire » absent de CODE_EVE_PRO : RIG v2 creerait un evenement sans type (CreateEvenementProcedure,
                // :1741-1743) ; la V3 refuse plutot que d'ecrire un evenement que la table des codes ne connait pas (rien n'est ecrit).
                if (await LireCodeAsync(geste, l.CodeExpire, ct) is not { } codeSurvenu)
                    return IssueGeste.Refuser($"Le code d'expiration « {l.CodeExpire} » du code « {l.CodeEve} » n'est pas dans la table des codes d'événement du greffe : rien n'est validé.");
                var jour = geste.Maintenant.Date;
                var sourceSurvenu = NouveauGuid();
                var idSurvenu = await InsererAsync(geste, l.Mesure, codeSurvenu, survenu: true, jour, null, null, null, sourceSurvenu, source, ++rang, ct);
                survenus++;
                traces.Add(new TraceGeste("affaire", $"l'événement survenu {codeSurvenu.Valeur} de la mesure {l.NumMesure}",
                    idSurvenu.ToString(CultureInfo.InvariantCulture), "a cree", null,
                    new { Code = codeSurvenu.Valeur, Date = jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Lie = l.Id }));

                // creationEvenementFutur : le futur du code survenu, ses dates du survenu plus les delais du code.
                if (codeSurvenu.Futur is not { } codeFutur) continue;
                if (await LireCodeAsync(geste, codeFutur, ct) is not { } futur)
                    return IssueGeste.Refuser($"Le code futur « {codeFutur} » du code « {codeSurvenu.Valeur} » n'est pas dans la table des codes d'événement du greffe : rien n'est validé.");
                var (maxi, mini, fixe) = (Echeance(jour, codeSurvenu.Maxi), Echeance(jour, codeSurvenu.Mini), Echeance(jour, codeSurvenu.Fixe));
                if (maxi is null && mini is null && fixe is null) continue;   // IEntiteJudiciaire.cs:182-184 : un evenement sans date n'est pas cree
                if (fixe is not null) (maxi, mini) = (null, null);
                else if (maxi is not null) mini = null;
                var idFutur = await InsererAsync(geste, l.Mesure, futur, survenu: false, null, maxi, mini, fixe, NouveauGuid(), sourceSurvenu, ++rang, ct);
                futurs++;
                traces.Add(new TraceGeste("affaire", $"l'événement futur {futur.Valeur} de la mesure {l.NumMesure}",
                    idFutur.ToString(CultureInfo.InvariantCulture), "a cree", null,
                    new { Code = futur.Valeur, Echeance = FormeLigne.Date(fixe ?? maxi ?? mini), Lie = idSurvenu }));
            }
        }

        var expires = faits.Count;
        var message = $"{Compte(expires, "événement expiré", "événements expirés")} sur {Compte(parMesure.Count, "mesure", "mesures")} ; "
                    + $"{Compte(survenus, "événement survenu créé", "événements survenus créés")}"
                    + (futurs > 0 ? $", {Compte(futurs, "événement futur", "événements futurs")}" : "")
                    + ". Les éditions ne sont pas générées."
                    + (bloquees.Count > 0 ? $" Non traitée{(bloquees.Count > 1 ? "s" : "")} : {string.Join(" ; ", bloquees)}." : "");
        return IssueGeste.Ecrit(message, [.. faits], [.. traces]);
    }

    private static string Compte(int n, string un, string plusieurs) => n == 1 ? $"1 {un}" : $"{n} {plusieurs}";

    private static string NouveauGuid() => Guid.NewGuid().ToString().ToUpperInvariant();

    /// <summary>La date du survenu plus les mois, sinon les jours, sinon les semaines du code (Outils.cs:495-567) ; null sans delai.</summary>
    internal static DateTime? Echeance(DateTime date, (int? Mois, int? Jours, int? Semaines) delai)
        => delai.Mois is { } m ? date.AddMonths(m) : delai.Jours is { } j ? date.AddDays(j) : delai.Semaines is { } s ? date.AddDays(s * 7) : null;

    /// <summary>Les evenements de la mesure renumerotes 1..n (Mesure.cs:242-247) ; rend n, le rang du dernier.</summary>
    private static async Task<int> RenumeroterAsync(GesteEnCours geste, int mesure, CancellationToken ct)
    {
        await geste.EcrireAsync("""
            UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_NUM_ORDRE = n.Ordre
            FROM (SELECT EVNPR_ID_EVNPR, CONVERT(smallint, ROW_NUMBER() OVER (ORDER BY ISNULL(EVNPR_NUM_ORDRE, 0), EVNPR_ID_EVNPR)) AS Ordre
                  FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_MSINS = @m) AS n
            WHERE n.EVNPR_ID_EVNPR = v2.EVENEMENT_PROCEDURE.EVNPR_ID_EVNPR
            """, ct, new SqlParameter("@m", mesure));
        await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_MSINS = @m", ct, new SqlParameter("@m", mesure));
        await r.ReadAsync(ct);
        return r.GetInt32(0);
    }

    private static async Task<CodeEve?> LireCodeAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(Code, ct, new SqlParameter("@c", code));
        if (!await r.ReadAsync(ct)) return null;
        string? Texte(string colonne) => FormeLigne.Texte(r, colonne) is { Length: > 0 } t ? t : null;
        int? Entier(string colonne) => r[colonne] is DBNull ? null : Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture);
        (int?, int?, int?) Delai(string type) => (Entier($"R_CODEEVEPRO_MOIS_DATE_{type}"), Entier($"R_CODEEVEPRO_JOURS_DATE_{type}"), Entier($"R_CODEEVEPRO_SEMAINES_DATE_{type}"));
        return new CodeEve(FormeLigne.Texte(r, "R_CODEEVEPRO_VALEUR"), Texte("R_CODEEVEPRO_TYPE_EVE_PRO") is { } t ? t[..1] : null, r["Piece"] is true,
            Texte("R_CODEEVEPRO_NATURE_DOC_GED"), Texte("R_CODEEVEPRO_TYPE_DEPOSANT_GED"), Texte("R_CODEEVEPRO_NOM_DEPOSANT_GED"),
            Texte("R_CODEEVEPRO_CODE_EVE_PRO_FUTUR"), Delai("MAXI"), Delai("MINI"), Delai("FIXE"));
    }

    /// <summary>CreateEvenementProcedure sur la mesure (EvenementProcedure.cs:1721-1757) : le code, survenu ou futur, ses dates, son
    /// type, son numero source et son lien, sa piece numerisee (et, pour un survenu, le deposant : GererPiecesNum, :2120-2149).</summary>
    private static Task<int> InsererAsync(GesteEnCours geste, int mesure, CodeEve code, bool survenu, DateTime? date, DateTime? maxi,
                                          DateTime? mini, DateTime? fixe, string source, string lie, int rang, CancellationToken ct)
        => CreationV2.InsererAsync(geste, "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", new Dictionary<string, object?>
        {
            ["EVNPR_ID_MSINS"] = mesure, ["EVNPR_CODE_EVE_PRO"] = code.Valeur, ["EVNPR_DATE"] = date, ["EVNPR_DATE_MAXI"] = maxi,
            ["EVNPR_DATE_MINI"] = mini, ["EVNPR_DATE_FIXE"] = fixe, ["EVNPR_SURVENU"] = survenu, ["EVNPR_DELAI_EXPIRE"] = false,
            ["EVNPR_NUMERO_SOURCE"] = source, ["EVNPR_NUMERO_SOURCE_EP_LIE"] = lie, ["EVNPR_TYPE_EVE_PRO"] = code.Type,
            ["EVNPR_NUM_ORDRE"] = (short)rang, ["EVNPR_AVEC_PIECE_NUMERISE"] = code.Piece,
            ["EVNPR_NATURE_DOC_GED"] = code.Piece ? code.Nature : null, ["EVNPR_TYPE_DEPOSANT_GED"] = code.Piece ? code.TypeDeposant : null,
            ["EVNPR_NOM_DEPOSANT_GED"] = code.Piece && survenu ? code.NomDeposant : null,
        }, ct);
}
