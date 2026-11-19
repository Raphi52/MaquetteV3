namespace RigV3Desktop;

// ============================================================
// Bouton « Actions » du formulaire du milieu : la liste des actions que
// RIG v2 propose dans le processus XACTION (MDPRC_ID_MDPRC = 1499).
//
// Liste GENEREE, pas recopiee a la main : lecture du 15/09/2026 de
// RIG_DEV@SQL-DEV\DEV, jointure MODELE_PROCESSUS > ADAPTATEUR_ETAPE >
// MODELE_ETAPE, ordre ADPET_NUM_ORDRE. Retenu : les etapes de nature
// « A » (action au choix de l'utilisateur). Ecarte : l'entree (E_ENT_ACTR,
// nature E), l'historisation (9HISTODEP, F), la post-validation
// (9INF_ACT, 9EMISCFEX, RESTNUMACT, P) et LIBNUM (obligatoire, automatique).
// ============================================================

public sealed record ActionXaction(string Code, string Libelle);

public static class ActionsXaction
{
    public const string Processus = "XACTION";

    public static readonly ActionXaction[] Liste =
    [
        new("1GDMNX", "demande du XACTION"),
        new("XRACTIO", "corriger l'action RCS"),
        new("XREVENEM", "corriger un evenement RCS"),
        new("XRINRCS", "corriger une inscription RCS"),
        new("XRDEMAND", "corriger la demande"),
        new("XRPASCFE", "corriger la fonction CFE"),
        new("XCFEDEST", "destinataires des EDI CFE"),
        new("RM0", "Infos CFE"),
        new("RM0_INPI", "Infos CFE (INPI)"),
        new("XRJUS", "corriger une pièce justificative RC"),
        new("XRCASIE", "corriger le casier judiciaire"),
        new("XRBODAC", "corriger le BODACC"),
        new("XBODRECT", "Rectifier ou annuler un Bodacc"),
        new("XBODPREC", "Consulter l'annonce précédente"),
        new("XNOTIFXML", "corriger une notification RCS"),
        new("XRJRCS", "corriger une procédure juge RCS"),
        new("XRDILIG", "corriger une diligence RCS"),
        new("XRMOBS", "corriger mention"),
        new("XRDAS_1", "Corriger le dépôt d'acte"),
        new("XVERIF", "vérifications et alertes"),
    ];
}

/// <summary>
/// Une modification de valeur dans le formulaire, sur le modele de la table
/// AUDIT de RIG v2 (AUDIT_DATE, AUDIT_LOGIN, AUDIT_TABLE, AUDIT_XML /
/// AUDIT_XML_NEW) : qui, quand, quel element du dossier, avant, apres.
/// </summary>
public sealed record ModificationChamp(DateTime Quand, string Auteur, string Element,
                                      string Champ, string Colonne, string Avant, string Apres);

/// <summary>Un message ecrit dans le fil d'une affaire : qui, quand, quoi.</summary>
public sealed record MessageFil(DateTime Quand, string Auteur, string Texte);
