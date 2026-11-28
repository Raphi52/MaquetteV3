using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Refacturation » (PROC_FACTREEDIT) : « Valider » relance l'edition des factures cochees -- une NOUVELLE demande d'edition, copie de
/// la derniere edition de facture de chacune, dans la FILE DES EDITIONS. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurRefacturation.cs (Id = le numero de facture) ; le droit : EditionsEtatsV2.cs (en tete). Plan-f4fb097f, lot 4, 28/11/2026.
///
/// SOURCE (ancien RIG) : VALIDER du processus -> OPE_REVIEW.DoGenererLesDemandesEditions (D:\RigApplication\Source\RIG\DLL\Processus\
/// PROC_FACTREEDIT\OPE_REVIEW.cs:236-241) : pour chaque facture COCHEE, _GetEditionFactureARelancer (:249-254) -- parmi les editions de
/// la demande de la facture, celles de facture (DemandeEdition.isFacture : la maquette repond a « FACT_[0-9]*_IMP », DemandeEdition.cs:2008),
/// la plus recente (id le plus grand) ; une facture sans edition de facture est passee (« .Where(de => de != null) », :239). Ces editions
/// vont au tableau d'edition (PROC_TABLEAU_LOT_2), qui fait de chacune un noeud portant une NOUVELLE edition copiee de l'origine
/// (TreeViewDestinataire.cs:2682 -> _init, :1289-1302 : DemandeEdition.CreateNouvelleDemandeEdition), l'origine comme edition reeditee
/// (OPE_TABLOT2.cs:439-440) ; sa validation (TableauEditionLot2Parametre.SauveDiffere -> _sauveDiffereLot2, :328-425) sauve cette copie.
/// CreateNouvelleDemandeEdition (Outils\DLL\RigMetier\Edition\DemandeEdition.cs:431-470) recopie l'edition d'origine, date de creation =
/// maintenant, phase C, sans groupe d'impression ni numero d'ordre, sans dates de pre-generation et de pre-emission, sans numeros
/// (DMNED_LES_NUMEROS), sans courriel envoye (DMNED_ID_EMAIL_ENVOYE), sans document sauf en EMISSION_SIMPLE (:456-462) ; la V3 met l'agent
/// comme utilisateur et interlocuteur (celui de la session V2 qui valide, DemandeEdition.cs:3938-3939) ; le drapeau « courriel envoye a
/// la validation » suit l'envoi par courriel (_sauveDiffereLot2, TableauEditionLot2Parametre.cs:389-393).
/// CONTROLES, avant toute ecriture :
///   1. le droit d'executer FACTREEDIT (EditionsEtatsV2.ProcessusAutoriseAsync ; FORM_ACCUEIL.cs:1624, au mot pres) ;
///   2. au moins une facture cochee (la V2 valide alors un tableau vide : rien) -- message sans source V2 ;
///   3. chaque facture existe dans la base du greffe -- sinon refus, message sans source V2 ;
///   4. au moins une facture cochee a une edition de facture : sans aucune, la V2 ne relance rien (:239) -- la V3 le dit et refuse ; les
///      factures cochees sans edition de facture sont nommees dans le message du geste fait.
/// NON REPRIS (nommes) : ce que la validation du tableau d'edition ajoute -- le groupe d'impression du lot direct ou differe
/// (_creerNumGroupe, TableauEditionLot2Parametre.cs:447-505), les numeros LR/LRAR (_CreerLRARRig ; DemandeEdition.SauveValidation,
/// DemandeEdition.cs:1388-1407) et la ligne d'historique DEMANDE_EDITION_HISTO (:1406-1407) ; les objets lies que la copie emporte en
/// memoire (lettres recommandees, ligne d'edition, courriel : CreateNouvelleDemandeEdition, :463-470) ; les choix faits dans le tableau
/// (imprimante, lot, destinataire) -- la copie garde ceux de l'edition d'origine. La demande reste sans groupe, comme celles des autres ecrivains (EditionsV2.cs : « le tableau d'editions la choisit »).
/// TRACE : famille « demande », « a cree », la reedition de chaque facture (edition d'origine, nouvelle edition).
/// </summary>
// fix-ok: fichier NEUF du lot 4, ecrit en plusieurs passes : les citations de l'en-tete ont ete recalees apres lecture du tableau
// d'edition de la V2 (TreeViewDestinataire.cs:2682 cree la copie ; le chemin IEdition.SauveIEdition cite d'abord n'est pas celui du lot 2).
internal sealed partial class EcrivainRefacturation : IEcrivainEcran
{
    public string Procedure => "PROC_FACTREEDIT";
    public string Ecran => "Refacturation";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Valider = "Valider", Processus = "FACTREEDIT";
    internal const string AucuneCochee = "Aucune facture cochée : rien n'est réédité.";
    internal const string SansEditionFacture = "Aucune édition de facture (maquette FACT_n_IMP) pour les factures cochées : RIG v2 n'en relance aucune.";
    internal static string Introuvable(string numero) => $"La facture {numero} n'existe plus dans la base du greffe : relisez la liste.";

    /// <summary>DemandeEdition.isFacture (DemandeEdition.cs:2008), la meme expression, sans ancrage.</summary>
    [GeneratedRegex("FACT_[0-9]*_IMP", RegexOptions.CultureInvariant)]
    private static partial Regex MaquetteFacture();

    // CreateNouvelleDemandeEdition (:443-454) : les champs vides dans la copie.
    private static readonly string[] Vides =
        ["DMNED_NUM_GROUPE", "DMNED_NUM_ORDRE_GROUPE", "DMNED_DATE_PRE_GENERATION", "DMNED_DATE_PRE_EMISSION", "DMNED_LES_NUMEROS", "DMNED_ID_EMAIL_ENVOYE"];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Valider) return IssueGeste.Refuser($"Geste « {geste.Demande.Nom} » inconnu de la refacturation : RIG v2 n'a que « {Valider} ».");
        // 1. Le droit d'executer le processus.
        if (!await EditionsEtatsV2.ProcessusAutoriseAsync(geste, Processus, ct)) return IssueGeste.Refuser(EditionsEtatsV2.NonAutorise(Processus));
        // 2. Une selection.
        var numeros = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (numeros.Length == 0) return IssueGeste.Refuser(AucuneCochee);

        var colonnes = await EditionsV2.ColonnesAsync(geste, "v2.DEMANDE_EDITION", ct);
        var aRelancer = new List<(string Facture, int Origine)>();
        var sansEdition = new List<string>();
        foreach (var numero in numeros)
        {
            // 3. La facture existe ; 4. sa derniere edition de facture (_GetEditionFactureARelancer).
            int? idDemande;
            await using (var r = await geste.LireAsync("""
                SELECT d.DMND_ID_DMND FROM v2.FACTURE AS f LEFT JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
                WHERE f.FACTU_NUMERO_FACTURE = @numero
                """, ct, new SqlParameter("@numero", numero)))
            {
                if (!await r.ReadAsync(ct)) return IssueGeste.Refuser(Introuvable(numero));
                idDemande = r.IsDBNull(0) ? null : r.GetInt32(0);
            }
            int? origine = null;
            if (idDemande is { } demande && colonnes.Contains("DMNED_CODE_MAQUETTE"))
            {
                await using var e = await geste.LireAsync(
                    "SELECT DMNED_ID_DMNED, DMNED_CODE_MAQUETTE FROM v2.DEMANDE_EDITION WHERE DMNED_ID_DMND = @demande ORDER BY DMNED_ID_DMNED DESC", ct,
                    new SqlParameter("@demande", demande));
                while (origine is null && await e.ReadAsync(ct))
                    if (!e.IsDBNull(1) && MaquetteFacture().IsMatch(e.GetString(1))) origine = e.GetInt32(0);
            }
            if (origine is { } o) aRelancer.Add((numero, o));
            else sansEdition.Add(numero);
        }
        if (aRelancer.Count == 0) return IssueGeste.Refuser(SansEditionFacture);

        var compte = geste.Agent.CompteWindows.Length > 32 ? geste.Agent.CompteWindows[..32] : geste.Agent.CompteWindows;
        var traces = new List<TraceGeste>();
        var crees = new List<string>();
        foreach (var (facture, origine) in aRelancer)
        {
            var copie = await LireEditionAsync(geste, origine, ct);
            copie["DMNED_DATE_CREATION"] = geste.Maintenant;
            copie["DMNED_PHASE_TRAITEMENT"] = "C";
            foreach (var vide in Vides.Where(copie.ContainsKey)) copie[vide] = null;
            if (copie.ContainsKey("DMNED_NUM_DOCUMENT_EDITION") && !Equals(copie.GetValueOrDefault("DMNED_TYPE_GENERATION"), "EMISSION_SIMPLE"))
                copie["DMNED_NUM_DOCUMENT_EDITION"] = null;
            foreach (var agent in new[] { "DMNED_UTILISATEUR", "DMNED_INTERLOCUTEUR" }.Where(copie.ContainsKey)) copie[agent] = compte;
            if (copie.ContainsKey("DMNED_ID_DMNED_REEDITEE")) copie["DMNED_ID_DMNED_REEDITEE"] = origine;
            if (copie.ContainsKey("DMNED_MAIL_ENVOYE_VALIDATION")) copie["DMNED_MAIL_ENVOYE_VALIDATION"] = copie.GetValueOrDefault("DMNED_ENVOI_PAR_MAIL") is true;
            var id = await geste.InsererAsync("v2.DEMANDE_EDITION", "DMNED_ID_DMNED", copie, ct);
            var idTexte = id.ToString(CultureInfo.InvariantCulture);
            crees.Add(idTexte);
            traces.Add(new TraceGeste("demande", $"la réédition de la facture {facture}", idTexte, "a cree", null,
                new { Facture = facture, EditionOrigine = origine, Edition = id, File = "file des éditions" }));
        }
        var message = aRelancer.Count == 1
            ? $"Facture {aRelancer[0].Facture} : son édition n° {aRelancer[0].Origine} relancée dans la file des éditions."
            : $"{aRelancer.Count} factures : leur dernière édition relancée dans la file des éditions.";
        if (sansEdition.Count > 0) message += $" Sans édition de facture, non rééditée{(sansEdition.Count > 1 ? "s" : "")} : {string.Join(", ", sansEdition)}.";
        return IssueGeste.Ecrit(message, [.. crees], [.. traces]);
    }

    /// <summary>Toutes les colonnes de l'edition <paramref name="id"/> (DemandeEdition.Copie), son id excepte.</summary>
    private static async Task<Dictionary<string, object?>> LireEditionAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        var copie = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        await using var r = await geste.LireAsync("SELECT * FROM v2.DEMANDE_EDITION WITH (UPDLOCK) WHERE DMNED_ID_DMNED = @id", ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return copie;
        for (var i = 0; i < r.FieldCount; i++)
            if (!string.Equals(r.GetName(i), "DMNED_ID_DMNED", StringComparison.OrdinalIgnoreCase))
                copie[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return copie;
    }
}
