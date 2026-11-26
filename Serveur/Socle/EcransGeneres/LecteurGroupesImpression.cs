using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Relancer un lot » (PROC_RELANCELOT) : les GROUPES D'IMPRESSION des lots de la chaine d'impression differee (Lot ->
/// GroupeImpression -> DemandeEdition), pour les relancer, les reimprimer ou les figer. Mode d'emploi : LecteurEcran.cs ; les
/// gestes : EcrivainGroupesImpression.cs. Plan « RIG v3 100 % fonctionnel », lot 9, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_relancelot.md (Detail OPE, qui fait foi) et le code -- Source\RIG\DLL\Processus\PROC_RELANCELOT\
/// et Source\Outils\DLL\RigMetier\Edition\GroupeImpression.cs :
///   - les groupes d'un lot : GroupeImpression.GetGroupeImpression(nomLot, debut, fin, etats), avec le nombre d'editions du
///     groupe (« SELECT count(*) FROM DEMANDE_EDITION WHERE DMNED_NUM_GROUPE = GPIMP_NUM_GROUPE »), tries
///     « order by gpimp_date_debut desc » ;
///   - les lots proposes : Lot.GetListeLot, SAUF les files Amitel pour un agent qui n'est pas d'Amitel (OPE_RECHERCHELOT.cs:38-39 ;
///     Lot.IsFileAmitel = le nom commence par « AMI », Lot.cs:40-44) : la V3 tient l'agent pour un agent du greffe ;
///   - un groupe sans edition deja emis (NombreEditions = 0 et phase E) n'est pas affiche (OPE_LISTVIEWGRP.cs:154-155) ;
///   - aucune case d'etat cochee = aucun filtre d'etat (GetGroupeImpression : « if (lstEtat.Count > 0) »).
///
/// GROUPE (les cases de la recherche V2, GroupeImpression.ETAT_AFFICHAGE et les phases qu'elles cherchent) : « Groupes émis »
/// (E), « Groupes intermédiaires » (I, P), « Groupes en traitement » (PT), « Groupes en erreur » (IZ, PZ, et toute autre phase,
/// que GroupeImpression.Phase nomme « Erreur »).
/// REGLE D'ETAT -- les boutons que RIG v2 active selon la phase (OPE_EDITGRP._enabledButtons, :109-128) :
///   - AFaire « À réimprimer » : E ou PZ, IMPRIMER et IMPRIMER_AUTRE actifs (lstImpressionOk, :117-125) ;
///   - EnCours « À figer » : I, FIGER actif (:116) ;
///   - EnAttente « Prêt à l'émission » : P ou PT, aucun bouton (le service d'impression prend le groupe) ;
///   - Rejetee « Erreur sans reprise » : toute autre phase (IZ...), aucun bouton (Reinitialiser n'est jamais actif, :120-126).
/// Id = GPIMP_NUM_GROUPE, la cle des gestes. Les precisions : la phase au libelle de RIG v2 (GroupeImpression.Phase), le nombre
/// d'editions, les dates de debut et de fin, l'imprimante.
///
/// CE QUE LA V3 NE REPREND PAS (nomme) : la forme File ne transmet aucun critere, donc ni le lot choisi, ni la plage de dates
/// (aujourd'hui 7 h -> maintenant par defaut, OPE_RECHERCHE.cs:132-139), ni les cases d'etat, ni le numero de groupe : la liste
/// est celle de RIG v2 dates effacees (plage ouverte, DateDebut/DateFin = MinValue/MaxValue, OPE_RECHERCHE.cs:22-56), tous lots
/// et tous etats, plafonnee. Le filtre « Uniquement mes éditions » d'un lot direct (OPE_RECHERCHE.cs:103-113) demande l'agent, que
/// le lecteur ne connait pas : les groupes du lot direct sont tous montres. L'avertissement au-dela de 300 groupes
/// (:120-125) n'a pas lieu d'etre : la liste est plafonnee et son total donne.
/// </summary>
internal sealed class LecteurGroupesImpression : ILecteurEcran
{
    public string Procedure => "PROC_RELANCELOT";

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Emis = "Groupes émis", Intermediaires = "Groupes intermédiaires", EnTraitement = "Groupes en traitement",
                          EnErreur = "Groupes en erreur";

    /// <summary>Les phases d'un groupe (DemandeEditionPourServices.cs, GroupeImpression.cs:175-180).</summary>
    internal const string PhaseInitial = "I", PhasePret = "P", PhaseEnCoursEmission = "PT", PhasePretErreur = "PZ", PhaseEmis = "E";

    private const string Requete = """
        SELECT TOP (@plafond) g.GPIMP_NUM_GROUPE, g.GPIMP_NOM_LOT, g.GPIMP_DATE_DEBUT, g.GPIMP_DATE_FIN, g.GPIMP_NOM_IMPRIMANTE,
               g.GPIMP_PHASE_TRAITEMENT, n.NombreEditions, COUNT(*) OVER () AS Total
        FROM v2.GROUPE_IMPRESSION AS g
        JOIN v2.LOT AS l ON l.LOT_NOM = g.GPIMP_NOM_LOT
        CROSS APPLY (SELECT COUNT(*) AS NombreEditions FROM v2.DEMANDE_EDITION AS e WHERE e.DMNED_NUM_GROUPE = g.GPIMP_NUM_GROUPE) AS n
        WHERE UPPER(l.LOT_NOM) NOT LIKE 'AMI%'
          AND NOT (n.NombreEditions = 0 AND g.GPIMP_PHASE_TRAITEMENT = 'E')
        ORDER BY g.GPIMP_DATE_DEBUT DESC, g.GPIMP_NUM_GROUPE
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    /// <summary>Le libelle de la phase dans RIG v2 (GroupeImpression.Phase) : toute phase inconnue est « Erreur ».</summary>
    internal static string LibellePhase(string phase) => phase switch
    {
        PhaseEmis => "Emis",
        PhasePret => "Prêt à l'émission",
        PhaseEnCoursEmission => "En cours d'émission",
        PhaseInitial => "Initial",
        _ => "Erreur",
    };

    /// <summary>La case de recherche V2 de la phase (GroupeImpression.ETAT_AFFICHAGE) : le groupe du descripteur.</summary>
    internal static string GroupeDe(string phase) => phase switch
    {
        PhaseEmis => Emis,
        PhaseInitial or PhasePret => Intermediaires,
        PhaseEnCoursEmission => EnTraitement,
        _ => EnErreur,
    };

    /// <summary>L'etat de la ligne : les boutons que RIG v2 active pour la phase (en tete du fichier).</summary>
    internal static string EtatDe(string phase) => phase switch
    {
        PhaseEmis or PhasePretErreur => "AFaire",
        PhaseInitial => "EnCours",
        PhasePret or PhaseEnCoursEmission => "EnAttente",
        _ => "Rejetee",
    };

    private static LigneLue Ligne(SqlDataReader r)
    {
        var phase = FormeLigne.Texte(r, "GPIMP_PHASE_TRAITEMENT");
        var numero = FormeLigne.Texte(r, "GPIMP_NUM_GROUPE");
        var editions = Convert.ToInt32(r["NombreEditions"], System.Globalization.CultureInfo.InvariantCulture);
        var debut = FormeLigne.DateDe(r, "GPIMP_DATE_DEBUT");
        var fin = FormeLigne.DateDe(r, "GPIMP_DATE_FIN");
        var precisions = FormeLigne.Joindre(LibellePhase(phase), editions > 1 ? $"{editions} éditions" : $"{editions} édition",
                                            debut is null ? null : $"ouvert le {FormeLigne.Date(debut)}",
                                            fin is null ? null : $"fini le {FormeLigne.Date(fin)}",
                                            FormeLigne.Texte(r, "GPIMP_NOM_IMPRIMANTE"));
        return new LigneLue($"Lot {FormeLigne.Texte(r, "GPIMP_NOM_LOT")} · groupe {numero}", precisions, GroupeDe(phase), EtatDe(phase), [])
        {
            Id = numero,
        };
    }
}
