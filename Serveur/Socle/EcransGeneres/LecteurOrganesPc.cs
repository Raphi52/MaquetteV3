using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Changer les organes » (PROC_CHORGANESENMASSE, dans l'ecran reuni « Modifier un lot de PC ») : la recherche des procedures
/// collectives par juge-commissaire de RIG v2, en LECTURE (forme Consultation). Mode d'emploi : LecteurEcran.cs. Plan « RIG v3
/// 100 % fonctionnel », lot 4, 26/11/2026.
///
/// SOURCE (ancien RIG) : le Detail OPE de proc_chorganesenmasse.md fait foi -- malgre son nom, le processus n'ecrit RIEN : « Changer
/// le juge » n'affiche qu'un recapitulatif (OPE_CHANGEMENT.cs:62, aucun UPDATE dans tout le processus), l'impression ne fait rien
/// (OPE_RECHERCHE.cs:138, :146), VALIDER et VERIFIER sont masques (FORM_CHORGANESENMASSE.cs:18-20). Seule la recherche est un
/// metier : d'ou la forme Consultation et aucun ecrivain (aucun geste a reprendre).
///
/// FILTRE (OPE_RECHERCHE.Recherche, OPE_RECHERCHE.cs:65-111) : « PRCLL_CODE_CLOTURE Is Null » (PC non cloturees) et le juge
/// PRINCIPAL en mission (ETJCM_FIN_MISSION = 0) dans la fonction de base -- statut titulaire : 'JCT', 'JC' ; suppleant : 'JCS'
/// (:74-80). Critere secondaire facultatif : le juge SECONDAIRE tient (comparaison « égal », :92) ou ne tient pas (« différent »)
/// la fonction critere ('JCS' pour un titulaire, 'JCT', 'JC' pour un suppleant). ORDRE : « ORDER BY PRCLL_NUM_PC » (:99).
/// RIG v2 exige un juge principal (« Merci de choisir un juge », :67, :103-104) : sans lui, la V3 montre la meme recherche sans le
/// filtre du juge (toutes les PC non cloturees dont un juge titulaire est en mission). Une PC ou le juge tient deux fonctions de
/// base sort deux fois dans RIG v2 (pas de DISTINCT) : une fois ici.
///
/// CRITERES (IRechercheEcran, libelles du descripteur) : « Juge principal » et « Juge secondaire » (le nom, l'abrege ou le prenom du
/// juge, la combo ULT_COMBO_JUGE_Commissaire de RIG v2), « Statut recherché » (« titulaire » par defaut, :26 ; « suppléant »),
/// « Comparaison » (« égal » par defaut, :27 ; « différent »). Le champ de recherche : numero ou nom de la PC.
/// GROUPES : le statut recherche, « Juge titulaire » / « Juge suppléant ». ETAT : « PC non clôturée » (AFaire), le seul que la
/// recherche rend.
/// FICHE (IFicheEcran) : les colonnes de la grille de RIG v2 (OPE_RESULTAT.cs:34-42), en deux sections : « Procédure collective »
/// (numero, nom, code et date de solution, forfait facture) et « Organes » (dbo.ListeJuge JCT et JCS, dbo.ListeMandataireJudiciaire,
/// refaites par OrganesPc).
/// </summary>
internal sealed class LecteurOrganesPc : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_CHORGANESENMASSE";

    internal const string Titulaire = "Juge titulaire", Suppleant = "Juge suppléant";

    internal const string CritereJuge = "Juge principal", CritereStatut = "Statut recherché", CritereSecondaire = "Juge secondaire",
                          CritereComparaison = "Comparaison";

    internal const string SectionPc = "Procédure collective", SectionOrganes = "Organes";

    internal const string ChampNumero = "Numéro de PC", ChampNom = "Nom de l'affaire", ChampSolution = "Code solution",
                          ChampDateSolution = "Date de solution", ChampForfait = "Forfait facturé", ChampJct = "Juge-commissaire titulaire",
                          ChampJcs = "Juge-commissaire suppléant", ChampMandataires = "Mandataire judiciaire";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereJuge, CritereStatut, CritereSecondaire, CritereComparaison];

    private const string FonctionsTitulaire = "('JCT', 'JC')", FonctionsSuppleant = "('JCS')";

    // Le juge d'une combo de RIG v2, retrouve par son nom, son prenom ou son abrege (un texte, ici).
    private const string JugeCherche = "(j.JUG_NOM LIKE {0} OR j.JUG_PRENOM LIKE {0} OR j.JUG_ABREGE LIKE {0})";

    private static string Requete(string fonctionsBase, string filtres) => $"""
        SELECT TOP (@plafond) p.PRCLL_ID_PRCLL AS Id, p.PRCLL_NUM_PC AS NumPc, p.PRCLL_NOM_AFFAIRE AS Nom,
               {OrganesPc.Juges("p", "JCT", justeNom: false)} AS Jct, {OrganesPc.Juges("p", "JCS", justeNom: true)} AS Jcs,
               {OrganesPc.Mandataires("p")} AS Mandataires, COUNT(*) OVER () AS Total
        FROM v2.PROCEDURE_COLLECTIVE AS p
        WHERE p.PRCLL_CODE_CLOTURE IS NULL
          AND EXISTS (SELECT 1 FROM v2.ETRE_JUGE_COMM AS ej JOIN v2.JUGE AS j ON j.JUG_ID_JUG = ej.ETJCM_ID_JUG
                      WHERE ej.ETJCM_ID_PRCLL = p.PRCLL_ID_PRCLL AND ej.ETJCM_FIN_MISSION = 0 AND ej.ETJCM_FONCTION_PC IN {fonctionsBase} /*juge*/)
          {filtres}
        ORDER BY p.PRCLL_NUM_PC, p.PRCLL_ID_PRCLL
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete(FonctionsTitulaire, "").Replace("/*juge*/", ""), r => Ligne(r, Titulaire), ct,
                                  new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Le statut : « S » suppleant, sinon titulaire (OPE_RECHERCHE.cs:74-80) ; la comparaison : « E » egal, sinon different (:92).
        var suppleant = recherche.Critere(CritereStatut) is { } statut && statut.StartsWith("supp", StringComparison.OrdinalIgnoreCase);
        var different = recherche.Critere(CritereComparaison) is { } comparaison && comparaison.StartsWith("diff", StringComparison.OrdinalIgnoreCase);
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        var juge = "";
        if (recherche.Critere(CritereJuge) is { } principal)
        {
            juge = " AND " + string.Format(CultureInfo.InvariantCulture, JugeCherche, "@juge");
            parametres.Add(new SqlParameter("@juge", FormeLigne.Contient(principal)));
        }
        if (recherche.Critere(CritereSecondaire) is { } secondaire)
        {
            filtres.Add($"""
                (SELECT COUNT(*) FROM v2.ETRE_JUGE_COMM AS ec JOIN v2.JUGE AS j ON j.JUG_ID_JUG = ec.ETJCM_ID_JUG
                 WHERE ec.ETJCM_ID_PRCLL = p.PRCLL_ID_PRCLL AND ec.ETJCM_FIN_MISSION = 0
                   AND ec.ETJCM_FONCTION_PC IN {(suppleant ? FonctionsTitulaire : FonctionsSuppleant)}
                   AND {string.Format(CultureInfo.InvariantCulture, JugeCherche, "@secondaire")}) {(different ? "= 0" : "> 0")}
                """);
            parametres.Add(new SqlParameter("@secondaire", FormeLigne.Contient(secondaire)));
        }
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(p.PRCLL_NUM_PC LIKE @texteNumero OR p.PRCLL_NOM_AFFAIRE LIKE @texteNom)");
            parametres.Add(new SqlParameter("@texteNumero", FormeLigne.Contient(recherche.Texte.Replace(" ", "", StringComparison.Ordinal))));
            parametres.Add(new SqlParameter("@texteNom", FormeLigne.Contient(recherche.Texte)));
        }
        var sql = Requete(suppleant ? FonctionsSuppleant : FonctionsTitulaire, string.Concat(filtres.Select(f => " AND " + f))).Replace("/*juge*/", juge);
        return LectureEcran.LireAsync(baseGreffe, sql, r => Ligne(r, suppleant ? Suppleant : Titulaire), ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r, string groupe)
    {
        var numero = FormeLigne.Texte(r, "NumPc");
        var id = FormeLigne.Texte(r, "Id");
        var jct = FormeLigne.Texte(r, "Jct");
        var jcs = FormeLigne.Texte(r, "Jcs");
        var mandataires = FormeLigne.Texte(r, "Mandataires");
        var precisions = FormeLigne.Joindre(numero.Length > 0 ? $"PC {numero}" : null,
            jct.Length > 0 ? $"titulaire {jct}" : null, jcs.Length > 0 ? $"suppléant {jcs}" : null,
            mandataires.Length > 0 ? $"mandataire {mandataires}" : null);
        var nom = FormeLigne.Texte(r, "Nom") is { Length: > 0 } n ? n : $"PC n° {(numero.Length > 0 ? numero : id)}";
        return new LigneLue(nom, precisions, groupe, "AFaire", []) { Id = id };
    }

    private static readonly string Fiche = $"""
        SELECT p.PRCLL_NUM_PC AS NumPc, p.PRCLL_NOM_AFFAIRE AS Nom, p.PRCLL_CODE_SOLUTION AS CodeSolution,
               cs.R_CODESOLUTI_LIBELLE_ECRAN AS Solution, p.PRCLL_DATE_SOLUTION AS DateSolution, p.PRCLL_FORFAIT_FACTURE AS Forfait,
               {OrganesPc.Juges("p", "JCT", justeNom: false)} AS Jct, {OrganesPc.Juges("p", "JCS", justeNom: true)} AS Jcs,
               {OrganesPc.Mandataires("p")} AS Mandataires
        FROM v2.PROCEDURE_COLLECTIVE AS p
        LEFT JOIN v2.CODE_SOLUTION AS cs ON cs.R_CODESOLUTI_VALEUR = p.PRCLL_CODE_SOLUTION
        WHERE p.PRCLL_ID_PRCLL = @id
        """;

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, Fiche, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var code = FormeLigne.Texte(r, "CodeSolution");
        var libelle = FormeLigne.Texte(r, "Solution");
        return new FicheEcran(Procedure, id, FormeLigne.Texte(r, "Nom") is { Length: > 0 } nom ? nom : $"PC n° {FormeLigne.Texte(r, "NumPc")}",
        [
            new SectionFiche(SectionPc,
            [
                new(ChampNumero, FormeLigne.Texte(r, "NumPc")), new(ChampNom, FormeLigne.Texte(r, "Nom")),
                new(ChampSolution, libelle.Length > 0 ? $"{code} - {libelle}" : code),
                new(ChampDateSolution, FormeLigne.DateFiche(FormeLigne.DateDe(r, "DateSolution"))),
                new(ChampForfait, FormeLigne.Case(r["Forfait"] is true)),
            ]),
            new SectionFiche(SectionOrganes,
            [
                new(ChampJct, FormeLigne.Texte(r, "Jct")), new(ChampJcs, FormeLigne.Texte(r, "Jcs")),
                new(ChampMandataires, FormeLigne.Texte(r, "Mandataires")),
            ]),
        ]);
    }
}
