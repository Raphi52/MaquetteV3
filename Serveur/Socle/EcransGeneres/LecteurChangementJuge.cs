using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Changer le juge-commissaire » (PROC_CHM, dans l'ecran reuni « Modifier un lot de PC ») : les procedures collectives ou un juge
/// a remplacer est en mission, et ce que RIG v2 en dit avant de les traiter. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 4, 26/11/2026. AUCUN ECRIVAIN : voir plus bas.
///
/// SOURCE (ancien RIG) : PROC_CHJCM (repertoire PROC_CHM ; le Detail OPE de proc_chm.md fait foi). La recherche est
/// MetierChangementJuge._GetChangementJuges (MetierChangementJuge.cs:183-262) : PROCEDURE_COLLECTIVE INNER JOIN ETRE_JUGE_COMM INNER
/// JOIN CLIENT, ou le juge a remplacer tient la fonction (titulaire : 'JCT','JC' ; suppleant : 'JCS', :190-200), ETJCM_FIN_MISSION =
/// 0 ; « autre juge » facultatif dans l'autre fonction (:203-205) ; ORDER BY PRCLL_NUM_PC ; une ligne par PC (:261-264). RIG v2 exige
/// le juge (_VerifierSaisieCritereRecherche, OPE_RECHERCHE_CHJCM.cs:99-104) : sans lui, la V3 montre la meme recherche pour tout
/// juge titulaire (la fonction par defaut, OPE_CRITERE.cs:68). CLIENT est reprise en rec.Client (REC-013), ses adresses en
/// rec.ClientAdresse.
///
/// CRITERES (IRechercheEcran) : « Juge à remplacer » et « Juge de l'autre fonction » (nom, prenom ou abrege : les combos de juges
/// de RIG v2), « Statut titulaire ou suppléant » (« titulaire » par defaut ; « suppléant »).
/// GROUPES : « Au forfait » / « Hors forfait » (PRCLL_FORFAIT_FACTURE : une PC au forfait echappe aux controles du client et ne se
/// facture jamais, ChangementJugeCommissairePc.cs:201, Ordonnance.cs:136).
/// REGLE D'ETAT (ChangementJugeCommissairePc.UpdateErreursMetier, :191-232 ; une ligne en erreur ne se coche pas,
/// DataGridViewChangementJuge.cs:273-280) :
///   - « Bloquée » (Rejetee) : une erreur de haut niveau -- le juge a remplacer present plusieurs fois dans la PC, PC exterieure,
///     et hors forfait : client pas en compte de PC, en sommeil, bloque ou inactif, pas un client en compte, sans adresse ;
///   - « Changement en attente » (EnAttente) : un changement de juge deja en attente pour le juge a remplacer
///     (ETRE_JUGE_COMM_EN_ATTENTE sans ordonnance signee, :225 ; sans juge choisi : pour tout juge de la PC) ;
///   - « À traiter » (AFaire) sinon.
/// FICHE (IFicheEcran) : « Juges et mandataires » (les juges en mission et les mandataires MDJ, RDC, LQJ, LIQ, MJ en mission,
/// :303-338) et « Contrôles » (les erreurs ci-dessus, au mot pres).
///
/// LE GESTE N'EST PAS ECRIT (« Valider » reste une demonstration) : RIG v2 ne change pas le juge de la PC, il FABRIQUE une ordonnance
/// du president par PC (Ordonnance.CreateOrdonnance, Ordonnance.cs:242-294) -- une instance NUMEROTEE (numero de role et repertoire
/// general, Ordonnance.cs:154-160 : dans la V3, num.Attribuer, hors des tables v2.* et rec.* qu'un ecran genere ecrit), une
/// affaire, une demande, une decision, ses evenements, une facture hors forfait (non reprise par la V3), les editions -- puis
/// ETRE_JUGE_COMM_EN_ATTENTE (PROC_CHJCM.cs:255-263), que MPC consomme. Une partie seule (le changement en attente sans
/// l'ordonnance qu'il vise) serait un etat que RIG v2 ne produit jamais.
/// </summary>
internal sealed class LecteurChangementJuge : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_CHM";

    internal const string AuForfait = "Au forfait", HorsForfait = "Hors forfait";

    internal const string CritereJuge = "Juge à remplacer", CritereStatut = "Statut titulaire ou suppléant", CritereAutre = "Juge de l'autre fonction";

    internal const string SectionOrganes = "Juges et mandataires", SectionControles = "Contrôles";

    internal const string ChampOrganes = "Organes de la PC", ChampErreurs = "Erreurs de RIG v2";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereJuge, CritereStatut, CritereAutre];

    private const string FonctionsTitulaire = "('JCT', 'JC')", FonctionsSuppleant = "('JCS')";

    private const string JugeCherche = "(j.JUG_NOM LIKE {0} OR j.JUG_PRENOM LIKE {0} OR j.JUG_ABREGE LIKE {0})";

    // Une ligne par PC. Le juge a remplacer present plusieurs fois (:195) : un juge de la fonction recherchee, cherche, qui a
    // plusieurs lignes en mission dans la PC (toutes fonctions, la liste JugesCommissaire de :303-317).
    private static string Requete(string fonctions, string juge, string filtres) => $"""
        SELECT TOP (@plafond) p.PRCLL_ID_PRCLL AS Id, p.PRCLL_NUM_PC AS NumPc, p.PRCLL_NOM_AFFAIRE AS Nom, p.PRCLL_EXTERIEUR AS Exterieur,
               p.PRCLL_FORFAIT_FACTURE AS Forfait, c.Type AS TypeClient, c.Statut AS StatutClient, c.EstComptePc,
               (SELECT COUNT(*) FROM rec.ClientAdresse AS ca WHERE ca.IdClient = c.Id) AS AdressesClient,
               {OrganesPc.Juges("p", "JCT", justeNom: false)} AS Jct, {OrganesPc.Juges("p", "JCS", justeNom: false)} AS Jcs,
               CASE WHEN EXISTS (SELECT 1 FROM v2.ETRE_JUGE_COMM AS ej JOIN v2.JUGE AS j ON j.JUG_ID_JUG = ej.ETJCM_ID_JUG
                                 WHERE ej.ETJCM_ID_PRCLL = p.PRCLL_ID_PRCLL AND ej.ETJCM_FIN_MISSION = 0 AND ej.ETJCM_FONCTION_PC IN {fonctions} {juge}
                                   AND (SELECT COUNT(*) FROM v2.ETRE_JUGE_COMM AS e2 WHERE e2.ETJCM_ID_PRCLL = p.PRCLL_ID_PRCLL
                                        AND e2.ETJCM_FIN_MISSION = 0 AND e2.ETJCM_ID_JUG = ej.ETJCM_ID_JUG) > 1) THEN 1 ELSE 0 END AS JugeEnDouble,
               CASE WHEN EXISTS (SELECT 1 FROM v2.ETRE_JUGE_COMM_EN_ATTENTE AS a JOIN v2.ETRE_JUGE_COMM AS ej ON ej.ETJCM_ID_JUG = a.ETJCMA_ID_JUG_A_REMPLACER
                                 JOIN v2.JUGE AS j ON j.JUG_ID_JUG = ej.ETJCM_ID_JUG
                                 WHERE a.ETJCMA_ID_PRCLL = p.PRCLL_ID_PRCLL AND a.ETJCMA_DATE_ORDOSIGN IS NULL AND ej.ETJCM_ID_PRCLL = p.PRCLL_ID_PRCLL
                                   AND ej.ETJCM_FIN_MISSION = 0 AND ej.ETJCM_FONCTION_PC IN {fonctions} {juge}) THEN 1 ELSE 0 END AS EnAttente,
               COUNT(*) OVER () AS Total
        FROM v2.PROCEDURE_COLLECTIVE AS p
        JOIN rec.Client AS c ON c.Id = p.PRCLL_ID_CLIEN
        WHERE EXISTS (SELECT 1 FROM v2.ETRE_JUGE_COMM AS ej JOIN v2.JUGE AS j ON j.JUG_ID_JUG = ej.ETJCM_ID_JUG
                      WHERE ej.ETJCM_ID_PRCLL = p.PRCLL_ID_PRCLL AND ej.ETJCM_FIN_MISSION = 0 AND ej.ETJCM_FONCTION_PC IN {fonctions} {juge})
          {filtres}
        ORDER BY p.PRCLL_NUM_PC, p.PRCLL_ID_PRCLL
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete(FonctionsTitulaire, "", ""), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var suppleant = recherche.Critere(CritereStatut) is { } statut && statut.StartsWith("supp", StringComparison.OrdinalIgnoreCase);
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var juge = "";
        var filtres = new List<string>();
        if (recherche.Critere(CritereJuge) is { } aRemplacer)
        {
            juge = "AND " + string.Format(CultureInfo.InvariantCulture, JugeCherche, "@juge");
            parametres.Add(new SqlParameter("@juge", FormeLigne.Contient(aRemplacer)));
        }
        if (recherche.Critere(CritereAutre) is { } autre)
        {
            // « andAutreJuge » (:203-205), comparaison « égal » : l'autre juge tient l'AUTRE fonction dans la PC.
            filtres.Add($"""
                EXISTS (SELECT 1 FROM v2.ETRE_JUGE_COMM AS ea JOIN v2.JUGE AS j ON j.JUG_ID_JUG = ea.ETJCM_ID_JUG
                        WHERE ea.ETJCM_ID_PRCLL = p.PRCLL_ID_PRCLL AND ea.ETJCM_FIN_MISSION = 0
                          AND ea.ETJCM_FONCTION_PC IN {(suppleant ? FonctionsTitulaire : FonctionsSuppleant)}
                          AND {string.Format(CultureInfo.InvariantCulture, JugeCherche, "@autre")})
                """);
            parametres.Add(new SqlParameter("@autre", FormeLigne.Contient(autre)));
        }
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(p.PRCLL_NUM_PC LIKE @texteNumero OR p.PRCLL_NOM_AFFAIRE LIKE @texteNom)");
            parametres.Add(new SqlParameter("@texteNumero", FormeLigne.Contient(recherche.Texte.Replace(" ", "", StringComparison.Ordinal))));
            parametres.Add(new SqlParameter("@texteNom", FormeLigne.Contient(recherche.Texte)));
        }
        var sql = Requete(suppleant ? FonctionsSuppleant : FonctionsTitulaire, juge, string.Concat(filtres.Select(f => " AND " + f)));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    /// <summary>Les erreurs de haut niveau de UpdateErreursMetier (ChangementJugeCommissairePc.cs:195-221), au mot pres.</summary>
    internal static List<string> ErreursBloquantes(SqlDataReader r)
    {
        var erreurs = new List<string>();
        if (Convert.ToInt32(r["JugeEnDouble"], CultureInfo.InvariantCulture) == 1) erreurs.Add("Le juge à remplacer est présent plusieurs fois dans la PC");
        if (r["Exterieur"] is true) erreurs.Add("La PC est extérieure");
        if (r["Forfait"] is not true)
        {
            if (r["EstComptePc"] is true)
            {
                var statut = FormeLigne.Texte(r, "StatutClient");
                if (statut != "A")
                    erreurs.Add(statut == "S" ? "Le client de la PC est en sommeil" : statut == "B" ? "Le client de la PC est en bloqué" : "Le client de la PC n'est pas actif");
                if (FormeLigne.Texte(r, "TypeClient") != "C") erreurs.Add("Le type du client de la PC n'est pas un client en compte");
                if (Convert.ToInt32(r["AdressesClient"], CultureInfo.InvariantCulture) == 0) erreurs.Add("Le client de la PC n'a pas d'adresse");
            }
            else erreurs.Add("Le client n'est pas un client en compte de PC");
        }
        return erreurs;
    }

    /// <summary>L'erreur de bas niveau (:225), au mot pres.</summary>
    internal const string ErreurEnAttente = "Il y a déjà un changement de juge en attente dans la PC pour le juge à remplacer";

    private static LigneLue Ligne(SqlDataReader r)
    {
        var numero = FormeLigne.Texte(r, "NumPc");
        var id = FormeLigne.Texte(r, "Id");
        var erreurs = ErreursBloquantes(r);
        var enAttente = Convert.ToInt32(r["EnAttente"], CultureInfo.InvariantCulture) == 1;
        var jct = FormeLigne.Texte(r, "Jct");
        var jcs = FormeLigne.Texte(r, "Jcs");
        var precisions = FormeLigne.Joindre(numero.Length > 0 ? $"PC {numero}" : null,
            jct.Length > 0 ? $"titulaire {jct}" : null, jcs.Length > 0 ? $"suppléant {jcs}" : null,
            erreurs.FirstOrDefault() ?? (enAttente ? "changement de juge déjà en attente" : null));
        var nom = FormeLigne.Texte(r, "Nom") is { Length: > 0 } n ? n : $"PC n° {(numero.Length > 0 ? numero : id)}";
        return new LigneLue(nom, precisions, r["Forfait"] is true ? AuForfait : HorsForfait,
                            erreurs.Count > 0 ? "Rejetee" : enAttente ? "EnAttente" : "AFaire", []) { Id = id };
    }

    // Les juges et les mandataires en mission de la PC (requeteSqlJugePc et requeteSqlMandataire, :229-249).
    private const string Organes = """
        SELECT ej.ETJCM_FONCTION_PC AS Fonction, LTRIM(RTRIM(ISNULL(j.JUG_NOM, '') + ' ' + ISNULL(j.JUG_PRENOM, ''))) AS Nom, ej.ETJCM_ID_ETJCM AS Ordre, 1 AS Rang
        FROM v2.ETRE_JUGE_COMM AS ej JOIN v2.JUGE AS j ON j.JUG_ID_JUG = ej.ETJCM_ID_JUG
        WHERE ej.ETJCM_ID_PRCLL = @id AND ej.ETJCM_FIN_MISSION = 0
        UNION ALL
        SELECT g.GRRPC_FONCTION_PC, mp.MNDPC_NOM + ISNULL(' ' + mp.MNDPC_COMPLEMENT_NOM, ''), g.GRRPC_ID_GRRPC, 2
        FROM v2.GERER_PC AS g JOIN v2.MANDATAIRE_PC AS mp ON mp.MNDPC_ID_MNDPC = g.GRRPC_ID_MNDPC
        WHERE g.GRRPC_ID_PRCLL = @id AND g.GRRPC_FONCTION_PC IN ('MDJ', 'RDC', 'LQJ', 'LIQ', 'MJ') AND g.GRRPC_FIN_MISSION = 0
        ORDER BY Rang, Ordre
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        string nom;
        var erreurs = new List<string>();
        // La ligne de la PC, lue par la requete de la liste restreinte a elle, pour tout juge-commissaire (titulaire ou suppleant :
        // la ligne ouverte peut venir d'une recherche de suppleant).
        var sql = Requete("('JCT', 'JC', 'JCS')", "", "AND p.PRCLL_ID_PRCLL = @id").Replace("TOP (@plafond)", "TOP (1)");
        await using (var r = await LectureV2.LireAsync(baseGreffe, sql, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            nom = FormeLigne.Texte(r, "Nom") is { Length: > 0 } x ? x : $"PC n° {FormeLigne.Texte(r, "NumPc")}";
            erreurs.AddRange(ErreursBloquantes(r));
            if (Convert.ToInt32(r["EnAttente"], CultureInfo.InvariantCulture) == 1) erreurs.Insert(0, ErreurEnAttente);
        }
        var organes = new List<string[]>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, Organes, ct, new SqlParameter("@id", n)))
            while (await r.ReadAsync(ct)) organes.Add([FormeLigne.Texte(r, "Fonction"), FormeLigne.Texte(r, "Nom")]);
        return new FicheEcran(Procedure, id, nom,
        [
            new SectionFiche(SectionOrganes, [new(ChampOrganes, "") { Colonnes = ["Fonction", "Nom"], Lignes = [.. organes] }]),
            new SectionFiche(SectionControles, [new(ChampErreurs, "")
            {
                Colonnes = ["Erreur"], Lignes = erreurs.Count == 0 ? [["Aucune : la PC peut être traitée."]] : [.. erreurs.Select(e => new[] { e })],
            }]),
        ]);
    }
}
