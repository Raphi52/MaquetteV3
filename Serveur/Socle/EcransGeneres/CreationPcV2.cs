using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// CREER UNE PROCEDURE COLLECTIVE COMME RIG v2 (plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026) : le debiteur, le numero
/// de PC, le client « compte PC » et la ligne v2.PROCEDURE_COLLECTIVE. Ecrivain : EcrivainNouvellesProcedures.cs (PROC_NPC).
///
/// SOURCE (ancien RIG) :
///   - LE DEBITEUR : un dossier RCS (rec.DossierRcs, numero de gestion) ou, a defaut, un non-inscrit (v2.NON_INSCRIT) -- la
///     PC en recoit l'entreprise (PRCLL_ID_RPRTR, NOT NULL sur RIG_DEV) et le lien (PRCLL_ID_DSRCS / PRCLL_ID_NINSC) :
///     CreateProcedureCollective(DossierRCS | NonInscrit), ProcedureCollective.cs:2870-2899, setters :1342 et :1538 ;
///   - LES DATES : une PC porte la date de son jugement dans UNE des quatre colonnes que l'etape saisit (ouverture,
///     sauvegarde, retablissement professionnel, traitement de sortie de crise : OPE_PROCEDURE_COLLECTIVE.cs:194-197) ; son
///     ETAT et son TYPE en sont recalcules a la sauvegarde (recupEtatPC / recupTypePC, ProcedureCollective.cs:2961-2965,
///     :3189-3262) : ouverture → etat 1, type 1 (6 avant 1986, 9 avant le 14/07/1967) ; sortie de crise → 15 ; sauvegarde
///     → 10 ; retablissement professionnel → 14. Etat info 1 (processusRIG, Metier.cs:16 ; Sauve :2973-2979), compteur de
///     mise a jour 1 (cree a 0, :2905, puis +1 a la sauvegarde, :2968-2969), numero source GUID (:2906) ;
///   - LE NUMERO : a la preparation de la sauvegarde, si la PC n'en a pas (OPE_PROCEDURE_COLLECTIVE.cs:1076-1108) : serie
///     JPC, ou JPCEX pour une PC exterieure ; annee de la premiere date posee parmi retablissement professionnel, sauvegarde,
///     sortie de crise, ouverture ; SANS date, pas de numero (la PC s'enregistre sans) ;
///   - LE CLIENT : la PC neuve recoit un client « compte PC » cree depuis son entreprise (OPE_FACTU_GESTCOM.cs:1128-1129,
///     Client.CreateClientCompteFromEntreprise, Client.cs:907-975) : assujetti TVA, groupe PC, mode de paiement CH, compte
///     41100199, releve de facture, statut A, type C, compte PC ; nom = la designation (personne morale : civilite « PM »,
///     Entreprise.cs:317, :303), sinon « civilite designation » ; bloc-notes « Client crée automatiquement dans la demande
///     <n° de demande> » ; abrege = les 8 derniers caracteres du numero de demande (Client.cs:936-940), puis du numero de PC
///     une fois donne -- et si un client porte deja cet abrege, la PC prend CE client (OPE_PROCEDURE_COLLECTIVE.cs:1099-1105) ;
///     son adresse : celle de l'entreprise, sinon refus « Impossible de trouver l'adresse du client à facturer » (Client.cs:958-963),
///     recopiee en CLIENT_ADRESSE type P, par defaut (ClientAdresse.cs:22-47, Client.cs:687-697) ;
///   - LA DEMANDE : validee, nom = nom de l'affaire (NomDemande, ProcedureCollective.cs:538-541), indicatif et numeros = numero
///     de PC (PROC_NPC.cs:150-154), sequenceur 2P (ProcedureCollective.cs:812).
///
/// NON REPRIS (nommes ; le message du geste le dit) : la facture (articles NPC, PROC_NPC.cs:90-91) et les editions ; les
/// etablissements et dirigeants de la PC copies de l'entreprise (UpdateEtablissementsPcAndDirigeantsPcOEntrepriseChanged,
/// ProcedureCollective.cs:3490-3600) et l'adresse de la PC qui en vient ; le nom d'affaire propose avec la forme juridique
/// (RecupNomAvecFormeJuridique, :3578-3590) -- il se saisit ; les organes, les chiffres, les evenements, la note ; l'historique
/// des etats (PC_ETAT_HISTO) ; la prevention (mode NPR).
/// </summary>
internal static class CreationPcV2
{
    /// <summary>La nature du jugement : la colonne de date qu'elle remplit, l'etat et le type que la V2 en calcule.</summary>
    internal sealed record Nature(string Libelle, string Colonne, string Etat, int RangAnnee);

    /// <summary>Les quatre dates de l'etape « Procédure collective » (OPE_PROCEDURE_COLLECTIVE.cs:194-197), au libelle de l'ecran.
    /// RangAnnee : l'ordre dans lequel la numerotation lit les dates (OPE_PROCEDURE_COLLECTIVE.cs:1084-1092).</summary>
    internal static readonly Nature[] Natures =
    [
        new("Ouverture (redressement ou liquidation)", "PRCLL_DATE_OUVERTURE", "1", 4),
        new("Sauvegarde", "PRCLL_DATE_SAUVEGARDE", "10", 2),
        new("Rétablissement professionnel", "PRCLL_DATE_RETABL_PROF", "14", 1),
        new("Traitement de sortie de crise", "PRCLL_DATE_TRAITEMENT_SORTIE_CRISE", "15", 3),
    ];

    /// <summary>Le type de PC que recupTypePC donne pour la seule date <paramref name="n"/> (ProcedureCollective.cs:3237-3252).</summary>
    internal static string TypePc(Nature n, DateTime date) => n.Colonne switch
    {
        "PRCLL_DATE_OUVERTURE" when date < new DateTime(1967, 7, 14) => "9",
        "PRCLL_DATE_OUVERTURE" when date < new DateTime(1986, 1, 1) => "6",
        "PRCLL_DATE_OUVERTURE" => "1",
        _ => n.Etat,
    };

    /// <summary>Le debiteur : l'entreprise et le dossier RCS ou le non-inscrit qui la porte.</summary>
    internal sealed record Debiteur(bool EstDossier, int Id, string Numero, int IdEntreprise, string Civilite, string Designation, int? IdAdresse);

    /// <summary>
    /// Le debiteur de numero <paramref name="numero"/> : un dossier RCS par son numero de gestion, sinon un non-inscrit par son
    /// numero (l'ordre de la recherche de RIG v2, comme DepotsRcsV2.TrouverAsync) ; null si rien ne le porte, ou si son
    /// entreprise manque (PRCLL_ID_RPRTR est obligatoire).
    /// </summary>
    internal static async Task<Debiteur?> DebiteurAsync(GesteEnCours geste, string numero, CancellationToken ct)
    {
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) d.Id, d.NumeroGestion, e.Id AS IdEntreprise, e.Civilite, e.Denomination, e.IdAdresse
            FROM rec.DossierRcs AS d JOIN rec.Entreprise AS e ON e.Id = d.IdEntreprise
            WHERE d.NumeroGestion = @n ORDER BY d.Id
            """, ct, new SqlParameter("@n", numero)))
            if (await r.ReadAsync(ct)) return Lire(r, true, "Id", "NumeroGestion");
        await using (var r = await geste.LireAsync("""
            SELECT TOP (1) n.NINSC_ID_NINSC, n.NINSC_NUM_NON_INSCRIT, e.Id AS IdEntreprise, e.Civilite, e.Denomination, e.IdAdresse
            FROM v2.NON_INSCRIT AS n JOIN rec.Entreprise AS e ON e.Id = n.NINSC_ID_RPRTR
            WHERE n.NINSC_NUM_NON_INSCRIT = @n ORDER BY n.NINSC_ID_NINSC
            """, ct, new SqlParameter("@n", numero)))
            if (await r.ReadAsync(ct)) return Lire(r, false, "NINSC_ID_NINSC", "NINSC_NUM_NON_INSCRIT");
        return null;
    }

    private static Debiteur Lire(SqlDataReader r, bool estDossier, string colonneId, string colonneNumero)
        => new(estDossier, Convert.ToInt32(r[colonneId], CultureInfo.InvariantCulture), FormeLigne.Texte(r, colonneNumero),
               Convert.ToInt32(r["IdEntreprise"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "Civilite"),
               FormeLigne.Texte(r, "Denomination"), r["IdAdresse"] is int a ? a : null);

    /// <summary>Le refus d'un numero qui ne designe aucun debiteur.</summary>
    internal static string DebiteurIntrouvable(string numero)
        => $"Aucun dossier RCS ni non-inscrit ne porte le numéro « {numero} » dans la base du greffe (ou son entreprise manque).";

    /// <summary>Ce que la saisie donne pour la PC.</summary>
    internal sealed record PcSaisie(Debiteur Debiteur, string NomAffaire, Nature? Nature, DateTime? Date, bool Exterieure,
                                    string? JuridictionExterieure);

    /// <summary>La PC creee : son id, son numero (null sans date), son client.</summary>
    internal sealed record PcCreee(int Id, string? Numero, int IdClient, string Abrege, bool ClientExistant, DemandeCreee Demande);

    /// <summary>
    /// ECRIT la demande <paramref name="codeProcessus"/> validee, le numero de PC, le client « compte PC » (ou celui qui porte
    /// deja l'abrege), son adresse, la PC, puis rattache la demande a la PC. Un refus : rien d'ecrit (la transaction du geste
    /// annule tout).
    /// </summary>
    internal static async Task<(PcCreee? Pc, string? Refus)> CreerAsync(GesteEnCours geste, string codeProcessus, PcSaisie s,
                                                                        CancellationToken ct)
    {
        // L'adresse du client a facturer, avant toute ecriture (Client.cs:958-963).
        if (s.Debiteur.IdAdresse is not { } idAdresse)
            return (null, "Impossible de trouver l'adresse du client à facturer.");
        string? numero = null;
        if (s.Nature is not null && s.Date is { } date)
        {
            var (n, refus) = await NumeroAsync(geste, s.Exterieure ? "JPCEX" : "JPC", date.Year, ct);
            if (n is null) return (null, refus);
            numero = n;
        }

        var (demande, refusDemande) = await CreerDemandeAsync(geste, codeProcessus, "V", ct);
        if (demande is null) return (null, refusDemande);

        var source = numero ?? demande.Numero;
        var abrege = source.Length >= 8 ? source[^8..] : $"ID:{demande.Id}";
        int? existant = null;
        if (numero is not null)
            await using (var r = await geste.LireAsync("SELECT TOP (1) Id FROM rec.Client WHERE Abrege = @a ORDER BY Id", ct,
                             new SqlParameter("@a", abrege)))
                if (await r.ReadAsync(ct)) existant = r.GetInt32(0);
        var idClient = existant ?? await CreerClientAsync(geste, s.Debiteur, abrege, demande.Numero, idAdresse, ct);

        var valeurs = new Dictionary<string, object?>
        {
            ["PRCLL_NUM_PC"] = numero, ["PRCLL_ID_RPRTR"] = s.Debiteur.IdEntreprise, ["PRCLL_ID_CLIEN"] = idClient,
            [s.Debiteur.EstDossier ? "PRCLL_ID_DSRCS" : "PRCLL_ID_NINSC"] = s.Debiteur.Id,
            ["PRCLL_NOM_AFFAIRE"] = s.NomAffaire, ["PRCLL_EXTERIEUR"] = s.Exterieure,
            ["PRCLL_JURIDICTION_EXTERIEURE"] = s.Exterieure ? s.JuridictionExterieure : null,
            ["PRCLL_ETAT_PC"] = s.Nature is { } n2 && s.Date is not null ? n2.Etat : "N",
            ["PRCLL_TYPE_PC"] = s.Nature is { } n3 && s.Date is { } d3 ? TypePc(n3, d3) : "0",
            ["PRCLL_ETAT_INFO"] = "1", ["PRCLL_DATE_MAJ"] = geste.Maintenant, ["PRCLL_COMPTEUR_MAJ"] = (short)1,
            ["PRCLL_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
        };
        if (s.Nature is { } n4 && s.Date is { } d4) valeurs[n4.Colonne] = d4.Date;
        var id = await InsererAsync(geste, "v2.PROCEDURE_COLLECTIVE", "PRCLL_ID_PRCLL", valeurs, ct);

        await RattacherAsync(geste, demande, s.NomAffaire, numero ?? "", numero ?? "", DepotsPcV2.SequenceurPc,
            id.ToString(CultureInfo.InvariantCulture), ct);
        return (new PcCreee(id, numero, idClient, abrege, existant is not null, demande), null);
    }

    /// <summary>Le client « compte PC » de Client.CreateClientCompteFromEntreprise (Client.cs:907-975) et son adresse par defaut.</summary>
    private static async Task<int> CreerClientAsync(GesteEnCours geste, Debiteur d, string abrege, string numeroDemande, int idAdresse,
                                                    CancellationToken ct)
    {
        var nom = d.Civilite == "PM" ? d.Designation : $"{d.Civilite.Trim()} {d.Designation}";
        var id = await geste.InsererAsync("rec.Client", "Id", new Dictionary<string, object?>
        {
            ["Abrege"] = abrege, ["NumeroCompte"] = "41100199", ["Nom"] = nom.Length > 255 ? nom[..255] : nom, ["Type"] = "C",
            ["Civilite"] = "", ["Statut"] = "A", ["Groupe"] = "PC", ["ModePaiement"] = "CH", ["AssujettiTva"] = true,
            ["ReleveFacture"] = true, ["EstComptePc"] = true, ["EstCompteWeb"] = false, ["DateDerniereModification"] = geste.Maintenant,
            ["BlocNotes"] = $"Client crée automatiquement dans la demande {numeroDemande}",
        }, ct);

        // CreateClientAdresse (ClientAdresse.cs:22-47) : ligne = numero, complement, type de voie, nom de voie (« . » si vide),
        // puis « BP » ; localite suivie de « CEDEX » ; type P ; la premiere adresse d'un client est celle par defaut.
        // Le libelle du type de voie vient de v2.TYPE_VOIE (R_TYPEVOIE_LIBELLE_ECRAN) quand la base la porte, sinon son code.
        var typesVoie = false;
        await using (var t = await geste.LireAsync("SELECT CONVERT(bit, CASE WHEN OBJECT_ID(N'v2.TYPE_VOIE') IS NULL THEN 0 ELSE 1 END)", ct))
            if (await t.ReadAsync(ct)) typesVoie = t.GetBoolean(0);
        var libelleVoie = typesVoie
            ? "(SELECT TOP (1) tv.R_TYPEVOIE_LIBELLE_ECRAN FROM v2.TYPE_VOIE AS tv WHERE tv.R_TYPEVOIE_VALEUR = a.TypeVoie)"
            : "a.TypeVoie";
        Dictionary<string, object?> adresse;
        await using (var r = await geste.LireAsync($"""
            SELECT a.NumeroVoie, a.ComplementNumero, {libelleVoie} AS LibelleVoie, a.NomVoie, a.CodePostal, a.Localite, a.Pays,
                   a.SuiteAdresse, a.BoitePostale, a.Cedex
            FROM rec.Adresse AS a WHERE a.Id = @a
            """, ct, new SqlParameter("@a", idAdresse)))
        {
            await r.ReadAsync(ct);
            string T(string c) => FormeLigne.Texte(r, c).Trim();
            var ligne = string.Join(" ", new[] { T("NumeroVoie"), T("ComplementNumero"), T("LibelleVoie"), T("NomVoie") }.Where(x => x.Length > 0));
            if (ligne.Length == 0) ligne = ".";
            if (T("BoitePostale").Length > 0) ligne += " BP " + T("BoitePostale");
            var localite = T("Localite") + (T("Cedex").Length > 0 ? " CEDEX " + T("Cedex") : "");
            adresse = new Dictionary<string, object?>
            {
                ["IdClient"] = id, ["IdAdresse"] = idAdresse, ["TypeAdresse"] = "P", ["ParDefaut"] = true,
                ["LigneAdresse"] = ligne, ["SuiteAdresse"] = T("SuiteAdresse"), ["CodePostal"] = T("CodePostal"),
                ["Localite"] = localite, ["Pays"] = T("Pays"),
            };
        }
        await geste.InsererAsync("rec.ClientAdresse", "Id", adresse, ct);
        return id;
    }

    /// <summary>La juridiction exterieure <paramref name="code"/> si la table de reference la porte (ULT_COMBO_JURIDICTION_EXTERIEURE.cs:17).</summary>
    internal static async Task<bool> JuridictionExisteAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT 1 FROM v2.JURIDICTION_EXTERIEURE WHERE R_JURIDICTIO_VALEUR = @c", ct,
            new SqlParameter("@c", code));
        return await r.ReadAsync(ct);
    }

    /// <summary>La trace de la PC creee (famille « affaire » du journal des gestes : celle des affaires judiciaires).</summary>
    internal static TraceGeste Trace(PcCreee pc, PcSaisie s) =>
        new("affaire", $"procédure collective {pc.Numero ?? "sans numéro"}", pc.Id.ToString(CultureInfo.InvariantCulture), "a cree", null,
            new { Pc = pc.Numero, s.NomAffaire, Debiteur = s.Debiteur.Numero, Nature = s.Nature?.Libelle, s.Exterieure,
                  Juridiction = s.JuridictionExterieure, Client = pc.Abrege, pc.ClientExistant, Demande = pc.Demande.Numero });
}
