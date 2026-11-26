using System.Globalization;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les EDITIONS d'un evenement de procedure d'une INSTANCE, calculees puis mises dans la file des editions (v2.DEMANDE_EDITION)
/// comme RIG v2, ecrites ici une seule fois pour tous les ecrivains : les Convocations (lot 7, « Citer par huissier ») et les
/// lots qui en auront besoin (un evenement ecrit par EvenementsProcedureV2). Plan « RIG v3 100 % fonctionnel », lot 7, 27/11/2026.
///
/// SOURCE (ancien RIG, Source\Outils\DLL\RigMetier\JUDICIAIRE\EvenementProcedure.cs) :
///   - CreateDemandeEdition (:2607-2690), branche INSTANCE : les maquettes CEP_EDITION du code de l'evenement de type « I »
///     (:2649), les destinataires de l'instance (_EntiteParente.destinataireEditionLot2.listeEntites, :2627), le document interne
///     masque (:2628) ;
///   - GetAllEditions (:2801-3170), maquette par maquette : sa condition (AnalyseCondition, :3586-3905), son reglage d'edition
///     (PARAMETRE_EDITION, :2811) dont la liste de parametres n'est pas vide (:2814), ses destinataires par mode d'envoi
///     (GetDestinatairesEditions, :3217-3402 ; une maquette a plusieurs « MAIL » ou « CFEC » est decoupee ligne par ligne, :2817-2833),
///     les autres parametres de la maquette (CEPED_AUTRE_PARAMETRE, :2867-2893), la source (CODE_ENTITE_SOURCE, :2895-2918), l'entite
///     source (:2921), le mode d'envoi (:2923-2928), puis une demande par mode : lettre simple (:3054-3075), coffre (:3095-3118) ;
///   - GenereDemandeEdition (:3406-3500) : la demande de DemandeEdition.CreateDemandeEdition (Edition\DemandeEdition.cs:536-600),
///     chaque parametre pose sur sa colonne DMNED_<nom> (noms de getParametreCs, :3546-3580 ; SetChamp, DemandeEditionPourServices.cs:
///     478-508), le reste dans DMNED_PARAMETRE_GENERATION ; le courriel du premier destinataire coche (:3433-3438), le coffre (:3440-
///     3447), le libelle source « <code> - <date> (EP n° <ordre>) » (:3475, EditionLibelleSource :467-476) ;
///   - les destinataires d'une instance : les destinataires institutionnels actifs, code « INST_<type> » (DestinatairesEditionsLot2.cs:
///     1520, :2216-2235 ; nom du coffre vide avant sa date de demarrage, DestinataireInstitutionnel.NomCoffre) ; un type de destinataire
///     d'une maquette est « INST_<type> » s'il est un type de DESTINATAIRE_INSTITUTIONNEL (GetSpecifiquesActeursJudiciaires, :171-250).
/// Le tableau des editions de RIG v2 enregistre ces demandes telles quelles (TableauEditionLot2Parametre._valider, :104-108) : elles
/// entrent dans la file en phase « C » (creation).
///
/// CE QUE LA V3 NE REPREND PAS. Une maquette qui en a besoin REFUSE le geste avec son nom (Calcul.NonRepris) : une partie des
/// editions d'une maquette ne s'ecrit pas en silence.
///   - les conditions autres que nature d'affaire, nature particuliere et acte de saisine (NA, NP, AS : colonnes de l'instance) ;
///   - les destinataires parties, avocats, dirigeants et entites liees (PC, prevention) de l'instance (RecupDestEditLot2FROMInstance,
///     :1232-1519 : PART_*, DIR, PDIR, entites des instances jointes) : une maquette qui les vise refuse. Seuls les destinataires
///     institutionnels sont dans la liste des destinataires ; RIG v2 y ajoute aussi, decoches, ces acteurs de l'instance (:3257) ;
///   - les envois LR et LRAR (imprimes JLR / JLRAR, numeros de LRAR, :3131-3252), RMP (bordereau, :3277-3293), par courriel
///     (:3320-3342), au coffre d'un code de depot « CFEC(...) » ou d'un modele « MAIL(...) » (:3340-3395) ;
///   - les files d'impression (parametres LOT, LOT_LR, LOT_ILR, LOT_LRAR, LOT_ILRAR : lots actifs, lots Amitel et leur decoupage par
///     destinataire, :2835-2865, :3477-3497) ;
///   - les sources DEPOT_PC, BODACC, PUBLICITE, CJ (:2899-2917) ;
///   - le code-barre d'une piece numerisee sans depot (_GetEditionsCodeBarre, :3172-3215) ;
///   - le groupe d'impression que la validation du tableau attribue (ValiderNumGroupeParImprimante) : la demande reste sans groupe,
///     comme celles des relances (EcrivainRelancesComptesAnnuels) ;
///   - la mise en page du document (le moteur d'editions, etats Crystal / PDF) : hors du plan.
/// </summary>
// fix-ok: relu contre RIG v2 avant le premier banc (27/11/2026) -- trois ecarts du premier jet : la ligne « DI » ne cochait pas le
// document interne DANS la liste du mode, qui doit cesser d'etre vide et creer la demande (SetDocumentInterneVisibility(true),
// EvenementProcedure.cs:3247-3291) ; l'entite source etait echappee alors que RIG v2 la concatene brute (:2921) ; la citation a
// besoin des valeurs FINALES de chaque demande (coffre, impression) pour son KBIS (OPE_CITATIONS.cs:448-452), d'ou Depot.
internal static class EditionsEvenementV2
{
    /// <summary>Un destinataire de la liste LISTE_ENTITE_DESTINATION (DestinatairesEditionsLot2.ENTITE, ses attributs XML).</summary>
    internal sealed class Destinataire
    {
        public string ParDefaut { get; set; } = "0";
        public string Code { get; init; } = "";
        public string Valeur { get; init; } = "";
        public string Libelle { get; init; } = "";
        public string Adresse { get; init; } = "";
        public string Email { get; init; } = "";
        public string NomCoffre { get; init; } = "";
        public string Cle { get; init; } = "";

        /// <summary>Le document interne (CreateEntiteXMLDestinataireDocumentInterne, DestinatairesEditionsLot2.cs:136-153).</summary>
        public bool EstDocumentInterne => Code.Length == 0 && Valeur.Length == 0 && Libelle.Length == 0 && Adresse.Length == 0;

        public Destinataire Copie() => (Destinataire)MemberwiseClone();
    }

    /// <summary>Une demande d'edition calculee, avant son ecriture : ses parametres (ListeParametre), ses destinataires, et les
    /// colonnes que le geste pose encore apres le calcul (<see cref="Colonnes"/>, ecrites en dernier).</summary>
    internal sealed class Edition
    {
        public SortedDictionary<string, string> Parametres { get; init; } = new(StringComparer.Ordinal);
        public List<Destinataire> Destinataires { get; init; } = [];
        public bool Impression { get; set; }
        public bool Coffre { get; set; }
        /// <summary>Le courriel du premier destinataire coche AU CALCUL (GenereDemandeEdition, :3433-3438).</summary>
        public string? Courriel { get; set; }
        public string? LibelleSource { get; set; }
        /// <summary>Le type de generation impose (CreateDemandeEdition(idDemande, eTypeGeneration...)) ; null : celui du parametre.</summary>
        public string? TypeGeneration { get; init; }
        /// <summary>Le libelle pose APRES les parametres (le KBIS de la citation) ; null : LIBELLE_DOCUMENT.</summary>
        public string? LibelleImpose { get; init; }
        public Dictionary<string, object?> Colonnes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string Libelle => LibelleImpose ?? (Parametres.TryGetValue("LIBELLE_DOCUMENT", out var l) ? l : "");
    }

    /// <summary>L'instance de l'evenement : ce que CreateDemandeEdition et AnalyseCondition en lisent.</summary>
    internal sealed record Instance(int Id, string Role, string? NumeroSource, string? NatureAffaire, string? NatureParticuliere,
                                    string? ActeSaisine);

    /// <summary>L'evenement : son code, son numero source, sa date, son numero d'ordre (null : « EP sans n° ordre »), sa piece.</summary>
    internal sealed record Evenement(string Code, string NumeroSource, DateTime? Date, short? NumOrdre, bool AvecPieceNumerisee);

    /// <summary>Le calcul : les editions, ou ce que la V3 ne reprend pas (le geste refuse avec ce texte).</summary>
    internal sealed record Calcul(IReadOnlyList<Edition> Editions, string? NonRepris);

    private const string LotsParametres = ";LOT;LOT_LR;LOT_ILR;LOT_LRAR;LOT_ILRAR;";

    /// <summary>Les editions de l'evenement <paramref name="eve"/> de l'instance <paramref name="inst"/> (voir l'en-tete).</summary>
    internal static async Task<Calcul> CalculerAsync(GesteEnCours geste, Instance inst, Evenement eve, CancellationToken ct)
    {
        Calcul Refus(string quoi) => new([], $"{quoi} n'est pas repris par RIG v3 : rien n'est enregistré, faire la citation dans RIG v2.");

        // _GetEditionsCodeBarre (:3176) : seulement pour une piece numerisee.
        if (eve.AvecPieceNumerisee) return Refus($"Le code-barre de la pièce numérisée de l'événement {eve.Code}");

        var destinataires = await DestinatairesInstanceAsync(geste, ct);
        var types = await TypesInstitutionnelsAsync(geste, ct);
        var editions = new List<Edition>();
        var maquettes = new List<(string CodeEdition, string Desti, string Condition, string Autres)>();
        await using (var r = await geste.LireAsync("""
            SELECT CEPED_CODE_EDITION, CEPED_DESTI_MODE_ENVOI, CEPED_CONDITION, CEPED_AUTRE_PARAMETRE
            FROM v2.CEP_EDITION WHERE CEPED_CODE_EVE_PRO = @c AND CEPED_TYPE_EP = 'I' ORDER BY CEPED_ID_CEPED
            """, ct, new SqlParameter("@c", eve.Code)))
            while (await r.ReadAsync(ct))
                maquettes.Add((FormeLigne.Texte(r, "CEPED_CODE_EDITION"), FormeLigne.Texte(r, "CEPED_DESTI_MODE_ENVOI"),
                               FormeLigne.Texte(r, "CEPED_CONDITION"), FormeLigne.Texte(r, "CEPED_AUTRE_PARAMETRE")));

        var cle = 1;
        foreach (var m in maquettes)
        {
            var nonRepris = await MaquetteAsync(geste, inst, eve, m.CodeEdition, m.Desti, m.Condition, m.Autres, destinataires, types,
                                                editions, () => cle++, ct);
            if (nonRepris is not null) return Refus(nonRepris);
        }
        return new Calcul(editions, null);
    }

    /// <summary>Une maquette (GetAllEditions, :2808-3345) ; le texte de ce qui n'est pas repris, null sinon.</summary>
    private static async Task<string?> MaquetteAsync(GesteEnCours geste, Instance inst, Evenement eve, string codeEdition, string desti,
        string condition, string autres, IReadOnlyList<Destinataire> parents, IReadOnlySet<string> types, List<Edition> editions,
        Func<int> cleSuivante, CancellationToken ct)
    {
        if (Condition(condition, inst) is not { } remplie) return $"La condition « {condition.Trim()} » de l'édition {codeEdition}";
        if (!remplie) return null;
        if (await ReglageAsync(geste, codeEdition, ct) is not { } reglage) return null;          // :2813
        if (reglage.Parametres.Count == 0) return null;                                          // :2814

        // GetDestinatairesEditions (:3217-3402).
        var lignes = (desti.Length == 0 ? "DI" : desti.Replace("\r\n", ";", StringComparison.Ordinal)).Split(';');
        var dico = new Dictionary<string, List<Destinataire>> { ["SIMPLE"] = [], ["COFFRE"] = [] };
        int coffres = 0;
        foreach (var ligne in lignes)
        {
            var morceaux = ligne.Split(',');
            var type = morceaux[0];
            var mode = morceaux.Length == 2 ? morceaux[1] : "LS";
            if (type == "DI")
            {
                // Le document interne coche dans la liste du mode (SetDocumentInterneVisibility(true), :3247-3291) : la liste n'est
                // plus vide, la demande se cree. Seuls LS (lettre simple) et CFEC (coffre) sont repris.
                var liste = mode switch { "LS" => "SIMPLE", "CFEC" => "COFFRE", _ => null };
                if (liste is null) return $"L'envoi « {mode} » du document interne de l'édition {codeEdition}";
                if (dico[liste].FirstOrDefault(x => x.EstDocumentInterne) is { } di) di.ParDefaut = "1";
                else dico[liste].Add(new Destinataire { ParDefaut = "1" });
                continue;
            }
            if (!types.Contains(type)) return $"Le destinataire « {type} » de l'édition {codeEdition}";
            var specifiques = parents.Where(e => e.Code == "INST_" + type).Select(e => { var c = e.Copie(); c.ParDefaut = "1"; return c; }).ToList();
            foreach (var envoi in mode.Split('-'))
            {
                switch (envoi)
                {
                    case "LS":
                        Ajouter(dico["SIMPLE"], specifiques);
                        break;
                    case "CFEC":
                        Ajouter(dico["SIMPLE"], specifiques.Where(e => e.NomCoffre.Length == 0));
                        Ajouter(dico["COFFRE"], specifiques.Where(e => e.NomCoffre.Length > 0));
                        coffres++;
                        break;
                    default:
                        return $"L'envoi « {envoi} » de l'édition {codeEdition}";
                }
            }
        }
        // Decoupage (:2817-2833) : plusieurs CFEC, une maquette par ligne.
        if (coffres > 1)
        {
            foreach (var ligne in lignes)
                if (await MaquetteAsync(geste, inst, eve, codeEdition, ligne, condition, autres, parents, types, editions, cleSuivante, ct) is { } nr)
                    return nr;
            return null;
        }

        var p = new SortedDictionary<string, string>(reglage.Parametres, StringComparer.Ordinal);
        // Les files d'impression ne sont pas reprises (en tete).
        foreach (var lot in LotsParametres.Trim(';').Split(';'))
            if (p.TryGetValue(lot, out var v) && v.Length > 0) return $"La file d'impression « {v} » de l'édition {codeEdition}";
        // CEPED_AUTRE_PARAMETRE (:2867-2893).
        foreach (var autre in autres.Split(';'))
        {
            var paire = autre.Split('=');
            if (paire.Length != 2) continue;
            if (LotsParametres.Contains(";" + paire[0] + ";", StringComparison.Ordinal))
            {
                if (paire[1].Length > 0) return $"La file d'impression « {paire[1]} » de l'édition {codeEdition}";
                p[paire[0]] = paire[1];
            }
            else p[paire[0]] = paire[1];
        }
        // La source (:2895-2918).
        var source = p.GetValueOrDefault("CODE_ENTITE_SOURCE", "");
        switch (source)
        {
            case "EVT_PROC": p["VALEUR_ENTITE_SOURCE"] = eve.NumeroSource; break;
            case "INSTANCE" or "PROC_COLL" or "PREVENTION" or "MESURE": p["VALEUR_ENTITE_SOURCE"] = inst.NumeroSource ?? ""; break;
            case "DEPOT_PC" or "BODACC" or "PUBLICITE" or "CJ": return $"La source « {source} » de l'édition {codeEdition}";
        }
        // L'entite source, au mot pres et sans echappement, comme RIG v2 la concatene (:2921).
        p["ENTITE_SOURCE"] = "<ENTITE PAR_DEFAUT=\"0\" CODE=\"\" VALEUR=\"\" LIBELLE=\"" + inst.Role + " - " + p.GetValueOrDefault("LIBELLE_DOCUMENT", "")
                           + "\" ADRESSE=\"\" TYPE=\"\" LIBENTITE=\"\"/>";
        var mode0 = p.GetValueOrDefault("MODE_ENVOI", "");
        if (mode0 is "LR" or "LRAR") return $"L'envoi « {mode0} » de l'édition {codeEdition}";
        p["MODE_ENVOI"] = "EMISSION_SIMPLE";

        var libelleSource = $"{eve.Code}{(eve.Date is { } d ? " - " + d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "")}"
                          + (eve.NumOrdre is { } n ? $" (EP n° {n})" : " (EP sans n° ordre)");

        // Lettre simple (:3054-3075) : les destinataires, puis ceux de l'instance (decoches) ; mode LS.
        if (dico["SIMPLE"].Count > 0)
        {
            var liste = Liste(dico["SIMPLE"], parents);
            p["TYPE_DOCUMENT"] = reglage.CodeDocument;
            p["CLE"] = cleSuivante().ToString(CultureInfo.InvariantCulture);
            p["MODE_ENVOI"] = "LS";
            editions.Add(Nouvelle(p, liste, reglage.Impression, coffre: false, libelleSource));
        }
        // Coffre (:3095-3118) : une copie des parametres, mode COFFRE, libelle « - Coffre ».
        if (dico["COFFRE"].Count > 0)
        {
            var copie = new SortedDictionary<string, string>(p, StringComparer.Ordinal)
            {
                ["TYPE_DOCUMENT"] = reglage.CodeDocument, ["MODE_ENVOI"] = "COFFRE",
                ["LIBELLE_DOCUMENT"] = p.GetValueOrDefault("LIBELLE_DOCUMENT", "") + " - Coffre",
                ["CLE"] = cleSuivante().ToString(CultureInfo.InvariantCulture),
            };
            editions.Add(Nouvelle(copie, Liste(dico["COFFRE"], parents), reglage.Impression, coffre: true, libelleSource));
        }
        return null;
    }

    private static Edition Nouvelle(SortedDictionary<string, string> p, List<Destinataire> liste, bool impression, bool coffre, string libelleSource)
        => new()
        {
            Parametres = new SortedDictionary<string, string>(p, StringComparer.Ordinal), Destinataires = liste, Impression = impression,
            Coffre = coffre, LibelleSource = libelleSource,
            Courriel = liste.FirstOrDefault(e => e.ParDefaut == "1")?.Email,
        };

    /// <summary>La liste d'un mode : ses destinataires (coches), puis ceux de l'instance tels quels (AddRange(listeEntitesParent),
    /// :3257, :3301), qui commencent par le document interne masque (SetDocumentInterneVisibility(false), :2628).</summary>
    private static List<Destinataire> Liste(List<Destinataire> mode, IReadOnlyList<Destinataire> parents)
    {
        var liste = new List<Destinataire>(mode) { new() { ParDefaut = "0" } };
        liste.AddRange(parents.Select(e => e.Copie()));
        return liste;
    }

    private static void Ajouter(List<Destinataire> dans, IEnumerable<Destinataire> quoi)
    {
        foreach (var e in quoi)
            if (!dans.Any(x => x.Code == e.Code && x.Valeur == e.Valeur)) dans.Add(e);
    }

    /// <summary>
    /// AnalyseCondition (:3586-3905) pour une instance : vide = remplie ; « +NA<x> » exige la nature d'affaire x (une des « + »),
    /// « -NA<x> » l'exclut ; de meme NP (nature particuliere) et AS (acte de saisine). null : une condition que la V3 ne lit pas.
    /// Le decoupage est celui de RIG v2 : « + » et « - » deviennent « ;+ » et « ;- », le premier caractere saute (:3599-3600).
    /// </summary>
    internal static bool? Condition(string condition, Instance inst)
    {
        if (string.IsNullOrEmpty(condition)) return true;
        var c = condition.Replace("+", ";+", StringComparison.Ordinal).Replace("-", ";-", StringComparison.Ordinal).Replace("\r\n", "", StringComparison.Ordinal);
        var plus = new Dictionary<string, List<string>> { ["NA"] = [], ["NP"] = [], ["AS"] = [] };
        var exclue = false;
        foreach (var morceau in c[1..].Split(';').Where(s => s.Length > 0))
        {
            if (morceau.Length < 3) return null;
            var signe = morceau[0];
            var code = morceau.Substring(1, 2).ToUpperInvariant();
            var valeur = morceau[3..];
            var champ = code switch { "NA" => inst.NatureAffaire, "NP" => inst.NatureParticuliere, "AS" => inst.ActeSaisine, _ => null };
            if (!plus.ContainsKey(code)) return null;
            if (signe == '+') plus[code].Add(valeur);
            else if (champ == valeur) exclue = true;
        }
        if (exclue) return false;
        return (plus["NA"].Count == 0 || plus["NA"].Contains(inst.NatureAffaire ?? ""))
            && (plus["NP"].Count == 0 || plus["NP"].Contains(inst.NatureParticuliere ?? ""))
            && (plus["AS"].Count == 0 || plus["AS"].Contains(inst.ActeSaisine ?? ""));
    }

    private sealed record Reglage(SortedDictionary<string, string> Parametres, string CodeDocument, bool Impression);

    /// <summary>Le reglage d'edition (ParametreEdition.GetParametreEdition) : la liste PRMED_LISTE (« PARAMETRES » et ses
    /// attributs), le code document, l'impression ; null s'il n'existe pas.</summary>
    internal static async Task<(SortedDictionary<string, string> Parametres, string CodeDocument, bool Impression)?> ReglageEditionAsync(
        GesteEnCours geste, string code, CancellationToken ct)
        => await ReglageAsync(geste, code, ct) is { } r ? (r.Parametres, r.CodeDocument, r.Impression) : null;

    private static async Task<Reglage?> ReglageAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(
            "SELECT PRMED_LISTE, PRMED_CODE_DOCUMENT, PRMED_IMPRESSION FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @c", ct, new SqlParameter("@c", code));
        if (!await r.ReadAsync(ct)) return null;
        return new Reglage(Parametres(FormeLigne.Texte(r, "PRMED_LISTE")), FormeLigne.Texte(r, "PRMED_CODE_DOCUMENT"), r["PRMED_IMPRESSION"] is true);
    }

    /// <summary>Une ListeParametre de RIG v2 (« &lt;PARAMETRES A="x" B="y" /&gt; ») ; vide si elle ne se lit pas.</summary>
    internal static SortedDictionary<string, string> Parametres(string xml)
    {
        var p = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(xml)) return p;
        try
        {
            foreach (var a in XElement.Parse(xml).Attributes()) p[a.Name.LocalName] = a.Value;
        }
        catch (System.Xml.XmlException) { }
        return p;
    }

    /// <summary>Les destinataires institutionnels actifs, par type (RecupDestEditLot2DestinataireInstitutionnelForInstanceOrMesure).</summary>
    private static async Task<List<Destinataire>> DestinatairesInstanceAsync(GesteEnCours geste, CancellationToken ct)
    {
        var liste = new List<Destinataire>();
        await using var r = await geste.LireAsync("""
            SELECT DTINS_CODE, DTINS_TYPE, DTINS_LIBELLE, DTINS_BLOC_ADRESSE, DTINS_ADRESSE_MAIL,
                   CASE WHEN DTINS_DATE_DEMARRAGE_COFFRE > CAST(GETDATE() AS date) THEN '' ELSE DTINS_NOM_COFFRE END AS NomCoffre
            FROM v2.DESTINATAIRE_INSTITUTIONNEL WHERE DTINS_ACTIF = 1 ORDER BY DTINS_TYPE, DTINS_CODE
            """, ct);
        while (await r.ReadAsync(ct))
            liste.Add(new Destinataire
            {
                Code = "INST_" + FormeLigne.Texte(r, "DTINS_TYPE"), Valeur = FormeLigne.Texte(r, "DTINS_CODE"),
                Libelle = FormeLigne.Texte(r, "DTINS_LIBELLE"), Adresse = FormeLigne.Texte(r, "DTINS_BLOC_ADRESSE"),
                Email = FormeLigne.Texte(r, "DTINS_ADRESSE_MAIL"), NomCoffre = FormeLigne.Texte(r, "NomCoffre"),
                Cle = "DESTINATAIRE_INSTITUTIONNEL_" + FormeLigne.Texte(r, "DTINS_CODE"),
            });
        return liste;
    }

    /// <summary>Les types de destinataire institutionnel (GetListeTypesDestinataireInstitutionnel : SELECT distinct DTINS_TYPE).</summary>
    private static async Task<HashSet<string>> TypesInstitutionnelsAsync(GesteEnCours geste, CancellationToken ct)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        await using var r = await geste.LireAsync("SELECT DISTINCT DTINS_TYPE FROM v2.DESTINATAIRE_INSTITUTIONNEL", ct);
        while (await r.ReadAsync(ct)) types.Add(FormeLigne.Texte(r, "DTINS_TYPE"));
        return types;
    }

    // ------------------------------------------------------------------------------------------------ L'ECRITURE

    /// <summary>Les champs de DEMANDE_EDITION de RIG v2 (DemandeEdition.ListeChamps) : les colonnes de la table dans RIG_DEV
    /// (sys.columns de SQL-DEV\DEV, lu le 27/11/2026). Un parametre dont le nom (getParametreCs) est l'un d'eux va sur sa colonne
    /// (SetChamp) ; les autres restent dans DMNED_PARAMETRE_GENERATION.</summary>
    private static readonly HashSet<string> ChampsV2 = new(StringComparer.OrdinalIgnoreCase)
    {
        "ID_DMNED", "CODE_ENTITE_SOURCE", "VALEUR_ID_SOURCE", "CODE_ENTITE_DESTINATAIRE", "VALEUR_ID_DESTINATAIRE", "NUM_DOCUMENT_EDITION",
        "NUM_DOCUMENT_FLUX", "ID_DMND", "ID_DMNED_REEDITEE", "IMPRIMANTE", "TYPE_DESTINATAIRE", "MEDIA", "LOT", "ETAT_EMISSION", "TYPE_DOCUMENT",
        "PHASE_TRAITEMENT", "NUM_GROUPE", "NUM_ORDRE_GROUPE", "UTILISATEUR", "DATE_PRE_EMISSION", "DATE_PRE_GENERATION", "NOMBRE_EXEMPLAIRE",
        "DESIGNATION_DESTINATAIRE", "BLOC_ADRESSE_DESTINATAIRE", "ADRESSE_MAIL_DESTINATAIRE", "IMPRESSION", "PAPIER_TRAME", "TYPE_PAPIER",
        "AGRAFAGE", "RECTO_VERSO", "TYPE_GENERATION", "PARAMETRE_GENERATION", "TRI_EDITION", "AFF_AVANT_IMPRESSION", "INTERLOCUTEUR",
        "LIBELLE_DOCUMENT", "LIBELLE_ERREUR", "NB_TENTATIVES", "GROUPAGE_AUTOMATIQUE", "DATE_CREATION", "RESUME", "NUM_LRAR", "READ_ONLY",
        "LES_NUMEROS", "PARAMETRAGE_COPIE_CONFORME", "CODE_MAQUETTE", "CODE_PROCESSUS", "BATCH_POST_EDITION", "ENVOI_PAR_MAIL", "EMAIL_TYPE",
        "EMAIL_SUJET", "EMAIL_CORPS", "ID_EMAIL_ENVOYE", "ID_DSRCS", "HAS_NUMLRAR", "IS_NON_SUPPRESSIBLE", "HAS_NUMLR", "REFERENCE_DESTINATAIRE",
        "LIBELLE_SOURCE", "MASQUER_COFFRE_FORT", "MAIL_ENVOYE_VALIDATION", "ENVOI_AU_COFFRE", "NOM_COFFRE_FORT", "CODE_CFECP", "OBSERVATION",
        "ID_INFDS", "ID_LGEDT", "ID_DETD_X", "TRIBUNAL_DIGITAL_NATURE_DOC", "PLIEUSE_DATE_IMPRESSION", "PLIEUSE_DATE_AFFRANCHISSEMENT",
        "PLIEUSE_DATE_REMISE", "NATURE_EDITION", "MODE_ENVOI",
    };

    /// <summary>Les noms de parametre que RIG v2 renomme avant de les poser (getParametreCs, EvenementProcedure.cs:3546-3580).</summary>
    private static string NomCs(string parametre) => parametre switch
    {
        "DATE_GENERATION" => "DATE_PRE_GENERATION", "DATE_EMISSION" => "DATE_PRE_EMISSION", "NOMBRE_EXEMPLAIRES" => "NOMBRE_EXEMPLAIRE",
        "NUM_GROUPE_IMPRESSION" => "NUM_GROUPE", "NUM_ORDRE_GROUPE_IMPRESSION" => "NUM_ORDRE_GROUPE", "CODE_DESTINATAIRE" => "TYPE_DESTINATAIRE",
        "TYPE_MEDIA" => "MEDIA", "CODE_DOCUMENT" => "TYPE_DOCUMENT", "ADRESSE_POSTALE" => "BLOC_ADRESSE_DESTINATAIRE",
        "DESIGNATION" => "DESIGNATION_DESTINATAIRE", "EMETTRE" => "IMPRESSION", "ADRESSE_EMAIL" => "ADRESSE_MAIL_DESTINATAIRE",
        "ID_DEMANDE_EDITION" => "ID_DMNED_DEMANDE_EDITION", "ID_DEMANDE_REEDITION_SOURCE" => "ID_DMNED_REEDITEE", "ID_DEMANDE" => "ID_DMND",
        "VALEUR_ENTITE_SOURCE" => "VALEUR_ID_SOURCE", "AFFICHER_AVANT_IMPRESSION" => "AFF_AVANT_IMPRESSION", "NUM_FLUX_XML" => "NUM_DOCUMENT_FLUX",
        "NUM_DOCUMENT" => "NUM_DOCUMENT_EDITION", "PROCESSUS_EDITION" => "CODE_PROCESSUS", "CODE_ENTITE_DESTINATION" => "CODE_ENTITE_DESTINATAIRE",
        "VALEUR_ENTITE_DESTINATION" => "VALEUR_ID_DESTINATAIRE", _ => parametre,
    };

    /// <summary>
    /// Ecrit <paramref name="e"/> dans la file (v2.DEMANDE_EDITION), rattachee a la demande <paramref name="idDemande"/> ; rend son id.
    /// L'ordre de RIG v2 : les valeurs de CreateDemandeEdition (DemandeEdition.cs:536-565), l'impression du reglage (:3420), chaque
    /// parametre sur sa colonne ou dans DMNED_PARAMETRE_GENERATION (:3422-3429), le courriel, le coffre, le libelle source
    /// (:3433-3475), puis ce que le geste pose apres (<see cref="Edition.Colonnes"/>). Les colonnes NOT NULL sans defaut dans la base
    /// V3 (HAS_NUMLRAR...) sont posees a leur defaut de RIG_DEV, comme EcrivainRegistresJudiciaires ; une colonne que la base du greffe
    /// n'a pas n'est pas ecrite (base de banc partielle), comme EcrivainRelancesComptesAnnuels.
    /// </summary>
    internal static async Task<Depot> DeposerAsync(GesteEnCours geste, int idDemande, Edition e, CancellationToken ct)
    {
        var compte = geste.Agent.CompteWindows.Length > 32 ? geste.Agent.CompteWindows[..32] : geste.Agent.CompteWindows;
        var p = new SortedDictionary<string, string>(e.Parametres, StringComparer.Ordinal)
        {
            ["LISTE_ENTITE_DESTINATION"] = ListeXml(e.Destinataires),
        };
        var valeurs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DMNED_DATE_CREATION"] = geste.Maintenant, ["DMNED_TYPE_PAPIER"] = "NORMAL", ["DMNED_MEDIA"] = "IMPR", ["DMNED_ETAT_EMISSION"] = "N",
            ["DMNED_PHASE_TRAITEMENT"] = "C", ["DMNED_UTILISATEUR"] = compte, ["DMNED_NOMBRE_EXEMPLAIRE"] = (short)1, ["DMNED_IMPRESSION"] = true,
            ["DMNED_PAPIER_TRAME"] = false, ["DMNED_AGRAFAGE"] = false, ["DMNED_RECTO_VERSO"] = false,
            ["DMNED_TYPE_GENERATION"] = e.TypeGeneration ?? EcrivainRelancesComptesAnnuels.TypeGeneration(p.GetValueOrDefault("TYPE_GENERATION", ""), ""),
            ["DMNED_AFF_AVANT_IMPRESSION"] = false, ["DMNED_INTERLOCUTEUR"] = compte, ["DMNED_LIBELLE_DOCUMENT"] = e.Libelle,
            ["DMNED_GROUPAGE_AUTOMATIQUE"] = true, ["DMNED_RESUME"] = false, ["DMNED_READ_ONLY"] = false, ["DMNED_ID_DMND"] = idDemande,
            ["DMNED_TRIBUNAL_DIGITAL_NATURE_DOC"] = Vide(p.GetValueOrDefault("TRIBUNAL_DIGITAL_NATURE_DOC", "")),
            ["DMNED_ENVOI_PAR_MAIL"] = false, ["DMNED_HAS_NUMLRAR"] = false, ["DMNED_IS_NON_SUPPRESSIBLE"] = false, ["DMNED_HAS_NUMLR"] = false,
            ["DMNED_MASQUER_COFFRE_FORT"] = false, ["DMNED_ENVOI_AU_COFFRE"] = false,
        };
        valeurs["DMNED_IMPRESSION"] = e.Impression;
        var types = await TypesColonnesAsync(geste, ct);
        var reste = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (nom, valeur) in p)
        {
            if (nom is "PARAMETRE_GENERATION" or "ID_DMNED") continue;                          // la liste elle-meme, la cle (CorrigeParametre)
            var cs = NomCs(nom);
            if (!ChampsV2.Contains(cs) || Champ(types.GetValueOrDefault("DMNED_" + cs), valeur) is not { } champ) { reste[cs] = valeur; continue; }
            valeurs["DMNED_" + cs] = champ.Valeur;
        }
        if (e.LibelleImpose is { } libelle) valeurs["DMNED_LIBELLE_DOCUMENT"] = libelle;
        if (valeurs.GetValueOrDefault("DMNED_ADRESSE_MAIL_DESTINATAIRE") is null && !string.IsNullOrEmpty(e.Courriel))
            valeurs["DMNED_ADRESSE_MAIL_DESTINATAIRE"] = e.Courriel;
        if (e.Coffre) valeurs["DMNED_ENVOI_AU_COFFRE"] = true;
        valeurs["DMNED_MODE_ENVOI"] = null;                                                      // :3462 (ni LR ni LRAR)
        if (e.LibelleSource is { } source) valeurs["DMNED_LIBELLE_SOURCE"] = source;
        valeurs["DMNED_PARAMETRE_GENERATION"] = new XElement("PARAMETRES", reste.Select(kv => new XAttribute(kv.Key, kv.Value)))
            .ToString(SaveOptions.DisableFormatting);
        foreach (var (colonne, valeur) in e.Colonnes) valeurs[colonne] = valeur;
        var id = await geste.InsererAsync("v2.DEMANDE_EDITION", "DMNED_ID_DMNED",
            valeurs.Where(v => types.ContainsKey(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);
        return new Depot(id, valeurs["DMNED_ENVOI_AU_COFFRE"] is true, valeurs["DMNED_IMPRESSION"] is true,
                         valeurs.GetValueOrDefault("DMNED_CODE_CFECP"));
    }

    /// <summary>La demande ecrite : son id, et les valeurs finales que la citation recopie sur le KBIS (OPE_CITATIONS.cs:448-452).</summary>
    internal sealed record Depot(int Id, bool Coffre, bool Impression, object? CodeCfecp);

    private static object? Vide(string s) => s.Length == 0 ? null : s;

    private sealed record ValeurChamp(object? Valeur);

    /// <summary>SetChamp (DemandeEditionPourServices.cs:478-508) selon le type de la colonne ; null : la valeur ne se pose pas, le
    /// parametre reste dans DMNED_PARAMETRE_GENERATION. Un texte vide s'ecrit NULL (DBString vide).</summary>
    private static ValeurChamp? Champ(string? type, string valeur)
    {
        switch (type)
        {
            case null: return null;
            case "bit":
                if (valeur.Length == 0) return new(false);
                if (bool.TryParse(valeur, out var b)) return new(b);
                return int.TryParse(valeur, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? new(i == 1) : null;
            case "int" or "smallint" or "tinyint" or "bigint":
                if (valeur.Length == 0) return new(type == "smallint" ? (short)0 : 0);
                if (!int.TryParse(valeur, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
                return new(type == "smallint" ? (short)n : n);
            case "datetime" or "date" or "datetime2" or "smalldatetime":
                return DateTime.TryParse(valeur, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var d) ? new(d) : null;
            case "varchar" or "nvarchar" or "char" or "nchar" or "text" or "ntext" or "xml":
                return new(Vide(valeur));
            default: return null;
        }
    }

    /// <summary>Les colonnes de v2.DEMANDE_EDITION dans la base du greffe, avec leur type SQL.</summary>
    private static async Task<Dictionary<string, string>> TypesColonnesAsync(GesteEnCours geste, CancellationToken ct)
    {
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await geste.LireAsync("""
            SELECT c.name, t.name AS Type FROM sys.columns AS c JOIN sys.types AS t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(N'v2.DEMANDE_EDITION')
            """, ct);
        while (await r.ReadAsync(ct)) types[r.GetString(0)] = r.GetString(1);
        return types;
    }

    /// <summary>La liste des destinataires au format de RIG v2 (LISTE_ENTITES serialisee : un element ENTITE par destinataire, les
    /// attributs de DestinatairesEditionsLot2.ENTITE, TYPE « ENTITE » et PAYS « FRA » par defaut, :312-317).</summary>
    internal static string ListeXml(IEnumerable<Destinataire> liste)
        => new XElement("LISTE_ENTITES", liste.Select(e => new XElement("ENTITE",
                new XAttribute("PAR_DEFAUT", e.ParDefaut), new XAttribute("CODE", e.Code), new XAttribute("VALEUR", e.Valeur),
                new XAttribute("LIBENTITE", ""), new XAttribute("LIBELLE", e.Libelle), new XAttribute("ADRESSE", e.Adresse),
                new XAttribute("PAYS", "FRA"), new XAttribute("TYPE", "ENTITE"), new XAttribute("EMAIL", e.Email),
                new XAttribute("NOM_COFFRE", e.NomCoffre), new XAttribute("KEY_DESTINATAIRE", e.Cle))))
            .ToString(SaveOptions.DisableFormatting);
}
