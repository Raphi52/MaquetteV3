using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurJonctions;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Jonction d'instances » (PROC_JOIN) : ENREGISTRER LA JONCTION joint une fille a la mere cochee, ou l'en disjoint. Mode
/// d'emploi : EcrivainEcran.cs ; la liste et la fiche : LecteurJonctions.cs. Plan « RIG v3 100 % fonctionnel », lot 4, 26/11/2026.
///
/// SOURCE (ancien RIG) : OPE_JONCTION_FILLES (proc_join.md, Detail OPE). La mere est l'objet du processus ; la fille se designe
/// par son numero de role (TextNumMetier, OPE_JONCTION_FILLES.cs:53-67 ; recherche « Instance=JROP, JRIP, JRCT, JRPC, JRREF,
/// JRJC », ETP_INST_FILLES.cs:25). Le lien est porte par la FILLE seule : INSTN_ID_INSTN_INSTANCE_PRINCIPA, INSTN_DATE_JONCTION,
/// INSTN_TYPE_JONCTION ; la mere n'est jamais ecrite. DoSauver (:150-162) : date presente = jonction ; date absente = les trois
/// colonnes a NULL (disjonction, bouton BtnDisjoindre :141-146) ; puis Instance.Sauve de la fille.
///
/// CONTROLES DE RIG v2, dans l'ordre, chacun a son message AU MOT PRES :
///   1. une seule mere (MetierBase du processus) ; elle n'est pas tenue par une autre demande (instance.Lock, PROC_JOIN.cs:49) ;
///   2. le numero designe une seule fille : « Aucune instance trouvée portant ce numéro. » / « Plusieurs instances correspondent
///      à ce numéro. Effectuer une recherche pour charger l'instance souhaitée. » (:59-60) ;
///   3. jonction : « Vous ne pouvez pas joindre une instance sur elle même. » (SetInstance, :71-72) ; _VerifierDejaJointe
///      (:84-106) : « Cette instance est déjà jointe à l'instance &lt;role&gt; », puis, si la mere est elle-meme jointe, la parente
///      croisee (_PossedeAncetreInstance, :108-116) « Cette instance est déjà liée à l'instance principale. ( &lt;fille&gt; :
///      &lt;mere&gt;) » et « Cette instance est déjà la mère de l'instance &lt;mere&gt; » ;
///   4. DoVerifier (:163-195) : un type sans date (« Attention, vous avez valorisé un type de jonction mais pas de date. Si vous
///      continuez, la jonction ne sera pas sauvegardée. » : un avertissement en RIG v2, ici un refus, rien n'est sauve), la date
///      de jonction obligatoire (DateJonction.MetierObligatoire), puis jamais avant la date de l'acte de la fille, ni de la mere
///      (« La date de saisine de l'instance n°&lt;role&gt; est supérieur à la date de jonction &lt;date&gt;. »).
/// Une fille deja jointe a CETTE mere se rouvre (SetPhase la pre-charge, PROC_JOIN.cs:71-72) : sa date et son type se changent.
/// Disjoindre une fille qui n'est pas jointe a cette mere est refuse (RIG v2 n'offre le bouton que sur une fille jointe, :134).
///
/// CE QUI S'ECRIT : les trois colonnes de jonction de la fille (v2.INSTANCE), le type par sa valeur de v2.TYPE_JONCTION (la liste
/// de l'ecran porte le libelle), puis les effets de Instance.Sauve (SauveInstanceV2.cs). Trace : famille « affaire »,
/// « a modifie », mere, date et type avant et apres.
/// NON REPRIS (nomme) : la recopie des parties de la fille vers la mere (OPE_INST_PARTIES_FILLES.cs:42-53 : une grille ou chaque
/// partie se coche, avec son qualifiant de copie -- l'ecran n'a pas cette grille) ; la demande JOIN de RIG v2 (DMND_NUM_INDICATIF,
/// DMND_LES_NUMEROS, PROC_JOIN.cs:52-53), une trace sans facture (MetierFacture = false, :21) que le journal des gestes remplace.
/// </summary>
internal sealed class EcrivainJonctions : IEcrivainEcran
{
    public string Procedure => "PROC_JOIN";
    public string Ecran => "Jonction d'instances";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Confirmer];

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        // 1. Une mere, libre.
        if (geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray() is not [var id])
            return IssueGeste.Refuser("Une jonction se fait sur UNE instance mère : cocher une seule instance.");
        if (await LireMereAsync(geste.Base, geste.Transaction, id, ct) is not { } mere)
            return IssueGeste.Refuser($"Instance introuvable dans la base du greffe ({id}).");
        if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance, id, ct) is { } verrou)
            return IssueGeste.Refuser(verrou);

        // 2. La fille, par son numero de role.
        var role = geste.Valeur(SectionFille, ChampRoleFille)?.Replace(" ", "", StringComparison.Ordinal) ?? "";
        var disjoindre = geste.Valeur(SectionDisjonction, ChampDisjoindre) == "oui";
        var typeSaisi = geste.Valeur(SectionFille, ChampType)?.Trim() ?? "";
        var dateSaisie = geste.Valeur(SectionFille, ChampDate)?.Trim() ?? "";
        if (role.Length == 0) return IssueGeste.Refuser($"« {ChampRoleFille} » est obligatoire.");
        var trouvees = await FillesParRoleAsync(geste.Base, geste.Transaction, role, ct);
        if (trouvees.Count == 0) return IssueGeste.Refuser("Aucune instance trouvée portant ce numéro.");
        if (trouvees.Count > 1) return IssueGeste.Refuser("Plusieurs instances correspondent à ce numéro. Effectuer une recherche pour charger l'instance souhaitée.");
        var fille = trouvees[0];

        var avant = await EtatJonctionAsync(geste, fille, ct);
        if (disjoindre)
        {
            if (typeSaisi.Length > 0 || dateSaisie.Length > 0)
                return IssueGeste.Refuser($"Cocher « {ChampDisjoindre} » OU saisir la date et le type de jonction, pas les deux.");
            if (fille.IdMere != mere.Id)
                return IssueGeste.Refuser($"L'instance {fille.Role} n'est pas jointe à l'instance {mere.Role} : rien à disjoindre.");
            await EcrireAsync(geste, fille.Id, null, null, null, ct);
            return IssueGeste.Ecrit($"Instance {fille.Role} disjointe de l'instance {mere.Role} dans la base du greffe.", [id],
                new TraceGeste("affaire", $"l'instance {fille.Role}", fille.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                    avant, new Dictionary<string, string> { ["Mère"] = "", [ChampDate] = "", [ChampType] = "" }));
        }

        // 3. Les controles de la selection de la fille.
        if (fille.Id == mere.Id) return IssueGeste.Refuser("Vous ne pouvez pas joindre une instance sur elle même.");
        if (fille.IdMere is { } idMereFille && idMereFille != mere.Id
            && await LireParIdAsync(geste.Base, geste.Transaction, idMereFille, ct) is { } autreMere)
            return IssueGeste.Refuser("Cette instance est déjà jointe à l'instance " + autreMere.Role);
        if (mere.IdMere is not null)
        {
            if (await PossedeAncetreAsync(geste, fille, mere.Id, ct) || await PossedeAncetreAsync(geste, mere, fille.Id, ct))
                return IssueGeste.Refuser("Cette instance est déjà liée à l'instance principale. ( " + fille.Role + " : " + mere.Role + ")");
            if (mere.IdMere == fille.Id) return IssueGeste.Refuser("Cette instance est déjà la mère de l'instance " + mere.Role);
        }

        // 4. DoVerifier : type sans date, date obligatoire, puis les dates des actes (fille, puis mere).
        if (dateSaisie.Length == 0 && typeSaisi.Length > 0)
            return IssueGeste.Refuser("Attention, vous avez valorisé un type de jonction mais pas de date. Si vous continuez, la jonction ne sera pas sauvegardée.");
        if (dateSaisie.Length == 0) return IssueGeste.Refuser($"« {ChampDate} » est obligatoire.");
        if (!DateTime.TryParseExact(dateSaisie, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return IssueGeste.Refuser($"« {ChampDate} » : date illisible ({dateSaisie}).");
        foreach (var i in new[] { fille, mere })
            if (i.DateActe is { } acte && date < acte)
                return IssueGeste.Refuser($"La date de saisine de l'instance n°{i.Role} est supérieur à la date de jonction {date.ToString("d", Francais)}.");
        string? type = null;
        if (typeSaisi.Length > 0)
        {
            await using var r = await geste.LireAsync("SELECT TOP (1) R_TYPEJONCTI_VALEUR FROM v2.TYPE_JONCTION WHERE R_TYPEJONCTI_LIBELLE_ECRAN = @libelle",
                                                      ct, new SqlParameter("@libelle", typeSaisi));
            if (!await r.ReadAsync(ct)) return IssueGeste.Refuser($"« {ChampType} » : « {typeSaisi} » n'est pas un type de jonction du greffe.");
            type = FormeLigne.Texte(r, "R_TYPEJONCTI_VALEUR");
        }

        await EcrireAsync(geste, fille.Id, mere.Id, date, type, ct);
        return IssueGeste.Ecrit(
            $"Instance {fille.Role} jointe à l'instance {mere.Role} le {date.ToString("d", Francais)}{(typeSaisi.Length > 0 ? $" ({typeSaisi})" : "")} dans la base du greffe.",
            [id],
            new TraceGeste("affaire", $"l'instance {fille.Role}", fille.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                avant, new Dictionary<string, string> { ["Mère"] = mere.Role, [ChampDate] = FormeLigne.DateFiche(date), [ChampType] = typeSaisi }));
    }

    /// <summary>_PossedeAncetreInstance (OPE_JONCTION_FILLES.cs:108-116) : la chaine des meres de <paramref name="instance"/>
    /// atteint-elle <paramref name="ancetre"/> ? Une chaine qui boucle (base incoherente) s'arrete au premier retour.</summary>
    private static async Task<bool> PossedeAncetreAsync(GesteEnCours geste, InstanceJointe instance, int ancetre, CancellationToken ct)
    {
        var vues = new HashSet<int>();
        var courante = instance;
        while (courante.IdMere is { } idMere && vues.Add(courante.Id))
        {
            if (idMere == ancetre) return true;
            if (await LireParIdAsync(geste.Base, geste.Transaction, idMere, ct) is not { } m) return false;
            courante = m;
        }
        return false;
    }

    private static async Task<Dictionary<string, string>> EtatJonctionAsync(GesteEnCours geste, InstanceJointe fille, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT m.INSTN_NUM_ROLE AS Mere, f.INSTN_DATE_JONCTION AS DateJonction,
                   COALESCE(tj.R_TYPEJONCTI_LIBELLE_ECRAN, f.INSTN_TYPE_JONCTION) AS TypeJonction
            FROM v2.INSTANCE AS f
            LEFT JOIN v2.INSTANCE AS m ON m.INSTN_ID_INSTN = f.INSTN_ID_INSTN_INSTANCE_PRINCIPA
            LEFT JOIN v2.TYPE_JONCTION AS tj ON tj.R_TYPEJONCTI_VALEUR = f.INSTN_TYPE_JONCTION
            WHERE f.INSTN_ID_INSTN = @id
            """, ct, new SqlParameter("@id", fille.Id));
        await r.ReadAsync(ct);
        return new Dictionary<string, string>
        {
            ["Mère"] = FormeLigne.Texte(r, "Mere"), [ChampDate] = FormeLigne.DateFiche(FormeLigne.DateDe(r, "DateJonction")),
            [ChampType] = FormeLigne.Texte(r, "TypeJonction"),
        };
    }

    private static async Task EcrireAsync(GesteEnCours geste, int fille, int? mere, DateTime? date, string? type, CancellationToken ct)
    {
        var touchees = await geste.EcrireAsync($"""
            UPDATE v2.INSTANCE SET INSTN_ID_INSTN_INSTANCE_PRINCIPA = @mere, INSTN_DATE_JONCTION = @date, INSTN_TYPE_JONCTION = @type,
                   {SauveInstanceV2.Effets}
            WHERE INSTN_ID_INSTN = @id
            """, ct,
            [new SqlParameter("@mere", (object?)mere ?? DBNull.Value), new SqlParameter("@date", (object?)date ?? DBNull.Value),
             new SqlParameter("@type", (object?)type ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.VarChar, Size = 5 },
             new SqlParameter("@id", fille), .. SauveInstanceV2.Parametres(geste.Maintenant)]);
        if (touchees != 1) throw new InvalidOperationException($"Instance {fille} : {touchees} ligne(s) mise(s) a jour au lieu d'une.");
    }
}
