using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Publications BODACC » (PROC_GESTBOD, forme Operation) : les deux boutons de RIG v2 sur les annonces cochees, offerts au temps
/// Confirmer (gestes nommes, lot 11). Mode d'emploi : EcrivainEcran.cs ; les listes : LecteurPublicationsBodacc.cs (plan « RIG v3 100 %
/// fonctionnel », lot 11, 27/11/2026, reprises).
///
/// « VALIDER » -- la case « renvoyer » ou « verifier » de chaque liste :
///   - REJETS : Saisie\OPE_LST_LISTE_REJ.DoSauver (:51-57), en mode « MiseAJourDesRejets » (OPE_LST_MAJ_REJ, _MajRejet = true) : pour
///     chaque ligne cochee « renvoyer », BDCCR_ETAT_BODACC = '3' (« rectif. (rejet) a envoyer »). Une annonce deja a renvoyer (etat 3)
///     n'est plus dans la liste de la V2 : introuvable.
///   - CERTIFICATS DE PARUTION : Saisie\Certificats parution Bodacc\OPE_LST_CERTIF_BOD (DoGenererLesEditions, :49-59) : pour chaque ligne
///     cochee « verifier » dont l'etat n'est pas deja 6, BDCCR_ETAT_BODACC = '6' (« publicite faite et verifiee »). Une annonce deja
///     verifiee n'est pas reecrite (:57) : la V3 la nomme.
///   - CONTROLES NON CONFORMES : la liste n'a pas de case (OPE_LST_CTRLNC_BOD.CreeDataGridView, :108-119) : refuse, le message renvoie
///     a « Editer », la seule ecriture de RIG v2 sur cette liste.
/// « EDITER » -- les editions de VALIDER (DoGenererLesEditions), mises dans la FILE DES EDITIONS (v2.DEMANDE_EDITION, phase C) par
/// EditionsEvenementV2.DeposerAsync (les parametres du reglage sur leurs colonnes, le reste dans DMNED_PARAMETRE_GENERATION), toutes
/// rattachees a la demande du processus GESTBOD (Processus._CreateDemande -> Demande.CreateDemandeAutomate, RigAutomate\Processus.cs:
/// 962-966, sans nom ; MetierFacture = false, PROC_GESTBOD.Designer.cs:100-101), creee validee par EditionsV2.DemandeProcessusValideeAsync :
///   - REJETS : UNE demande LISTE_REJETS_BODACC (OPE_LST_LISTE_REJ.cs:62-120, mode liste, _MajRejet = false) -- source « DEMANDE » =
///     la demande du processus, libelle source « », et dans le XML de generation TitreEtat (« Listes des N Bodaccs rejetés », « Bodacc
///     rejeté » pour un seul, puis la periode : « dont la date envoi est ... », :73-94), GreffeNom (TypeEtNomGreffeMaj) et SQL (la
///     requete de l'etat : la selection, les jointures et la clause de date de la V2, plus l'UNION des annonces qui suivent un rejet
///     sans evenement, :102-106) ;
///   - CONTROLES NON CONFORMES : UNE demande LISTE_BODACC_QUALITE (OPE_LST_CTRLNC_BOD.cs:49-103), de meme (« Listes des N Bodaccs en
///     contrôle qualité non conforme » et sa periode, :61-96) ;
///   LES DEUX LISTES sont, comme dans RIG v2, TOUTE la liste de la recherche (la V2 n'y a pas de case) : les criteres que l'ecran envoie
///   avec le geste (FormeOperation, sous leur libelle) donnent la clause de date et la periode du titre, N est le nombre d'annonces de
///   cette liste (_listRejetsBod.Count, _listControlBod.Count ; le filtre du lecteur, LecteurPublicationsBodacc.FiltreDe). Cocher une
///   annonce de la liste choisit la liste a editer ; une annonce cochee hors de la liste de ces criteres : refus « relisez la liste ».
///   - CERTIFICATS DE PARUTION : par annonce, UNE demande CERTIF_BOD (procedure collective) ou CERTIF_BODPR (prevention) PAR
///     DESTINATAIRE (OPE_LST_CERTIF_BOD.cs:61-101) -- libelle « Certificat de parution au Bodacc, <PC ou PRV> EP <code> du <date> »
///     (CertifBod.cs:61-73, la date au format de DBDateTime, « dd/MM/yyyy HH:mm:ss »), libelle source le libelle du type d'annonce
///     (TYPE_BODACC, :82), source = BDCCR_NUMERO_SOURCE ; les destinataires sont ceux de l'entite dont le CODE est dans le reglage
///     CERTIF_BOD_DEST suivi de « ; » (PARAMETRAGE, repris dans soc.Reglage ; « MDJ » dans RIG_DEV, lu le 27/11/2026 ; :49, :75-76),
///     plus le document interne si le reglage contient « DI » (:77) ; chaque demande porte sa seule ENTITE, par defaut
///     (LISTE_ENTITE_DESTINATION, :96-99). Le destinataire au coffre (nom de coffre et date de demarrage passee, :85-93) : envoi au
///     coffre, pas d'impression, code CFE-CP « BODACC » si le reglage n'en donne pas.
///     Les destinataires d'une PC ou d'une prevention que la V3 calcule (RecupDestEditLot2FROMProcedureCollective / FROMPrevention,
///     JUDICIAIRE\DestinatairesEditionsLot2.cs:1628-1677, :1992-2018) : ses MANDATAIRES en mission (GERER_PC, MANDATAIRE_PC) -- code =
///     la fonction, valeur = le mandataire, libelle = Outils.FormerNomComplet (civilite abregee, nom, complement), adresse
///     (EditionsEvenement.BlocAdresseAsync du lot 8, puis TransfoTexteEnXml), son pays (ADRBS_PAYS, :1643), courriel, coffre, et la
///     reference du mandataire (REFERENCE_DESTINATAIRE = GRRPC_NUMERO_INTERNE, :1647, :2009) ; et pour une PC, sans coffre, le meme
///     sous le code « <fonction>SC » (:1655-1676).
///   Annonce de la liste des certificats sans procedure ni prevention : la V2 la saute (« continue », :69-73) ; la V3 aussi, et le dit.
/// CONTROLES : au moins une annonce cochee ; chaque annonce relue DANS la transaction, dans sa liste (la requete du lecteur) ; un
/// reglage d'edition absent (CreateDemandeEditionByParametreGeneration rend null, DemandeEdition.cs:2580-2584, la V2 s'arrete alors
/// sur l'objet nul) et un reglage CERTIF_BOD_DEST absent : refus nomme, rien d'ecrit ; un CODE du reglage CERTIF_BOD_DEST qui n'est
/// ni une fonction de mandataire (de GERER_PC, avec ou sans « SC ») ni « DI » -- la PC elle-meme, un juge, l'entourage, un dirigeant,
/// un creancier : RIG v2 les ajoute aussi (:1532-1700), la V3 ne les calcule pas -- : refus nomme, a faire dans RIG v2.
/// LA PARTIE EXTERNE (simulee, etiquetee) : l'impression et l'envoi au coffre-fort sont faits par le service d'edition de RIG v2, qui
/// relit la file des editions ; RIG v3 ne l'appelle pas.
/// NON REPRIS (nomme) : deux attributs de l'ENTITE d'un mandataire que RIG v2 pose aussi (:1644-1648) -- IS_ADRESSE_SPECIAL (adresse a
/// type special) et TELEPHONE_DESTINATAIRE (MNDPC_TELEPHONE) : l'outil commun des editions (EditionsEvenementV2.ListeXml) ne les ecrit
/// pas. EntiteMere (le tableau des editions, en memoire). La ligne de rapprochement de la demande (CreateLigneRappGene).
/// </summary>
// fix-ok: mesure par le controle final du lot 11 (27/11/2026) -- les listes visaient les annonces cochees (BDCCR_ID_BDCRC IN) avec un
// titre sans date, et le certificat perdait REFERENCE_DESTINATAIRE et ADRBS_PAYS du mandataire ; relu contre OPE_LST_LISTE_REJ.cs:73-94,
// OPE_LST_CTRLNC_BOD.cs:61-83 et DestinatairesEditionsLot2.cs:1643-1647.
// fix-ok: ajout demande par le controle final (27/11/2026), pas un correctif a l'aveugle -- les editions de VALIDER de RIG v2
// (OPE_LST_CERTIF_BOD.cs:61-101, OPE_LST_LISTE_REJ.cs:62-120, OPE_LST_CTRLNC_BOD.cs:49-103, relus) n'avaient pas d'ecrivain : « Editer » les ecrit.
internal sealed class EcrivainPublicationsBodacc : IEcrivainEcran
{
    public string Procedure => "PROC_GESTBOD";

    public string Ecran => "Publications BODACC";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    /// <summary>Les boutons du descripteur, au mot pres.</summary>
    internal const string Valider = "Valider", Editer = "Éditer";

    internal const string SansLigne = "Aucune annonce cochée : rien n'est enregistré.";
    internal const string ControleSansEcriture = "Un contrôle non conforme ne se valide pas : RIG v2 n'en édite que la liste (« Éditer »). Décochez-le.";
    internal const string SimuleImpression = "Impression simulée : le service d'édition de RIG v2 imprime, RIG v3 ne l'appelle pas.";
    internal const string SimuleCoffre = "Impression et envoi au coffre-fort simulés : le service d'édition de RIG v2 les fait, RIG v3 ne l'appelle pas.";

    /// <summary>Le reglage des destinataires du certificat (PARAMETRAGE CERTIF_BOD_DEST, OPE_LST_CERTIF_BOD.cs:49).</summary>
    internal const string ReglageDestinataires = "CERTIF_BOD_DEST";

    private static string Introuvable(string id) => $"Annonce introuvable dans sa liste BODACC ({id}) : relisez la liste.";

    private static string SansReglage(string code)
        => $"Rien n'est édité : le réglage d'édition {code} n'existe pas dans ce greffe, et RIG v2 ne peut alors pas créer l'édition.";

    private sealed record Annonce(int Id, char Liste, string Nom, string Etat);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom is not (Valider or Editer)) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(SansLigne);

        var annonces = new List<Annonce>();
        foreach (var id in ids)
        {
            if (id.Length < 3 || id[1] != '-' || LecteurPublicationsBodacc.RequeteDe(id[0]) is not { } requete
                || !int.TryParse(id[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return IssueGeste.Refuser(Introuvable(id));
            if (id[0] == 'Q' && geste.Demande.Nom == Valider) return IssueGeste.Refuser(ControleSansEcriture);
            Annonce? annonce = null;
            await using (var r = await geste.LireAsync(requete.Replace("/*filtre*/", "AND b.BDCCR_ID_BDCRC = @id", StringComparison.Ordinal),
                             ct, new SqlParameter("@plafond", 1), new SqlParameter("@id", n)))
                if (await r.ReadAsync(ct))
                    annonce = new Annonce(n, id[0], LecteurPublicationsBodacc.Ligne(r).Nom, FormeLigne.Texte(r, "BDCCR_ETAT_BODACC"));
            // Une annonce a renvoyer (etat 3) n'est plus dans la liste des rejets de la V2 (en tete).
            if (annonce is null || (annonce.Liste == 'R' && annonce.Etat != "9")) return IssueGeste.Refuser(Introuvable(id));
            annonces.Add(annonce);
        }
        return geste.Demande.Nom == Valider ? await ValiderAsync(geste, ids, annonces, ct) : await EditerAsync(geste, annonces, ct);
    }

    private static async Task<IssueGeste> ValiderAsync(GesteEnCours geste, string[] ids, List<Annonce> annonces, CancellationToken ct)
    {
        var traces = new List<TraceGeste>();
        var (renvoyees, verifiees, deja) = (0, 0, new List<string>());
        foreach (var a in annonces)
        {
            var idTexte = a.Id.ToString(CultureInfo.InvariantCulture);
            if (a.Liste == 'R')
            {
                await geste.EcrireAsync("UPDATE v2.BODACC SET BDCCR_ETAT_BODACC = '3' WHERE BDCCR_ID_BDCRC = @id AND BDCCR_ETAT_BODACC = '9'", ct,
                                        new SqlParameter("@id", a.Id));
                traces.Add(new TraceGeste("affaire", $"l'annonce BODACC {idTexte} ({a.Nom})", idTexte, "a modifie",
                    new Dictionary<string, string> { ["Etat"] = "9 - rejet Bodacc, à rectifier" },
                    new Dictionary<string, string> { ["Etat"] = "3 - rectif. (rejet) à envoyer" }));
                renvoyees++;
            }
            else if (a.Etat == "6") deja.Add(idTexte);
            else
            {
                await geste.EcrireAsync("UPDATE v2.BODACC SET BDCCR_ETAT_BODACC = '6' WHERE BDCCR_ID_BDCRC = @id AND BDCCR_ETAT_BODACC <> '6'", ct,
                                        new SqlParameter("@id", a.Id));
                traces.Add(new TraceGeste("affaire", $"l'annonce BODACC {idTexte} ({a.Nom})", idTexte, "a modifie",
                    new Dictionary<string, string> { ["Etat"] = a.Etat == "10" ? "10 - rectif. (rejet) fait, à vérifier" : "5 - publicité faite, à vérifier" },
                    new Dictionary<string, string> { ["Etat"] = "6 - publicité faite et vérifiée" }));
                verifiees++;
            }
        }
        if (traces.Count == 0)
            return IssueGeste.Refuser(deja.Count > 1 ? $"Les annonces {string.Join(", ", deja)} sont déjà vérifiées : rien n'est enregistré."
                                                     : $"L'annonce {deja[0]} est déjà vérifiée : rien n'est enregistré.");
        var phrases = new List<string>();
        if (renvoyees > 0)
            phrases.Add($"{(renvoyees > 1 ? $"{renvoyees} annonces" : "1 annonce")} à renvoyer : le prochain envoi BODACC (« Envoi JAL / BODACC ») "
                        + (renvoyees > 1 ? "les reprend." : "la reprend."));
        if (verifiees > 0) phrases.Add($"{(verifiees > 1 ? $"{verifiees} parutions vérifiées" : "1 parution vérifiée")}.");
        if (deja.Count > 0) phrases.Add($"Déjà vérifiée{(deja.Count > 1 ? "s" : "")}, inchangée{(deja.Count > 1 ? "s" : "")} : {string.Join(", ", deja)}.");
        return IssueGeste.Ecrit(string.Join(" ", phrases), [.. ids], [.. traces]);
    }

    // ------------------------------------------------------------------------------------------------------------ EDITER

    /// <summary>Une edition calculee : ce qui s'ecrit, et comment le message et la trace la nomment.</summary>
    private sealed record Calculee(EditionsEvenementV2.Edition Edition, string Libelle, string? Destinataire, bool SourceDemande);

    /// <summary>Les jointures de l'etat des listes, au mot pres (OPE_LST_LISTE_REJ.cs:72, OPE_LST_CTRLNC_BOD.cs:57).</summary>
    private const string FromEdition = " From BODACC With (nolock) INNER JOIN EVENEMENT_PROCEDURE With (nolock) ON BDCCR_ID_EVNPR = EVNPR_ID_EVNPR "
        + "LEFT OUTER JOIN PROCEDURE_COLLECTIVE With (nolock) ON EVNPR_ID_PRCLL = PRCLL_ID_PRCLL LEFT OUTER JOIN PREVENTION With (nolock) ON EVNPR_ID_PREVE = PREVE_ID_PREVE ";

    /// <summary>La requete de la liste des rejets (OPE_LST_LISTE_REJ.cs:96-106) sur la clause de date de la V2 (<see cref="DateV2"/>).</summary>
    internal static string RequeteRejets(string dateV2)
    {
        var where = (dateV2.Length == 0 ? " Where" : dateV2 + " And") + " (BDCCR_ETAT_BODACC = '9' AND BDCCR_TYPE_BODACC like 'P%')";
        const string select = " Select Isnull(PREVE_NUM_PRV,PRCLL_NUM_PC) as NUM_PC_OU_PRV , EVNPR_CODE_EVE_PRO, EVNPR_DATE, BDCCR_DATE_ENVOI, "
            + "isnull(BDCCR_MOTIF_REJET_BODACC_1,'') + ' ' + isnull(BDCCR_MOTIF_REJET_BODACC_2,'') + case isnull(BDCCR_COMMENTAIRE,'') when '' then '' "
            + "else ' Commentaire : ' + BDCCR_COMMENTAIRE end as BDCCR_MOTIF_REJET, convert(varchar(8000),BDCCR_TEXTE_BODACC) ";
        return select + FromEdition + where + " UNION " + select + FromEdition
             + " WHERE BDCCR_ID_PREC_BOD in (Select BDCCR_ID_BDCRC From BODACC With (nolock) " + where + " AND BDCCR_ID_EVNPR is null)";
    }

    /// <summary>La requete de la liste des controles non conformes (OPE_LST_CTRLNC_BOD.cs:85-90) sur la clause de date de la V2.</summary>
    internal static string RequeteControles(string dateV2)
        => "Select Isnull(PREVE_NUM_PRV,PRCLL_NUM_PC) as NUM_PC_OU_PRV , EVNPR_CODE_EVE_PRO, EVNPR_DATE, BDCCR_DATE_ENVOI, BDCCR_DATE_PUBLICITE, "
         + "BDCCR_NUM_PARUTION, BDCCR_CONTROLE_QUALITE  " + FromEdition + dateV2
         + " And (BDCCR_ETAT_BODACC = '5' or BDCCR_ETAT_BODACC = '6' or BDCCR_ETAT_BODACC = '10')"
         + " And BDCCR_CONTROLE_QUALITE is not null";

    /// <summary>La clause de date de l'etat et la fin de son titre, au mot pres (rejets : OPE_LST_LISTE_REJ.cs:73-94 -- date d'EP, avec
    /// l'evenement supprime, ou d'envoi ; controles : OPE_LST_CTRLNC_BOD.cs:61-83 -- date d'EP seule, de parution ou d'envoi), les dates
    /// comme la V2 les tient (le texte du champ date, « dd/MM/yyyy », relu par convert 103). Vide sans date : la liste des rejets
    /// ouverte sans critere.</summary>
    internal static (string Where, string Titre) DateV2(char liste, LecteurPublicationsBodacc.CriteresLus c)
    {
        if (c.Du is not { } du) return ("", "");
        var mini = du.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var maxi = c.Au?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        string Egal(string colonne) => $"{colonne} = convert(datetime, '{mini}',103)";
        string Entre(string colonne) => $"{colonne} between convert(datetime, '{mini}',103) and convert(datetime, '{maxi}',103)";
        string Titre(string quoi) => maxi is null ? $" dont la date {quoi} est {mini}" : $" dont la date {quoi} est comprise entre le {mini} et le {maxi}";
        return c.Mode switch
        {
            LecteurPublicationsBodacc.ModeDate.Envoi => (" Where " + (maxi is null ? Egal("BDCCR_DATE_ENVOI") : Entre("BDCCR_DATE_ENVOI")), Titre("envoi")),
            LecteurPublicationsBodacc.ModeDate.Parution => (" Where " + (maxi is null ? Egal("BDCCR_DATE_PUBLICITE") : Entre("BDCCR_DATE_PUBLICITE")), Titre("de parution")),
            // « EVNPR_DATE= » sans espace : la V2 (OPE_LST_LISTE_REJ.cs:85).
            _ when liste == 'R' => (maxi is null ? $" Where (EVNPR_DATE= convert(datetime, '{mini}',103) OR {Egal("BDCCR_DATE_EP_SUPPR")})"
                                                 : $" Where ({Entre("EVNPR_DATE")} OR {Entre("BDCCR_DATE_EP_SUPPR")})", Titre("d'évènement")),
            _ => (" Where " + (maxi is null ? Egal("EVNPR_DATE") : Entre("EVNPR_DATE")), Titre("d'évènement")),
        };
    }

    /// <summary>Les criteres de la recherche que l'ecran envoie avec le geste (FormeOperation : leur libelle, comme les parametres d'une
    /// audience, GesteEcran.Valeurs).</summary>
    private static LecteurPublicationsBodacc.CriteresLus CriteresDe(GesteEcran demande)
        => LecteurPublicationsBodacc.Lire(new RechercheEcran(null, new Dictionary<string, string>(
               demande.Valeurs.Where(v => v.Key is LecteurPublicationsBodacc.CritereDate or LecteurPublicationsBodacc.CritereDu
                                                 or LecteurPublicationsBodacc.CritereAu or LecteurPublicationsBodacc.CritereNumero), StringComparer.Ordinal)));

    /// <summary>La liste de la V2 sur ces criteres (le filtre du lecteur) : combien d'annonces elle compte (_listRejetsBod.Count,
    /// _listControlBod.Count : le titre), et lesquelles des cochees y sont. La liste des rejets de la V2 n'a que l'etat 9 (le lecteur y
    /// garde aussi les etats 3, figes).</summary>
    private static async Task<(int Total, HashSet<int> Presentes)> ListeAsync(GesteEnCours geste, char liste,
        (string Filtre, Func<SqlParameter[]> Parametres) f, List<int> ids, CancellationToken ct)
    {
        var filtre = f.Filtre + (liste == 'R' ? " AND b.BDCCR_ETAT_BODACC = '9'" : "");
        var requete = LecteurPublicationsBodacc.RequeteDe(liste)!;
        var total = 0;
        await using (var r = await geste.LireAsync(requete.Replace("/*filtre*/", filtre, StringComparison.Ordinal), ct,
                         [new SqlParameter("@plafond", 1), .. f.Parametres()]))
            if (await r.ReadAsync(ct)) total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
        var presentes = new HashSet<int>();
        var dans = $" AND b.BDCCR_ID_BDCRC IN ({string.Join(", ", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)))})";
        await using (var r = await geste.LireAsync(requete.Replace("/*filtre*/", filtre + dans, StringComparison.Ordinal), ct,
                         [new SqlParameter("@plafond", ids.Count), .. f.Parametres()]))
            while (await r.ReadAsync(ct)) presentes.Add(Convert.ToInt32(r["BDCCR_ID_BDCRC"], CultureInfo.InvariantCulture));
        return (total, presentes);
    }

    private static async Task<IssueGeste> EditerAsync(GesteEnCours geste, List<Annonce> annonces, CancellationToken ct)
    {
        var calculees = new List<Calculee>();
        var sautees = new List<string>();
        var criteres = CriteresDe(geste.Demande);
        foreach (var (liste, code, titre, requete) in new (char, string, Func<int, string>, Func<string, string>)[]
                 {
                     ('R', "LISTE_REJETS_BODACC", n => n > 1 ? $"Listes des {n} Bodaccs rejetés" : "Bodacc rejeté", RequeteRejets),
                     ('Q', "LISTE_BODACC_QUALITE", n => n > 1 ? $"Listes des {n} Bodaccs en contrôle qualité non conforme" : "Bodacc en contrôle qualité non conforme",
                      RequeteControles),
                 })
        {
            var ids = annonces.Where(a => a.Liste == liste).Select(a => a.Id).ToList();
            if (ids.Count == 0) continue;
            // RIG v2 edite TOUTE la liste de la recherche (DoGenererLesEditions, sans case : OPE_LST_LISTE_REJ.cs:62-120,
            // OPE_LST_CTRLNC_BOD.cs:49-103) : une annonce cochee hors de la liste de ces criteres est une liste a relire.
            if (LecteurPublicationsBodacc.FiltreDe(liste, criteres) is not { } f) return IssueGeste.Refuser(Introuvable($"{liste}-{ids[0]}"));
            var (total, presentes) = await ListeAsync(geste, liste, f, ids, ct);
            if (ids.FirstOrDefault(i => !presentes.Contains(i)) is var hors and > 0) return IssueGeste.Refuser(Introuvable($"{liste}-{hors}"));
            var (e, refus) = await NouvelleAsync(geste, code, sansListe: true, ct);
            if (refus is not null) return IssueGeste.Refuser(refus);
            if (await GreffeNomAsync(geste, ct) is not { } greffe)
                return IssueGeste.Refuser("Rien n'est édité : le nom du greffe de l'état (v2.INFO_GREFFE, v2.CODE_GREFFE, v2.TYPE_GREFFE, v2.PARAM_EDITIONS) "
                                          + "manque à la base du greffe.");
            var (date, periode) = DateV2(liste, criteres);
            e!.Parametres["TitreEtat"] = titre(total) + periode;
            e.Parametres["GreffeNom"] = greffe;
            e.Parametres["SQL"] = requete(date);
            e.Colonnes["DMNED_CODE_ENTITE_SOURCE"] = "DEMANDE";
            e.LibelleSource = " ";
            calculees.Add(new Calculee(e, $"{e.Libelle} ({total} annonce{(total > 1 ? "s" : "")}, toute la liste de la recherche{periode})", null, true));
        }

        var certificats = annonces.Where(a => a.Liste == 'C').ToList();
        if (certificats.Count > 0)
        {
            var reglage = await ReglageDestinatairesAsync(geste, ct);
            if (reglage is null)
                return IssueGeste.Refuser($"Rien n'est édité : le réglage {ReglageDestinataires} (destinataires du certificat) n'existe pas dans ce greffe.");
            if (await CodeNonCalculeAsync(geste, reglage, ct) is { } autre)
                return IssueGeste.Refuser($"Le réglage {ReglageDestinataires} vise « {autre} », un destinataire que RIG v3 ne calcule pas (seuls les mandataires de la "
                                          + "procédure et le document interne le sont) : rien n'est édité, faire le certificat dans RIG v2.");
            foreach (var a in certificats)
            {
                var c = await CertificatAsync(geste, a.Id, ct);
                if (c is null) { sautees.Add($"{a.Id} (ni procédure ni prévention)"); continue; }
                var destinataires = await DestinatairesAsync(geste, c, reglage, ct);
                if (destinataires.Count == 0) { sautees.Add($"{a.Id} (aucun destinataire « {reglage} »)"); continue; }
                foreach (var d in destinataires)
                {
                    var (e, refus) = await NouvelleAsync(geste, c.CodeEdition, sansListe: false, ct, $"Certificat de parution au Bodacc, {c.Titre}");
                    if (refus is not null) return IssueGeste.Refuser(refus);
                    e!.LibelleSource = c.TypeLibelle;
                    if (c.NumeroSource.Length > 0) e.Colonnes["DMNED_VALEUR_ID_SOURCE"] = c.NumeroSource;
                    if (d.Coffre)
                    {
                        e.Coffre = true;
                        e.Colonnes["DMNED_IMPRESSION"] = false;
                        if (e.Colonnes.GetValueOrDefault("DMNED_CODE_CFECP") is null or "") e.Colonnes["DMNED_CODE_CFECP"] = "BODACC";
                    }
                    var entite = d.Entite.Copie();
                    entite.ParDefaut = "1";
                    e.Destinataires.Add(entite);
                    calculees.Add(new Calculee(e, e.Libelle, d.Entite.EstDocumentInterne ? "document interne" : $"{d.Entite.Libelle}{(d.Coffre ? " (coffre-fort)" : "")}", false));
                }
            }
        }
        var raison = sautees.Count == 0 ? "" : $" Sans édition, comme RIG v2 : {string.Join(", ", sautees)}.";
        if (calculees.Count == 0) return IssueGeste.Refuser($"Rien n'est édité.{raison}");

        var (idDemande, numero, refusDemande) = await EditionsV2.DemandeProcessusValideeAsync(geste, "GESTBOD", "", ct);
        if (refusDemande is not null) return IssueGeste.Refuser(refusDemande);
        var idDemandeTexte = EditionsV2.Texte(idDemande);
        var crees = new List<string> { idDemandeTexte };
        var traces = new List<TraceGeste> { new("demande", $"la demande {numero} (Publications BODACC)", idDemandeTexte, "a cree", null,
                                                new { Demande = numero, Processus = "GESTBOD", Editions = calculees.Count }) };
        foreach (var c in calculees)
        {
            if (c.SourceDemande) c.Edition.Colonnes["DMNED_VALEUR_ID_SOURCE"] = idDemandeTexte;
            var depot = await EditionsEvenementV2.DeposerAsync(geste, idDemande, c.Edition, ct);
            var id = EditionsV2.Texte(depot.Id);
            crees.Add(id);
            traces.Add(new TraceGeste("demande", $"l'édition « {c.Edition.Libelle} »", id, "a cree", null,
                new { Document = c.Edition.Libelle, Destinataire = c.Destinataire ?? "", Coffre = depot.Coffre, Impression = depot.Impression,
                      Demande = numero, File = "file des éditions" }));
        }
        var noms = calculees.Select(c => c.Destinataire is null ? c.Libelle : $"{c.Libelle} → {c.Destinataire}");
        var message = $"{(calculees.Count > 1 ? $"{calculees.Count} éditions placées" : "1 édition placée")} dans la file des éditions : "
                    + $"{string.Join(" ; ", noms)}.{raison} {(calculees.Any(c => c.Edition.Coffre) ? SimuleCoffre : SimuleImpression)}";
        return IssueGeste.Ecrit(message, [.. crees], [.. traces]);
    }

    /// <summary>L'edition du reglage <paramref name="code"/> (CreateDemandeEditionByParametreGeneration, DemandeEdition.cs:2579-2600 :
    /// la liste du reglage, son libelle -- <paramref name="libelle"/> s'il est donne --, son type de generation, ses codes CFE-CP et
    /// Tribunal digital), ou le refus d'un reglage absent.</summary>
    private static async Task<(EditionsEvenementV2.Edition? Edition, string? Refus)> NouvelleAsync(GesteEnCours geste, string code, bool sansListe, CancellationToken ct,
        string? libelle = null)
    {
        var parametre = await EditionsV2.ParametreAsync(geste, code, ct);
        var liste = await EditionsEvenementV2.ReglageEditionAsync(geste, code, ct);
        if (parametre is null || liste is not { } l) return (null, SansReglage(code));
        // ParametreEdition.LibelleDocument (:257-266) : LIBELLE_DOCUMENT de la liste, sinon PRMED_LIBELLE.
        var libelleDocument = libelle ?? (l.Parametres.TryGetValue("LIBELLE_DOCUMENT", out var ld) && ld.Length > 0 ? ld : parametre.Libelle);
        var e = new EditionsEvenementV2.Edition
        {
            Parametres = new SortedDictionary<string, string>(l.Parametres, StringComparer.Ordinal), Impression = true,
            TypeGeneration = parametre.TypeGeneration, LibelleImpose = libelleDocument, SansListeDestinataires = sansListe,
        };
        e.Colonnes["DMNED_CODE_CFECP"] = parametre.Cfecp;
        e.Colonnes["DMNED_TRIBUNAL_DIGITAL_NATURE_DOC"] = parametre.NatureTribunalDigital;
        return (e, null);
    }

    /// <summary>RigGreffe.ParametresEdition.TypeEtNomGreffeMaj (RigTools\TOOLS\RigGreffe.cs:1576-1586) : le type du greffe en majuscules
    /// (TYPE_GREFFE.R_TYPEGREFFE_NOM_GREFFE_MAJ du CODE_GREFFE du greffe), une espace, PARAM_EDITIONS.PRMED_DE_NOM_MAJ (« | » devenus
    /// « &lt;BR&gt; », :1543) ; vide sans type trouve. Le code du greffe : INFO_GREFFE.CODE_GREFFE, comme l'etat du bordereau LR de RIG v2
    /// (MetierProcBordlr.cs, EcrivainBordereauxLr.RequeteEdition). Null : une de ces tables manque a la base.</summary>
    private static async Task<string?> GreffeNomAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using (var t = await geste.LireAsync("""
            SELECT CONVERT(bit, CASE WHEN OBJECT_ID(N'v2.INFO_GREFFE') IS NULL OR OBJECT_ID(N'v2.CODE_GREFFE') IS NULL
                                       OR OBJECT_ID(N'v2.TYPE_GREFFE') IS NULL OR OBJECT_ID(N'v2.PARAM_EDITIONS') IS NULL THEN 0 ELSE 1 END)
            """, ct))
            if (!await t.ReadAsync(ct) || !t.GetBoolean(0)) return null;
        await using var r = await geste.LireAsync("""
            SELECT TOP (1) CASE WHEN tg.R_TYPEGREFFE_VALEUR IS NULL THEN ''
                                ELSE ISNULL(tg.R_TYPEGREFFE_NOM_GREFFE_MAJ, '') + ' '
                                   + ISNULL((SELECT TOP (1) REPLACE(pe.PRMED_DE_NOM_MAJ, '|', '<BR>') FROM v2.PARAM_EDITIONS AS pe), '') END
            FROM v2.INFO_GREFFE AS ig
            LEFT JOIN v2.CODE_GREFFE AS cg ON cg.R_CODEGREFFE_VALEUR = ig.CODE_GREFFE
            LEFT JOIN v2.TYPE_GREFFE AS tg ON tg.R_TYPEGREFFE_VALEUR = cg.R_CODEGREFFE_TYPE_GREFFE
            """, ct);
        return await r.ReadAsync(ct) ? (r.IsDBNull(0) ? "" : r.GetString(0)) : "";
    }

    /// <summary>La valeur du reglage CERTIF_BOD_DEST (soc.Reglage) ; null s'il n'existe pas.</summary>
    private static async Task<string?> ReglageDestinatairesAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = @c", ct, new SqlParameter("@c", ReglageDestinataires));
        return await r.ReadAsync(ct) ? FormeLigne.Texte(r, "Valeur") : null;
    }

    /// <summary>Le premier code du reglage (avant « , » : le mode d'envoi, PARAM_UTILISATION de RIG_DEV) que la V3 ne calcule pas : ni
    /// « DI », ni une fonction de mandataire de GERER_PC, avec ou sans « SC » (en tete) ; null s'il n'y en a pas.</summary>
    private static async Task<string?> CodeNonCalculeAsync(GesteEnCours geste, string reglage, CancellationToken ct)
    {
        var fonctions = new HashSet<string>(StringComparer.Ordinal);
        await using (var r = await geste.LireAsync("SELECT DISTINCT GRRPC_FONCTION_PC FROM v2.GERER_PC WHERE GRRPC_FONCTION_PC IS NOT NULL", ct))
            while (await r.ReadAsync(ct)) fonctions.Add(r.GetString(0).Trim());
        return reglage.Split(';').Select(t => t.Split(',')[0].Trim()).Where(t => t.Length > 0)
            .FirstOrDefault(t => t != "DI" && !fonctions.Contains(t) && !(t.EndsWith("SC", StringComparison.Ordinal) && fonctions.Contains(t[..^2])));
    }

    /// <summary>Ce que CertifBod lit d'une annonce (CertifBod.cs:42-131) : son entite (la PC d'abord, OPE_LST_CERTIF_BOD.cs:63-69), le
    /// reglage d'edition qui en suit, le titre, le libelle du type et le numero source.</summary>
    private sealed record Certificat(int? IdPc, int? IdPrv, string CodeEdition, string Titre, string TypeLibelle, string NumeroSource);

    private static async Task<Certificat?> CertificatAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        // L'evenement de l'annonce ; sinon (« EpSuprimer », :89-131) celui de l'annonce qui la suit (BDCCR_ID_PREC_BOD), le code et la
        // date de l'evenement supprime etant ceux de l'annonce elle-meme (BODPERE).
        await using var r = await geste.LireAsync("""
            SELECT b.BDCCR_NUMERO_SOURCE,
                   (SELECT TOP (1) t.R_TYPEBODACC_LIBELLE_ECRAN FROM v2.TYPE_BODACC AS t WHERE t.R_TYPEBODACC_VALEUR = b.BDCCR_TYPE_BODACC) AS TypeLibelle,
                   CASE WHEN ev.EVNPR_ID_EVNPR IS NULL THEN b.BDCCR_CODE_EP_SUPPR ELSE ev.EVNPR_CODE_EVE_PRO END AS CodeEp,
                   CASE WHEN ev.EVNPR_ID_EVNPR IS NULL THEN b.BDCCR_DATE_EP_SUPPR ELSE ev.EVNPR_DATE END AS DateEp,
                   pc.PRCLL_ID_PRCLL, pc.PRCLL_NUM_PC, pr.PREVE_ID_PREVE, pr.PREVE_NUM_PRV
            FROM v2.BODACC AS b
            LEFT JOIN v2.EVENEMENT_PROCEDURE AS ev ON ev.EVNPR_ID_EVNPR = b.BDCCR_ID_EVNPR
            OUTER APPLY (SELECT TOP (1) e2.EVNPR_ID_PRCLL, e2.EVNPR_ID_PREVE FROM v2.BODACC AS s
                         JOIN v2.EVENEMENT_PROCEDURE AS e2 ON e2.EVNPR_ID_EVNPR = s.BDCCR_ID_EVNPR
                         WHERE s.BDCCR_ID_PREC_BOD = b.BDCCR_ID_BDCRC ORDER BY s.BDCCR_ID_BDCRC) AS sup
            LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = CASE WHEN ev.EVNPR_ID_EVNPR IS NULL THEN sup.EVNPR_ID_PRCLL ELSE ev.EVNPR_ID_PRCLL END
            LEFT JOIN v2.PREVENTION AS pr ON pr.PREVE_ID_PREVE = CASE WHEN ev.EVNPR_ID_EVNPR IS NULL THEN sup.EVNPR_ID_PREVE ELSE ev.EVNPR_ID_PREVE END
            WHERE b.BDCCR_ID_BDCRC = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        int? idPc = r["PRCLL_ID_PRCLL"] is int p ? p : null, idPrv = r["PREVE_ID_PREVE"] is int v ? v : null;
        if (idPc is null && idPrv is null) return null;
        // « le dernier renseigne gagne » pour le titre (:61-71), la PC d'abord pour l'entite (OPE_LST_CERTIF_BOD.cs:63-69).
        var numero = idPrv is not null ? FormeLigne.Texte(r, "PREVE_NUM_PRV") : FormeLigne.Texte(r, "PRCLL_NUM_PC");
        var date = r["DateEp"] is DateTime d ? d.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) : "";
        var titre = $"{numero} EP {FormeLigne.Texte(r, "CodeEp")} du {date}";
        return new Certificat(idPc, idPrv, idPc is not null ? "CERTIF_BOD" : "CERTIF_BODPR", titre, FormeLigne.Texte(r, "TypeLibelle"),
                              FormeLigne.Texte(r, "BDCCR_NUMERO_SOURCE"));
    }

    /// <summary>Un destinataire retenu, et s'il part au coffre-fort (:85-93).</summary>
    private sealed record Retenu(EditionsEvenementV2.Destinataire Entite, bool Coffre);

    /// <summary>Les destinataires du certificat : les mandataires de l'entite dont le CODE suivi de « ; » est dans le reglage suivi de
    /// « ; » (:75-76, Contains tel quel), puis le document interne si le reglage contient « DI » (:77).</summary>
    private static async Task<List<Retenu>> DestinatairesAsync(GesteEnCours geste, Certificat c, string reglage, CancellationToken ct)
    {
        var parametre = reglage + ";";
        var pc = c.IdPc is not null;
        var retenus = new List<Retenu>();
        await using (var r = await geste.LireAsync($"""
            SELECT g.GRRPC_FONCTION_PC, g.GRRPC_ID_MNDPC, m.MNDPC_CIVILITE, c.R_CIVILITE_ABREGE_EDIT, m.MNDPC_NOM, m.MNDPC_COMPLEMENT_NOM,
                   m.MNDPC_ID_ADRBS, m.MNDPC_ADRESSE_MAIL, m.MNDPC_NOM_COFFRE, m.MNDPC_DATE_DEMARRAGE_COFFRE, g.GRRPC_NUMERO_INTERNE,
                   a.Id AS AdresseLue, a.Pays
            FROM v2.GERER_PC AS g JOIN v2.MANDATAIRE_PC AS m ON m.MNDPC_ID_MNDPC = g.GRRPC_ID_MNDPC
            LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = m.MNDPC_CIVILITE
            LEFT JOIN rec.Adresse AS a ON a.Id = m.MNDPC_ID_ADRBS
            WHERE {(pc ? "g.GRRPC_ID_PRCLL" : "g.GRRPC_ID_PREVE")} = @id AND g.GRRPC_FIN_MISSION = 0
            ORDER BY g.GRRPC_ID_GRRPC
            """, ct, new SqlParameter("@id", (object?)c.IdPc ?? c.IdPrv)))
        {
            var lus = new List<(string Fonction, string Valeur, string Nom, int? IdAdresse, string Email, string NomCoffre, bool Coffre,
                                string Reference, string Pays)>();
            while (await r.ReadAsync(ct))
            {
                var civilite = FormeLigne.Texte(r, "MNDPC_CIVILITE");
                var nom = FormeLigne.Texte(r, "MNDPC_NOM") + " " + FormeLigne.Texte(r, "MNDPC_COMPLEMENT_NOM");
                // Outils.FormerNomComplet (Fonctions diverses\Outils.cs:663-669) ; MandatairePC.NomCoffre (MandatairePC.cs:86-93).
                var complet = (civilite is "PM" or "ND" ? nom : FormeLigne.Texte(r, "R_CIVILITE_ABREGE_EDIT") + " " + nom).Trim();
                DateTime? demarrage = r["MNDPC_DATE_DEMARRAGE_COFFRE"] is DateTime dd ? dd : null;
                var nomCoffre = demarrage > geste.Maintenant.Date ? "" : FormeLigne.Texte(r, "MNDPC_NOM_COFFRE");
                lus.Add((FormeLigne.Texte(r, "GRRPC_FONCTION_PC").Trim(), EditionsV2.Texte(Convert.ToInt32(r["GRRPC_ID_MNDPC"], CultureInfo.InvariantCulture)),
                         complet, r["MNDPC_ID_ADRBS"] is int a ? a : null, FormeLigne.Texte(r, "MNDPC_ADRESSE_MAIL"), nomCoffre,
                         nomCoffre != "" && demarrage is { } dem && dem <= geste.Maintenant.Date,
                         // REFERENCE_DESTINATAIRE = GRRPC_NUMERO_INTERNE (DBString.Valeur : « » si nul, :1647, :1670, :2009) ; PAYS =
                         // ADRBS_PAYS de l'adresse lue (:1643, :1666), « FRA » (le defaut d'ENTITE) sans adresse.
                         FormeLigne.Texte(r, "GRRPC_NUMERO_INTERNE"), r["AdresseLue"] is DBNull ? "FRA" : FormeLigne.Texte(r, "Pays")));
            }
            r.Close();
            foreach (var m in lus)
            {
                // AdresseMultiLignesPourEcran : les lignes du bloc d'edition (EditionsEvenement.BlocAdresseAsync, separees par « | »,
                // AdresseMultiLignesPourEdition) sur des sauts de ligne, puis TransfoTexteEnXml (DestinatairesEditionsLot2.cs:1637-1640).
                var bloc = m.IdAdresse is { } ia ? await EditionsEvenement.BlocAdresseAsync(geste, ia, ct) : "";
                var adresse = TexteEnXml(bloc.Replace("|", "\r\n", StringComparison.Ordinal));
                EditionsEvenementV2.Destinataire Entite(string code, string coffre) => new()
                {
                    Code = code, Valeur = m.Valeur, Libelle = m.Nom, Adresse = adresse, Email = m.Email, NomCoffre = coffre,
                    Cle = $"GERER_PC_{m.Fonction}_{m.Valeur}", Reference = m.Reference, Pays = m.Pays,
                };
                var codes = new List<(string Code, string Coffre)> { (m.Fonction, m.NomCoffre) };
                if (pc && m.NomCoffre == "") codes.Add((m.Fonction + "SC", ""));            // « Sans coffre », PC seulement (:1655-1676)
                foreach (var (code, coffre) in codes)
                    if (code != "" && parametre.Contains(code + ";", StringComparison.Ordinal))
                        retenus.Add(new Retenu(Entite(code, coffre), m.Coffre));
            }
        }
        if (reglage.Contains("DI", StringComparison.Ordinal))
            retenus.Add(new Retenu(new EditionsEvenementV2.Destinataire(), false));              // CreateEntiteDocumentInterne (:113-124)
        return retenus;
    }

    /// <summary>TransfoTexteEnXml (DestinatairesEditionsLot2.cs:1091-1103) : le texte tel que RIG v2 le pose dans l'attribut ADRESSE
    /// d'une ENTITE (la serialisation XML l'echappe ensuite encore une fois, comme XElement dans EditionsEvenementV2.ListeXml).</summary>
    internal static string TexteEnXml(string texte)
    {
        if (string.IsNullOrEmpty(texte)) return texte;
        return texte.Replace("&", "&amp;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal)
                    .Replace("\"", "&quot;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
                    .Replace("<", "&lt;", StringComparison.Ordinal).Replace("\r\n", "&amp;crlf;", StringComparison.Ordinal)
                    .Replace("\r", "&amp;crlf;", StringComparison.Ordinal).Replace('\u001F', ' ');
    }
}
