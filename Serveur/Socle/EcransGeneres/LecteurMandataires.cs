using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mandataires » (PROC_MANDATAIRE) : les mandataires et praticiens du greffe (avocats, administrateurs, huissiers,
/// societes), la table MANDATAIRE de la base du greffe. Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : PROC_MANDATAIRE (D:\RigApplication\Source\RIG\DLL\Processus\PROC_Mandataire) n'edite qu'UN
/// mandataire, ouvert par son id ou en creation (fiche proc_mandataire.md, « DoInitObject ») ; la liste d'ou on l'ouvre est
/// la recherche des mandataires, D:\RigApplication\Source\RIG\DLL\RigOcxRecherche\FormRecherche_MANDATAIRE.cs:155-212 :
/// « SELECT TOP n ... From MANDATAIRE Where 1=1 [criteres] ORDER BY ordreSelect asc, MNDTR_NOM asc », ou ordreSelect met
/// les actifs (MNDTR_ACTIF = 1) avant les inactifs. La V2 exige un critere (l. 159-160), l'ecran V3 ouvre sans : tous les
/// mandataires, sans filtre, dans l'ordre de la V2 (puis l'id, pour un ordre stable entre homonymes). Un inactif reste liste,
/// apres les actifs : ses precisions le disent. (La V2 grise et refuse au choix les lignes ACTIVES, l. 289-321 : test inverse,
/// non repris.)
///
/// NOM : celui de la V2, Mandataire.NomComplet (Outils\DLL\RigMetier\JUDICIAIRE\Mandataire.cs:42) = Outils.FormerNomComplet
/// (Outils\DLL\RigMetier\JUDICIAIRE\Fonctions diverses\Outils.cs:663-669) : l'abrege d'edition de la civilite
/// (CIVILITE.R_CIVILITE_ABREGE_EDIT : « Me », « M. », « Mme ») puis le nom, sauf pour PM et ND (le nom seul).
///
/// GROUPES : MNDTR_CIVILITE = 'PM' -> « Personne morale » (la V2 pose la civilite PM a une personne morale :
/// OPE_SAISIE_MANDATAIRE.cs:212-222, et CIVILITE.R_CIVILITE_TOUJOURS_PP = 0 pour elle seule, RIG_DEV lu le 25/11/2026) ;
/// toute autre civilite -> « Personne physique », y compris ND (« civilite non determinee »), que ses precisions nomment.
///
/// REGLE D'ETAT (sections du descripteur : 0 Identite, 1 Adresse et contacts, 2 Abrege, 3 Client de facturation,
/// 4 Qualification ; seule l'identite est obligatoire) :
///   - Identite (0) : civilite ET designation (MNDTR_CIVILITE, MNDTR_NOM) renseignees -- les deux champs obligatoires de la
///     fiche (proc_mandataire.md, « Controles reels ») ;
///   - Adresse et contacts (1) : une adresse (MNDTR_ID_ADRBS), un telephone, un fax ou un courriel ;
///   - Abrege (2) : un abrege (MNDTR_ABREGE) : le mandataire est « repertorie » (OPE_SAISIE_MANDATAIRE.cs:124) ;
///   - Client de facturation (3) : MNDTR_ID_CLIEN renseigne et different de -1 (la V2 lit -1 comme « aucun client »,
///     OPE_SAISIE_MANDATAIRE.cs:98) ;
///   - Qualification (4) : une qualification de la profession ou des infos complementaires.
///   identite complete                  -> Validee « Enregistre » ;
///   identite incomplete, autre section -> EnCours « En saisie » ;
///   identite incomplete, rien d'autre  -> AFaire « A completer ».
///
/// GRILLE (plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026) : chaque ligne porte son id (MNDTR_ID_MNDTR) et ses cellules
/// dans l'ordre des Colonnes du descripteur (Désignation = MNDTR_NOM, Civilité = le code, Abrégé, SIREN, Courriel, Qualification =
/// le code de QUALIF_PROFESSION, Actif « oui ») : la grille les ouvre en modification, EcrivainMandataires.cs les ecrit.
/// RECHERCHE (IRechercheEcran) : les criteres de la V2, FormRecherche_MANDATAIRE.cs:179-198 -- « Désignation » (MNDTR_NOM LIKE
/// %x%, « * » vaut « % »), « Numéro CNBF » (=), « CNBF de structure » (=), « SIREN » (=) ; le champ de recherche cherche le nom ou
/// l'abrege.
/// FICHE (IFicheEcran) : les sections du descripteur (OPE_SAISIE_MANDATAIRE, « Contrôles réels & liaisons » de
/// proc_mandataire.md) ; l'adresse est la fiche reprise rec.Adresse (ADRESSE_BASE), le client de facturation rec.Client ;
/// « Utilisations » compte ce qui reference le mandataire (LiaisonsMandataire.cs), ce qui interdit sa suppression.
/// </summary>
internal sealed class LecteurMandataires : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MANDATAIRE";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string PersonnePhysique = "Personne physique", PersonneMorale = "Personne morale";

    /// <summary>Les colonnes de la grille (descripteur), au mot pres : les cles des cellules d'un geste.</summary>
    internal const string ColDesignation = "Désignation", ColCivilite = "Civilité", ColAbrege = "Abrégé", ColSiren = "SIREN",
                          ColCourriel = "Courriel", ColQualification = "Qualification", ColActif = "Actif";

    internal const string CritereDesignation = "Désignation", CritereCnbf = "Numéro CNBF", CritereCnbfStructure = "CNBF de structure",
                          CritereSiren = "SIREN";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDesignation, CritereCnbf, CritereCnbfStructure, CritereSiren];

    private const string Requete = """
        SELECT TOP (@plafond) m.MNDTR_ID_MNDTR, m.MNDTR_CIVILITE, m.MNDTR_NOM, m.MNDTR_ABREGE, m.MNDTR_SIREN, m.MNDTR_NUMERO_CNBF,
               m.MNDTR_ID_ADRBS, m.MNDTR_TELEPHONE, m.MNDTR_FAX, m.MNDTR_ADRESSE_MAIL, m.MNDTR_ID_CLIEN,
               m.MNDTR_QUALIF_PROFESSION, m.MNDTR_TEXTE, m.MNDTR_ACTIF, c.R_CIVILITE_ABREGE_EDIT,
               COUNT(*) OVER () AS Total
        FROM v2.MANDATAIRE AS m
        LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = m.MNDTR_CIVILITE
        WHERE 1 = 1 /*filtres*/
        ORDER BY CASE WHEN m.MNDTR_ACTIF = 1 THEN 0 ELSE 1 END, m.MNDTR_NOM, m.MNDTR_ID_MNDTR
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
            f.Ajouter("m.MNDTR_NOM LIKE @texte OR m.MNDTR_ABREGE LIKE @texte", ("@texte", FormeLigne.Contient(recherche.Texte)));
        // FormRecherche_MANDATAIRE.cs:179-184 : le nom se cherche en LIKE %x%, « * » est le joker de la V2.
        if (recherche.Critere(CritereDesignation) is { } nom)
            f.Ajouter("m.MNDTR_NOM LIKE @nom", ("@nom", FormeLigne.Contient(nom).Replace("*", "%", StringComparison.Ordinal)));
        if (recherche.Critere(CritereCnbf) is { } cnbf) f.Ajouter("m.MNDTR_NUMERO_CNBF = @cnbf", ("@cnbf", cnbf));
        if (recherche.Critere(CritereCnbfStructure) is { } structure)
            f.Ajouter("m.MNDTR_NUMERO_CNBF_STRUCTURE = @structure", ("@structure", structure));
        if (recherche.Critere(CritereSiren) is { } siren)
            f.Ajouter("m.MNDTR_SIREN = @siren", ("@siren", siren.Replace(" ", "", StringComparison.Ordinal)));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    /// <summary>Le nom de la V2 (Outils.FormerNomComplet) : l'abrege d'edition de la civilite puis le nom, sauf PM et ND.</summary>
    internal static string NomComplet(string civilite, string abregeEdition, string nom)
        => civilite.Equals("PM", StringComparison.OrdinalIgnoreCase) || civilite.Equals("ND", StringComparison.OrdinalIgnoreCase)
            ? nom : $"{abregeEdition} {nom}".Trim();

    private static LigneLue Ligne(SqlDataReader r)
    {
        var civilite = FormeLigne.Texte(r, "MNDTR_CIVILITE");
        var nom = FormeLigne.Texte(r, "MNDTR_NOM");
        var abrege = FormeLigne.Texte(r, "MNDTR_ABREGE");
        var siren = FormeLigne.Texte(r, "MNDTR_SIREN");
        var cnbf = FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF");
        var actif = Convert.ToBoolean(r["MNDTR_ACTIF"], CultureInfo.InvariantCulture);
        var morale = civilite.Equals("PM", StringComparison.OrdinalIgnoreCase);
        var nonDeterminee = civilite.Equals("ND", StringComparison.OrdinalIgnoreCase);

        // La regle d'etat (en tete du fichier).
        var identite = civilite.Length > 0 && nom.Length > 0;
        var contacts = r["MNDTR_ID_ADRBS"] is not DBNull || FormeLigne.Texte(r, "MNDTR_TELEPHONE").Length > 0
                       || FormeLigne.Texte(r, "MNDTR_FAX").Length > 0 || FormeLigne.Texte(r, "MNDTR_ADRESSE_MAIL").Length > 0;
        var client = r["MNDTR_ID_CLIEN"] is int idClient && idClient != -1;
        var qualification = FormeLigne.Texte(r, "MNDTR_QUALIF_PROFESSION").Length > 0 || FormeLigne.Texte(r, "MNDTR_TEXTE").Length > 0;
        int[] faites = [.. new[] { (identite, 0), (contacts, 1), (abrege.Length > 0, 2), (client, 3), (qualification, 4) }
            .Where(m => m.Item1).Select(m => m.Item2)];
        var etat = identite ? "Validee" : faites.Length > 0 ? "EnCours" : "AFaire";

        var affiche = nom.Length > 0 ? NomComplet(civilite, FormeLigne.Texte(r, "R_CIVILITE_ABREGE_EDIT"), nom)
            : abrege.Length > 0 ? abrege : $"Mandataire {FormeLigne.Texte(r, "MNDTR_ID_MNDTR")}";
        var precisions = FormeLigne.Joindre(
            civilite.Length == 0 ? "civilité à saisir" : nonDeterminee ? "civilité non déterminée" : null,
            nom.Length == 0 ? "désignation à saisir" : null,
            abrege.Length > 0 ? $"abrégé {abrege}" : "non répertorié",
            siren.Length > 0 ? $"SIREN {FormeLigne.Siren(siren)}" : null,
            cnbf.Length > 0 ? $"CNBF {cnbf}" : null,
            actif ? null : "inactif");
        return new LigneLue(affiche, precisions, morale ? PersonneMorale : PersonnePhysique, etat, faites)
        {
            Id = FormeLigne.Texte(r, "MNDTR_ID_MNDTR"),
            Valeurs = [nom, civilite, abrege, siren, FormeLigne.Texte(r, "MNDTR_ADRESSE_MAIL"), FormeLigne.Texte(r, "MNDTR_QUALIF_PROFESSION"),
                       FormeLigne.Case(actif)],
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (CriteresConsultation.Entier(id) is not { } n) return null;
        string nom, affiche, civilite, siren, abrege, cnbf, cnbfStructure, courriel, telephone, fax, qualif, texte, adresse, client;
        bool actif;
        await using (var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT m.MNDTR_CIVILITE, m.MNDTR_NOM, m.MNDTR_SIREN, m.MNDTR_ABREGE, m.MNDTR_NUMERO_CNBF, m.MNDTR_NUMERO_CNBF_STRUCTURE,
                   m.MNDTR_ADRESSE_MAIL, m.MNDTR_TELEPHONE, m.MNDTR_FAX, m.MNDTR_QUALIF_PROFESSION, m.MNDTR_TEXTE, m.MNDTR_ACTIF,
                   c.R_CIVILITE_ABREGE_EDIT,
                   CONCAT_WS(' ', a.NumeroVoie, a.ComplementNumero, a.TypeVoie, a.NomVoie, a.SuiteAdresse, a.CodePostal, a.Localite) AS Adresse,
                   cl.Nom AS Client
            FROM v2.MANDATAIRE AS m
            LEFT JOIN v2.CIVILITE AS c ON c.R_CIVILITE_VALEUR = m.MNDTR_CIVILITE
            LEFT JOIN rec.Adresse AS a ON a.Id = m.MNDTR_ID_ADRBS
            LEFT JOIN rec.Client AS cl ON cl.Id = m.MNDTR_ID_CLIEN
            WHERE m.MNDTR_ID_MNDTR = @id
            """, ct, new SqlParameter("@id", n)))
        {
            if (!await r.ReadAsync(ct)) return null;
            civilite = FormeLigne.Texte(r, "MNDTR_CIVILITE"); nom = FormeLigne.Texte(r, "MNDTR_NOM"); siren = FormeLigne.Texte(r, "MNDTR_SIREN");
            abrege = FormeLigne.Texte(r, "MNDTR_ABREGE"); cnbf = FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF");
            cnbfStructure = FormeLigne.Texte(r, "MNDTR_NUMERO_CNBF_STRUCTURE"); courriel = FormeLigne.Texte(r, "MNDTR_ADRESSE_MAIL");
            telephone = FormeLigne.Texte(r, "MNDTR_TELEPHONE"); fax = FormeLigne.Texte(r, "MNDTR_FAX");
            qualif = FormeLigne.Texte(r, "MNDTR_QUALIF_PROFESSION"); texte = FormeLigne.Texte(r, "MNDTR_TEXTE");
            actif = Convert.ToBoolean(r["MNDTR_ACTIF"], CultureInfo.InvariantCulture);
            adresse = FormeLigne.Texte(r, "Adresse"); client = FormeLigne.Texte(r, "Client");
            affiche = nom.Length > 0 ? NomComplet(civilite, FormeLigne.Texte(r, "R_CIVILITE_ABREGE_EDIT"), nom) : abrege;
        }
        var comptes = await LiaisonsMandataire.CompterAsync((sql, p) => LectureV2.LireAsync(baseGreffe, sql, ct, p), n, ct);
        // Modifiable : ce que la grille saisit (les colonnes de meme libelle) et qu'EcrivainMandataires ecrit.
        static ChampFiche M(string libelle, string valeur) => new(libelle, valeur) { Modifiable = true };
        static ChampFiche L(string libelle, string valeur) => new(libelle, valeur);
        return new FicheEcran(Procedure, id, affiche,
        [
            new("Identité", [M(ColCivilite, civilite), M(ColDesignation, nom), M(ColSiren, siren)]),
            new("Adresse et contacts", [L("Adresse", adresse), L("Téléphone", telephone), L("Fax", fax), M(ColCourriel, courriel)]),
            new("Abrégé", [L("Répertorié", FormeLigne.Case(abrege.Length > 0)), M(ColAbrege, abrege),
                           L("Numéro CNBF", FormeLigne.Joindre(cnbf, cnbfStructure.Length > 0 ? $"structure {cnbfStructure}" : null))]),
            new("Client de facturation", [L("Client de facturation", client)]),
            new("Qualification", [M(ColQualification, qualif), L("Infos complémentaires", texte), M(ColActif, FormeLigne.Case(actif))]),
            new("Utilisations", [new ChampFiche("Liaisons", "")
            {
                Colonnes = ["Liaison", "Nombre"],
                Lignes = [.. comptes.Select(c => new[] { c.Liaison.Libelle, c.Nombre.ToString(CultureInfo.InvariantCulture) })],
            }]),
        ]);
    }
}
