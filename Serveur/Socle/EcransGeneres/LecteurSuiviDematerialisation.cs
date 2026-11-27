using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Suivi dématérialisation » (PROC_SDEMATJUD) : les publicités JAL et les annonces BODACC des événements de procédure, et
/// où en est leur parution. Une FILE en deux listes, les deux onglets de RIG v2 (un métier par onglet, PROC_SDEMATJUD.cs:86-92) :
/// chaque groupe a son plafond et son total (LignesLues.TotalParGroupe). Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 9, 27/11/2026.
///
/// SOURCE (ancien RIG) : proc_sdematjud.md (Détail OPE, qui fait foi) et Source\RIG\DLL\Processus\PROC_SDEMATJUD\ :
///   - les critères par défaut (OPE_DATE_SDEMATJUD.cs:70-75) : la date de DÉCISION (EVNPR_DATE) du jour moins un mois à
///     aujourd'hui, bornes à 0 h et à 24 h, élargie aux événements sans date (« OR EVNPR_DATE IS NULL »,
///     SdematJud\MetierSdematJud.cs:303-304, :420-421) ; ni journal, ni « parution sans retour », ni numéro d'affaire ;
///   - JAL (_GetSqlDetailsSdematJudJal, MetierSdematJud.cs:225-337) : PUBLICITE, son événement (EVENEMENT_PROCEDURE, CODE_EVE_PRO,
///     jointures internes), la procédure collective ou la prévention, le bordereau JAL, le journal (DESTINATAIRE_INSTITUTIONNEL) et
///     les documents reçus en GED (VIDIGED_INDEX de nature « JAL » ou « FACTJAL ») ; puis le REGROUPEMENT de _GetSdematJUDJal
///     (:618-673) : une ligne par publicité ET date de réception GED, parution et facture fusionnées ; une publicité qui a deux
///     documents de la même nature (ou une ligne en double) est un DOUBLON, « Publicité déjà existante » (SdematJudDetail.cs:171-179) ;
///   - BODACC (_GetSqlDetailsSdematJudBodacc, :351-486) : BODACC et les mêmes jointures, le bordereau BODACC, sans GED ni regroupement,
///     et TOUJOURS « BDCCR_TYPE_BODACC like 'P%' » (:428-436) ; une annonce d'état « 9 » est rejetée, « Le boddac a été rejeté »
///     (SdematJudDetail.cs:175 -- le texte de RIG v2, faute comprise) ;
///   - l'affaire : le numéro de PC s'il existe, sinon celui de la prévention (:556-564, :722-730).
/// ORDRE : RIG v2 n'en a pas (orderBy = "", :327, :475) ; ici la publicité ou l'annonce, par identifiant.
/// ETAT -- ce que RIG v2 sait de chaque ligne, sa grille n'ayant pas de colonne d'état : « En erreur » (Rejetee) pour une ligne que
/// RIG v2 colore en rouge (HasVisibleError, DataGridViewSdematJud.cs:477-480 : doublon JAL, BODACC rejeté) ; « Parue » (Validee) avec
/// une date de parution (PBLCT_DATE_REALISATION, BDCCR_DATE_PUBLICITE : la colonne « Date parution ») ; « Sans retour de parution »
/// (EnCours) pour une ligne envoyée et pas encore parue -- le critère « Parution sans retour » de RIG v2 (JAL :309-312,
/// PBLCT_DATE_ENVOI_ELECTRONIQUE ; BODACC :438-447, BDCCR_DATE_ENVOI) ; « Non envoyée » (AFaire) sinon.
///
/// CE QUE LA V3 NE REPREND PAS (nommé) : les autres critères (type de date « Envoi EDI » ou « Parution », dates saisies, journal,
/// parution sans retour, numéro d'affaire, :289-320) -- la forme File n'en transmet aucun ; les garde-fous de volume (500 / 5000,
/// OPE_RECHERCHE_JAL.cs:104-115) -- le plafond des écrans les remplace ; le détail d'une ligne (OPE_DETAIL, une fiche modale en
/// lecture) ; et les trois actions de l'onglet JAL : « Valider » GÉNÈRE des éditions de pièces numérisées (une demande par affaire
/// et une DemandeEdition par document GED coché, OPE_RESULTAT_JAL.cs:133-200) et « Éditer les détails » un classeur Excel
/// (ResultatRechercheRapport) -- la V3 n'a ni génération d'édition ni édition Excel : aucun écrivain, l'écran le dit.
/// </summary>
internal sealed class LecteurSuiviDematerialisation : ILecteurEcran
{
    public string Procedure => "PROC_SDEMATJUD";

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot près.</summary>
    internal const string Jal = "JAL (journal d'annonces légales)", Bodacc = "BODACC";

    // La plage par défaut : la date de décision, du jour moins un mois (0 h) à aujourd'hui (24 h), ou sans date.
    private const string DateDecision = "((e.EVNPR_DATE >= @du AND e.EVNPR_DATE < @lendemain) OR e.EVNPR_DATE IS NULL)";

    private const string Affaire = """
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = e.EVNPR_ID_PRCLL
        LEFT JOIN v2.PREVENTION AS pv ON pv.PREVE_ID_PREVE = e.EVNPR_ID_PREVE
        """;

    // MetierSdematJud.cs:619-635 : un doublon = deux lignes d'une publicité au même document facture, ou au même document de
    // parution ; :638-665 : une ligne par publicité et date de réception GED, parution et facture fusionnées, le doublon valant
    // pour toute la publicité. LectureV2 n'accepte qu'un SELECT : les étapes sont des tables dérivées, pas un WITH.
    private static readonly string RequeteJal = $$"""
        SELECT TOP (@plafond) l.*, COUNT(*) OVER () AS Total
        FROM (SELECT PBLCT_ID_PBLCT, VGIDX_DATE_RECEPTION, MAX(PBLCT_DATE_REALISATION) AS DateParution,
                     MAX(PBLCT_DATE_ENVOI_ELECTRONIQUE) AS DateEnvoi, MAX(PBLCT_NUM_PARUTION) AS NumParution, MAX(EVNPR_DATE) AS DateEvenement,
                     MAX(R_CODEEVEPRO_LIBELLE_ECRAN) AS Evenement, MAX(PRCLL_NUM_PC) AS NumPc, MAX(PRCLL_NOM_AFFAIRE) AS NomPc,
                     MAX(PREVE_NUM_PRV) AS NumPrv, MAX(PREVE_NOM_AFFAIRE) AS NomPrv, MAX(BRDRJ_DATE_CREA_XML) AS DateEdi,
                     MAX(DTINS_LIBELLE) AS Journal, MAX(IdJal) AS IdJal, MAX(IdFacture) AS IdFacture,
                     MAX(MAX(Doublon)) OVER (PARTITION BY PBLCT_ID_PBLCT) AS Doublon
              FROM (SELECT r.*, CASE WHEN COUNT(*) OVER (PARTITION BY r.PBLCT_ID_PBLCT, r.IdFacture) > 1
                                       OR COUNT(*) OVER (PARTITION BY r.PBLCT_ID_PBLCT, r.IdJal) > 1 THEN 1 ELSE 0 END AS Doublon
                    FROM (SELECT p.PBLCT_ID_PBLCT, p.PBLCT_DATE_REALISATION, p.PBLCT_DATE_ENVOI_ELECTRONIQUE, p.PBLCT_NUM_PARUTION, e.EVNPR_DATE,
                                 c.R_CODEEVEPRO_LIBELLE_ECRAN, pc.PRCLL_NUM_PC, pc.PRCLL_NOM_AFFAIRE, pv.PREVE_NUM_PRV, pv.PREVE_NOM_AFFAIRE,
                                 bj.BRDRJ_DATE_CREA_XML, d.DTINS_LIBELLE, v.VGIDX_DATE_RECEPTION,
                                 CASE WHEN v.VGIDX_NATURE_DOC = 'JAL' THEN v.VGIDX_ID_VGIDX ELSE 0 END AS IdJal,
                                 CASE WHEN v.VGIDX_NATURE_DOC = 'FACTJAL' THEN v.VGIDX_ID_VGIDX ELSE 0 END AS IdFacture
                          FROM v2.PUBLICITE AS p
                          JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_ID_EVNPR = p.PBLCT_ID_EVNPR
                          JOIN v2.CODE_EVE_PRO AS c ON e.EVNPR_CODE_EVE_PRO = c.R_CODEEVEPRO_VALEUR
                          {{Affaire}}
                          LEFT JOIN v2.BORDEREAU_JAL AS bj ON bj.BRDRJ_NUM_BORDEREAU = p.PBLCT_NUM_BORDEREAU
                          LEFT JOIN v2.DESTINATAIRE_INSTITUTIONNEL AS d ON d.DTINS_CODE = p.PBLCT_DESTINATAIRE
                          LEFT JOIN v2.VIDIGED_INDEX AS v ON v.VGIDX_ID_PBLCT = p.PBLCT_ID_PBLCT
                                                          AND (v.VGIDX_NATURE_DOC = 'JAL' OR v.VGIDX_NATURE_DOC = 'FACTJAL')
                          WHERE {{DateDecision}}) AS r) AS dbl
              GROUP BY PBLCT_ID_PBLCT, VGIDX_DATE_RECEPTION) AS l
        ORDER BY l.PBLCT_ID_PBLCT, l.VGIDX_DATE_RECEPTION
        """;

    private static readonly string RequeteBodacc = $$"""
        SELECT TOP (@plafond) b.BDCCR_ID_BDCRC, b.BDCCR_DATE_PUBLICITE AS DateParution, b.BDCCR_DATE_ENVOI AS DateEnvoi,
               b.BDCCR_NUM_PARUTION AS NumParution, b.BDCCR_ETAT_BODACC, e.EVNPR_DATE AS DateEvenement,
               c.R_CODEEVEPRO_LIBELLE_ECRAN AS Evenement, pc.PRCLL_NUM_PC AS NumPc, pc.PRCLL_NOM_AFFAIRE AS NomPc,
               pv.PREVE_NUM_PRV AS NumPrv, pv.PREVE_NOM_AFFAIRE AS NomPrv, br.BRDRC_DATE_CREA_XML AS DateEdi, COUNT(*) OVER () AS Total
        FROM v2.BODACC AS b
        JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_ID_EVNPR = b.BDCCR_ID_EVNPR
        JOIN v2.CODE_EVE_PRO AS c ON e.EVNPR_CODE_EVE_PRO = c.R_CODEEVEPRO_VALEUR
        {{Affaire}}
        LEFT JOIN v2.BORDEREAU AS br ON br.BRDRC_NUMERO_BORDEREAU = b.BDCCR_NUM_BORDEREAU
        WHERE {{DateDecision}} AND b.BDCCR_TYPE_BODACC LIKE 'P%'
        ORDER BY b.BDCCR_ID_BDCRC
        """;

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        SqlParameter[] Parametres() =>
        [
            new("@du", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date.AddMonths(-1) },
            new("@lendemain", System.Data.SqlDbType.DateTime) { Value = aujourdhui.Date.AddDays(1) },
            new("@plafond", plafond),
        ];
        var lignes = new List<LigneLue>();
        var totaux = new Dictionary<string, int> { [Jal] = 0, [Bodacc] = 0 };
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteJal, ct, Parametres()))
            while (await r.ReadAsync(ct))
            {
                totaux[Jal] = Convert.ToInt32(r["Total"], System.Globalization.CultureInfo.InvariantCulture);
                var erreur = Convert.ToInt32(r["Doublon"], System.Globalization.CultureInfo.InvariantCulture) == 1 ? "Publicité déjà existante" : null;
                var id = Convert.ToInt32(r["PBLCT_ID_PBLCT"], System.Globalization.CultureInfo.InvariantCulture);
                lignes.Add(Ligne(r, $"Publicité {id}", Jal, erreur, FormeLigne.Texte(r, "Journal"),
                    Convert.ToInt32(r["IdJal"], System.Globalization.CultureInfo.InvariantCulture) != 0 ? "parution reçue" : null,
                    Convert.ToInt32(r["IdFacture"], System.Globalization.CultureInfo.InvariantCulture) != 0 ? "facture reçue" : null));
            }
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteBodacc, ct, Parametres()))
            while (await r.ReadAsync(ct))
            {
                totaux[Bodacc] = Convert.ToInt32(r["Total"], System.Globalization.CultureInfo.InvariantCulture);
                var erreur = FormeLigne.Texte(r, "BDCCR_ETAT_BODACC").Trim() == "9" ? "Le boddac a été rejeté" : null;
                var id = Convert.ToInt32(r["BDCCR_ID_BDCRC"], System.Globalization.CultureInfo.InvariantCulture);
                lignes.Add(Ligne(r, $"Annonce {id}", Bodacc, erreur, null, null, null));
            }
        return new LignesLues(lignes, totaux.Values.Sum()) { TotalParGroupe = totaux };
    }

    private static LigneLue Ligne(SqlDataReader r, string objet, string groupe, string? erreur, string? journal, string? parution, string? facture)
    {
        var numPc = FormeLigne.Texte(r, "NumPc").Trim();
        var (numero, nom) = numPc.Length > 0 ? (numPc, FormeLigne.Texte(r, "NomPc")) : (FormeLigne.Texte(r, "NumPrv").Trim(), FormeLigne.Texte(r, "NomPrv"));
        var dateEvenement = FormeLigne.DateDe(r, "DateEvenement");
        var envoi = FormeLigne.DateDe(r, "DateEnvoi");
        var edi = FormeLigne.DateDe(r, "DateEdi");
        var paru = FormeLigne.DateDe(r, "DateParution");
        var numParution = FormeLigne.Texte(r, "NumParution").Trim();
        var precisions = FormeLigne.Joindre(
            dateEvenement is null ? FormeLigne.Texte(r, "Evenement") : $"{FormeLigne.Texte(r, "Evenement")} du {FormeLigne.Date(dateEvenement)}",
            journal,
            envoi is null ? "non envoyée" : $"envoyée le {FormeLigne.Date(envoi)}",
            edi is null ? null : $"envoi EDI le {FormeLigne.Date(edi)}",
            paru is null ? null : numParution.Length > 0 ? $"parue le {FormeLigne.Date(paru)} (n° {numParution})" : $"parue le {FormeLigne.Date(paru)}",
            parution, facture, erreur);
        // La règle d'état (en tête) : l'erreur, puis la parution, puis l'envoi.
        var etat = erreur is not null ? "Rejetee" : paru is not null ? "Validee" : envoi is not null ? "EnCours" : "AFaire";
        return new LigneLue(FormeLigne.Joindre(objet, numero, nom), precisions, groupe, etat, []);
    }
}
