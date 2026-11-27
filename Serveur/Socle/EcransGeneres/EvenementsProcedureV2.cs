using System.Globalization;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les EVENEMENTS DE PROCEDURE d'une instance que les gestes ecrivent comme RIG v2 (v2.EVENEMENT_PROCEDURE), ecrits ici une seule
/// fois pour tous les ecrivains : le Retour d'audience et les Convocations (lot 7), et les lots qui en auront besoin. Plan « RIG v3
/// 100 % fonctionnel », lot 7, 26/11/2026.
///
/// L'EVENEMENT SURVENU (EvenementProcedure.CreateEvenementProcedure, Outils\DLL\RigMetier\JUDICIAIRE\EvenementProcedure.cs:1721-1757,
/// survenu = vrai, expire = faux) : le code, la date sans l'heure, un numero source GUID, le type du code
/// (R_CODEEVEPRO_TYPE_EVE_PRO), la piece numerisee quand le code la demande pour une instance (R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES,
/// avec sa nature et son type de deposant GED) ; son numero d'ordre = le nombre d'evenements de l'instance une fois ajoute
/// (Instance.AddEvenementProcedure, Instance.cs:589-602).
///
/// L'EVENEMENT FUTUR LIE (EvenementProcedure.creationEvenementFutur, EvenementProcedure.cs:1769-1806, branche INSTANCE) : seulement
/// pour un survenu DATE (« Création d'évenement futur sur survenu », OPE_EVENEMENT.cs:1587-1608) dont le code porte un code futur
/// (R_CODEEVEPRO_CODE_EVE_PRO_FUTUR). Ses trois dates se calculent depuis la date du survenu avec les delais du code SURVENU
/// (Outils.CalculDateFixe/Maxi/MiniEveProSurvenuInstanceMesure, JUDICIAIRE\Fonctions diverses\Outils.cs:495-566) : les mois, sinon
/// les jours, sinon les semaines x 7, sinon pas de date. Puis IEntiteJudiciaireExtensions.AddEvenementProcedureFuturOuExpire
/// (Interfaces Internes\IEntiteJudiciaire.cs:159-197) : aucune date -> aucun evenement ; plusieurs -> la fixe, sinon la maxi, sinon
/// la mini ; l'evenement est cree au code futur, non survenu, non expire, sans date, avec ces dates (CreateEvenementProcedure : type
/// et piece numerisee lus sur le code FUTUR) ; numero d'ordre suivant ; EVNPR_NUMERO_SOURCE_EP_LIE = le numero source du survenu
/// (:1800). En masse (PROC_MJUDS, PROC_RETAUD\PROC_MJUDS\PROC_MJUDS.cs:1557-1576), chaque instance recoit une copie du survenu et de
/// ses futurs lies, chacun avec un GUID neuf, le futur lie au GUID du survenu copie : c'est ce qu'ecrit cette classe, instance par
/// instance.
///
/// LE PASSAGE EN SURVENU D'UN FUTUR (EvenementProcedure.PasserEnSurvenuEveProFutur, EvenementProcedure.cs:1968-1996 ; lot 11, le
/// retour d'un LRAR, PROC_RETOURLRAR) : l'evenement futur existant devient survenu a la date donnee, ses trois dates s'effacent, son
/// texte aussi (reconstruit par la V2 a l'enregistrement), ses champs de piece numerisee se reprennent du code (GererPiecesNum,
/// :2120-2149, branche instance : la piece si le code la demande, sa nature, son type et son nom de deposant GED) ; avec la creation
/// du futur, son propre futur lie se cree comme ci-dessus (creationEvenementFutur), lie a SON numero source.
///
/// NON REPRIS (nomme) : le texte de l'evenement (EVNPR_TEXTE, EvenementProcedure.ConstructionTexte) ; le courriel a la hotline quand
/// le code futur a plusieurs delais (IEntiteJudiciaire.cs:170-182 : service externe, la priorite fixe / maxi / mini est appliquee) ;
/// les evenements inter-entites d'une instance liee a une PC ou une prevention (OPE_EVENEMENT.cs:1534-1584 : PROC_MJUDS les cree sur
/// son instance de saisie, qui n'a ni PC ni prevention en lot).
/// </summary>
internal static class EvenementsProcedureV2
{
    /// <summary>Ce qui a ete ecrit : le survenu (id, numero source) et, s'il y en a un, le futur lie (id, code) ; et chacun tel que
    /// ses editions le lisent (<see cref="EditionsEvenementV2.Evenement"/> : code, numero source, date, rang, piece), survenu d'abord.</summary>
    internal sealed record Ecrits(int IdSurvenu, string NumeroSource, int? IdFutur, string? CodeFutur,
                                  IReadOnlyList<EditionsEvenementV2.Evenement> Evenements);

    /// <summary>Ecrit l'evenement SURVENU <paramref name="codeEve"/> sur l'instance, a <paramref name="date"/> ; avec
    /// <paramref name="avecFutur"/>, aussi l'evenement futur lie que son code porte (voir l'en-tete).</summary>
    internal static async Task<Ecrits> SurvenuAsync(GesteEnCours geste, int idInstance, string codeEve, DateTime? date, bool avecFutur,
                                                    CancellationToken ct)
    {
        var jour = date?.Date;
        var numeroSource = Guid.NewGuid().ToString().ToUpperInvariant();
        var code = await LireCodeAsync(geste, codeEve, avecFutur, ct);
        var (idSurvenu, rang, piece) = await InsererAsync(geste, idInstance, codeEve, code, new Dictionary<string, object?>
        {
            ["EVNPR_DATE"] = jour, ["EVNPR_SURVENU"] = true, ["EVNPR_NUMERO_SOURCE"] = numeroSource,
        }, ct);
        var survenu = new EditionsEvenementV2.Evenement(codeEve, numeroSource, jour, rang, piece);
        if (!avecFutur || jour is not { } depuis) return new Ecrits(idSurvenu, numeroSource, null, null, [survenu]);
        return await FuturLieAsync(geste, idInstance, code, depuis, numeroSource, ct) is { } futur
            ? new Ecrits(idSurvenu, numeroSource, futur.Id, futur.Evenement.Code, [survenu, futur.Evenement])
            : new Ecrits(idSurvenu, numeroSource, null, null, [survenu]);
    }

    /// <summary>Passe en SURVENU, a <paramref name="date"/>, l'evenement FUTUR existant <paramref name="idEvenement"/> de l'instance
    /// (PasserEnSurvenuEveProFutur, en-tete) ; avec <paramref name="creerFutur"/>, cree aussi le futur lie que son code porte. Rend
    /// l'id et le code de ce futur cree, null sans futur.</summary>
    internal static async Task<(int Id, string Code)?> PasserEnSurvenuAsync(GesteEnCours geste, int idEvenement, int idInstance, string codeEve,
                                                                           string numeroSource, DateTime date, bool creerFutur, CancellationToken ct)
    {
        var jour = date.Date;
        var code = await LireCodeAsync(geste, codeEve, creerFutur, ct);
        var colonnes = await EditionsV2.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        var parametres = new List<SqlParameter> { new("@id", idEvenement), new("@d", jour) };
        var champs = new List<string> { "EVNPR_SURVENU = 1", "EVNPR_DATE = @d", "EVNPR_DATE_MAXI = NULL", "EVNPR_DATE_MINI = NULL", "EVNPR_DATE_FIXE = NULL" };
        if (colonnes.Contains("EVNPR_TEXTE")) champs.Add("EVNPR_TEXTE = NULL");
        // GererPiecesNum (:2120-2149) : rien sans le code ; la piece seulement si le code la demande ; nature, type et nom du deposant
        // GED repris du code dans tous les cas.
        if (code is not null)
        {
            if (code.Piece) champs.Add("EVNPR_AVEC_PIECE_NUMERISE = 1");
            champs.Add("EVNPR_NATURE_DOC_GED = @nature");
            champs.Add("EVNPR_TYPE_DEPOSANT_GED = @deposant");
            parametres.Add(new SqlParameter("@nature", (object?)code.Nature ?? DBNull.Value));
            parametres.Add(new SqlParameter("@deposant", (object?)code.Deposant ?? DBNull.Value));
            if (colonnes.Contains("EVNPR_NOM_DEPOSANT_GED"))
            {
                champs.Add("EVNPR_NOM_DEPOSANT_GED = @nom");
                parametres.Add(new SqlParameter("@nom", (object?)await NomDeposantAsync(geste, codeEve, ct) ?? DBNull.Value));
            }
        }
        await geste.EcrireAsync($"UPDATE v2.EVENEMENT_PROCEDURE SET {string.Join(", ", champs)} WHERE EVNPR_ID_EVNPR = @id", ct, [.. parametres]);
        if (!creerFutur) return null;
        return await FuturLieAsync(geste, idInstance, code, jour, numeroSource, ct) is { } futur ? (futur.Id, futur.Evenement.Code) : null;
    }

    /// <summary>
    /// L'evenement FUTUR LIE que le code <paramref name="code"/> d'un survenu porte (creationEvenementFutur, en-tete) : ses dates
    /// depuis <paramref name="depuis"/>, lie au numero source du survenu ; null si le code n'en porte pas ou qu'aucune date ne se calcule.
    /// </summary>
    private static async Task<(int Id, EditionsEvenementV2.Evenement Evenement)?> FuturLieAsync(GesteEnCours geste, int idInstance, Code? code,
                                                                                                DateTime depuis, string numeroSource, CancellationToken ct)
    {
        if (code?.CodeFutur is not { } codeFutur) return null;
        // AddEvenementProcedureFuturOuExpire : aucune date -> rien ; plusieurs -> fixe, sinon maxi, sinon mini.
        var fixe = Delai(depuis, code.MoisFixe, code.JoursFixe, code.SemainesFixe);
        var maxi = Delai(depuis, code.MoisMaxi, code.JoursMaxi, code.SemainesMaxi);
        var mini = Delai(depuis, code.MoisMini, code.JoursMini, code.SemainesMini);
        if (fixe is null && maxi is null && mini is null) return null;
        if (fixe is not null) (maxi, mini) = (null, null);
        else if (maxi is not null) mini = null;
        var numeroFutur = Guid.NewGuid().ToString().ToUpperInvariant();
        var (idFutur, rangFutur, pieceFutur) = await InsererAsync(geste, idInstance, codeFutur, await LireCodeAsync(geste, codeFutur, false, ct),
            new Dictionary<string, object?>
            {
                ["EVNPR_SURVENU"] = false, ["EVNPR_NUMERO_SOURCE"] = numeroFutur,
                ["EVNPR_DATE_FIXE"] = fixe, ["EVNPR_DATE_MAXI"] = maxi, ["EVNPR_DATE_MINI"] = mini, ["EVNPR_NUMERO_SOURCE_EP_LIE"] = numeroSource,
            }, ct);
        // Le futur n'a pas de date (EVNPR_DATE) : son libelle source n'en porte pas (EditionLibelleSource, EvenementProcedure.cs:468-475).
        return (idFutur, new EditionsEvenementV2.Evenement(codeFutur, numeroFutur, null, rangFutur, pieceFutur));
    }

    /// <summary>Le nom de deposant GED du code (R_CODEEVEPRO_NOM_DEPOSANT_GED, GererPiecesNum :2147) ; null sans la colonne.</summary>
    private static async Task<string?> NomDeposantAsync(GesteEnCours geste, string codeEve, CancellationToken ct)
    {
        if (!(await EditionsV2.ColonnesAsync(geste, "v2.CODE_EVE_PRO", ct)).Contains("R_CODEEVEPRO_NOM_DEPOSANT_GED")) return null;
        await using var r = await geste.LireAsync("SELECT R_CODEEVEPRO_NOM_DEPOSANT_GED FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                                                  new SqlParameter("@c", codeEve));
        return await r.ReadAsync(ct) && FormeLigne.Texte(r, "R_CODEEVEPRO_NOM_DEPOSANT_GED") is { Length: > 0 } nom ? nom : null;
    }

    /// <summary>
    /// La FACTURE de l'evenement <paramref name="codeEve"/> d'une instance (EvenementProcedure.CreateArticles, EvenementProcedure.cs:
    /// 2451-2492) : ses articles CEP_ARTICLE de type « I » (listTypeEP d'une instance, :2388-2400), dont la condition est remplie
    /// (AnalyseCondition, la meme lecture que celle des editions : EditionsEvenementV2.Condition) et dont l'article existe, actif, a la
    /// date du geste (Article.GetArticle, RigMetier\Facture\Article.cs:185-199). RIG v2 les facture par son tableau de facturation
    /// dans la meme sauvegarde que le geste (DoGenererLesArticles, PROC_MJUDS.cs:744-747 ; l'annuler annule tout, :774-776). La V3 n'a
    /// pas de moteur de facturation : rend le refus du geste (<paramref name="objet"/> : « le renvoi »...), null s'il n'y a rien a facturer.
    /// Mesure du 27/11/2026 (RIG_DEV) : 21 des 250 codes de renvoi utilisables en saisie portent un article (RINT -> JGTJR...).
    /// </summary>
    internal static async Task<string?> FactureNonRepriseAsync(GesteEnCours geste, EditionsEvenementV2.Instance inst, string codeEve, string objet,
                                                               CancellationToken ct)
    {
        var articles = new List<(string Article, string Condition)>();
        await using (var r = await geste.LireAsync("""
            SELECT CEPAR_CODE_ARTICLE, CEPAR_CONDITION FROM v2.CEP_ARTICLE WHERE CEPAR_CODE_EVE_PRO = @c AND CEPAR_TYPE_EP = 'I'
            ORDER BY CEPAR_NUM_ORDRE, CEPAR_ID_CEPAR
            """, ct, new SqlParameter("@c", codeEve)))
            while (await r.ReadAsync(ct)) articles.Add((FormeLigne.Texte(r, "CEPAR_CODE_ARTICLE"), FormeLigne.Texte(r, "CEPAR_CONDITION")));
        string Refus(string quoi) => $"{quoi} n'est pas reprise par RIG v3 : rien n'est enregistré, faire {objet} dans RIG v2.";
        foreach (var (article, condition) in articles)
        {
            if (EditionsEvenementV2.Condition(condition, inst) is not { } remplie)
                return Refus($"La condition « {condition.Trim()} » de l'article {article} de l'événement {codeEve}");
            if (!remplie) continue;
            await using var a = await geste.LireAsync("""
                SELECT COUNT(*) FROM v2.ARTICLE WHERE ARTCL_CODE_ARTICLE_RIG = @a AND ARTCL_DATE_APPLICATION <= @maintenant AND ARTCL_STATUT = 'A'
                """, ct, new SqlParameter("@a", article), new SqlParameter("@maintenant", geste.Maintenant));
            await a.ReadAsync(ct);
            if (a.GetInt32(0) > 0) return Refus($"La facture de l'article {article} de l'événement {codeEve}");
        }
        return null;
    }

    /// <summary>Un delai du code (Outils.cs:495-566) : les mois, sinon les jours, sinon les semaines x 7 ; null sans aucun.</summary>
    internal static DateTime? Delai(DateTime depuis, int? mois, int? jours, int? semaines)
        => mois is { } m ? depuis.AddMonths(m) : jours is { } j ? depuis.AddDays(j) : semaines is { } s ? depuis.AddDays(s * 7) : null;

    /// <summary>Un code d'evenement : ce que CreateEvenementProcedure en lit, et, pour un survenu dont on calcule le futur, son code
    /// futur et ses delais.</summary>
    private sealed record Code(string? Type, bool Piece, string? Nature, string? Deposant, string? CodeFutur,
                               int? MoisFixe, int? JoursFixe, int? SemainesFixe, int? MoisMaxi, int? JoursMaxi, int? SemainesMaxi,
                               int? MoisMini, int? JoursMini, int? SemainesMini);

    /// <summary>Le code tel que RIG v2 le lit ; null s'il n'est pas dans la base (CreateEvenementProcedure laisse alors le type vide,
    /// et creationEvenementFutur ne cree rien, EvenementProcedure.cs:1770-1771). Les colonnes du futur ne se lisent que si
    /// <paramref name="avecFutur"/>.</summary>
    private static async Task<Code?> LireCodeAsync(GesteEnCours geste, string codeEve, bool avecFutur, CancellationToken ct)
    {
        var futur = avecFutur
            ? """
              , R_CODEEVEPRO_CODE_EVE_PRO_FUTUR, R_CODEEVEPRO_MOIS_DATE_FIXE, R_CODEEVEPRO_JOURS_DATE_FIXE, R_CODEEVEPRO_SEMAINES_DATE_FIXE,
              R_CODEEVEPRO_MOIS_DATE_MAXI, R_CODEEVEPRO_JOURS_DATE_MAXI, R_CODEEVEPRO_SEMAINES_DATE_MAXI,
              R_CODEEVEPRO_MOIS_DATE_MINI, R_CODEEVEPRO_JOURS_DATE_MINI, R_CODEEVEPRO_SEMAINES_DATE_MINI
              """
            : "";
        await using var r = await geste.LireAsync($"""
            SELECT R_CODEEVEPRO_TYPE_EVE_PRO, CONVERT(bit, ISNULL(R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES, 0)) AS Piece,
                   R_CODEEVEPRO_NATURE_DOC_GED, R_CODEEVEPRO_TYPE_DEPOSANT_GED {futur}
            FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c
            """, ct, new SqlParameter("@c", codeEve));
        if (!await r.ReadAsync(ct)) return null;
        string? Texte(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
        int? Nombre(string c) => avecFutur && r[c] is not DBNull ? Convert.ToInt32(r[c], CultureInfo.InvariantCulture) : null;
        return new Code(Texte("R_CODEEVEPRO_TYPE_EVE_PRO") is { } t ? t[..1] : null, r["Piece"] is true, Texte("R_CODEEVEPRO_NATURE_DOC_GED"),
            Texte("R_CODEEVEPRO_TYPE_DEPOSANT_GED"), avecFutur ? Texte("R_CODEEVEPRO_CODE_EVE_PRO_FUTUR") : null,
            Nombre("R_CODEEVEPRO_MOIS_DATE_FIXE"), Nombre("R_CODEEVEPRO_JOURS_DATE_FIXE"), Nombre("R_CODEEVEPRO_SEMAINES_DATE_FIXE"),
            Nombre("R_CODEEVEPRO_MOIS_DATE_MAXI"), Nombre("R_CODEEVEPRO_JOURS_DATE_MAXI"), Nombre("R_CODEEVEPRO_SEMAINES_DATE_MAXI"),
            Nombre("R_CODEEVEPRO_MOIS_DATE_MINI"), Nombre("R_CODEEVEPRO_JOURS_DATE_MINI"), Nombre("R_CODEEVEPRO_SEMAINES_DATE_MINI"));
    }

    /// <summary>Ajoute l'evenement a l'instance : non expire, numero d'ordre suivant, type et piece numerisee du code
    /// (CreateEvenementProcedure), plus <paramref name="valeurs"/> ; rend son id, son rang et sa piece numerisee.</summary>
    private static async Task<(int Id, short Rang, bool Piece)> InsererAsync(GesteEnCours geste, int idInstance, string codeEve, Code? code, Dictionary<string, object?> valeurs,
                                                CancellationToken ct)
    {
        int rang;
        await using (var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_INSTN = @i", ct, new SqlParameter("@i", idInstance)))
        {
            await r.ReadAsync(ct);
            rang = r.GetInt32(0) + 1;
        }
        var piece = code?.Piece == true;
        valeurs["EVNPR_ID_INSTN"] = idInstance;
        valeurs["EVNPR_CODE_EVE_PRO"] = codeEve;
        valeurs["EVNPR_DELAI_EXPIRE"] = false;
        valeurs["EVNPR_TYPE_EVE_PRO"] = code?.Type;
        valeurs["EVNPR_NUM_ORDRE"] = (short)rang;
        valeurs["EVNPR_AVEC_PIECE_NUMERISE"] = piece;
        valeurs["EVNPR_NATURE_DOC_GED"] = piece ? code!.Nature : null;
        valeurs["EVNPR_TYPE_DEPOSANT_GED"] = piece ? code!.Deposant : null;
        return (await CreationV2.InsererAsync(geste, "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", valeurs, ct), (short)rang, piece);
    }
}
