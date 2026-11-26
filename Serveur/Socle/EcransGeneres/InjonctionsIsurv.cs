using System.Globalization;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// L'INJONCTION DES SURVEILLANCES COMME RIG v2 : le moteur PROC_ISURV (D:\RigApplication\Source\RIG\DLL\Processus\SURVEILLANCES\
/// PROC_ISURV\PROC_ISURV.cs, « Injonctions des surveillances », menu ISURV visible dans RIG_DEV), partage par les ecrivains des
/// injonctions de l'ecran reuni « Relances et injonctions » : EcrivainInjonctionsComptesAnnuels (nature SDCA) et
/// EcrivainInjonctionsBeneficiaires (nature SDBE). Plan « RIG v3 100 % fonctionnel », lot 8, 26/11/2026.
///
/// LES SAISIES : l'etape obligatoire « Cabinet et decision » (ETP_CABINET_DECISION.cs:24-28, MetierObligatoire,
/// PROC_ISURV.cs:173) -- les Parametres du descripteur, que la liste a cocher envoie avec le geste (FormeFile.tsx) :
///   - le cabinet de liquidation d'astreinte (OPE_CABINET_INJONCTION, un CABINET a venir : MetierParametres « Type=FUTUR »,
///     AUDIENCE_CABINET.AUDNC_AUDIENCE = 0) : « Date du cabinet », et « Cabinet » (le code de sa chambre) quand plusieurs
///     cabinets tiennent ce jour-la -- la V3 n'a pas la fenetre de recherche des cabinets de RIG v2 ; « Heure d'appel »
///     (MetierShowHeureAppel, APPAF_HEURE_APPEL) ; « Type de classement » : a defaut, le seul type admis par le cabinet
///     (_RecopierTypeClassementDuCabinet, ETP_CABINET_DECISION.cs:91-103), sinon refus au message de RIG v2 (PROC_ISURV.cs:228-230) ;
///   - la decision d'injonction (OPE_DECISION_JUDICIAIRE, Designer :78-117) : « Juge » et « Greffier » (abreges, utilisables en
///     saisie) -- a defaut ceux du cabinet, que RIG v2 recopie au choix du cabinet (APPAF_ID_ADNC_AfterChangementValeur,
///     ETP_CABINET_DECISION.cs:45-62, :70-86) ; « Date prevue », obligatoire, au plus tot aujourd'hui (ultDateDJ_ChangementValeur,
///     OPE_DECISION_JUDICIAIRE.cs:36-43). La « Date limite pour deposer » n'est montree que si le reglage DelaiInjonction du groupe
///     vaut DTLIMIT (DoInitZoneSaisie, :29-33) ; il vaut CLASSI dans RIG_DEV : non offerte.
///   Un cabinet a moins d'un mois n'est qu'un avertissement (OPE_CABINET_INJONCTION.cs:24-28) : il est repris dans le message.
///
/// LES REGLAGES : NATURE_DILIGENCE_PARAM de la nature, processus ISURV, sans numero, type CritereMetier (GetParametre(code,
/// CritereMetier), NatureDiligence.parametre.cs:423-434 -- dans RIG_DEV, ces codes n'existent pour SDCA et SDBE que sous ISURV, lu le
/// 26/11/2026). Code affaire, nature d'affaire, acte de saisine, type de decision, designation et civilite du demandeur sont
/// exiges : sans eux RIG v2 ecrit une instance sans code ou s'arrete ; la V3 refuse en nommant le reglage absent.
///
/// CE QUI S'ECRIT, par injonction (une par exercice pour SDCA, une par dossier sinon : _PreparationObjetsBeforeOperation,
/// PROC_ISURV.cs:205-242), comme _Traitement (:244-273), _GenererNumeros (:488-516), _Sauve (:577-655) et
/// GestionDiligeanceEtRelance (:656-722) :
///   - v2.AFFAIRE et v2.DEMANDE : la demande « OPE » de l'instance (InfosInstanceGeneree.cs:34-37 ; CreationV2.CreerDemandeAsync),
///     validee, du dossier, fille de la demande du lot, son nom, son indicatif (le role) et ses numeros (role et repertoire), et son
///     lien 2J a l'instance (:600-612) ;
///   - v2.INSTANCE (Instance.CreateInstance(code, nature, « 0 », « 1 »), Instance.cs:3372-3385 ; PrepaInstance, :274-282) : saisine,
///     enrolement et acte du jour, acte de saisine du reglage, type d'instance du code affaire, numero source neuf ; role de la serie
///     du code affaire, annee en cours, et repertoire general JGAFF (:493-500) ; nom courant « demandeur # defendeur » ;
///   - v2.PARTIE, trois parties (PrepaParties, :289-343) : le DEMANDEUR du reglage (DesignationDemandeur, CiviliteDemandeur,
///     qualifiant D) ; le DEFENDEUR, le dossier (Partie.CreatePartie(numOrdre, DossierRCS), Partie.cs:453-487 : presentation « A »,
///     designation, SIREN, civilite « PM » et forme juridique d'une personne morale, l'entreprise, le dossier, l'adresse du siege) ;
///     l'INTERVENANT, le premier dirigeant representant legal vivant (DossierRCS.GetDirigeantsRepresentantLegauxVivants,
///     DossierRCS.cs ; FONCTION_DIRIGEANT.R_FONCTIONDI_REP_LEGAL, non supprime, par numero d'ordre) : nom, civilite, SIREN,
///     naissance, adresse. Toutes « copie de la decision » ;
///   - v2.CHEF_DEMANDE si le reglage CodeChef est donne (PrepaChefDemande, :345-363) : texte libre = la date de cloture de
///     l'exercice, sinon le texte du code chef ;
///   - v2.APPEL_AFFAIRE : l'appel au cabinet, numero 1, type de classement, heure (PrepaAppelAffaire, :364-367 ; la fabrique
///     AppelAffaire.CreateAppelAffaire, celle d'EcrivainRetourAudience.InsererAppelAsync) ;
///   - v2.DECISION_JUDICIAIRE (PrepaDecisionjudiciaire, :368-388 ; l'ecriture d'EcrivainMiseAuPointChambres.CreerDecisionAsync,
///     puis ce que l'injonction y pose) : type du reglage, sens general ACCTT / REJTT selon R_TYPEDECISI_ACCEPTE, avec piece
///     numerisee, GED du type, date prevue, juge, greffier, ressort / contradictoire / avant dire droit « 0 », archivage « 0 » ;
///   - v2.EVENEMENT_PROCEDURE : l'evenement du type de decision, survenu a la date prevue, et ceux de creation du code affaire,
///     survenus du jour (PrepaEveDeProcedureLieALaDecision / ...AuCodeAffaire, :389-404 ; EcrivainRetourAudience.EvenementSurvenuAsync) ;
///   - v2.DILIGENCE_RCS_PARAMETRE, v2.RELANCE_RCS, v2.DILIGENCE_RCS : la relance d'injonction sur la diligence (CreerRelanceInjonctionDCA,
///     Exercice.cs:350-369 ; CreerRelanceInjonctionDBE, DossierRCS.cs:1821-1838) : parametres de la diligence d'apres les reglages
///     (CreateDiligenceRcsParametreInjonction, DiligenceRcsParametre.cs:31-45), relance numero suivant, auteur « 4 », intervenant
///     « 1 », du jour, type IDCA / IDBE (NatureDiligence.CodeTypeRelance, NatureDiligence.cs:18-23), portant l'instance ; etat
///     relance = 2 x le nombre de relances ; pour SDCA, l'etat DCA de l'exercice = cet etat deux fois (« 44 ») ;
///   - v2.DEMANDE_EDITION : l'ordonnance, si le type de decision a un parametre d'edition present dans la base (_GenererEditions,
///     :405-447 ; RelancesRsurv.EditionAsync, entite DECI_JUDICIAIRE, numero source de la decision) ;
///   - la demande du lot « ISURV », « Injonctions du groupe <groupe> - n occurence(s) » (:651-652), validee.
/// Deux exercices d'une meme diligence : la V3 relit ses relances avant chaque injonction (numero et etat suivants).
///
/// NON REPRIS (nommes, le message du geste le dit) : la FACTURE de chaque injonction (_GenererFacture, :517-576 ; ModeFactu F ou T)
/// -- la V3 n'a pas encore la facturation, la demande reste sans facture ; les editions des evenements du code affaire
/// (evePro.CreateDemandeEdition, :431-440), les destinataires et le parametre LOT de l'ordonnance (:415-428) ; la designation du
/// defendeur avec sa forme juridique (Outils.RecupNomAvecFormeJuridique, reglage PRT_LA_SOCIETE) -- la denomination seule ;
/// l'adresse d'un siege etranger, d'un dossier de type 2 ou d'une personne physique (premier etablissement du ressort,
/// DossierRCS.cs:874-892) ; les mots directeurs de l'instance (:666) ; la ligne de rapport general (:614-622) ; le recapitulatif Word
/// des injonctions (_EditionRecapitulativeInjonction, :742-824) ; la relance RDBE d'un vieux dossier SRBE sans diligence (:682-707) :
/// les lignes de l'ecran ont toutes leur diligence ; la CREATION d'un cabinet depuis la fenetre de recherche des cabinets
/// (ParamCreationAudCab = true, OPE_CABINET_INJONCTION.cs:33) -- le cabinet doit deja exister dans AUDIENCE_CABINET. Mesure le
/// 26/11/2026 : RIG_DEV n'a AUCUN cabinet a venir (AUDNC_AUDIENCE 0, date >= aujourd'hui : 0), la copie de dev non plus (ses cabinets
/// s'arretent en 2004) ; sur ces donnees, l'injonction se refuse au controle du cabinet, et se prouve par le banc
/// (PROC_DBE_INJ.gestes, PROC_DCA_INJ.gestes).
/// </summary>
internal static class InjonctionsIsurv
{
    // Les Parametres des descripteurs PROC_DCA_INJ et PROC_DBE_INJ (Pont/EcransGeneres/Surveiller.cs), au mot pres.
    internal const string ParametreDateCabinet = "Date du cabinet", ParametreCabinet = "Cabinet", ParametreHeure = "Heure d'appel",
                          ParametreClassement = "Type de classement", ParametreJuge = "Juge", ParametreGreffier = "Greffier",
                          ParametreDatePrevue = "Date prévue";

    /// <summary>« Aucun élément selectionné » : le refus de Verifier sans ligne cochee (PROC_ISURV.cs:221-223), au mot pres.</summary>
    internal const string AucunElement = "Aucun élément selectionné";

    /// <summary>Les reglages ISURV d'une nature (NATURE_DILIGENCE_PARAM, CritereMetier, sans numero).</summary>
    internal sealed class Reglages(IReadOnlyDictionary<string, string> valeurs)
    {
        internal string? this[string code] => valeurs.TryGetValue(code, out var v) && v.Trim().Length > 0 ? v.Trim() : null;
    }

    /// <summary>Le type de decision du reglage TypeDecision (v2.TYPE_DECISION).</summary>
    internal sealed record TypeDecision(string Valeur, bool? Accepte, string? NatureDoc, string? TypeDeposant, string? NomDeposant, string? CodeEvePro,
                                        string? ParametreEdition);

    /// <summary>Ce que l'etape « Cabinet et decision » donne.</summary>
    internal sealed record Saisie(int IdCabinet, string LibelleCabinet, string? Heure, string TypeClassement, int IdJuge, int IdGreffier,
                                  DateTime DatePrevue, string? Avertissement);

    /// <summary>Tout ce qu'une injonction lit avant d'ecrire : la nature, ses reglages, la saisie, le code affaire et le type de decision.</summary>
    internal sealed record Contexte(string Nature, Reglages Reglages, Saisie Saisie, CreationV2.CodeAffaire Code, string? EvenementsCreation,
                                    TypeDecision Type);

    /// <summary>Le premier dirigeant representant legal vivant : l'intervenant de l'injonction.</summary>
    internal sealed record Intervenant(string Nom, string Civilite, string? Siren, DateTime? DateNaissance, string? Lieu, string? Departement,
                                       string? Arrondissement, string? Pays, int? IdAdresse);

    /// <summary>Le dossier enjoint : le defendeur et son intervenant.</summary>
    internal sealed record Dossier(int Id, string NumeroGestion, string Nom, string Designation, string? Siren, bool PersonneMorale, string Civilite,
                                   string? FormeJuridique, DateTime? DateNaissance, int? IdEntreprise, int? IdAdresse, Intervenant? Intervenant);

    /// <summary>Une injonction ecrite : son instance, ses numeros, son ordonnance, sa trace.</summary>
    internal sealed record Enjointe(int IdInstance, string Role, string? Repertoire, string? Ordonnance, TraceGeste Trace);

    // ------------------------------------------------------------------ CE QUE L'INJONCTION LIT AVANT D'ECRIRE

    /// <summary>Les reglages, la saisie, le code affaire et le type de decision ; un refus nomme ce qui manque, rien n'est ecrit.</summary>
    internal static async Task<(Contexte? Contexte, string? Refus)> PreparerAsync(GesteEnCours geste, string nature, CancellationToken ct)
    {
        if (await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.NATURE_DILIGENCE_PARAM')", ct) is DBNull or null)
            return (null, "Les réglages des injonctions (table NATURE_DILIGENCE_PARAM) sont absents de la base du greffe : rien n'est enjoint.");
        var valeurs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await geste.LireAsync("""
            SELECT NADIP_CODE_PARAM, NADIP_VALEUR_PARAM FROM v2.NATURE_DILIGENCE_PARAM
            WHERE NADIP_NATURE_DILIGENCE = @nature AND NADIP_CODE_PROCESSUS = 'ISURV' AND NADIP_NUM_ORDRE IS NULL AND NADIP_TYPE_PARAM = 'CritereMetier'
            ORDER BY NADIP_ID_NADIP
            """, ct, new SqlParameter("@nature", nature)))
            while (await r.ReadAsync(ct)) valeurs.TryAdd(FormeLigne.Texte(r, "NADIP_CODE_PARAM"), FormeLigne.Texte(r, "NADIP_VALEUR_PARAM"));
        var reglages = new Reglages(valeurs);
        var manquants = new[] { "CodeAffaire", "NatureAffaire", "ActeSaisine", "TypeDecision", "DesignationDemandeur", "CiviliteDemandeur" }
            .Where(c => reglages[c] is null).ToArray();
        if (manquants.Length > 0)
            return (null, $"Réglage{(manquants.Length > 1 ? "s" : "")} des injonctions {nature} absent{(manquants.Length > 1 ? "s" : "")} de la base du greffe " +
                          $"({string.Join(", ", manquants)}, processus ISURV) : rien n'est enjoint.");

        var (saisie, refusSaisie) = await SaisieAsync(geste, ct);
        if (saisie is null) return (null, refusSaisie);

        var code = await CreationV2.CodeAffaireAsync(geste, reglages["CodeAffaire"]!, reglages["ActeSaisine"]!, ct);
        if (code is null) return (null, $"Code affaire « {reglages["CodeAffaire"]} » des injonctions {nature} inconnu dans la base du greffe : rien n'est enjoint.");
        var creation = await RelancesRsurv.Scalaire(geste, "SELECT R_CODEAFFAIR_CODE_EVE_PRO_CREA FROM v2.CODE_AFFAIRE WHERE R_CODEAFFAIR_VALEUR = @c", ct,
                                                    new SqlParameter("@c", code.Valeur)) as string;
        TypeDecision? type = null;
        await using (var r = await geste.LireAsync("""
            SELECT R_TYPEDECISI_VALEUR, R_TYPEDECISI_ACCEPTE, R_TYPEDECISI_NATURE_DOC_GED, R_TYPEDECISI_TYPE_DEPOSANT_GED, R_TYPEDECISI_NOM_DEPOSANT_GED,
                   R_TYPEDECISI_CODE_EVE_PRO, R_TYPEDECISI_PARAMETRE_EDITION
            FROM v2.TYPE_DECISION WHERE R_TYPEDECISI_VALEUR = @t
            """, ct, new SqlParameter("@t", reglages["TypeDecision"]!)))
            if (await r.ReadAsync(ct))
            {
                string? Opt(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
                type = new TypeDecision(FormeLigne.Texte(r, "R_TYPEDECISI_VALEUR"), r["R_TYPEDECISI_ACCEPTE"] is bool b ? b : null,
                    Opt("R_TYPEDECISI_NATURE_DOC_GED"), Opt("R_TYPEDECISI_TYPE_DEPOSANT_GED"), Opt("R_TYPEDECISI_NOM_DEPOSANT_GED"),
                    Opt("R_TYPEDECISI_CODE_EVE_PRO"), Opt("R_TYPEDECISI_PARAMETRE_EDITION"));
            }
        if (type is null) return (null, $"Type de décision « {reglages["TypeDecision"]} » des injonctions {nature} inconnu dans la base du greffe : rien n'est enjoint.");
        return (new Contexte(nature, reglages, saisie, code, creation, type), null);
    }

    /// <summary>L'etape « Cabinet et decision » (en tete du fichier) : ses controles, aux messages de RIG v2 quand il en a un.</summary>
    private static async Task<(Saisie? Saisie, string? Refus)> SaisieAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string libelle) => geste.Demande.Valeurs.TryGetValue(libelle, out var v) ? v.Trim() : "";
        var jour = geste.Maintenant.Date;

        // Le cabinet de liquidation d'astreinte, a venir (« Type=FUTUR »).
        if (S(ParametreDateCabinet).Length == 0) return (null, CreationV2.Obligatoire(ParametreDateCabinet));
        if (AudiencesV2.Date(S(ParametreDateCabinet)) is not { } dateCabinet)
            return (null, $"« {ParametreDateCabinet} » : date illisible ({S(ParametreDateCabinet)}).");
        if (dateCabinet < jour)
            return (null, $"Le cabinet de liquidation d'astreinte doit être à venir : RIG v2 ne propose que les cabinets futurs ({FormeLigne.Date(dateCabinet)}).");
        var chambre = S(ParametreCabinet) is { Length: > 0 } c ? c : null;
        var cabinets = await EcrivainRetourAudience.AudiencesDuJourAsync(geste, dateCabinet, false, chambre, ct);
        if (cabinets.Count == 0)
            return (null, $"Aucun cabinet le {FormeLigne.Date(dateCabinet)}{(chambre is null ? "" : $" pour la chambre « {chambre} »")} dans la base du greffe.");
        if (cabinets.Count > 1)
            return (null, $"{cabinets.Count} cabinets le {FormeLigne.Date(dateCabinet)} : précisez « {ParametreCabinet} » (le code de sa chambre).");
        var (idCabinet, libelle) = cabinets[0];
        int? jugeCabinet = null, greffierCabinet = null;
        var admis = "";
        await using (var r = await geste.LireAsync("SELECT AUDNC_ID_JUG, AUDNC_ID_GRASS, AUDNC_TYPE_CLASSEMENT_ADMIS FROM v2.AUDIENCE_CABINET WHERE AUDNC_ID_ADNC = @id",
                         ct, new SqlParameter("@id", idCabinet)))
            if (await r.ReadAsync(ct))
            {
                jugeCabinet = r["AUDNC_ID_JUG"] is DBNull ? null : Convert.ToInt32(r["AUDNC_ID_JUG"], CultureInfo.InvariantCulture);
                greffierCabinet = r["AUDNC_ID_GRASS"] is DBNull ? null : Convert.ToInt32(r["AUDNC_ID_GRASS"], CultureInfo.InvariantCulture);
                admis = FormeLigne.Texte(r, "AUDNC_TYPE_CLASSEMENT_ADMIS");
            }
        var heure = S(ParametreHeure) is { Length: > 0 } h ? h : null;
        if (heure is { Length: > 5 }) return (null, $"« {ParametreHeure} » : cinq caractères au plus (« 09:30 »).");

        // Le type de classement : saisi, sinon le seul que le cabinet admet.
        var classement = S(ParametreClassement);
        if (classement.Length == 0)
        {
            var types = admis.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (types.Length == 1) classement = types[0];
        }
        if (classement.Length == 0) return (null, "Le type classement du cabinet est obligatoire");
        if (classement.Length > 5) return (null, $"« {ParametreClassement} » : cinq caractères au plus.");

        // Le juge et le greffier : saisis par leur abrege, sinon ceux du cabinet.
        var juge = jugeCabinet;
        if (S(ParametreJuge) is { Length: > 0 } abregeJuge)
        {
            juge = await PersonneAsync(geste, "v2.JUGE", "JUG_ID_JUG", "JUG_ABREGE", "JUG_ACTIF", abregeJuge, ct);
            if (juge is null) return (null, $"Le juge « {abregeJuge} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");
        }
        if (juge is null) return (null, CreationV2.Obligatoire(ParametreJuge) + " Le cabinet choisi n'a pas de juge.");
        var greffier = greffierCabinet;
        if (S(ParametreGreffier) is { Length: > 0 } abregeGreffier)
        {
            greffier = await PersonneAsync(geste, "v2.GREFFIER_ASSERMENTE", "GRASS_ID_GRASS", "GRASS_ABREGE", "GRASS_ACTIF", abregeGreffier, ct);
            if (greffier is null)
                return (null, $"Le greffier « {abregeGreffier} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");
        }
        if (greffier is null) return (null, CreationV2.Obligatoire(ParametreGreffier) + " Le cabinet choisi n'a pas de greffier.");

        // La date prevue de la decision.
        if (S(ParametreDatePrevue).Length == 0) return (null, CreationV2.Obligatoire(ParametreDatePrevue));
        if (AudiencesV2.Date(S(ParametreDatePrevue)) is not { } datePrevue) return (null, $"« {ParametreDatePrevue} » : date illisible ({S(ParametreDatePrevue)}).");
        if (datePrevue < jour) return (null, "La date de décision doit être supérieur ou égale à la date du jour");

        var avertissement = dateCabinet < jour.AddMonths(1) ? "Attention, vous avez choisit un cabinet à moins de 1 mois de la date du jour" : null;
        return (new Saisie(idCabinet, libelle, heure, classement, juge.Value, greffier.Value, datePrevue, avertissement), null);
    }

    // La meme lecture qu'EcrivainComposition.PersonneAsync (privee, dans le fichier du lot 7 encore en cours dans un autre fil le
    // 26/11/2026) : abrege non vide, utilisable en saisie, porte par une seule personne. A reunir quand le lot 7 est integre.
    private static async Task<int?> PersonneAsync(GesteEnCours geste, string table, string colonneId, string colonneAbrege, string colonneActif,
                                                 string abrege, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(
            $"SELECT TOP (2) {colonneId} AS Id FROM {table} WHERE {colonneAbrege} = @a AND {colonneActif} = 1 ORDER BY {colonneId}",
            ct, new SqlParameter("@a", abrege));
        if (!await r.ReadAsync(ct)) return null;
        var id = Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture);
        return await r.ReadAsync(ct) ? null : id;
    }

    /// <summary>
    /// Le dossier <paramref name="idDossier"/> en defendeur et son intervenant ; un refus si RIG v2 ne pourrait pas creer l'intervenant
    /// (PrepaParties prend le premier dirigeant par .First(), PROC_ISURV.cs:305 et :327 : sans dirigeant, il s'arrete).
    /// </summary>
    internal static async Task<(Dossier? Dossier, string? Refus)> DossierAsync(GesteEnCours geste, int idDossier, CancellationToken ct)
    {
        Dossier? d = null;
        await using (var r = await geste.LireAsync("""
            SELECT s.Id, s.NumeroGestion, s.Siren, s.TypeRcs, s.IdEntreprise, e.Civilite, e.Denomination, e.Patronyme, e.Prenom, e.NomUsage,
                   e.FormeJuridique, e.DateNaissance,
                   (SELECT TOP (1) et.ETBLS_ID_ADRBS FROM v2.ETABLISSEMENT AS et
                    WHERE et.ETBLS_ID_DSRCS = s.Id AND et.ETBLS_TYPE_ADRESSE IN ('SPM', 'HRM') AND et.ETBLS_SUPPRIME = 0 ORDER BY et.ETBLS_ID_ETBLS) AS Siege
            FROM rec.DossierRcs AS s LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
            WHERE s.Id = @id
            """, ct, new SqlParameter("@id", idDossier)))
            if (await r.ReadAsync(ct))
            {
                string? Opt(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
                // Personne morale : une entreprise sans patronyme (la designation de RIG v2 : InscritPM.Designation, sinon le nom d'usage
                // ou le patronyme et le prenom de l'inscrit, Partie.GenererNomPartie, Partie.cs:1196-1222).
                var pm = Opt("Patronyme") is null;
                var designation = pm ? FormeLigne.Texte(r, "Denomination")
                    : string.Join(' ', new[] { Opt("NomUsage") ?? Opt("Patronyme"), Opt("Prenom") }.Where(x => x is not null));
                // L'adresse du siege : l'etablissement referent d'une personne morale de type 1 (DossierRCS.cs:874-892, siege SPM ou HRM).
                var type1 = FormeLigne.Texte(r, "TypeRcs").EndsWith('1');
                d = new Dossier(idDossier, FormeLigne.Texte(r, "NumeroGestion"), FormeRelance.NomDossier(r), designation, Opt("Siren"), pm,
                    pm ? "PM" : Opt("Civilite") ?? "", pm ? Opt("FormeJuridique") : null, pm ? null : FormeLigne.DateDe(r, "DateNaissance"),
                    r["IdEntreprise"] is DBNull ? null : Convert.ToInt32(r["IdEntreprise"], CultureInfo.InvariantCulture),
                    pm && type1 && r["Siege"] is not DBNull ? Convert.ToInt32(r["Siege"], CultureInfo.InvariantCulture) : null, null);
            }
        if (d is null) return (null, $"Dossier introuvable dans la base du greffe ({idDossier}).");

        Intervenant? intervenant = null;
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) dg.DRGNT_NUM_SIREN, dg.DRGNT_ID_ADRBS, pp.DRGPP_ID_DRGPP, pp.DRGPP_CIVILITE, pp.DRGPP_NOM_USAGE, pp.DRGPP_PATRONYME,
                   pp.DRGPP_PRENOM_USAGE, pp.DRGPP_PRENOMS_ETAT_CIVIL, pp.DRGPP_DATE_NAISSANCE, pp.DRGPP_LIEU_NAISSANCE, pp.DRGPP_DEPARTEMENT_NAISS,
                   pp.DRGPP_ARROND_NAISSANCE, pp.DRGPP_PAYS, pm.DRGPM_DENOMINATION_USAGE, pm.DRGPM_DENOMINATION_STATUT
            FROM v2.DIRIGEANT AS dg
            JOIN v2.FONCTION_DIRIGEANT AS f ON f.R_FONCTIONDI_VALEUR = dg.DRGNT_FONCTION_DIRIGEANT AND f.R_FONCTIONDI_REP_LEGAL = 1
            LEFT JOIN v2.DIRIGEANT_PP AS pp ON pp.DRGPP_ID_DRGNT = dg.DRGNT_ID_DRGNT
            LEFT JOIN v2.DIRIGEANT_PM AS pm ON pm.DRGPM_ID_DRGNT = dg.DRGNT_ID_DRGNT
            WHERE dg.DRGNT_ID_DSRCS = @id AND dg.DRGNT_SUPPRIME = 0
            ORDER BY dg.DRGNT_NUM_ORDRE, dg.DRGNT_ID_DRGNT
            """, ct, new SqlParameter("@id", idDossier)))
            if (await r.ReadAsync(ct))
            {
                string? Opt(string c) => FormeLigne.Texte(r, c) is { Length: > 0 } s ? s : null;
                var pp = r["DRGPP_ID_DRGPP"] is not DBNull;
                // DesignationShortSansCivilite (Dirigeant.cs:159) : nom d'usage (sinon patronyme) et prenom d'usage (sinon prenoms) d'une
                // personne physique ; la denomination d'une personne morale (DRGPM_DENOMINATION_USAGE, sinon celle des statuts).
                var nom = pp ? $"{Opt("DRGPP_NOM_USAGE") ?? Opt("DRGPP_PATRONYME")} {Opt("DRGPP_PRENOM_USAGE") ?? Opt("DRGPP_PRENOMS_ETAT_CIVIL")}".Trim()
                             : Opt("DRGPM_DENOMINATION_USAGE") ?? Opt("DRGPM_DENOMINATION_STATUT") ?? "";
                intervenant = new Intervenant(nom, pp ? Opt("DRGPP_CIVILITE") ?? "" : "PM", Opt("DRGNT_NUM_SIREN"),
                    pp ? FormeLigne.DateDe(r, "DRGPP_DATE_NAISSANCE") : null, pp ? Opt("DRGPP_LIEU_NAISSANCE") : null,
                    pp ? Opt("DRGPP_DEPARTEMENT_NAISS") : null, pp ? Opt("DRGPP_ARROND_NAISSANCE") : null, pp ? Opt("DRGPP_PAYS") : null,
                    r["DRGNT_ID_ADRBS"] is DBNull ? null : Convert.ToInt32(r["DRGNT_ID_ADRBS"], CultureInfo.InvariantCulture));
            }
        if (intervenant is null)
            return (null, $"{d.Nom} : aucun dirigeant représentant légal en fonction ; RIG v2 ne peut pas créer l'intervenant de l'injonction (rien n'est enjoint).");
        return (d with { Intervenant = intervenant }, null);
    }

    // ------------------------------------------------------------------ CE QUE L'INJONCTION ECRIT

    /// <summary>La demande du lot (PROC_ISURV.cs:651-652), validee : « Injonctions du groupe <groupe> - n occurence(s) ».</summary>
    internal static async Task<(int? Id, string? Refus)> LotAsync(GesteEnCours geste, string nature, int injonctions, CancellationToken ct)
    {
        var lot = await RelancesRsurv.DemandeAsync(geste, null, $"Injonctions du groupe {await RelancesRsurv.GroupeAsync(geste, nature, ct)} - {injonctions} occurence(s)",
                                                   null, null, null, null, ct, "ISURV");
        return (lot.Id, lot.Refus);
    }

    /// <summary>
    /// UNE injonction du dossier <paramref name="d"/> sur sa diligence <paramref name="idDiligence"/> (et l'exercice <paramref name="idExercice"/>
    /// cloture le <paramref name="cloture"/> pour SDCA) : tout ce que l'en-tete du fichier nomme, dans la transaction du geste.
    /// </summary>
    internal static async Task<(Enjointe? Enjointe, string? Refus)> EnjoindreAsync(GesteEnCours geste, Contexte c, Dossier d, int idDiligence,
        int? idExercice, DateTime? cloture, int idLot, CancellationToken ct)
    {
        var jour = geste.Maintenant.Date;
        var annee = geste.Maintenant.Year;
        var reglages = c.Reglages;

        // Les numeros (_GenererNumeros, PROC_ISURV.cs:493-500) : le role du code affaire et le repertoire general, annee en cours.
        var (role, refusRole) = await CreationV2.NumeroAsync(geste, c.Code.TypeNumRole, annee, ct);
        if (role is null) return (null, refusRole);
        var (repertoire, refusRep) = await CreationV2.NumeroAsync(geste, "JGAFF", annee, ct);
        if (repertoire is null) return (null, refusRep);

        // La demande OPE de l'instance et son affaire.
        var (demande, refusDemande) = await CreationV2.CreerDemandeAsync(geste, "OPE", "V", ct);
        if (demande is null) return (null, refusDemande);
        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ID_DSRCS = @dossier, DMND_ID_DMND_MERE = @lot, DMND_DATE_VALIDATION = @maintenant WHERE DMND_ID_DMND = @id", ct,
            new SqlParameter("@dossier", d.Id), new SqlParameter("@lot", idLot), new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@id", demande.Id));

        // L'instance.
        var demandeur = reglages["DesignationDemandeur"]!;
        var nomCourant = CreationV2.NomCourant(demandeur, d.Designation);
        var idInstance = await CreationV2.InsererAsync(geste, "v2.INSTANCE", "INSTN_ID_INSTN", new Dictionary<string, object?>
        {
            ["INSTN_ID_AFFRE"] = demande.IdAffaire, ["INSTN_CODE_AFFAIRE"] = c.Code.Valeur, ["INSTN_NATURE_AFFAIRE"] = reglages["NatureAffaire"],
            ["INSTN_ETAT_INSTANCE"] = "0", ["INSTN_ETAT_INFO"] = "1", ["INSTN_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            ["INSTN_DATE_SAISINE_GREFFE"] = jour, ["INSTN_DATE_ENROLEMENT"] = jour, ["INSTN_CODE_ACTE_PROCE"] = reglages["ActeSaisine"],
            ["INSTN_DATE_ACTE"] = jour, ["INSTN_TYPE_INSTANCE"] = c.Code.TypeInstance, ["INSTN_NUM_ROLE"] = role, ["INSTN_NUM_REP_GENERAL"] = repertoire,
            ["INSTN_NOM_COURANT"] = nomCourant,
        }, ct);

        // Les trois parties.
        static string? Couper(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];   // Instance.TronquageAuto (PROC_ISURV.cs:579)
        var parties = new (string Qualif, Dictionary<string, object?> Valeurs)[]
        {
            ("D", new() { ["PRT_DESIGNATION"] = demandeur, ["PRT_NOM"] = demandeur, ["PRT_CIVILITE"] = Couper(reglages["CiviliteDemandeur"], 7) }),
            ("F", new()
            {
                ["PRT_MODE_PRESENTATION"] = "A", ["PRT_DESIGNATION"] = d.Designation, ["PRT_NOM"] = d.Designation, ["PRT_SIREN"] = d.Siren,
                ["PRT_CIVILITE"] = Couper(d.Civilite, 7), ["PRT_FORME_JURIDIQUE_UTI"] = d.FormeJuridique, ["PRT_DATE_NAISSANCE"] = d.DateNaissance,
                ["PRT_ID_DSRCS"] = d.Id, ["PRT_ID_RPRTR"] = d.IdEntreprise, ["PRT_ID_ADRBS"] = d.IdAdresse,
            }),
            ("I", new()
            {
                ["PRT_DESIGNATION"] = d.Intervenant!.Nom, ["PRT_NOM"] = d.Intervenant.Nom, ["PRT_CIVILITE"] = Couper(d.Intervenant.Civilite, 7),
                ["PRT_SIREN"] = d.Intervenant.Siren, ["PRT_DATE_NAISSANCE"] = d.Intervenant.DateNaissance,
                ["PRT_LIEU_NAISSANCE"] = Couper(d.Intervenant.Lieu, 50), ["PRT_DEPARTEMENT_NAISS"] = Couper(d.Intervenant.Departement, 3),
                ["PRT_ARROND_NAISSANCE"] = Couper(d.Intervenant.Arrondissement, 2), ["PRT_PAYS"] = Couper(d.Intervenant.Pays, 3),
                ["PRT_ID_ADRBS"] = d.Intervenant.IdAdresse,
            }),
        };
        foreach (var (qualif, valeurs) in parties)
        {
            valeurs["PRT_ID_INSTN"] = idInstance;
            valeurs["PRT_NUM_ORDRE"] = (short)1;
            valeurs["PRT_QUALIF_PARTIE"] = qualif;
            valeurs["PRT_COPIE_DECISION"] = true;
            valeurs["PRT_EDITION_JUGEMENT"] = "D";
            valeurs["PRT_MANDATAIRE_EDITION_JUGEMENT"] = "D";
            valeurs["PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT"] = "D";
            await CreationV2.InsererAsync(geste, "v2.PARTIE", "PRT_ID_PRT", valeurs.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value), ct);
        }

        // Le chef de demande.
        if (reglages["CodeChef"] is { } codeChef)
        {
            var texte = cloture is { } dc ? FormeLigne.Date(dc)
                : await RelancesRsurv.Scalaire(geste, "SELECT R_CODECHEF_TEXTE_DEMANDE FROM v2.CODE_CHEF WHERE R_CODECHEF_VALEUR = @c", ct,
                                               new SqlParameter("@c", codeChef)) as string;
            await geste.InsererAsync("v2.CHEF_DEMANDE", "CHFDM_ID_CHFDM", new Dictionary<string, object?>
            {
                ["CHFDM_ID_INSTN"] = idInstance, ["CHFDM_NUM_ORDRE"] = (short)1, ["CHFDM_CODE_CHEF"] = codeChef, ["CHFDM_TEXTE_LIBRE"] = texte,
            }, ct);
        }

        // L'appel au cabinet, puis son heure.
        var appel = await EcrivainRetourAudience.InsererAppelAsync(geste, idInstance, role, 1, c.Saisie.IdCabinet, null, c.Saisie.TypeClassement,
                                                                   c.Saisie.LibelleCabinet, ct);
        if (c.Saisie.Heure is { } heure)
            await geste.EcrireAsync("UPDATE v2.APPEL_AFFAIRE SET APPAF_HEURE_APPEL = @h WHERE APPAF_ID_APPAF = @id", ct,
                new SqlParameter("@h", heure), new SqlParameter("@id", int.Parse(appel.IdObjet!, CultureInfo.InvariantCulture)));

        // La decision d'injonction.
        var decision = (await EcrivainMiseAuPointChambres.CreerDecisionAsync(geste, idInstance, role, c.Type.Valeur, c.Saisie.DatePrevue, null, ct))[0];
        var idDecision = int.Parse(decision.IdObjet!, CultureInfo.InvariantCulture);
        await geste.EcrireAsync("""
            UPDATE v2.DECISION_JUDICIAIRE SET DCJDC_ID_JUG = @juge, DCJDC_ID_GRASS = @greffier, DCJDC_SENS_GENERAL = @sens, DCJDC_AVEC_PIECE_NUMERISE = 1,
                   DCJDC_NATURE_DOC_GED = @nature, DCJDC_TYPE_DEPOSANT_GED = @typeDeposant, DCJDC_NOM_DEPOSANT_GED = @nomDeposant,
                   DCJDC_ETAT_ARCHIVAGE = '0', DCJDC_MODE_ARCHIVAGE = '0'
            WHERE DCJDC_ID_DCJDC = @id
            """, ct, new SqlParameter("@juge", c.Saisie.IdJuge), new SqlParameter("@greffier", c.Saisie.IdGreffier),
            new SqlParameter("@sens", c.Type.Accepte is { } a ? a ? "ACCTT" : "REJTT" : DBNull.Value),
            new SqlParameter("@nature", (object?)c.Type.NatureDoc ?? DBNull.Value), new SqlParameter("@typeDeposant", (object?)c.Type.TypeDeposant ?? DBNull.Value),
            new SqlParameter("@nomDeposant", (object?)c.Type.NomDeposant ?? DBNull.Value), new SqlParameter("@id", idDecision));

        // Les evenements : celui de la decision (date prevue), ceux de creation du code affaire (du jour) qui existent dans CODE_EVE_PRO.
        var evenements = new List<string>();
        if (c.Type.CodeEvePro is { } eveDecision)
        {
            await EcrivainRetourAudience.EvenementSurvenuAsync(geste, idInstance, eveDecision, c.Saisie.DatePrevue, ct);
            evenements.Add(eveDecision);
        }
        foreach (var eve in (c.EvenementsCreation ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (await RelancesRsurv.Scalaire(geste, "SELECT COUNT(*) FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct, new SqlParameter("@c", eve)) is int n && n > 0)
            {
                await EcrivainRetourAudience.EvenementSurvenuAsync(geste, idInstance, eve, jour, ct);
                evenements.Add(eve);
            }

        // La relance d'injonction sur la diligence, et ses parametres.
        var idParametre = await geste.InsererAsync("v2.DILIGENCE_RCS_PARAMETRE", "DLGPA_ID_DLGPA", new Dictionary<string, object?>
        {
            ["DLGPA_EDITIONS_DIFFEREES"] = reglages["ForcerDiffere"] == "1", ["DLGPA_NON_DISTRIBUE"] = reglages["ApresAvise"],
            ["DLGPA_ASTREINTE"] = reglages["Astreinte"] == "1", ["DLGPA_TAUX_ASTREINTE"] = 0d, ["DLGPA_NPAI"] = reglages["TraitementNpai"],
            ["DLGPA_RETRAIT_ROLE"] = reglages["ModaliteRetraitRole"], ["DLGPA_RETRAIT_ROLE_MENTION_NPAI"] = reglages["RetraitRoleMentionNPAI"],
            ["DLGPA_RETRAIT_ROLE_MENTION_DEPOT"] = reglages["RetraitRoleMentionDepot"], ["DLGPA_RETRAIT_ROLE_ORDO_NPAI"] = reglages["RetraitRoleOrdoNPAI"],
            ["DLGPA_RETRAIT_ROLE_ORDO_DEPOT"] = reglages["RetraitRoleOrdoDepot"], ["DLGPA_TARIFER"] = reglages["ModeFactu"] == "T",
        }, ct);
        var relances = Convert.ToInt32(await RelancesRsurv.Scalaire(geste, "SELECT COUNT(*) FROM v2.RELANCE_RCS WHERE RLCRC_ID_DLGRC = @g", ct,
                                                                     new SqlParameter("@g", idDiligence)), CultureInfo.InvariantCulture) + 1;
        var typeRelance = c.Nature switch { "SDBE" => "IDBE", "SDCA" => "IDCA", _ => "I" + c.Nature };
        await geste.InsererAsync("v2.RELANCE_RCS", "RLCRC_ID_RLCRC", new Dictionary<string, object?>
        {
            ["RLCRC_ID_DLGRC"] = idDiligence, ["RLCRC_NUM_ORDRE"] = (short)relances, ["RLCRC_AUTEUR"] = "4", ["RLCRC_INTERVENANT"] = "1",
            ["RLCRC_DATE_RELANCE"] = jour, ["RLCRC_TYPE_RELANCE"] = typeRelance, ["RLCRC_ID_INSTN"] = idInstance,
        }, ct);
        var etatRelance = (relances * 2).ToString(CultureInfo.InvariantCulture);
        await geste.EcrireAsync("UPDATE v2.DILIGENCE_RCS SET DLGRC_ID_DLGPA = @p, DLGRC_ETAT_RELANCE = @etat WHERE DLGRC_ID_DLGRC = @g", ct,
            new SqlParameter("@p", idParametre), new SqlParameter("@etat", etatRelance), new SqlParameter("@g", idDiligence));
        if (idExercice is { } x)
            await geste.EcrireAsync("UPDATE v2.EXERCICE SET EXRCC_ETAT_DCA = @etat WHERE EXRCC_ID_EXRCC = @x", ct,
                new SqlParameter("@etat", etatRelance + etatRelance), new SqlParameter("@x", x));

        // La demande : son nom (le nom courant de l'instance), son indicatif, ses numeros, son lien a l'instance.
        await CreationV2.RattacherAsync(geste, demande, nomCourant, role, $"{role} {repertoire}".Trim(), VerrouDemandeV2.SequenceurInstance,
                                        idInstance.ToString(CultureInfo.InvariantCulture), ct);

        // L'ordonnance.
        string? ordonnance = null;
        if (c.Type.ParametreEdition is { } parametre)
        {
            var numeroSource = await RelancesRsurv.Scalaire(geste, "SELECT DCJDC_NUMERO_SOURCE FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_DCJDC = @id", ct,
                                                            new SqlParameter("@id", idDecision)) as string ?? "";
            ordonnance = await RelancesRsurv.EditionAsync(geste, demande.Id, parametre, "", "DECI_JUDICIAIRE", numeroSource, null, null, ct);
        }

        var trace = new TraceGeste("affaire", $"l'injonction {role} du dossier {d.NumeroGestion}", idInstance.ToString(CultureInfo.InvariantCulture), "a cree", null,
            new Dictionary<string, string?>
            {
                ["Role"] = role, ["Repertoire"] = repertoire, ["Demande"] = demande.Numero, ["Nature"] = c.Nature, ["Dossier"] = d.NumeroGestion,
                ["Exercice"] = cloture is { } cl ? FormeLigne.Date(cl) : null, ["Cabinet"] = c.Saisie.LibelleCabinet, ["TypeClassement"] = c.Saisie.TypeClassement,
                ["Decision"] = c.Type.Valeur, ["DatePrevue"] = FormeLigne.Date(c.Saisie.DatePrevue), ["Evenements"] = string.Join(", ", evenements),
                ["Relance"] = $"{typeRelance} n° {relances} (diligence {idDiligence}, état relance {etatRelance})",
                ["Ordonnance"] = ordonnance ?? "aucune : pas de paramètre d'édition",
            });
        return (new Enjointe(idInstance, role, repertoire, ordonnance, trace), null);
    }

    /// <summary>Le message de l'ecran : « Traitements effectues » (PROC_ISURV.cs:675), les roles, l'ordonnance, l'avertissement du
    /// cabinet, et ce que la V3 ne reprend pas.</summary>
    internal static string Message(IReadOnlyCollection<Enjointe> faites, int dossiers, Saisie saisie)
    {
        var ordonnances = faites.Where(f => f.Ordonnance is not null).Select(f => f.Ordonnance!).Distinct().ToArray();
        var n = faites.Count;
        return FormeLigne.Joindre(
                   $"Traitements effectués : {n} injonction{(n > 1 ? "s" : "")} pour {dossiers} dossier{(dossiers > 1 ? "s" : "")}, " +
                   $"rôle{(n > 1 ? "s" : "")} {string.Join(", ", faites.Select(f => f.Role))}",
                   saisie.LibelleCabinet,
                   ordonnances.Length == 0 ? "aucune ordonnance, pas de paramètre d'édition pour ce type de décision dans la base du greffe"
                                           : $"ordonnance « {string.Join(" », « ", ordonnances)} » en file d'édition",
                   saisie.Avertissement)
               + ". Non repris par la V3 : la facture, les mots directeurs et le récapitulatif Word des injonctions.";
    }
}
