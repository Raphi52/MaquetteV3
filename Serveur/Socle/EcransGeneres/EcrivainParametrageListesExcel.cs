using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Listes Excel » (PROC_PARAM_LISTEEXCEL) : « Exporter », le bouton EXPORT de RIG v2, sur la liste ouverte. Mode d'emploi :
/// EcrivainEcran.cs ; les listes et leur fiche : LecteurParametrageListesExcel.cs. Plan « RIG v3 complet, mesure par le banc du parcours
/// complet », lot 2, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\Judiciaire\Exe\RIG_CLIENT_PARAM_LISTEEXCEL (le plugin du menu, PROC_PARAM_LISTEEXCEL,
/// est une coquille : LecteurParametrageListesExcel.cs, en tete) :
///   - le bouton (FORM_PARAM_LISTEEXCEL.cs:23-33) : EXPORT, le seul de la barre (VALIDER, VERIFIER et INTERROMPRE caches, :19-21) ;
///   - le controle (Processus.Verifier -> OPE_LISTE.DoVerifier, Edition\OPE_LISTE.cs:147-150) : moins de deux champs dans la grille
///     « Ordre des champs » (les champs SELECT de la liste, ParametrageListe.cs:81-90) -- « 2 champs minimum sont requis », au mot pres ;
///   - l'export (OPE_LISTE.Exporter, :90-145) : un SCRIPT SQL, « exportListeExcel_&lt;type&gt;.sql », ecrit dans un dossier choisi du poste
///     (:125-136), « Fichier exporte : &lt;chemin&gt; » (:138), que l'equipe Amitel joue ensuite. Pour une liste neuve, le script pose la
///     liste, sa clause FROM et ses champs, chacun garde par un « IF NOT EXISTS » sur son code (:96-121) ; pour une liste deja en base,
///     seulement ce que l'ecran a change. RIG v2 n'ecrit RIEN dans la base du greffe.
/// RIG v3 : decision du plan (lot 3, 28/11/2026) -- un geste RIG v2 qui n'ecrit rien en base (un export) pose son edition dans la file des
/// editions (EditionsV2.SansParametre). La demande d'edition porte le nom du fichier de la V2 (son libelle), le processus PARAM_LISTEEXCEL
/// et, dans DMNED_PARAMETRE_GENERATION (comme EditionsEtatsV2.ListeParametre : un element PARAMETRES, un attribut par cle), le type de la
/// liste, son nombre de champs et le SCRIPT. Type EMISSION_SIMPLE (DemandeEdition.cs:160-164, « le document n'est pas genere par le
/// service ») : le service d'edition n'execute JAMAIS le texte SQL du script, qui reste a jouer par Amitel comme dans la V2.
/// Le script est celui que la V2 ecrit pour une liste NEUVE, sur la liste lue en base : l'ecran de la V3 est en lecture, rien n'y est
/// change -- l'export « des seuls changements » de la V2 serait vide. Chaque ordre reprend la forme de la V2 (IF NOT EXISTS sur le code,
/// puis l'INSERT, requeteInsert de SqlBase.RequeteSauve, Outils\DLL\RigTools\SQL\SqlBase.cs:505-509), apostrophes doublees (:108).
/// LE FICHIER (lot 16 du plan-f4fb097f, 28/11/2026 -- ecart releve a la reception du lot 2 : « la demande n'est jamais executee, aucun
/// fichier ») : le geste REND le fichier de la V2 (IssueGeste.Fait.Fichier -> ResultatGeste.Fichier), que l'ecran enregistre sur le poste
/// (le telechargement de la fenetre, web/src/ecrans/formes/commun.tsx, useGeste) : « exportListeExcel_&lt;type&gt;.sql », le texte des trois
/// StreamWriter.WriteLine de la V2 (:131-135 : la liste, la clause FROM, les champs, chacun suivi d'une fin de ligne) -- le SCRIPT plus la
/// fin de ligne de son dernier WriteLine. RIG v2 ne produit PAS de classeur Excel ici : c'est l'« Export Excel » (PROC_LISTEEXCEL) qui
/// sort les listes d'instances dans Excel, avec ce parametrage.
/// NON REPRIS (nommes) : le choix du dossier (FolderBrowserDialog, :125-128 : le telechargement de la fenetre le remplace) et le fichier
/// deja present non ecrase, sans message (:130) ; la saisie et l'ordre des champs, que l'ecran de la V3 ne modifie pas ; le
/// droit d'executer le processus (FORM_ACCUEIL.cs:1624) : PARAM_LISTEEXCEL n'est pas une fonction de la base V3 (soc.Fonction), le droit
/// par procedure reste celui d'EcrivainEcran.cs (en tete).
/// TRACE : famille « demande », « a cree », l'export pose (fichier, type, champs).
/// </summary>
internal sealed class EcrivainParametrageListesExcel : IEcrivainEcran
{
    public string Procedure => "PROC_PARAM_LISTEEXCEL";
    public string Ecran => "Listes Excel";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres (le bouton EXPORT de la V2).</summary>
    internal const string Exporter = "Exporter";

    /// <summary>Le processus de RIG v2 (DMNED_CODE_PROCESSUS).</summary>
    internal const string Processus = "PARAM_LISTEEXCEL";

    /// <summary>Le controle de la V2 (OPE_LISTE.cs:149), au mot pres.</summary>
    internal const string DeuxChamps = "2 champs minimum sont requis";

    /// <summary>Le fichier de la V2 (OPE_LISTE.cs:128).</summary>
    internal static string Fichier(string type) => $"exportListeExcel_{type}.sql";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Exporter)
            return IssueGeste.Refuser($"Geste « {geste.Demande.Nom} » inconnu de l'écran « {Ecran} » : RIG v2 n'a que « {Exporter} ».");
        // La V2 exporte la liste ouverte, une a la fois (message de la V3 : la V2 n'a pas d'export sans liste ouverte).
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length != 1) return IssueGeste.Refuser("Ouvrez une liste Excel pour l'exporter : RIG v2 exporte la liste ouverte, une à la fois.");
        var id = CriteresConsultation.Entier(ids[0]);

        // La liste, sous le filtre de la recherche de la V2 (LecteurParametrageListesExcel.Filtre).
        string type, libelle, liste;
        await using (var r = await geste.LireAsync($"""
                         SELECT l.PRMLS_VALEUR, l.PRMLS_LIBELLE, l.PRMLS_LISTE FROM v2.PARAMETRAGE_LISTE AS l
                         WHERE l.PRMLS_ID_PRMLS = @id AND {LecteurParametrageListesExcel.Filtre}
                         """, ct, new SqlParameter("@id", (object?)id ?? DBNull.Value)))
        {
            if (id is null || !await r.ReadAsync(ct)) return IssueGeste.Refuser($"Liste Excel introuvable ({ids[0]}) : relisez les listes.");
            type = FormeLigne.Texte(r, "PRMLS_VALEUR");
            libelle = FormeLigne.Texte(r, "PRMLS_LIBELLE");
            liste = FormeLigne.Texte(r, "PRMLS_LISTE");
        }

        // Ses champs SELECT (la grille « Ordre des champs ») et sa clause FROM, dans l'ordre de la fiche.
        var champs = new List<Champ>();
        Champ? from = null;
        await using (var r = await geste.LireAsync("""
                         SELECT c.PRMLX_CODE_CHAMP, c.PRMLX_LIBELLE, c.PRMLX_TEXTE, c.PRMLX_NUM_ORDRE, c.PRMLX_NUM_ORDRE_INIT, c.PRMLX_TYPE_TEXTE
                         FROM v2.PARAMETRAGE_LISTE_XLS AS c
                         WHERE c.PRMLX_TYPE_LISTE = @type AND c.PRMLX_TYPE_TEXTE IN ('SELECT', 'FROM')
                         ORDER BY CASE c.PRMLX_TYPE_TEXTE WHEN 'FROM' THEN 1 ELSE 0 END, c.PRMLX_NUM_ORDRE_INIT, c.PRMLX_CODE_CHAMP
                         """, ct, new SqlParameter("@type", type)))
        {
            while (await r.ReadAsync(ct))
            {
                // Le texte SQL tel qu'en base, blancs compris : la V2 l'ecrit tel quel (ToSQL), et la clause FROM y commence par un blanc
                // (« ' FROM INSTANCE ...' » dans RIG_DEV) que l'export Excel colle apres les champs SELECT (FormeLigne.Texte le couperait).
                var champ = new Champ(FormeLigne.Texte(r, "PRMLX_CODE_CHAMP"), FormeLigne.Texte(r, "PRMLX_LIBELLE"), r["PRMLX_TEXTE"] as string ?? "",
                    CriteresConsultation.EntierDe(r, "PRMLX_NUM_ORDRE"), CriteresConsultation.EntierDe(r, "PRMLX_NUM_ORDRE_INIT") ?? 0,
                    FormeLigne.Texte(r, "PRMLX_TYPE_TEXTE"));
                if (champ.TypeTexte == "FROM") from ??= champ;
                else champs.Add(champ);
            }
        }
        // OPE_LISTE.DoVerifier (:147-150) : deux champs au moins, avant tout export.
        if (champs.Count < 2) return IssueGeste.Refuser(DeuxChamps);

        var script = Script(type, libelle, liste, from, champs);
        var fichier = Fichier(type);
        var parametres = new XElement("PARAMETRES",
                new XAttribute("FICHIER", fichier), new XAttribute("NB_CHAMPS", champs.Count.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("SCRIPT", script), new XAttribute("TYPE_LISTE", type))
            .ToString(SaveOptions.DisableFormatting);
        var colonnes = await EditionsV2.ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        var autres = new Dictionary<string, object?>();
        if (colonnes.Contains("DMNED_CODE_PROCESSUS")) autres["DMNED_CODE_PROCESSUS"] = Processus;
        if (colonnes.Contains("DMNED_PARAMETRE_GENERATION")) autres["DMNED_PARAMETRE_GENERATION"] = parametres;
        var idEdition = await EditionsV2.DemandeEditionAsync(geste, null, EditionsV2.SansParametre(fichier, "EMISSION_SIMPLE"), fichier,
            null, null, null, null, ct, autres);

        var idTexte = EditionsV2.Texte(idEdition);
        var trace = new TraceGeste("demande", $"l'export « {fichier} »", idTexte, "a cree", null,
            new { Fichier = fichier, Liste = type, Champs = champs.Count, File = "file des éditions" });
        // « Fichier exporté : <chemin> » (OPE_LISTE.cs:138) : l'ecran enregistre le fichier rendu sur le poste (en tete, « LE FICHIER »).
        // fix-ok: pas un correctif a l'aveugle -- ecart mesure a la reception du lot 2 (aucun fichier produit) ; source V2 lue (OPE_LISTE.cs:90-145).
        return new IssueGeste.Fait($"Fichier exporté : {fichier} (le script SQL de la liste, enregistré sur le poste) ; l'export est aussi placé dans la file des éditions.",
            [trace], [idTexte]) { Fichier = new FichierGeste(fichier, script + "\r\n") };
    }

    private sealed record Champ(string Code, string Libelle, string Texte, int? Ordre, int OrdreInit, string TypeTexte);

    /// <summary>Le script de la V2 pour une liste (OPE_LISTE.cs:96-121) : la liste, sa clause FROM, puis ses champs, chacun garde.</summary>
    private static string Script(string type, string libelle, string liste, Champ? from, List<Champ> champs)
    {
        static string Sql(string? s) => s is null ? "NULL" : "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
        static string Entier(int? n) => n is { } v ? v.ToString(CultureInfo.InvariantCulture) : "NULL";
        static string Insert(Champ c, string type) =>
            "INSERT INTO PARAMETRAGE_LISTE_XLS (PRMLX_TYPE_LISTE, PRMLX_TEXTE, PRMLX_NUM_ORDRE, PRMLX_LIBELLE, PRMLX_TYPE_TEXTE, PRMLX_NUM_ORDRE_INIT, PRMLX_CODE_CHAMP) "
            + $"VALUES ({Sql(type)}, {Sql(c.Texte)}, {Entier(c.Ordre)}, {Sql(c.Libelle)}, {Sql(c.TypeTexte)}, {Entier(c.OrdreInit)}, {Sql(c.Code)})";
        var sb = new StringBuilder();
        sb.Append($"IF NOT EXISTS(SELECT PRMLS_VALEUR FROM PARAMETRAGE_LISTE WHERE PRMLS_VALEUR = {Sql(type)}) \r\n");
        sb.Append($"INSERT INTO PARAMETRAGE_LISTE (PRMLS_VALEUR, PRMLS_LIBELLE, PRMLS_LISTE) VALUES ({Sql(type)}, {Sql(libelle)}, {Sql(liste)})\r\n");
        if (from is not null)
        {
            sb.Append($"IF NOT EXISTS(SELECT PRMLX_CODE_CHAMP FROM PARAMETRAGE_LISTE_XLS WHERE PRMLX_CODE_CHAMP = {Sql(from.Code)}) \r\n");
            sb.Append(Insert(from, type)).Append("\r\n");
        }
        foreach (var c in champs)
        {
            sb.Append($"IF NOT EXISTS(SELECT PRMLX_CODE_CHAMP FROM PARAMETRAGE_LISTE_XLS WHERE PRMLX_CODE_CHAMP = {Sql(c.Code)}) \r\n");
            sb.Append(Insert(c, type)).Append("; \r\n");
        }
        return sb.ToString();
    }
}
