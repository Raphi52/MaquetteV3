using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les EDITIONS d'un evenement de procedure SURVENU sur une instance, comme RIG v2 les cree a la validation (le courrier du titre
/// executoire, de la non-avenue, du certificat de non-opposition, de l'opposition...). Plan « RIG v3 100 % fonctionnel », lot 8,
/// 27/11/2026 : les injonctions de payer (EcrivainSurveillanceIp, OPE_TRAITEMENTS.cs:822-835) et le suivi des preventions
/// (EcrivainSuiviIp, OPE_IPSUIVI.cs:1770 et suivantes) passent par ici.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\Outils\DLL\RigMetier) : EvenementProcedure.CreateDemandeEdition
/// (JUDICIAIRE\EvenementProcedure.cs:2607-2696, branche INSTANCE : maquettes de type « I »), GetAllEditions (:2801-3170),
/// GetDestinatairesEditions (:3217-3400), GenereDemandeEdition (:3406-3480), AnalyseCondition (:3586-3960) ; les destinataires
/// d'une instance : DestinatairesEditionsLot2.RecupDestEditLot2FROMInstance (JUDICIAIRE\DestinatairesEditionsLot2.cs:1232-1400)
/// et GetSpecifiquesActeursJudiciaires (:171-250) ; les noms : Partie.NomComplet (Partie.cs:329-336), Outils.FormerNomComplet
/// (Fonctions diverses\Outils.cs:663-669) ; un parametre de la liste va dans la colonne DMNED_&lt;nom&gt; (getParametreCs,
/// EvenementProcedure.cs:3554-3580 ; SetParametreOuChamp, Edition\DemandeEditionPourServices.cs:468-510).
///
/// CE QUI S'ECRIT, pour chaque maquette CEP_EDITION du code (type « I ») dont la condition tient :
///   - le parametre d'edition CEPED_CODE_EDITION, avec une liste (PRMED_LISTE) ; ses parametres, surcharges par
///     CEPED_AUTRE_PARAMETRE (« NOM=VALEUR;... ») ; « USER » dans un lot = le compte de l'agent en majuscules ; un lot Amitel (AMI...)
///     cede la place au lot de substitution (PRMED_LOT_SUBSTITUTION) ; un lot absent des lots actifs (v2.LOT, LOT_ACTIF = 1) est vide ;
///   - les destinataires de CEPED_DESTI_MODE_ENVOI (« TYPE[,MODE] » par ligne ; vide = DI ; MODE par defaut LS) : DI = le document
///     interne ; D, F, I = les parties de cette qualite ; PO*, PL* = leur mandataire postulant, plaidant ; AV* = le postulant, sinon
///     la partie. Les modes LS, LR, LRAR et leurs combinaisons (LSLR, LSLRAR, LRLRAR, LSLRLRAR, RMP) rangent chaque destinataire dans
///     la lettre simple, la LR ou la LRAR ;
///   - une ligne v2.DEMANDE_EDITION PAR DESTINATAIRE et par document (mesure sur RIG_DEV le 27/11/2026 : la demande 7077894 porte,
///     pour sa convocation, une ligne « convocation - LRAR » et une ligne d'imprime par destinataire -- PART_D BIAELEC, PART_D SEB
///     AUTO, PART_POD ROUGEOT, PART_F SEB AUTO, DEMANDE_EDITION 4450095 a 4450104), dans l'ordre de RIG v2 : LR (libelle « - LR », lot LOT_LR) et son imprime JLR
///     (« - Imprimé LR La Poste »), puis LRAR (« - LRAR », lot LOT_LRAR) et son imprime JLRAR, puis la lettre simple (lot LOT) ;
///     l'entite source (CODE_ENTITE_SOURCE de la liste : EVT_PROC = le numero source de l'evenement, INSTANCE = celui de
///     l'instance), le destinataire (code PART_..., valeur, designation ; « Document interne » pour DI), le libelle source
///     « CODE - jj/mm/aaaa (EP n° N) », les numeros « role repertoire » ; DMNED_PARAMETRE_GENERATION = HAS_OBSERVATION, MODE_ENVOI
///     et NUM_DEMANDE, comme les lignes de RIG_DEV (DEMANDE_EDITION 2869398 : « HAS_OBSERVATION="0" MODE_ENVOI="LS"
///     NUM_DEMANDE="D2223701138" »).
///   - L'ADRESSE du destinataire (DMNED_BLOC_ADRESSE_DESTINATAIRE) : celle de la partie (PRT_ID_ADRBS) ou de son mandataire
///     (MNDTR_ID_ADRBS), comme RecupDestEditLot2FROMInstance (DestinatairesEditionsLot2.cs:1281-1398 : partie.Adresse,
///     mandatairePostulant.Adresse...) puis EntiteXML.cs:580-581 ; dans la base V3, l'adresse est la fiche rec.Adresse (ADRESSE_BASE,
///     Recopie/FichesV3.cs). Ses lignes sont celles d'AdresseBase.ReturnAdresseCompleteMinouMaj (ADRESSE\AdresseBase.cs:928-986) :
///     type special (« Toque », « Case », « Boîte Greffe », « Boîte Ordre » et « n° » valeur), pre-adresse, « numero complement type
///     de voie nom de voie » (le libelle ecran du type de voie, v2.TYPE_VOIE), « BP boite suite », « code postal localite CEDEX
///     cedex », le pays s'il n'est pas FRA (v2.PAYS) ; separees par « | » comme sur RIG_DEV (DEMANDE_EDITION 4450095 :
///     « 7 Rue Eugène Ravanat|38320 EYBENS », 0x...7C...). Sans adresse, ou pour le document interne, la colonne reste vide.
/// Sans maquette pour le code, aucune edition (RIG v2 rend une liste vide).
///
/// NON REPRIS (rendu dans NonRepris, le message du geste le dit) : une condition autre que NA, NP, AS, IE (opposition devant une
/// juridiction exterieure, VOIE_RECOURS I1408) et, sur une instance sans procedure collective ni prevention, PM, PP, RC, NI, RM, EX,
/// LS, L9 -- l'edition n'est pas creee ; un destinataire autre que les parties et leurs mandataires (dirigeants, destinataires
/// institutionnels, huissier...) et les envois par courriel ou au coffre-fort ; le code-barre d'une piece numerisee (_GetEditionsCodeBarre) ; le decoupage des lots
/// Amitel ; DMNED_MODE_ENVOI : RIG v2 ne le pose que si MODE_ENVOI_LRAR_ACTIF (RigParametre, absent de la base du greffe : ni
/// PARAMETRAGE ni table PARAM_VALEUR_BOOL dans RIG_DEV) -- il reste vide, comme sur les lignes de RIG_DEV.
/// </summary>
internal static class EditionsEvenement
{
    /// <summary>Les editions creees (« libelle -> destinataire ») et ce que la V3 n'a pas pu reprendre pour cet evenement.</summary>
    internal sealed record Bilan(IReadOnlyList<string> Editions, IReadOnlyList<string> NonRepris)
    {
        internal static readonly Bilan Vide = new([], []);
    }

    private sealed record Destinataire(string Code, string Valeur, string Designation, string? Adresse = null);

    private const string DocumentInterne = "Document interne";

    /// <summary>Les editions de l'evenement <paramref name="idEvenement"/> (survenu, sur une instance), rattachees a la demande
    /// <paramref name="idDemande"/> (son numero <paramref name="numeroDemande"/> va dans les parametres de generation).</summary>
    internal static async Task<Bilan> CreerAsync(GesteEnCours geste, int idDemande, string? numeroDemande, int idEvenement, CancellationToken ct)
    {
        if (await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.CEP_EDITION')", ct) is DBNull or null
            || await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.PARAMETRE_EDITION')", ct) is DBNull or null
            || await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.DEMANDE_EDITION')", ct) is DBNull or null)
            return new Bilan([], ["les éditions de l'événement (tables CEP_EDITION, PARAMETRE_EDITION ou DEMANDE_EDITION absentes de la base du greffe)"]);

        var colEve = await RelancesRsurv.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        var colIns = await RelancesRsurv.ColonnesAsync(geste, "v2.INSTANCE", ct);
        string C(HashSet<string> cols, string alias, string col, string type) => cols.Contains(col) ? $"{alias}.{col}" : $"CAST(NULL AS {type})";

        // L'evenement et son instance.
        string code = "", role = "";
        string? source = null, repertoire = null, sourceInstance = null, nature = null, naturePart = null, acte = null;
        int ordre = -1, idInstance = 0;
        DateTime? date = null;
        bool avecPcOuPrevention = false;
        await using (var r = await geste.LireAsync($"""
            SELECT e.EVNPR_CODE_EVE_PRO, {C(colEve, "e", "EVNPR_NUMERO_SOURCE", "varchar(40)")} AS Source, e.EVNPR_NUM_ORDRE, e.EVNPR_DATE, e.EVNPR_ID_INSTN,
                   i.INSTN_NUM_ROLE, {C(colIns, "i", "INSTN_NUM_REP_GENERAL", "varchar(20)")} AS Repertoire,
                   {C(colIns, "i", "INSTN_NUMERO_SOURCE", "varchar(40)")} AS SourceInstance,
                   {C(colIns, "i", "INSTN_NATURE_AFFAIRE", "varchar(10)")} AS Nature, {C(colIns, "i", "INSTN_NAT_AFF_PART", "varchar(10)")} AS NaturePart,
                   {C(colIns, "i", "INSTN_CODE_ACTE_PROCE", "varchar(10)")} AS Acte,
                   CAST(CASE WHEN {C(colIns, "i", "INSTN_ID_PRCLL", "int")} IS NOT NULL OR {C(colIns, "i", "INSTN_ID_PREVE", "int")} IS NOT NULL THEN 1 ELSE 0 END AS bit) AS PcPrev
            FROM v2.EVENEMENT_PROCEDURE AS e JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = e.EVNPR_ID_INSTN
            WHERE e.EVNPR_ID_EVNPR = @id
            """, ct, new SqlParameter("@id", idEvenement)))
        {
            if (!await r.ReadAsync(ct)) return Bilan.Vide;
            code = FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO").Trim();
            source = FormeLigne.Texte(r, "Source") is { Length: > 0 } s ? s : null;
            ordre = r["EVNPR_NUM_ORDRE"] is DBNull ? -1 : Convert.ToInt32(r["EVNPR_NUM_ORDRE"], CultureInfo.InvariantCulture);
            date = FormeLigne.DateDe(r, "EVNPR_DATE");
            idInstance = Convert.ToInt32(r["EVNPR_ID_INSTN"], CultureInfo.InvariantCulture);
            role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
            repertoire = FormeLigne.Texte(r, "Repertoire") is { Length: > 0 } rg ? rg : null;
            sourceInstance = FormeLigne.Texte(r, "SourceInstance") is { Length: > 0 } si ? si : null;
            nature = FormeLigne.Texte(r, "Nature");
            naturePart = FormeLigne.Texte(r, "NaturePart");
            acte = FormeLigne.Texte(r, "Acte");
            avecPcOuPrevention = r["PcPrev"] is true;
        }
        // « SECURITE : un numero source est obligatoire » (CreateDemandeEdition, :2611-2612).
        if (source is null && colEve.Contains("EVNPR_NUMERO_SOURCE"))
        {
            source = Guid.NewGuid().ToString().ToUpperInvariant();
            await geste.EcrireAsync("UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_NUMERO_SOURCE = @s WHERE EVNPR_ID_EVNPR = @id", ct,
                new SqlParameter("@s", source), new SqlParameter("@id", idEvenement));
        }

        // Les maquettes du code, type « I » (instance).
        var colCep = await RelancesRsurv.ColonnesAsync(geste, "v2.CEP_EDITION", ct);
        var maquettes = new List<(string Edition, string Destinataires, string Condition, string Autre)>();
        await using (var r = await geste.LireAsync($"""
            SELECT c.CEPED_CODE_EDITION, c.CEPED_DESTI_MODE_ENVOI, {C(colCep, "c", "CEPED_CONDITION", "varchar(max)")} AS Condition,
                   {C(colCep, "c", "CEPED_AUTRE_PARAMETRE", "varchar(max)")} AS Autre
            FROM v2.CEP_EDITION AS c WHERE c.CEPED_CODE_EVE_PRO = @code AND c.CEPED_TYPE_EP = 'I' ORDER BY c.CEPED_ID_CEPED
            """, ct, new SqlParameter("@code", code)))
            while (await r.ReadAsync(ct))
                maquettes.Add((FormeLigne.Texte(r, "CEPED_CODE_EDITION").Trim(), FormeLigne.Texte(r, "CEPED_DESTI_MODE_ENVOI"),
                               FormeLigne.Texte(r, "Condition"), FormeLigne.Texte(r, "Autre")));
        if (maquettes.Count == 0) return Bilan.Vide;

        var exterieure = await OppositionExterieureAsync(geste, idInstance, ct);
        var entites = await EntitesInstanceAsync(geste, idInstance, ct);
        var lotsActifs = await LotsActifsAsync(geste, ct);
        var colEdition = await RelancesRsurv.ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        var compte = geste.Agent.CompteWindows.ToUpperInvariant();
        var libelleSource = (date is { } d ? $"{code} - {d:dd/MM/yyyy}" : code) + (ordre == -1 ? " (EP sans n° ordre)" : $" (EP n° {ordre})");
        var numeros = repertoire is null ? role : $"{role} {repertoire}";
        var editions = new List<string>();
        var nonRepris = new List<string>();

        foreach (var m in maquettes)
        {
            // La condition (AnalyseCondition).
            if (Condition(m.Condition, nature, naturePart, acte, exterieure, avecPcOuPrevention) is { } condition)
            {
                if (condition.Length > 0) nonRepris.Add($"l'édition {m.Edition} de l'événement {code} (condition « {condition} » non reprise)");
                continue;
            }
            // Le parametre d'edition et sa liste.
            if (await ParametreAsync(geste, m.Edition, ct) is not { } parametre) continue;
            var p = new Dictionary<string, string>(parametre.Liste, StringComparer.OrdinalIgnoreCase);
            foreach (var lot in new[] { "LOT", "LOT_LR", "LOT_ILR", "LOT_LRAR", "LOT_ILRAR" })
                if (p.TryGetValue(lot, out var v) && v.StartsWith("AMI", StringComparison.Ordinal))
                    p[lot] = lot == "LOT" ? parametre.LotSubstitution ?? "" : "";
            foreach (var autre in m.Autre.Split(';'))
                if (autre.Split('=') is [var nom, var valeur] && !valeur.StartsWith("AMI", StringComparison.Ordinal)) p[nom] = valeur;
            string P(string nom) => p.TryGetValue(nom, out var v) ? v : "";
            string Lot(string v) => v.Replace("USER", compte, StringComparison.Ordinal);

            // Les destinataires, rangés par mode d'envoi.
            var (simple, lr, lrar, ignores) = Ranger(m.Destinataires, entites);
            foreach (var ignore in ignores) nonRepris.Add($"le destinataire « {ignore} » de l'édition {m.Edition}");

            var valeurSource = P("CODE_ENTITE_SOURCE") switch
            {
                "EVT_PROC" => source,
                "INSTANCE" => sourceInstance,
                _ => null,
            };
            async Task Ecrire(Dictionary<string, string> liste, string libelle, string lot, string mode, IEnumerable<Destinataire> destinataires,
                              string? valeur)
            {
                foreach (var dest in destinataires)
                {
                    var ligne = RelancesRsurv.ValeursEdition(geste, idDemande);
                    string L(string nom) => liste.TryGetValue(nom, out var v) ? v : "";
                    ligne["DMNED_LIBELLE_DOCUMENT"] = libelle;
                    ligne["DMNED_CODE_ENTITE_SOURCE"] = L("CODE_ENTITE_SOURCE") is { Length: > 0 } ces ? ces : null;
                    ligne["DMNED_VALEUR_ID_SOURCE"] = valeur;
                    ligne["DMNED_CODE_ENTITE_DESTINATAIRE"] = dest.Code.Length > 0 ? dest.Code : null;
                    ligne["DMNED_VALEUR_ID_DESTINATAIRE"] = dest.Valeur.Length > 0 ? dest.Valeur : null;
                    ligne["DMNED_TYPE_DESTINATAIRE"] = "ENTITE";
                    ligne["DMNED_DESIGNATION_DESTINATAIRE"] = dest.Designation;
                    // fix-ok: mesure le 27/11/2026 (controle final du lot 8) -- les editions de la V3 n'avaient pas d'adresse, celles de
                    // RIG_DEV l'ont (DEMANDE_EDITION 4450095 : « 7 Rue Eugène Ravanat|38320 EYBENS ») : le bloc adresse vient de rec.Adresse.
                    ligne["DMNED_BLOC_ADRESSE_DESTINATAIRE"] = dest.Adresse is { Length: > 0 } adresse ? adresse : null;
                    ligne["DMNED_LOT"] = lot.Length == 0 ? null : lotsActifs is null || lotsActifs.Contains(lot) ? lot : "";
                    ligne["DMNED_TYPE_PAPIER"] = L("TYPE_PAPIER") is { Length: > 0 } tp ? tp : "NORMAL";
                    ligne["DMNED_TYPE_GENERATION"] = RelancesRsurv.TypeGeneration(L("TYPE_GENERATION"), "");
                    ligne["DMNED_CODE_MAQUETTE"] = L("CODE_MAQUETTE") is { Length: > 0 } cm ? cm : null;
                    ligne["DMNED_CODE_PROCESSUS"] = L("CODE_PROCESSUS") is { Length: > 0 } cp ? cp : null;
                    ligne["DMNED_TYPE_DOCUMENT"] = L("CODE_DOCUMENT") is { Length: > 0 } cd ? cd : null;
                    ligne["DMNED_NOMBRE_EXEMPLAIRE"] = int.TryParse(L("NOMBRE_EXEMPLAIRES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
                    ligne["DMNED_RECTO_VERSO"] = L("RECTO_VERSO") == "1";
                    ligne["DMNED_IMPRESSION"] = liste.ContainsKey("IMPRESSION") ? L("IMPRESSION") == "1" : true;
                    ligne["DMNED_AFF_AVANT_IMPRESSION"] = L("AFFICHER_AVANT_IMPRESSION") == "1";
                    ligne["DMNED_GROUPAGE_AUTOMATIQUE"] = L("GROUPAGE_AUTOMATIQUE") == "1";
                    ligne["DMNED_READ_ONLY"] = L("READ_ONLY") == "1";
                    ligne["DMNED_IS_NON_SUPPRESSIBLE"] = L("NON_SUPPRESSIBLE") == "1";
                    ligne["DMNED_LIBELLE_SOURCE"] = libelleSource;
                    ligne["DMNED_LES_NUMEROS"] = numeros;
                    ligne["DMNED_PARAMETRE_GENERATION"] = Generation(L("HAS_OBSERVATION") is { Length: > 0 } ho ? ho : "0", mode, numeroDemande);
                    await geste.InsererAsync("v2.DEMANDE_EDITION", "DMNED_ID_DMNED",
                        ligne.Where(c => colEdition.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value), ct);
                    editions.Add($"« {libelle} » → {dest.Designation}");
                }
            }

            var libelleDoc = P("LIBELLE_DOCUMENT") is { Length: > 0 } ld ? ld : parametre.Libelle;
            // LR, puis son imprime (« si l'imprime LR existe », :2950-2975).
            if (lr.Count > 0)
            {
                var maquette = await MaquetteAsync(geste, P("CODE_MAQUETTE"), "_LR", ct);
                var l = new Dictionary<string, string>(p, StringComparer.OrdinalIgnoreCase) { ["CODE_MAQUETTE"] = maquette };
                await Ecrire(l, $"{libelleDoc} - LR", Lot(P("LOT_LR")).ToUpperInvariant(), "LR", lr, valeurSource);
                if (await ParametreAsync(geste, "JLR", ct) is { } jlr)
                {
                    var lotJ = P("LOT_ILR") is { Length: > 0 } ilr ? Lot(ilr) : (jlr.Liste.TryGetValue("LOT", out var lj) ? Lot(lj).ToUpperInvariant() : "");
                    await Ecrire(jlr.Liste, $"{libelleDoc} - Imprimé LR La Poste", lotJ, "LR", lr, valeurSource);
                }
            }
            // LRAR, puis son imprime (:2980-3040) : RIG v2 compose son libelle sur celui de JLRAR.
            if (lrar.Count > 0)
            {
                var maquette = await MaquetteAsync(geste, P("CODE_MAQUETTE"), "_LRAR", ct);
                var l = new Dictionary<string, string>(p, StringComparer.OrdinalIgnoreCase) { ["CODE_MAQUETTE"] = maquette };
                await Ecrire(l, $"{libelleDoc} - LRAR", Lot(P("LOT_LRAR")).ToUpperInvariant(), "LRAR", lrar, valeurSource);
                if (await ParametreAsync(geste, "JLRAR", ct) is { } jlrar)
                {
                    var lotJ = P("LOT_ILRAR") is { Length: > 0 } ilrar ? Lot(ilrar) : (jlrar.Liste.TryGetValue("LOT", out var lj) ? Lot(lj) : "");
                    var libelleJ = jlrar.Liste.TryGetValue("LIBELLE_DOCUMENT", out var lj2) && lj2.Length > 0 ? lj2 : jlrar.Libelle;
                    var valeurJ = jlrar.Liste.TryGetValue("CODE_ENTITE_SOURCE", out var cesJ) && cesJ == "INSTANCE" ? sourceInstance : source;
                    await Ecrire(jlrar.Liste, $"{libelleJ} - Imprimé LR La Poste", lotJ, "LRAR", lrar, valeurJ);
                }
            }
            // La lettre simple (:3060-).
            if (simple.Count > 0)
                await Ecrire(p, libelleDoc, Lot(P("LOT")), "LS", simple, valeurSource);
        }
        if (entites.Count == 0 && editions.Count == 0 && nonRepris.Count == 0) return Bilan.Vide;
        return new Bilan(editions, nonRepris);
    }

    /// <summary>Le message du geste : « N éditions en file : … » et ce que la V3 n'a pas repris ; vide sans maquette.</summary>
    internal static string Message(IReadOnlyCollection<Bilan> bilans)
    {
        var editions = bilans.SelectMany(b => b.Editions).ToList();
        var nonRepris = bilans.SelectMany(b => b.NonRepris).Distinct().ToList();
        var texte = new StringBuilder();
        if (editions.Count > 0)
            texte.Append($" {editions.Count} édition{(editions.Count > 1 ? "s" : "")} en file : {string.Join(" ; ", editions)}.");
        if (nonRepris.Count > 0)
            texte.Append($" Éditions non reprises par la V3 : {string.Join(" ; ", nonRepris)}.");
        return texte.ToString();
    }

    // ------------------------------------------------------------------ LA CONDITION (AnalyseCondition, :3586-3960)

    /// <summary>Null si la condition tient ; « » si elle ne tient pas ; le terme si la V3 ne sait pas l'evaluer.</summary>
    private static string? Condition(string brute, string? nature, string? naturePart, string? acte, bool exterieure, bool avecPcOuPrevention)
    {
        if (string.IsNullOrWhiteSpace(brute)) return null;
        var texte = brute.Replace("+", ";+", StringComparison.Ordinal).Replace("-", ";-", StringComparison.Ordinal).Replace("\r\n", "", StringComparison.Ordinal);
        texte = texte.Length > 0 ? texte[1..] : texte;   // AmiString.Right(texte, longueur - 1)
        var natures = new List<string>();
        var naturesPart = new List<string>();
        var actes = new List<string>();
        foreach (var terme in texte.Split(';').Where(t => t.Length > 0))
        {
            if (terme.Length < 3) return terme;
            var plus = terme[0] == '+';
            var cle = terme.Substring(1, 2).ToUpperInvariant();
            var valeur = terme[3..];
            switch (cle)
            {
                case "NA":
                    if (plus) natures.Add(valeur); else if (nature == valeur) return "";
                    break;
                case "NP":
                    if (plus) naturesPart.Add(valeur); else if (naturePart == valeur) return "";
                    break;
                case "AS":
                    if (plus) actes.Add(valeur); else if (acte == valeur) return "";
                    break;
                case "IE":
                    if (plus != exterieure) return "";
                    break;
                // Sans PC ni prevention, ces criteres de RIG v2 valent faux (isPM, isRCS, isRM, isPCEXT, isLSE, isLSE9 : :3962-4010).
                case "PM" or "RC" or "RM" or "EX" or "LS" or "L9" when !avecPcOuPrevention:
                    if (plus) return "";
                    break;
                case "PP" or "NI" when !avecPcOuPrevention:
                    if (!plus) return "";
                    break;
                default:
                    return terme;
            }
        }
        if (natures.Count > 0 && !natures.Contains(nature ?? "")) return "";
        if (naturesPart.Count > 0 && !naturesPart.Contains(naturePart ?? "")) return "";
        if (actes.Count > 0 && !actes.Contains(acte ?? "")) return "";
        return null;
    }

    /// <summary>IsIPOppositionGreffeExterieur (Instance.cs:5644-5656) : la 1re decision a une voie de recours I1408 (VoieRecoursIPE,
    /// DecisionJudiciaire.cs:15) devant une juridiction exterieure renseignee. RIG v2 l'exige aussi differente du code du greffe
    /// (Common.CodeGreffeRig) : la base V3 du greffe ne porte pas ce code, la comparaison n'est pas faite (une voie de recours vers
    /// son propre greffe n'a pas de sens metier). Sans table VOIE_RECOURS : faux.</summary>
    internal static async Task<bool> OppositionExterieureAsync(GesteEnCours geste, int idInstance, CancellationToken ct)
    {
        var col = await RelancesRsurv.ColonnesAsync(geste, "v2.VOIE_RECOURS", ct);
        if (!col.Contains("VRCRS_ID_DCJDC") || !col.Contains("VRCRS_JURIDICTION_EXTERIEURE")) return false;
        return await RelancesRsurv.Scalaire(geste, SqlExterieure, ct, new SqlParameter("@i", idInstance)) is not (null or DBNull);
    }

    /// <summary>La requete d'<see cref="OppositionExterieureAsync"/> (@i = l'instance), partagee avec la fiche du suivi des preventions
    /// (ult_Juridiction_Exterieure, OPE_IPSUIVI.cs:862-864, la meme regle) ; a ne lancer que si v2.VOIE_RECOURS porte ses colonnes.</summary>
    internal const string SqlExterieure = """
        SELECT TOP (1) 1 FROM v2.VOIE_RECOURS AS v
        WHERE v.VRCRS_ID_DCJDC = (SELECT TOP (1) d.DCJDC_ID_DCJDC FROM v2.DECISION_JUDICIAIRE AS d WHERE d.DCJDC_ID_INSTN = @i
                                  ORDER BY d.DCJDC_NUM_ORDRE, d.DCJDC_ID_DCJDC)
          AND UPPER(v.VRCRS_TYPE_RECOURS) = 'I1408' AND ISNULL(v.VRCRS_JURIDICTION_EXTERIEURE, '') <> ''
        """;

    // ------------------------------------------------------------------ LES DESTINATAIRES

    /// <summary>Les entites des parties de l'instance (RecupDestEditLot2FROMInstance, DestinatairesEditionsLot2.cs:1268-1400) : la
    /// partie (PART_q, si elle a un numero source), son postulant (PART_POq), son plaidant (PART_PLq), le postulant sinon la partie
    /// (PART_AVq) ; q = la 1re lettre de sa qualite.</summary>
    private static async Task<List<Destinataire>> EntitesInstanceAsync(GesteEnCours geste, int idInstance, CancellationToken ct)
    {
        var liste = new List<Destinataire>();
        var colPrt = await RelancesRsurv.ColonnesAsync(geste, "v2.PARTIE", ct);
        if (colPrt.Count == 0) return liste;
        var avecMandataires = await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.MANDATAIRE')", ct) is not (null or DBNull);
        var avecCivilites = await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.CIVILITE')", ct) is not (null or DBNull);
        var colMnd = avecMandataires ? await RelancesRsurv.ColonnesAsync(geste, "v2.MANDATAIRE", ct) : [];
        string Col(string col) => colPrt.Contains(col) ? $"p.{col}" : "CAST(NULL AS varchar(40))";
        string Abrege(string civilite) => avecCivilites
            ? $"(SELECT TOP (1) c.R_CIVILITE_ABREGE_EDIT FROM v2.CIVILITE AS c WHERE c.R_CIVILITE_VALEUR = {civilite})"
            : "CAST(NULL AS varchar(5))";
        string Mandataire(string colonne, string alias) => avecMandataires && colPrt.Contains(colonne) && colMnd.Contains("MNDTR_CIVILITE")
            ? $"""
               LEFT JOIN v2.MANDATAIRE AS {alias} ON {alias}.MNDTR_ID_MNDTR = p.{colonne}
               """
            : "";
        var pos = Mandataire("PRT_ID_MNDTR_POS", "mpo");
        var pla = Mandataire("PRT_ID_MNDTR_PLAI", "mpl");
        string M(string alias, string col, bool present) => present ? $"{alias}.{col}" : "CAST(NULL AS varchar(128))";
        // Les adresses (rec.Adresse, la fiche d'ADRESSE_BASE) : leur id ici, leurs lignes apres la lecture (BlocAdresseAsync).
        var adrMandataire = colMnd.Contains("MNDTR_ID_ADRBS");
        var adresses = new List<(int Rang, int? Id)>();
        await using (var r = await geste.LireAsync($"""
            SELECT p.PRT_QUALIF_PARTIE, {Col("PRT_NUMERO_SOURCE")} AS Source, p.PRT_CIVILITE, p.PRT_DESIGNATION, {Abrege("p.PRT_CIVILITE")} AS AbregeP,
                   {(colPrt.Contains("PRT_ID_ADRBS") ? "p.PRT_ID_ADRBS" : "CAST(NULL AS int)")} AS AdrP,
                   {M("mpo", "MNDTR_ID_MNDTR", pos.Length > 0)} AS IdPo, {M("mpo", "MNDTR_CIVILITE", pos.Length > 0)} AS CivPo, {M("mpo", "MNDTR_NOM", pos.Length > 0)} AS NomPo,
                   {(pos.Length > 0 ? Abrege("mpo.MNDTR_CIVILITE") : "CAST(NULL AS varchar(5))")} AS AbregePo,
                   {(pos.Length > 0 && adrMandataire ? "mpo.MNDTR_ID_ADRBS" : "CAST(NULL AS int)")} AS AdrPo,
                   {M("mpl", "MNDTR_ID_MNDTR", pla.Length > 0)} AS IdPl, {M("mpl", "MNDTR_CIVILITE", pla.Length > 0)} AS CivPl, {M("mpl", "MNDTR_NOM", pla.Length > 0)} AS NomPl,
                   {(pla.Length > 0 ? Abrege("mpl.MNDTR_CIVILITE") : "CAST(NULL AS varchar(5))")} AS AbregePl,
                   {(pla.Length > 0 && adrMandataire ? "mpl.MNDTR_ID_ADRBS" : "CAST(NULL AS int)")} AS AdrPl
            FROM v2.PARTIE AS p {pos} {pla}
            WHERE p.PRT_ID_INSTN = @i ORDER BY p.PRT_NUM_ORDRE, p.PRT_ID_PRT
            """, ct, new SqlParameter("@i", idInstance)))
        while (await r.ReadAsync(ct))
        {
            int? Adr(string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
            void Ajouter(Destinataire d, int? adresse) { adresses.Add((liste.Count, adresse)); liste.Add(d); }
            var q = FormeLigne.Texte(r, "PRT_QUALIF_PARTIE") is { Length: > 0 } qualif ? qualif[..1] : "";
            var sourcePartie = FormeLigne.Texte(r, "Source");
            // Partie.NomComplet : l'abrege d'edition de la civilite, sauf « », PM et ND.
            var civilite = FormeLigne.Texte(r, "PRT_CIVILITE").Trim();
            var abrege = FormeLigne.Texte(r, "AbregeP");
            var nomPartie = (civilite is not ("" or "PM" or "ND") && abrege.Length > 0 ? $"{abrege} {FormeLigne.Texte(r, "PRT_DESIGNATION")}"
                                                                                      : FormeLigne.Texte(r, "PRT_DESIGNATION")).Trim();
            // Outils.FormerNomComplet : l'abrege, sauf PM et ND.
            static string NomMandataire(string civ, string abr, string nom) => (civ.Trim() is "PM" or "ND" ? nom : $"{abr} {nom}").Trim();
            var idPo = FormeLigne.Texte(r, "IdPo");
            var idPl = FormeLigne.Texte(r, "IdPl");
            var nomPo = NomMandataire(FormeLigne.Texte(r, "CivPo"), FormeLigne.Texte(r, "AbregePo"), FormeLigne.Texte(r, "NomPo"));
            if (sourcePartie.Length > 0) Ajouter(new Destinataire($"PART_{q}", sourcePartie, nomPartie), Adr("AdrP"));
            if (idPo.Length > 0) Ajouter(new Destinataire($"PART_PO{q}", idPo, nomPo), Adr("AdrPo"));
            if (idPl.Length > 0)
                Ajouter(new Destinataire($"PART_PL{q}", idPl, NomMandataire(FormeLigne.Texte(r, "CivPl"), FormeLigne.Texte(r, "AbregePl"), FormeLigne.Texte(r, "NomPl"))), Adr("AdrPl"));
            if (idPo.Length > 0) Ajouter(new Destinataire($"PART_AV{q}", idPo, nomPo), Adr("AdrPo"));
            else if (sourcePartie.Length > 0) Ajouter(new Destinataire($"PART_AV{q}", sourcePartie, nomPartie), Adr("AdrP"));
        }
        foreach (var (rang, id) in adresses)
            if (id is { } a && await BlocAdresseAsync(geste, a, ct) is { Length: > 0 } bloc)
                liste[rang] = liste[rang] with { Adresse = bloc };
        return liste;
    }

    /// <summary>
    /// Le bloc adresse d'une edition : AdresseBase.ReturnAdresseCompleteMinouMaj(0, false) (ADRESSE\AdresseBase.cs:928-986), lignes
    /// separees par « | » (AdresseMultiLignesPourEdition, :1087-1092 ; la forme des lignes de RIG_DEV, en tete). Lue dans rec.Adresse ;
    /// le type de voie par son libelle ecran (v2.TYPE_VOIE, _loadTypeVoie), le pays par le sien (v2.PAYS, _loadPays) s'il n'est pas
    /// FRA. Vide si la base n'a pas la fiche, ou si l'adresse est introuvable.
    /// </summary>
    internal static async Task<string> BlocAdresseAsync(GesteEnCours geste, int idAdresse, CancellationToken ct)
    {
        var col = await RelancesRsurv.ColonnesAsync(geste, "rec.Adresse", ct);
        if (!col.Contains("Id") || !col.Contains("NomVoie")) return "";
        var colVoie = await RelancesRsurv.ColonnesAsync(geste, "v2.TYPE_VOIE", ct);
        var colPays = await RelancesRsurv.ColonnesAsync(geste, "v2.PAYS", ct);
        string A(string c) => col.Contains(c) ? $"a.{c}" : "CAST(NULL AS nvarchar(100))";
        var typeVoie = colVoie.Contains("R_TYPEVOIE_LIBELLE_ECRAN") && col.Contains("TypeVoie")
            ? "(SELECT TOP (1) tv.R_TYPEVOIE_LIBELLE_ECRAN FROM v2.TYPE_VOIE AS tv WHERE tv.R_TYPEVOIE_VALEUR = a.TypeVoie)" : "CAST(NULL AS nvarchar(100))";
        var pays = colPays.Contains("R_PAYS_LIBELLE_ECRAN") && col.Contains("Pays")
            ? "(SELECT TOP (1) py.R_PAYS_LIBELLE_ECRAN FROM v2.PAYS AS py WHERE py.R_PAYS_VALEUR = a.Pays)" : "CAST(NULL AS nvarchar(100))";
        await using var r = await geste.LireAsync($"""
            SELECT {A("TypeSpecial")} AS TypeSpecial, {A("ValeurSpecial")} AS ValeurSpecial, {A("PreAdresse")} AS PreAdresse,
                   {A("NumeroVoie")} AS NumeroVoie, {A("ComplementNumero")} AS ComplementNumero, {typeVoie} AS TypeVoie, a.NomVoie,
                   {A("BoitePostale")} AS BoitePostale, {A("SuiteAdresse")} AS SuiteAdresse, {A("CodePostal")} AS CodePostal,
                   {A("Localite")} AS Localite, {A("Cedex")} AS Cedex, {A("Pays")} AS Pays, {pays} AS LibellePays
            FROM rec.Adresse AS a WHERE a.Id = @id
            """, ct, new SqlParameter("@id", idAdresse));
        if (!await r.ReadAsync(ct)) return "";
        string T(string c) => FormeLigne.Texte(r, c);
        var lignes = new List<string>();
        void Ligne(string texte) { if (texte.Trim().Length > 0) lignes.Add(texte.Trim()); }
        var special = T("TypeSpecial").Trim() switch { "T" => "Toque", "C" => "Case", "B" => "Boîte Greffe", "O" => "Boîte Ordre", _ => "" };
        if (special.Length > 0) Ligne(T("ValeurSpecial").Length > 0 ? $"{special} n° {T("ValeurSpecial")}" : special);
        if (T("PreAdresse").Length > 0) lignes.Add(T("PreAdresse"));
        Ligne($"{T("NumeroVoie")} {T("ComplementNumero")} {T("TypeVoie")} {T("NomVoie")}");
        Ligne((T("BoitePostale").Length > 0 ? $"BP {T("BoitePostale")} " : "") + T("SuiteAdresse"));
        var cedex = T("Cedex");
        Ligne($"{T("CodePostal")} {T("Localite")} {(cedex.Length > 0 ? "CEDEX " : "")}{(cedex != "." ? cedex : "")}");
        var codePays = T("Pays").Trim();
        if (codePays.Length > 0 && codePays != "FRA" && T("LibellePays").Length > 0) lignes.Add(T("LibellePays"));
        var bloc = string.Join("|", lignes).Trim();
        while (bloc.Contains("  ", StringComparison.Ordinal)) bloc = bloc.Replace("  ", " ", StringComparison.Ordinal);
        return bloc;
    }

    // Les codes de partie que GetSpecifiquesActeursJudiciaires sait trouver (:173-197), sans les instances de la famille (…J).
    private static readonly HashSet<string> CodesParties = new(StringComparer.Ordinal)
        { "D", "AVD", "POD", "PLD", "F", "AVF", "POF", "PLF", "I", "AVI", "POI", "PLI" };

    /// <summary>GetDestinatairesEditions (:3217-3400) pour les lettres simple, LR et LRAR : chaque « TYPE[,MODE] » range ses
    /// destinataires ; DI ajoute le document interne. Rend aussi les types que la V3 ne sait pas trouver.</summary>
    private static (List<Destinataire> Simple, List<Destinataire> Lr, List<Destinataire> Lrar, List<string> Ignores) Ranger(string brute, List<Destinataire> entites)
    {
        List<Destinataire> simple = [], lr = [], lrar = [];
        var ignores = new List<string>();
        var interne = new Destinataire("", "", DocumentInterne);
        var texte = string.IsNullOrEmpty(brute) ? "DI" : brute.Replace("\r\n", ";", StringComparison.Ordinal);
        foreach (var entree in texte.Split(';'))
        {
            var morceaux = entree.Split(',');
            var type = morceaux[0].Trim();
            var mode = morceaux.Length == 2 ? morceaux[1].Trim() : "LS";
            if (type.Length == 0) continue;
            List<Destinataire> trouves;
            if (type == "DI") trouves = [interne];
            else if (CodesParties.Contains(type)) trouves = entites.Where(e => e.Code == $"PART_{type}").ToList();
            else { ignores.Add(entree.Trim()); continue; }
            void Ajouter(List<Destinataire> cible) { foreach (var t in trouves) if (!cible.Contains(t)) cible.Add(t); }
            foreach (var m in type == "DI" ? [mode] : mode.Split('-'))
                switch (m)
                {
                    case "LS" or "RMP": Ajouter(simple); break;
                    case "LR": Ajouter(lr); break;
                    case "LRAR": Ajouter(lrar); break;
                    case "LSLR": Ajouter(simple); Ajouter(lr); break;
                    case "LSLRAR": Ajouter(simple); Ajouter(lrar); break;
                    case "LRLRAR": Ajouter(lr); Ajouter(lrar); break;
                    case "LSLRLRAR": Ajouter(simple); Ajouter(lr); Ajouter(lrar); break;
                    default: ignores.Add(entree.Trim()); break;   // MAIL, CFEC, COFFRE(...) : courriel et coffre-fort
                }
        }
        return (simple, lr, lrar, ignores);
    }

    // ------------------------------------------------------------------ LE PARAMETRE, LES LOTS, LA MAQUETTE

    private sealed record Parametre(string Libelle, string? LotSubstitution, Dictionary<string, string> Liste);

    private static readonly Regex Attribut = new("([A-Z_0-9]+)=\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>ParametreEdition.GetParametreEdition : null sans parametre ou sans liste (GetAllEditions n'edite alors rien, :2811).</summary>
    private static async Task<Parametre?> ParametreAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        var col = await RelancesRsurv.ColonnesAsync(geste, "v2.PARAMETRE_EDITION", ct);
        if (!col.Contains("PRMED_LISTE")) return null;
        await using var r = await geste.LireAsync($"""
            SELECT PRMED_LIBELLE, CAST(PRMED_LISTE AS nvarchar(max)) AS Liste,
                   {(col.Contains("PRMED_LOT_SUBSTITUTION") ? "PRMED_LOT_SUBSTITUTION" : "CAST(NULL AS varchar(50))")} AS Substitution
            FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @c
            """, ct, new SqlParameter("@c", code));
        if (!await r.ReadAsync(ct)) return null;
        var liste = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attribut.Matches(FormeLigne.Texte(r, "Liste"))) liste[m.Groups[1].Value] = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value);
        if (liste.Count == 0) return null;
        return new Parametre(FormeLigne.Texte(r, "PRMED_LIBELLE"), FormeLigne.Texte(r, "Substitution") is { Length: > 0 } s ? s : null, liste);
    }

    /// <summary>Lot.GetListeLotActif (Edition\Lot.cs:84-86, « LOT_ACTIF = 1 ») en majuscules ; null si la base n'a pas la table.</summary>
    private static async Task<HashSet<string>?> LotsActifsAsync(GesteEnCours geste, CancellationToken ct)
    {
        var col = await RelancesRsurv.ColonnesAsync(geste, "v2.LOT", ct);
        if (!col.Contains("LOT_NOM")) return null;
        var lots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await geste.LireAsync(col.Contains("LOT_ACTIF") ? "SELECT LOT_NOM FROM v2.LOT WHERE LOT_ACTIF = 1" : "SELECT LOT_NOM FROM v2.LOT", ct);
        while (await r.ReadAsync(ct)) lots.Add(FormeLigne.Texte(r, "LOT_NOM"));
        return lots;
    }

    /// <summary>La maquette « _LR » / « _LRAR » si elle existe (Maquette.GetMaquette, Maquette\Maquette.cs:118), sinon la maquette
    /// de base ; la base de base si la table des maquettes n'est pas dans la base du greffe.</summary>
    private static async Task<string> MaquetteAsync(GesteEnCours geste, string maquette, string suffixe, CancellationToken ct)
    {
        if (maquette.Length == 0) return "";
        var col = await RelancesRsurv.ColonnesAsync(geste, "v2.MAQUETTE", ct);
        if (!col.Contains("MQTT_CODE_MAQUETTE")) return maquette;
        return await RelancesRsurv.Scalaire(geste, "SELECT MQTT_CODE_MAQUETTE FROM v2.MAQUETTE WHERE MQTT_CODE_MAQUETTE = @m", ct,
                                            new SqlParameter("@m", maquette + suffixe)) is string m ? m.Trim() : maquette;
    }

    /// <summary>DMNED_PARAMETRE_GENERATION tel que les lignes de RIG_DEV le portent (en tete).</summary>
    private static string Generation(string observation, string mode, string? numeroDemande)
        => $"<PARAMETRES HAS_OBSERVATION=\"{observation}\" MODE_ENVOI=\"{mode}\"{(numeroDemande is { Length: > 0 } n ? $" NUM_DEMANDE=\"{n}\"" : "")} />";
}
