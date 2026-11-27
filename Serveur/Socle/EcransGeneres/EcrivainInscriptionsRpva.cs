using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPVA » (PROC_EDI_RPVAINS) : TRAITER (accepter) ou REFUSER une demande d'inscription recue par le reseau RPVA, sur la
/// ligne cochee. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurInscriptionsRpva.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 15, 27/11/2026. Rien ne part hors du poste : la demande recue est lue dans la base du greffe (DEMAT_RPJC_INS), son etat
/// et les mandataires rattaches y sont ecrits, comme la V2.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDI_RPVAINS (fiche : proc_edi_rpvains.md, Detail OPE).
///   L'OUVERTURE de la demande (FormRechercheDemandes\OPE_RESULTAT_EDI_RPVAINS.cs:74-99), avant tout geste, refuse au mot pres :
///     une demande de MODIFICATION (RPJCI_TYPE_MAJ = 'M', l. 77-80) ; une demande deja refusee (RPJCI_ETAT_TRAITEMENT_RIG = 'R',
///     l. 82-85, avec son motif) ; une demande qu'un agent tient (RPJCI_USER_EN_COURS renseigne, l. 89-95 : la V2 refuse alors
///     tout le monde). La V2 traite une demande a la fois (double-clic sur UNE ligne, l. 74).
///
///   « Traiter la demande » (le bouton VALIDER renomme, FormTraitement\FORM_TRAITEMENT.cs:37) : la V2 montre deux grilles de
///   mandataires CANDIDATS, l'agent coche ceux a traiter. Ici, les saisies « Mandataires traites », « Mandataire principal » et
///   « Structures traitees » les nomment par leur abrege (ou leur numero, MNDTR_ID_MNDTR), separes par des virgules.
///   - candidats (FormTraitement\DataProcTraitement.cs:62-121, les criteres de nom sont libres a l'ecran de la V2) : grille des
///     personnes, un mandataire sans numero CNBF ni CNBF de structure (l. 77-109), sans CNBF et au CNBF de structure de la
///     demande (l. 117), ou deja au CNBF de la demande (l. 120) ; grille des structures, sans CNBF ni CNBF de structure, ou dont
///     le CNBF correspond a la structure dans CNBF_CORRESP_STRUCTURE (l. 115). La grille des structures n'existe que si la
///     demande a un SIREN ET un CNBF de structure, sinon l'avertissement de la V2 (OPE_TRAITEMENT_RECHERCHE.cs:97-105) ;
///   - controles (RigDataGridViewTraitements.cs:145-190), au mot pres : au moins un mandataire ; un seul principal ; le principal
///     coche a traiter, actif et avec abrege ; une structure deja principale (active, abrege, CNBF de structure) ne se coche
///     pas (RigDataGridViewTraitementsStructure.cs:144-158) ; un mandataire ne se coche pas dans les deux grilles
///     (RigDataGridViewTraitements.cs:193-198, la V2 propose de decocher : ici, refus) ;
///   - courriels (PROC_TRAITEMENT.cs:196-222, FormRemplacerEmail\) : si des mandataires traites ont un autre courriel que celui
///     du demandeur (RPJCI_MAIL), la V2 ouvre la liste de ces courriels a cocher ; ici la saisie « Courriels remplaces » les
///     nomme, ou « aucun » (la liste validee sans case cochee) ; vide, le geste est refuse avec la liste, comme la V2 la montre ;
///   - ecriture, personnes (RigDataGridViewTraitements.cs:223-273) : le CNBF de la demande (s'il est renseigne), son CNBF de
///     structure et son SIREN ; le courriel du demandeur (sans liste de remplacement : s'il est renseigne ; avec : a la place
///     d'un courriel coche ou d'un courriel vide) ; telephones et fax de la demande joints par « ; » (AjouteChamps,
///     DataProcDemandeInscr.cs:230-251) ; tout mandataire traite autre que le principal est DESACTIVE (MNDTR_ACTIF = 0) ;
///     structures (RigDataGridViewTraitementsStructure.cs:174-197) : CNBF vide, CNBF de structure et SIREN de la demande. Une
///     chaine vide s'ecrit NULL, comme le DBString de la V2 (Outils\DLL\RigTools\SQL\DBObject.cs:165-166, :120) ;
///   - la demande (GridViewBase\RigDataGridViewRpvaBase.cs:119-133) : si elle est a l'etat 1, RPJCI_ETAT = '2', date, 'A',
///     l'agent ; les instances des mandataires traites (parties representees ou plaidees) repassent a recentraliser sur
///     Infogreffe (Classes\AideInstances.cs:44-49 : INSTN_TOP_EXPERIAN = 0, INSTN_DATE_MAJ_INFOG = maintenant) -- rien n'est
///     envoye d'ici, l'envoi est un traitement a part de la V2.
///   - la V2 n'a pas de message de reussite (la fenetre se ferme) : le nom qu'elle donne a sa demande, « Demande d'inscription
///     de X acceptée » (PROC_TRAITEMENT.cs:285-287), en tient lieu.
///
///   « Refuser » : le motif est obligatoire (FormSaisieRejet\OPE_SAISIE_REJET.Designer.cs:71, MetierObligatoire), 300 caracteres
///   au plus (RPJCI_MOTIF_TRAITEMENT_RIG de RIG_DEV) ; DataProcDemandeInscr.Refuse (DataProcDemandeInscr.cs:213-229) n'agit que
///   sur une demande a l'etat 1, et ecrit RPJCI_ETAT = '2', RPJCI_DATE_TRAITEMENT_RIG = maintenant, RPJCI_ETAT_TRAITEMENT_RIG =
///   'R', le motif, l'agent ; sinon « Echec du traitement. » (PROC_TRAITEMENT.cs:186) ; reussi : « Demande d'inscription rejetée
///   avec succès. » (l. 173). La demande quitte la liste (etat 2).
///
/// NON REPRIS (nommes) : la recherche des candidats par nom et le service Infogreffe qui la prepare (OPE_TRAITEMENT_INFOS.cs:73-151,
/// un service externe : jamais appele) -- l'agent nomme les mandataires, le controle des candidats ci-dessus garde la regle ; la
/// creation d'un mandataire (FormCreationMandataire\) et le rapprochement d'acteurs (RAPPACTEUR), d'autres ecrans ; le nom de la
/// demande du processus (« ... acceptée / refusée ») : une trace, que le journal des gestes remplace (decision du plan).
/// TRACE : famille « reglage » (les inscriptions des avocats du greffe), « a modifie », l'etat avant et apres de la demande et
/// de chaque mandataire.
/// </summary>
internal sealed class EcrivainInscriptionsRpva : IEcrivainEcran
{
    public string Procedure => "PROC_EDI_RPVAINS";
    public string Ecran => "EDI RPVA";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes et les saisies du descripteur, au mot pres.</summary>
    internal const string Traiter = "Traiter la demande", Refuser = "Refuser", Motif = "Motif du refus",
        Mandataires = "Mandataires traités", Principal = "Mandataire principal", Structures = "Structures traitées",
        Courriels = "Courriels remplacés", Aucun = "aucun";

    internal const string Modification = "Cette demande de modification a déjà été traitée. Vous ne pouvez pas re-traiter une demande de modification déjà traitée.";
    internal const string Echec = "Echec du traitement.";
    internal const string Reussi = "Demande d'inscription rejetée avec succès.";

    /// <summary>Les messages de VerifierSelections (RigDataGridViewTraitements.cs:149-189) et des grilles, au mot pres ; les
    /// « Environment.NewLine » en blancs.</summary>
    internal const string SansMandataire = "Vous devez sélectionner un mandataire.";
    internal const string SansPrincipal = "Vous devez cocher un mandataire principal.";
    internal const string DeuxPrincipaux = "Vous ne pouvez cocher qu'un seul mandataire principal.";
    internal const string PrincipalNonTraite = "Le mandataire principal doit être remis à traiter.";
    internal const string PrincipalInactif = "Le mandataire principal doit être actif et avec abrégé.";
    internal const string StructurePrincipale = "Ce mandataire est mandataire principal. Vous ne pouvez pas le cocher";
    internal const string DeuxListes = "Attention ce mandataire est déjà coché dans la liste des mandataires. Vous ne pouvez pas cocher le même mandataire dans les deux listes.";
    internal const string SansStructureCnbf = "ATTENTION : Informations incomplètes, le n° SIREN est présent mais pas de n° CNBF structure fourni";
    internal const string SansSiren = "ATTENTION : Informations incomplètes, le n° CNBF structure est présent mais pas de n° SIREN fourni, il est donc impossible de récupérer les informations de la structure";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var refus = geste.Demande.Nom == Refuser;
        if (!refus && geste.Demande.Nom != Traiter)
            return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // La V2 ouvre UNE demande, double-cliquee (OPE_RESULTAT_EDI_RPVAINS.cs:74).
        if (ids.Length != 1) return IssueGeste.Refuser($"Cochez une seule demande : RIG v2 {(refus ? "refuse" : "traite")} les demandes une à une.");
        var motif = Saisie(geste, Motif);
        if (refus && motif.Length == 0) return IssueGeste.Refuser($"« {Motif} » est obligatoire.");
        if (refus && motif.Length > 300) return IssueGeste.Refuser($"« {Motif} » : 300 caractères au plus.");
        if (!int.TryParse(ids[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return IssueGeste.Refuser($"Demande introuvable dans la base du greffe ({ids[0]}).");

        var d = await LireDemandeAsync(geste, id, ct);
        if (d is null) return IssueGeste.Refuser($"Demande introuvable dans la base du greffe ({id}).");
        // Les refus de l'ouverture (OPE_RESULTAT_EDI_RPVAINS.cs:77-95), dans l'ordre de la V2 ; leurs « \n » en blancs.
        if (d.TypeMaj == "M") return IssueGeste.Refuser(Modification);
        if (d.EtatTraitement == "R")
            return IssueGeste.Refuser($"Vous ne pouvez pas traiter une demande d'inscription déjà refusée. Cette demande d'inscription a été refusée pour le motif suivant : {d.MotifAncien}");
        if (d.TenuePar.Length > 0) return IssueGeste.Refuser($"Cette demande d'inscription est en cours de traitement par l'utilisateur {d.TenuePar}");

        var compte = geste.Agent.CompteWindows.Length > 255 ? geste.Agent.CompteWindows[..255] : geste.Agent.CompteWindows;
        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        return refus ? await RefuserAsync(geste, d, motif, compte, idTexte, ct) : await TraiterAsync(geste, d, compte, idTexte, ct);
    }

    private static async Task<IssueGeste> RefuserAsync(GesteEnCours geste, Demande d, string motif, string compte, string idTexte, CancellationToken ct)
    {
        // Refuse n'agit que sur l'etat 1 (DataProcDemandeInscr.cs:216), sinon « Echec du traitement. ».
        if (d.Etat != "1") return IssueGeste.Refuser(Echec);
        await geste.EcrireAsync("""
            UPDATE v2.DEMAT_RPJC_INS SET RPJCI_ETAT = '2', RPJCI_DATE_TRAITEMENT_RIG = @maintenant, RPJCI_ETAT_TRAITEMENT_RIG = 'R',
                RPJCI_MOTIF_TRAITEMENT_RIG = @motif, RPJCI_USER_TRAITEMENT_RIG = @agent
            WHERE RPJCI_ID_RPJCI = @id
            """, ct, new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@motif", motif), new SqlParameter("@agent", compte),
            new SqlParameter("@id", d.Id));
        return IssueGeste.Ecrit(Reussi, [idTexte],
            new TraceGeste("reglage", $"la demande d'inscription RPVA n° {idTexte} ({d.Nom})", idTexte, "a modifie",
                new Dictionary<string, string> { ["Etat"] = "1 (en attente)" },
                new Dictionary<string, string> { ["Etat"] = "2 (refusée)", ["Motif"] = motif }));
    }

    private static async Task<IssueGeste> TraiterAsync(GesteEnCours geste, Demande d, string compte, string idTexte, CancellationToken ct)
    {
        var nomsPersonnes = Jetons(Saisie(geste, Mandataires));
        if (nomsPersonnes.Length == 0) return IssueGeste.Refuser(SansMandataire);
        var nomsPrincipal = Jetons(Saisie(geste, Principal));
        var nomsStructures = Jetons(Saisie(geste, Structures));

        // Les mandataires nommes, trouves dans la base du greffe.
        var personnes = new List<Mandataire>();
        var structures = new List<Mandataire>();
        Mandataire? principal = null;
        foreach (var (noms, liste) in new[] { (nomsPersonnes, personnes), (nomsPrincipal, (List<Mandataire>?)null), (nomsStructures, structures) })
            foreach (var nom in noms)
            {
                var (m, pourquoi) = await TrouverAsync(geste, nom, ct);
                if (m is null) return IssueGeste.Refuser(pourquoi!);
                if (liste is null) principal = m;
                else if (liste.All(x => x.Id != m.Id)) liste.Add(m);
            }

        // VerifierSelections, grille des personnes (RigDataGridViewTraitements.cs:155-189).
        if (nomsPrincipal.Length == 0) return IssueGeste.Refuser(SansPrincipal);
        if (nomsPrincipal.Length > 1) return IssueGeste.Refuser(DeuxPrincipaux);
        if (personnes.All(m => m.Id != principal!.Id)) return IssueGeste.Refuser(PrincipalNonTraite);
        if (!principal!.Actif || principal.Abrege.Length == 0) return IssueGeste.Refuser(PrincipalInactif);
        // Les candidats de la grille des personnes (DataProcTraitement.cs:77-120).
        foreach (var m in personnes)
            if (!((m.Cnbf.Length == 0 && m.CnbfStructure.Length == 0)
                  || (m.Cnbf.Length == 0 && d.CnbfStructure.Length > 0 && m.CnbfStructure == d.CnbfStructure)
                  || (d.Cnbf.Length > 0 && m.Cnbf == d.Cnbf)))
                return IssueGeste.Refuser($"{m.Designation} n'est pas un mandataire que RIG v2 propose pour cette demande : seuls ceux sans numéro CNBF ni CNBF de structure, ou déjà au numéro CNBF de la demande, s'y rattachent.");

        // La grille des structures (OPE_TRAITEMENT_RECHERCHE.cs:97-115) et ses cases (RigDataGridViewTraitementsStructure.cs:144-158).
        if (structures.Count > 0)
        {
            if (d.Siren.Length == 0) return IssueGeste.Refuser(SansSiren);
            if (d.CnbfStructure.Length == 0) return IssueGeste.Refuser(SansStructureCnbf);
            foreach (var m in structures)
            {
                if (personnes.Any(p => p.Id == m.Id)) return IssueGeste.Refuser(DeuxListes);
                if (m.Actif && m.Abrege.Length > 0 && m.CnbfStructure.Length > 0) return IssueGeste.Refuser(StructurePrincipale);
                if (!(m.Cnbf.Length == 0 && m.CnbfStructure.Length == 0) && !await CorrespondAsync(geste, m.Cnbf, d.CnbfStructure, ct))
                    return IssueGeste.Refuser($"{m.Designation} n'est pas une structure que RIG v2 propose pour cette demande : seules celles sans numéro CNBF ni CNBF de structure, ou dont le numéro CNBF correspond à la structure {d.CnbfStructure}, s'y rattachent.");
            }
        }

        // Le remplacement des courriels (PROC_TRAITEMENT.cs:196-222) : la liste des AUTRES courriels des personnes traitees.
        IReadOnlyCollection<string>? remplaces = null;
        string[] autres = d.Email.Length == 0 ? [] : personnes.Select(m => m.Email).Where(e => e.Length > 0 && e != d.Email).Distinct(StringComparer.Ordinal).ToArray();
        if (autres.Length > 0)
        {
            var saisis = Saisie(geste, Courriels);
            if (saisis.Length == 0)
                return IssueGeste.Refuser($"Des mandataires traités ont un autre courriel que celui du demandeur ({d.Email}) : {string.Join(", ", autres)}. Indiquez dans « {Courriels} » ceux à remplacer, ou « {Aucun} ».");
            var choisis = new List<string>();
            if (!string.Equals(saisis, Aucun, StringComparison.OrdinalIgnoreCase))
                foreach (var c in Jetons(saisis))
                {
                    var trouve = autres.FirstOrDefault(a => string.Equals(a, c, StringComparison.OrdinalIgnoreCase));
                    if (trouve is null) return IssueGeste.Refuser($"« {c} » n'est pas un courriel des mandataires traités ({string.Join(", ", autres)}).");
                    choisis.Add(trouve);
                }
            remplaces = choisis;
        }

        // Les valeurs ecrites, dans la largeur des colonnes de la base (une valeur trop longue ferait echouer la V2 en base).
        var telephone = Joindre(d.Telephones);
        var fax = Joindre(d.Fax);
        foreach (var (colonne, libelle, valeur) in new[] { ("MNDTR_TELEPHONE", "Le téléphone", telephone), ("MNDTR_FAX", "Le fax", fax), ("MNDTR_ADRESSE_MAIL", "Le courriel", d.Email) })
        {
            var largeur = await LargeurAsync(geste, colonne, ct);
            if (largeur > 0 && valeur.Length > largeur)
                return IssueGeste.Refuser($"{libelle} du demandeur ({valeur}) dépasse les {largeur} caractères de la fiche du mandataire ({colonne}).");
        }

        var traces = new List<TraceGeste>();
        foreach (var m in personnes)
        {
            var courriel = remplaces is null
                ? (d.Email.Length > 0 ? d.Email : m.Email)
                : (remplaces.Contains(m.Email) || m.Email.Length == 0 ? d.Email : m.Email);
            var actif = m.Id == principal.Id ? m.Actif : false;
            await geste.EcrireAsync("""
                UPDATE v2.MANDATAIRE SET MNDTR_NUMERO_CNBF = COALESCE(@cnbf, MNDTR_NUMERO_CNBF), MNDTR_NUMERO_CNBF_STRUCTURE = @structure,
                    MNDTR_SIREN = @siren, MNDTR_ADRESSE_MAIL = @courriel, MNDTR_TELEPHONE = @telephone, MNDTR_FAX = @fax, MNDTR_ACTIF = @actif
                WHERE MNDTR_ID_MNDTR = @id
                """, ct, Valeur("@cnbf", d.Cnbf), Valeur("@structure", d.CnbfStructure), Valeur("@siren", d.Siren), Valeur("@courriel", courriel),
                Valeur("@telephone", telephone), Valeur("@fax", fax), new SqlParameter("@actif", actif), new SqlParameter("@id", m.Id));
            traces.Add(Trace(m, new Dictionary<string, string>
            {
                ["Numéro CNBF"] = d.Cnbf.Length > 0 ? d.Cnbf : m.Cnbf, ["CNBF de structure"] = d.CnbfStructure, ["SIREN"] = d.Siren,
                ["Courriel"] = courriel, ["Actif"] = actif ? "oui" : "non",
            }));
        }
        foreach (var m in structures)
        {
            await geste.EcrireAsync("""
                UPDATE v2.MANDATAIRE SET MNDTR_NUMERO_CNBF = NULL, MNDTR_NUMERO_CNBF_STRUCTURE = @structure, MNDTR_SIREN = @siren
                WHERE MNDTR_ID_MNDTR = @id
                """, ct, Valeur("@structure", d.CnbfStructure), Valeur("@siren", d.Siren), new SqlParameter("@id", m.Id));
            traces.Add(Trace(m, new Dictionary<string, string> { ["Numéro CNBF"] = "", ["CNBF de structure"] = d.CnbfStructure, ["SIREN"] = d.Siren }));
        }

        // La demande, si elle attend encore (RigDataGridViewRpvaBase.cs:125-131).
        if (d.Etat == "1")
            await geste.EcrireAsync("""
                UPDATE v2.DEMAT_RPJC_INS SET RPJCI_ETAT = '2', RPJCI_DATE_TRAITEMENT_RIG = @maintenant, RPJCI_ETAT_TRAITEMENT_RIG = 'A',
                    RPJCI_USER_TRAITEMENT_RIG = @agent
                WHERE RPJCI_ID_RPJCI = @id
                """, ct, new SqlParameter("@maintenant", geste.Maintenant), new SqlParameter("@agent", compte), new SqlParameter("@id", d.Id));

        // Les instances des mandataires traites, a recentraliser sur Infogreffe (AideInstances.cs:44-49).
        var tous = personnes.Concat(structures).Select(m => m.Id).Distinct().ToArray();
        var marques = string.Join(", ", tous.Select((_, k) => $"@m{k}"));
        var instances = await geste.EcrireAsync($"""
            UPDATE v2.INSTANCE SET INSTN_TOP_EXPERIAN = 0, INSTN_DATE_MAJ_INFOG = @maintenant
            WHERE INSTN_ID_INSTN IN (SELECT p.PRT_ID_INSTN FROM v2.PARTIE AS p
                                     WHERE p.PRT_ID_MNDTR_POS IN ({marques}) OR p.PRT_ID_MNDTR_PLAI IN ({marques}))
            """, ct, [new SqlParameter("@maintenant", geste.Maintenant), .. tous.Select((m, k) => new SqlParameter($"@m{k}", m))]);

        traces.Insert(0, new TraceGeste("reglage", $"la demande d'inscription RPVA n° {idTexte} ({d.Nom})", idTexte, "a modifie",
            new Dictionary<string, string> { ["Etat"] = $"{d.Etat} ({(d.Etat == "1" ? "en attente" : "déjà traitée")})" },
            new Dictionary<string, string>
            {
                ["Etat"] = d.Etat == "1" ? "2 (acceptée)" : $"{d.Etat} (inchangé)",
                ["Mandataires"] = string.Join(", ", personnes.Select(m => m.Designation)), ["Principal"] = principal.Designation,
                ["Structures"] = string.Join(", ", structures.Select(m => m.Designation)),
                ["Instances à recentraliser"] = instances.ToString(CultureInfo.InvariantCulture),
            }));
        return IssueGeste.Ecrit($"Demande d'inscription de {d.Nom} acceptée", [idTexte], [.. traces]);
    }

    /// <summary>La demande recue, sous verrou de mise a jour.</summary>
    private static async Task<Demande?> LireDemandeAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT RPJCI_ID_RPJCI, RPJCI_ETAT, RPJCI_TYPE_MAJ, RPJCI_ETAT_TRAITEMENT_RIG, RPJCI_MOTIF_TRAITEMENT_RIG, RPJCI_USER_EN_COURS,
                   LTRIM(RTRIM(CONCAT(RPJCI_NOM, ' ', RPJCI_PRENOM))) AS Nom, RPJCI_DENOMINATION, RPJCI_NUMERO_CNBF,
                   RPJCI_NUMERO_CNBF_STRUCTURE, RPJCI_NUMERO_SIREN, RPJCI_MAIL, RPJCI_TELEPHONE, RPJCI_TELEPHONE2, RPJCI_TELEPHONE3,
                   RPJCI_FAX, RPJCI_FAX2, RPJCI_FAX3
            FROM v2.DEMAT_RPJC_INS WITH (UPDLOCK) WHERE RPJCI_ID_RPJCI = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        string T(string c) => FormeLigne.Texte(r, c);
        return new Demande(id, T("RPJCI_ETAT"), T("RPJCI_TYPE_MAJ"), T("RPJCI_ETAT_TRAITEMENT_RIG"), T("RPJCI_MOTIF_TRAITEMENT_RIG"),
            T("RPJCI_USER_EN_COURS"), T("Nom") is { Length: > 0 } n ? n : T("RPJCI_DENOMINATION"), T("RPJCI_NUMERO_CNBF"),
            T("RPJCI_NUMERO_CNBF_STRUCTURE"), T("RPJCI_NUMERO_SIREN"), T("RPJCI_MAIL"),
            [T("RPJCI_TELEPHONE"), T("RPJCI_TELEPHONE2"), T("RPJCI_TELEPHONE3")], [T("RPJCI_FAX"), T("RPJCI_FAX2"), T("RPJCI_FAX3")]);
    }

    /// <summary>Le mandataire nomme par son abrege, ou a defaut par son numero : un seul, sinon le refus dit pourquoi.</summary>
    private static async Task<(Mandataire?, string?)> TrouverAsync(GesteEnCours geste, string nom, CancellationToken ct)
    {
        var numero = int.TryParse(nom, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
        var parAbrege = new List<Mandataire>();
        var parNumero = new List<Mandataire>();
        await using (var r = await geste.LireAsync("""
                         SELECT MNDTR_ID_MNDTR, MNDTR_ABREGE, MNDTR_NOM, MNDTR_NUMERO_CNBF, MNDTR_NUMERO_CNBF_STRUCTURE, MNDTR_ADRESSE_MAIL,
                                MNDTR_ACTIF, CASE WHEN LTRIM(RTRIM(MNDTR_ABREGE)) = @nom THEN 0 ELSE 1 END AS Rang
                         FROM v2.MANDATAIRE WITH (UPDLOCK)
                         WHERE LTRIM(RTRIM(MNDTR_ABREGE)) = @nom OR MNDTR_ID_MNDTR = @numero
                         ORDER BY MNDTR_ID_MNDTR
                         """, ct, new SqlParameter("@nom", nom), new SqlParameter("@numero", (object?)numero ?? DBNull.Value)))
            while (await r.ReadAsync(ct))
                (r.GetInt32(r.GetOrdinal("Rang")) == 0 ? parAbrege : parNumero).Add(new Mandataire(r.GetInt32(0),
                    FormeLigne.Texte(r, "MNDTR_ABREGE"), FormeLigne.Texte(r, "MNDTR_NOM"), FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF"),
                    FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF_STRUCTURE"), FormeLigne.Texte(r, "MNDTR_ADRESSE_MAIL"), r["MNDTR_ACTIF"] is bool b && b));
        // L'abrege passe devant le numero ; deux mandataires au meme abrege ne se departagent que par leur numero.
        if (parAbrege.Count > 1)
            return (null, $"L'abrégé « {nom} » désigne {parAbrege.Count} mandataires : nommez-les par leur numéro ({string.Join(", ", parAbrege.Select(m => m.Id))}).");
        if (parAbrege.Count == 1) return (parAbrege[0], null);
        return parNumero.Count == 1 ? (parNumero[0], null) : (null, $"Mandataire introuvable dans la base du greffe : « {nom} ».");
    }

    /// <summary>Le CNBF de <paramref name="cnbf"/> est-il une personne de la structure (CNBF_CORRESP_STRUCTURE) ?</summary>
    private static async Task<bool> CorrespondAsync(GesteEnCours geste, string cnbf, string structure, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT COUNT(*) FROM v2.CNBF_CORRESP_STRUCTURE WHERE CNBFC_NUMERO_CNBF = @cnbf AND CNBFC_NUMERO_CNBF_STRUCTURE = @structure
            """, ct, new SqlParameter("@cnbf", cnbf), new SqlParameter("@structure", structure));
        return await r.ReadAsync(ct) && r.GetInt32(0) > 0;
    }

    /// <summary>La largeur d'une colonne texte de v2.MANDATAIRE dans la base du greffe (0 : sans limite lue).</summary>
    private static async Task<int> LargeurAsync(GesteEnCours geste, string colonne, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT COL_LENGTH('v2.MANDATAIRE', @c)", ct, new SqlParameter("@c", colonne));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture) : 0;
    }

    private static string Saisie(GesteEnCours geste, string libelle)
        => (geste.Demande.Valeurs.TryGetValue(libelle, out var v) ? v : "").Trim();

    private static string[] Jetons(string texte)
        => texte.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>AjouteChamps de la V2 (DataProcDemandeInscr.cs:230-251) : les valeurs non vides, jointes par « ; ».</summary>
    private static string Joindre(IEnumerable<string> valeurs) => string.Join(";", valeurs.Where(v => v.Length > 0));

    /// <summary>Une chaine vide s'ecrit NULL, comme le DBString de la V2.</summary>
    private static SqlParameter Valeur(string nom, string valeur) => new(nom, valeur.Length == 0 ? DBNull.Value : valeur);

    private static TraceGeste Trace(Mandataire m, Dictionary<string, string> apres)
    {
        var id = m.Id.ToString(CultureInfo.InvariantCulture);
        return new TraceGeste("reglage", $"le mandataire {m.Designation}", id, "a modifie",
            new Dictionary<string, string>
            {
                ["Numéro CNBF"] = m.Cnbf, ["CNBF de structure"] = m.CnbfStructure, ["Courriel"] = m.Email, ["Actif"] = m.Actif ? "oui" : "non",
            }, apres);
    }

    private sealed record Demande(int Id, string Etat, string TypeMaj, string EtatTraitement, string MotifAncien, string TenuePar, string Nom,
        string Cnbf, string CnbfStructure, string Siren, string Email, string[] Telephones, string[] Fax);

    private sealed record Mandataire(int Id, string Abrege, string Nom, string Cnbf, string CnbfStructure, string Email, bool Actif)
    {
        public string Designation => Abrege.Length > 0 ? $"{Nom} ({Abrege})" : $"{Nom} (n° {Id.ToString(CultureInfo.InvariantCulture)})";
    }
}
