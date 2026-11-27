using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurLettresRecommandees;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Livre de répertoire » (PROC_JLR) : « Configurer l'édition » d'un destinataire genere ses lettres recommandees dans la FILE DES
/// EDITIONS (v2.DEMANDE_EDITION), comme RIG v2. Mode d'emploi : EcrivainEcran.cs ; les lignes : LecteurLettresRecommandees.cs. Plan
/// « RIG v3 100 % fonctionnel », lot 13, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_JLR (proc_jlr.md, Detail OPE ; D:\RigApplication\Source\RIG\DLL\Processus\PROC_JLR).
/// CONTROLES (DoVerifier, proc_jlr.md:314-335), avant toute ecriture : une saisie sans designation NI adresse ne compte pas, et sans
/// aucun recommande « Vous devez saisir au moins un recommandé » (PROC_JLR.cs:59-63) ; designation vide « Saisie désignation »,
/// adresse vide « Saisie adresse » (OPE_LR.cs:71-76) ; l'adresse tient en 3 lignes (OPE_LR.Designer.cs:85) ; le type de lettre est
/// celui de l'etape choisie (LR ou LRAR) ; une lettre simple quand la plage de LR n'est pas commandee (NUMERO_METIER 'LRN',
/// NMMTR_CHRONO_DEBUT = 1 : la saisie LR est masquee, « Impossible vous n'avez pas commandé de plage de LR », PROC_JLR.cs:31-44,
/// OPE_LR.Designer.cs:106) ; le parametre d'edition JLR / JLRAR absent du greffe (la V2 n'edite alors rien, sans le dire :
/// proc_jlr.md:293-297 -- la V3 refuse et le dit). Le nombre d'exemplaires vide ou nul repasse a 1 (OPE_LR.cs:26-30).
/// CE QUI S'ECRIT : la demande du processus JLR, non facturee (MetierFacture = false, PROC_JLR.cs:27-28), validee
/// (EditionsV2.DemandeProcessusValideeAsync) -- l'entite source des lettres ; puis, par exemplaire, une demande d'edition du parametre
/// (EditionsV2.DemandeEditionAsync : CreateDemandeEditionByParametreGeneration, DemandeEdition.cs:2579-2600), avec la designation, le
/// bloc adresse, le libelle source « designation 1-n » (une occurrence par geste, NumOrdre 1) et la source DEMANDE (OPE_LR.cs:56-62).
/// NON REPRIS (nommes) : vingt destinataires dans un meme geste (MetierCardinaliteMaximum = 20) -- un destinataire par geste, une
/// demande par geste ; le XML de generation (DMNED_PARAMETRE_GENERATION, comme les lots 6 et 10).
/// TRACE : famille « demande », « a cree », la demande et ses editions.
/// </summary>
internal sealed class EcrivainLettresRecommandees : IEcrivainEcran
{
    public string Procedure => "PROC_JLR";
    public string Ecran => "Livre de répertoire";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    internal const string SectionDestinataire = "Destinataire", ChampDesignation = "Désignation", ChampAdresse = "Adresse (3 lignes au plus)";
    internal const string SectionExemplaires = "Exemplaires", ChampNombre = "Nombre d'exemplaires";
    internal const string SectionType = "Type d'envoi", ChampType = "Type de lettre";

    internal const string AuMoinsUn = "Vous devez saisir au moins un recommandé";
    internal const string SansPlage = "Impossible vous n'avez pas commandé de plage de LR";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var designation = CreationV2.Saisi(geste, SectionDestinataire, ChampDesignation);
        var adresse = (geste.Valeur(SectionDestinataire, ChampAdresse) ?? "").Trim();
        if (designation.Length == 0 && adresse.Length == 0) return IssueGeste.Refuser(AuMoinsUn);
        if (designation.Length == 0) return IssueGeste.Refuser("Saisie désignation");
        if (adresse.Length == 0) return IssueGeste.Refuser("Saisie adresse");
        var lignes = adresse.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        if (lignes.Length > 3) return IssueGeste.Refuser($"« {ChampAdresse} » : l'adresse tient en 3 lignes au plus ({lignes.Length} saisies).");
        var nombre = int.TryParse(CreationV2.Saisi(geste, SectionExemplaires, ChampNombre), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 ? n : 1;
        var type = CreationV2.Saisi(geste, SectionType, ChampType);
        var code = type switch { Simple => ParametreSimple, AvecAccuse => ParametreAccuse, _ => null };
        if (code is null) return IssueGeste.Refuser(CreationV2.Obligatoire(ChampType) + $" ({Simple} ou {AvecAccuse})");
        if (code == ParametreSimple && await PlageNonCommandeeAsync(geste, ct)) return IssueGeste.Refuser(SansPlage);
        if (await EditionsV2.ParametreAsync(geste, code, ct) is not { } parametre)
            return IssueGeste.Refuser($"Le paramètre d'édition {code} n'existe pas dans ce greffe : RIG v2 ne génère alors aucune lettre.");

        var (idDemande, numero, refus) = await EditionsV2.DemandeProcessusValideeAsync(geste, "JLR", designation, ct);
        if (refus is not null) return IssueGeste.Refuser(refus);
        var source = EditionsV2.Texte(idDemande);
        var bloc = string.Join("\r\n", lignes);
        var editions = new List<string>();
        for (var i = 0; i < nombre; i++)
        {
            var idEdition = await EditionsV2.DemandeEditionAsync(geste, idDemande, parametre, null, "DEMANDE", source, null, null, ct);
            await geste.EcrireAsync("""
                UPDATE v2.DEMANDE_EDITION SET DMNED_DESIGNATION_DESTINATAIRE = @designation, DMNED_BLOC_ADRESSE_DESTINATAIRE = @adresse,
                    DMNED_LIBELLE_SOURCE = @libelle WHERE DMNED_ID_DMNED = @id
                """, ct, new SqlParameter("@designation", designation), new SqlParameter("@adresse", bloc),
                new SqlParameter("@libelle", $"{designation} 1-{i + 1}"), new SqlParameter("@id", idEdition));
            editions.Add(EditionsV2.Texte(idEdition));
        }
        var trace = new TraceGeste("demande", $"la demande {numero} ({type}, {designation})", source, "a cree", null,
            new { Demande = numero, Parametre = code, Destinataire = designation, Exemplaires = nombre, Editions = editions });
        return IssueGeste.Ecrit($"{type} pour {designation} : {nombre} édition{(nombre > 1 ? "s" : "")} dans la file des éditions (demande {numero}).",
            [.. editions], trace);
    }

    /// <summary>La plage de LR n'est pas commandee : la premiere ligne NUMERO_METIER de type LRN a NMMTR_CHRONO_DEBUT = 1 (PROC_JLR.cs:35-39,
    /// la requete de la V2 telle quelle, sans filtre de greffe ni d'annee : son defaut est nomme par la fiche, proc_jlr.md:245-253).</summary>
    private static async Task<bool> PlageNonCommandeeAsync(GesteEnCours geste, CancellationToken ct)
    {
        if ((await EditionsV2.ColonnesAsync(geste, "v2.NUMERO_METIER", ct)).Count == 0) return false;
        await using var r = await geste.LireAsync("SELECT TOP (1) NMMTR_CHRONO_DEBUT FROM v2.NUMERO_METIER WHERE NMMTR_TYPE_METIER = 'LRN'", ct);
        return await r.ReadAsync(ct) && !r.IsDBNull(0) && Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture) == 1;
    }
}
