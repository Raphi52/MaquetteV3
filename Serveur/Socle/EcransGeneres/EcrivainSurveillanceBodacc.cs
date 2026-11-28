using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Surveillance BODACC » (PROC_SURVBOD) : les trois gestes de RIG v2 sur les annonces au controle qualite non verifie. Mode
/// d'emploi : EcrivainEcran.cs ; la liste : LecteurRejetsBodacc.cs (Id = l'annonce, BDCCR_ID_BDCRC). Plan « RIG v3 complet, mesure par
/// le banc du parcours complet », lot 1, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_SURVBOD\ -- un processus « avec demande »
/// (PROC_SURVBOD.cs:7, base(param, true)) :
///   - « Word » (WORD, texte « &amp;Exporter », FORM_SURVBOD.cs:27-29) et « Imprimer » (IMPRIMER, :31) valident le processus
///     (:58-71 ; seul MetierTableauEdition change : sans, puis avec le tableau d'editions). La validation verifie qu'au moins une
///     annonce est cochee (_Verifier, OPE_REJETS_BODACC.cs:162-166, message au mot pres), puis genere UNE edition : la liste des
///     controles qualite des annonces cochees, document Word du parametre LOT2_SURVBOD_LST_CQ (_CreateDemandeEditionListControleQualite,
///     :135-161 -> DemandeEdition.CreateDemandeEditionForDocWord : EditionsV2.DemandeEditionWordAsync), sur la demande du processus,
///     validee avec lui (les demandes SURVBOD de RIG_DEV sont V ou Q, sans nom ; leurs editions « Liste des contrôles qualité »,
///     FUSION_ASPOSE, lu le 28/11/2026) ;
///   - « Modifier l'annonce » (le bouton « M » de la ligne et le double clic, LaunchXAction, :82-95, :168-185) : sur UNE annonce, celle
///     de la ligne. Sans dossier RCS (un non-inscrit), le bouton est masque (MasquerBoutonsVides, :270-272, « XAction non possible pour
///     les non inscrits ») et rien ne se passe (:181) : la V3 le refuse avec ces mots. Sinon (_LaunchOldProcessViaDemandeForDossierRCS,
///     :192-208) : une Demande XACTION sur le dossier (Demande.CreateDemande, qui cree l'affaire : CreationV2.CreerDemandeAsync), nommee
///     par la designation du dossier, indicatif et numeros = le numero de gestion, sequenceur « R » = le dossier, puis le dossier,
///     l'action de l'annonce, ses numeros (le chrono de l'inscription, sinon le numero du depot) et DMND_ARRIVEE_SAISIE = 0 ; puis
///     qualifiee (Cloturer(Qualifiee), :231).
/// NON REPRIS (nommes) : l'ouverture en modale de l'ancien processus VB6 XACTION sur cette demande (:236-248) -- un ecran de RIG v2 sans
/// equivalent dans la V3 : la demande qualifiee l'attend, comme les 4 demandes XACTION « Q » de RIG_DEV ; le fichier de donnees XML de la
/// liste Word (EditionsV2.DemandeEditionWordAsync). Rien, dans ce processus, ne pose BDCCR_CONTROLE_QUALITE_VERIFIE : les lignes ne
/// changent pas (c'est XACTION qui verifie l'annonce).
/// Traces : famille « demande », l'edition ou la demande XACTION creee, « a cree ».
/// </summary>
internal sealed class EcrivainSurveillanceBodacc : IEcrivainEcran
{
    public string Procedure => "PROC_SURVBOD";
    public string Ecran => "Surveillance BODACC";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Modifier = "Modifier l'annonce", Word = "Word", Imprimer = "Imprimer";

    /// <summary>Le parametre d'edition de la liste (OPE_REJETS_BODACC.cs:158).</summary>
    internal const string ParametreListe = "LOT2_SURVBOD_LST_CQ";

    /// <summary>_Verifier, OPE_REJETS_BODACC.cs:164, au mot pres.</summary>
    internal const string AucuneAnnonce = "Vous devez sélectionner au moins un dossier à traiter.";

    /// <summary>MasquerBoutonsVides, OPE_REJETS_BODACC.cs:271 : le bouton d'une annonce sans dossier RCS est masque.</summary>
    internal const string SansDossier = "XAction non possible pour les non inscrits : l'annonce n'est rattachée à aucun dossier RCS.";

    internal const string UneAnnonce = "« Modifier l'annonce » corrige UNE annonce, celle de la ligne (le bouton « M » de RIG v2) : cochez-en une seule.";

    internal static string Disparue(string id) => $"L'annonce n° {id} n'est plus dans la base du greffe : actualisez la liste.";

    private sealed record Annonce(int Id, int? IdAction, int? IdDossier, string? NumeroGestion, string? Designation, string? Numeros);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        switch (geste.Demande.Nom)
        {
            case Word or Imprimer:
                if (ids.Length == 0) return IssueGeste.Refuser(AucuneAnnonce);
                var annonces = new List<Annonce>();
                foreach (var id in ids)
                {
                    if (await LireAsync(geste, id, ct) is not { } a) return IssueGeste.Refuser(Disparue(id));
                    annonces.Add(a);
                }
                return await ListeAsync(geste, annonces, ct);
            case Modifier:
                if (ids.Length != 1) return IssueGeste.Refuser(ids.Length == 0 ? AucuneAnnonce : UneAnnonce);
                if (await LireAsync(geste, ids[0], ct) is not { } annonce) return IssueGeste.Refuser(Disparue(ids[0]));
                if (annonce.IdDossier is null) return IssueGeste.Refuser(SansDossier);
                return await XactionAsync(geste, annonce, ct);
            default:
                return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        }
    }

    /// <summary>« Word » / « Imprimer » : la demande du processus, validee, et la liste Word des annonces cochees (en tete).</summary>
    private async Task<IssueGeste> ListeAsync(GesteEnCours geste, List<Annonce> annonces, CancellationToken ct)
    {
        var (idDemande, numero, refus) = await EditionsV2.DemandeProcessusValideeAsync(geste, "SURVBOD", null, ct);
        if (refus is not null) return IssueGeste.Refuser(refus);
        var (idEdition, refusEdition) = await EditionsV2.DemandeEditionWordAsync(geste, idDemande, ParametreListe, ct);
        if (refusEdition is not null) return IssueGeste.Refuser(refusEdition);
        var n = annonces.Count;
        var quoi = $"la liste des contrôles qualité de {n} annonce{(n > 1 ? "s" : "")}";
        var trace = new TraceGeste("demande", quoi, EditionsV2.Texte(idEdition), "a cree", null,
            new { Demande = numero, Parametre = ParametreListe, Annonces = annonces.Select(a => a.Id).ToArray(), Geste = geste.Demande.Nom });
        var suite = geste.Demande.Nom == Word
            ? "le service d'édition de RIG v2 la produit en Word"
            : "elle s'imprime depuis la file";
        return IssueGeste.Ecrit($"Liste des contrôles qualité ({n} annonce{(n > 1 ? "s" : "")}) placée dans la file des éditions (demande {numero}) : {suite}.",
            [EditionsV2.Texte(idEdition)], trace);
    }

    /// <summary>« Modifier l'annonce » : la demande XACTION qualifiee du dossier de l'annonce (en tete).</summary>
    private static async Task<IssueGeste> XactionAsync(GesteEnCours geste, Annonce a, CancellationToken ct)
    {
        var (demande, refus) = await CreationV2.CreerDemandeAsync(geste, "XACTION", "Q", ct);
        if (demande is null) return IssueGeste.Refuser(refus!);
        var gestion = a.NumeroGestion ?? "";
        await CreationV2.RattacherAsync(geste, demande, a.Designation ?? gestion, gestion, gestion, "R",
            a.IdDossier!.Value.ToString(CultureInfo.InvariantCulture), ct);
        // :197-205 : le dossier, l'action de l'annonce, ses numeros, DMND_ARRIVEE_SAISIE = 0 (colonne absente d'une base de banc : non ecrite).
        var arrivee = (await EditionsV2.ColonnesAsync(geste, "v2.DEMANDE", ct)).Contains("DMND_ARRIVEE_SAISIE") ? ", DMND_ARRIVEE_SAISIE = 0" : "";
        await geste.EcrireAsync($"""
            UPDATE v2.DEMANDE SET DMND_ID_DSRCS = @dossier, DMND_ID_ACTRC = @action,
                DMND_LES_NUMEROS = CASE WHEN @numeros IS NULL THEN DMND_LES_NUMEROS ELSE LEFT(@numeros, 128) END{arrivee}
            WHERE DMND_ID_DMND = @id
            """, ct, new SqlParameter("@dossier", a.IdDossier), new SqlParameter("@action", (object?)a.IdAction ?? DBNull.Value),
            new SqlParameter("@numeros", System.Data.SqlDbType.VarChar, 128) { Value = (object?)a.Numeros ?? DBNull.Value },
            new SqlParameter("@id", demande.Id));
        var trace = new TraceGeste("demande", $"la demande XACTION {demande.Numero} du dossier {gestion}", EditionsV2.Texte(demande.Id), "a cree", null,
            new { Demande = demande.Numero, Processus = "XACTION", Etat = "Q", Annonce = a.Id, Dossier = gestion, Action = a.IdAction });
        return IssueGeste.Ecrit(
            $"Demande XACTION {demande.Numero} créée et qualifiée pour le dossier {FormeLigne.NumeroGestion(gestion)} : la correction de l'annonce se " +
            "fait dans le traitement XACTION de RIG v2, que RIG v3 n'ouvre pas.",
            [EditionsV2.Texte(demande.Id)], trace);
    }

    /// <summary>L'annonce de la ligne et ce que LaunchXAction en lit (OPE_REJETS_BODACC.cs:168-185 ; la requete du lecteur) ; null si elle
    /// n'est plus dans la base. Le dossier : celui de l'action de l'annonce (rec.ActionRegistre, ACTION_RCS de RIG v2).</summary>
    private static async Task<Annonce?> LireAsync(GesteEnCours geste, string texte, CancellationToken ct)
    {
        if (!int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return null;
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) b.BDCCR_ID_BDCRC, a.Id AS IdAction, s.Id AS IdDossier, s.NumeroGestion, e.Denomination,
                   CASE WHEN ir.INSRC_NUM_CHRONO IS NULL THEN p.DPTRC_NUM_DEPOT ELSE ir.INSRC_NUM_CHRONO END AS Numeros
            FROM v2.BODACC AS b
            LEFT JOIN rec.ActionRegistre AS a ON a.IdAnnonceBodacc = b.BDCCR_ID_BDCRC
            LEFT JOIN v2.DEPOT_RCS AS p ON a.IdDepot = p.DPTRC_ID_DPTRC
            LEFT JOIN v2.INSCRIPTION_RCS AS ir ON a.IdInscriptionRegistre = ir.INSRC_ID_INSRC
            LEFT JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
            LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
            WHERE b.BDCCR_ID_BDCRC = @id
            ORDER BY CASE WHEN s.Id IS NULL THEN 1 ELSE 0 END, a.Id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        int? Entier(string colonne) => r[colonne] is DBNull ? null : Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture);
        string? Texte(string colonne) => r[colonne] is DBNull ? null : FormeLigne.Texte(r, colonne);
        return new Annonce(id, Entier("IdAction"), Entier("IdDossier"), Texte("NumeroGestion"), Texte("Denomination"), Texte("Numeros"));
    }
}
