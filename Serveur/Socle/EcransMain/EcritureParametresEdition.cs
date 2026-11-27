using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Edition des maquettes » (PROC_EDIT_MAQUETTE, écran fait main web/src/ecrans/EcranMaquettesEdition.tsx) : les gestes sur UN
/// paramètre d'édition, ENREGISTRER ses champs changés et le SUPPRIMER, comme les boutons Valider et Supprimer d'OPE_PARAM_EDT_MAJ de
/// RIG v2. Joués par la route POST RoutesMaquettesEdition.Parametre (ModuleEcransMain) dans la transaction et le journal des gestes
/// du socle (EcrivainsEcrans.JouerAsync, sa version pour un écran fait main) : mêmes gardes d'écriture (v2.* seulement). Plan « RIG v3
/// 100 % fonctionnel », lot 15, 27/11/2026 ; les lignes : LectureMaquettesEdition.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDIT_MAQUETTE\PARAMETRE_EDITION\OPE_PARAM_EDT_MAJ.cs) :
///   SUPPRIMER (Supprimer, :529-541) : bouton ouvert au seul agent d'Amitel (enableSupp, :111) ; CanSupp (:259-307) refuse un code
///     encore employé, dans cet ordre et au premier trouvé : un code événement (CEP_EDITION.CEPED_CODE_EDITION, CepEdition.cs:32-34),
///     un ULT de processus (ADAPTATEUR_ULT « ADPUL_CODE = 'EDITPARAM' AND ADPUL_VALEUR_DEFAUT LIKE code », AdaptateurUlt.cs:117-121),
///     le paramétrage Vidiged (VIDIGED_PARAMETRAGE.VGPRM_PARAMETRE_EDITION_CFEC sans casse, VidigedParametrage.cs:86-88), un type de
///     décision (TYPE_DECISION.R_TYPEDECISI_PARAMETRE_EDITION, TYPE_DECISION.cs:167-172), un courrier judiciaire
///     (COURRIER_JUDICIAIRE.COUJU_CODE_PRMED, CourrierJudiciaire.cs:30-32), chacun à son message ; sinon, après « Etes-vous sûr…
///     (:535, la confirmation de l'écran), ParametreEdition.Delete = « DELETE FROM PARAMETRE_EDITION WHERE PRMED_CODE = … »
///     (SqlBase.RequeteDelete, RigTools\SQL\SqlBase.cs:498-500).
///   ENREGISTRER (Valider, :494-527) : les contrôles de DoVerifier (:309-327) -- EDIT_LDEC sans DECISION_JUD, nombre d'exemplaires
///     au moins 1, à 1 si le lot ou le lot LRAR est une file d'Amitel (Lot.IsFileAmitel : LOT_NOM commence par « AMI »,
///     RigMetier\Edition\Lot.cs:40-44, le lot lu dans LOT comme la liste ComboBoxLot) -- et les champs obligatoires du formulaire
///     (MetierObligatoire, OPE_PARAM_EDT_MAJ.Designer.cs:226-236 : « Libellé paramètre édition », 100 caractères au plus, et
///     « Nb exemplaires ») ; un formulaire en lecture seule pour une maquette REDA ou inconnue (:210) ; la case « Garder edition
///     lot 1 » montrée au seul agent d'Amitel, pour une génération FUSION_RIG (:140-141). Puis ParametreEdition.Sauve :
///     RequeteSauve (RigMetier\Edition\ParametreEdition.cs:473-479) écrit les COLONNES et, avant elles, leur copie dans le XML
///     PRMED_LISTE (UpdateListeParametre, :435-469 : une case en « 1 » / « 0 », un nombre en texte). Ce XML compte : au chargement,
///     RIG v2 recopie chacune de ses clés connues PAR-DESSUS la colonne (_PostLoadDr -> _UpdateChampsFromXml, :21-24, :312-421).
///     Une colonne changée sans son attribut serait donc défaite par la V2 à sa prochaine lecture : la V3 change les deux. LIBELLE,
///     que la V2 lit dans le XML mais ne réécrit jamais (:443, « on le gere en lecture »), en est retiré quand le libellé change,
///     comme le fait une sauvegarde V2 ; GARDER_EDITION_LOT1, hors de la liste des clés (:270-300), reste une colonne seule.
/// NON REPRIS (nommés, compte rendu du lot 15) : les champs à listes (imprimante et papier qui en dépend, les lots, la nature, le
/// code processus et l'entité source qui en découle, avec sa recopie sur les autres paramètres de la maquette, :501-506, et la
/// question « Un autre paramètre édition de la maquette dispose d'un code processus différent… », :508-513), la requête SQL, les
/// « autres paramètres » (SaveAutresParametres, :516) : l'écran les montre en lecture. AJOUTER et DUPLIQUER (un code neuf,
/// LinkDonnees :111-131, Dupliquer :482-492) et EXPORT (fichier SQL ou XML sur le poste, :400-430, agent d'Amitel) : non repris.
/// La sauvegarde V2 réécrit aussi tout le XML (ménage des clés lues, CODE_DOCUMENT corrigé…) : la V3 n'y touche qu'aux clés changées.
/// TRACE : famille « reglage », « a modifie » / « a supprime ».
/// </summary>
public static class EcritureParametresEdition
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Edition des maquettes";

    // Les champs, sous le titre de leur contrôle dans RIG v2 (MetierTitre, OPE_PARAM_EDT_MAJ.Designer.cs) : les clés des Valeurs du geste.
    public const string ChampLibelle = "Libellé paramètre édition";
    public const string ChampExemplaires = "Nb exemplaires";
    public const string ChampRectoVerso = "Recto/Verso";
    public const string ChampAgrafage = "Agrafage";
    public const string ChampAffichage = "Affichage avant impression";
    public const string ChampNonModifiable = "Doc non modifiable";
    public const string ChampImpression = "Impression";
    public const string ChampGarderLot1 = "Garder edition lot 1";

    /// <summary>Une case du formulaire : son titre, sa colonne et sa clé du XML PRMED_LISTE (null : colonne seule).</summary>
    private sealed record Case(string Champ, string Colonne, string? CleXml);

    private static readonly Case[] Cases =
    [
        new(ChampRectoVerso, "PRMED_RECTO_VERSO", "RECTO_VERSO"),
        new(ChampAgrafage, "PRMED_AGRAFAGE", "AGRAFAGE"),
        new(ChampAffichage, "PRMED_AFFICHER_AVANT_IMPRESSION", "AFFICHER_AVANT_IMPRESSION"),
        new(ChampNonModifiable, "PRMED_READ_ONLY", "READ_ONLY"),
        new(ChampImpression, "PRMED_IMPRESSION", "IMPRESSION"),
        new(ChampGarderLot1, "PRMED_GARDER_EDITION_LOT1", null),
    ];

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Enregistrer, TypeGeste.Supprimer];

    /// <summary>Joue le geste sur le paramètre Ids[0], pour un agent <paramref name="agentAmitel"/> ou du greffe.</summary>
    public static Task<IssueGeste> JouerAsync(GesteEnCours geste, bool agentAmitel, CancellationToken ct) => geste.Demande.Type switch
    {
        TypeGeste.Enregistrer => EnregistrerAsync(geste, agentAmitel, ct),
        TypeGeste.Supprimer => SupprimerAsync(geste, agentAmitel, ct),
        _ => throw new InvalidOperationException($"{Ecran} n'écrit pas le geste {geste.Demande.Type} (la route le refuse avant)."),
    };

    /// <summary>Ce que le geste lit du paramètre avant d'écrire.</summary>
    private sealed record Lu(string Code, string Libelle, int Exemplaires, string Processus, string EntiteSource, string Lot, string LotLrar,
                             string TypeGeneration, string TypeMaquette, string Liste, Dictionary<string, bool> Cases);

    private static async Task<Lu?> LireAsync(GesteEnCours geste, bool agentAmitel, CancellationToken ct)
    {
        var code = geste.Demande.Ids.FirstOrDefault()?.Trim() ?? "";
        if (code.Length == 0) return null;
        // Les maquettes que l'agent voit (OPE_MAQUETTE.cs:81-84, les mêmes types que la lecture) : un paramètre hors d'elles n'est pas à
        // son écran. Les types sont des constantes, jamais une saisie.
        var types = string.Join(", ", (agentAmitel ? LectureMaquettesEdition.TypesAmitel : LectureMaquettesEdition.TypesGreffe).Select(t => $"'{t}'"));
        await using var r = await geste.LireAsync($"""
            SELECT p.PRMED_CODE, p.PRMED_LIBELLE, p.PRMED_NOMBRE_EXEMPLAIRES, p.PRMED_CODE_PROCESSUS, p.PRMED_CODE_ENTITE_SOURCE, p.PRMED_LOT,
                   p.PRMED_LOT_LRAR, p.PRMED_TYPE_GENERATION, p.PRMED_LISTE, q.MQTT_TYPE_MAQUETTE,
                   {string.Join(", ", Cases.Select(c => "p." + c.Colonne))}
            FROM v2.PARAMETRE_EDITION AS p WITH (UPDLOCK)
            INNER JOIN v2.MAQUETTE AS q ON q.MQTT_CODE_MAQUETTE = p.PRMED_CODE_MAQUETTE
            WHERE p.PRMED_CODE = @code AND q.MQTT_TYPE_MAQUETTE IN ({types})
            """, ct, new SqlParameter("@code", code));
        if (!await r.ReadAsync(ct)) return null;
        return new Lu(FormeLigne.Texte(r, "PRMED_CODE").Trim(), FormeLigne.Texte(r, "PRMED_LIBELLE"),
            r["PRMED_NOMBRE_EXEMPLAIRES"] is int n ? n : 0, FormeLigne.Texte(r, "PRMED_CODE_PROCESSUS").Trim(),
            FormeLigne.Texte(r, "PRMED_CODE_ENTITE_SOURCE").Trim(), FormeLigne.Texte(r, "PRMED_LOT").Trim(), FormeLigne.Texte(r, "PRMED_LOT_LRAR").Trim(),
            FormeLigne.Texte(r, "PRMED_TYPE_GENERATION").Trim(), FormeLigne.Texte(r, "MQTT_TYPE_MAQUETTE").Trim(), FormeLigne.Texte(r, "PRMED_LISTE"),
            Cases.ToDictionary(c => c.Champ, c => r[c.Colonne] is true));
    }

    private static string Introuvable(GesteEnCours geste)
        => $"Paramètre d'édition introuvable dans la base du greffe ({geste.Demande.Ids.FirstOrDefault()}).";

    // ============================================================ SUPPRIMER (OPE_PARAM_EDT_MAJ.Supprimer, :529-541)

    private static async Task<IssueGeste> SupprimerAsync(GesteEnCours geste, bool agentAmitel, CancellationToken ct)
    {
        // Le bouton n'est ouvert qu'à Amitel (:111) : la V2 n'a pas de message, le bouton est grisé. Ce refus le dit.
        if (!agentAmitel) return IssueGeste.Refuser("Seul un agent d'Amitel supprime un paramètre d'édition (RIG v2 : bouton Supprimer grisé pour le greffe).");
        if (await LireAsync(geste, agentAmitel, ct) is not { } p) return IssueGeste.Refuser(Introuvable(geste));
        if (await EmploiAsync(geste, p.Code, ct) is { } refus) return IssueGeste.Refuser(refus);
        await geste.EcrireAsync("DELETE FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @code", ct, new SqlParameter("@code", p.Code));
        return IssueGeste.Ecrit($"Paramètre d'édition {p.Code} supprimé.", [p.Code],
            new TraceGeste("reglage", $"le paramètre d'édition {p.Code}", p.Code, "a supprime",
                new Dictionary<string, string> { ["Libellé"] = p.Libelle, ["Type de maquette"] = p.TypeMaquette }, null));
    }

    /// <summary>CanSupp (:259-307) : le message du premier emploi trouvé, dans l'ordre de la V2 ; null si le code n'est employé nulle part.</summary>
    private static async Task<string?> EmploiAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        var evenements = new List<string>();
        await using (var r = await geste.LireAsync("SELECT CEPED_CODE_EVE_PRO FROM v2.CEP_EDITION WHERE CEPED_CODE_EDITION = @code ORDER BY CEPED_ID_CEPED", ct,
                         new SqlParameter("@code", code)))
            while (await r.ReadAsync(ct)) evenements.Add(FormeLigne.Texte(r, "CEPED_CODE_EVE_PRO"));
        if (evenements.Count > 0)
        {
            // « (A - B - » privé de ses deux derniers caractères, puis « ) » : « (A - B ) », au caractère près (:268-274).
            var liste = "(" + string.Concat(evenements.Select(e => e + " - "));
            liste = liste.Remove(liste.Length - 2) + ")";
            return $"Le code param {code} est lié aux évènements suivants : {liste}.{Environment.NewLine}" +
                   "Vous devez supprimer le ou les liens dans la table CODE_EVE_PRO de l'outil des tables de référence.";
        }
        if (await ExisteAsync(geste, "SELECT 1 FROM v2.ADAPTATEUR_ULT WHERE ADPUL_CODE = 'EDITPARAM' AND ADPUL_VALEUR_DEFAUT LIKE @code", code, ct))
            return $"Le code param {code} est utilisé dans les processus.{Environment.NewLine}Vous ne pouvez pas le supprimer.";
        if (await ExisteAsync(geste, "SELECT 1 FROM v2.VIDIGED_PARAMETRAGE WHERE UPPER(VGPRM_PARAMETRE_EDITION_CFEC) = UPPER(@code)", code, ct))
            return $"Le code param {code} est utilisé dans le paramétrage de vidiged.{Environment.NewLine}Vous ne pouvez pas le supprimer.";
        if (await ExisteAsync(geste, "SELECT 1 FROM v2.TYPE_DECISION WHERE R_TYPEDECISI_PARAMETRE_EDITION = @code", code, ct))
            return $"Le code param {code} est utilisé dans les types decisions.{Environment.NewLine}Vous ne pouvez pas le supprimer.";
        if (await ExisteAsync(geste, "SELECT 1 FROM v2.COURRIER_JUDICIAIRE WHERE COUJU_CODE_PRMED = @code", code, ct))
            return $"Le code param {code} est utilisé dans les courriers judiciaires.{Environment.NewLine}Vous ne pouvez pas le supprimer.";
        return null;
    }

    private static async Task<bool> ExisteAsync(GesteEnCours geste, string sql, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct, new SqlParameter("@code", code));
        return await r.ReadAsync(ct);
    }

    // ============================================================ ENREGISTRER (OPE_PARAM_EDT_MAJ.Valider, :494-527)

    private static async Task<IssueGeste> EnregistrerAsync(GesteEnCours geste, bool agentAmitel, CancellationToken ct)
    {
        if (await LireAsync(geste, agentAmitel, ct) is not { } p) return IssueGeste.Refuser(Introuvable(geste));
        string? Saisi(string champ) => geste.Demande.Valeurs.TryGetValue(champ, out var v) ? v : null;

        // Un champ que la V3 n'écrit pas encore n'est pas avalé en silence : le geste le refuse en le nommant.
        var connus = new HashSet<string>([ChampLibelle, ChampExemplaires, .. Cases.Select(c => c.Champ)], StringComparer.Ordinal);
        if (geste.Demande.Valeurs.Keys.FirstOrDefault(k => !connus.Contains(k)) is { } inconnu)
            return IssueGeste.Refuser($"« {inconnu} » ne s'enregistre pas encore dans RIG v3 : rien n'est enregistré.");
        // :210 -- le formulaire d'une maquette REDA (ou de type inconnu, que LireAsync n'ouvre pas) est en lecture seule ; la V2 n'a pas
        // de message, rien ne s'y saisit.
        if (p.TypeMaquette == "REDA")
            return IssueGeste.Refuser("Les paramètres d'une maquette REDA se lisent sans se modifier (RIG v2 : formulaire en lecture seule).");

        var libelle = Saisi(ChampLibelle) is { } l ? l.Trim() : p.Libelle;
        if (libelle.Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampLibelle));
        if (libelle.Length > 100) return IssueGeste.Refuser($"« {ChampLibelle} » : 100 caractères au plus.");

        var exemplaires = p.Exemplaires;
        if (Saisi(ChampExemplaires) is { } e)
        {
            // MetierRegExSaisie « [0-9] » (Designer :237) : des chiffres seuls ; vide = obligatoire.
            if (e.Trim().Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampExemplaires));
            if (!e.Trim().All(char.IsAsciiDigit) || !int.TryParse(e.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out exemplaires))
                return IssueGeste.Refuser($"« {ChampExemplaires} » : des chiffres seuls ({e.Trim()}).");
        }
        var cases = new Dictionary<string, bool>(p.Cases, StringComparer.Ordinal);
        foreach (var c in Cases)
            if (Saisi(c.Champ) is { } v) cases[c.Champ] = Coche(v);
        // :140-141 -- la case n'est montrée qu'à Amitel, pour une fusion RIG : ailleurs elle ne change pas.
        if (cases[ChampGarderLot1] != p.Cases[ChampGarderLot1] && !(agentAmitel && p.TypeGeneration == "FUSION_RIG"))
            return IssueGeste.Refuser($"« {ChampGarderLot1} » ne se montre qu'à un agent d'Amitel, pour une génération FUSION_RIG (RIG v2) : rien n'est enregistré.");

        // DoVerifier (:309-327), sur les valeurs que la V2 aurait à l'écran (le code processus et l'entité source ne changent pas ici).
        if (p.Processus == "EDIT_LDEC" && p.EntiteSource != "DECISION_JUD")
            return IssueGeste.Refuser("Si vous saisissez EDIT_LDEC en code processus vous devez saisir DECISION_JUD en code entité source.");
        if (exemplaires < 1) return IssueGeste.Refuser("Le nombre d'exemplaires est obligatoire.");
        if (exemplaires != 1 && (await FileAmitelAsync(geste, p.Lot, ct) || await FileAmitelAsync(geste, p.LotLrar, ct)))
            return IssueGeste.Refuser("Le nombre d'exemplaires doit être à 1 si on veut imprimer à AMITEL. Pensez à prévenir le greffe.");

        // Ce qui change, colonne et clé du XML.
        var avant = new Dictionary<string, string>(StringComparer.Ordinal);
        var apres = new Dictionary<string, string>(StringComparer.Ordinal);
        var colonnes = new List<(string Colonne, object Valeur)>();
        var xml = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (libelle != p.Libelle)
        {
            (avant[ChampLibelle], apres[ChampLibelle]) = (p.Libelle, libelle);
            colonnes.Add(("PRMED_LIBELLE", libelle));
            xml["LIBELLE"] = null;   // lue par la V2 par-dessus la colonne, jamais réécrite (:443) : retirée, comme une sauvegarde V2
        }
        if (exemplaires != p.Exemplaires)
        {
            (avant[ChampExemplaires], apres[ChampExemplaires]) = (p.Exemplaires.ToString(CultureInfo.InvariantCulture), exemplaires.ToString(CultureInfo.InvariantCulture));
            colonnes.Add(("PRMED_NOMBRE_EXEMPLAIRES", exemplaires));
            xml["NOMBRE_EXEMPLAIRES"] = exemplaires.ToString(CultureInfo.InvariantCulture);
        }
        foreach (var c in Cases.Where(c => cases[c.Champ] != p.Cases[c.Champ]))
        {
            (avant[c.Champ], apres[c.Champ]) = (p.Cases[c.Champ] ? "Oui" : "Non", cases[c.Champ] ? "Oui" : "Non");
            colonnes.Add((c.Colonne, cases[c.Champ]));
            if (c.CleXml is { } cle) xml[cle] = cases[c.Champ] ? "1" : "0";
        }
        if (colonnes.Count == 0) return IssueGeste.Refuser($"Paramètre d'édition {p.Code} : aucun champ n'a changé, rien n'est enregistré.");

        var liste = AvecCles(p.Liste, xml);
        var parametres = colonnes.Select((c, i) => new SqlParameter($"@v{i}", c.Valeur)).ToList();
        parametres.Add(new SqlParameter("@code", p.Code));
        var miseAJour = string.Join(", ", colonnes.Select((c, i) => $"{c.Colonne} = @v{i}"));
        if (liste != p.Liste)
        {
            miseAJour += ", PRMED_LISTE = @liste";
            parametres.Add(new SqlParameter("@liste", System.Data.SqlDbType.VarChar, 4000) { Value = liste });
        }
        await geste.EcrireAsync($"UPDATE v2.PARAMETRE_EDITION SET {miseAJour} WHERE PRMED_CODE = @code", ct, [.. parametres]);
        return IssueGeste.Ecrit($"Paramètre d'édition {p.Code} enregistré.", [p.Code],
            new TraceGeste("reglage", $"le paramètre d'édition {p.Code}", p.Code, "a modifie", avant, apres));
    }

    private static bool Coche(string valeur) => valeur.Trim().ToLowerInvariant() is "oui" or "true" or "1";

    /// <summary>Lot.IsFileAmitel (Lot.cs:40-44) du lot <paramref name="nom"/> lu dans LOT, comme la liste ComboBoxLot : faux sans lot.</summary>
    private static async Task<bool> FileAmitelAsync(GesteEnCours geste, string nom, CancellationToken ct)
    {
        if (nom.Length == 0) return false;
        await using var r = await geste.LireAsync("SELECT LOT_NOM FROM v2.LOT WHERE LOT_NOM = @nom", ct, new SqlParameter("@nom", nom));
        return await r.ReadAsync(ct) && FormeLigne.Texte(r, "LOT_NOM").ToUpperInvariant().StartsWith("AMI", StringComparison.Ordinal);
    }

    /// <summary>
    /// Le XML PRMED_LISTE (« &lt;PARAMETRES CLE="valeur" … /&gt; », AmiSystem.ListeParametre.GetXmlParametre, ListeParametre.cs:310-330)
    /// avec <paramref name="cles"/> posées (null : retirée) : un attribut existant garde sa place, un nouveau va à la fin ; les autres ne
    /// bougent pas. Un XML vide ou illisible (la V2 l'ignore alors, SetXmlParametre :373-391) repart de « &lt;PARAMETRES /&gt; ».
    /// </summary>
    internal static string AvecCles(string liste, IReadOnlyDictionary<string, string?> cles)
    {
        XElement racine;
        try { racine = string.IsNullOrWhiteSpace(liste) ? new XElement("PARAMETRES") : XElement.Parse(liste); }
        catch (XmlException) { racine = new XElement("PARAMETRES"); }
        var avant = racine.ToString(SaveOptions.DisableFormatting);
        foreach (var (cle, valeur) in cles)
        {
            // Une cle de la V2 se compare sans casse (ListeParametre._ClefUpperKey) : l'attribut existant garde son nom.
            var attribut = racine.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, cle, StringComparison.OrdinalIgnoreCase));
            if (valeur is null) attribut?.Remove();
            else if (attribut is null) racine.Add(new XAttribute(cle, valeur));
            else attribut.Value = valeur;
        }
        var apres = racine.ToString(SaveOptions.DisableFormatting);
        // Rien de change : le texte d'origine reste tel quel (aucune reecriture de forme).
        if (apres == avant) return liste;
        return racine.HasAttributes || racine.HasElements ? apres : "";
    }
}
