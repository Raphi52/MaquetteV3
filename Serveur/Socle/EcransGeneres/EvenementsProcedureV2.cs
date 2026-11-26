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
/// NON REPRIS (nomme) : le texte de l'evenement (EVNPR_TEXTE, EvenementProcedure.ConstructionTexte) ; le courriel a la hotline quand
/// le code futur a plusieurs delais (IEntiteJudiciaire.cs:170-182 : service externe, la priorite fixe / maxi / mini est appliquee) ;
/// les evenements inter-entites d'une instance liee a une PC ou une prevention (OPE_EVENEMENT.cs:1534-1584 : PROC_MJUDS les cree sur
/// son instance de saisie, qui n'a ni PC ni prevention en lot).
/// </summary>
internal static class EvenementsProcedureV2
{
    /// <summary>Ce qui a ete ecrit : le survenu (id, numero source) et, s'il y en a un, le futur lie (id, code).</summary>
    internal sealed record Ecrits(int IdSurvenu, string NumeroSource, int? IdFutur, string? CodeFutur);

    /// <summary>Ecrit l'evenement SURVENU <paramref name="codeEve"/> sur l'instance, a <paramref name="date"/> ; avec
    /// <paramref name="avecFutur"/>, aussi l'evenement futur lie que son code porte (voir l'en-tete).</summary>
    internal static async Task<Ecrits> SurvenuAsync(GesteEnCours geste, int idInstance, string codeEve, DateTime? date, bool avecFutur,
                                                    CancellationToken ct)
    {
        var jour = date?.Date;
        var numeroSource = Guid.NewGuid().ToString().ToUpperInvariant();
        var code = await LireCodeAsync(geste, codeEve, avecFutur, ct);
        var idSurvenu = await InsererAsync(geste, idInstance, codeEve, code, new Dictionary<string, object?>
        {
            ["EVNPR_DATE"] = jour, ["EVNPR_SURVENU"] = true, ["EVNPR_NUMERO_SOURCE"] = numeroSource,
        }, ct);
        if (!avecFutur || jour is not { } depuis || code?.CodeFutur is not { } codeFutur) return new Ecrits(idSurvenu, numeroSource, null, null);

        // AddEvenementProcedureFuturOuExpire : aucune date -> rien ; plusieurs -> fixe, sinon maxi, sinon mini.
        var fixe = Delai(depuis, code.MoisFixe, code.JoursFixe, code.SemainesFixe);
        var maxi = Delai(depuis, code.MoisMaxi, code.JoursMaxi, code.SemainesMaxi);
        var mini = Delai(depuis, code.MoisMini, code.JoursMini, code.SemainesMini);
        if (fixe is null && maxi is null && mini is null) return new Ecrits(idSurvenu, numeroSource, null, null);
        if (fixe is not null) (maxi, mini) = (null, null);
        else if (maxi is not null) mini = null;
        var idFutur = await InsererAsync(geste, idInstance, codeFutur, await LireCodeAsync(geste, codeFutur, false, ct), new Dictionary<string, object?>
        {
            ["EVNPR_SURVENU"] = false, ["EVNPR_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            ["EVNPR_DATE_FIXE"] = fixe, ["EVNPR_DATE_MAXI"] = maxi, ["EVNPR_DATE_MINI"] = mini, ["EVNPR_NUMERO_SOURCE_EP_LIE"] = numeroSource,
        }, ct);
        return new Ecrits(idSurvenu, numeroSource, idFutur, codeFutur);
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
    /// (CreateEvenementProcedure), plus <paramref name="valeurs"/>.</summary>
    private static async Task<int> InsererAsync(GesteEnCours geste, int idInstance, string codeEve, Code? code, Dictionary<string, object?> valeurs,
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
        return await CreationV2.InsererAsync(geste, "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR", valeurs, ct);
    }
}
