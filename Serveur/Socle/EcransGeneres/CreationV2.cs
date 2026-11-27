using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.Numerotation;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// CREER COMME RIG v2 : ce que les saisies de creation du verbe « Ouvrir » partagent (plan « RIG v3 100 % fonctionnel », lot 5,
/// 26/11/2026) -- les valeurs par defaut de RIG_DEV, la lecture d'un champ saisi, une instance numerotee avec ses deux parties,
/// la demande V2 qui la porte. Chaque ecrivain (Ecrivain*.cs) cite ce qu'il en prend.
///
/// LES VALEURS PAR DEFAUT : RIG_DEV porte des DEFAULT sur ses tables (sys.default_constraints de SQL-DEV\DEV, lu le
/// 26/11/2026) que la base V3 du greffe ne recopie pas (aucune contrainte DEFAULT sur v2.* dans la copie de dev, meme jour) :
/// une ligne neuve les recoit ici, pour les colonnes que la table porte (les tables des jeux des bancs n'en ont qu'une partie).
/// </summary>
internal static class CreationV2
{
    /// <summary>Les DEFAULT de RIG_DEV, par table (SQL-DEV\DEV, sys.default_constraints, 26/11/2026).</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> DefautsRigDev =
        new Dictionary<string, IReadOnlyDictionary<string, object>>(StringComparer.OrdinalIgnoreCase)
        {
            ["v2.INSTANCE"] = new Dictionary<string, object>
            {
                ["INSTN_AVIS_MP"] = false, ["INSTN_DEMAT_TIMBRE_AJ"] = false, ["INSTN_ETAT_TRANSMISSION"] = "F",
                ["INSTN_EXONERATION_TIMBRE_AJ"] = false, ["INSTN_IP_EUROPEENNE"] = false, ["INSTN_NAT_AFF_PART"] = "0A",
                ["INSTN_PRESENCE_MP"] = false, ["INSTN_REPRISE_TGI"] = false, ["INSTN_TIMBRE_AJ"] = false,
                ["INSTN_TOP_EXPERIAN"] = false, ["INSTN_TYPE_INSTANCE"] = "CX",
            },
            ["v2.PARTIE"] = new Dictionary<string, object>
            {
                ["PRT_AIDE_JURIDIC_PARTIELLE"] = false, ["PRT_AIDE_JURIDIC_TOTALE"] = false, ["PRT_COPIE_DECISION"] = true,
                ["PRT_EDITION_JUGEMENT"] = "D", ["PRT_IDEM_PRESENTATION"] = false, ["PRT_MANDATAIRE_EDITION_JUGEMENT"] = "D",
                ["PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT"] = "X",
            },
            ["v2.DEMANDE"] = new Dictionary<string, object>
            {
                ["DMND_ARRIVEE_SAISIE"] = false, ["DMND_EDITION"] = false, ["DMND_EN_COURS"] = false, ["DMND_FACTURE"] = false,
                ["DMND_FOCUS"] = false, ["DMND_INHIBEE"] = false,
            },
            ["v2.PROCEDURE_COLLECTIVE"] = new Dictionary<string, object>
            {
                ["PRCLL_ETAT_TRANSMISSION"] = "M", ["PRCLL_EXTERIEUR"] = false, ["PRCLL_FORFAIT_FACTURE"] = false,
                ["PRCLL_INSCRIT_REGISTRE_METIER"] = false, ["PRCLL_NE_PAS_TRANSFERER"] = false, ["PRCLL_NON_DIFFUSABLE"] = false,
                ["PRCLL_PC_PRINCIPALE"] = false, ["PRCLL_PROCEDURE_INSOLVABILITE"] = false, ["PRCLL_REPRISE_TGI"] = false,
                ["PRCLL_TAE"] = false, ["PRCLL_TC_SPECIALISE"] = false, ["PRCLL_TOP_CENTRA_INFOG"] = false, ["PRCLL_TOP_EXPERIAN"] = false,
            },
            ["v2.PREVENTION"] = new Dictionary<string, object>
            {
                ["PREVE_PRV_PRINCIPALE"] = false, ["PREVE_TAE"] = false, ["PREVE_TOP_EXPERIAN"] = false,
            },
            ["v2.EVENEMENT_PROCEDURE"] = new Dictionary<string, object>
            {
                ["EVNPR_AVEC_PIECE_NUMERISE"] = false, ["EVNPR_DELAI_EXPIRE"] = false, ["EVNPR_SURVENU"] = false,
            },
            ["v2.DEPOT_PC"] = new Dictionary<string, object> { ["DPTPC_TOP_EXPERIAN"] = false },
            ["v2.DEPOT_RCS"] = new Dictionary<string, object>
            {
                ["DPTRC_DIFFUSABILITE_CONFIDENTIELLE"] = false, ["DPTRC_DISPENSE_ANNEXE"] = false, ["DPTRC_ENVOI_INPI"] = false,
                ["DPTRC_ETAT_TRANSMISSION"] = "M", ["DPTRC_GRATUIT"] = false, ["DPTRC_NUMERISE"] = false,
                ["DPTRC_PRESENTATION_SIMPLIFIEE"] = false, ["DPTRC_RESULTAT_CONFIDENTIEL"] = false, ["DPTRC_SAISIE_SIMPLIFIEE"] = false,
                ["DPTRC_SANS_BODACC"] = false,
            },
            ["v2.ACTE_DEPOSE"] = new Dictionary<string, object>
            {
                ["ACTDP_BD_NUM_TRANSFERE"] = false, ["ACTDP_COMMANDABLE"] = false, ["ACTDP_INPI_ETAT"] = "1", ["ACTDP_SANS_DOCUMENT"] = false,
            },
            // Lot 9 (27/11/2026, « Valider » de Suivi dematerialisation) : les colonnes NOT NULL a DEFAULT de ces deux tables (lues
            // dans sys.default_constraints de RIG_DEV ce jour), que la base V3 garde sans leur defaut (PROC_REGJUD.sql:47-50).
            ["v2.DEMANDE_EDITION"] = new Dictionary<string, object>
            {
                ["DMNED_NOMBRE_EXEMPLAIRE"] = (short)1, ["DMNED_IMPRESSION"] = true, ["DMNED_PAPIER_TRAME"] = false, ["DMNED_AGRAFAGE"] = false,
                ["DMNED_RECTO_VERSO"] = false, ["DMNED_AFF_AVANT_IMPRESSION"] = false, ["DMNED_GROUPAGE_AUTOMATIQUE"] = true,
                ["DMNED_RESUME"] = false, ["DMNED_READ_ONLY"] = false, ["DMNED_ENVOI_PAR_MAIL"] = false, ["DMNED_HAS_NUMLRAR"] = false,
                ["DMNED_IS_NON_SUPPRESSIBLE"] = false, ["DMNED_HAS_NUMLR"] = false, ["DMNED_MASQUER_COFFRE_FORT"] = false,
            },
            ["v2.GED_RIG"] = new Dictionary<string, object> { ["GDRG_VIDIGED_X"] = 0 },
        };

    /// <summary>
    /// Ajoute une ligne a <paramref name="table"/> : <paramref name="valeurs"/>, completees des DEFAULT de RIG_DEV pour les
    /// colonnes que la table porte et que l'ecrivain ne donne pas. Rend l'identifiant (GesteEnCours.InsererAsync).
    /// </summary>
    internal static async Task<int> InsererAsync(GesteEnCours geste, string table, string colonneId, IReadOnlyDictionary<string, object?> valeurs,
                                                 CancellationToken ct)
    {
        var completes = new Dictionary<string, object?>(valeurs, StringComparer.OrdinalIgnoreCase);
        if (DefautsRigDev.TryGetValue(table, out var defauts))
        {
            var presentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var r = await geste.LireAsync("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@t)", ct, new SqlParameter("@t", table)))
                while (await r.ReadAsync(ct)) presentes.Add(r.GetString(0));
            foreach (var (colonne, valeur) in defauts)
                if (presentes.Contains(colonne) && !completes.ContainsKey(colonne)) completes[colonne] = valeur;
        }
        return await geste.InsererAsync(table, colonneId, completes, ct);
    }

    /// <summary>La valeur saisie (blancs retires) du champ <paramref name="libelle"/> de <paramref name="section"/> ; vide si absente.</summary>
    internal static string Saisi(GesteEnCours geste, string section, string libelle) => geste.Valeur(section, libelle)?.Trim() ?? "";

    /// <summary>Une date saisie (« 2026-03-31 », le format des champs date de l'ecran) ; null si vide ou illisible.</summary>
    internal static DateTime? Date(string valeur)
        => DateTime.TryParseExact(valeur, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>« « X » est obligatoire. » : le refus du moteur de RIG v2 pour un champ MetierObligatoire vide (celui des ecrans
    /// generes depuis le lot 2, EcrivainInstanceJudiciaire.cs).</summary>
    internal static string Obligatoire(string libelle) => $"« {libelle} » est obligatoire.";

    /// <summary>Un numero de la serie <paramref name="type"/> pour l'annee <paramref name="annee"/> ; null et <paramref name="refus"/>
    /// si la base ne le donne pas (type inconnu, serie pleine : le message de num.Attribuer).</summary>
    internal static async Task<(string? Numero, string? Refus)> NumeroAsync(GesteEnCours geste, string type, int annee, CancellationToken ct)
    {
        var n = await geste.AttribuerNumeroAsync(new DemandeNumero(type, annee), ct);
        return n.Code == CodesNumerotation.Ok && !string.IsNullOrEmpty(n.Numero)
            ? (n.Numero, null)
            : (null, $"Numérotation {type} {annee} : {n.Message ?? "aucun numéro donné"} (code {n.Code}).");
    }

    // ------------------------------------------------------------ L'INSTANCE ET SES PARTIES

    /// <summary>Une partie saisie : civilite, designation, SIREN (OPE_PARTIE.Designer.cs : Civilité et Désignation obligatoires).</summary>
    internal sealed record PartieSaisie(string Civilite, string Designation, string Siren);

    /// <summary>Ce qu'une saisie d'instance donne : les champs obligatoires de l'etape « Instance » de RIG v2
    /// (Rig_Etape_Instance\INSTANCE\Saisie\ETP_INSTANCE\OPE_INSTANCE.Designer.cs, MetierObligatoire : Code affaire, Nature affaire,
    /// Nature particulière de affaire, Acte de saisine, Saisine tribunal, Date de l'acte).</summary>
    internal sealed record InstanceSaisie(string CodeAffaire, string NatureAffaire, string NatureParticuliere, string ActeSaisine,
                                          DateTime? DateActe, DateTime Saisine, DateTime Enrolement, string? Note);

    /// <summary>L'instance creee : son id, ses numeros, son nom courant.</summary>
    internal sealed record InstanceCreee(int Id, string NumRole, string? NumRepGeneral, string NomCourant, string TypeInstance);

    /// <summary>Le code affaire de RIG v2 (v2.CODE_AFFAIRE) : le type de numero de role, le type d'instance, le repertoire.</summary>
    internal sealed record CodeAffaire(string Valeur, string TypeNumRole, string TypeInstance, bool NumRepertoire, string? NatAffPart, string? IsoActe);

    /// <summary>Le code affaire <paramref name="code"/>, et l'ISO de l'acte <paramref name="acte"/> (01 = assignation) ; null s'il n'existe pas.</summary>
    internal static async Task<CodeAffaire?> CodeAffaireAsync(GesteEnCours geste, string code, string acte, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("""
            SELECT ca.R_CODEAFFAIR_VALEUR, ca.R_CODEAFFAIR_TYPE_NUM_ROLE, ca.R_CODEAFFAIR_TYPE_INSTANCE,
                   CONVERT(bit, ISNULL(ca.R_CODEAFFAIR_NUM_REPERTOIRE, 0)) AS Repertoire, ca.R_CODEAFFAIR_NAT_AFF_PART,
                   (SELECT TOP (1) ap.R_CODEACTEPR_ISO FROM v2.CODE_ACTE_PROCE AS ap WHERE ap.R_CODEACTEPR_VALEUR = @acte) AS Iso
            FROM v2.CODE_AFFAIRE AS ca WHERE ca.R_CODEAFFAIR_VALEUR = @code
            """, ct, new SqlParameter("@code", code), new SqlParameter("@acte", acte));
        if (!await r.ReadAsync(ct)) return null;
        return new CodeAffaire(FormeLigne.Texte(r, "R_CODEAFFAIR_VALEUR"), FormeLigne.Texte(r, "R_CODEAFFAIR_TYPE_NUM_ROLE"),
            FormeLigne.Texte(r, "R_CODEAFFAIR_TYPE_INSTANCE"), r["Repertoire"] is true,
            r["R_CODEAFFAIR_NAT_AFF_PART"] is string n ? n.Trim() : null, r["Iso"] is string i ? i.Trim() : null);
    }

    /// <summary>
    /// Le nom courant d'une instance neuve : « demandeur # défendeur », tronque a 250 caracteres (Instance.GetNomCourantInstance et
    /// _GetNomInstance, Instance.cs:864-905 ; cSeparateurNonCourant = " # ", :796 ; INSTN_NOM_COURANT varchar(250) dans RIG_DEV).
    /// </summary>
    internal static string NomCourant(string? demandeur, string? defendeur)
    {
        const int Longueur = 250;
        if (string.IsNullOrEmpty(demandeur) && string.IsNullOrEmpty(defendeur)) return "<Aucune partie saisie - Nom inconnu>";
        var dem = string.IsNullOrEmpty(demandeur) ? "<Inconnu>" : demandeur;
        var def = defendeur ?? "";
        var sep = def.Length > 0 ? " # " : "";
        if (dem.Length + sep.Length + def.Length <= Longueur) return dem + sep + def;
        var dispo = Longueur - sep.Length;
        if (dem.Length > dispo / 2 && def.Length > dispo / 2) { dem = dem[..(dispo / 2)]; def = def[..(dispo - dem.Length)]; }
        else if (dem.Length > def.Length) dem = dem[..(dispo - def.Length)];
        else def = def[..(dispo - dem.Length)];
        return dem + sep + def;
    }

    /// <summary>
    /// Les controles de dates de l'etape « Instance » (VerifierDates, OPE_INSTANCE.cs:1325-1350), aux messages de RIG v2. Pour une
    /// assignation (ISO 01), la date de l'acte est celle des defendeurs (OPE_INSTANCE.cs:1326-1329) : non controlee ici, la
    /// saisie n'en porte pas.
    /// </summary>
    internal static string? VerifierDates(InstanceSaisie i)
    {
        var erreurs = new List<string>();
        if (i.DateActe > i.Enrolement) erreurs.Add("La date de l'acte doit être inférieure ou égale à la date d'enrôlement");
        if (i.Saisine > i.Enrolement) erreurs.Add("La date de saisine du Tribunal doit être inférieure ou égale à la date d'enrôlement");
        if (i.DateActe > i.Saisine) erreurs.Add("La date de l'acte doit être inférieure ou égale à la date de saisine du Tribunal");
        return erreurs.Count == 0 ? null : string.Join(". ", erreurs) + ".";
    }

    /// <summary>
    /// Ecrit une instance neuve et ses deux parties, comme OPE_INSTANCE.CallPrepSauve (Rig_Etape_Instance\INSTANCE\Saisie\
    /// ETP_INSTANCE\OPE_INSTANCE.cs:1460-1495) et Instance.CreateInstance (Instance.cs:3372-3385) :
    ///   - INSTN_ID_AFFRE = l'affaire de la demande ; INSTN_ETAT_INSTANCE « 0 », INSTN_ETAT_INFO « 1 » ; INSTN_NAT_AFF_PART et
    ///     INSTN_TYPE_INSTANCE copies du code affaire quand ils manquent ; INSTN_NUMERO_SOURCE un GUID en majuscules ;
    ///   - le numero de role : la serie R_CODEAFFAIR_TYPE_NUM_ROLE du code affaire, annee de la saisine du tribunal
    ///     (Numerotation.Generer, :1477) ; le repertoire general JGAFF, meme annee, si R_CODEAFFAIR_NUM_REPERTOIRE (:1491-1494) ;
    ///   - le nom courant des parties (NomCourant) ;
    ///   - les parties : Partie.CreatePartie (Partie.cs:428-451), numero d'ordre 1, PRT_NOM = la designation, editions de jugement
    ///     « D » ; qualifiant <paramref name="qualifDemandeur"/> / <paramref name="qualifDefendeur"/> (Partie.cs:823-830).
    /// NON REPRIS (nomme par l'ecrivain) : le dossier judiciaire JRDSJ d'une instance RP sans PC (:1461-1466) -- refuse par
    /// l'appelant si le code affaire est de type RP ; la facture de la demande (:1478-1487).
    /// </summary>
    internal static async Task<(InstanceCreee? Instance, string? Refus)> CreerInstanceAsync(GesteEnCours geste, CodeAffaire code, InstanceSaisie i,
        int idAffaire, PartieSaisie demandeur, string qualifDemandeur, PartieSaisie defendeur, string qualifDefendeur, CancellationToken ct)
    {
        var annee = i.Saisine.Year;
        var (role, refusRole) = await NumeroAsync(geste, code.TypeNumRole, annee, ct);
        if (role is null) return (null, refusRole);
        string? repertoire = null;
        if (code.NumRepertoire)
        {
            var (rep, refusRep) = await NumeroAsync(geste, "JGAFF", annee, ct);
            if (rep is null) return (null, refusRep);
            repertoire = rep;
        }
        var nom = NomCourant(demandeur.Designation, defendeur.Designation);
        var id = await InsererAsync(geste, "v2.INSTANCE", "INSTN_ID_INSTN", new Dictionary<string, object?>
        {
            ["INSTN_ID_AFFRE"] = idAffaire, ["INSTN_CODE_AFFAIRE"] = code.Valeur, ["INSTN_NATURE_AFFAIRE"] = i.NatureAffaire,
            ["INSTN_NAT_AFF_PART"] = i.NatureParticuliere.Length > 0 ? i.NatureParticuliere : code.NatAffPart ?? "0A",
            ["INSTN_CODE_ACTE_PROCE"] = i.ActeSaisine, ["INSTN_DATE_ACTE"] = i.DateActe, ["INSTN_DATE_SAISINE_GREFFE"] = i.Saisine,
            ["INSTN_DATE_ENROLEMENT"] = i.Enrolement, ["INSTN_TYPE_INSTANCE"] = code.TypeInstance, ["INSTN_ETAT_INSTANCE"] = "0",
            ["INSTN_ETAT_INFO"] = "1", ["INSTN_NUM_ROLE"] = role, ["INSTN_NUM_REP_GENERAL"] = repertoire, ["INSTN_NOM_COURANT"] = nom,
            ["INSTN_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(),
            ["INSTN_NOTE_AUDIENCE"] = string.IsNullOrWhiteSpace(i.Note) ? null : i.Note,
        }, ct);
        foreach (var (p, qualif) in new[] { (demandeur, qualifDemandeur), (defendeur, qualifDefendeur) })
            await InsererAsync(geste, "v2.PARTIE", "PRT_ID_PRT", new Dictionary<string, object?>
            {
                ["PRT_ID_INSTN"] = id, ["PRT_NUM_ORDRE"] = (short)1, ["PRT_CIVILITE"] = p.Civilite, ["PRT_DESIGNATION"] = p.Designation,
                ["PRT_NOM"] = p.Designation, ["PRT_SIREN"] = p.Siren.Length > 0 ? p.Siren.Replace(" ", "", StringComparison.Ordinal) : null,
                ["PRT_QUALIF_PARTIE"] = qualif, ["PRT_EDITION_JUGEMENT"] = "D", ["PRT_MANDATAIRE_EDITION_JUGEMENT"] = "D",
                ["PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT"] = "D",
            }, ct);
        return (new InstanceCreee(id, role, repertoire, nom, code.TypeInstance), null);
    }

    // ------------------------------------------------------------ UNE SAISIE D'INSTANCE (IPE, CORDJRCS)

    /// <summary>Les etapes et champs des saisies d'instance (descripteurs PROC_IPE et PROC_CORDJRCS, Ouvrir.cs), au mot pres.</summary>
    internal const string EtapeInstance = "Instance", EtapeDemandeur = "Demandeur", EtapeDefendeur = "Défendeur";

    internal const string ChampCodeAffaire = "Code affaire", ChampNature = "Nature affaire", ChampNatureParticuliere = "Nature particulière de l'affaire",
                          ChampActe = "Acte de saisine", ChampDateActe = "Date de l'acte", ChampSaisine = "Saisine tribunal",
                          ChampNote = "Note sur l'affaire", ChampCivilite = "Civilité", ChampDesignation = "Désignation", ChampSiren = "N° de Siren";

    /// <summary>Ce qui distingue deux processus qui creent une instance par l'etape « Instance » de RIG v2 : le code processus de
    /// la demande, le type d'instance des codes affaire proposes, les qualifiants des parties, et les mots du message.</summary>
    internal sealed record SaisieInstanceV2(string CodeProcessus, string TypeInstance, string QualifDemandeur, string QualifDefendeur,
                                            string Objet, string ObjetUn, string NonRepris);

    /// <summary>
    /// CREER une instance numerotee, ses deux parties et sa demande (laissee interrompue), pour <paramref name="saisie"/>.
    /// CONTROLES DE RIG v2, avant toute ecriture (un refus n'ecrit rien) :
    ///   1. les champs obligatoires (MetierObligatoire) de l'etape « Instance » (OPE_INSTANCE.Designer.cs) -- la date de l'acte
    ///      n'est pas exigee pour une assignation (ISO 01, OPE_INSTANCE.cs:1326-1329) -- et de chaque partie (Civilité,
    ///      Désignation, OPE_PARTIE.Designer.cs) ;
    ///   2. le code affaire est un de ceux que la liste de RIG v2 propose au processus (type d'instance, OPE_INSTANCE.cs:1649-1664) ;
    ///   3. VerifierDates (OPE_INSTANCE.cs:1325-1350), l'enrolement etant maintenant (PROC_IPE.cs:155, PROC_CORDJRCS.cs:214) ;
    ///   4. la numerotation donne ses numeros (num.Attribuer : type inconnu ou serie pleine = refus, rien d'ecrit).
    /// CE QUI S'ECRIT : CreerDemandeAsync (etat I), CreerInstanceAsync, RattacherAsync (DoSauveAfterEtape : PROC_IPE.cs:304-307,
    /// PROC_CORDJRCS.cs:251-258). La demande reste INTERROMPUE : l'etat que RIG v2 laisse a une saisie sauvee avant ses
    /// dernieres etapes (decision, facturation) ; la reprendre dans RIG v2 les termine.
    /// </summary>
    internal static async Task<IssueGeste> CreerSaisieInstanceAsync(GesteEnCours geste, SaisieInstanceV2 saisie, CancellationToken ct)
    {
        string S(string etape, string champ) => Saisi(geste, etape, champ);
        foreach (var (etape, champ) in new[]
                 {
                     (EtapeInstance, ChampCodeAffaire), (EtapeInstance, ChampNature), (EtapeInstance, ChampNatureParticuliere),
                     (EtapeInstance, ChampActe), (EtapeInstance, ChampSaisine), (EtapeDemandeur, ChampCivilite),
                     (EtapeDemandeur, ChampDesignation), (EtapeDefendeur, ChampCivilite), (EtapeDefendeur, ChampDesignation),
                 })
            if (S(etape, champ).Length == 0) return IssueGeste.Refuser(Obligatoire($"{etape} › {champ}"));
        if (Date(S(EtapeInstance, ChampSaisine)) is not { } saisine)
            return IssueGeste.Refuser($"« {EtapeInstance} › {ChampSaisine} » : date illisible ({S(EtapeInstance, ChampSaisine)}).");
        var dateActeSaisie = S(EtapeInstance, ChampDateActe);
        var dateActe = Date(dateActeSaisie);
        if (dateActeSaisie.Length > 0 && dateActe is null)
            return IssueGeste.Refuser($"« {EtapeInstance} › {ChampDateActe} » : date illisible ({dateActeSaisie}).");

        var codeSaisi = S(EtapeInstance, ChampCodeAffaire);
        var code = await CodeAffaireAsync(geste, codeSaisi, S(EtapeInstance, ChampActe), ct);
        if (code is null) return IssueGeste.Refuser($"Code affaire inconnu dans la base du greffe : « {codeSaisi} ».");
        if (code.TypeInstance != saisie.TypeInstance)
            return IssueGeste.Refuser($"Le code affaire « {codeSaisi} » n'est pas proposé pour {saisie.ObjetUn} (type d'instance {saisie.TypeInstance}).");
        if (dateActe is null && code.IsoActe != "01") return IssueGeste.Refuser(Obligatoire($"{EtapeInstance} › {ChampDateActe}"));

        var instance = new InstanceSaisie(code.Valeur, S(EtapeInstance, ChampNature), S(EtapeInstance, ChampNatureParticuliere),
            S(EtapeInstance, ChampActe), dateActe, saisine, geste.Maintenant, S(EtapeInstance, ChampNote));
        if (VerifierDates(instance) is { } dates) return IssueGeste.Refuser(dates);

        var (demande, refusDemande) = await CreerDemandeAsync(geste, saisie.CodeProcessus, "I", ct);
        if (demande is null) return IssueGeste.Refuser(refusDemande!);
        var (creee, refus) = await CreerInstanceAsync(geste, code, instance, demande.IdAffaire,
            new PartieSaisie(S(EtapeDemandeur, ChampCivilite), S(EtapeDemandeur, ChampDesignation), S(EtapeDemandeur, ChampSiren)), saisie.QualifDemandeur,
            new PartieSaisie(S(EtapeDefendeur, ChampCivilite), S(EtapeDefendeur, ChampDesignation), S(EtapeDefendeur, ChampSiren)), saisie.QualifDefendeur, ct);
        if (creee is null) return IssueGeste.Refuser(refus!);
        var id = creee.Id.ToString(CultureInfo.InvariantCulture);
        await RattacherAsync(geste, demande, creee.NomCourant, creee.NumRole, $"{creee.NumRole} {creee.NumRepGeneral}".Trim(),
            VerrouDemandeV2.SequenceurInstance, id, ct);

        // fix-ok: mesure le 26/11/2026 -- la serie DMD a la structure « LAQC » (num.Compteur de la copie de dev) : son numero porte
        // le quantieme du jour ; il va au journal des gestes, pas dans le message, que le banc compare au mot pres.
        return IssueGeste.Ecrit(
            $"{saisie.Objet} {creee.NumRole} créée dans la base du greffe (répertoire général {creee.NumRepGeneral ?? "—"}) ; sa demande " +
            $"reste interrompue : {saisie.NonRepris} ne sont pas reprises par la V3, reprendre la demande dans RIG v2.",
            [demande.Id.ToString(CultureInfo.InvariantCulture)],
            new TraceGeste("affaire", $"{saisie.Objet.ToLowerInvariant()} {creee.NumRole}", id, "a cree", null,
                new { NumeroRole = creee.NumRole, Repertoire = creee.NumRepGeneral, creee.NomCourant, Demande = demande.Numero, CodeAffaire = code.Valeur }));
    }

    // ------------------------------------------------------------ LA DEMANDE V2

    /// <summary>La demande creee : son id et son numero.</summary>
    internal sealed record DemandeCreee(int Id, string Numero, int IdAffaire);

    /// <summary>
    /// L'affaire et la demande V2 d'un geste de creation, comme le moteur de RIG v2 : une affaire neuve (AFFRE_DATE_CREATION),
    /// une demande numerotee dans la serie DMD (Demande.cs:493 et :669, Numerotation.Generer(codeGreffe, "DMD", 0, 0, "")), au
    /// code du processus et a l'etat <paramref name="etat"/>, avec son nom et ses numeros (PROC_IPE.cs:304-307 ;
    /// PROC_NPC.cs:150-154), et ses DOSSIER_DEMANDE (sequenceur, valeur) -- 2J = l'instance (VerrouDemandeV2). L'auteur : le nom
    /// de l'agent dans DMND_NOM_UTILISATEUR (la colonne que le verrou de RIG v2 cite).
    /// </summary>
    internal static async Task<(DemandeCreee? Demande, string? Refus)> CreerDemandeAsync(GesteEnCours geste, string codeProcessus, string etat,
        CancellationToken ct)
    {
        var (numero, refus) = await NumeroAsync(geste, "DMD", geste.Maintenant.Year, ct);
        if (numero is null) return (null, refus);
        var affaire = await geste.InsererAsync("v2.AFFAIRE", "AFFRE_ID_AFFRE",
            new Dictionary<string, object?> { ["AFFRE_DATE_CREATION"] = geste.Maintenant }, ct);
        var id = await InsererAsync(geste, "v2.DEMANDE", "DMND_ID_DMND", new Dictionary<string, object?>
        {
            ["DMND_NUM_DEMANDE"] = numero, ["DMND_ETAT_DEMANDE"] = etat, ["DMND_CODE_PROSS"] = codeProcessus,
            ["DMND_ID_AFFRE"] = affaire, ["DMND_DATE_CREATION"] = geste.Maintenant, ["DMND_EN_COURS"] = false,
            ["DMND_NOM_UTILISATEUR"] = geste.Agent.NomAffiche,
        }, ct);
        return (new DemandeCreee(id, numero, affaire), null);
    }

    /// <summary>Le nom, le numero indicatif et les numeros de la demande (PROC_IPE.cs:304-307), et son lien au sequenceur.</summary>
    internal static async Task RattacherAsync(GesteEnCours geste, DemandeCreee demande, string nom, string indicatif, string numeros,
                                              string sequenceur, string valeur, CancellationToken ct)
    {
        await geste.EcrireAsync("""
            UPDATE v2.DEMANDE SET DMND_NOM = LEFT(@nom, 255), DMND_NUM_INDICATIF = LEFT(@indicatif, 14), DMND_LES_NUMEROS = LEFT(@numeros, 128)
            WHERE DMND_ID_DMND = @id
            """, ct, new SqlParameter("@nom", nom), new SqlParameter("@indicatif", indicatif), new SqlParameter("@numeros", numeros),
            new SqlParameter("@id", demande.Id));
        await geste.InsererAsync("v2.DOSSIER_DEMANDE", "DSSDM_ID_DSSDM", new Dictionary<string, object?>
        {
            ["DSSDM_ID_DMND"] = demande.Id, ["DSSDM_CODE_SEQUENCEUR"] = sequenceur, ["DSSDM_VALEUR_SEQUENCEUR"] = valeur,
        }, ct);
    }
}
