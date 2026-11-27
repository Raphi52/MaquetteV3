using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Radiations d'office » (PROC_ROFSURVEILLANCES) : « Radier d'office » les dossiers coches de la liste. Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurRadiationsOffice.cs (Id = le dossier). Plan « RIG v3 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RCS\Exe\PROC_ROFSurveillances\FrmProcROFSurveillances.cs, _radiationOffice
/// (l. 383-534), sur le dossier choisi dans la liste :
///   1. Demande.CreateDemande(« ROFSURV - Radiation d'Office - » + designation, numero de gestion, « ROF ») sur le dossier
///      (l. 391-395 ; DMND_ID_DSRCS = le dossier) : la demande ROF que le lecteur relit (son etat donne celui de la ligne) ;
///   2. hors S123-124, l'evenement 90P d'AVISIR du dossier est inhibe (l. 409-431) ;
///   3. la mention d'office et l'evenement de radiation (AddMentionObservation, ajoutRadiationOffice l. 1223-1400 : action RCS,
///      inscription, numero chronologique, date de radiation du dossier) ; en echec, la demande passe en X (l. 434-440) ;
///   4. les editions ROFRCSED, Kbis, bordereau de numerotation et lettre au procureur (l. 441-494), la validation (l. 497), la
///      liasse CFE (l. 509-513) et l'impression (l. 516).
/// CE QUE LA V3 ECRIT : le 1, seul -- la demande ROF du dossier, posee INTERROMPUE (« I », Demande.eEtatDemande.Interrompue,
///   Demande.cs:36-37), comme les saisies de creation dont la V3 ne reprend pas toute l'ecriture (CreationV2.cs, « sa demande reste
///   interrompue ») : la radiation est COMMENCEE, la ligne passe « En cours » (LecteurRadiationsOffice, regle d'etat) et se reprend dans
///   RIG v2 par le processus ROF -- celui que la V2 nomme elle-meme quand sa radiation echoue (« Veuillez utiliser le processus ROF si
///   vous souhaitez effectuer la radiation d'office », l. 437). Numero de la demande : serie DMD (CreationV2.NumeroAsync), comme
///   Demande.CreateDemande (Demande.cs:493 et :669). Auteur : l'agent (DMND_NOM_UTILISATEUR, la colonne que le verrou de la V2 cite).
/// NON REPRIS (nommes, a faire dans RIG v2 sur la demande interrompue) : 2 (l'inhibition du 90P suit une radiation reelle, que la V3 ne
///   fait pas), 3 et 4 -- mention, evenement et date de radiation, editions (dont la lettre au procureur), liasse CFE : aucun service
///   d'edition ni d'envoi n'est appele.
/// CONTROLES, avant toute ecriture :
///   1. au moins un dossier coche ;
///   2. chaque dossier est ENCORE dans la liste de la V2 : la requete meme du lecteur (LecteurRadiationsOffice.Requete, reglage
///      ROFSURV_S123_136 compris), relue dans la transaction -- un dossier radie, sorti de surveillance ou en procedure collective entre
///      la lecture et le geste est refuse ;
///   3. aucune radiation n'y est deja commencee ou faite depuis la relance (la demande ROF de la ligne) : la V2 grise « Radiation » une
///      fois la radiation lancee (l. 528-529) ; message sans source V2.
/// Traces : famille « dossier », le dossier en mots, son id, « a cree », la demande ROF interrompue.
/// </summary>
internal sealed class EcrivainRadiationsOffice : IEcrivainEcran
{
    public string Procedure => "PROC_ROFSURVEILLANCES";
    public string Ecran => "Radiations d'office";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres (le bouton RADIATION de la barre de la V2, tbRofSurv).</summary>
    internal const string Radier = "Radier d'office";

    /// <summary>L'etat ecrit : Demande.eEtatDemande.Interrompue (Demande.cs:36-37).</summary>
    internal const string Interrompue = "I";

    /// <summary>Le debut du nom de la demande de la V2 (l. 390).</summary>
    internal const string NomDemande = "ROFSURV - Radiation d'Office - ";

    private sealed record Dossier(int Id, string Numero, string Designation, string? EtatRof);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Radier) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une selection.
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun dossier coché : rien n'est radié.");

        // 2. et 3. Chaque dossier, encore dans la liste, sans radiation commencee.
        var relanceSeule = LectureReglage.OuiNon(await ReglageAsync(geste, ct), RegleOuiNon.EgalUn);
        var dossiers = new List<Dossier>();
        foreach (var id in ids)
        {
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || await LigneAsync(geste, n, relanceSeule, ct) is not { } d)
                return IssueGeste.Refuser($"Le dossier {id} n'est plus à radier d'office : il est sorti de la liste (radié, hors surveillance ou en procédure collective).");
            if (d.EtatRof is not null)
                return IssueGeste.Refuser(d.EtatRof is "V" or "T"
                    ? $"Le dossier {FormeLigne.NumeroGestion(d.Numero)} est déjà radié d'office depuis sa relance."
                    : $"Une radiation d'office est déjà commencée pour le dossier {FormeLigne.NumeroGestion(d.Numero)} : reprenez-la dans RIG v2 (processus ROF).");
            dossiers.Add(d);
        }

        var traces = new List<TraceGeste>();
        foreach (var d in dossiers)
        {
            var (numero, refus) = await CreationV2.NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
            if (numero is null) return IssueGeste.Refuser(refus!);
            var nom = NomDemande + d.Designation;
            await CreationV2.InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", new Dictionary<string, object?>
            {
                ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = Interrompue, ["DMND_CODE_PROSS"] = "ROF",
                ["DMND_ID_DSRCS"] = d.Id, ["DMND_NOM"] = nom.Length > 255 ? nom[..255] : nom,
                ["DMND_NUM_INDICATIF"] = d.Numero.Length > 14 ? d.Numero[..14] : d.Numero, ["DMND_DATE_CREATION"] = geste.Maintenant,
                ["DMND_EN_COURS"] = false, ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche,
            }, ct);
            traces.Add(new TraceGeste("dossier", $"le dossier {FormeLigne.NumeroGestion(d.Numero)}", d.Id.ToString(CultureInfo.InvariantCulture), "a cree",
                null, new Dictionary<string, string> { ["Demande ROF"] = numero, ["Etat"] = "I (interrompue, à reprendre dans RIG v2)", ["Nom"] = nom }));
        }
        var nb = dossiers.Count;
        var quoi = nb > 1 ? $"{nb} radiations d'office commencées" : $"Radiation d'office commencée pour le dossier {FormeLigne.NumeroGestion(dossiers[0].Numero)}";
        return IssueGeste.Ecrit(
            $"{quoi} : demande ROF posée interrompue. La mention, l'événement de radiation, les éditions (dont la lettre au procureur) et la " +
            "liasse CFE ne sont pas repris par la V3 : reprendre la demande dans RIG v2 (processus ROF).",
            [.. dossiers.Select(d => d.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    /// <summary>Le reglage ROFSURV_S123_136, lu dans la transaction du geste ; vide s'il n'existe pas (le SiAbsent du lecteur).</summary>
    private static async Task<string> ReglageAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = @code", ct,
            new SqlParameter("@code", LecteurRadiationsOffice.RelanceSeule.Code));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? r.GetString(0) : LecteurRadiationsOffice.RelanceSeule.SiAbsent;
    }

    /// <summary>La ligne du dossier <paramref name="id"/> dans la liste du lecteur (sa requete telle quelle) ; null s'il n'y est plus.</summary>
    private static async Task<Dossier?> LigneAsync(GesteEnCours geste, int id, bool relanceSeule, CancellationToken ct)
    {
        await using var r = await geste.LireAsync($"""
            SELECT TOP (1) l.NumeroGestion, l.Denomination, l.EtatRof
            FROM ({LecteurRadiationsOffice.Requete(relanceSeule)}) AS l
            WHERE l.IdDossier = @id
            """, ct, new SqlParameter("@id", id), new SqlParameter("@plafond", int.MaxValue),
            new SqlParameter("@aujourdhui", System.Data.SqlDbType.DateTime) { Value = geste.Maintenant.Date });
        if (!await r.ReadAsync(ct)) return null;
        var numero = FormeLigne.Texte(r, "NumeroGestion");
        var designation = FormeLigne.Texte(r, "Denomination");
        return new Dossier(id, numero, designation.Length > 0 ? designation : numero, r["EtatRof"] is DBNull ? null : FormeLigne.Texte(r, "EtatRof"));
    }
}
