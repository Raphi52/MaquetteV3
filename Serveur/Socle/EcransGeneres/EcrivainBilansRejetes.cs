using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Bilans en rejet » (PROC_REJETS_BILANS) : « Envoyer vers XDCA » cree, comme RIG v2, l'affaire et la demande de correction XDCA
/// du bilan coche. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurBilansRejetes.cs (Id = l'acte depose, ACTDP_ID_ACDPS).
/// Plan « RIG v3 complet », lot 3, 28/11/2026.
///
/// SOURCE (ancien RIG) : EnvoyerVersXdca, D:\RigApplication\Source\RIG\DLL\Processus\PROC_REJETS_BILANS\ETP_REJETS_BILANS\
/// OPE_REJETS_BILANS.cs:237-262 (FDDA 17770) ; proc_rejets_bilans.md:434-439 (Detail OPE) :
///   1. une AFFAIRE neuve, « Affaire créée le <dd-MM-yyyy> » (l. 241 ; Affaire.CreateAffaireSansSauver, RigMetier\Facture\
///      Affaire.cs:122-133 : date de creation, utilisateur courant en AFFRE_ID_UTLST_CREATION) ;
///   2. une DEMANDE (Demande.CreateDemande, RigMetier\Demande\Demande.cs) sur l'action RCS et le dossier de la ligne, de l'affaire
///      neuve : etat « Q » qualifiee (EtatQualifiee, Demande.cs:45), numero de la serie DMD, nom = la designation avec civilite du
///      dossier (l. 248 ; DossierRCS.DesignationAvecCivilite, lue par DepotsRcsV2.TrouverAsync), numero indicatif = le numero de
///      gestion (l. 249), code processus « XDCA » (l. 250), date de creation ;
///   3. DMND_TEXTE_PARAMETRE = « SAUTER_ETAPE_ENTREE=1; » et DMND_EN_COURS = faux (l. 254-255), puis Sauve (l. 256).
/// La ligne passe alors « En correction » (LecteurBilansRejetes : une demande XDCA non validee sur l'action du bilan).
///
/// CONTROLES, avant toute ecriture :
///   1. UN bilan : la grille de la V2 est a selection unique (ultDataGridViewRejetsBilans.cs:187, MultiSelect = false) et le geste
///      part sur la ligne selectionnee, rien sans selection (garde SelectedIdDossierRcs != -1, l. 238). La V2 n'a pas de message
///      (le geste ne fait rien) : la V3 refuse en le disant ;
///   2. le bilan est encore un bilan en rejet : un acte depose d'un exercice, a l'un des codes de rejet de la liste
///      (LecteurBilansRejetes.CodesRejet, proc_rejets_bilans.md:501) -- la V2 n'agit que sur les lignes de sa grille.
///
/// NON REPRIS (nommes) : l'ouverture de l'ecran XDCA sur la demande (AmiCommunication.RigAccueil.ExecuteDemande, l. 257 -- RIG v3
/// n'a pas cet ecran : la demande se reprend dans RIG v2) ; l'arrivee et la ligne du rapport general que Demande.CreateDemande cree
/// pour l'ecran de recherche des demandes (ARRIVEE, LIGNE_RAPP_GENE), la categorie de la demande (DMND_ID_CTGR, Metier.CategorieId)
/// et le verrou de l'affaire (VerrouillageAffaire) : comme les autres demandes creees par la V3 (CreationV2.CreerDemandeAsync).
/// Les autres gestes de la V2 n'ecrivent pas, ou pas seuls : VGED ouvre un autre ecran (l. 230-234) ; le bordereau de
/// numerisation est une edition creee puis effacee aussitot affichee (l. 271-293) ; le renvoi a Infogreffe re-archive le PDF du
/// bilan dans Vidiged avant de marquer l'acte (l. 364-436), un service que la V3 n'appelle pas ; l'export est un fichier du poste.
/// Trace : famille « dossier », « a cree », la demande XDCA et son affaire.
/// </summary>
internal sealed class EcrivainBilansRejetes : IEcrivainEcran
{
    public string Procedure => "PROC_REJETS_BILANS";
    public string Ecran => "Bilans en rejet";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres (son bouton Valider : proc_rejets_bilans.md:208).</summary>
    internal const string EnvoyerXdca = "Envoyer vers XDCA";

    /// <summary>Ce que la V2 ecrit dans la demande (OPE_REJETS_BILANS.cs:250, :254).</summary>
    internal const string ProcessusXdca = "XDCA", SauterEntree = "SAUTER_ETAPE_ENTREE=1;";

    internal const string AucunBilan = "Aucun bilan coché : rien n'est envoyé vers XDCA.";
    internal const string UnSeulBilan = "Un seul bilan à la fois : RIG v2 envoie vers XDCA le bilan sélectionné dans sa grille, à sélection unique.";

    // Le bilan coche, s'il est encore un bilan en rejet (un acte depose d'un exercice, a un code de rejet : LecteurBilansRejetes) : son
    // action RCS et son dossier.
    private const string Bilan = $"""
        SELECT TOP (1) a.Id AS IdAction, s.Id AS IdDossier, s.NumeroGestion, p.DPTRC_NUM_DEPOT
        FROM v2.ACTE_DEPOSE AS ad
        JOIN v2.DEPOT_RCS AS p ON p.DPTRC_ID_DPTRC = ad.ACTDP_ID_DPTRC
        JOIN v2.EXERCICE AS x ON x.EXRCC_ID_EXRCC = p.DPTRC_ID_EXRCC
        JOIN rec.ActionRegistre AS a ON a.IdDepot = p.DPTRC_ID_DPTRC
        JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        WHERE ad.ACTDP_ID_ACDPS = @id AND ad.ACTDP_SAISIE_BILAN_CodeRejet IN ({LecteurBilansRejetes.CodesRejet})
        ORDER BY a.Id
        """;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != EnvoyerXdca) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Un bilan, un seul.
        if (ids.Length == 0) return IssueGeste.Refuser(AucunBilan);
        if (ids.Length > 1) return IssueGeste.Refuser(UnSeulBilan);
        // 2. Encore dans la liste.
        var refusLigne = $"Le bilan {ids[0]} n'est plus en rejet dans la base du greffe : relisez la liste.";
        if (!int.TryParse(ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var idActe)) return IssueGeste.Refuser(refusLigne);
        int idAction, idDossier;
        string numeroGestion, depot;
        await using (var r = await geste.LireAsync(Bilan, ct, new SqlParameter("@id", idActe)))
        {
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser(refusLigne);
            idAction = Convert.ToInt32(r["IdAction"], CultureInfo.InvariantCulture);
            idDossier = Convert.ToInt32(r["IdDossier"], CultureInfo.InvariantCulture);
            numeroGestion = FormeLigne.Texte(r, "NumeroGestion");
            depot = FormeLigne.Texte(r, "DPTRC_NUM_DEPOT");
        }
        var dossier = await DepotsRcsV2.TrouverAsync(geste, numeroGestion, ct);
        // L'utilisateur courant de RIG v2 (AFFRE_ID_UTLST_CREATION) : le compte de l'agent, repris de UTILISATEUR (IdV2,
        // Serveur/Recopie/FichesV3.cs:250) ; un compte sans utilisateur V2 laisse la colonne vide.
        int? idUtilisateur = null;
        await using (var r = await geste.LireAsync("SELECT IdV2 FROM soc.Compte WHERE CompteWindows = @c", ct,
                         new SqlParameter("@c", geste.Agent.CompteWindows)))
            if (await r.ReadAsync(ct) && !r.IsDBNull(0)) idUtilisateur = r.GetInt32(0);

        // 1-3. L'affaire, la demande XDCA, ses parametres (en tete du fichier).
        var (demande, refus) = await CreationV2.CreerDemandeAsync(geste, ProcessusXdca, "Q", ct);
        if (demande is null) return IssueGeste.Refuser(refus!);
        var libelle = "Affaire créée le " + geste.Maintenant.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        await geste.EcrireAsync("UPDATE v2.AFFAIRE SET AFFRE_LIBELLE = @libelle, AFFRE_ID_UTLST_CREATION = @utilisateur WHERE AFFRE_ID_AFFRE = @id", ct,
            new SqlParameter("@libelle", libelle), new SqlParameter("@utilisateur", (object?)idUtilisateur ?? DBNull.Value),
            new SqlParameter("@id", demande.IdAffaire));
        var nom = dossier?.Designation ?? "";
        await geste.EcrireAsync("""
            UPDATE v2.DEMANDE SET DMND_ID_ACTRC = @action, DMND_ID_DSRCS = @dossier, DMND_NOM = LEFT(@nom, 255),
                   DMND_NUM_INDICATIF = LEFT(@indicatif, 14), DMND_TEXTE_PARAMETRE = @parametre, DMND_EN_COURS = 0
            WHERE DMND_ID_DMND = @id
            """, ct, new SqlParameter("@action", idAction), new SqlParameter("@dossier", idDossier), new SqlParameter("@nom", nom),
            new SqlParameter("@indicatif", numeroGestion), new SqlParameter("@parametre", SauterEntree), new SqlParameter("@id", demande.Id));

        // Le numero de la demande (serie DMD, il porte le quantieme du jour) va au journal, pas dans le message que le banc compare.
        return IssueGeste.Ecrit(
            $"Bilan du dépôt {depot} envoyé vers XDCA : sa demande de correction est créée. RIG v3 n'a pas l'écran XDCA : reprendre la demande dans RIG v2.",
            [idActe.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("dossier", $"le bilan du dépôt {depot} (dossier {FormeLigne.NumeroGestion(numeroGestion)})",
                idDossier.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new { Processus = ProcessusXdca, Demande = demande.Numero, Affaire = libelle, Action = idAction, Parametres = SauterEntree }));
    }
}
