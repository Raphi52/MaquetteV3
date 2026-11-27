using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurOccultations;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Occultations » (PROC_DJ_OCCULTATIONS) : « Enregistrer » ecrit les consignes d'occultation d'une decision (ses 12 cases, ses
/// mots a masquer et a conserver) dans la base du greffe. RIG v2 n'appelle aucun service : il enregistre des consignes que l'open
/// data des decisions de justice lira plus tard (DecisionJudiciaireOccultations.cs:12) -- rien a simuler. Mode d'emploi :
/// EcrivainEcran.cs ; la fiche : LecteurOccultations.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_DJ_OCCULTATIONS\ (proc_dj_occultations.md, Detail OPE) :
///   - les cases sont liees aux colonnes DJOC_* (OPE_OCCULTATIONS.DoChargerDonnees, :33-48) ; une decision sans consignes en recoit
///     une ligne neuve a l'enregistrement (DecisionJudiciaire.AddIfNotExisteOccultations, DecisionJudiciaire.cs:1747-1754) ;
///   - les mots : une liste par drapeau DJOM_A_MASQUER ; un mot retire est supprime a l'enregistrement (DoDelete, DataGridViewMots.
///     _ColDeleteButtonClick, :83-100 ; DecisionJudiciaireOccultations.Sauve, :159-176) ;
///   - l'enregistrement (ETP_OCCULTATIONS.DoSauveAfterOperation, :63-70) : la decision et ses consignes, puis Instance.
///     UpdateCentralisation (Instance.cs:4682-4686, « INSTN_TOP_EXPERIAN = 0, INSTN_DATE_MAJ_INFOG = maintenant »).
/// CONTROLES, avant toute ecriture :
///   1. la decision existe ;
///   2. un mot n'est qu'une fois dans les deux listes, sans tenir compte de la casse ni des blancs (Exsite, DecisionJudiciaire-
///      Occultations.cs:85-92) : « Le mot est déjà défini dans la liste des mots à masquer » (ou « à conserver », la liste du mot
///      deja present), au mot pres de OPE_OCCULTATIONS_MOTS.cs:82 et :103.
/// Traces : famille « affaire », « a cree » (premieres consignes) ou « a modifie », les cases cochees et les mots avant et apres.
///
/// NON REPRIS (nomme) : la demande de RIG v2 (DMND_NOM « Occultations de la décision : <type> - <minute> », PROC_DJ_OCCULTATIONS.
/// DoSauveAfterEtape, :45-58) : une trace creee et validee d'un coup, remplacee par le journal des gestes (decision du chef du plan).
/// L'avertissement d'un mot saisi sans clic sur « + » (DoVerifier, :108-117) n'a pas lieu d'etre : la V3 enregistre la liste saisie.
/// </summary>
internal sealed class EcrivainOccultations : IEcrivainEcran
{
    public string Procedure => "PROC_DJ_OCCULTATIONS";
    public string Ecran => "Occultations";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var id = geste.Demande.Ids.FirstOrDefault();
        if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            || await ConsignesAsync((sql, p) => geste.LireAsync(sql, ct, p), n, ct) is not { } avant)
            return IssueGeste.Refuser($"Décision introuvable dans la base du greffe ({id}).");

        // Ce que la fiche envoie (un champ absent garde sa valeur : celle de la base, ou la case par defaut d'une decision sans consignes).
        var cases = (bool[])avant.Cases.Clone();
        for (var k = 0; k < Cases.Length; k++)
            if (geste.Valeur(SectionCategories, Cases[k].Libelle) is { } v) cases[k] = v.Trim() == "oui";
        var masquer = Mots(geste.Valeur(SectionMasquer, ChampMasquer)) ?? avant.Masquer;
        var conserver = Mots(geste.Valeur(SectionConserver, ChampConserver)) ?? avant.Conserver;

        // 2. Un mot une seule fois, dans les deux listes (Exsite : sans casse, sans blancs).
        var vus = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (mot, aMasquer) in masquer.Select(m => (m, true)).Concat(conserver.Select(m => (m, false))))
        {
            if (vus.TryGetValue(mot, out var deja))
                return IssueGeste.Refuser($"Le mot est déjà défini dans la liste des mots {(deja ? "à masquer" : "à conserver")}");
            vus[mot] = aMasquer;
        }

        int idOccultations;
        var colonnes = Cases.Select((k, i) => (k.Colonne, Valeur: (object?)cases[i])).ToList();
        if (avant.IdOccultations is { } existant)
        {
            idOccultations = existant;
            await geste.EcrireAsync($"UPDATE v2.DECISION_JUDICIAIRE_OCCULTATIONS SET {string.Join(", ", colonnes.Select((c, i) => $"{c.Colonne} = @c{i}"))} WHERE DJOC_ID_DJOC = @o",
                ct, [.. colonnes.Select((c, i) => new SqlParameter($"@c{i}", c.Valeur)), new SqlParameter("@o", existant)]);
        }
        else
        {
            var valeurs = colonnes.ToDictionary(c => c.Colonne, c => c.Valeur);
            valeurs["DJOC_ID_DCJDC"] = n;
            idOccultations = await geste.InsererAsync("v2.DECISION_JUDICIAIRE_OCCULTATIONS", "DJOC_ID_DJOC", valeurs, ct);
        }

        // Les mots : ceux retires (ou passes dans l'autre liste) supprimes, les nouveaux ajoutes ; les autres gardent leur ligne.
        var gardes = new List<(int Id, string Mot, bool AMasquer)>();
        await using (var r = await geste.LireAsync("SELECT DJOM_ID_DJOM, DJOM_MOTS, DJOM_A_MASQUER FROM v2.DECISION_JUDICIAIRE_OCCULTATIONS_MOTS WHERE DJOM_ID_DJOC = @o",
                         ct, new SqlParameter("@o", idOccultations)))
            while (await r.ReadAsync(ct))
                gardes.Add((Convert.ToInt32(r["DJOM_ID_DJOM"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "DJOM_MOTS"), r["DJOM_A_MASQUER"] is true));
        foreach (var g in gardes.Where(g => !vus.TryGetValue(g.Mot, out var drapeau) || drapeau != g.AMasquer))
            await geste.EcrireAsync("DELETE FROM v2.DECISION_JUDICIAIRE_OCCULTATIONS_MOTS WHERE DJOM_ID_DJOM = @m", ct, new SqlParameter("@m", g.Id));
        foreach (var (mot, aMasquer) in vus.Where(v => !gardes.Any(g => string.Equals(g.Mot, v.Key, StringComparison.OrdinalIgnoreCase) && g.AMasquer == v.Value))
                                          .Select(v => (v.Key, v.Value)))
            await geste.InsererAsync("v2.DECISION_JUDICIAIRE_OCCULTATIONS_MOTS", "DJOM_ID_DJOM", new Dictionary<string, object?>
            {
                ["DJOM_ID_DJOC"] = idOccultations, ["DJOM_MOTS"] = mot, ["DJOM_A_MASQUER"] = aMasquer,
            }, ct);

        // Instance.UpdateCentralisation (Instance.cs:4682-4686).
        await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_TOP_EXPERIAN = 0, INSTN_DATE_MAJ_INFOG = @maintenant WHERE INSTN_ID_INSTN = @i", ct,
            new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@i", avant.IdInstance));

        object Etat(bool[] c, List<string> m, List<string> k) => new
        {
            Cases = string.Join(", ", Cases.Where((_, i) => c[i]).Select(x => x.Libelle)),
            MotsAMasquer = string.Join(", ", m), MotsAConserver = string.Join(", ", k),
        };
        var trace = new TraceGeste("affaire", $"les occultations de la décision du rôle {avant.Role}", id, avant.IdOccultations is null ? "a cree" : "a modifie",
            avant.IdOccultations is null ? null : Etat(avant.Cases, avant.Masquer, avant.Conserver), Etat(cases, masquer, conserver));
        return IssueGeste.Ecrit($"Occultations de la décision du rôle {avant.Role} enregistrées ({cases.Count(c => c)} case{(cases.Count(c => c) > 1 ? "s" : "")} cochée{(cases.Count(c => c) > 1 ? "s" : "")}, "
                              + $"{masquer.Count} mot{(masquer.Count > 1 ? "s" : "")} à masquer, {conserver.Count} à conserver).", [id!], trace);
    }

    /// <summary>Les mots saisis, un par ligne (blancs retires, lignes vides ignorees) ; null si le champ n'est pas envoye.</summary>
    private static List<string>? Mots(string? saisie)
        => saisie is null ? null : [.. saisie.Split('\n').Select(m => m.Trim()).Where(m => m.Length > 0)];
}
