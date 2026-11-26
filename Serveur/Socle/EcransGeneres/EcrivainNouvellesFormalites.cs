using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Nouvelle formalité » (PROC_NF, processus NF) : OUVRIR la procedure collective d'une INSTANCE existante, dans la base du
/// greffe. Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurNouvellesFormalites.cs ; le commun : CreationPcV2.cs (le meme
/// que « Nouvelle proc. collective »). Plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_NF (fiche proc_nf.md « Détail OPE » §1-5, titre « Création d'une PC », FORM_NF.Designer.cs:53) :
/// l'objet est une INSTANCE (MetierBase, PROC_NF.cs:18), MetierCreateAffaire = false (PROC_NF.Designer.cs:349) ; a la selection
/// l'instance est verrouillee (Lock, PROC_NF.cs:55) ; en saisie, la PC se cree depuis le DEBITEUR de l'instance et lui est
/// rattachee (PROC_NF.cs:73-77, setter Instance.procedureCollective, Instance.cs:1075-1085).
///
/// CONTROLES DE RIG v2, dans l'ordre (un refus n'ecrit rien) :
///   1. le numero de role est obligatoire (etape « Instance », MetierObligatoire, PROC_NF.Designer.cs:270) ; le nom de
///      l'affaire aussi (OPE_PROCEDURE_COLLECTIVE.Designer.cs:185) ;
///   2. l'etape de recherche ne trouve que les instances des series JRCT et JRPC (MetierParamNumMetiers, PROC_NF.Designer.cs:305,
///      filtre CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE comme LecteurNouvelleInstance.TypesXaff) ; plusieurs : « Plusieurs entités
///      correspondent à ce numéro… » (OPE_ENTITE_JUD_RECHERCHE.cs:286) ; aucune : refus au libelle V3 (le champ de recherche de
///      RIG v2 ne propose rien) ;
///   3. MetierAllowPcNullOnInstance=2 (PROC_NF.Designer.cs:303-304) : « Une PC est rattachée à l'instance. »
///      (OPE_ENTITE_JUD_RECHERCHE.cs:300-301) ;
///   4. le verrou : une instance tenue par une autre demande reste en lecture seule (PROC_NF.cs:55-61) -- message de
///      VerrouDemandeV2 ;
///   5. la date et la nature du jugement, comme « Nouvelle proc. collective » (CreationPcV2.Natures) ;
///   6. LE DEBITEUR : la premiere partie (ordre PARTIE.cs:797 : lettre de la qualite, puis numero d'ordre) dont
///      QualifiantAuteurSaisinePC vaut Débiteur (Partie.cs:252-292) -- instance de type 08, JC, PR ou LS avec un acte de
///      saisine, en cours d'ouverture (IsEOUV : nature d'affaire d'origine ISO « 08 », Instance.cs:46-53). L'autre cas, IsEPREV
///      (:58-61, prevention dont la cle PREVE_ID_PREVE_PREVENTION est vide -- c'est son ID, Prevention.cs:526 --), ne vaut que
///      pour une prevention pas encore enregistree : jamais pour une instance lue en base. Auteur de la saisine « 1 »
///      (R_CODEACTEPR_AUTEUR_SAISINE) : les
///      DEMANDEURS (D…, dont DOIP) ; un autre auteur non vide : les DEFENDEURS (F…, dont FOIP). La PC prend son dossier RCS,
///      sinon son non-inscrit (PROC_NF.cs:74-75). Sans l'un ni l'autre, RIG v2 cree une PC vide qui n'a pas d'entreprise
///      (PROC_NF.cs:76) et dont la sauvegarde echoue (PRCLL_ID_RPRTR NOT NULL sur RIG_DEV ; proc_nf.md:218 « la création de PC
///      échoue ») : la V3 le refuse avant toute ecriture ;
///   7. l'adresse du client a facturer, la numerotation JPC de l'annee du jugement (CreationPcV2.CreerAsync).
///
/// CE QUI S'ECRIT : la demande NF validee, au nom de l'instance, indicatif = son numero de role, liee a l'instance (2J) ; le
/// client « compte PC » ; la PC (type « 0 » sans date, PROC_NF.cs:77, recalcule avec la date comme a la sauvegarde) numerotee
/// JPC ; INSTN_ID_PRCLL de l'instance. Trace : famille « affaire », « a cree ». La PC se retrouve par la recherche de
/// « Procédure collective » (PROC_MPC), l'instance par celle de « Nouvelle instance » (PROC_XAFF).
///
/// NON REPRIS (nommes ; le message le dit) : ceux de CreationPcV2 (facture NF -- PC au forfait ou non, PROC_NF.cs:192-193 --,
/// editions, etablissements, dirigeants, organes, chiffres, evenements, note) ; les parties, appels, decision, chef de demande et
/// evenements de l'instance que la saisie de RIG v2 permet de completer (PROC_NF.cs:79-160) : la V3 ne les modifie pas.
/// </summary>
internal sealed class EcrivainNouvellesFormalites : IEcrivainEcran
{
    internal const string NomEcran = "Nouvelle formalité";

    /// <summary>Les etapes et les champs du descripteur PROC_NF (Ouvrir.cs), au mot pres.</summary>
    internal const string EtapeInstance = "Instance", EtapeProcedure = "Procédure collective";

    internal const string ChampRole = "Numéro de rôle", ChampNom = "Nom de l'affaire", ChampNature = "Nature", ChampDate = "Date du jugement";

    /// <summary>Les series de l'etape de recherche (MetierParamNumMetiers, PROC_NF.Designer.cs:305).</summary>
    internal const string Series = "('JRCT', 'JRPC')";

    public string Procedure => "PROC_NF";
    public string Ecran => NomEcran;
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. Les champs obligatoires.
        var role = Saisi(geste, EtapeInstance, ChampRole).Replace(" ", "", StringComparison.Ordinal);
        if (role.Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeInstance} › {ChampRole}"));
        string P(string champ) => Saisi(geste, EtapeProcedure, champ);
        if (P(ChampNom).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeProcedure} › {ChampNom}"));

        // 2-3. L'instance, par l'etape de recherche.
        var trouvees = new List<(CreationPcV2.InstancePortee Instance, bool APc)>();
        await using (var r = await geste.LireAsync($"""
            SELECT i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NUM_REP_GENERAL, i.INSTN_NOM_COURANT,
                   CONVERT(bit, CASE WHEN i.INSTN_ID_PRCLL IS NULL THEN 0 ELSE 1 END) AS APc
            FROM v2.INSTANCE AS i JOIN v2.CODE_AFFAIRE AS ca ON ca.R_CODEAFFAIR_VALEUR = i.INSTN_CODE_AFFAIRE
            WHERE i.INSTN_NUM_ROLE = @role AND ca.R_CODEAFFAIR_TYPE_NUM_ROLE IN {Series}
            ORDER BY i.INSTN_ID_INSTN
            """, ct, new SqlParameter("@role", role)))
            while (await r.ReadAsync(ct))
                trouvees.Add((new CreationPcV2.InstancePortee(r.GetInt32(0), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
                    FormeLigne.Texte(r, "INSTN_NUM_REP_GENERAL"), FormeLigne.Texte(r, "INSTN_NOM_COURANT")), r.GetBoolean(4)));
        if (trouvees.Count == 0)
            return IssueGeste.Refuser($"Aucune instance des séries JRCT ou JRPC ne porte le numéro « {role} » dans la base du greffe.");
        if (trouvees.Count > 1)
            return IssueGeste.Refuser("Plusieurs entités correspondent à ce numéro. Effectuer une recherche pour charger l'instance, la PC ou la prévention souhaitée.");
        var (instance, aPc) = trouvees[0];
        if (aPc) return IssueGeste.Refuser("Une PC est rattachée à l'instance.");

        // 4. Le verrou de RIG v2.
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance,
                instance.Id.ToString(CultureInfo.InvariantCulture), ct) is { } verrou)
            return IssueGeste.Refuser(verrou);

        // 5. Le jugement.
        var dateSaisie = P(ChampDate);
        var date = dateSaisie.Length > 0 ? Date(dateSaisie) : null;
        if (dateSaisie.Length > 0 && date is null)
            return IssueGeste.Refuser($"« {EtapeProcedure} › {ChampDate} » : date illisible ({dateSaisie}).");
        var nature = CreationPcV2.Natures.FirstOrDefault(n => n.Libelle == P(ChampNature));
        if (P(ChampNature).Length > 0 && nature is null)
            return IssueGeste.Refuser($"« {EtapeProcedure} › {ChampNature} » : « {P(ChampNature)} » n'est pas une des dates de l'étape.");
        if ((nature is null) != (date is null))
            return IssueGeste.Refuser($"« {EtapeProcedure} › {ChampNature} » et « {ChampDate} » vont ensemble : la nature dit quelle date de la procédure le jugement remplit.");

        // 6. Le debiteur de l'instance.
        int? idDossier = null, idNonInscrit = null;
        var aDebiteur = false;
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) p.PRT_ID_DSRCS, p.PRT_ID_NINSC
            FROM v2.INSTANCE AS i
            JOIN v2.PARTIE AS p ON p.PRT_ID_INSTN = i.INSTN_ID_INSTN
            JOIN v2.CODE_ACTE_PROCE AS ac ON ac.R_CODEACTEPR_VALEUR = i.INSTN_CODE_ACTE_PROCE
            JOIN v2.NATURE_AFFAIRE AS na ON na.R_NATUREAFFA_VALEUR = i.INSTN_NATURE_AFFAIRE
            WHERE i.INSTN_ID_INSTN = @id AND i.INSTN_TYPE_INSTANCE IN ('08', 'JC', 'PR', 'LS') AND na.R_NATUREAFFA_ORIGINE_ISO = '08'
              AND ((p.PRT_QUALIF_PARTIE LIKE 'D%' AND ac.R_CODEACTEPR_AUTEUR_SAISINE = '1')
                OR (p.PRT_QUALIF_PARTIE LIKE 'F%' AND ISNULL(ac.R_CODEACTEPR_AUTEUR_SAISINE, '') NOT IN ('', '1')))
            ORDER BY LEFT(p.PRT_QUALIF_PARTIE, 1), p.PRT_NUM_ORDRE
            """, ct, new SqlParameter("@id", instance.Id)))
            if (await r.ReadAsync(ct))
            {
                aDebiteur = true;
                idDossier = r.IsDBNull(0) ? null : r.GetInt32(0);
                idNonInscrit = r.IsDBNull(1) ? null : r.GetInt32(1);
            }
        if (await CreationPcV2.DebiteurDePartieAsync(geste, idDossier, idNonInscrit, ct) is not { } debiteur)
            return IssueGeste.Refuser(aDebiteur
                ? $"Le débiteur de l'instance {instance.Role} n'a ni dossier RCS ni non-inscrit (ou son entreprise manque) : la procédure collective ne peut pas être créée."
                : $"Aucun débiteur dans l'instance {instance.Role} (une ouverture de procédure dont l'acte de saisine désigne le débiteur parmi les parties) : la procédure collective ne peut pas être créée.");

        // 7. La PC, rattachee a l'instance.
        var saisie = new CreationPcV2.PcSaisie(debiteur, P(ChampNom), nature, date, Exterieure: false, JuridictionExterieure: null);
        var (pc, refus) = await CreationPcV2.CreerAsync(geste, "NF", saisie, ct, instance);
        if (pc is null) return IssueGeste.Refuser(refus!);

        var client = pc.ClientExistant ? $"client existant {pc.Abrege}" : (pc.Numero is null ? "client compte PC créé" : $"client compte PC {pc.Abrege} créé");
        var quoi = pc.Numero is null
            ? "Procédure collective ouverte sans numéro (aucune date de jugement)"
            : $"Procédure collective {pc.Numero} ouverte";
        return IssueGeste.Ecrit(
            $"{quoi} sur l'instance {instance.Role} dans la base du greffe pour {saisie.NomAffaire} ({debiteur.Numero}), {client} ; " +
            "la facture, les éditions, les établissements, dirigeants et organes ne sont pas repris par la V3.",
            [pc.Demande.Id.ToString(CultureInfo.InvariantCulture)],
            CreationPcV2.Trace(pc, saisie));
    }
}
