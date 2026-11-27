using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Édition interne d'un Kbis » (PROC_XEX) : le dossier RCS choisi par son numero de gestion, et les Kbis internes deja edites. Mode
/// d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 13, 27/11/2026. Le geste : EcrivainKbisInterne.cs.
///
/// SOURCE (ancien RIG) : PROC_XEX (D:\RigApplication\Source\RIG\DLL\Processus\PROC_XEX ; le Detail OPE de proc_xex.md fait foi) : un
/// processus mince sur l'etape ETP_RCS, recherche du dossier PAR SON NUMERO DE GESTION, sans numero d'identification (MetierAllowNI
/// = false, ETP_RCS.cs:21, proc_xex.md:134), radie admis (MetierTestEntiteRadiee = false). La demande XEX que la V2 valide porte le
/// dossier (DMND_ID_DSRCS), son numero de gestion (DMND_NUM_INDICATIF) et sa designation (DMND_NOM) (PROC_XEX.cs:21-30) : c'est la
/// trace de chaque Kbis interne edite.
/// Dans la base V3 : DOSSIER_RCS = rec.DossierRcs, ENTREPRISE = rec.Entreprise (Serveur/Recopie/FichesV3.cs).
///
/// LA LISTE A L'OUVERTURE : RIG v2 n'a pas de liste (son numero se saisit). La V3 rend les dossiers qui ont deja un Kbis interne
/// (une demande XEX), le plus recent d'abord : c'est l'historique de l'ecran ; le numero saisi cherche, lui, tout dossier.
/// RECHERCHE (IRechercheEcran) : « Numéro de gestion du dossier RCS » (DSSRC_NUM_GESTION, blancs retires), son seul critere ; le
/// champ de recherche cherche aussi le numero.
///
/// UNE LIGNE = un dossier : son nom est son numero de gestion (l'ecran d'une operation sur UN objet le retrouve par ce numero).
/// GROUPE : « Dossiers RCS ». REGLE D'ETAT : aucune demande XEX -> AFaire « À rechercher » ; au moins une -> EnCours « Dossier
/// choisi », pour qu'un dossier deja edite puisse l'etre encore (RIG v2 valide autant de demandes XEX qu'on en edite).
///
/// FICHE (IFicheEcran), Id = DSSRC_ID_DSRCS : « Dossier RCS » (numero, designation) et « Demande » (ce que la demande XEX recevra :
/// designation, numero de gestion, identifiant interne, PROC_XEX.cs:24-27).
/// </summary>
internal sealed class LecteurKbisInterne : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_XEX";

    internal const string Groupe = "Dossiers RCS";
    internal const string CritereNumero = "Numéro de gestion du dossier RCS";
    internal const string SectionDossier = "Dossier RCS", SectionDemande = "Demande";
    internal const string ChampNumero = "Numéro de gestion", ChampDesignation = "Désignation", ChampIdentifiant = "Identifiant interne";

    /// <summary>La designation d'un dossier : la denomination de l'entreprise, sinon le nom et le prenom de la personne.</summary>
    internal const string Designation = "COALESCE(NULLIF(LTRIM(RTRIM(e.Denomination)), ''), LTRIM(RTRIM(CONCAT(e.Patronyme, ' ', e.Prenom))))";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero];

    private const string Requete = $"""
        SELECT TOP (@plafond) s.Id, s.NumeroGestion, s.Siren, s.DateRadiation, {Designation} AS Designation,
               x.NbKbis, x.Dernier, COUNT(*) OVER () AS Total
        FROM rec.DossierRcs AS s
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        OUTER APPLY (SELECT COUNT(*) AS NbKbis, MAX(COALESCE(d.DMND_DATE_VALIDATION, d.DMND_DATE_CREATION)) AS Dernier
                     FROM v2.DEMANDE AS d WHERE d.DMND_ID_DSRCS = s.Id AND d.DMND_CODE_PROSS = 'XEX') AS x
        WHERE /*filtres*/
        ORDER BY x.Dernier DESC, s.NumeroGestion, s.Id
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, "x.NbKbis > 0", plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var numero = recherche.Critere(CritereNumero) ?? recherche.Texte;
        if (string.IsNullOrWhiteSpace(numero)) return LireAsync(baseGreffe, aujourdhui, plafond, ct);
        return Lire(baseGreffe, "s.NumeroGestion = @numero", plafond, ct, new SqlParameter("@numero", CriteresConsultation.Numero(numero)));
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, string filtre, int plafond, CancellationToken ct, params SqlParameter[] p)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", filtre, StringComparison.Ordinal), Ligne, ct,
            [new SqlParameter("@plafond", plafond), .. p]);

    private static LigneLue Ligne(SqlDataReader r)
    {
        var n = CriteresConsultation.EntierDe(r, "NbKbis") ?? 0;
        var radie = FormeLigne.DateDe(r, "DateRadiation") is { } d ? $"radié le {FormeLigne.Date(d)}" : null;
        var historique = n == 0 ? "aucun Kbis interne édité"
            : $"{n} Kbis interne{(n > 1 ? "s" : "")}, le dernier le {FormeLigne.Date(FormeLigne.DateDe(r, "Dernier"))}";
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "Designation"), FormeLigne.Siren(FormeLigne.Texte(r, "Siren")), radie, historique);
        return new LigneLue(FormeLigne.Texte(r, "NumeroGestion"), precisions, Groupe, n == 0 ? "AFaire" : "EnCours", []) { Id = FormeLigne.Texte(r, "Id") };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, $"""
            SELECT s.Id, s.NumeroGestion, {Designation} AS Designation FROM rec.DossierRcs AS s
            LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise WHERE s.Id = @id
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var numero = FormeLigne.Texte(r, "NumeroGestion");
        var designation = FormeLigne.Texte(r, "Designation");
        return new FicheEcran("PROC_XEX", id, numero,
        [
            new SectionFiche(SectionDossier, [new(ChampNumero, numero), new(ChampDesignation, designation)]),
            new SectionFiche(SectionDemande, [new(ChampDesignation, designation), new(ChampNumero, numero), new(ChampIdentifiant, id)]),
        ]);
    }
}
