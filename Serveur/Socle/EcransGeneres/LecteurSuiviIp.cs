using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Suivi des preventions » (PROC_IPSUIVI) : le suivi d'UNE injonction de payer apres son ordonnance -- les dates des actes
/// qui la suivent (signification, opposition, accuse de reception, provision, certificat de non-opposition). Mode d'emploi des
/// lecteurs : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// FORME : FICHE, pas File (decision du chef, « formes a revoir » : proc_ipsuivi.md:173). Le « Detail OPE » de la fiche fait
/// foi : IPSUIVI est « Injonction de Payer -- SUIVI », l'ecran de suivi post-decision d'UNE instance de type IP
/// (proc_ipsuivi.md:173) ; elle s'ouvre par son numero (parametre NumRole, PROC_IPSUIVI.cs:20-26, ou le champ « N° IP »,
/// ETP_IPSUIVI.cs:14-15). La partie haute de la fiche (dix etats NonTraite... Transfere, tables IP_SUIVI) ne correspond pas
/// au code (proc_ipsuivi.md:165-171) : elle n'est pas reprise. Le titre du menu reste celui que Raphael a valide.
///
/// LIGNES : les instances de type IP (InstanceIP_EnCours n'accepte qu'une IP, OPE_IPSUIVI.cs:135-143), la plus recente en
/// tete ; Id = INSTN_ID_INSTN, la cle de la fiche. Recherche : le « N° IP » (le numero de role), et le texte sur le numero ou
/// le nom courant. Dans la base V3, INSTANCE, EVENEMENT_PROCEDURE, DECISION_JUDICIAIRE et PARTIE sont les tables V2 (v2.*).
///
/// LA FICHE (OPE_IPSUIVI.cs:845-945, le chargement de l'IP) :
///   « Date IP » : la 1re decision judiciaire, sa date sinon sa date prevue (l. 849-858) ;
///   « Demandeur », « Defendeur » : la 1re partie de qualite D / F (_ChargementPartie ; PRT_NOM tronque a 30 dans le recap
///   V2, l. 532-541 ; ici la designation entiere) ;
///   « Signification » et « A personne » : le dernier evenement SURVENU ISIGNI/IPSIGNIF (EVNPR_DATE, EVNPR_SIGNIF_A_PERSONNE),
///   sinon le dernier EXPIRE (sa date maxi, fixe ou mini : « la date affichee est la date d'expiration », l. 885-898) ;
///   « Opposition » : survenu IOIP/IPOPOS, sinon expire (l. 900-912) ; « Reception LRAR » : survenu IAROIP/IPAROPOS
///   (l. 914-920) ; « Provision » : survenu ICONSOIP/IPCONSOPOS, sinon expire (l. 921-931) ; « Certificat de non-opposition » :
///   survenu IPCERTNOPP (l. 932-944 ; la V2 ne montre le champ que pour une IP « nouvelle loi » acceptee -- ici toujours, vide
///   sans evenement). Codes : EvenementProcedure.typeEvenementProToBD (EvenementProcedure.cs:585-604). La V2 avertit quand
///   deux evenements survenus du meme type existent (l. 881-882...) ; la V3 prend le dernier.
///
/// GROUPE : « Injonctions de payer » (la V2 n'en a pas). REGLE D'ETAT (les evenements survenus de l'IP, memes codes) :
///   un evenement qui clot l'IP -- fin (ICAUSEFIN, IDESIN, IPDESIN), executoire (IEXECU, IPEXECUT%), non avenue (INAORDO,
///   IPNAV, IPCNAV%), caducite (ICADUORDO, IPCAD, IPCCAD%) -> Validee « Close » ;
///   sinon une signification survenue                 -> EnCours « Signifiee » ;
///   sinon                                            -> AFaire  « A signifier ».
///
/// GESTES : la saisie des dates N'EST PAS BRANCHEE (pas d'ecrivain, la fiche reste en lecture). Valider, dans la V2, fait
/// survenir ou expirer des evenements de procedure, cree au besoin une instance J ou OP numerotee, facture (OPE_FACTU_GLOBAL)
/// et edite (proc_ipsuivi.md, Detail OPE §4-§7) : un lot a lui seul.
/// </summary>
internal sealed class LecteurSuiviIp : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_IPSUIVI";

    /// <summary>Le groupe, les sections et les champs du descripteur, au mot pres.</summary>
    internal const string Groupe = "Injonctions de payer";
    internal const string SectionIp = "Injonction de payer", SectionActes = "Actes après l'ordonnance";
    internal const string CritereNumero = "N° IP";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero];

    // Le dernier evenement survenu (ou expire) d'une liste de codes, pour l'instance i.
    private static string Dernier(string codes, string colonne, string etat) => $"""
        (SELECT TOP (1) {colonne} FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
         AND {etat} AND e.EVNPR_CODE_EVE_PRO IN ({codes}) ORDER BY e.EVNPR_NUM_ORDRE DESC, e.EVNPR_ID_EVNPR DESC)
        """;

    private const string Survenu = "e.EVNPR_SURVENU = 1", Expire = "e.EVNPR_DELAI_EXPIRE = 1";
    private const string DateExpiree = "COALESCE(e.EVNPR_DATE_MAXI, e.EVNPR_DATE_MINI, e.EVNPR_DATE_FIXE)";
    private const string Signif = "'ISIGNI','IPSIGNIF'", Oppos = "'IOIP','IPOPOS'", Ar = "'IAROIP','IPAROPOS'",
                         Conso = "'ICONSOIP','IPCONSOPOS'", Certif = "'IPCERTNOPP'";

    private static readonly string Colonnes = $"""
        i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT,
        (SELECT TOP (1) COALESCE(dj.DCJDC_DATE, dj.DCJDC_DATE_PREVUE) FROM v2.DECISION_JUDICIAIRE AS dj
         WHERE dj.DCJDC_ID_INSTN = i.INSTN_ID_INSTN ORDER BY dj.DCJDC_NUM_ORDRE, dj.DCJDC_ID_DCJDC) AS DateIp,
        (SELECT TOP (1) COALESCE(NULLIF(p.PRT_DESIGNATION, ''), p.PRT_NOM) FROM v2.PARTIE AS p
         WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE LIKE 'D%' ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT) AS Demandeur,
        (SELECT TOP (1) COALESCE(NULLIF(p.PRT_DESIGNATION, ''), p.PRT_NOM) FROM v2.PARTIE AS p
         WHERE p.PRT_ID_INSTN = i.INSTN_ID_INSTN AND p.PRT_QUALIF_PARTIE LIKE 'F%' ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT) AS Defendeur,
        COALESCE({Dernier(Signif, "e.EVNPR_DATE", Survenu)}, {Dernier(Signif, DateExpiree, Expire)}) AS Signification,
        COALESCE({Dernier(Signif, "e.EVNPR_SIGNIF_A_PERSONNE", Survenu)}, {Dernier(Signif, "e.EVNPR_SIGNIF_A_PERSONNE", Expire)}) AS APersonne,
        COALESCE({Dernier(Oppos, "e.EVNPR_DATE", Survenu)}, {Dernier(Oppos, DateExpiree, Expire)}) AS Opposition,
        {Dernier(Ar, "e.EVNPR_DATE", Survenu)} AS ReceptionLrar,
        COALESCE({Dernier(Conso, "e.EVNPR_DATE", Survenu)}, {Dernier(Conso, DateExpiree, Expire)}) AS Provision,
        {Dernier(Certif, "e.EVNPR_DATE", Survenu)} AS Certificat,
        CASE WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN AND e.EVNPR_SURVENU = 1
                          AND (e.EVNPR_CODE_EVE_PRO IN ('ICAUSEFIN','IDESIN','IPDESIN','IEXECU','INAORDO','IPNAV','ICADUORDO','IPCAD')
                               OR e.EVNPR_CODE_EVE_PRO LIKE 'IPEXECUT%' OR e.EVNPR_CODE_EVE_PRO LIKE 'IPCNAV%'
                               OR e.EVNPR_CODE_EVE_PRO LIKE 'IPCCAD%')) THEN 'Validee'
             WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE AS e WHERE e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN AND e.EVNPR_SURVENU = 1
                          AND e.EVNPR_CODE_EVE_PRO IN ({Signif})) THEN 'EnCours'
             ELSE 'AFaire' END AS Etat
        """;

    private static readonly string Requete = $"""
        SELECT TOP (@plafond) {Colonnes}, COUNT(*) OVER () AS Total
        FROM v2.INSTANCE AS i
        WHERE i.INSTN_TYPE_INSTANCE = 'IP' /*recherche*/
        ORDER BY i.INSTN_ID_INSTN DESC
        """;

    private static readonly string UneIp = $"""
        SELECT {Colonnes} FROM v2.INSTANCE AS i WHERE i.INSTN_TYPE_INSTANCE = 'IP' AND i.INSTN_ID_INSTN = @id
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", "", StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        // Le texte : le numero de role ou le nom courant ; le « N° IP » : le numero de role (blancs retires, comme la saisie V2).
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereNumero) is { } numero)
        {
            filtres.Add("i.INSTN_NUM_ROLE LIKE @numero");
            parametres.Add(new SqlParameter("@numero", FormeLigne.Contient(numero.Replace(" ", "", StringComparison.Ordinal))));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres), StringComparison.Ordinal);
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, UneIp, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var ligne = Ligne(r);
        string D(string colonne) => FormeLigne.DateFiche(FormeLigne.DateDe(r, colonne));
        return new FicheEcran(Procedure, id, ligne.Nom,
        [
            new SectionFiche(SectionIp,
            [
                new ChampFiche(CritereNumero, FormeLigne.Texte(r, "INSTN_NUM_ROLE")), new ChampFiche("Date IP", D("DateIp")),
                new ChampFiche("Demandeur", FormeLigne.Texte(r, "Demandeur")), new ChampFiche("Défendeur", FormeLigne.Texte(r, "Defendeur")),
            ]),
            new SectionFiche(SectionActes,
            [
                new ChampFiche("Signification", D("Signification")), new ChampFiche("À personne", FormeLigne.Case(r["APersonne"] as bool?)),
                new ChampFiche("Opposition", D("Opposition")), new ChampFiche("Réception LRAR", D("ReceptionLrar")),
                new ChampFiche("Provision", D("Provision")), new ChampFiche("Certificat de non-opposition", D("Certificat")),
            ]),
        ]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        string demandeur = FormeLigne.Texte(r, "Demandeur"), defendeur = FormeLigne.Texte(r, "Defendeur");
        var parties = demandeur.Length > 0 && defendeur.Length > 0 ? $"{demandeur} c/ {defendeur}" : FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        var nom = FormeLigne.Joindre($"IP {FormeLigne.Texte(r, "INSTN_NUM_ROLE")}", parties);
        DateTime? date(string c) => FormeLigne.DateDe(r, c);
        var precisions = FormeLigne.Joindre(
            date("DateIp") is { } d ? $"ordonnance du {FormeLigne.Date(d)}" : null,
            date("Signification") is { } s ? $"signification {FormeLigne.Date(s)}" : null,
            date("Opposition") is { } o ? $"opposition {FormeLigne.Date(o)}" : null,
            date("Provision") is { } p ? $"provision {FormeLigne.Date(p)}" : null,
            date("Certificat") is { } c ? $"certificat de non-opposition {FormeLigne.Date(c)}" : null);
        return new LigneLue(nom, precisions, Groupe, FormeLigne.Texte(r, "Etat"), [])
        {
            Id = Convert.ToInt32(r["INSTN_ID_INSTN"], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        };
    }
}
