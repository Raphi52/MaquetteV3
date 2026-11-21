using System.Text.RegularExpressions;

namespace RigV3.Outils.Anonymisation;

/// <summary>Ce que la copie fait d'une colonne.</summary>
public enum Traitement
{
    /// <summary>Recopiee telle quelle : numeros, dates des affaires, montants, codes (SOC-021 CA03).</summary>
    Garder,
    /// <summary>Chaque mot remplace (noms, prenoms, adresses, textes libres) : longueur et forme gardees.</summary>
    Mots,
    /// <summary>Comme <see cref="Mots"/>, extension du domaine gardee.</summary>
    Courriel,
    /// <summary>Chiffres permutes ensemble, deux premiers gardes.</summary>
    Telephone,
    /// <summary>Autre jour de la meme annee.</summary>
    DateNaissance,
    /// <summary>NULL (ou vide si la colonne n'accepte pas NULL) : les secrets (mots de passe).</summary>
    Vider,
    /// <summary>XML : chaque attribut ou element traite par la regle de la colonne qu'il porte.</summary>
    Xml,
}

/// <summary>
/// La regle d'une colonne et d'ou elle vient (le rapport la cite : c'est ce que relit le delegue a
/// la protection des donnees, SOC-021 T01). <paramref name="ColonneTable"/> : pour <see cref="Traitement.Xml"/>,
/// la colonne de la MEME ligne qui nomme la table decrite par le XML (AUDIT.AUDIT_TABLE).
/// </summary>
public sealed record Regle(Traitement Traitement, string Origine, string? ColonneTable = null)
{
    public bool Anonymise => Traitement != Traitement.Garder;
}

/// <summary>
/// LES REGLES PAR TABLE ET COLONNE (SOC-021 T01, T02).
///
/// 1. Le CATALOGUE : les tables de la story (CLIENT, UTILISATEUR, AUDIT, EMAIL_ENVOI_TRACE,
///    EMAIL_ENVOYE, ADRESSE_BASE, PARTIE, DIRIGEANT_PP) et les tables signees qui recopient le client
///    (FACTURE, ENCAISSEMENT), colonne par colonne, avec la raison de chaque choix -- y compris
///    ce qui est GARDE et pourquoi.
/// 2. Pour toute autre colonne, le NOM : decoupe en segments (« DRGPP_NOM_USAGE » -> DRGPP, NOM,
///    USAGE), un segment personnel (NOM, PRENOM, MAIL, TEL, NAISSANCE, ADRESSE, TEXTE…) donne la
///    regle ; un segment technique (CODE, TYPE, FICHIER, ROLE…) la leve. Par defaut, ce qui
///    ressemble a une donnee personnelle est TRAITE (on prefere abimer un libelle que laisser
///    passer un nom) et le rapport le dit « a relire ».
/// 3. Des REMPLACEMENTS « TABLE.COLONNE=traitement » (option --regles) : la relecture du delegue
///    s'applique sans toucher au code. Ils priment sur le reste.
/// </summary>
public sealed partial class CatalogueRegles
{
    private readonly Dictionary<(string Table, string Colonne), Regle> _explicites = new(new ComparateurCle());
    private readonly Dictionary<(string Table, string Colonne), Regle> _remplacements = new(new ComparateurCle());

    public CatalogueRegles(IEnumerable<KeyValuePair<string, Traitement>>? remplacements = null)
    {
        Remplir();
        foreach (var (cle, t) in remplacements ?? [])
        {
            var point = cle.LastIndexOf('.');
            if (point <= 0 || point == cle.Length - 1)
                throw new ArgumentException($"Remplacement « {cle} » : attendu TABLE.COLONNE=traitement.");
            _remplacements[(cle[..point], cle[(point + 1)..])] = new Regle(t, "remplacement (--regles)");
        }
    }

    /// <summary>Lit un fichier de remplacements : une ligne « TABLE.COLONNE=traitement », # commente.</summary>
    public static IEnumerable<KeyValuePair<string, Traitement>> LireRemplacements(string fichier)
    {
        foreach (var brute in File.ReadAllLines(fichier))
        {
            var ligne = brute.Split('#')[0].Trim();
            if (ligne.Length == 0) continue;
            var egal = ligne.IndexOf('=');
            if (egal <= 0 || !Enum.TryParse<Traitement>(ligne[(egal + 1)..].Trim(), ignoreCase: true, out var t))
                throw new ArgumentException($"{fichier} : ligne illisible « {ligne} » (attendu TABLE.COLONNE=Garder|Mots|Courriel|Telephone|DateNaissance|Vider|Xml).");
            yield return new(ligne[..egal].Trim(), t);
        }
    }

    /// <summary>
    /// La regle d'une colonne. <paramref name="table"/> sans schema (« CLIENT ») ; <paramref name="typeSql"/>
    /// le type de base (varchar, datetime, xml…) : une regle de texte ne s'applique qu'a un texte.
    /// </summary>
    public Regle Pour(string table, string colonne, string typeSql)
    {
        var regle = _remplacements.GetValueOrDefault((table, colonne))
                    ?? _parLesCles.GetValueOrDefault((table, colonne))
                    ?? _explicites.GetValueOrDefault((table, colonne))
                    ?? ParLeNom(colonne, typeSql);
        return Compatible(regle, typeSql);
    }

    private readonly Dictionary<(string Table, string Colonne), Regle> _parLesCles = new(new ComparateurCle());

    /// <summary>
    /// LES DEUX COTES D'UNE CLE ETRANGERE PRENNENT LA MEME REGLE (SOC-021 CA02). Les valeurs fictives
    /// dependent de la valeur seule : une colonne et celle qu'elle reference, traitees pareil, restent
    /// egales -- le lien tient. Traitees differemment, il casse. Mesure sur RIG_DEV (2026-11-21) :
    /// DEMANDE_EDITION.DMNED_LOT reference LOT.LOT_NOM, que son nom fait anonymiser ; sans cet
    /// alignement, la cle ne se verifie plus dans la copie. Le cote anonymise l'emporte (on prefere
    /// abimer un libelle que garder un nom) ; un remplacement relu reste prioritaire.
    /// fix-ok: inventaire de RIG_DEV croise avec sys.foreign_key_columns (2026-11-21) -- 1 cle texte sur 155
    /// traitee d'un seul cote (DMNED_LOT garde, LOT_NOM anonymise) ; le banc la rejoue avec la vraie cle.
    /// </summary>
    public void AlignerSurLesCles(SchemaSource schema)
    {
        var tables = schema.Tables.ToDictionary(t => t.Id);
        string TypeDe(TableSql t, string c) => t.Colonnes.FirstOrDefault(x => x.Nom == c)?.Type ?? "varchar";
        var paires = schema.Cles
            .Where(k => tables.ContainsKey(k.Table) && tables.ContainsKey(k.Reference))
            .SelectMany(k => k.Colonnes.Select(c => (T: tables[k.Table], C: c.Colonne, R: tables[k.Reference], RC: c.Referencee, Cle: k.Nom)))
            .ToList();
        // Une chaine de cles (A -> B -> C) peut demander plusieurs tours ; borne : le nombre de paires.
        for (var tour = 0; tour <= paires.Count; tour++)
        {
            var change = false;
            foreach (var p in paires)
            {
                var a = Pour(p.T.Nom, p.C, TypeDe(p.T, p.C));
                var b = Pour(p.R.Nom, p.RC, TypeDe(p.R, p.RC));
                if (a.Traitement == b.Traitement) continue;
                var (modele, table, colonne) = b.Anonymise ? (b, p.T.Nom, p.C) : (a, p.R.Nom, p.RC);
                if (_remplacements.ContainsKey((table, colonne)) || _parLesCles.ContainsKey((table, colonne))) continue;
                _parLesCles[(table, colonne)] = modele with
                {
                    Origine = $"cle etrangere {p.Cle} ({p.T.Nom}.{p.C} -> {p.R.Nom}.{p.RC}) : meme regle des deux cotes, le lien tient",
                };
                change = true;
            }
            if (!change) break;
        }
    }

    /// <summary>
    /// La regle d'un attribut ou d'un element d'un XML de la ligne d'une table (le journal AUDIT garde la
    /// ligne entiere : un attribut par colonne). Table inconnue : par le nom seul ; type inconnu : texte.
    /// </summary>
    public Regle PourXml(string? table, string nom)
    {
        if (table is not null && (_remplacements.GetValueOrDefault((table, nom)) ?? _explicites.GetValueOrDefault((table, nom))) is { } r)
            return r.Traitement == Traitement.Xml ? r with { Traitement = Traitement.Mots } : r;
        var parNom = ParLeNom(nom, "varchar");
        return parNom;
    }

    /// <summary>Les colonnes que le nom designe comme personnelles mais qu'un segment technique ou le
    /// catalogue garde : le rapport les liste « a relire ».</summary>
    public string? Suspicion(string table, string colonne)
    {
        if (_remplacements.ContainsKey((table, colonne)) || _parLesCles.ContainsKey((table, colonne))) return null;
        if (_explicites.TryGetValue((table, colonne), out var r))
            return r.Traitement == Traitement.Garder && MarqueurPersonnel(Segments(colonne)) is { } m ? $"segment « {m} », gardee par le catalogue : {r.Origine}" : null;
        var segments = Segments(colonne);
        if (MarqueurPersonnel(segments) is { } marqueur && segments.FirstOrDefault(Techniques.Contains) is { } technique)
            return $"segment personnel « {marqueur} » mais segment technique « {technique} » : gardee";
        return null;
    }

    private static Regle Compatible(Regle r, string typeSql)
    {
        var texte = EstTexte(typeSql);
        return r.Traitement switch
        {
            Traitement.Garder => r,
            Traitement.Xml when typeSql == "xml" => r,
            Traitement.Xml => texte ? r with { Traitement = Traitement.Mots } : Garde(r, typeSql),
            Traitement.DateNaissance when EstDate(typeSql) || texte => r,
            Traitement.Vider => r,
            _ when texte => r,
            _ when typeSql == "xml" => r with { Traitement = Traitement.Xml },
            _ => Garde(r, typeSql),
        };
    }

    private static Regle Garde(Regle r, string typeSql)
        => new(Traitement.Garder, $"{r.Origine} -- non applicable au type {typeSql} : gardee");

    public static bool EstTexte(string t) => t is "varchar" or "nvarchar" or "char" or "nchar" or "text" or "ntext" or "sysname";
    public static bool EstDate(string t) => t is "datetime" or "datetime2" or "smalldatetime" or "date" or "datetimeoffset";

    // ------------------------------------------------------------------ par le nom

    private static readonly HashSet<string> Techniques = new(StringComparer.OrdinalIgnoreCase)
    {
        "LOGIQUE", "FICHIER", "FICHIERS", "TABLE", "ROLE", "PROC", "PROCEDURE", "CHAMP", "COLONNE", "MODELE",
        "MAQUETTE", "SERVEUR", "IMPRIMANTE", "REPERTOIRE", "CHEMIN", "TYPE", "CODE", "FORMAT", "CLE", "ETAT",
        "STATUT", "NATURE", "CATEGORIE", "PARAMETRE", "SCHEMA", "INDEX", "VUE", "BASE", "DOMAINE", "CLASSE",
        "MENU", "ECRAN", "ONGLET", "BOUTON", "REQUETE", "SQL", "ID", "DLL", "EXE",
    };

    private static readonly Dictionary<string, Traitement> Personnels = new(StringComparer.OrdinalIgnoreCase)
    {
        // Les secrets : vides, jamais permutes (une valeur derivee d'un mot de passe reste un risque).
        ["PASSWORD"] = Traitement.Vider, ["MDP"] = Traitement.Vider, ["PWD"] = Traitement.Vider,
        ["PASSE"] = Traitement.Vider, ["SECRET"] = Traitement.Vider,
        ["MAIL"] = Traitement.Courriel, ["EMAIL"] = Traitement.Courriel, ["COURRIEL"] = Traitement.Courriel,
        ["MEL"] = Traitement.Courriel, ["MAILS"] = Traitement.Courriel,
        ["TEL"] = Traitement.Telephone, ["TELEPHONE"] = Traitement.Telephone, ["PORTABLE"] = Traitement.Telephone,
        ["FAX"] = Traitement.Telephone, ["MOBILE"] = Traitement.Telephone, ["GSM"] = Traitement.Telephone,
        ["TELECOPIE"] = Traitement.Telephone,
        ["NAISSANCE"] = Traitement.DateNaissance, ["NAISS"] = Traitement.DateNaissance, ["NAIS"] = Traitement.DateNaissance,
        ["NOM"] = Traitement.Mots, ["NOMS"] = Traitement.Mots, ["PRENOM"] = Traitement.Mots, ["PRENOMS"] = Traitement.Mots,
        ["PATRONYME"] = Traitement.Mots, ["SURNOM"] = Traitement.Mots, ["PSEUDONYME"] = Traitement.Mots,
        ["PSEUDO"] = Traitement.Mots, ["DESIGNATION"] = Traitement.Mots, ["DENOMINATION"] = Traitement.Mots,
        ["ENSEIGNE"] = Traitement.Mots, ["ADRESSE"] = Traitement.Mots, ["VOIE"] = Traitement.Mots,
        ["RUE"] = Traitement.Mots, ["LIEU"] = Traitement.Mots, ["LOGIN"] = Traitement.Mots,
        ["PAYEUR"] = Traitement.Mots, ["DESTINATAIRE"] = Traitement.Mots, ["EXPEDITEUR"] = Traitement.Mots,
        ["SIGNATAIRE"] = Traitement.Mots, ["CONJOINT"] = Traitement.Mots, ["INITIALES"] = Traitement.Mots,
        ["ALATTENTION"] = Traitement.Mots, ["ATTENTION"] = Traitement.Mots, ["IBAN"] = Traitement.Mots,
        ["BIC"] = Traitement.Mots, ["RIB"] = Traitement.Mots,
        // Les champs libres (SOC-021 CA04) : remplaces mot a mot, leur volume reste.
        ["COMMENTAIRE"] = Traitement.Mots, ["COMMENTAIRES"] = Traitement.Mots, ["NOTE"] = Traitement.Mots,
        ["NOTES"] = Traitement.Mots, ["OBSERVATION"] = Traitement.Mots, ["OBSERVATIONS"] = Traitement.Mots,
        ["TEXTE"] = Traitement.Mots, ["LIBRE"] = Traitement.Mots, ["MESSAGE"] = Traitement.Mots,
        ["CORPS"] = Traitement.Mots, ["BLOC"] = Traitement.Mots, ["REMARQUE"] = Traitement.Mots,
        ["REMARQUES"] = Traitement.Mots,
    };

    private static Regle ParLeNom(string colonne, string typeSql)
    {
        if (typeSql == "xml") return new Regle(Traitement.Xml, "colonne XML : chaque valeur par le nom de son attribut ou element");
        var segments = Segments(colonne);
        if (MarqueurPersonnel(segments) is not { } marqueur) return new Regle(Traitement.Garder, "nom sans segment personnel");
        if (segments.FirstOrDefault(Techniques.Contains) is { } technique)
            return new Regle(Traitement.Garder, $"segment « {marqueur} » leve par le segment technique « {technique} » (a relire)");
        var t = Personnels[marqueur];
        if (t == Traitement.DateNaissance && !EstDate(typeSql)) t = Traitement.Mots;
        return new Regle(t, $"nom de colonne : segment « {marqueur} » (a relire)");
    }

    private static string? MarqueurPersonnel(string[] segments) => segments.FirstOrDefault(Personnels.ContainsKey);

    /// <summary>« DRGPP_NOM_USAGE » -> DRGPP, NOM, USAGE ; « NomUsage » (XML) -> NOM, USAGE.</summary>
    public static string[] Segments(string nom)
        => [.. Decoupe().Split(nom).Where(s => s.Length > 0).Select(s => s.ToUpperInvariant())];

    [GeneratedRegex(@"[_\W]+|(?<=\p{Ll})(?=\p{Lu})")]
    private static partial Regex Decoupe();

    // ------------------------------------------------------------------ le catalogue

    private void Ajouter(string table, Traitement t, string raison, params string[] colonnes)
    {
        foreach (var c in colonnes) _explicites[(table, c)] = new Regle(t, "catalogue : " + raison);
    }

    private void Remplir()
    {
        // CLIENT (D:\RigApplication\...\Tables\CLIENT.sql:4-5,20-22,33) : nom, prenom, courriel, telephones, naissance.
        Ajouter("CLIENT", Traitement.Mots, "nom du client", "CLIEN_NOM", "CLIEN_PRENOM", "CLIEN_ABREGE");
        Ajouter("CLIENT", Traitement.Courriel, "courriel du client", "CLIEN_EMAIL");
        Ajouter("CLIENT", Traitement.Telephone, "telephone du client", "CLIEN_TELEPHONE", "CLIEN_TELEPHONE_PORTABLE", "CLIEN_FAX");
        Ajouter("CLIENT", Traitement.DateNaissance, "date de naissance du client", "CLIEN_DATE_NAISSANCE");
        Ajouter("CLIENT", Traitement.Mots, "champ libre (CA04)", "CLIEN_BLOC_NOTES");
        Ajouter("CLIENT", Traitement.Vider, "mot de passe du compte web", "CLIEN_MDP");
        Ajouter("CLIENT", Traitement.Garder, "numero (CA03)", "CLIEN_SIREN", "CLIEN_NUMERO_COMPTE", "CLIEN_COMPTE_COLLECTIF");

        // UTILISATEUR (UTILISATEUR.sql:10-11) : les agents.
        Ajouter("UTILISATEUR", Traitement.Mots, "nom de l'agent", "UTLST_NOM", "UTLST_PRENOM", "UTLST_ABREGE", "UTLST_INITIALES", "UTLST_LOGIN");
        Ajouter("UTILISATEUR", Traitement.Telephone, "telephone de l'agent", "UTLST_TEL_DIRECT");
        Ajouter("UTILISATEUR", Traitement.Mots, "champ libre (CA04)", "UTLST_TEXTE_DISPONIBILITE");
        Ajouter("UTILISATEUR", Traitement.Vider, "mot de passe", "UTLST_PASSWORD");

        // AUDIT (AUDIT.sql:8-9) : la ligne entiere avant et apres, en XML (un attribut par colonne).
        _explicites[("AUDIT", "AUDIT_XML")] = new Regle(Traitement.Xml, "catalogue : ligne journalisee (CA04), regle de chaque colonne", "AUDIT_TABLE");
        _explicites[("AUDIT", "AUDIT_XML_NEW")] = new Regle(Traitement.Xml, "catalogue : ligne journalisee (CA04), regle de chaque colonne", "AUDIT_TABLE");
        Ajouter("AUDIT", Traitement.Mots, "champ libre (CA04) : texte de la modification", "AUDIT_FULL_TEXT_MODIF");
        Ajouter("AUDIT", Traitement.Mots, "compte de l'agent (memes mots que UTLST_LOGIN)", "AUDIT_LOGIN");
        Ajouter("AUDIT", Traitement.Garder, "table et numero de la fiche (CA03)", "AUDIT_TABLE", "AUDIT_ID_TABLE");

        // EMAIL_ENVOI_TRACE (RIG_Send_Email\SQL\EMAIL_ENVOI_TRACE.sql:14-18) : trace des courriels.
        Ajouter("EMAIL_ENVOI_TRACE", Traitement.Courriel, "adresses de la trace (CA04)",
            "EMTRC_FROM", "EMTRC_TO", "EMTRC_BCC", "EMTRC_REPLY", "EMTRC_COMMAND_LINE");
        Ajouter("EMAIL_ENVOI_TRACE", Traitement.Mots, "champ libre de la trace (CA04)",
            "EMTRC_SUJET", "EMTRC_PIECES", "EMTRC_URLS", "EMTRC_ERREUR_MESSAGE", "EMTRC_STACKTRACE", "EMTRC_UTILISATEUR_WINDOWS");

        // EMAIL_ENVOYE : les courriels envoyes (objet, corps, adresses).
        Ajouter("EMAIL_ENVOYE", Traitement.Courriel, "adresses du courriel (CA04)", "EMAILENV_FROM", "EMAILENV_REPLY", "EMAILENV_COPY_TO", "EMAILENV_TO");
        Ajouter("EMAIL_ENVOYE", Traitement.Mots, "champ libre du courriel (CA04)", "EMAILENV_SUJET", "EMAILENV_CORPS");

        // ADRESSE_BASE : numero et voie remplaces ; commune, code postal, pays gardes (routage et
        // geographie des tests : seuls, ils ne designent personne).
        Ajouter("ADRESSE_BASE", Traitement.Mots, "adresse",
            "ADRBS_NUM_VOIE", "ADRBS_NOM_VOIE", "ADRBS_PRE_ADRESSE", "ADRBS_SUITE_ADRESSE", "ADRBS_BOITE_POSTALE",
            "ADRBS_LIEU_RATTACHE", "ADRBS_VALEUR_SPECIAL");
        Ajouter("ADRESSE_BASE", Traitement.Garder, "code ou commune : seul, il ne designe personne",
            "ADRBS_COMPLEMENT_NO", "ADRBS_TYPE_VOIE", "ADRBS_CODE_POSTAL", "ADRBS_LOCALITE_POSTALE", "ADRBS_PAYS",
            "ADRBS_CODE_ROUTAGE", "ADRBS_CEDEX", "ADRBS_DIVISION_TERRIT", "ADRBS_CODE_INSEE", "ADRBS_TYPE_SPECIAL");

        // PARTIE : les parties des affaires judiciaires.
        Ajouter("PARTIE", Traitement.Mots, "nom de la partie", "PRT_NOM", "PRT_DESIGNATION", "PRT_ABREGE", "PRT_REFERENCES");
        Ajouter("PARTIE", Traitement.DateNaissance, "date de naissance de la partie", "PRT_DATE_NAISSANCE");
        Ajouter("PARTIE", Traitement.Courriel, "courriel de la partie", "PRT_ADRESSE_MAIL");
        Ajouter("PARTIE", Traitement.Telephone, "telephone de la partie", "PRT_TELEPHONE", "PRT_FAX", "PRT_PORTABLE");
        Ajouter("PARTIE", Traitement.Mots, "champ libre (CA04)", "PRT_TEXTE_ASSISTANCE", "PRT_TEXTE_ESQUALITE", "PRT_TEXTE_COMPLEMENTAIRE");
        Ajouter("PARTIE", Traitement.Garder, "numero (CA03)", "PRT_SIREN", "PRT_NUMERO_SOURCE", "PRT_AIDE_JURIDIC_NUM");

        // DIRIGEANT_PP : les dirigeants personnes physiques (publies au registre : anonymises aussi, notes de SOC-021).
        Ajouter("DIRIGEANT_PP", Traitement.Mots, "nom du dirigeant",
            "DRGPP_PATRONYME", "DRGPP_PRENOMS_ETAT_CIVIL", "DRGPP_NOM_USAGE", "DRGPP_PRENOM_USAGE", "DRGPP_NOM_CONJOINT",
            "DRGPP_PSEUDONYME", "DRGPP_PRENOM_PSEUDO", "DRGPP_SURNOM", "DRGPP_LIEU_NAISSANCE", "DRGPP_DATE_NAISSANCE_NC",
            "DRGPP_NUM_PIECE_IDENT");
        Ajouter("DIRIGEANT_PP", Traitement.DateNaissance, "date de naissance du dirigeant", "DRGPP_DATE_NAISSANCE");
        Ajouter("DIRIGEANT_PP", Traitement.Mots, "champ libre (CA04)", "DRGPP_TEXTE_RPPP");
        Ajouter("DIRIGEANT_PP", Traitement.Garder, "numero (CA03)", "DRGPP_NUM_SIREN", "DRGPP_NUM_GESTION");
        Ajouter("DIRIGEANT_PP", Traitement.Garder, "code du departement ou de l'arrondissement : seul, il ne designe personne",
            "DRGPP_DEPARTEMENT_NAISS", "DRGPP_ARROND_NAISSANCE");

        // FACTURE : le client recopie sur la facture (la facture est signee : CA05).
        Ajouter("FACTURE", Traitement.Mots, "client recopie sur la facture",
            "FACTU_DESIGNATION_FACTURATION", "FACTU_LIGNE_ADRESSE_FACTURATION", "FACTU_SUITE_ADRESSE_FACTURATION",
            "FACTU_ALATTENTION", "FACTU_REFERENCES_CLIENT", "FACTU_COMMENTAIRE");
        Ajouter("FACTURE", Traitement.Garder, "numero, commune ou pays (CA03)",
            "FACTU_NUMERO_FACTURE", "FACTU_NUM_DEMANDE", "FACTU_CODE_POSTAL_FACTURATION", "FACTU_LOCALITE_POSTALE_FACTURATION",
            "FACTU_PAYS_FACTURATION", "FACTU_CLIEN_SIREN", "FACTU_CLIEN_TVA_INTRA", "FACTU_CLIEN_SIRET", "FACTU_NUMERO_GED_DONNEES_XML");
        Ajouter("FACTURE", Traitement.Garder, "signature gardee telle quelle : elle ne vaut plus (CA05)", "FACTU_SIGN_ELEC");

        // ENCAISSEMENT : le payeur (le reglement est signe : CA05).
        Ajouter("ENCAISSEMENT", Traitement.Mots, "payeur", "ENCMT_DESIGNATION_PAYEUR", "ENCMT_COMMENTAIRE");
        Ajouter("ENCAISSEMENT", Traitement.Garder, "banque ou compte du greffe : pas une personne",
            "ENCMT_DESIGNATION_BANQUE", "ENCMT_COMPTE_BANCAIRE_REMIS");
        Ajouter("ENCAISSEMENT", Traitement.Garder, "numero (CA03)", "ENCMT_NUMERO_TITRE", "ENCMT_GUID", "ENCMT_EXT_ID");
        Ajouter("ENCAISSEMENT", Traitement.Garder, "signature gardee telle quelle : elle ne vaut plus (CA05)", "ENCMT_SIGN_ELEC");
    }

    /// <summary>Les colonnes que le catalogue nomme (le banc verifie que chaque table de la story y est).</summary>
    public IEnumerable<(string Table, string Colonne, Regle Regle)> Explicites
        => _explicites.Select(e => (e.Key.Table, e.Key.Colonne, e.Value));

    private sealed class ComparateurCle : IEqualityComparer<(string Table, string Colonne)>
    {
        public bool Equals((string Table, string Colonne) a, (string Table, string Colonne) b)
            => string.Equals(a.Table, b.Table, StringComparison.OrdinalIgnoreCase)
               && string.Equals(a.Colonne, b.Colonne, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Table, string Colonne) c)
            => HashCode.Combine(c.Table.ToUpperInvariant(), c.Colonne.ToUpperInvariant());
    }
}
