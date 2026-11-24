using System;
using System.Collections.Generic;
using System.Linq;

namespace RigV3Desktop;

/// <summary>
/// QUI VOIT QUOI. Un agent du greffe ne pratique qu'un metier a la fois :
/// le RCS, le judiciaire, ou l'endettement (injonctions de payer et
/// creances). Le menu de gauche porte pourtant le catalogue ENTIER de
/// RIG v2 : sans filtre, chacun traverse cent cinquante ecrans dont neuf
/// sur dix ne le concernent pas.
///
/// Ce tableau dit, pour les ecrans PROPRES a un metier, quels domaines y
/// ont acces. Un code ABSENT du tableau est TRANSVERSE : tout le monde le
/// voit (le courrier, la GED, la comptabilite, les reglages). La regle par
/// defaut est donc « visible » : ajouter un ecran au menu ne le fait jamais
/// disparaitre par accident.
/// </summary>
public static class PerimetreProfil
{
    private static readonly string[] Rcs = [DomaineGed.Rcs];
    private static readonly string[] Jud = [DomaineGed.Judiciaire];
    private static readonly string[] JudEnd = [DomaineGed.Judiciaire, DomaineGed.Endettement];
    private static readonly string[] RcsEnd = [DomaineGed.Rcs, DomaineGed.Endettement];
    private static readonly string[] RcsJud = [DomaineGed.Rcs, DomaineGed.Judiciaire];

    /// <summary>Les ecrans reserves, par code de procedure.</summary>
    private static readonly Dictionary<string, string[]> ParProc = new(StringComparer.Ordinal)
    {
        // ---- Registre du commerce ----
        ["PROC_NF"] = Rcs, ["PROC_DAS"] = Rcs, ["PROC_DASBEN"] = Rcs,
        ["PROC_CORDJRCS"] = Rcs, ["OUVERTURE_DOSSIER"] = Rcs, ["PROC_VDOSSIER"] = Rcs,
        ["PROC_VCHIFFRES"] = Rcs, ["PROC_TARIFS"] = Rcs, ["PROC_REJETS_BILANS"] = Rcs,
        ["PROC_BE"] = Rcs, ["PROC_BEN"] = Rcs, ["PROC_DBEN_ALERT"] = Rcs,
        ["PROC_MMA"] = Rcs, ["PROC_MDIL"] = Rcs, ["PROC_XEX"] = Rcs, ["PROC_CNI"] = Rcs,
        // Les RELANCES et INJONCTIONS sont du recouvrement : l'endettement les
        // suit avec le RCS (demande utilisateur du 2026-09-17).
        ["PROC_DCA_RELANCE"] = RcsEnd, ["PROC_DCA_INJ"] = RcsEnd,
        ["PROC_DBE_RELANCE"] = RcsEnd, ["PROC_DBE_INJ"] = RcsEnd,
        ["PROC_SRBE_RAPPEL"] = RcsEnd,
        // Actes retenus et actes rejetes : ouverts aussi a l'endettement
        // (demande utilisateur du 2026-09-17).
        ["PROC_ACTRET"] = RcsEnd, ["PROC_ACTREJ"] = RcsEnd, ["PROC_INPIPAY"] = Rcs, ["PROC_GINPI"] = Rcs,
        ["PROC_CFENET_RET"] = Rcs,
        // Commandes web (NeoRIG) : etats d'endettement ET certificats du
        // registre ; l'endettement les traite avec le RCS, comme les relances.
        ["PROC_COMMANDES_WEB"] = RcsEnd,

        // ---- Judiciaire ----
        ["PROC_XAFF"] = Jud, ["PROC_NPC"] = Jud, ["PROC_CJUD"] = Jud,
        ["PROC_MESIN"] = Jud, ["PROC_JOIN"] = Jud,
        ["PROC_DEPOTJUD"] = Jud, ["PROC_DEP"] = Jud, ["PROC_DEPPCM"] = Jud,
        ["PROC_VJUD"] = Jud, ["PROC_REGJUD"] = Jud, ["PROC_RECHERCHE_BATEAU"] = Jud,
        ["PROC_ACTEUR_JUD"] = Jud, ["PROC_EXPERT"] = Jud, ["PROC_MANDATAIRE"] = Jud,
        ["PROC_PARTIES"] = Jud, ["PROC_RAPPACTEUR"] = Jud, ["PROC_SUPP_ACTEUR_JUD"] = Jud,
        ["PROC_MJUD"] = Jud, ["PROC_MPC"] = Jud, ["PROC_MDJUD"] = Jud,
        ["PROC_MODIF_REPER"] = Jud, ["PROC_LIBAFFAIRE"] = Jud, ["PROC_XLIEN"] = Jud,
        ["PROC_XXAFF"] = Jud, ["PROC_CHM"] = Jud, ["PROC_CHORGANESENMASSE"] = Jud,
        ["PROC_DEBITEURS_MULTIPLES"] = Jud,
        ["PROC_GESTAUDS"] = Jud, ["PROC_CABINET"] = Jud, ["PROC_COMPO"] = Jud,
        ["PROC_PREAUD"] = Jud, ["PROC_GCONV"] = Jud, ["PROC_MPCS"] = Jud,
        ["PROC_AUDIENCE"] = Jud, ["PROC_RETAUD"] = Jud, ["PROC_LISTE_DELIBERES"] = Jud,
        ["PROC_TAXESETTOURS"] = Jud, ["PROC_REDA"] = Jud, ["PROC_REDA_MASSE"] = Jud,
        ["PROC_REDAA"] = Jud, ["PROC_DEST_REDA"] = Jud, ["PROC_SIGNJUD"] = Jud,
        ["PROC_ORDOSIGN"] = Jud, ["PROC_DJ_OCCULTATIONS"] = Jud, ["PROC_IMPLEG"] = Jud,
        ["PROC_GEDJUD"] = Jud, ["PROC_SDEMATJUD"] = Jud, ["PROC_SURVMES"] = Jud,
        ["PROC_STATCA"] = Jud, ["PROC_EDI_RPVAINS"] = Jud, ["PROC_EDI_RPJC_TRCNC"] = Jud,

        // ---- Endettement : injonctions de payer, creances, recouvrement.
        // Partagees avec le judiciaire, qui instruit les memes affaires.
        ["PROC_IPE"] = JudEnd, ["PROC_SURVIP"] = JudEnd, ["PROC_IPSUIVI"] = JudEnd,
        ["PROC_CREANCE"] = JudEnd, ["PROC_ADDCREANCE"] = JudEnd,
        ["PROC_MODCREANCE"] = JudEnd, ["PROC_INTEGCREANCE"] = JudEnd,
        ["PROC_CREANREP"] = JudEnd,

        // ---- REGLAGES AVANCES : fermes a l'endettement (demande utilisateur
        // du 2026-09-17). Le verbe « Mon poste » (accueil, dates, aide) reste
        // ouvert a tous : ce sont les referentiels et les outils qui sortent.
        ["PROC_GTABREF"] = RcsJud, ["PROC_GED_NATURE_DOC"] = RcsJud,
        ["PROC_GESTEMAIL"] = RcsJud, ["PROC_GESTBANQ"] = RcsJud,
        ["PROC_BORNE"] = RcsJud, ["PROC_PARAM_IP"] = Jud,
        ["PROC_PARAM_LISTEEXCEL"] = RcsJud, ["PROC_PARAMEXPRESSO"] = RcsJud,
        ["PROC_REQUETES"] = RcsJud, ["PROC_IA"] = Rcs,

        // ---- Les fonctions de RIG v2 retrouvees le 24/11/2026 (conv-2142,
        // Pont/EcransGeneres/FonctionsRetrouvees.cs) : le metier de l'ecran qui
        // les reunit, ou celui de leurs voisines.
        ["PROC_DCA"] = Rcs,
        ["PROC_ROFSURVEILLANCES"] = RcsEnd, ["PROC_ACTREJ_CMD_WEB"] = RcsEnd, ["PROC_REJETSKBISXML"] = RcsEnd,
        ["PROC_GDEST_INST"] = Jud, ["PROC_EDI_RPJC_INS"] = Jud, ["PROC_EDI_RPJC_REN"] = Jud,
        ["PROC_IMP_WIN"] = RcsJud,

        // ---- Les ECRANS REUNIS (Pont/MenuAccueil.cs, EntreeMenu.Reunit) : le
        // metier de leurs procedures, qui est le meme pour toutes (tests/
        // EcransGeneresCheck le verifie). Un ecran reuni transverse n'est pas ici.
        ["SYNTHESE_DEPOTS_REGISTRE"] = Rcs, ["SYNTHESE_CHIFFRES_RCS"] = Rcs,
        ["SYNTHESE_CORRIGER_RCS"] = Rcs, ["SYNTHESE_INPI"] = Rcs, ["SYNTHESE_FORMALITES_DEMAT"] = Rcs,
        ["SYNTHESE_RELANCES"] = RcsEnd, ["SYNTHESE_ACTES_BLOQUES"] = RcsEnd,
        ["SYNTHESE_NOUVELLE_AFFAIRE_JUD"] = Jud, ["SYNTHESE_DEPOTS_PC"] = Jud,
        ["SYNTHESE_DOSSIERS_JUD"] = Jud, ["SYNTHESE_CORRIGER_JUD"] = Jud,
        ["SYNTHESE_DEFAIRE"] = Jud, ["SYNTHESE_PC_EN_MASSE"] = Jud,
        ["SYNTHESE_DECISIONS"] = Jud, ["SYNTHESE_PREPARER_AUDIENCE"] = Jud,
        ["SYNTHESE_APRES_AUDIENCE"] = Jud, ["SYNTHESE_FICHES_ACTEURS"] = Jud,
        ["SYNTHESE_EDI_AVOCATS"] = Jud,
        ["SYNTHESE_IP"] = JudEnd,
        ["SYNTHESE_TABLES"] = RcsJud
    };

    /// <summary>Cet ecran est-il dans le perimetre de ces domaines ? Un ecran
    /// non reserve l'est toujours.</summary>
    public static bool Visible(string code, string[] domaines)
        => !ParProc.TryGetValue(code, out var requis)
           || requis.Any(domaines.Contains);

    /// <summary>Les domaines qui voient cet ecran, vide s'il est transverse.
    /// Sert aux bancs de controle.</summary>
    public static string[] Reserve(string code)
        => ParProc.TryGetValue(code, out var r) ? r : [];
}
