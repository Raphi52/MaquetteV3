using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mentions en marge » (PROC_MMA, dans l'ecran reuni « Corriger un dossier RCS ») : les demandes de mention en marge des
/// inscriptions (suretes), une recherche par numero d'inscription, et la FICHE (en lecture) d'une demande. Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 3, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MMA (D:\RigApplication\Source\RIG\DLL\Processus\Inscriptions\PROC_MMA, fiche proc_mma.md), une
/// demande MMA sur UNE inscription : « MMA sur l'inscription <numero> », DMND_ID_INSCR = l'inscription (PROC_MMA.cs:56-58).
///
/// FILTRE DE LA LISTE : les demandes du processus MMA (DEMANDE.DMND_CODE_PROSS = 'MMA', MetierCodeProcessus,
/// PROC_MMA.Designer.cs:162) rattachees a leur inscription (DMND_ID_INSCR -> INSCRIPTION.INEDT_ID_INSCR). L'inscription est
/// une FICHE REPRISE de la V3 : rec.Inscription (Id = INEDT_ID_INSCR, NumeroInscription, TypeInscription, IdBateau ;
/// Serveur/Recopie/FichesV3.cs:196-209) -- la base V3 n'a pas de v2.INSCRIPTION. ORDRE : date de creation de la demande
/// decroissante. fix-ok: mesure le 26/11/2026 sur la copie de dev (serveur 5231, journal) -- « Invalid object name
/// 'v2.INSCRIPTION' » : la recopie range INSCRIPTION dans rec.Inscription ; le banc, qui creait v2.INSCRIPTION, ne le voyait pas.
///
/// GROUPES (descripteur) : « Inscriptions maritimes » = l'inscription porte un bateau (INEDT_ID_BTEAU, l'etape Bateau que la
/// V2 n'affiche qu'alors, proc_mma.md « ChargerDonnees » 3) ; « Autres inscriptions » sinon.
///
/// REGLE D'ETAT, par l'etat de la demande (ETAT_DEMANDE) et les issues de PROC_MMA (Validee, Reclamee, Refusee, En attente,
/// proc_mma.md « DoGenererLesDemandesEditions ») :
///   - « Validée » (Validee) : V (validee) ou T (terminee) ;
///   - « Refusée » (Rejetee) : O (refus) ;
///   - « En attente » (EnAttente) : N (reclamation) ou W (en attente) -- le descripteur range la reclamation avec l'attente ;
///   - « En cours » (EnCours) : en cours de saisie (DMND_EN_COURS) ou interrompue (I) ;
///   - « À traiter » (AFaire) : toute autre (arrivee A, qualifiee Q...).
///
/// RECHERCHE (IRechercheEcran) : « Numéro d'inscription » (INEDT_NUM_INSCRIPTION) ; le champ de recherche cherche ce numero ou
/// le nom de la demande. NON CHERCHES (nommes) : « Débiteur » et « Créancier », que la V2 lit dans les debiteurs et creanciers
/// de l'inscription (etapes Debiteurs, Creance), non lus par cet ecran.
///
/// FICHE (IFicheEcran), EN LECTURE : Mention en marge (la derniere mention de l'inscription, MENTION_MARGINALE par date
/// decroissante, proc_mma.md « Chargement des mentions marginales ») et Décision (l'issue lue de l'etat de la demande).
///
/// GESTES : AUCUN ECRIVAIN (nomme). Valider et Refuser sont FACTURES dans RIG v2 (DoGetArticles : article de l'inscription,
/// certificat « 21-98 » pour les types V, N, G, O, K, NP ; droits de rejet au refus) et produisent des editions (lettres,
/// bordereau, LRAR) ; Reclamer et Attendre produisent une lettre. Une demande facturee ne se remplace pas par le journal des
/// gestes (decision du chef) et ni la facturation ni les editions ne sont reprises : l'ecran dit « Démonstration ».
/// </summary>
internal sealed class LecteurMentionsEnMarge : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_MMA";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Maritimes = "Inscriptions maritimes", Autres = "Autres inscriptions";

    /// <summary>Le critere du descripteur cherche dans la base.</summary>
    internal const string CritereNumero = "Numéro d'inscription";

    /// <summary>Les sections et champs de la fiche, aux titres et libelles du descripteur (ModifierAffaire.cs).</summary>
    internal const string SectionMention = "Mention en marge", SectionDecision = "Décision";

    internal const string ChampMention = "Mention marginale", ChampDate = "Date de la mention", ChampDecision = "Décision";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero];

    private const string Requete = """
        SELECT TOP (@plafond) d.DMND_ID_DMND, d.DMND_NOM, d.DMND_ETAT_DEMANDE, d.DMND_EN_COURS, d.DMND_DATE_CREATION,
               e.R_ETATDEMAND_LIBELLE_ECRAN, i.NumeroInscription AS INEDT_NUM_INSCRIPTION, i.TypeInscription AS INEDT_TYPE_INSCRIPTION, i.IdBateau AS INEDT_ID_BTEAU,
               COUNT(*) OVER () AS Total
        FROM v2.DEMANDE AS d
        JOIN rec.Inscription AS i ON i.Id = d.DMND_ID_INSCR
        LEFT JOIN v2.ETAT_DEMANDE AS e ON e.R_ETATDEMAND_VALEUR = d.DMND_ETAT_DEMANDE
        WHERE d.DMND_CODE_PROSS = 'MMA' /*recherche*/
        ORDER BY d.DMND_DATE_CREATION DESC, d.DMND_ID_DMND DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*recherche*/", ""), Ligne, ct, new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtres = new List<string>();
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(i.NumeroInscription LIKE @texte OR d.DMND_NOM LIKE @texte)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereNumero) is { } numero)
        {
            filtres.Add("i.NumeroInscription LIKE @numero");
            parametres.Add(new SqlParameter("@numero", FormeLigne.Contient(numero)));
        }
        var sql = Requete.Replace("/*recherche*/", filtres.Count == 0 ? "" : " AND " + string.Join(" AND ", filtres));
        return LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    /// <summary>La regle d'etat (en tete du fichier).</summary>
    internal static string Etat(string etatDemande, bool enCours) => enCours ? "EnCours" : etatDemande switch
    {
        "V" or "T" => "Validee",
        "O" => "Rejetee",
        "N" or "W" => "EnAttente",
        "I" => "EnCours",
        _ => "AFaire",
    };

    /// <summary>L'issue de la demande dans la liste « Décision » du descripteur ; vide tant qu'aucune n'est prise.</summary>
    private static string Decision(string etatDemande) => etatDemande switch
    {
        "V" or "T" => "Valider", "N" => "Réclamer", "O" => "Refuser", "W" => "Attendre", _ => "",
    };

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etat = FormeLigne.Texte(r, "DMND_ETAT_DEMANDE");
        var creation = FormeLigne.DateDe(r, "DMND_DATE_CREATION");
        var precisions = FormeLigne.Joindre(
            $"Inscription {FormeLigne.Texte(r, "INEDT_NUM_INSCRIPTION")}", FormeLigne.Texte(r, "INEDT_TYPE_INSCRIPTION"),
            creation is null ? null : $"demande du {FormeLigne.Date(creation)}",
            FormeLigne.Texte(r, "R_ETATDEMAND_LIBELLE_ECRAN").ToLowerInvariant());
        var nom = FormeLigne.Texte(r, "DMND_NOM") is { Length: > 0 } n ? n : $"MMA sur l'inscription {FormeLigne.Texte(r, "INEDT_NUM_INSCRIPTION")}";
        return new LigneLue(nom, precisions, r["INEDT_ID_BTEAU"] is DBNull ? Autres : Maritimes, Etat(etat, r["DMND_EN_COURS"] is true), [])
        {
            Id = FormeLigne.Texte(r, "DMND_ID_DMND"),
        };
    }

    // ------------------------------------------------------------------ LA FICHE (en lecture)

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        await using var r = await LectureV2.LireAsync(baseGreffe, """
            SELECT d.DMND_NOM, d.DMND_ETAT_DEMANDE, i.NumeroInscription AS INEDT_NUM_INSCRIPTION, m.MNMRG_TEXTE_MENTION, m.MNMRG_DATE
            FROM v2.DEMANDE AS d
            JOIN rec.Inscription AS i ON i.Id = d.DMND_ID_INSCR
            OUTER APPLY (SELECT TOP (1) mm.MNMRG_TEXTE_MENTION, mm.MNMRG_DATE FROM v2.MENTION_MARGINALE AS mm
                         WHERE mm.MNMRG_ID_INSCR = i.Id ORDER BY mm.MNMRG_DATE DESC, mm.MNMRG_ID_MNMRG DESC) AS m
            WHERE d.DMND_ID_DMND = @id AND d.DMND_CODE_PROSS = 'MMA'
            """, ct, new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var nom = FormeLigne.Texte(r, "DMND_NOM") is { Length: > 0 } x ? x : $"MMA sur l'inscription {FormeLigne.Texte(r, "INEDT_NUM_INSCRIPTION")}";
        return new FicheEcran(Procedure, id, nom,
        [
            new SectionFiche(SectionMention,
                [new(ChampMention, FormeLigne.Texte(r, "MNMRG_TEXTE_MENTION")), new(ChampDate, FormeLigne.DateFiche(FormeLigne.DateDe(r, "MNMRG_DATE")))]),
            new SectionFiche(SectionDecision, [new(ChampDecision, Decision(FormeLigne.Texte(r, "DMND_ETAT_DEMANDE")))]),
        ]);
    }
}
