using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rappels RBE » (PROC_SRBE_RAPPEL) : « Éditer les courriers » -- le courrier de rappel ou de relance du registre des beneficiaires
/// effectifs aux dossiers des lignes cochees, comme le bouton EDITION de RIG v2. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurRappelsRbe.cs (Id = une demande SRBE ; le geste vise son DOSSIER). Plan « RIG v3 complet, mesure par le banc du parcours
/// complet », lot 1, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_SRBE_RAPPEL\PROC_SRBE\PROC_SRBE_RAPPEL.cs (un processus « avec
/// demande », :24), GenererLesEditionsRBE (:109-204), au moment des editions (DoGenererLesEditions, :32-34), puis ValidationSauver (:42-45) :
///   - au moins un dossier coche, sinon « Vous devez sélectionner au moins un dossier à traiter. » (OPE_RECHERCHE_RESULTATS.cs:352-353) ;
///   - PAR DOSSIER : immatricule avant le 01/08/2017 (DATE_LIMITE_RAPPEL, :20 ; :136) -> « Rappel », parametre LOT1_LRNRBE_3_IMP ; sinon
///     « Relance 1 », LOT1_LRNRBE_1_IMP (:123-126). Une demande du processus (code SRBE, le seul de RIG_DEV) sur une affaire neuve
///     (Affaire.CreateAffaire, :146 : CreationV2.CreerDemandeAsync), nommee « &lt;type&gt; RBE &lt;designation&gt; », indicatif et numeros
///     = le numero de gestion, le dossier et son sequenceur « R » (:147-155) ; validee a la sauvegarde (:43 ; ecrite validee, ici, dans
///     la transaction du geste) ;
///   - sa demande d'edition (CreateDemandeEditionByParametreGeneration, :158-159 : EditionsV2.DemandeEditionAsync) -- entite DOSSIER_RCS,
///     le dossier en source, le numero de gestion en libelle source (:162), le lot Amitel si les rappels s'editent chez Amitel et que la
///     file est reglee (:165-167), recto verso et affichage avant impression du parametre, sinon oui / non (:168-174), la designation du
///     destinataire (:175) ; la maquette du parametre (les editions SRBE de RIG_DEV portent LRNRBE_3_IMP / LRNRBE_1_IMP, lu le
///     28/11/2026). Sans parametre, la V2 ne cree pas d'edition pour ce dossier (« if (demandeEdition != null) », :160) : la demande reste ;
///   - les reglages (ParametrageRBE.cs, PARAMETRAGE repris dans soc.Reglage) : SRBE_EDITION_RAPPEL_AMITEL (defaut « 1 »),
///     SRBE_EDITION_FILE_AMITEL (defaut vide), SRBE_EDITION_LISTE_DOSSIERS (defaut « 1 ») ;
///   - la liste des dossiers relances, si ce dernier reglage le veut et qu'au moins un courrier part (:181-202) : un document Word du
///     parametre LRNRBE_1_LIST_IMP sur la demande DU PROCESSUS (CreateDemandeEditionListeDossierRcs, :223-245 ;
///     EditionsV2.DemandeEditionWordAsync, dont le refus est celui de RIG v2 quand le parametre manque).
/// LA LISTE DE LA V3 N'EST PAS CELLE DE RIG v2 (dit dans LecteurRappelsRbe.cs) : les requetes qui proposaient les dossiers A ENVOYER
/// n'existent plus dans l'ancien RIG ; la V3 montre les demandes SRBE passees. Le geste, lui, est celui de RIG v2, applique aux dossiers des
/// lignes cochees (un dossier coche deux fois recoit un courrier).
/// NON REPRIS (nommes) : le bloc adresse du destinataire (EtablissementReferent.Adresse.AdresseMultiLignesPourEdition, :176 -- l'adresse de
/// l'etablissement referent n'est pas une fiche lue ici) ; le fichier de donnees XML de la liste Word (EditionsV2.DemandeEditionWordAsync).
/// Traces : famille « demande », chaque demande SRBE creee (et la liste), « a cree ».
/// </summary>
internal sealed class EcrivainRappelsRbe : IEcrivainEcran
{
    public string Procedure => "PROC_SRBE_RAPPEL";
    public string Ecran => "Rappels RBE";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur (Valider, Surveiller.cs : le bouton EDITION de FORM_SRBE_RAPPEL), au mot pres.</summary>
    internal const string Editer = "Éditer les courriers";

    /// <summary>OPE_RECHERCHE_RESULTATS.cs:352-353, au mot pres.</summary>
    internal const string AucunDossier = "Vous devez sélectionner au moins un dossier à traiter.";

    internal const string ParametreRappel = "LOT1_LRNRBE_3_IMP", ParametreRelance = "LOT1_LRNRBE_1_IMP", ParametreListe = "LRNRBE_1_LIST_IMP";

    internal static string Disparue(string id) => $"La demande SRBE n° {id} n'est plus dans la base du greffe : relisez la liste.";

    private sealed record Dossier(int Id, string NumeroGestion, DateTime Immatriculation, string Designation);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Editer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(AucunDossier);
        var dossiers = new List<Dossier>();
        foreach (var id in ids)
        {
            if (await DossierAsync(geste, id, ct) is not { } d) return IssueGeste.Refuser(Disparue(id));
            if (dossiers.All(x => x.Id != d.Id)) dossiers.Add(d);
        }

        // ParametrageRBE.cs:50, :55, :61.
        var rappelAmitel = (await ReglageAsync(geste, "SRBE_EDITION_RAPPEL_AMITEL", ct) ?? "1").Trim() == "1";
        var fileAmitel = await ReglageAsync(geste, "SRBE_EDITION_FILE_AMITEL", ct) ?? "";
        var liste = (await ReglageAsync(geste, "SRBE_EDITION_LISTE_DOSSIERS", ct) ?? "1").Trim() == "1";
        var lot = rappelAmitel && !string.IsNullOrWhiteSpace(fileAmitel) ? fileAmitel : null;

        var traces = new List<TraceGeste>();
        var crees = new List<string>();
        var editions = 0;
        var (rappels, relances) = (0, 0);
        foreach (var d in dossiers.OrderBy(x => x.NumeroGestion, StringComparer.Ordinal))
        {
            var rappel = d.Immatriculation < LecteurRappelsRbe.DateLimiteRappel;
            var (typeLettre, code) = rappel ? ("Rappel", ParametreRappel) : ("Relance 1", ParametreRelance);
            if (rappel) rappels++; else relances++;
            var (demande, refus) = await CreationV2.CreerDemandeAsync(geste, "SRBE", "V", ct);
            if (demande is null) return IssueGeste.Refuser(refus!);
            var dossier = d.Id.ToString(CultureInfo.InvariantCulture);
            await CreationV2.RattacherAsync(geste, demande, $"{typeLettre} RBE {d.Designation}", d.NumeroGestion, d.NumeroGestion, "R", dossier, ct);
            await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ID_DSRCS = @dossier, DMND_DATE_VALIDATION = @maintenant WHERE DMND_ID_DMND = @id", ct,
                new SqlParameter("@dossier", d.Id), new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", demande.Id));
            int? edition = null;
            if (await EditionsV2.ParametreAsync(geste, code, ct) is { } parametre)
            {
                var (rectoVerso, avantImpression, maquette) = await OptionsAsync(geste, code, ct);
                edition = await EditionsV2.DemandeEditionAsync(geste, demande.Id, parametre, null, "DOSSIER_RCS", dossier, lot, null, ct,
                    new Dictionary<string, object?>
                    {
                        ["DMNED_LIBELLE_SOURCE"] = d.NumeroGestion, ["DMNED_RECTO_VERSO"] = rectoVerso, ["DMNED_AFF_AVANT_IMPRESSION"] = avantImpression,
                        ["DMNED_DESIGNATION_DESTINATAIRE"] = d.Designation, ["DMNED_CODE_MAQUETTE"] = maquette,
                    });
                editions++;
            }
            crees.Add(EditionsV2.Texte(demande.Id));
            traces.Add(new TraceGeste("demande", $"le courrier « {typeLettre} RBE » du dossier {d.NumeroGestion}", EditionsV2.Texte(demande.Id), "a cree", null,
                new { Demande = demande.Numero, Processus = "SRBE", Courrier = typeLettre, Parametre = code, Edition = edition, Lot = lot }));
        }

        // La liste des dossiers relances, sur la demande du processus (:181-202) : seulement si un courrier part.
        string? numeroListe = null;
        if (liste && editions > 0)
        {
            var (idProcessus, numero, refus) = await EditionsV2.DemandeProcessusValideeAsync(geste, "SRBE", null, ct);
            if (refus is not null) return IssueGeste.Refuser(refus);
            var (idListe, refusListe) = await EditionsV2.DemandeEditionWordAsync(geste, idProcessus, ParametreListe, ct);
            if (refusListe is not null) return IssueGeste.Refuser(refusListe);
            numeroListe = numero;
            traces.Add(new TraceGeste("demande", $"la liste des {dossiers.Count} dossier{(dossiers.Count > 1 ? "s" : "")} relancé{(dossiers.Count > 1 ? "s" : "")}",
                EditionsV2.Texte(idListe), "a cree", null, new { Demande = numero, Parametre = ParametreListe }));
        }

        var courriers = string.Join(" et ", new[]
        {
            rappels > 0 ? $"{rappels} rappel{(rappels > 1 ? "s" : "")}" : null, relances > 0 ? $"{relances} relance{(relances > 1 ? "s" : "")} 1" : null,
        }.Where(x => x is not null));
        var message = editions == dossiers.Count
            ? $"{courriers} RBE dans la file des éditions"
            : $"{courriers} RBE : {dossiers.Count - editions} sans paramètre d'édition, RIG v2 n'en édite pas le courrier ; {editions} dans la file des éditions";
        message += numeroListe is null ? "." : $", avec la liste des dossiers relancés (demande {numeroListe}).";
        return IssueGeste.Ecrit(message, [.. crees], [.. traces]);
    }

    /// <summary>Le dossier de la demande SRBE <paramref name="texte"/> (une ligne de la liste) ; null si elle n'est plus dans la liste.</summary>
    private static async Task<Dossier?> DossierAsync(GesteEnCours geste, string texte, CancellationToken ct)
    {
        if (!int.TryParse(texte, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return null;
        await using var r = await geste.LireAsync("""
            SELECT s.Id, s.NumeroGestion, s.DateImmatriculation, e.Civilite, e.Denomination
            FROM v2.DEMANDE AS d
            JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS
            LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
            WHERE d.DMND_ID_DMND = @id AND d.DMND_CODE_PROSS IN ('SRBE', 'SRBE_RAPPEL', 'SRBE_OLD') AND d.DMND_ETAT_DEMANDE <> 'X'
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        // DesignationShortAvecCivilite : la civilite devant la denomination quand il y en a une.
        var designation = $"{FormeLigne.Texte(r, "Civilite")} {FormeLigne.Texte(r, "Denomination")}".Trim();
        return new Dossier(Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "NumeroGestion"),
            FormeLigne.DateDe(r, "DateImmatriculation") ?? DateTime.MinValue, designation);
    }

    /// <summary>Un reglage du greffe (PARAMETRAGE de RIG v2, repris dans soc.Reglage) ; null s'il n'existe pas ou s'il est vide
    /// (Parametrage.GetParameterValue rend alors la valeur par defaut, ParametrageRBE.cs:40-44).</summary>
    private static async Task<string?> ReglageAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = @code", ct, new SqlParameter("@code", code));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) && r.GetString(0).Length > 0 ? r.GetString(0) : null;
    }

    /// <summary>Recto verso, affichage avant impression et maquette du parametre (:168-170) ; le parametre existe (lu juste avant).</summary>
    private static async Task<(bool RectoVerso, bool AvantImpression, string? Maquette)> OptionsAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        var colonnes = await EditionsV2.ColonnesAsync(geste, "v2.PARAMETRE_EDITION", ct);
        string Colonne(string nom) => colonnes.Contains(nom) ? nom : $"NULL AS {nom}";
        await using var r = await geste.LireAsync(
            $"SELECT {Colonne("PRMED_RECTO_VERSO")}, {Colonne("PRMED_AFFICHER_AVANT_IMPRESSION")}, {Colonne("PRMED_CODE_MAQUETTE")} " +
            "FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @code", ct, new SqlParameter("@code", code));
        await r.ReadAsync(ct);
        return (r["PRMED_RECTO_VERSO"] is true, r["PRMED_AFFICHER_AVANT_IMPRESSION"] is true,
                r["PRMED_CODE_MAQUETTE"] is DBNull ? null : FormeLigne.Texte(r, "PRMED_CODE_MAQUETTE"));
    }
}
