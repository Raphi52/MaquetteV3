using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Demandes FNIG » (PROC_FNIG) : « Désactiver » une demande envoyée et « Renvoyer la demande à l'identique » une demande en
/// erreur. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurDemandesFnig.cs (Id = la demande, CFNIG_ID_CFNIG). Plan « RIG v3
/// 100 % fonctionnel », lot 16, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\EtapeJudiciaire\Rig_Etape_Instance\ENTITE_JUDICIAIRE (ALL)\Saisie\ETP_FNIG\
/// EDITION\OPE_EDITION.cs, la barre de l'edition (InitToolBar, l. 86-111 : « Désactiver », « Renvoyer la demande à l'identique »).
///   « Désactiver » (_Desactiver l. 325-328, puis VALIDER -> _Enregistrer l. 214-255) : la demande affichee devient de type
///     Desactivation EN MEMOIRE, et l'enregistrement en CREE UNE AUTRE (l. 236-246) : CasierJudiciaireFnig.Copie de la demande (toutes
///     ses colonnes sauf l'identifiant : le constructeur de copie, RigBaseGreffe\GREFFE\CASIER_JUDICIAIRE_FNIG.cs:465-472), puis
///     l'utilisateur COURANT (CFNIG_UTILISATEUR = NomUtilisateurFnig, « <login> : <nom> <prenom> », RigMetier\Habilitation\
///     Utilisateur.cs:121 ; CFNIG_ID_UTLST), sans date d'envoi, a envoyer (AEnvoyer), erreur vide, creee maintenant. La demande
///     d'origine n'est pas reecrite. Les entreprises ne suivent pas : Copie ne recopie que les colonnes de la demande, et Sauve
///     n'enregistre que la liste d'entreprises chargee dans l'objet neuf, vide (CasierJudiciaireFnig.cs:324-341). Le mode
///     Desactivation ouvre un seul champ, « Raison annulation » (OPE_EDITION_JUGEMENT.cs:126-138, Designer l. 99,
///     CFNIG_RAISON_ANNULATION, l. 268), saisi a cote de la liste ; sans saisie, la copie garde celle de la demande.
///   « Renvoyer la demande à l'identique » (_RenvoyerIdentique, l. 257-266) : la demande en erreur repasse a envoyer (AEnvoyer),
///     CFNIG_ERREUR_ENVOI_FNIG a NULL, et rien d'autre.
/// L'ENVOI au FNIG : ni l'un ni l'autre n'appelle le FNIG (proc_fnig.md, « Effet fonctionnel cle ») ; le traitement de fond de RIG v2
/// exporte les demandes « AEnvoyer ». La V3 n'appelle rien (partie externe, simulee : le message le dit) ; elle ecrit l'etat V2.
/// CONTROLES, avant toute ecriture :
///   1. au moins une demande cochee ;
///   2. la demande existe dans la base du greffe ;
///   3. l'etat qui allume le bouton (_ChangerActivationBouttons, l. 296-305) : « Désactiver » si la demande est envoyee et n'est pas
///      deja une desactivation (EstDesactivationPossible, l. 46-51) ; « Renvoyer » si elle est en erreur (EstCasierFnigEnErreur,
///      l. 56-58). Le bouton est grise sinon : messages sans source V2 ;
///   4. Processus.Verifier (l. 215, 259), pour la partie que la demande enregistree porte : les dates du jugement
///      (OPE_EDITION_JUGEMENT.DoVerifier, l. 210-233), messages AU MOT PRES -- debut d'interdiction avant le jugement ; debut apres la
///      fin, quand la fin est affichee (une demande rattachee a un casier du greffe, l. 99) ; jugement avant la naissance.
/// NON REPRIS (nommes) : le reste de Processus.Verifier -- la duree d'interdiction d'une demande d'une autre juridiction, un champ de
///   l'ecran que la demande ne stocke pas (l. 211), et les champs obligatoires des sous-formulaires : la demande a ete verifiee a son
///   enregistrement ; la confirmation « Êtes vous sur de vouloir envoyer la demande au FNIG? » (l. 119) ; « Modifier », « Valider »
///   une saisie et la nouvelle demande (une Saisie de l'edition, pas un geste de la liste).
/// Traces : famille « demande », la demande FNIG en mots, son id, « a cree » (la desactivation) ou « a modifie » (le renvoi).
/// </summary>
internal sealed class EcrivainDemandesFnig : IEcrivainEcran
{
    public string Procedure => "PROC_FNIG";
    public string Ecran => "Demandes FNIG";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes et la saisie du descripteur, au mot pres (InitToolBar, l. 102, 107 ; Designer de OPE_EDITION_JUGEMENT, l. 99).</summary>
    internal const string Desactiver = "Désactiver", Renvoyer = "Renvoyer la demande à l'identique", RaisonAnnulation = "Raison annulation";

    /// <summary>Les messages de OPE_EDITION_JUGEMENT.DoVerifier, au mot pres (l. 216, 222, 228).</summary>
    internal const string DebutAvantJugement = "La date de début d'interdiction ne peut pas être inférieur à la date du jugement",
        DebutApresFin = "La date de début d'interdiction ne peut pas être supérieur à la date de fin d'interdiction",
        JugementAvantNaissance = "La date du jugement ne peut pas être supérieur à la date de naissance";

    private sealed record Demande(int Id, string Personne, string Etat, string Type, bool RattacheeCasier,
        DateTime? Jugement, DateTime? Debut, DateTime? Fin, DateTime? Naissance, Dictionary<string, object?> Colonnes);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nomGeste = geste.Demande.Nom;
        if (nomGeste != Desactiver && nomGeste != Renvoyer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nomGeste}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        // 1. Une selection.
        if (ids.Length == 0) return IssueGeste.Refuser("Aucune demande cochée : rien n'est envoyé au FNIG.");

        var demandes = new List<Demande>();
        foreach (var id in ids)
        {
            // 2. La demande existe.
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || await LireAsync(geste, n, ct) is not { } d)
                return IssueGeste.Refuser($"Demande FNIG introuvable dans la base du greffe ({id}).");
            // 3. L'etat qui allume le bouton.
            if (nomGeste == Desactiver && (d.Etat != "Envoye" || d.Type == "Desactivation"))
                return IssueGeste.Refuser(d.Type == "Desactivation"
                    ? $"La demande n° {d.Id} ({d.Personne}) est déjà une désactivation : RIG v2 ne la désactive pas."
                    : $"La demande n° {d.Id} ({d.Personne}) n'est pas envoyée : RIG v2 ne désactive qu'une demande envoyée au FNIG.");
            if (nomGeste == Renvoyer && d.Etat != "Erreur")
                return IssueGeste.Refuser($"La demande n° {d.Id} ({d.Personne}) n'est pas en erreur : RIG v2 ne renvoie à l'identique qu'une demande en erreur.");
            // 4. Les dates du jugement (OPE_EDITION_JUGEMENT.DoVerifier).
            if (d.Debut is { } debut && d.Jugement is { } jugement && debut < jugement) return IssueGeste.Refuser(DebutAvantJugement);
            if (d.RattacheeCasier && d.Debut is { } debut2 && d.Fin is { } fin && debut2 > fin) return IssueGeste.Refuser(DebutApresFin);
            if (d.Naissance is { } naissance && d.Jugement is { } jugement2 && naissance > jugement2) return IssueGeste.Refuser(JugementAvantNaissance);
            demandes.Add(d);
        }

        return nomGeste == Desactiver ? await DesactiverAsync(geste, demandes, ct) : await RenvoyerAsync(geste, demandes, ct);
    }

    /// <summary>_Enregistrer, branche Desactivation (OPE_EDITION.cs:236-246) : une demande neuve, copie de la demande envoyee.</summary>
    private async Task<IssueGeste> DesactiverAsync(GesteEnCours geste, List<Demande> demandes, CancellationToken ct)
    {
        // L'utilisateur courant de RIG v2 (Utilisateur.GetUtilisateurCourant, l. 237) : le compte de l'agent, repris de UTILISATEUR
        // (Serveur/Recopie/FichesV3.cs:250 : CompteWindows = UTLST_LOGIN, Nom = UTLST_NOM, Prenom = UTLST_PRENOM ; IdV2 = UTLST_ID_UTLST).
        int idV2;
        string utilisateurFnig;
        await using (var r = await geste.LireAsync("SELECT IdV2, CompteWindows, Nom, Prenom FROM soc.Compte WHERE CompteWindows = @c", ct,
                         new SqlParameter("@c", geste.Agent.CompteWindows)))
        {
            if (!await r.ReadAsync(ct) || r.IsDBNull(0))
                return IssueGeste.Refuser("Votre compte n'a pas d'utilisateur RIG v2 : la désactivation ne peut pas porter son auteur (CFNIG_UTILISATEUR).");
            idV2 = r.GetInt32(0);
            // NomUtilisateurFnig (Utilisateur.cs:121) : UTLST_LOGIN + " : " + UTLST_NOM + " " + UTLST_PRENOM.
            utilisateurFnig = $"{FormeLigne.Texte(r, "CompteWindows")} : {FormeLigne.Texte(r, "Nom")} {FormeLigne.Texte(r, "Prenom")}";
        }
        var raison = geste.Demande.Valeurs.TryGetValue(RaisonAnnulation, out var saisie) ? saisie.Trim() : "";

        var traces = new List<TraceGeste>();
        var nouvelles = new List<string>();
        foreach (var d in demandes)
        {
            var copie = new Dictionary<string, object?>(d.Colonnes, StringComparer.OrdinalIgnoreCase)
            {
                ["CFNIG_TYPE_DEMANDE_FNIG"] = "Desactivation", // _Desactiver, l. 326, avant la copie
                ["CFNIG_UTILISATEUR"] = utilisateurFnig,        // l. 239
                ["CFNIG_ID_UTLST"] = idV2,                      // l. 240
                ["CFNIG_DATE_ENVOI"] = null,                    // l. 241
                ["CFNIG_ETAT_FNIG"] = "AEnvoyer",               // l. 242
                ["CFNIG_ERREUR_ENVOI_FNIG"] = "",               // l. 243
                ["CFNIG_DATE_CREATION"] = geste.Maintenant,     // l. 244
            };
            if (raison.Length > 0) copie["CFNIG_RAISON_ANNULATION"] = raison;
            var id = await geste.InsererAsync("v2.CASIER_JUDICIAIRE_FNIG", "CFNIG_ID_CFNIG", copie, ct);
            var cle = id.ToString(CultureInfo.InvariantCulture);
            nouvelles.Add(cle);
            traces.Add(new TraceGeste("demande", $"la désactivation FNIG n° {cle} ({d.Personne})", cle, "a cree", null,
                new Dictionary<string, string>
                {
                    ["Demande désactivée"] = d.Id.ToString(CultureInfo.InvariantCulture), ["Type"] = "Desactivation",
                    ["Etat"] = "AEnvoyer (à envoyer au FNIG)", ["Utilisateur FNIG"] = utilisateurFnig,
                    ["Raison annulation"] = raison.Length > 0 ? raison : Texte(d.Colonnes, "CFNIG_RAISON_ANNULATION"),
                }));
        }
        var premiere = demandes[0];
        return IssueGeste.Ecrit(
            (demandes.Count > 1
                ? $"{demandes.Count} désactivations FNIG créées, à envoyer"
                : $"Désactivation FNIG n° {nouvelles[0]} créée pour {premiere.Personne}, à envoyer") +
            $" : le traitement de fond de RIG v2 {(demandes.Count > 1 ? "les envoie" : "l'envoie")} au FNIG. Rien n'est envoyé d'ici.",
            [.. nouvelles], [.. traces]);
    }

    /// <summary>_RenvoyerIdentique (OPE_EDITION.cs:257-266) : la demande en erreur repasse a envoyer, son erreur effacee.</summary>
    private async Task<IssueGeste> RenvoyerAsync(GesteEnCours geste, List<Demande> demandes, CancellationToken ct)
    {
        var traces = new List<TraceGeste>();
        foreach (var d in demandes)
        {
            await geste.EcrireAsync("""
                UPDATE v2.CASIER_JUDICIAIRE_FNIG SET CFNIG_ETAT_FNIG = 'AEnvoyer', CFNIG_ERREUR_ENVOI_FNIG = NULL WHERE CFNIG_ID_CFNIG = @id
                """, ct, new SqlParameter("@id", d.Id));
            traces.Add(new TraceGeste("demande", $"la demande FNIG n° {d.Id} ({d.Personne})", d.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Etat"] = "Erreur (envoi refusé)", ["Erreur"] = Texte(d.Colonnes, "CFNIG_ERREUR_ENVOI_FNIG") },
                new Dictionary<string, string> { ["Etat"] = "AEnvoyer (à envoyer au FNIG)", ["Erreur"] = "" }));
        }
        return IssueGeste.Ecrit(
            (demandes.Count > 1
                ? $"{demandes.Count} demandes FNIG renvoyées à l'identique : à envoyer, leur erreur effacée ; le traitement de fond de RIG v2 les envoie"
                : $"Demande FNIG n° {demandes[0].Id} ({demandes[0].Personne}) renvoyée à l'identique : à envoyer, son erreur effacée ; le traitement de fond de RIG v2 l'envoie") +
            " au FNIG. Rien n'est envoyé d'ici.",
            [.. demandes.Select(d => d.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    /// <summary>Une colonne lue, en texte rogne (vide si NULL ou absente).</summary>
    private static string Texte(IReadOnlyDictionary<string, object?> colonnes, string colonne)
        => colonnes.TryGetValue(colonne, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";

    /// <summary>La demande, toutes ses colonnes (la copie de la desactivation les reprend, sauf l'identifiant).</summary>
    private static async Task<Demande?> LireAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT * FROM v2.CASIER_JUDICIAIRE_FNIG WITH (UPDLOCK) WHERE CFNIG_ID_CFNIG = @id", ct,
            new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        var colonnes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < r.FieldCount; i++)
            if (!string.Equals(r.GetName(i), "CFNIG_ID_CFNIG", StringComparison.OrdinalIgnoreCase))
                colonnes[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        DateTime? Date(string colonne) => colonnes.TryGetValue(colonne, out var v) && v is DateTime d ? d : null;
        var personne = $"{Texte(colonnes, "CFNIG_NOM")} {Texte(colonnes, "CFNIG_PRENOM")}".Trim();
        return new Demande(id, personne.Length > 0 ? personne : $"demande n° {id}", Texte(colonnes, "CFNIG_ETAT_FNIG"),
            Texte(colonnes, "CFNIG_TYPE_DEMANDE_FNIG"),
            colonnes.TryGetValue("CFNIG_ID_CSRJD", out var casier) && casier is not null,
            Date("CFNIG_DATE_JUGEMENT"), Date("CFNIG_DATE_DEBUT_INTERDICTION"), Date("CFNIG_DATE_FIN_INTERDICTION"),
            Date("CFNIG_DATE_NAISSANCE"), colonnes);
    }
}
