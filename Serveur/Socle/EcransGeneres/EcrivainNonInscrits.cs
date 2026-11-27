using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.CreationV2;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Non-inscrits » (PROC_CNI) : CRÉER un non-inscrit, comme la validation de RIG v2. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurNonInscrits.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// SOURCE (ancien RIG) : PROC_CNI.cs (DoInitObject : pays FRA par defaut, date de travail WINS) et OPE_CNI
/// (Source\RIG\DLL\EtapeRCS\Rig_Etape_Dossier_Rcs\ETP_CNI\OPE_CNI.cs, OPE_CNI.Designer.cs).
/// CONTROLES, avant toute ecriture, dans l'ordre du moteur (champs obligatoires, puis DoVerifier) :
///   1. obligatoires (MetierObligatoire, Designer l. 123, 168, 178, 198) : le type du non-inscrit, la civilite, l'adresse de
///      l'etablissement ; personne MORALE (civilite non « toujours PP ») : la denomination (l. 97) ; personne PHYSIQUE : le patronyme
///      et les prenoms (_Rafraichir, OPE_CNI.cs:272-274), la forme juridique (« ultFormeJuridique.MetierObligatoire = !isPM », l. 270) et,
///      pour la civilite MME, le nom d'usage (l. 402) ;
///   2. le type est une valeur de TYPE_NON_INSCRIT et la civilite une valeur de CIVILITE (les listes des Ult), si la base les a ;
///   3. DoVerifier (l. 104-118) : un SIREN deja connu d'un dossier RCS, d'une entreprise ou d'un non-inscrit refuse la creation, au mot
///      pres (la V3 n'ouvre pas PROC_CNI sur une entreprise choisie : _idEntreprise = -1).
/// CE QUI S'ECRIT (DoSauve, l. 119-177), dans la transaction du geste :
///   - l'adresse (rec.Adresse : voie, code postal, ville) ;
///   - l'entreprise representante (Entreprise.CreateEntreprise(NonInscrit), RigMetier\_Dossier\Entreprise.cs) : civilite, designation
///     sans civilite (Metier.cs:329-340 : nom d'usage ou patronyme, prenoms, « né(e) » patronyme s'il differe), patronyme, prenom, SIREN,
///     forme juridique, nom d'usage, etat 1, adresse ;
///   - le non-inscrit (v2.NON_INSCRIT) : etat 1, numero RGNI de l'annee de la date de travail WINS (DateTravail._GetDateTravail,
///     RigMetier\Divers\DateTravail.cs:19-45 : la date de WINS si elle n'est pas automatique, sinon le jour), principal, non inscrit au
///     registre des metiers, date de mise a jour, PM derive de la civilite ; les champs de l'autre nature a NULL (l. 131-135) ;
///   - l'etablissement principal (v2.ETABLISSEMENT : ordre 1, adresse NQ, l. 145-151) et, pour un type JUD ou PCOL, le second « NQS »
///     commercial a la meme adresse, ordre 1 (l. 155-167) ;
///   - la demande CNI validee, au nom de la personne (l. 173-176 : « abrégé civilité patronyme prénoms » ou la denomination).
/// NON REPRIS (nommes) : l'appel au service InfoGreffe (_RecupererDonneesInfoGreffe, l. 288-388 -- service externe, jamais appele) ;
/// l'ouverture sur une entreprise existante et les parametres d'appel (IdEntreprise, ModeFonctionnementCNI « hors ressort »,
/// l. 184-252 : le menu ouvre la creation simple) ; la commune et l'adresse de domicile du bloc naissance (ope_Naissance, non
/// obligatoire) ; l'indexation des mots directeurs apres validation (MotsDirecteurs.IndexerNonInscrit, l. 181).
/// TRACE : famille « dossier », « a cree », le non-inscrit et son numero.
/// </summary>
internal sealed class EcrivainNonInscrits : IEcrivainEcran
{
    internal const string EtapeIdentite = "Type et identité", EtapeNaissance = "Naissance", EtapeAdresse = "Adresse";
    internal const string ChampType = "Type de non-inscrit", ChampCivilite = "Civilité", ChampPatronyme = "Nom de naissance", ChampPrenoms = "Prénoms",
                          ChampNomUsage = "Nom d'usage", ChampDenomination = "Dénomination", ChampForme = "Forme juridique", ChampSiren = "SIREN",
                          ChampDateNaissance = "Date de naissance", ChampDepartement = "Département", ChampNationalite = "Nationalité",
                          ChampPays = "Pays de naissance", ChampAdresse = "Adresse", ChampCodePostal = "Code postal", ChampVille = "Ville";

    public string Procedure => "PROC_CNI";
    public string Ecran => "Non-inscrits";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Creer];

    private sealed record Civilite(string Valeur, string AbregeEdit, bool ToujoursPp, bool Masculin, bool Feminin);

    private static string? Vide(string v) => v.Length == 0 ? null : v;

    /// <summary>La premiere valeur de <paramref name="sql"/> pour <c>@v</c> ; null si aucune.</summary>
    private static async Task<string?> PremiereAsync(GesteEnCours geste, string sql, string valeur, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(sql, ct, new SqlParameter("@v", valeur));
        return await r.ReadAsync(ct) && !r.IsDBNull(0) ? Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!.Trim() : null;
    }

    private static async Task<bool> TableAsync(GesteEnCours geste, string table, CancellationToken ct)
        => await PremiereAsync(geste, "SELECT OBJECT_ID(@v)", table, ct) is not null;

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string etape, string champ) => Saisi(geste, etape, champ);
        // 1. Les obligatoires communs (type, civilite, adresse de l'etablissement).
        foreach (var (etape, champ) in new[] { (EtapeIdentite, ChampType), (EtapeIdentite, ChampCivilite), (EtapeAdresse, ChampAdresse) })
            if (S(etape, champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{etape} › {champ}"));

        // 2. Le type et la civilite dans leurs listes.
        var saisiType = S(EtapeIdentite, ChampType);
        var type = saisiType;
        if (await TableAsync(geste, "v2.TYPE_NON_INSCRIT", ct))
            type = await PremiereAsync(geste, """
                       SELECT TOP (1) R_TYPENONINS_VALEUR FROM v2.TYPE_NON_INSCRIT WHERE R_TYPENONINS_VALEUR = @v OR R_TYPENONINS_LIBELLE_ECRAN = @v
                       ORDER BY R_TYPENONINS_VALEUR
                       """, saisiType, ct)
                   ?? "";
        if (type.Length == 0) return IssueGeste.Refuser($"« {ChampType} » : « {saisiType} » n'est pas un type de non-inscrit de la table TYPE_NON_INSCRIT.");
        var saisiCivilite = S(EtapeIdentite, ChampCivilite);
        Civilite? civilite = null;
        await using (var r = await geste.LireAsync("""
                         SELECT TOP (1) R_CIVILITE_VALEUR, ISNULL(R_CIVILITE_ABREGE_EDIT, ''), ISNULL(R_CIVILITE_TOUJOURS_PP, 0),
                                ISNULL(R_CIVILITE_SEXE_MASCULIN, 0), ISNULL(R_CIVILITE_SEXE_FEMININ, 0)
                         FROM v2.CIVILITE WHERE R_CIVILITE_VALEUR = @v OR R_CIVILITE_LIBELLE_ECRAN = @v ORDER BY R_CIVILITE_VALEUR
                         """, ct, new SqlParameter("@v", saisiCivilite)))
            if (await r.ReadAsync(ct))
                civilite = new Civilite(r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4));
        if (civilite is null) return IssueGeste.Refuser($"« {ChampCivilite} » : « {saisiCivilite} » n'est pas une civilité de la table CIVILITE.");
        var pp = civilite.ToujoursPp;

        // 1 bis. Les obligatoires de la nature (PP ou PM), dans l'ordre des Ult (l. 97, 168, 272-274, 402).
        var patronyme = S(EtapeIdentite, ChampPatronyme);
        var prenoms = S(EtapeIdentite, ChampPrenoms);
        var nomUsage = S(EtapeIdentite, ChampNomUsage);
        var denomination = S(EtapeIdentite, ChampDenomination);
        var forme = S(EtapeIdentite, ChampForme);
        var obligatoires = pp
            ? new List<string> { ChampPatronyme, ChampPrenoms, ChampForme }
            : new List<string> { ChampDenomination };
        if (pp && civilite.Valeur == "MME") obligatoires.Add(ChampNomUsage);
        foreach (var champ in obligatoires)
            if (S(EtapeIdentite, champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{EtapeIdentite} › {champ}"));
        var saisieNaissance = S(EtapeNaissance, ChampDateNaissance);
        DateTime? naissance = saisieNaissance.Length == 0 ? null : Date(saisieNaissance);
        if (saisieNaissance.Length > 0 && naissance is null)
            return IssueGeste.Refuser($"« {EtapeNaissance} › {ChampDateNaissance} » : « {saisieNaissance} » n'est pas une date.");

        // 3. DoVerifier : un SIREN deja connu (dossier RCS, entreprise, non-inscrit) refuse la creation.
        var siren = S(EtapeIdentite, ChampSiren).Replace(" ", "", StringComparison.Ordinal);
        if (siren.Length > 0 && await PremiereAsync(geste, """
                SELECT TOP (1) 1 FROM (SELECT Siren AS s FROM rec.DossierRcs UNION ALL SELECT Siren FROM rec.Entreprise
                                       UNION ALL SELECT NINSC_NUM_SIREN FROM v2.NON_INSCRIT) AS x WHERE x.s = @v
                """, siren, ct) is not null)
            return IssueGeste.Refuser(string.Format(CultureInfo.InvariantCulture,
                "Une entreprise existe déjà pour le siren {0}, vous devez créer votre non inscrit sur cette entreprise. Quittez le processus actuel, " +
                "lancez une recherche par siren puis choisissez \"CNI sur entreprise sélectionnée\".", siren));

        // Les donnees inutiles de l'autre nature ne s'enregistrent pas (l. 131-135).
        if (pp) denomination = "";
        else { patronyme = ""; prenoms = ""; nomUsage = ""; forme = ""; }

        // La date de travail WINS (DateTravail._GetDateTravail) : son annee numerote le non-inscrit.
        var travail = await TableAsync(geste, "v2.DATE_TRAVAIL", ct)
            ? await PremiereAsync(geste, """
                  SELECT TOP (1) CONVERT(varchar(10), CASE WHEN DTTRV_AUTOMATIQUE <> 1 THEN DTTRV_DATE_TRAVAIL END, 23)
                  FROM v2.DATE_TRAVAIL WHERE DTTRV_CODE_DATE_TRAVAIL = @v
                  """, "WINS", ct)
            : null;
        var annee = travail is { Length: > 0 } ? Date(travail)?.Year ?? geste.Maintenant.Year : geste.Maintenant.Year;
        var (numero, refusNumero) = await NumeroAsync(geste, "RGNI", annee, ct);
        if (numero is null) return IssueGeste.Refuser(refusNumero!);
        var (demande, refusDemande) = await CreerDemandeAsync(geste, "CNI", "V", ct);
        if (demande is null) return IssueGeste.Refuser(refusDemande!);

        var adresse = await geste.InsererAsync("rec.Adresse", "Id", new Dictionary<string, object?>
        {
            ["NomVoie"] = S(EtapeAdresse, ChampAdresse), ["CodePostal"] = Vide(S(EtapeAdresse, ChampCodePostal)),
            ["Localite"] = Vide(S(EtapeAdresse, ChampVille)), ["EtatInfo"] = "1",
        }, ct);
        // DesignationSansCivilite (Metier.cs:329-340) : nom d'usage ou patronyme, prenoms, puis « né(e) patronyme » s'il differe.
        var nomAffiche = nomUsage.Length > 0 ? nomUsage : patronyme;
        var designation = pp
            ? (nomAffiche + " " + prenoms + (patronyme != nomAffiche
                ? (civilite.Masculin ? " né " : civilite.Feminin ? " née " : " né(e) ") + patronyme : "")).Trim()
            : denomination;
        var entreprise = await geste.InsererAsync("rec.Entreprise", "Id", new Dictionary<string, object?>
        {
            ["IdAdresse"] = adresse, ["Siren"] = Vide(siren), ["Civilite"] = civilite.Valeur, ["Denomination"] = designation,
            ["Patronyme"] = Vide(patronyme), ["Prenom"] = Vide(prenoms), ["FormeJuridique"] = Vide(forme), ["EtatInfo"] = "1",
            ["FicheSommaire"] = false, ["NomUsage"] = Vide(nomUsage),
        }, ct);
        var nonInscrit = await geste.InsererAsync("v2.NON_INSCRIT", "NINSC_ID_NINSC", new Dictionary<string, object?>
        {
            ["NINSC_ID_RPRTR"] = entreprise, ["NINSC_NUM_SIREN"] = Vide(siren), ["NINSC_CIVILITE"] = civilite.Valeur,
            ["NINSC_PATRONYME"] = Vide(patronyme), ["NINSC_PRENOMS"] = Vide(prenoms), ["NINSC_NOM_USAGE"] = Vide(nomUsage),
            ["NINSC_DATE_NAISSANCE"] = pp ? naissance : null, ["NINSC_DEPARTEMENT_NAISS"] = pp ? Vide(S(EtapeNaissance, ChampDepartement)) : null,
            ["NINSC_PAYS"] = pp ? Vide(S(EtapeNaissance, ChampPays)) ?? "FRA" : "FRA", ["NINSC_NATIONALITE"] = pp ? Vide(S(EtapeNaissance, ChampNationalite)) : null,
            ["NINSC_DENOMINATION"] = Vide(denomination), ["NINSC_FORME_JURIDIQUE_UTI"] = Vide(forme), ["NINSC_TYPE_NON_INSCRIT"] = type,
            ["NINSC_ETAT_INFO"] = "1", ["NINSC_NUM_NON_INSCRIT"] = numero, ["NINSC_PRINCIPAL"] = true, ["NINSC_INSCRIT_REGISTRE_METIER"] = false,
            ["NINSC_DATE_MAJ"] = geste.Maintenant, ["NINSC_PM"] = !pp, ["NINSC_NI_SECONDAIRE"] = false,
        }, ct);
        foreach (var (typeAdresse, commercial) in type is "JUD" or "PCOL" ? new[] { ("NQ", false), ("NQS", true) } : [("NQ", false)])
            await geste.InsererAsync("v2.ETABLISSEMENT", "ETBLS_ID_ETBLS", new Dictionary<string, object?>
            {
                ["ETBLS_ID_NINSC"] = nonInscrit, ["ETBLS_ID_ADRBS"] = adresse, ["ETBLS_NUM_ORDRE"] = 1, ["ETBLS_TYPE_ADRESSE"] = typeAdresse,
                ["ETBLS_ACTIVITE_IDEM"] = false, ["ETBLS_ETAT_INFO"] = "1", ["ETBLS_COMMERCIAL"] = commercial, ["ETBLS_SUPPRIME"] = false,
                ["ETBLS_1ER_OUVERT_EN_FRANCE"] = false,
            }, ct);
        // Le nom de la demande (l. 173-174) : « abrégé civilité patronyme prénoms » (avec l'espace final de la V2), ou la denomination.
        var nomDemande = pp ? $"{civilite.AbregeEdit} {patronyme} {prenoms} " : denomination;
        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_NOM = LEFT(@nom, 255) WHERE DMND_ID_DMND = @id", ct,
            new SqlParameter("@nom", nomDemande), new SqlParameter("@id", demande.Id));
        var id = demande.Id.ToString(CultureInfo.InvariantCulture);
        return IssueGeste.Ecrit($"Non-inscrit {numero} créé : {designation}.", [id],
            new TraceGeste("dossier", $"le non-inscrit {numero}", nonInscrit.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new { Numero = numero, Type = type, Civilite = civilite.Valeur, Designation = designation, Siren = siren, IdEntreprise = entreprise, Demande = demande.Numero }));
    }
}
