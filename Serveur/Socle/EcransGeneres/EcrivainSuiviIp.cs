using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurSuiviIp;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Suivi des preventions » (PROC_IPSUIVI) : « Valider » sur la fiche d'UNE injonction de payer (Id = l'instance IP, la cle de
/// LecteurSuiviIp.cs) enregistre la date saisie -- desistement, signification (et « à personne »), certificat de non-opposition,
/// opposition, reception de la LRAR, provision -- ou le 2e executoire, comme RIG v2 : l'evenement attendu SURVIENT (ou EXPIRE hors
/// delai), l'evenement suivant est cree, l'opposition est enrolee au fond ou une OP de caducite ouverte, les editions partent en
/// file. Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_IPSUIVI\Etape\OPE_IPSUIVI.cs -- le choix du traitement
/// (_TypeTraitement, :168-226), les dates saisies (ult_Signification_ChangementValeur :2769-2875, ult_Opposition_ChangementValeur
/// :2877-2935, ult_Certif_Non_Opposition_ChangementValeur :2950-3017, ult_Provision_ChangementValeur :3029-3240), leurs controles
/// (VerifierSaisie, :2696-2766), les traitements de PreparerObjectsEtEditions (:1762-2140), les grisages du chargement (Remplir_IP,
/// :845-1125, et _VerifierCoherenceIP, :1149-1160), une seule date par validation (_SetULTsAccessibility, :1196-1250, sauf
/// signification + certificat, :101-103), la note (:2150) et les demandes validees (MajListeDemande, :2492-2520, :2606-2608) ; le
/// moteur des evenements de RigMetier (JUDICIAIRE\EvenementProcedure.cs : PasserEnSurvenuEveProFutur :1968-1997,
/// creationEvenementFutur :1769-1810 ; les delais du futur, Fonctions diverses\Outils.cs:520-565) ; l'OP sur l'IP :
/// Instance.CreateInstanceOPSurIP (EcrivainSurveillanceIp.CreerOpAsync) ; les editions : EditionsEvenement.cs.
///
/// CONTROLES, dans l'ordre de RIG v2, avant toute ecriture (un refus n'ecrit rien) :
///   1. l'instance est une IP de la base (InstanceIP_EnCours n'accepte qu'une IP, :135-143) ; le verrou de RIG v2 (VerrouDemandeV2) ;
///   2. les alertes de Remplir_IP au mot pres (:976-1050) : desistement survenu, IP europeenne, IP rejetee ; decision sans date --
///      « Date de la décision vide, vous devez aller en ORDOSIGN. » sauf pour le desistement (:1004-1013) ; opposition enrolee (instance
///      TIP1 jointe, :1014-1021) ; aucun evenement futur (:1024-1050) ; une IP close (evenement de fin survenu, :1112-1114) ; les
///      anomalies de _VerifierCoherenceIP (futurs de l'autre loi, plusieurs futurs du meme type) ;
///   3. les dates et les cases que RIG v2 laisse saisir sur CETTE IP (Modifiable de la fiche, ZonesModifiables) ; une seule date a
///      la fois (le 2e executoire compte comme une date : il est grise des qu'une date est saisie) ;
///   4. les controles de saisie, au mot pres (VerifierSaisie) ; « Date incorrect » pour le desistement (:1764-1767) ; l'OP de
///      caducite exige son juge (Juge.GetJuge(Convert.ToInt32(ult_Juge.Valeur)), :2070) et son code affaire IPCADSO, l'enrolement
///      au fond le code affaire TIP1 (codeAffaireInsJ, :95) et son APPEL A L'AUDIENCE, obligatoire (AppelAuFondAsync : une audience
///      existante du greffe, a venir, designee par sa date, son heure et sa chambre ; le type de classement, de TIP1 par defaut) ;
///      l'audience saisie hors enrolement au fond est refusee (RIG v2 ne la montre qu'avec « Enrôlement » cochee, :3211-3228).
///
/// CE QUI S'ECRIT (loi « nouvelle » = decision acceptee rendue a partir du 01/03/2022, DecisionJudiciaireExtentions.cs:13, :29-34) :
///   Desistement   : l'evenement IPDESIN SURVENU a la date (:1768-1770) ; la note « Information » completee de « Désistement »
///                   (:3307-3310) ;
///   Signification : l'evenement attendu IPSIGNIF (nouvelle loi) ou ISIGNI SURVIENT a la date, « à personne » pose (cree s'il
///                   n'est pas attendu, :2795-2803) ; son evenement suivant est cree (IPCERTNOPP, IOIP...) sauf si une opposition
///                   ou un certificat est deja survenu ou un certificat deja attendu (:2809-2822) ;
///                   ANCIENNE LOI, APRES SA DATE MAXI, sans opposition : l'IP est NON AVENUE (:1836-1873) -- la signification attendue
///                   EXPIRE ; avec le reglage IP_NONAVENUE_EVT_SI_PAS_ENROLER_OP, l'evenement du reglage (+ « LR » avec « Envoi en
///                   LRAR », + « R » avec « Envoi en LR ») survient du jour, lie a elle (_CreerEvenementPourEdition, :2176-2190) ;
///                   sans reglage, son code « expire » (IPNAV ; + « LR » s'il existe et qu'un envoi est coche, :1856-1862). « Enrôler
///                   une OP » n'est jamais montre pour une signification (seule la provision le rend visible, :3065) : pas d'OP ici.
///                   NB : dans RIG v2 tel qu'il est, le gestionnaire de saisie (:2790-2819) fait deja survenir la signification
///                   attendue, et ce traitement (:1820-1822) dereference alors un futur introuvable (NullReferenceException) ; la
///                   V3 applique la branche ecrite, dans les delais comme hors delai ;
///   Certificat    : l'evenement IPCERTNOPP attendu SURVIENT (ou est cree survenu) ; l'opposition IPOPOS attendue EXPIRE
///                   (:1887-1910) ;
///   Opposition    : l'evenement attendu IPOPOS (nouvelle loi) ou IOIP SURVIENT avec son suivant (IPAROPOS) ; le certificat attendu
///                   EXPIRE (:2894-2925, :1962-1970). Apres la date maxi de l'ancienne loi, RIG v2 cherche l'opposition FUTURE a la
///                   date saisie pour la faire expirer (:1926-1940) : le gestionnaire de saisie l'a deja fait survenir (:2908-2925),
///                   la recherche ne trouve rien et l'opposition est enregistree comme dans les delais -- la V3 fait de meme ;
///   Reception LRAR: l'evenement attendu IAROIP / IPAROPOS SURVIENT, avec son suivant (la provision attendue) si aucune provision
///                   n'est survenue (:1981-2024) ;
///   Provision     : DANS LES DELAIS (au plus la date prevue de la provision attendue, controle_date :2658-2668 ; ou « Dans les
///                   délais » cochee, :2031) -- sans juridiction exterieure et « Enrôlement » cochee, l'opposition est ENROLEE AU FOND
///                   (:2033-2052, l'instance preparee :3097-3135) : une affaire et sa demande OIPE validee (nom, indicatif, numeros
///                   « role J role IP », lien 2J, :2497-2504) ; l'instance du code affaire TIP1 (nature de l'IP, nature particuliere et
///                   acte du code, acte a la date d'opposition sinon du jour, saisine et enrolement du jour, role de la serie du code
///                   et repertoire JGAFF, :1556-1561), jointe a l'IP ; ses chefs de demande et ses parties dupliques (demandeurs
///                   « DOIP », defendeurs « FOIP ») ; son appel n° 1 a l'audience saisie (section « Audience », AppelAuFondAsync) ;
///                   ses evenements de creation survenus du jour, et leurs editions sur la demande OIPE. Avec une juridiction exterieure, la note recoit « Dossier à renvoyer à la nouvelle juridiction » (:3086-3092).
///                   Puis la provision attendue (IPCONSOPOS / ICONSOIP) SURVIENT a la date (creee si absente), sans suivant, et ses
///                   editions ; forcee, la note recoit « Date de provision forcée, malgré Caducité » (:3230-3232) ;
///                   HORS DELAI : CADUCITE, la note recoit « Caducité » (:3054-3056) -- avec le reglage
///                   IP_CADUCITE_EVT_SI_PAS_ENROLER_OP, la provision attendue expire et l'evenement du reglage (+ « LR » / + « R »)
///                   survient (:2082-2090) ; sans reglage et « Enrôler une OP » cochee, l'OP « IPCADSO » est ouverte sur l'IP (juge,
///                   greffier, date prevue : CreateInstanceOPSurIP, :2060-2078) et la provision expire avec son code « expire »
///                   (IPCAD) ; decochee, elle expire seulement avec son code « expire » (:2094-2097) ;
///   2e executoire : « 2ème exécut. » cochee, l'evenement IPEXECU2 (IPEXECU2LR avec « Envoi en LRAR ») survient du jour, avec ses
///                   editions (:2101-2111). RIG v2 met ce traitement deux fois dans sa liste (:205-208 et :220-223) et cree donc deux
///                   evenements : la V3 n'en cree qu'un ;
///   et toujours : les EDITIONS de l'evenement survenu (CreateDemandeEdition ; EditionsEvenement.cs) et la v2.DEMANDE « IPSUIVI »
///   validee, liee a l'IP (2J). Un evenement suivant porte le numero source de celui qui l'a cree (EVNPR_NUMERO_SOURCE_EP_LIE) et
///   les dates du code (mois, jours ou semaines de CODE_EVE_PRO apres la date survenue).
///
/// NON REPRIS (le message du geste le dit) : la facture (bouton FACTURATION, facultatif, :489-494) et l'encaissement de la provision
/// (EncaissementProvision) ; le « Commentaire pour l'audience » de l'appel (APPAF_NOTE_AUDIENCE, facultatif) ; la creation d'une
/// audience absente (PROC_AUDIENCE) ; les mots directeurs de l'instance au fond (:1570-1576) ; les mots directeurs de l'OP ; la reedition de l'ordonnance en titre executoire (TrtTitreExecu, :2192-2230 : le
/// fichier de la GED, service externe) ; les pieces scannees et le code-barre ; le tableau recapitulatif (:2520-2600). Ce que les
/// editions ne reprennent pas est dit par EditionsEvenement.
/// </summary>
internal sealed class EcrivainSuiviIp : IEcrivainEcran
{
    public string Procedure => "PROC_IPSUIVI";
    public string Ecran => "Suivi des préventions";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Enregistrer];

    /// <summary>« Date incorrect » : l'erreur de la date de desistement (OPE_IPSUIVI.cs:1766), au mot pres.</summary>
    internal const string DateIncorrecte = "Date incorrect";

    /// <summary>L'alerte de Remplir_IP sans evenement futur (OPE_IPSUIVI.cs:1031), au mot pres.</summary>
    internal const string AucunFutur = "Aucun évènement futur, traitement impossible en IPSUIVI vous devez aller en MJUD.";

    private const string UneSeuleDate =
        "Une seule date par validation : RIG v2 grise les autres dès qu'une date est saisie (OPE_IPSUIVI.cs:1196-1250), sauf la signification avec le certificat de non-opposition (:101-103).";

    private static readonly string[] Dates = [ChampSignification, ChampOpposition, ChampAr, ChampProvision, ChampCertificat, ChampDesistement];

    private static bool Coche(string v) => v.ToLowerInvariant() is "oui" or "true" or "1";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. L'IP et le verrou.
        if (geste.Demande.Ids is not [var id] || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            return IssueGeste.Refuser($"Injonction de payer introuvable dans la base du greffe ({string.Join(", ", geste.Demande.Ids)}).");
        var colIns = await RelancesRsurv.ColonnesAsync(geste, "v2.INSTANCE", ct);
        var colEve = await RelancesRsurv.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        EtatIp? etat = null;
        await using (var r = await geste.LireAsync(RequeteEtat(colIns, verrouiller: true), ct, new SqlParameter("@id", n)))
            if (await r.ReadAsync(ct)) etat = EtatDe(r);
        if (etat is null) return IssueGeste.Refuser($"Injonction de payer introuvable dans la base du greffe ({id}).");
        etat = etat with { Exterieure = await EditionsEvenement.OppositionExterieureAsync(geste, n, ct) };
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct) is { } verrou)
            return IssueGeste.Refuser(verrou);

        // Les saisies : les champs des actes et des options, par libelle (et la section de chacun, pour les messages).
        var saisies = new Dictionary<string, string>(StringComparer.Ordinal);
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cle, valeur) in geste.Demande.Valeurs)
        {
            var (section, champ) = ChampsSaisis.FirstOrDefault(c => GesteEcran.Cle(SectionActes, c) == cle) is { } a ? (SectionActes, a)
                                 : ChampsOptions.FirstOrDefault(c => GesteEcran.Cle(SectionOptions, c) == cle) is { } o ? (SectionOptions, o)
                                 : ChampsAudience.FirstOrDefault(c => GesteEcran.Cle(SectionAudience, c) == cle) is { } au ? (SectionAudience, au)
                                 : ("", (string?)null);
            if (champ is null) return IssueGeste.Refuser($"« {cle} » n'est pas une date de la fiche que RIG v2 laisse saisir.");
            saisies[champ] = valeur.Trim();
            sections[champ] = section;
        }
        string Saisie(string champ) => saisies.TryGetValue(champ, out var v) ? v : "";
        var executoire2 = Coche(Saisie(ChampExecutoire2));
        var dates = saisies.Keys.Where(c => Dates.Contains(c)).ToList();
        if (dates.Count == 0 && !executoire2)
            return IssueGeste.Refuser(saisies.ContainsKey(ChampAPersonne)
                ? $"« {ChampAPersonne} » se saisit avec la date de signification (OPE_IPSUIVI.cs:2861-2864)."
                : "Aucune date saisie.");
        foreach (var c in dates)
            if (saisies[c].Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire($"{SectionActes} › {c}"));

        // 2. Les alertes de Remplir_IP.
        if (etat.Desiste) return IssueGeste.Refuser("Désistement IP, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (etat.Europeenne) return IssueGeste.Refuser("Injonction de payer européenne, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (!etat.Accepte) return IssueGeste.Refuser("IP rejetée, traitement impossible en IPSUIVI vous devez aller en MJUD.");
        if (etat.DateDecision is null && (executoire2 || dates.Any(c => c != ChampDesistement)))
            return IssueGeste.Refuser("Date de la décision vide, vous devez aller en ORDOSIGN.");
        if (etat.DateDecision is not null)
        {
            if (etat.OppositionEnrolee is { } tip1) return IssueGeste.Refuser($"Opposition à injonction de payer enrôlée sous le numéro : {tip1}.");
            if (etat.NbFuturs == 0) return IssueGeste.Refuser(AucunFutur);
            if (etat.Fin) return IssueGeste.Refuser("L'IP est close (événement de fin survenu) : RIG v2 grise toutes ses dates (OPE_IPSUIVI.cs:1112-1114).");
            if (Anomalies(etat) is { Count: > 0 } anomalies)
                return IssueGeste.Refuser($"Vous devez corriger {(anomalies.Count > 1 ? "les anomalies" : "l'anomalie")} ci-dessous en MJUD : "
                                          + Environment.NewLine + string.Join(Environment.NewLine, anomalies));
        }

        // 3. Les dates et les cases que RIG v2 laisse saisir ; une date a la fois.
        var modifiables = ZonesModifiables(etat);
        foreach (var c in saisies.Keys)
            if (!modifiables.Contains(c))
                return IssueGeste.Refuser($"« {sections[c]} › {c} » n'est pas modifiable sur cette IP : RIG v2 la grise (OPE_IPSUIVI.cs:976-1120).");
        var traitements = dates.Count + (executoire2 ? 1 : 0);
        if (traitements > 1 && !(dates.Count == 2 && !executoire2 && dates.Contains(ChampSignification) && dates.Contains(ChampCertificat) && etat.NouvelleLoi))
            return IssueGeste.Refuser(UneSeuleDate);

        // 4. Les controles de saisie.
        DateTime? Lue(string champ) => saisies.TryGetValue(champ, out var v) ? AudiencesV2.Date(v) : null;
        foreach (var c in dates)
            if (Lue(c) is null) return IssueGeste.Refuser(c == ChampDesistement ? DateIncorrecte : $"« {SectionActes} › {c} » : date illisible ({saisies[c]}).");
        var dateIp = etat.DateIp?.Date;
        var signification = Lue(ChampSignification) ?? etat.Signification;
        var opposition = Lue(ChampOpposition) ?? etat.Opposition;
        var lrar = Coche(Saisie(ChampLrar));
        var lr = !lrar && Coche(Saisie(ChampLr));
        if (Lue(ChampDesistement) is { } desistement && dateIp is { } dip0 && desistement < dip0) return IssueGeste.Refuser(DateIncorrecte);
        DateTime? nonAvenueApres = null;
        if (Lue(ChampSignification) is { } s)
        {
            if (dateIp is { } dip && s < dip) return IssueGeste.Refuser("La date de signification ne peut pas être inférieure à la date de l'IP");
            if (opposition is { } o && s > o) return IssueGeste.Refuser("La date de signification ne peut pas être supérieure à la date d'opposition");
            // _DateSaisieSuperieurDatemaxi(ult_Signification) d'une IP de l'ancienne loi sans opposition (:1823-1836).
            if (!etat.NouvelleLoi && !etat.OppositionSurvenue && etat.Opposition is null && etat.MaxiSignification is { } maxi && s > maxi) nonAvenueApres = maxi;
        }
        if (Lue(ChampCertificat) is { } cert && dateIp is { } dip2 && cert < dip2)
            return IssueGeste.Refuser("La date du certificat de non opposition ne peut pas être inférieure à la date de l'IP");
        if (Lue(ChampOpposition) is { } op)
        {
            if (dateIp is { } dip && op < dip) return IssueGeste.Refuser("La date de l'opposition ne peut pas être inférieure à la date de l'IP");
            if (signification is { } sg && op < sg) return IssueGeste.Refuser("La date de l'opposition ne peut pas être inférieure à la date de la signification");
        }
        if (Lue(ChampAr) is { } ar)
        {
            if (dateIp is { } dip && ar < dip) return IssueGeste.Refuser("La date de réception du LRAR ne peut pas être inférieur à la date de l'IP");
            if (opposition is { } o && ar < o) return IssueGeste.Refuser("La date de réception du LRAR ne peut pas être inférieur à la date de l'opposition");
            if (etat.Provision is { } pv && ar > pv) return IssueGeste.Refuser("La date de provision ne peut pas être supérieur à la date de réception du LRAR");
        }

        // La provision : dans les delais ou caduque, et ce qu'il faut pour l'enrolement au fond ou l'OP de caducite (lu AVANT d'ecrire).
        var provision = Lue(ChampProvision);
        var horsDelai = false;
        CodeTip1? codeJ = null;
        AppelAuFond? appelJ = null;
        EcrivainSurveillanceIp.CodeOp? codeOp = null;
        EcrivainSurveillanceIp.Juge? juge = null;
        int? idGreffier = null;
        DateTime? datePrevue = null;
        var reglages = await EcrivainSurveillanceIp.ReglagesAsync(geste, ct);
        if (provision is { } dpv)
        {
            if (dateIp is { } dip && dpv < dip) return IssueGeste.Refuser("La date de la provision ne peut pas être inférieure à la date de l'IP");
            if (etat.ReceptionAr is { } ar0 && dpv < ar0) return IssueGeste.Refuser("La date de la provision ne peut pas être inférieure à la date de réception du LRAR");
            horsDelai = etat.MaxiProvision is { } mp && dpv > mp && !Coche(Saisie(ChampDansLesDelais));
            // fix-ok: mesure le 27/11/2026 (controle final du lot 8) -- la provision etait refusee « pas encore » alors que RIG v2
            // l'enregistre (:2028-2100) ; enrolement au fond, caducite, non-avenue et 2e executoire s'ecrivent desormais ici.
            var enrolerJ = !horsDelai && !etat.Exterieure && (!saisies.ContainsKey(ChampEnrolement) || Coche(Saisie(ChampEnrolement)));
            if (enrolerJ)
            {
                codeJ = await CodeTip1Async(geste, ct);
                if (codeJ is null)
                    return IssueGeste.Refuser("Le code affaire « TIP1 » est absent ou incomplet dans la base du greffe (type de numéro de rôle, type d'instance) : RIG v2 y enrôle l'opposition au fond (OPE_IPSUIVI.cs:95, :3098). Rien n'est enregistré.");
                // L'appel a l'audience de l'instance au fond, obligatoire (en tete du fichier).
                var (appel, refusAppel) = await AppelAuFondAsync(geste, Saisie, codeJ, ct);
                if (appel is null) return IssueGeste.Refuser(refusAppel!);
                appelJ = appel;
            }
            if (horsDelai && reglages.Caducite is null && (!saisies.ContainsKey(ChampEnrolerOp) || Coche(Saisie(ChampEnrolerOp))))
            {
                if (Saisie(ChampJuge).Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire($"{SectionOptions} › {ChampJuge}"));
                var idJuge = await InjonctionsIsurv.PersonneAsync(geste, "v2.JUGE", "JUG_ID_JUG", "JUG_ABREGE", "JUG_ACTIF", Saisie(ChampJuge), ct);
                if (idJuge is null)
                    return IssueGeste.Refuser($"Le juge « {Saisie(ChampJuge)} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");
                if (Saisie(ChampGreffier) is { Length: > 0 } abregeGreffier)
                {
                    idGreffier = await InjonctionsIsurv.PersonneAsync(geste, "v2.GREFFIER_ASSERMENTE", "GRASS_ID_GRASS", "GRASS_ABREGE", "GRASS_ACTIF", abregeGreffier, ct);
                    if (idGreffier is null)
                        return IssueGeste.Refuser($"Le greffier « {abregeGreffier} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");
                }
                if (Saisie(ChampDatePrevue) is { Length: > 0 } dp)
                {
                    if (AudiencesV2.Date(dp) is not { } d) return IssueGeste.Refuser($"« {SectionOptions} › {ChampDatePrevue} » : date illisible ({dp}).");
                    datePrevue = d;
                }
                codeOp = await EcrivainSurveillanceIp.CodeOpAsync(geste, "IPCADSO", ct);
                juge = await EcrivainSurveillanceIp.JugeAsync(geste, idJuge.Value, ct);
                // Le message enveloppe de RIG v2 quand l'OP ne peut pas etre creee (Instance.cs:3527).
                if (codeOp is null || juge is null) return IssueGeste.Refuser($"Problème à la création de l'OP pour l'instance {etat.Role}.");
            }
        }
        // Hors enrolement au fond (pas de provision, provision hors delai, « Enrôlement » decochee), RIG v2 cache l'audience.
        if (appelJ is null && saisies.Keys.FirstOrDefault(c => sections[c] == SectionAudience) is { } horsEnrolement)
            return IssueGeste.Refuser($"« {SectionAudience} › {horsEnrolement} » se saisit avec l'enrôlement au fond d'une provision reçue dans les délais : RIG v2 ne montre l'audience qu'avec « {ChampEnrolement} » cochée (OPE_IPSUIVI.cs:3211-3228).");
        var codeExecutoire2 = lrar ? "IPEXECU2LR" : "IPEXECU2";
        if (executoire2 && await RelancesRsurv.Scalaire(geste, "SELECT R_CODEEVEPRO_VALEUR FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                                                         new SqlParameter("@c", codeExecutoire2)) is null or DBNull)
            return IssueGeste.Refuser($"L'événement « {codeExecutoire2} » est absent des codes d'événement de la base du greffe : RIG v2 le crée pour le 2e exécutoire (OPE_IPSUIVI.cs:2101-2108). Rien n'est enregistré.");

        // Les ecritures : la demande d'abord (les editions s'y rattachent), puis chaque date dans l'ordre de _TypeTraitement.
        var (idDemande, numeroDemande, refusDemande) = await EcrivainSurveillanceIp.DemandeInstanceAsync(geste, "IPSUIVI", n, etat.Affaire,
                                                                                                          etat.NomCourant ?? etat.Role, etat.Role, ct);
        if (idDemande is null) return IssueGeste.Refuser(refusDemande!);
        var faits = new List<string>();
        var bilans = new List<EditionsEvenement.Bilan>();
        var traces = new List<TraceGeste>();
        var nonRepris = new List<string> { "le tableau récapitulatif" };
        var apres = new Dictionary<string, string?> { ["Demande"] = numeroDemande };
        var note = etat.Note ?? "";
        void Noter(string texte) { if (!note.Contains(texte, StringComparison.Ordinal)) note = (note + " " + texte).Trim(); }
        async Task Editions(int idEvenement) => bilans.Add(await EditionsEvenement.CreerAsync(geste, idDemande.Value, numeroDemande, idEvenement, ct));
        var nom = etat.NomCourant ?? etat.Role;

        if (Lue(ChampDesistement) is { } dd)
        {
            var idEve = (await EvenementsProcedureV2.SurvenuAsync(geste, n, "IPDESIN", dd, avecFutur: false, ct)).IdSurvenu;
            await Editions(idEve);
            Noter("Désistement");
            faits.Add($"désistement du {FormeLigne.Date(dd)} (événement IPDESIN)");
            apres["Desistement"] = FormeLigne.Date(dd);
        }
        if (Lue(ChampSignification) is { } ds && nonAvenueApres is { } maxiSignif)
        {
            // Non avenue (:1836-1873) : la signification attendue expire, avec l'evenement du reglage ou son code « expire ».
            var futur = await FuturAsync(geste, n, "'ISIGNI','IPSIGNIF'", ct);
            if (futur is null) return IssueGeste.Refuser($"L'évènement futur n'existe plus. Impossible de traiter l'instance {etat.Role}. Problème de récupération");
            string? code;
            if (reglages.NonAvenue is { } rg) code = lrar ? rg.Lrar : lr ? rg.Lr : rg.Code;
            else
            {
                code = await CodeExpireAsync(geste, futur.Code, ct);
                if (code is not null && (lrar || lr) && await EcrivainSurveillanceIp.SuffixeAsync(geste, code, "LR", ct) is var avecLr && avecLr != code) code = avecLr;
            }
            var (survenu, idSurvenu) = await EcrivainSurveillanceIp.ExpirerAsync(geste, Echeance(futur, n, etat, nom), code is { Length: > 0 } ? code : null, colEve, ct);
            if (idSurvenu is { } idS) await Editions(idS);
            faits.Add($"signification du {FormeLigne.Date(ds)} après sa date maxi ({FormeLigne.Date(maxiSignif)}) : IP non avenue ({futur.Code} expiré{(survenu is null ? "" : $", événement {survenu}")})");
            apres["NonAvenue"] = survenu ?? $"{futur.Code} expiré";
        }
        else if (Lue(ChampSignification) is { } ds2)
        {
            var code = etat.NouvelleLoi ? "IPSIGNIF" : "ISIGNI";
            var aPersonne = Coche(Saisie(ChampAPersonne));
            // Le suivant (le certificat attendu) se cree meme avec le certificat saisi : RIG v2 le cree a la saisie de la signification
            // (:2809-2822), puis le traitement du certificat le fait survenir (:1891-1896).
            var lie = !etat.OppositionSurvenue && etat.Certificat is null && !etat.FuturCertificat;
            var (idEve, suivant) = await SurvenirAsync(geste, n, code, ds2, aPersonne, lie, colEve, ct);
            await Editions(idEve);
            faits.Add($"signification du {FormeLigne.Date(ds2)}{(aPersonne ? " à personne" : "")} (événement {code}{(suivant is null ? "" : $", {suivant} attendu")})");
            apres["Signification"] = FormeLigne.Date(ds2);
        }
        if (Lue(ChampCertificat) is { } dc)
        {
            var (idEve, _) = await SurvenirAsync(geste, n, "IPCERTNOPP", dc, null, false, colEve, ct);
            await Editions(idEve);
            await ExpirerFutursAsync(geste, n, "IPOPOS", colEve, ct);
            faits.Add($"certificat de non-opposition du {FormeLigne.Date(dc)} (événement IPCERTNOPP)");
            apres["Certificat"] = FormeLigne.Date(dc);
        }
        if (Lue(ChampOpposition) is { } dop)
        {
            var code = etat.NouvelleLoi ? "IPOPOS" : "IOIP";
            var (idEve, suivant) = await SurvenirAsync(geste, n, code, dop, null, true, colEve, ct);
            await Editions(idEve);
            await ExpirerFutursAsync(geste, n, "IPCERTNOPP", colEve, ct);
            faits.Add($"opposition du {FormeLigne.Date(dop)} (événement {code}{(suivant is null ? "" : $", {suivant} attendu")})");
            apres["Opposition"] = FormeLigne.Date(dop);
        }
        if (Lue(ChampAr) is { } dar)
        {
            var code = etat.CodeFuturAr ?? (etat.NouvelleLoi ? "IPAROPOS" : "IAROIP");
            var (idEve, suivant) = await SurvenirAsync(geste, n, code, dar, null, etat.Provision is null, colEve, ct);
            await Editions(idEve);
            faits.Add($"réception de la LRAR du {FormeLigne.Date(dar)} (événement {code}{(suivant is null ? "" : $", {suivant} attendu")})");
            apres["ReceptionLrar"] = FormeLigne.Date(dar);
        }
        if (provision is { } dprov && !horsDelai)
        {
            // Dans les delais (:2031-2058) : l'enrolement au fond, puis la provision attendue survient.
            string? enrolee = null;
            if (codeJ is not null)
            {
                var (roleJ, refusJ) = await EnrolerAuFondAsync(geste, n, etat, codeJ, appelJ!, bilans, traces, ct);
                if (roleJ is null) return IssueGeste.Refuser(refusJ!);
                enrolee = roleJ;
                apres["InstanceAuFond"] = roleJ;
                apres["Audience"] = appelJ!.Libelle;
                nonRepris.Add("la facture et l'encaissement de la provision, le commentaire de l'appel et les mots directeurs de l'instance au fond");
            }
            else if (etat.Exterieure) Noter("Dossier à renvoyer à la nouvelle juridiction");
            var code = etat.CodeFuturConso ?? "IPCONSOPOS";
            var (idEve, _) = await SurvenirAsync(geste, n, code, dprov, null, false, colEve, ct);
            await Editions(idEve);
            if (etat.MaxiProvision is { } mp && dprov > mp) Noter("Date de provision forcée, malgré Caducité");
            faits.Add($"provision du {FormeLigne.Date(dprov)} (événement {code}{(enrolee is null ? "" : $" ; opposition enrôlée au fond sous le n° {enrolee}, code affaire TIP1, appelée à l'{appelJ!.Libelle}{(appelJ.HeureAppel is { } ha ? $", appel à {ha}" : "")}, classement {appelJ.TypeClassement}")})");
            apres["Provision"] = FormeLigne.Date(dprov);
        }
        else if (provision is { } dcad)
        {
            // Hors delai (:2059-2099) : caducite.
            Noter("Caducité");
            var futur = await FuturAsync(geste, n, "'ICONSOIP','IPCONSOPOS'", ct);
            if (futur is null) return IssueGeste.Refuser($"L'évènement futur n'existe plus. Impossible de traiter l'instance {etat.Role}. Problème de récupération");
            string? code;
            string? opRole = null, ordonnance = null;
            if (reglages.Caducite is { } rg) code = lrar ? rg.Lrar : lr ? rg.Lr : rg.Code;
            else
            {
                if (codeOp is not null)
                {
                    var (creee, refusOp) = await EcrivainSurveillanceIp.CreerOpAsync(geste, Echeance(futur, n, etat, nom), codeOp, juge!, idGreffier, datePrevue, bilans, ct);
                    if (creee is null) return IssueGeste.Refuser(refusOp!);
                    (opRole, ordonnance) = (creee.Value.Role, creee.Value.Ordonnance);
                    // Les numeros de sa demande OPE : « role OP role IP » (MajListeDemande, :2508-2515).
                    await geste.EcrireAsync("""
                        UPDATE v2.DEMANDE SET DMND_LES_NUMEROS = LEFT(@n, 128)
                        WHERE DMND_CODE_PROSS = 'OPE' AND DMND_ID_DMND IN (SELECT DSSDM_ID_DMND FROM v2.DOSSIER_DEMANDE
                                                                           WHERE DSSDM_CODE_SEQUENCEUR = '2J' AND DSSDM_VALEUR_SEQUENCEUR = @op)
                        """, ct, new SqlParameter("@n", $"{creee.Value.Role} {etat.Role}"),
                        new SqlParameter("@op", creee.Value.Id.ToString(CultureInfo.InvariantCulture)));
                    nonRepris.Add("les mots directeurs de l'opposition");
                    apres["Opposition"] = opRole;
                }
                code = await CodeExpireAsync(geste, futur.Code, ct);
            }
            var (survenu, idSurvenu) = await EcrivainSurveillanceIp.ExpirerAsync(geste, Echeance(futur, n, etat, nom), code is { Length: > 0 } ? code : null, colEve, ct);
            if (idSurvenu is { } idS) await Editions(idS);
            faits.Add($"provision du {FormeLigne.Date(dcad)} après sa date prévue ({FormeLigne.Date(etat.MaxiProvision!.Value)}) : caducité ({futur.Code} expiré{(survenu is null ? "" : $", événement {survenu}")}{(opRole is null ? "" : $", opposition {opRole} enrôlée")})");
            if (ordonnance is not null) faits.Add($"ordonnance « {ordonnance} » en file d'édition");
            apres["Caducite"] = survenu ?? $"{futur.Code} expiré";
        }
        if (executoire2)
        {
            // Le 2e executoire (:2101-2111), une fois.
            var idEve = (await EvenementsProcedureV2.SurvenuAsync(geste, n, codeExecutoire2, geste.Maintenant.Date, avecFutur: false, ct)).IdSurvenu;
            await Editions(idEve);
            faits.Add($"2e exécutoire (événement {codeExecutoire2})");
            nonRepris.Add("la réédition de l'ordonnance en titre exécutoire (fichier de la GED)");
            apres["Executoire2"] = codeExecutoire2;
        }

        // La note « Information » (ult_Information, enregistree dans INSTN_NOTE_AUDIENCE, :2150).
        if (colIns.Contains("INSTN_NOTE_AUDIENCE") && note != (etat.Note ?? ""))
        {
            await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_NOTE_AUDIENCE = @note WHERE INSTN_ID_INSTN = @id", ct,
                new SqlParameter("@note", note), new SqlParameter("@id", n));
            apres["Information"] = note;
        }

        var message = $"Enregistré sur l'IP {etat.Role} : {string.Join(" ; ", faits)}.{EditionsEvenement.Message(bilans)} Non repris par la V3 : {string.Join(", ", nonRepris)}.";
        return IssueGeste.Ecrit(message, [id],
            [new TraceGeste("affaire", $"l'injonction de payer {etat.Role}", id, "a modifie",
                new Dictionary<string, string?> { ["Information"] = etat.Note }, apres), .. traces]);
    }

    /// <summary>Les anomalies de _VerifierCoherenceIP (OPE_IPSUIVI.cs:1149-1176), au mot pres.</summary>
    private static List<string> Anomalies(EtatIp e)
    {
        var messages = new List<string>();
        if (e.NouvelleLoiDecision && e.NbFuturIsigni > 0) messages.Add("- Vous avez des évènements futurs 'ISIGNI' au lieu de 'IPSIGNIF'");
        if (!e.NouvelleLoiDecision && e.NbFuturIpsignif > 0) messages.Add("- Vous avez des évènements futurs 'IPSIGNIF' au lieu de 'ISIGNI'");
        if (e.NouvelleLoiDecision && e.NbFuturIoip > 0) messages.Add("- Vous avez des évènements futurs 'IOIP' au lieu de 'IPOPOS'");
        if (!e.NouvelleLoiDecision && e.NbFuturIpopos > 0) messages.Add("- Vous avez des évènements futurs 'IPOPOS' au lieu de 'IOIP'");
        if (e.NbFuturIsigni + e.NbFuturIpsignif > 1) messages.Add("- Vous avez plusieurs évènements futurs de signification ('ISIGNI','IPSIGNIF')");
        if (e.NbFuturIoip + e.NbFuturIpopos > 1) messages.Add("- Vous avez plusieurs évènements futurs d'opposition ('IOIP','IPOPOS')");
        if (e.NbFuturAr > 1) messages.Add("- Vous avez plusieurs évènements futurs de LRAR provision ('IAROIP','IPAROPOS')");
        if (e.NbFuturConso > 1) messages.Add("- Vous avez plusieurs évènements futurs de provision ('ICONSOIP','IPCONSOPOS')");
        if (e.NbFuturCertif > 1) messages.Add("- Vous avez plusieurs évènements futurs de certificat de non opposition ('IPCERTNOPP')");
        return messages;
    }

    // ------------------------------------------------------------------ L'ENROLEMENT AU FOND (TIP1)

    /// <summary>Le code affaire de l'instance au fond (codeAffaireInsJ = CODE_AFFAIRE « TIP1 », OPE_IPSUIVI.cs:95) : ce que
    /// Instance.CreateInstance et la numerotation lisent (:3098-3107, :1556-1561). Null s'il manque, sans serie ou sans type.</summary>
    internal sealed record CodeTip1(string TypeNumRole, string TypeInstance, bool Repertoire, string? NatAffPart, string? Acte, string? Creation,
                                    string? TypeClassement);

    private static async Task<CodeTip1?> CodeTip1Async(GesteEnCours geste, CancellationToken ct)
    {
        var col = await RelancesRsurv.ColonnesAsync(geste, "v2.CODE_AFFAIRE", ct);
        if (!col.Contains("R_CODEAFFAIR_TYPE_NUM_ROLE") || !col.Contains("R_CODEAFFAIR_TYPE_INSTANCE")) return null;
        string C(string c) => col.Contains(c) ? c : $"CAST(NULL AS varchar(255)) AS {c}";
        await using var r = await geste.LireAsync($"""
            SELECT R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE, {C("R_CODEAFFAIR_NUM_REPERTOIRE")}, {C("R_CODEAFFAIR_NAT_AFF_PART")},
                   {C("R_CODEAFFAIR_CODE_ACTE_PRO")}, {C("R_CODEAFFAIR_CODE_EVE_PRO_CREA")}, {C("R_CODEAFFAIR_TYPE_CLASSEMENT")}
            FROM v2.CODE_AFFAIRE WHERE R_CODEAFFAIR_VALEUR = 'TIP1'
            """, ct);
        if (!await r.ReadAsync(ct)) return null;
        string? O(string c) => FormeLigne.Texte(r, c).Trim() is { Length: > 0 } s ? s : null;
        if (O("R_CODEAFFAIR_TYPE_NUM_ROLE") is not { } num || O("R_CODEAFFAIR_TYPE_INSTANCE") is not { } type) return null;
        return new CodeTip1(num, type, r["R_CODEAFFAIR_NUM_REPERTOIRE"] is true or 1 or "1", O("R_CODEAFFAIR_NAT_AFF_PART"),
                            O("R_CODEAFFAIR_CODE_ACTE_PRO"), O("R_CODEAFFAIR_CODE_EVE_PRO_CREA"), O("R_CODEAFFAIR_TYPE_CLASSEMENT"));
    }

    // ------------------------------------------------------------------ L'APPEL A L'AUDIENCE DE L'INSTANCE AU FOND

    /// <summary>L'audience choisie pour l'instance au fond, son type de classement et l'heure d'appel.</summary>
    internal sealed record AppelAuFond(int IdAudience, string Libelle, string TypeClassement, string? HeureAppel);

    /// <summary>
    /// L'appel a l'audience que RIG v2 fait saisir avec l'enrolement au fond (opE_APP_AFFAIRE, OPE_IPSUIVI.cs:3211-3225 : un AppelAffaire
    /// neuf, son type de classement pris du code affaire TIP1 ; rattache a l'instance au fond, devenue le MetierBase du processus,
    /// :3209 et OPE_APP_AFFAIRE.cs:1022-1042, puis enregistre avec elle). OBLIGATOIRE : l'operation est construite par
    /// OPE_APP_AFFAIRE() = this(true) (OPE_APP_AFFAIRE.cs:324-337), et ses champs Date, Heure, Chambre, Type de classement sont
    /// MetierObligatoire (OPE_APP_AFFAIRE_AUDIENCE.Designer.cs:117-169). En IPSUIVI c'est une AUDIENCE, sans choix du cabinet
    /// (OPE_APP_AFFAIRE.cs:185-187). RIG v2 la choisit parmi les audiences du greffe : celles du jour saisi (AUDNC_AUDIENCE = 1, a
    /// l'heure saisie ; GetListAudienceCabinetParDate, AudienceCabinet.cs:802-824), a la chambre saisie (OPE_APP_AFFAIRE_AUDIENCE.cs:243,
    /// :281) ; une seule est prise d'office (:422-430), plusieurs demandent la recherche (:432-439), aucune ouvre la creation d'audience
    /// (PROC_AUDIENCE, :384-420), que la V3 ne reprend pas. Controles : la date d'audience passee (:386-387 sans audience ; sinon
    /// VerifierDates, :660-661 : la saisine de l'instance au fond est du jour). Le type de classement saisi doit etre une valeur de
    /// TYPE_CLASSEMENT (la liste de la combo, :110) ; l'heure d'appel tient en cinq caracteres (APPAF_HEURE_APPEL varchar(5)).
    /// Rend l'appel, ou le refus (rien n'est ecrit).
    /// </summary>
    private static async Task<(AppelAuFond? Appel, string? Refus)> AppelAuFondAsync(GesteEnCours geste, Func<string, string> saisie, CodeTip1 code,
                                                                                     CancellationToken ct)
    {
        string Nom(string champ) => $"{SectionAudience} › {champ}";
        var jour = geste.Maintenant.Date;
        if (saisie(ChampDateAudience).Length == 0)
            return (null, CreationV2.Obligatoire(Nom(ChampDateAudience)) + " RIG v2 rend l'appel à l'audience de l'instance au fond obligatoire (OPE_APP_AFFAIRE.cs:324-337).");
        if (AudiencesV2.Date(saisie(ChampDateAudience)) is not { } date)
            return (null, $"« {Nom(ChampDateAudience)} » : date illisible ({saisie(ChampDateAudience)}).");
        var heure = saisie(ChampHeureAudience) is { Length: > 0 } h ? h : null;
        var chambre = saisie(ChampChambre) is { Length: > 0 } c ? c : null;
        var heureAppel = saisie(ChampHeureAppel) is { Length: > 0 } ha ? ha : null;
        if (heureAppel is { Length: > 5 }) return (null, $"« {Nom(ChampHeureAppel)} » : cinq caractères au plus (« 09:30 »).");
        var classement = saisie(ChampClassement) is { Length: > 0 } tc ? tc : code.TypeClassement ?? "";
        if (classement.Length == 0) return (null, CreationV2.Obligatoire(Nom(ChampClassement)));
        if (await RelancesRsurv.Scalaire(geste, "SELECT R_TYPECLASSE_VALEUR FROM v2.TYPE_CLASSEMENT WHERE R_TYPECLASSE_VALEUR = @t", ct,
                                         new SqlParameter("@t", classement)) is null or DBNull)
            return (null, $"Le type de classement « {classement} » n'est pas dans la base du greffe.");

        var trouvees = new List<(int Id, string Libelle)>();
        await using (var r = await geste.LireAsync($$"""
            SELECT a.AUDNC_ID_ADNC AS Id, {{AudiencesV2.Libelle}} AS Libelle
            FROM v2.AUDIENCE_CABINET AS a {{AudiencesV2.Jointures}}
            WHERE a.AUDNC_DATE >= @jour AND a.AUDNC_DATE < DATEADD(day, 1, @jour) AND a.AUDNC_AUDIENCE = 1
              AND (@heure IS NULL OR a.AUDNC_HEURE = @heure) AND (@chambre IS NULL OR a.AUDNC_CHAMBRE = @chambre)
            ORDER BY a.AUDNC_HEURE, a.AUDNC_ID_ADNC
            """, ct, new SqlParameter("@jour", date), new SqlParameter("@heure", (object?)heure ?? DBNull.Value),
            new SqlParameter("@chambre", (object?)chambre ?? DBNull.Value)))
            while (await r.ReadAsync(ct)) trouvees.Add((Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "Libelle")));
        var criteres = $"le {FormeLigne.Date(date)}{(heure is null ? "" : $" à {heure}")}{(chambre is null ? "" : $" pour la chambre {chambre}")}";
        if (trouvees.Count == 0)
            return (null, date < jour
                ? "La date d'audience renseignée est antérieure à la date du jour. Impossible en création d'instance."
                : $"Aucune audience {criteres} dans la base du greffe : RIG v2 ouvre alors la création d'audience (PROC_AUDIENCE, OPE_APP_AFFAIRE_AUDIENCE.cs:384-420), que la V3 ne reprend pas. Rien n'est enregistré.");
        if (trouvees.Count > 1)
            return (null, $"{trouvees.Count} audiences correspondent au(x) critère(s), veuillez lancer la recherche. Précisez « {Nom(ChampHeureAudience)} » ou « {Nom(ChampChambre)} » ({criteres}).");
        if (date < jour) return (null, "La date de 1ère audience ne peut être antérieure à la date de saisine du tribunal !");
        return (new AppelAuFond(trouvees[0].Id, trouvees[0].Libelle, classement, heureAppel), null);
    }

    /// <summary>Ecrit l'appel n° 1 de l'instance au fond : un AppelAffaire neuf (new AppelAffaire, OPE_IPSUIVI.cs:3214-3221), ses etats
    /// de preparation et de retour a leur valeur par defaut « 0 » (APPEL_AFFAIRE.cs:100, et le DEFAULT ((0)) de RIG_DEV ; l'etat « A »
    /// est celui d'un renvoi, AppelAffaire.CreateAppelAffaire :684). Mesure sur RIG_DEV le 27/11/2026 : les 161 instances TIP1 enrolees
    /// depuis 2024 ont toutes leur appel n° 1 (type ANL, celui de TIP1).</summary>
    private static async Task<TraceGeste> InsererAppelAsync(GesteEnCours geste, int idInstance, string role, AppelAuFond appel, CancellationToken ct)
    {
        var colApp = await RelancesRsurv.ColonnesAsync(geste, "v2.APPEL_AFFAIRE", ct);
        var valeurs = new Dictionary<string, object?>
        {
            ["APPAF_ID_INSTN"] = idInstance, ["APPAF_NUM_ORDRE"] = (short)1, ["APPAF_ID_ADNC"] = appel.IdAudience,
            ["APPAF_TYPE_CLASSEMENT"] = appel.TypeClassement, ["APPAF_HEURE_APPEL"] = appel.HeureAppel,
            ["APPAF_ETAT_PREPARATION"] = "0", ["APPAF_ETAT_RETOUR"] = "0",
        };
        var id = await geste.InsererAsync("v2.APPEL_AFFAIRE", "APPAF_ID_APPAF",
            valeurs.Where(v => v.Value is not null && colApp.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);
        return new TraceGeste("affaire", $"l'appel du rôle {role} à l'{appel.Libelle}", id.ToString(CultureInfo.InvariantCulture), "a cree", null,
                              new { Instance = role, Audience = appel.Libelle, appel.TypeClassement, appel.HeureAppel, NumOrdre = 1 });
    }

    /// <summary>
    /// L'opposition ENROLEE AU FOND (OPE_IPSUIVI.cs:2033-2052, l'instance preparee par ult_Provision_ChangementValeur :3097-3135 et
    /// numerotee a la validation :1556-1561, sa demande validee :2497-2504 et :2606-2608), en tete du fichier. Rend son role, ou le
    /// refus de la numerotation.
    /// </summary>
    private static async Task<(string? Role, string? Refus)> EnrolerAuFondAsync(GesteEnCours geste, int idIp, EtatIp ip, CodeTip1 code,
        AppelAuFond appel, List<EditionsEvenement.Bilan> bilans, List<TraceGeste> traces, CancellationToken ct)
    {
        var jour = geste.Maintenant.Date;
        var (role, refusRole) = await CreationV2.NumeroAsync(geste, code.TypeNumRole, geste.Maintenant.Year, ct);
        if (role is null) return (null, refusRole);
        string? repertoire = null;
        if (code.Repertoire)
        {
            // RIG v2 demande JGAFF sans annee (Numerotation.Generer(.., "JGAFF", 0, ..), :1559) ; la numerotation de la V3 tient ses
            // series par annee : l'annee en cours, comme l'OP (EcrivainSurveillanceIp.CreerOpAsync).
            var (rep, refusRep) = await CreationV2.NumeroAsync(geste, "JGAFF", geste.Maintenant.Year, ct);
            if (rep is null) return (null, refusRep);
            repertoire = rep;
        }
        var colIns = await RelancesRsurv.ColonnesAsync(geste, "v2.INSTANCE", ct);
        var nature = colIns.Contains("INSTN_NATURE_AFFAIRE")
            ? await RelancesRsurv.Scalaire(geste, "SELECT INSTN_NATURE_AFFAIRE FROM v2.INSTANCE WHERE INSTN_ID_INSTN = @i", ct, new SqlParameter("@i", idIp)) as string
            : null;

        // Les parties de l'IP, lues avant d'ecrire : demandeurs (D%) et defendeurs (F%), par ordre (ListePartiesDemandeur / Defendeur).
        var colPrt = await RelancesRsurv.ColonnesAsync(geste, "v2.PARTIE", ct);
        var copiees = EcrivainSurveillanceIp.ColonnesPartie.Where(colPrt.Contains).ToArray();
        var parties = new List<(char Qualite, Dictionary<string, object?> Valeurs)>();
        await using (var r = await geste.LireAsync($"""
            SELECT PRT_QUALIF_PARTIE{string.Concat(copiees.Select(c => ", " + c))} FROM v2.PARTIE
            WHERE PRT_ID_INSTN = @i AND (PRT_QUALIF_PARTIE LIKE 'D%' OR PRT_QUALIF_PARTIE LIKE 'F%') ORDER BY PRT_NUM_ORDRE, PRT_ID_PRT
            """, ct, new SqlParameter("@i", idIp)))
            while (await r.ReadAsync(ct))
            {
                var v = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in copiees) v[c] = r[c] is DBNull ? null : r[c];
                parties.Add((FormeLigne.Texte(r, "PRT_QUALIF_PARTIE")[0], v));
            }
        var nomCourant = CreationV2.NomCourant(parties.FirstOrDefault(p => p.Qualite == 'D').Valeurs?["PRT_DESIGNATION"] as string,
                                               parties.FirstOrDefault(p => p.Qualite == 'F').Valeurs?["PRT_DESIGNATION"] as string);

        // La demande OIPE et son affaire (Affaire.CreateAffaire puis Demande.CreateDemande(false, affaire de la J, ..., "OIPE", "2J"),
        // :3110-3111, :2037-2039), validee a la fin.
        var (demande, refusDemande) = await CreationV2.CreerDemandeAsync(geste, "OIPE", "V", ct);
        if (demande is null) return (null, refusDemande);
        var instance = new Dictionary<string, object?>
        {
            ["INSTN_ID_AFFRE"] = demande.IdAffaire, ["INSTN_CODE_AFFAIRE"] = "TIP1", ["INSTN_NATURE_AFFAIRE"] = nature,
            ["INSTN_NAT_AFF_PART"] = code.NatAffPart, ["INSTN_CODE_ACTE_PROCE"] = code.Acte, ["INSTN_ETAT_INSTANCE"] = "0", ["INSTN_ETAT_INFO"] = "1",
            ["INSTN_DATE_SAISINE_GREFFE"] = jour, ["INSTN_DATE_ENROLEMENT"] = jour, ["INSTN_DATE_ACTE"] = ip.Opposition?.Date ?? jour,
            ["INSTN_TYPE_INSTANCE"] = code.TypeInstance, ["INSTN_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            ["INSTN_NUM_ROLE"] = role, ["INSTN_NUM_REP_GENERAL"] = repertoire, ["INSTN_NOM_COURANT"] = nomCourant,
            ["INSTN_ID_INSTN_INSTANCE_PRINCIPA"] = idIp, ["INSTN_DATE_JONCTION"] = jour,
        };
        var idJ = await CreationV2.InsererAsync(geste, "v2.INSTANCE", "INSTN_ID_INSTN",
            instance.Where(v => colIns.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);

        // Les chefs de demande de l'IP (ChefDemande.DupliquerChefDemande, :3121-3124).
        var colChef = await RelancesRsurv.ColonnesAsync(geste, "v2.CHEF_DEMANDE", ct);
        if (colChef.Contains("CHFDM_ID_INSTN"))
        {
            var autres = colChef.Where(c => c is not ("CHFDM_ID_CHFDM" or "CHFDM_ID_INSTN")).ToArray();
            var chefs = new List<Dictionary<string, object?>>();
            await using (var r = await geste.LireAsync($"SELECT {string.Join(", ", autres)} FROM v2.CHEF_DEMANDE WHERE CHFDM_ID_INSTN = @i ORDER BY CHFDM_ID_CHFDM",
                                                       ct, new SqlParameter("@i", idIp)))
                while (await r.ReadAsync(ct))
                    chefs.Add(autres.ToDictionary(c => c, c => r[c] is DBNull ? null : r[c], StringComparer.OrdinalIgnoreCase));
            foreach (var chef in chefs)
            {
                chef["CHFDM_ID_INSTN"] = idJ;
                await CreationV2.InsererAsync(geste, "v2.CHEF_DEMANDE", "CHFDM_ID_CHFDM", chef.Where(c => c.Value is not null).ToDictionary(c => c.Key, c => c.Value), ct);
            }
        }

        // Les parties dupliquees (Partie.DupliquerPartie, numero source neuf, Partie.cs:1412) : « DOIP », « FOIP » (:3126-3136).
        foreach (var (qualite, valeurs) in parties)
        {
            var v = new Dictionary<string, object?>(valeurs, StringComparer.OrdinalIgnoreCase)
            {
                ["PRT_ID_INSTN"] = idJ, ["PRT_QUALIF_PARTIE"] = qualite == 'D' ? "DOIP" : "FOIP",
            };
            if (colPrt.Contains("PRT_NUMERO_SOURCE")) v["PRT_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant();
            await CreationV2.InsererAsync(geste, "v2.PARTIE", "PRT_ID_PRT", v.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value), ct);
        }

        // L'appel a l'audience (en tete de AppelAuFondAsync) : rattache a l'instance au fond, enregistre avec elle.
        traces.Add(await InsererAppelAsync(geste, idJ, role, appel, ct));

        // Les evenements de creation du code affaire, survenus du jour, et leurs editions sur la demande OIPE (:2040-2050).
        foreach (var eve in (code.Creation ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idEve = (await EvenementsProcedureV2.SurvenuAsync(geste, idJ, eve, jour, avecFutur: false, ct)).IdSurvenu;
            bilans.Add(await EditionsEvenement.CreerAsync(geste, demande.Id, demande.Numero, idEve, ct));
        }

        // La demande validee : nom, indicatif, numeros « role J role IP », lien 2J (MajListeDemande, :2497-2504).
        await CreationV2.RattacherAsync(geste, demande, nomCourant, role, $"{role} {ip.Role}", VerrouDemandeV2.SequenceurInstance,
                                        idJ.ToString(CultureInfo.InvariantCulture), ct);
        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_DATE_VALIDATION = @m WHERE DMND_ID_DMND = @d", ct,
            new SqlParameter("@m", geste.Maintenant), new SqlParameter("@d", demande.Id));
        return (role, null);
    }

    // ------------------------------------------------------------------ LES EVENEMENTS

    /// <summary>Un evenement attendu de l'IP parmi <paramref name="codes"/> (le 1er des futurs, ListeEvenementsProcedureFutursSeuls) :
    /// son id, son code, son numero source.</summary>
    private sealed record Futur(int Id, string Code, string? Source);

    private static async Task<Futur?> FuturAsync(GesteEnCours geste, int idInstance, string codes, CancellationToken ct)
    {
        var colEve = await RelancesRsurv.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        await using var r = await geste.LireAsync($"""
            SELECT TOP (1) EVNPR_ID_EVNPR, EVNPR_CODE_EVE_PRO,
                   {(colEve.Contains("EVNPR_NUMERO_SOURCE") ? "EVNPR_NUMERO_SOURCE" : "CAST(NULL AS varchar(40))")} AS Source
            FROM v2.EVENEMENT_PROCEDURE
            WHERE EVNPR_ID_INSTN = @i AND EVNPR_CODE_EVE_PRO IN ({codes}) AND ISNULL(EVNPR_SURVENU, 0) = 0 AND ISNULL(EVNPR_DELAI_EXPIRE, 0) = 0
            ORDER BY EVNPR_NUM_ORDRE, EVNPR_ID_EVNPR
            """, ct, new SqlParameter("@i", idInstance));
        if (!await r.ReadAsync(ct)) return null;
        return new Futur(Convert.ToInt32(r["EVNPR_ID_EVNPR"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO").Trim(),
                         FormeLigne.Texte(r, "Source") is { Length: > 0 } s ? s : null);
    }

    /// <summary>L'echeance d'EcrivainSurveillanceIp pour un evenement attendu de cette IP (ExpirerAsync, CreerOpAsync).</summary>
    private static EcrivainSurveillanceIp.Echeance Echeance(Futur f, int idInstance, EtatIp ip, string nom)
        => new(f.Id, f.Code, true, f.Source, idInstance, ip.Role, nom, ip.Affaire);

    /// <summary>Le code « expire » d'un code d'evenement (R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE : ISIGNI -> IPNAV, IPCONSOPOS -> IPCAD dans
    /// RIG_DEV) ; null sans code.</summary>
    private static async Task<string?> CodeExpireAsync(GesteEnCours geste, string code, CancellationToken ct)
        => await RelancesRsurv.Scalaire(geste, "SELECT R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                                        new SqlParameter("@c", code)) is string { Length: > 0 } x && x.Trim().Length > 0 ? x.Trim() : null;

    /// <summary>L'evenement attendu <paramref name="code"/> de l'instance SURVIENT a <paramref name="date"/> (PasserEnSurvenuEveProFutur,
    /// EvenementProcedure.cs:1968-1997 : dates mini, maxi et fixe effacees, texte a reconstruire) ; sans evenement attendu, il est
    /// d'abord cree attendu a cette date (AddEvenementProcedureFuturOuExpire("", code, false, date), OPE_IPSUIVI.cs:2795-2803).
    /// <paramref name="lie"/> : son suivant (R_CODEEVEPRO_CODE_EVE_PRO_FUTUR) est cree attendu, aux dates du code
    /// (creationEvenementFutur, :1769-1810). Rend l'id survenu et le code du suivant cree.</summary>
    private static async Task<(int Id, string? Suivant)> SurvenirAsync(GesteEnCours geste, int idInstance, string code, DateTime date, bool? aPersonne,
        bool lie, HashSet<string> colEve, CancellationToken ct)
    {
        var idFutur = await RelancesRsurv.Scalaire(geste, """
            SELECT TOP (1) EVNPR_ID_EVNPR FROM v2.EVENEMENT_PROCEDURE
            WHERE EVNPR_ID_INSTN = @i AND EVNPR_CODE_EVE_PRO = @c AND ISNULL(EVNPR_SURVENU, 0) = 0 AND ISNULL(EVNPR_DELAI_EXPIRE, 0) = 0
            ORDER BY EVNPR_NUM_ORDRE, EVNPR_ID_EVNPR
            """, ct, new SqlParameter("@i", idInstance), new SqlParameter("@c", code)) is int f ? f : (int?)null;
        var id = idFutur ?? await EvenementAttenduAsync(geste, idInstance, code, date, null, null, null, colEve, ct);
        var sets = new List<string> { "EVNPR_SURVENU = 1", "EVNPR_DATE = @d", "EVNPR_DATE_MAXI = NULL", "EVNPR_DATE_MINI = NULL", "EVNPR_DATE_FIXE = NULL" };
        if (colEve.Contains("EVNPR_TEXTE")) sets.Add("EVNPR_TEXTE = NULL");
        if (aPersonne is { } p && colEve.Contains("EVNPR_SIGNIF_A_PERSONNE")) sets.Add($"EVNPR_SIGNIF_A_PERSONNE = {(p ? 1 : 0)}");
        await geste.EcrireAsync($"UPDATE v2.EVENEMENT_PROCEDURE SET {string.Join(", ", sets)} WHERE EVNPR_ID_EVNPR = @id", ct,
            new SqlParameter("@d", date), new SqlParameter("@id", id));
        if (!lie) return (id, null);

        // Le suivant et ses dates (Outils.CalculDate{Maxi,Mini,Fixe}EveProSurvenuInstanceMesure : mois, sinon jours, sinon semaines).
        var colCode = await RelancesRsurv.ColonnesAsync(geste, "v2.CODE_EVE_PRO", ct);
        if (!colCode.Contains("R_CODEEVEPRO_CODE_EVE_PRO_FUTUR")) return (id, null);
        string Col(string c) => colCode.Contains(c) ? c : $"CAST(NULL AS int) AS {c}";
        string? suivant = null;
        DateTime? maxi = null, mini = null, fixe = null;
        await using (var r = await geste.LireAsync($"""
            SELECT R_CODEEVEPRO_CODE_EVE_PRO_FUTUR,
                   {Col("R_CODEEVEPRO_MOIS_DATE_MAXI")}, {Col("R_CODEEVEPRO_JOURS_DATE_MAXI")}, {Col("R_CODEEVEPRO_SEMAINES_DATE_MAXI")},
                   {Col("R_CODEEVEPRO_MOIS_DATE_MINI")}, {Col("R_CODEEVEPRO_JOURS_DATE_MINI")}, {Col("R_CODEEVEPRO_SEMAINES_DATE_MINI")},
                   {Col("R_CODEEVEPRO_MOIS_DATE_FIXE")}, {Col("R_CODEEVEPRO_JOURS_DATE_FIXE")}, {Col("R_CODEEVEPRO_SEMAINES_DATE_FIXE")}
            FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c
            """, ct, new SqlParameter("@c", code)))
            if (await r.ReadAsync(ct))
            {
                suivant = FormeLigne.Texte(r, "R_CODEEVEPRO_CODE_EVE_PRO_FUTUR").Trim() is { Length: > 0 } s ? s : null;
                DateTime? Delai(string borne)
                {
                    int? N(string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
                    if (N($"R_CODEEVEPRO_MOIS_DATE_{borne}") is { } m) return date.AddMonths(m);
                    if (N($"R_CODEEVEPRO_JOURS_DATE_{borne}") is { } j) return date.AddDays(j);
                    if (N($"R_CODEEVEPRO_SEMAINES_DATE_{borne}") is { } sem) return date.AddDays(sem * 7);
                    return null;
                }
                (maxi, mini, fixe) = (Delai("MAXI"), Delai("MINI"), Delai("FIXE"));
            }
        if (suivant is null) return (id, null);
        var source = await RelancesRsurv.Scalaire(geste, "SELECT EVNPR_NUMERO_SOURCE FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_EVNPR = @id", ct,
                                                  new SqlParameter("@id", id)) as string;
        await EvenementAttenduAsync(geste, idInstance, suivant, maxi, mini, fixe, source, colEve, ct);
        return (id, suivant);
    }

    /// <summary>Un evenement ATTENDU (futur) sur l'instance : Instance.AddEvenementProcedureFuturOuExpire (Instance.cs:571-600), numero
    /// d'ordre = nombre d'evenements + 1 (AddEvenementProcedure, :588-594), numero source neuf, lie a <paramref name="lie"/>.</summary>
    private static async Task<int> EvenementAttenduAsync(GesteEnCours geste, int idInstance, string code, DateTime? maxi, DateTime? mini, DateTime? fixe,
        string? lie, HashSet<string> colEve, CancellationToken ct)
    {
        var type = await RelancesRsurv.Scalaire(geste, "SELECT R_CODEEVEPRO_TYPE_EVE_PRO FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                                                new SqlParameter("@c", code)) as string;
        var rang = Convert.ToInt32(await RelancesRsurv.Scalaire(geste, "SELECT COUNT(*) FROM v2.EVENEMENT_PROCEDURE WHERE EVNPR_ID_INSTN = @i", ct,
                                                                new SqlParameter("@i", idInstance)), CultureInfo.InvariantCulture) + 1;
        var valeurs = new Dictionary<string, object?>
        {
            ["EVNPR_ID_INSTN"] = idInstance, ["EVNPR_CODE_EVE_PRO"] = code, ["EVNPR_SURVENU"] = false, ["EVNPR_DELAI_EXPIRE"] = false,
            ["EVNPR_DATE_MAXI"] = maxi, ["EVNPR_DATE_MINI"] = mini, ["EVNPR_DATE_FIXE"] = fixe, ["EVNPR_NUM_ORDRE"] = (short)rang,
            ["EVNPR_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["EVNPR_NUMERO_SOURCE_EP_LIE"] = lie,
            ["EVNPR_TYPE_EVE_PRO"] = type is { Length: > 0 } t ? t[..1] : null,
        };
        return await CreationV2.InsererAsync(geste, "v2.EVENEMENT_PROCEDURE", "EVNPR_ID_EVNPR",
            valeurs.Where(v => colEve.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);
    }

    /// <summary>Les evenements attendus <paramref name="code"/> de l'instance EXPIRENT, texte a reconstruire (OPE_IPSUIVI.cs:1902-1907,
    /// :1964-1969).</summary>
    private static Task ExpirerFutursAsync(GesteEnCours geste, int idInstance, string code, HashSet<string> colEve, CancellationToken ct)
        => geste.EcrireAsync($"""
               UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_DELAI_EXPIRE = 1{(colEve.Contains("EVNPR_TEXTE") ? ", EVNPR_TEXTE = NULL" : "")}
               WHERE EVNPR_ID_INSTN = @i AND EVNPR_CODE_EVE_PRO = @c AND ISNULL(EVNPR_SURVENU, 0) = 0 AND ISNULL(EVNPR_DELAI_EXPIRE, 0) = 0
               """, ct, new SqlParameter("@i", idInstance), new SqlParameter("@c", code));
}
