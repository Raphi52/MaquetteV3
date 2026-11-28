using System.Globalization;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « FIBEN » (PROC_FIBEN) : « Générer » pose le releve de la periode affichee dans la FILE DES EDITIONS -- la transmission FIBEN d'un
/// mois, ou le releve BODACC d'un trimestre. Mode d'emploi : EcrivainEcran.cs ; les lignes : LecteurReleveFiben.cs (Id = « FIBEN|BODACC
/// |premier jour|dernier jour », EditionsV2.IdSurPeriode) ; l'etat et le droit : EditionsEtatsV2.cs (en tete). Plan « RIG v3 complet,
/// mesure par le banc du parcours complet », lot 3, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_FIBEN, deux etapes, un bouton « Générer » chacune :
///   - transmission FIBEN : OPE_CRITERES._ButtonGenererButtonClick -> InitEdition (OPE_CRITERES.cs:34-52) : le mois choisi (combo,
///     le mois precedent par defaut, :30-31), du 1er au dernier jour, puis ReleveFIBEN.LancerEdition (D:\RigApplication\Source\
///     DllRapportCrystal\RigCrystalFactu\ReleveFIBEN.cs:81-108) : titre « MMMM yyyy » (le mois en toutes lettres, :93), etat
///     Expresso_EDI_FIBEN.rpt (:94), une ligne par greffe : NBAVISRCS (avis RCS hors « REGUL »), NBAVISPC (PC crees moins annules),
///     les bornes des fichiers transmis et les tarifs saisis (_insertGreffe, :110-170 ; _CompterGreffe, :181-210) ;
///   - releve BODACC : OPE_CRITERES_BODACC.InitEdition (OPE_CRITERES_BODACC.cs:61-77) : le trimestre et l'annee choisis, puis
///     ReleveBODACC.LancerEdition (ReleveBODACC.cs:115-153) : titre « &lt;1er|2ème|3ème|4ème&gt; trimestre &lt;annee&gt; » (:139 ;
///     _GetLibelleTrimestre, OPE_CRITERES_BODACC.cs:186-197), etat Expresso_ReleveBodacc2.rpt (:142), DEUX lignes par greffe,
///     gratuites et payantes (:219-231), comptees par _CompterAnnonces (:244-300).
/// RIG v2 n'ecrit rien dans la base du greffe : sa table de travail (##EDIFIBEN, ##RELBODACC) est dans tempdb, l'etat s'affiche a
/// l'ecran. Une edition de RIG v3 est une demande dans la file des editions (decision du chef du plan, EditionsV2.SansParametre) :
/// l'etat Crystal de RigCrystalFactu, pose comme les journaux du lot 4 (EditionsEtatsV2.PoserAsync : FUSION_CRYSTAL, processus FIBEN,
/// TitreEtat / NOMRPT / REQUETE de _GetLstParam, ReleveFIBEN.cs:225-230 et ReleveBODACC.cs:310-315).
/// CONTROLES, avant toute ecriture :
///   1. le droit d'executer FIBEN (EditionsEtatsV2.ProcessusAutoriseAsync ; FORM_ACCUEIL.cs:1624, au mot pres) ;
///   2. une periode : RIG v2 genere le mois, ou le trimestre, choisi dans sa combo -- un releve par clic. L'Etat de la V3 envoie les
///      lignes affichees, en un geste (FormeEtat.tsx) ; sans ligne, ou avec plusieurs periodes, rien ne part et l'ecran dit de choisir la periode
///      (message de la V3 : la combo de la V2 ne peut pas etre vide ni multiple) ;
///   3. la ligne est un releve de l'ecran (son Id), sinon refus.
/// REQUETE : la requete de comptage de la V2 sur la periode (ReleveFIBEN._CompterGreffe ; ReleveBODACC._CompterAnnonces), dans la base
/// du greffe, aux noms de table de RIG v2 -- elle relit les volumes edites. NB_LIGNES : les lignes de l'etat pour le greffe (FIBEN 1,
/// BODACC 2) ; TOTAL : les avis (FIBEN : NBAVISRCS + NBAVISPC) ou les annonces (BODACC : A, B, C, P) que l'etat compte, comme l'ecran.
/// NON REPRIS (nommes) : les autres greffes en exploitation (la V2 compte chaque base de COMMUN_RIG.GREFFE, OPE_CRITERES.cs:55-56 ;
/// la base V3 n'a que SON greffe, comme LecteurReleveFiben) ; les tarifs saisis (4 et 6), que la V3 n'a pas a l'ecran -- ni leurs
/// montants ; CodeGreffe et le pied de page du greffe, relus du greffe par le generateur (EditionsEtatsV2, en tete).
/// TRACE : famille « demande », « a cree », le releve pose (titre, etat Crystal, volumes).
/// </summary>
internal sealed class EcrivainReleveFiben : IEcrivainEcran
{
    public string Procedure => "PROC_FIBEN";
    public string Ecran => "FIBEN";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Generer = "Générer", Processus = "FIBEN";

    internal const string SansPeriode = "Choisissez la période à générer : RIG v2 génère un relevé à la fois, le mois de la transmission FIBEN ou le trimestre du relevé BODACC.";
    internal const string LigneInconnue = "Une ligne n'est pas un relevé de l'écran FIBEN : relisez les relevés.";

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Generer) return IssueGeste.Refuser($"Geste « {geste.Demande.Nom} » inconnu de l'écran FIBEN : RIG v2 n'a que « {Generer} ».");
        // 1. Le droit d'executer le processus.
        if (!await EditionsEtatsV2.ProcessusAutoriseAsync(geste, Processus, ct)) return IssueGeste.Refuser(EditionsEtatsV2.NonAutorise(Processus));
        // 2-3. Un releve, une periode.
        var lues = geste.Demande.Ids.Distinct(StringComparer.Ordinal).Select(EditionsV2.LirePeriode).ToArray();
        if (lues.Length != 1) return IssueGeste.Refuser(SansPeriode);
        // Un releve de l'ecran : un mois entier (FIBEN) ou un trimestre civil entier (BODACC), du 1er au dernier jour.
        // fix-ok: pas un correctif -- ecrivain neuf du lot 3 (plan-f4fb097f), mesure manquant le 28/11/2026 par le banc ParcoursCompletCheck
        // (PROC_FIBEN « aucun ecrivain (Générer) ») ; editions successives = bornes de date reprises au texte de la V2 (DBObject.cs:541-546).
        if (lues[0] is not { } releve || releve.Debut.Day != 1
            || releve.Cle switch
               {
                   LecteurReleveFiben.Fiben => releve.Fin != releve.Debut.AddMonths(1).AddDays(-1),
                   LecteurReleveFiben.Bodacc => (releve.Debut.Month - 1) % 3 != 0 || releve.Fin != releve.Debut.AddMonths(3).AddDays(-1),
                   _ => true,
               })
            return IssueGeste.Refuser(LigneInconnue);

        var (debut, fin) = (releve.Debut, releve.Fin);
        var etat = releve.Cle == LecteurReleveFiben.Fiben ? await FibenAsync(geste, debut, fin, ct) : await BodaccAsync(geste, debut, fin, ct);
        var id = await EditionsEtatsV2.PoserAsync(geste, etat.Etat, ct);

        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        var trace = new TraceGeste("demande", $"l'édition « {etat.Etat.Titre} »", idTexte, "a cree", null,
            new { Etat = etat.Etat.NomRpt, Volumes = etat.Volumes, File = "file des éditions" });
        return IssueGeste.Ecrit($"Relevé « {etat.Etat.Titre} » ({etat.Volumes}) : placé dans la file des éditions.", [idTexte], trace);
    }

    // La transmission FIBEN d'un mois : les deux requetes de ReleveFIBEN._CompterGreffe (ReleveFIBEN.cs:181-210), sur la periode.
    private static async Task<(EditionsEtatsV2.Etat Etat, string Volumes)> FibenAsync(GesteEnCours geste, DateTime debut, DateTime fin, CancellationToken ct)
    {
        const string Rcs = """
            SELECT COUNT(CASE WHEN DBDRC_AVIS NOT LIKE '%  REGUL  %' THEN 1 END) AS NbAvis,
                   ISNULL(MIN(CASE WHEN DBDRC_ID_TRANSMISSION > 0 THEN DBDRC_ID_TRANSMISSION END), 0) AS First,
                   ISNULL(MAX(CASE WHEN DBDRC_ID_TRANSMISSION > 0 THEN DBDRC_ID_TRANSMISSION END), 0) AS Last
            FROM {0}EDI_BDF_RCS WHERE DBDRC_DATE_ENVOI >= {1} AND DBDRC_DATE_ENVOI < {2}
            """;
        const string Pc = """
            SELECT COUNT(CASE WHEN EDIBDF_CODE_ACTION = 'C' THEN 1 END) AS NbCrees, COUNT(CASE WHEN EDIBDF_CODE_ACTION = 'A' THEN 1 END) AS NbAnnules,
                   ISNULL(MIN(CASE WHEN EDIBDF_CODE_ACTION = 'C' AND EDIBDF_NUMFICHIER > 0 THEN EDIBDF_NUMFICHIER END), 0) AS First,
                   ISNULL(MAX(CASE WHEN EDIBDF_CODE_ACTION = 'C' AND EDIBDF_NUMFICHIER > 0 THEN EDIBDF_NUMFICHIER END), 0) AS Last
            FROM {0}EDI_BDF WHERE EDIBDF_DATE_INSERTION_DANS_FICHIER >= {1} AND EDIBDF_DATE_INSERTION_DANS_FICHIER < {2}
            """;
        int avisRcs, crees, annules;
        await using (var r = await geste.LireAsync(string.Format(CultureInfo.InvariantCulture, Rcs, "v2.", Heure00(debut), Heure24(fin)), ct))
            avisRcs = await r.ReadAsync(ct) ? r.GetInt32(0) : 0;
        await using (var r = await geste.LireAsync(string.Format(CultureInfo.InvariantCulture, Pc, "v2.", Heure00(debut), Heure24(fin)), ct))
            (crees, annules) = await r.ReadAsync(ct) ? (r.GetInt32(0), r.GetInt32(1)) : (0, 0);
        var avisPc = crees - annules;   // NbPC = cpt.NbPcCrees - cpt.NbPcAnnules (ReleveFIBEN.cs:133)

        var titre = debut.ToString("MMMM yyyy", Francais);   // param.DateDebut.ToString("MMMM yyyy") (ReleveFIBEN.cs:93)
        var requete = string.Format(CultureInfo.InvariantCulture, Rcs, "", Heure00(debut), Heure24(fin)) + Environment.NewLine
                      + string.Format(CultureInfo.InvariantCulture, Pc, "", Heure00(debut), Heure24(fin));
        var volumes = $"{avisRcs} avis RCS, {avisPc} avis PC";
        return (new EditionsEtatsV2.Etat(Processus, titre, "Expresso_EDI_FIBEN.rpt", requete, 1, avisRcs + avisPc), volumes);
    }

    // Le releve BODACC d'un trimestre : la requete de ReleveBODACC._CompterAnnonces (ReleveBODACC.cs:244-267), sur la periode ; les
    // annonces comptees comme l'ecran (A, B, C, P, LecteurReleveFiben).
    private static async Task<(EditionsEtatsV2.Etat Etat, string Volumes)> BodaccAsync(GesteEnCours geste, DateTime debut, DateTime fin, CancellationToken ct)
    {
        const string Annonces = """
            SELECT UPPER(LEFT(BDCCR_TYPE_BODACC, 1)) AS TYP, CASE WHEN BDCCR_COUT_BODACC = 0 THEN 1 ELSE 0 END AS GRATUIT,
                   CASE WHEN BDCCR_TYPE_ANNONCE = 'C' THEN 0 ELSE 1 END AS RECTIF, CASE WHEN BDCCR_CONTROLE_QUALITE IS NULL THEN 0 ELSE 1 END AS NONREV,
                   COUNT(*) AS NB
            FROM {0}BODACC WHERE BDCCR_DATE_PUBLICITE >= {1} AND BDCCR_DATE_PUBLICITE <= {2}
              AND BDCCR_TYPE_BODACC IS NOT NULL AND BDCCR_COUT_BODACC IS NOT NULL AND BDCCR_TYPE_ANNONCE IS NOT NULL
            GROUP BY UPPER(LEFT(BDCCR_TYPE_BODACC, 1)), CASE WHEN BDCCR_COUT_BODACC = 0 THEN 1 ELSE 0 END,
                     CASE WHEN BDCCR_TYPE_ANNONCE = 'C' THEN 0 ELSE 1 END, CASE WHEN BDCCR_CONTROLE_QUALITE IS NULL THEN 0 ELSE 1 END
            """;
        var parType = new Dictionary<string, int> { ["A"] = 0, ["B"] = 0, ["C"] = 0, ["P"] = 0 };
        await using (var r = await geste.LireAsync($"""
            SELECT UPPER(LEFT(BDCCR_TYPE_BODACC, 1)) AS TYP, COUNT(*) AS NB
            FROM v2.BODACC WHERE BDCCR_DATE_PUBLICITE >= {Heure00(debut)} AND BDCCR_DATE_PUBLICITE <= {Heure24(fin)}
              AND BDCCR_TYPE_BODACC IS NOT NULL AND BDCCR_COUT_BODACC IS NOT NULL AND BDCCR_TYPE_ANNONCE IS NOT NULL
            GROUP BY UPPER(LEFT(BDCCR_TYPE_BODACC, 1))
            """, ct))
        {
            while (await r.ReadAsync(ct))
                if (parType.ContainsKey(r.GetString(0))) parType[r.GetString(0)] = r.GetInt32(1);   // un type sans colonne : ignore (:276-278)
        }
        var trimestre = (debut.Month - 1) / 3 + 1;
        var titre = $"{(trimestre == 1 ? "1er" : $"{trimestre}ème")} trimestre {debut.Year}";
        var requete = string.Format(CultureInfo.InvariantCulture, Annonces, "", Heure00(debut), Heure24(fin));
        var volumes = $"A {parType["A"]}, B {parType["B"]}, C {parType["C"]}, P {parType["P"]}";
        return (new EditionsEtatsV2.Etat(Processus, titre, "Expresso_ReleveBodacc2.rpt", requete, 2, parType.Values.Sum()), volumes);
    }

    // Les bornes de la periode, au texte pres de la V2 (DBDateTime.ToSQL00Heure / ToSQL24Heure, Outils\DLL\RigTools\SQL\DBObject.cs:541-546) :
    // FIBEN « < fin 23:59:59.998 » (ReleveFIBEN.cs:194, :201), BODACC « <= » (ReleveBODACC.cs:263-264). Des dates, jamais une saisie.
    private static string Heure00(DateTime d) => $"convert(DateTime, '{d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} 00:00:00.000', 121)";

    private static string Heure24(DateTime d) => $"convert(DateTime, '{d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} 23:59:59.998', 121)";
}
