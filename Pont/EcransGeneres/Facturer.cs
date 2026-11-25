using System.Globalization;

namespace RigV3Desktop.Pont.EcransGeneres;

// LE VERBE FACTURER du menu (Facturer, Encaisser, Tenir les comptes, Creances), plus
// « Édition interne d'un Kbis » (verbe Terminer, teinte 3), rangee ici a la demande du lot 6 du plan.
// Chaque descripteur reprend sa fiche RIG v2 (Documentation\reference\proc\proc_<code>.md) :
// les etapes deviennent les marches, les regles les consignes, les controles les messages.
// La section « Detail OPE » fait foi : quand elle contredit le corps de la fiche, c'est elle
// qui est reprise. Le titre reste le libelle du menu (le banc l'exige). Quand le menu, le code
// et la fiche ne disent pas la meme chose, l'ecart est ecrit au commentaire du descripteur :
// il reste a trancher par le chef. Les lignes sont une demonstration ANONYME (noms fictifs).
public sealed class DomaineFacturer : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        new DescripteurEcran
        {
            Code = "PROC_FACTREEDIT", Titre = "Refacturation", Sujet = "factures émises",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une facture, un numéro, un montant…",
            Criteres = ["Client", "Période de facturation (du, au)"],
            // Bouton cité par la fiche (proc_factreedit.md) : « VALIDER/QUITTER par défaut ».
            Valider = "Valider",
            Rang = 6,
            Etats = EtatsEcran.Trois("À rééditer", "Choisie", "Rééditée"),
            Groupes = ["Factures émises"],
            Sections =
            [
                Marche("Critères", "Un client et une période. Si l'ouverture fournit les trois paramètres, la liste s'affiche directement ; sinon Suivant passe à la liste. Sans client, la liste ne s'ouvre pas.", true,
                    "Client", "Période du", "Période au"),
                Marche("Choix des factures", "Toutes les factures de la période sont cochées par défaut : décocher celles à ne pas rééditer. Une seule édition par facture est relancée, la plus récente.", true,
                    "Case à cocher", "Numéro de facture", "Date de facturation", "Montant TTC"),
            ],
            Lignes =
            [
                Ligne("F2026-00412", $"Émise le 03/03/2026 · {Euros(125_000)} TTC", "Factures émises", "Validee", 0, 1),
                Ligne("F2026-00413", $"Émise le 04/03/2026 · {Euros(31_850)} TTC", "Factures émises", "EnCours", 0),
                Ligne("F2026-00415", $"Émise le 09/03/2026 · {Euros(204_000)} TTC", "Factures émises", "EnCours", 0),
                Ligne("F2026-00418", $"Émise le 12/03/2026 · {Euros(9_600)} TTC", "Factures émises", "AFaire"),
                Ligne("F2026-00421", $"Émise le 16/03/2026 · {Euros(61_240)} TTC", "Factures émises", "AFaire"),
                Ligne("F2026-00424", $"Émise le 20/03/2026 · {Euros(148_000)} TTC", "Factures émises", "Validee", 0, 1),
                Ligne("F2026-00430", $"Émise le 27/03/2026 · {Euros(7_500)} TTC", "Factures émises", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RELCLI", Titre = "Releve de compte", Sujet = "comptes clients",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un client, un compte…",
            Criteres = ["Statut du compte (actifs par défaut)", "Type de compte (standard et greffes par défaut)", "Solde minimum (seuil du greffe)"],
            // Action citée par la fiche (proc_relcli.md) : « valider les soldes mensuels ».
            Valider = "Valider les soldes",
            Rang = 6,
            Etats = EtatsEcran.Trois("À éditer", "Relevé édité", "Soldes validés"),
            Groupes = ["Standard", "Greffes"],
            Sections =
            [
                Marche("Critères", "Statut, type de compte et solde minimum, celui du greffe par défaut. Rien n'est calculé avant Rechercher.", true,
                    "Statut", "Type de compte", "Solde minimum"),
                Marche("Sélection", "Les clients à éditer se cochent. Une nouvelle recherche remet la sélection à zéro.", true,
                    "Case à cocher", "Client", "Compte"),
                Marche("Relevés", "Aperçu d'une ligne, ou édition en masse : envoi par e-mail si une adresse valide existe, sinon impression. Un aperçu est refusé si la fin de période précède son début.", true,
                    "Aperçu", "Édition", "Envoi par e-mail"),
                Marche("Validation des soldes", "Demande le droit de comptabilité. Un solde se fige seulement si la période est achevée, et jamais au-delà de la première pièce non comptabilisée, nommée dans l'avertissement.", true,
                    "Droit comptable", "Pièce bloquante", "Soldes mensuels"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", $"Compte actif · solde débiteur {Euros(184_000)}", "Standard", "Validee", 0, 1, 2, 3),
                Ligne("Bêta Industrie SAS", $"Compte actif · solde débiteur {Euros(73_215)}", "Standard", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL", "Compte actif · pièce non comptabilisée le 14/03/2026", "Standard", "EnCours", 0, 1, 2),
                Ligne("Compte greffe 01", $"Compte actif · solde {Euros(51_200)}", "Greffes", "AFaire"),
                Ligne("Epsilon Habitat SARL", "Sous le seuil du greffe", "Standard", "AFaire"),
                Ligne("Zêta Commerce SAS", $"Compte actif · solde débiteur {Euros(90_000)}", "Standard", "AFaire"),
                Ligne("Compte greffe 02", $"Compte actif · solde {Euros(221_580)}", "Greffes", "Validee", 0, 1, 2, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_ARTICLE", Titre = "Articles tarifaires", Sujet = "articles tarifaires",
            Forme = FormeEcran.Referentiel,
            Colonnes = ["Libellé", "Code de l'article", "Type d'article", "Période de validité", Champ.Montant("Prix HT")],
            Recherche = "Rechercher un article, un code, une période…",
            Criteres = ["Période de validité", "Type : article principal ou titre composé", "Catégorie de ventilation"],
            // Action citée par la fiche (proc_article.md) : « la transaction de sauvegarde ».
            Valider = "Sauvegarder",
            Rang = 6,
            Etats = EtatsEcran.Trois("À compléter", "En cours de saisie", "Enregistré"),
            Groupes = ["Articles principaux", "Titres composés"],
            Sections =
            [
                Marche("Identification", "Le type, principal ou titre, verrouille le reste du formulaire. Le code de l'article est unique : un code orphelin est repris, il n'est pas recréé.", true,
                    "Type d'article", "Code de l'article", "Libellé"),
                Marche("Versions et périodes", "Un article est une suite de versions datées, chacune avec son prix HT. Une version peut être clonée ou supprimée.", true,
                    "Période de validité", "Prix HT", "Versions"),
                Marche("Charges liées", "Charges de la version (catégories BO, IN et D), partagées entre les versions, frais d'huissier compris.", false,
                    "Charges", "Frais d'huissier"),
                Marche("Composition du titre", "Un titre est composé d'autres articles : son prix est la somme de ses composants.", false,
                    "Composants", "Quantité"),
            ],
            Lignes =
            [
                Ligne("Extrait Kbis, tarif 2026", $"Article principal · prix HT {Euros(2_500)}", "Articles principaux", "Validee", 0, 1, 2),
                Ligne("Copie d'acte, tarif 2026", $"Article principal · prix HT {Euros(1_250)}", "Articles principaux", "EnCours", 0),
                Ligne("Pack création de société", $"Titre composé de 3 articles · {Euros(14_800)} HT", "Titres composés", "Validee", 0, 1, 3),
                Ligne("Dépôt des comptes", $"Article principal · prix HT {Euros(4_000)}", "Articles principaux", "AFaire"),
                Ligne("Radiation d'office", $"Article principal · prix HT {Euros(6_000)}", "Articles principaux", "EnCours", 0, 1),
                Ligne("Pack reprise de fonds", $"Titre composé de 2 articles · {Euros(21_000)} HT", "Titres composés", "AFaire"),
                Ligne("Frais de signification", $"Article principal · prix HT {Euros(8_500)}", "Articles principaux", "AFaire"),
            ],
        },

        // Le corps de la fiche contient des colonnes et du SQL que la fiche elle-même dit inventés :
        // seuls les états réels (AValider, Valide, Rejete) et le Détail OPE sont repris.
        new DescripteurEcran
        {
            Code = "PROC_INPIPAY", Titre = "Reversements INPI", Sujet = "reversements INPI",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un reversement, un identifiant de paiement…",
            Criteres = ["Période de réception", "Identifiant de paiement", "État : à valider, validé ou rejeté"],
            // Bouton cité par la fiche (proc_inpipay.md) : « bouton valider » (OPE_VALIDATION).
            Valider = "Valider",
            Rang = 6,
            Etats = [.. EtatsEcran.Trois("À valider", "En cours de validation", "Validé"), new EtatEcran("Rejetee", "Rejeté", "rejetee")],
            Groupes = ["Virements INPI"],
            Sections =
            [
                Marche("Recherche", "Période de réception, identifiant de paiement et état. Seuls les reversements du greffe courant sont affichés.", true,
                    "Période", "Identifiant de paiement", "État"),
                Marche("Fichier de l'INPI", "Le fichier XML de l'INPI donne le total et le détail par numéro national. Un numéro sans demande liée bloque la validation.", true,
                    "Montant total", "Numéro national", "Demande liée"),
                Marche("Encaissement", "Création de l'encaissement et de ses ventilations, puis passage à « Validé ». Le mode de paiement retenu devient le défaut du greffe.", true,
                    "Mode de paiement", "Date d'encaissement", "Référence"),
            ],
            Lignes =
            [
                Ligne("Virement INPI 2026-0311", $"Reçu le 11/03/2026 · {Euros(196_000)}", "Virements INPI", "Validee", 0, 1, 2),
                Ligne("Virement INPI 2026-0318", $"Reçu le 18/03/2026 · {Euros(41_550)}", "Virements INPI", "EnCours", 0, 1),
                Ligne("Virement INPI 2026-0319", $"Reçu le 19/03/2026 · {Euros(8_800)}", "Virements INPI", "EnCours", 0),
                Ligne("Virement INPI 2026-0324", $"Reçu le 24/03/2026 · {Euros(273_000)}", "Virements INPI", "AFaire"),
                Ligne("Virement INPI 2026-0326", $"Reçu le 26/03/2026 · {Euros(15_000)}", "Virements INPI", "AFaire"),
                Ligne("Virement INPI 2026-0327", $"Reçu le 27/03/2026 · {Euros(64_000)}", "Virements INPI", "Rejetee"),
                Ligne("Virement INPI 2026-0330", $"Reçu le 30/03/2026 · {Euros(110_500)}", "Virements INPI", "AFaire"),
            ],
        },

        // fix-ok: commentaires recoupés aux titres réels des fiches (grep des sections ## et de la
        // section « Écarts ») : GINPI régularise au clic droit, ADDCREANCE est un lanceur EXE,
        // MODCREANCE n'a pas de Détail OPE.
        // fix-ok: libellés du bouton final ramenés à ceux que cite chaque fiche (décision chef,
        // msg-84c0fb24), mesurés par grep de proc_<code>.md ; ADDCREANCE rétabli : son formulaire source existe (décision du chef, 14:01).
        // Corrections de la fiche (section « Écarts », vérifiée au code) : pas de bouton Valider
        // dans l'écran, la recherche part au bouton Rechercher. La régularisation se fait au clic
        // droit (menu contextuel « Régulariser l'anomalie ») : la maquette porte ce libellé sur son
        // bouton Valider, faute de bouton réel.
        new DescripteurEcran
        {
            Code = "PROC_GINPI", Titre = "Formalites INPI", Sujet = "formalités INPI",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une affaire, un montant, un paiement…",
            Criteres = ["Période (date de début et de fin)", "Critères de paiement (cumulables)", "Non soldées uniquement"],
            // Action citée par la fiche (proc_ginpi.md) : menu contextuel « Régulariser l'anomalie ».
            Valider = "Régulariser l'anomalie",
            Rang = 6,
            Etats = EtatsEcran.Trois("À traiter", "Anomalie à régulariser", "Soldée"),
            Groupes = ["Formalités payantes", "Formalités gratuites"],
            Sections =
            [
                Marche("Recherche", "Période et critères de paiement, cumulables. La recherche part au bouton Rechercher. L'export Excel n'est proposé qu'après un résultat.", true,
                    "Période", "Critères de paiement", "Non soldées uniquement"),
                Marche("Rapprochement", "Montant attendu de l'INPI face aux paiements reçus : non traitée, en attente, paiement INPI ou autre reçu, total reçu, trop-perçu, à relancer.", true,
                    "Montant attendu", "Montant reçu INPI", "Reçu autre", "État"),
                Marche("Régularisation", "Clic droit sur une anomalie (en attente de paiement ou paiement INPI reçu), après confirmation : le montant attendu prend le montant reçu, dans l'affaire.", false,
                    "Montant attendu", "Confirmation"),
            ],
            Lignes =
            [
                Ligne("Affaire RCS 2026B00101", $"Attendu {Euros(12_000)} · reçu {Euros(12_000)}", "Formalités payantes", "Validee", 0, 1),
                Ligne("Affaire RCS 2026B00102", $"Attendu {Euros(19_000)} · reçu {Euros(9_500)}", "Formalités payantes", "EnCours", 0),
                Ligne("Affaire RCS 2026B00103", "Formalité gratuite", "Formalités gratuites", "Validee", 0, 1),
                Ligne("Affaire RCS 2026B00104", $"Attendu {Euros(19_000)} · rien reçu", "Formalités payantes", "AFaire"),
                Ligne("Affaire RCS 2026B00105", $"Attendu {Euros(7_500)} · trop-perçu {Euros(1_000)}", "Formalités payantes", "EnCours", 0, 1),
                Ligne("Affaire RCS 2026B00106", $"Attendu {Euros(38_000)} · à relancer", "Formalités payantes", "EnCours", 0),
                Ligne("Affaire RCS 2026B00107", $"Attendu {Euros(6_000)} · en attente de paiement", "Formalités payantes", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REJET", Titre = "Rejets d'encaissement", Sujet = "encaissements rejetés",
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher un encaissement, un numéro de titre…",
            Criteres = ["Encaissement d'origine", "Motif du rejet", "Destinataire de la lettre"],
            // Action citée par la fiche (proc_rejet.md) : « DoValider » (enregistrement puis clôture).
            Valider = "Valider",
            Rang = 6,
            Etats = EtatsEcran.Trois("À rédiger", "Motif saisi", "Lettre émise"),
            Groupes = ["Chèques rejetés", "Titres impayés"],
            Sections =
            [
                Marche("Encaissement rejeté", "Lecture seule : numéro de titre, date, montant, payeur et banque, repris de l'encaissement.", true,
                    "Numéro de titre", "Date", "Montant", "Payeur", "Banque"),
                Marche("Destinataire", "Contact, adresse et e-mail viennent du client de la première ventilation. Un champ vide ne bloque pas la lettre : il est signalé.", false,
                    "Contact", "Adresse", "E-mail"),
                Marche("Motif", "Le motif est obligatoire : sans motif, le rejet ne s'enregistre pas.", true,
                    "Motif du rejet"),
                Marche("Lettre de rejet", "La lettre est générée puis archivée en GED ; la demande se clôture à la validation. Si l'enregistrement échoue, le rejet est annulé.", true,
                    "Lettre", "Archivage GED"),
            ],
            Lignes =
            [
                Ligne("Chèque n° 0045871", $"Rejeté le 05/03/2026 · {Euros(85_000)} · Alpha Services SARL", "Chèques rejetés", "Validee", 0, 1, 2, 3),
                Ligne("Chèque n° 0045902", $"Rejeté le 09/03/2026 · {Euros(124_000)} · Gamma Conseil SARL", "Chèques rejetés", "EnCours", 0, 1),
                Ligne("Titre n° 2026-118", $"Impayé le 12/03/2026 · {Euros(41_500)} · Epsilon Habitat SARL", "Titres impayés", "EnCours", 0, 2),
                Ligne("Titre n° 2026-124", $"Impayé le 16/03/2026 · {Euros(9_900)} · Zêta Commerce SAS", "Titres impayés", "AFaire"),
                Ligne("Chèque n° 0045944", $"Rejeté le 20/03/2026 · {Euros(211_000)} · Êta Boulangerie SARL", "Chèques rejetés", "AFaire"),
                Ligne("Chèque n° 0045960", $"Rejeté le 23/03/2026 · {Euros(56_000)} · Thêta Import SAS", "Chèques rejetés", "Validee", 0, 1, 2, 3),
                Ligne("Titre n° 2026-131", $"Impayé le 27/03/2026 · {Euros(73_000)} · Iota Études SARL", "Titres impayés", "AFaire"),
            ],
        },

        // Garde « mode de paiement sans banque » : le Détail OPE montre qu'elle n'est jamais
        // exécutée dans le code. Elle n'est donc pas reprise comme règle de l'écran.
        new DescripteurEcran
        {
            Code = "PROC_REMBANQ", Titre = "Remise en banque", Sujet = "encaissements à remettre",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un encaissement, un mode de paiement, un numéro de remis…",
            Criteres = ["Période (du, au) : un seul mois", "Utilisateurs (tous par défaut)", "Mode de paiement"],
            // Action citée par la fiche (proc_rembanq.md) : « Générer un bordereau de remise en banque ».
            Valider = "Générer un bordereau",
            Rang = 6,
            Etats = EtatsEcran.Trois("À remettre", "Sélectionné", "Remis en banque"),
            Groupes = ["Chèque", "Carte bancaire", "Espèces"],
            Sections =
            [
                Marche("Période et utilisateurs", "Une période dans un seul mois : sinon le bordereau est refusé. Les utilisateurs se cochent ; changer la période remet la sélection à tous.", true,
                    "Date de début", "Date de fin", "Utilisateurs"),
                Marche("Encaissements à remettre", "Arbre par mode de paiement. Un encaissement est à remettre tant qu'il n'a pas de numéro de remis. Cocher un mode coche tous ses encaissements.", true,
                    "Mode de paiement", "Encaissement"),
                Marche("Bordereau", "Brouillon : aperçu, rien n'est enregistré. Définitif : confirmation, puis impression et rattachement des encaissements. Aucun encaissement coché : refusé.", true,
                    "Brouillon", "Définitif"),
                Marche("Rééditer ou annuler", "Rééditer reprend un remis à partir de son numéro. Annuler détache les seuls encaissements non comptabilisés, après confirmation.", false,
                    "Numéro de remis", "Annuler"),
            ],
            Lignes =
            [
                Ligne("Encaissement 2026-0311-07", $"Chèque · {Euros(48_000)} · remis n° 2026-014", "Chèque", "Validee", 0, 1, 2),
                Ligne("Encaissement 2026-0311-09", $"Chèque · {Euros(112_000)} · remis n° 2026-014", "Chèque", "Validee", 0, 1, 2),
                Ligne("Encaissement 2026-0312-02", $"Carte bancaire · {Euros(21_500)}", "Carte bancaire", "EnCours", 0, 1),
                Ligne("Encaissement 2026-0313-05", $"Carte bancaire · {Euros(8_900)}", "Carte bancaire", "AFaire"),
                Ligne("Encaissement 2026-0314-01", $"Espèces · {Euros(30_000)}", "Espèces", "AFaire"),
                Ligne("Encaissement 2026-0316-04", $"Espèces · {Euros(4_650)}", "Espèces", "EnCours", 0),
                Ligne("Encaissement 2026-0318-08", $"Chèque · {Euros(230_000)}", "Chèque", "AFaire"),
            ],
        },

        // Le Détail OPE fait foi : un état Crystal de contrôle des remboursements (trop-perçus et
        // notifications), pas une liste de chèques. Une seule étape de critères, un bouton VOIR.
        new DescripteurEcran
        {
            Code = "PROC_EDCHQEMIS", Titre = "Cheques emis", Sujet = "remboursements émis",
            Forme = FormeEcran.Etat,
            // Les deux cases et la periode : proc_edchqemis.md:81-89 (criteres de requete, jamais sauvegardes).
            Parametres = [Champ.Periode("Période"), Champ.Case("Trop-perçus"), Champ.Case("Notifications")],
            Colonnes = ["Remboursement", Champ.Montant("Montant")],
            Gestes = ["Voir"],
            Recherche = "Rechercher un remboursement, une période…",
            Criteres = ["Période (du, au) : aujourd'hui par défaut", "Trop-perçus", "Notifications"],
            // Bouton cité par la fiche (proc_edchqemis.md) : « un seul bouton, VOIR ».
            Valider = "Voir",
            Rang = 6,
            Etats = EtatsEcran.Trois("À contrôler", "En contrôle", "Contrôlé"),
            Groupes = ["Trop-perçus", "Notifications"],
            Sections =
            [
                Marche("Période et origine", "Dates du et au, aujourd'hui par défaut. Trop-perçus et notifications se cochent ensemble ou seuls ; sans case cochée, rien à voir.", true,
                    "Date de début", "Date de fin", "Trop-perçus", "Notifications"),
                Marche("Voir l'état", "VOIR n'est actif que s'il existe au moins un remboursement sur la période. L'état s'ouvre ensuite, après une barre de progression.", true,
                    "Nombre d'éléments", "État"),
            ],
            Lignes =
            [
                Ligne("Remboursement 2026-0041", $"Trop-perçu · {Euros(31_000)}", "Trop-perçus", "Validee", 0, 1) with { Valeurs = ["Remboursement 2026-0041", "310.00"] },
                Ligne("Remboursement 2026-0044", $"Trop-perçu · {Euros(7_720)}", "Trop-perçus", "EnCours", 0) with { Valeurs = ["Remboursement 2026-0044", "77.20"] },
                Ligne("Remboursement 2026-0047", $"Notification · {Euros(150_000)} · Gamma Conseil SARL", "Notifications", "AFaire") with { Valeurs = ["Remboursement 2026-0047", "1500.00"] },
                Ligne("Remboursement 2026-0052", $"Notification · {Euros(41_500)} · Delta Transports SAS", "Notifications", "EnCours", 0) with { Valeurs = ["Remboursement 2026-0052", "415.00"] },
                Ligne("Remboursement 2026-0055", $"Trop-perçu · {Euros(92_000)}", "Trop-perçus", "AFaire") with { Valeurs = ["Remboursement 2026-0055", "920.00"] },
                Ligne("Remboursement 2026-0061", $"Notification · {Euros(6_600)}", "Notifications", "Validee", 0, 1) with { Valeurs = ["Remboursement 2026-0061", "66.00"] },
            ],
        },

        // Le code n'a pas de grille : c'est un rapport Crystal édité sur critères. Les lignes
        // ci-dessous sont une démonstration (encaissements par mode de paiement).
        new DescripteurEcran
        {
            Code = "PROC_JCAISSE", Titre = "Journal de caisse", Sujet = "encaissements",
            Forme = FormeEcran.Etat,
            // Parametres : OPE_CRITERESGENERAUX et le tri a 4 options d'OPE_METHODETRI (proc_jcaisse.md, Detail OPE).
            // Colonnes : les champs reportes dans le journal (proc_jcaisse.md:322) ; un seul geste, EDITER (:208).
            Parametres = [Champ.Periode("Période"), "Caissier", "Modes de paiement", Champ.Liste("Tri", "Par mode de paiement", "Par utilisateur", "Par montant", "Par date")],
            Colonnes = ["Encaissement", Champ.Date("Date"), "Payeur", "Mode de paiement", "Numéro de titre", "Caissier", Champ.Montant("Montant")],
            Gestes = ["Éditer"],
            Recherche = "Rechercher un encaissement, une référence, un client…",
            Criteres = ["Période (du, au), 31 jours au plus conseillés", "Caissier (tous par défaut)", "Modes de paiement"],
            // Bouton cité par la fiche (proc_jcaisse.md, Détail OPE) : « sur clic EDITER ».
            Valider = "Éditer",
            Rang = 6,
            Etats = EtatsEcran.Trois("À éditer", "Critères choisis", "Journal édité"),
            Groupes = ["Espèces", "Chèque", "Carte bancaire", "Virement"],
            Sections =
            [
                Marche("Critères généraux", "La date de fin ne précède pas la date de début. Au-delà de 31 jours, un avertissement signale un affichage possiblement lent. Seuls les encaissements du greffe courant sont retenus.", true,
                    "Date de début", "Date de fin", "Caissier", "Modes de paiement"),
                Marche("Mode de tri et édition", "Choix du tri, puis Éditer lance le journal en tâche de fond, avec une barre de progression. Le rapport est le résultat : pas de grille à l'écran.", true,
                    "Tri", "Journal"),
            ],
            Lignes =
            [
                Ligne("Encaissement 0312-01", $"Espèces · {Euros(18_000)} · caissier de démonstration A", "Espèces", "Validee", 0, 1) with { Valeurs = ["Encaissement 0312-01", "2026-03-12", "", "Espèces", "", "caissier de démonstration A", "180.00"] },
                Ligne("Encaissement 0312-02", $"Espèces · {Euros(4_250)} · caissier de démonstration B", "Espèces", "AFaire") with { Valeurs = ["Encaissement 0312-02", "2026-03-12", "", "Espèces", "", "caissier de démonstration B", "42.50"] },
                Ligne("Encaissement 0312-03", $"Chèque n° 0046120 · {Euros(95_000)}", "Chèque", "EnCours", 0) with { Valeurs = ["Encaissement 0312-03", "2026-03-12", "", "Chèque", "0046120", "", "950.00"] },
                Ligne("Encaissement 0312-04", $"Carte bancaire · {Euros(32_000)}", "Carte bancaire", "Validee", 0, 1) with { Valeurs = ["Encaissement 0312-04", "2026-03-12", "", "Carte bancaire", "", "", "320.00"] },
                Ligne("Encaissement 0313-01", $"Virement · {Euros(241_000)}", "Virement", "AFaire") with { Valeurs = ["Encaissement 0313-01", "2026-03-13", "", "Virement", "", "", "2410.00"] },
                Ligne("Encaissement 0313-02", $"Chèque n° 0046124 · {Euros(12_800)}", "Chèque", "EnCours", 0) with { Valeurs = ["Encaissement 0313-02", "2026-03-13", "", "Chèque", "0046124", "", "128.00"] },
                Ligne("Encaissement 0313-03", $"Carte bancaire · {Euros(6_400)}", "Carte bancaire", "AFaire") with { Valeurs = ["Encaissement 0313-03", "2026-03-13", "", "Carte bancaire", "", "", "64.00"] },
            ],
        },

        // Meme cas que le journal de caisse : pas de grille dans le code, un rapport Crystal édité
        // sur critères. Les lignes sont une démonstration (une vente par ligne).
        new DescripteurEcran
        {
            Code = "PROC_JVENTE", Titre = "Journal de vente", Sujet = "ventes du greffe",
            Forme = FormeEcran.Etat,
            Parametres = [Champ.Periode("Période"), "Utilisateurs", "Clients", "Articles", "TVA", "Factures", "Type d'édition", "Tri"],
            // Colonnes : la vente et son montant (role, proc_jvente.md:6 : « les ventes réalisées »).
            Colonnes = ["Vente", Champ.Montant("Montant")],
            Gestes = ["Éditer"],
            Recherche = "Rechercher une vente, un adjudicataire, une référence…",
            Criteres = ["Période et utilisateurs", "Clients, articles, TVA et factures", "Type d'édition et tri"],
            // Bouton cité par la fiche (proc_jvente.md, Détail OPE) : « uniquement sur EDITER ».
            Valider = "Éditer",
            Rang = 6,
            Etats = EtatsEcran.Trois("À éditer", "Critères choisis", "Journal édité"),
            Groupes = ["Criée judiciaire", "Vente aux enchères"],
            Sections =
            [
                Marche("Généraux", "Dates et utilisateurs. Seules les ventes du greffe courant sont retenues, et seules les affaires de nature « vente » paramétrée.", true,
                    "Date de début", "Date de fin", "Utilisateurs"),
                Marche("Filtres", "Clients, articles, TVA et factures pour restreindre le rapport.", true,
                    "Clients", "Articles", "TVA", "Factures"),
                Marche("Édition", "Type d'édition. Le rapport se lance en tâche de fond : un rapport encore ouvert bloque la fermeture de l'écran.", true,
                    "Type d'édition"),
                Marche("Tri", "Choix du tri ; le rapport est généré de façon asynchrone.", true,
                    "Tri"),
            ],
            Lignes =
            [
                Ligne("Criée du 03/03/2026, lot 12", $"Adjudicataire de démonstration A · {Euros(1_450_000)}", "Criée judiciaire", "Validee", 0, 1, 2, 3) with { Valeurs = ["Criée du 03/03/2026, lot 12", "14500.00"] },
                Ligne("Criée du 10/03/2026, lot 3", $"Adjudicataire de démonstration B · {Euros(320_000)}", "Criée judiciaire", "EnCours", 0) with { Valeurs = ["Criée du 10/03/2026, lot 3", "3200.00"] },
                Ligne("Vente aux enchères 2026-07", $"Adjudicataire de démonstration C · {Euros(890_000)}", "Vente aux enchères", "AFaire") with { Valeurs = ["Vente aux enchères 2026-07", "8900.00"] },
                Ligne("Vente aux enchères 2026-09", $"Mise à prix {Euros(100_000)}", "Vente aux enchères", "EnCours", 0, 1) with { Valeurs = ["Vente aux enchères 2026-09", "1000.00"] },
                Ligne("Criée du 17/03/2026, lot 5", $"Adjudication {Euros(2_200_000)}", "Criée judiciaire", "AFaire") with { Valeurs = ["Criée du 17/03/2026, lot 5", "22000.00"] },
                Ligne("Vente aux enchères 2026-11", $"Adjudication {Euros(64_000)}", "Vente aux enchères", "Validee", 0, 1, 2, 3) with { Valeurs = ["Vente aux enchères 2026-11", "640.00"] },
                Ligne("Criée du 24/03/2026, lot 8", $"Adjudication {Euros(476_000)}", "Criée judiciaire", "AFaire") with { Valeurs = ["Criée du 24/03/2026, lot 8", "4760.00"] },
            ],
        },

        // ÉCART A TRANCHER (produit) : le menu dit « Livre de répertoire », mais le code et le
        // Détail OPE disent lettres recommandées (simple, et avec accusé de réception). Le contenu
        // suit le code ; le titre du menu est gardé, faute de décision.
        new DescripteurEcran
        {
            Code = "PROC_JLR", Titre = "Livre de repertoire", Sujet = "destinataires de lettres",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher un destinataire, une adresse…",
            Criteres = ["Désignation du destinataire", "Adresse (3 lignes au plus)", "Nombre d'exemplaires"],
            // Bouton cité par la fiche (proc_jlr.md, Détail OPE) : « Configurer l'édition ».
            Valider = "Configurer l'édition",
            Rang = 6,
            Etats = EtatsEcran.Trois("À renseigner", "Adresse saisie", "Lettre générée"),
            Groupes = ["Lettre recommandée", "Lettre avec accusé de réception"],
            Marches =
            [
                Marche("Destinataire", "Désignation et adresse sont requises : une ligne à moitié remplie est refusée, le champ manquant est nommé. Une ligne vide est ignorée.", true,
                    "Désignation", "Adresse (3 lignes au plus)"),
                Marche("Exemplaires", "Nombre d'exemplaires, 1 par défaut. Une saisie vide ou nulle repasse à 1. Vingt destinataires au plus.", true,
                    "Nombre d'exemplaires"),
                Marche("Type d'envoi", "Lettre recommandée simple ou avec accusé de réception. Chaque destinataire ajoute ses éditions à la demande en cours.", true,
                    "Type de lettre"),
            ],
            Lignes =
            [
                Ligne("Destinataire de démonstration 01", "Lettre recommandée simple · 2 exemplaires", "Lettre recommandée", "Validee", 0, 1, 2),
                Ligne("Destinataire de démonstration 02", "Lettre recommandée simple · 1 exemplaire", "Lettre recommandée", "EnCours", 0, 1),
                Ligne("Destinataire de démonstration 03", "Accusé de réception · 1 exemplaire", "Lettre avec accusé de réception", "Validee", 0, 1, 2),
                Ligne("Destinataire de démonstration 04", "Adresse non saisie", "Lettre recommandée", "EnCours", 0),
                Ligne("Destinataire de démonstration 05", "Accusé de réception · 3 exemplaires", "Lettre avec accusé de réception", "AFaire"),
                Ligne("Destinataire de démonstration 06", "Lettre recommandée simple", "Lettre recommandée", "AFaire"),
                Ligne("Destinataire de démonstration 07", "Accusé de réception · 2 exemplaires", "Lettre avec accusé de réception", "EnCours", 0, 1),
            ],
        },

        // Prototype (la fiche le dit) : les agrégats sont calculés puis jetés, et les filtres PC
        // ne sont pas appliqués au calcul. La maquette montre les règles d'interaction voulues.
        // Consultation seule : aucune écriture de lettrage.
        new DescripteurEcran
        {
            Code = "PROC_VLETTRAGE", Titre = "Lettrage", Sujet = "clients",
            Forme = FormeEcran.Etat,
            // Un ecran de consultation d'agregats (proc_vlettrage.md:46,57) : criteres, Rechercher, tableau de totaux.
            Parametres = [Champ.Periode("Période"), Champ.Liste("Catégorie de client", "Compte", "Divers", "PC"), "État PC", "Type PC"],
            Colonnes = ["Client", Champ.Montant("Facturé"), Champ.Montant("Payé"), Champ.Montant("Restant")],
            Gestes = ["Rechercher"],
            Recherche = "Rechercher un client, un compte…",
            Criteres = ["Période", "Catégorie de client (compte, divers, PC)", "Filtres PC : état et type, si PC est coché"],
            // Bouton cité par la fiche (proc_vlettrage.md) : « bouton Rechercher ». Aucun geste de validation n'est cité.
            Valider = "Rechercher",
            Rang = 6,
            Etats = EtatsEcran.Trois("À vérifier", "En vérification", "Vérifié"),
            Groupes = ["Compte hors PC", "Divers", "PC"],
            Sections =
            [
                Marche("Critères", "Période et catégories de client, toutes cochées par défaut. Si PC est coché, filtres d'état (clôturé, ouvert, en attente) et de type (sauvegarde, RJ, LIJI, plan de sauvegarde, plan de redressement, CIA, CEP). Rien ne se calcule avant Rechercher.", true,
                    "Période", "Catégorie de client", "État PC", "Type PC"),
                // L'etape « Resultat » de V2 EST la grille des totaux par client, que l'Etat affiche (Colonnes) : la section qui
                // la repetait est retiree (controle final du lot 1, 25/11/2026). Reste a lire pour le lot 13 : la grille V2
                // detaille facture et paye par poste, HT, TVA, BODACC, INPI, debours (proc_vlettrage.md:113, :125).
            ],
            Lignes =
            [
                Ligne("Client compte 01", $"Facturé {Euros(425_000)} · payé {Euros(425_000)}", "Compte hors PC", "Validee", 0) with { Valeurs = ["Client compte 01", "4250.00", "4250.00", "0.00"] },
                Ligne("Client compte 02", $"Facturé {Euros(98_000)} · payé {Euros(50_000)}", "Compte hors PC", "EnCours", 0) with { Valeurs = ["Client compte 02", "980.00", "500.00", "480.00"] },
                Ligne("Client divers 01", $"Facturé {Euros(31_000)} · payé {Euros(0)}", "Divers", "AFaire") with { Valeurs = ["Client divers 01", "310.00", "0.00", "310.00"] },
                Ligne("Client divers 02", $"Facturé {Euros(112_000)} · payé {Euros(112_000)}", "Divers", "Validee", 0) with { Valeurs = ["Client divers 02", "1120.00", "1120.00", "0.00"] },
                Ligne("Client PC 01", $"Facturé {Euros(1_640_000)} · payé {Euros(200_000)}", "PC", "EnCours", 0) with { Valeurs = ["Client PC 01", "16400.00", "2000.00", "14400.00"] },
                Ligne("Client PC 02", $"Facturé {Euros(790_000)} · payé {Euros(790_000)}", "PC", "Validee", 0) with { Valeurs = ["Client PC 02", "7900.00", "7900.00", "0.00"] },
                Ligne("Client PC 03", $"Facturé {Euros(264_000)} · payé {Euros(0)}", "PC", "AFaire") with { Valeurs = ["Client PC 03", "2640.00", "0.00", "2640.00"] },
            ],
        },

        // fiche v2 sans étape ; fondé sur FORM_ADDCREANCE.cs et proc_integcreance.md:251 (sous-processus
        // ADDCREA, « fiche dédiée requise »). fix-ok: ADDCREANCE retiré à tort au premier passage (la fiche
        // n'a pas d'étape, mais son formulaire existe) ; rétabli sur décision du chef (14:01).
        // Sources, sous D:\Sources\RigApplication\Source\RIG\DLL\Processus\PROC_INTEGCREANCE\ADDCREA\ :
        // FORM_ADDCREANCE.cs:10-11 (le formulaire délègue au processus), FORM_ADDCREANCE.Designer.cs:53
        // (titre), PROC_ADDCREANCE.cs (modes, paramètres, messages), ETP_CREANCE\OPE_CREANCE.cs (contrôles).
        // Le formulaire ne déclare aucun bouton : « Valider » reprend le verbe de son action finale
        // (PROC_ADDCREANCE.cs:405-407), libellé à confirmer à l'écran.
        new DescripteurEcran
        {
            Code = "PROC_ADDCREANCE", Titre = "Ajouter une creance", Sujet = "créances transitoires",
            Forme = FormeEcran.Saisie,
            // Recherche : « débiteur » (OPE_CREANCE.cs:112, CRTRA_NOM_DEBITEUR) ; le formulaire n'a pas de recherche, c'est la barre de l'écran.
            Recherche = "Rechercher une créance ou un débiteur…",
            // Critères : le mode et les dates que le formulaire saisit (PROC_ADDCREANCE.cs:50-57 ; OPE_CREANCE.cs:339-350).
            Criteres = ["Mode", "Date de l'acte", "Date de saisine"],
            // Bouton : verbe de l'action finale du formulaire (PROC_ADDCREANCE.cs:405-407).
            Valider = "Valider",
            Rang = 6,
            // États : « saisi » et « ajout » du formulaire (OPE_CREANCE.cs:311 ; PROC_ADDCREANCE.cs:237, 407).
            Etats = EtatsEcran.Trois("À saisir", "En saisie", "Ajoutée"),
            // Groupes : les trois modes du formulaire (OPE_CREANCE.cs:284-291).
            Groupes = ["Admission", "Contestation", "Défaut de réponse"],
            Marches =
            [
                // Mode : admission par défaut ; cases Contestation et Défaut de réponse (PROC_ADDCREANCE.cs:50-57, 124-133).
                Marche("Mode", "Admission par défaut. Cocher Contestation ou Défaut de réponse change le mode de la créance.", true,
                    "Contestation", "Défaut de réponse"),
                // Montants : au moins un montant (OPE_CREANCE.cs:310-312, et 325-327).
                Marche("Montants", "Au moins un montant de créance doit être saisi, dans les cases concernées. Sans montant, la validation est bloquée.", true,
                    "Montants de la créance"),
                // Dates : saisine au plus tôt à la date de l'acte (OPE_CREANCE.cs:339-341) ; acte obligatoire en contestation (348-350).
                Marche("Dates", "La date de saisine ne précède pas la date de l'acte. Pour une contestation, la date de l'acte est obligatoire.", false,
                    "Date de saisine", "Date de l'acte"),
                // Contestation : montant contesté obligatoire, ou décocher la contestation (OPE_CREANCE.cs:356-358).
                Marche("Contestation", "Pour une créance contestée, saisir le montant contesté, ou décocher la contestation.", false,
                    "Montant contesté"),
                // Validation : action finale ; une intégration en échec nomme le problème (PROC_ADDCREANCE.cs:405-407).
                Marche("Validation", "Valider ajoute la créance. Si l'intégration échoue, le message nomme le problème et rien n'est ajouté.", true,
                    "Valider"),
            ],
            Lignes =
            [
                Ligne("Atelier Delta SARL", $"Montant {Euros(125_000)} · saisie du 12/03/2026", "Admission", "Validee", 0, 1, 2, 4),
                Ligne("Transports Gamma SAS", $"Montant {Euros(32_000)} · à valider", "Admission", "EnCours", 0, 1),
                Ligne("Boulangerie Êta SARL", "Aucun montant saisi", "Admission", "AFaire"),
                Ligne("Bêta Industrie SAS", $"Contestée · acte du 02/02/2026 · montant contesté {Euros(850_000)}", "Contestation", "Validee", 0, 1, 2, 3, 4),
                Ligne("Zêta Commerce SAS", "Contestée · date de l'acte à saisir", "Contestation", "EnCours", 0, 1, 3),
                Ligne("Epsilon Habitat SARL", $"Défaut de réponse · montant {Euros(410_000)}", "Défaut de réponse", "Validee", 0, 1, 4),
                Ligne("Thêta Import SAS", "Défaut de réponse · dates à corriger", "Défaut de réponse", "EnCours", 0, 1, 2),
                Ligne("Kappa Services SARL", "Aucune saisie", "Admission", "AFaire"),
            ],
        },

        // Les étapes, les contrôles de montant et de date, le verrou et l'état « verrouillée par un
        // autre » (lecture seule) viennent de la fiche : sections « Architecture des étapes »,
        // « DoVerifier », « DoSauve » et « Règles métier ». La fiche n'a pas de Détail OPE.
        new DescripteurEcran
        {
            Code = "PROC_MODCREANCE", Titre = "Modifier une creance", Sujet = "procédures collectives",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une procédure, un numéro, un nom d'affaire…",
            Criteres = ["Numéro de la procédure", "Nom de l'affaire", "Période d'ouverture"],
            // Bouton cité par la fiche (proc_modcreance.md) : « Valider -> Sauvegarder ».
            Valider = "Valider",
            Rang = 6,
            Etats = [.. EtatsEcran.Trois("À modifier", "En saisie", "Modifiée"), new EtatEcran("EnAttente", "Verrouillée par un autre", "en-attente")],
            Groupes = ["Sauvegarde", "Redressement judiciaire (RJ)", "Liquidation judiciaire (LIJI)"],
            Sections =
            [
                Marche("Procédure collective", "Recherche, puis sélection : la procédure est verrouillée. Si un autre utilisateur la tient, un avertissement le nomme et la fiche s'ouvre en lecture seule.", true,
                    "Numéro", "Nom de l'affaire", "Période d'ouverture"),
                Marche("Créances", "Créances, créanciers, débiteurs et état de la procédure. Seules les créances modifiées sont enregistrées.", true,
                    Champ.Tableau("Créances"), Champ.Tableau("Créanciers"), Champ.Tableau("Débiteurs"), "État de la procédure"),
                Marche("Contrôle", "Montant zéro ou plus, date de déclaration renseignée, rang et priorité cohérents. Un verrou perdu entre-temps bloque l'enregistrement.", true,
                    "Montant", "Date de déclaration", "Rang"),
                Marche("Enregistrement", "Enregistre chaque créance modifiée, recalcule les totaux si besoin, puis libère le verrou. La saisie reste ouverte pour une autre procédure.", true,
                    Champ.Tableau("Totaux")),
            ],
            Lignes =
            [
                Ligne("PC 2026-0101, Alpha Services SARL", "2 créances modifiées · 3 créanciers", "Redressement judiciaire (RJ)", "Validee", 0, 1, 2, 3),
                Ligne("PC 2026-0102, Bêta Industrie SAS", "Verrouillée par un autre utilisateur", "Liquidation judiciaire (LIJI)", "EnAttente"),
                Ligne("PC 2026-0107, Delta Transports SAS", "Créance à corriger", "Sauvegarde", "EnCours", 0, 1),
                Ligne("PC 2026-0111, Epsilon Habitat SARL", "Montant à reprendre", "Redressement judiciaire (RJ)", "EnCours", 0),
                Ligne("PC 2026-0115, Zêta Commerce SAS", "Rang à vérifier", "Liquidation judiciaire (LIJI)", "AFaire"),
                Ligne("PC 2026-0120, Êta Boulangerie SARL", "Aucune modification", "Redressement judiciaire (RJ)", "AFaire"),
                Ligne("PC 2026-0124, Thêta Import SAS", "Totaux à recalculer", "Sauvegarde", "Validee", 0, 1, 2, 3),
            ],
        },

        // La fiche a un « Rôle » barré qui renvoie au Détail OPE : le rôle repris est celui du Détail.
        new DescripteurEcran
        {
            Code = "PROC_INTEGCREANCE", Titre = "Integrer des creances", Sujet = "créances à intégrer",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une créance, un créancier, un numéro de procédure…",
            Criteres = ["Nature : admission, contestation ou défaut de réponse", "Procédure collective (une seule par intégration)", "Fichier de créances"],
            // Bouton cité par la fiche (proc_integcreance.md, Boutons) : INTEGRER_SELECTIONNEES.
            Valider = "Intégrer les créances sélectionnées",
            Rang = 6,
            Etats = [.. EtatsEcran.Trois("À intégrer", "Vérifiée", "Intégrée"), new EtatEcran("Rejetee", "En erreur", "rejetee")],
            Groupes = ["Admissions", "Contestations", "Défauts de réponse"],
            Sections =
            [
                Marche("Import du fichier", "Un fichier de créances, venu d'un cabinet de mandataires, pour une seule procédure collective. Il alimente les créances en attente d'intégration.", true,
                    "Fichier", "Procédure collective"),
                Marche("Vérification", "Statistiques avant intégration et contrôles. Les erreurs, y compris de paramétrage, sont notifiées par courriel.", true,
                    "Statistiques", "Erreurs"),
                Marche("Intégration", "Créance par créance : affaire, instance si nécessaire, parties et événements. Une erreur sur une créance n'arrête pas les autres ; le rapport final compte réussites et erreurs.", true,
                    "Nature", "Instance", "Événements"),
            ],
            Lignes =
            [
                Ligne("Créance Alpha Services SARL", $"Admission · {Euros(320_000)}", "Admissions", "Validee", 0, 1, 2),
                Ligne("Créance Bêta Industrie SAS", $"Admission · {Euros(1_580_000)}", "Admissions", "EnCours", 0, 1),
                Ligne("Créance Gamma Conseil SARL", $"Contestation · {Euros(98_000)}", "Contestations", "AFaire"),
                Ligne("Créance Delta Transports SAS", $"Contestation · {Euros(410_000)}", "Contestations", "Rejetee"),
                Ligne("Créance Epsilon Habitat SARL", "Défaut de réponse", "Défauts de réponse", "AFaire"),
                Ligne("Créance Zêta Commerce SAS", $"Défaut de réponse · {Euros(125_000)}", "Défauts de réponse", "EnCours", 0),
                Ligne("Créance Êta Boulangerie SARL", $"Admission · {Euros(62_000)}", "Admissions", "Validee", 0, 1, 2),
            ],
        },

        // La fiche n'a pas de Détail OPE : seuls le rôle et les noms d'étapes des classes
        // (entrée, sélection du représentant) sont repris. À confirmer par le chef.
        new DescripteurEcran
        {
            Code = "PROC_CREANREP", Titre = "Representation", Sujet = "procédures collectives",
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher une procédure, un créancier, un représentant…",
            Criteres = ["Procédure collective", "Créancier", "Représentant désigné"],
            // Action citée par la fiche (proc_creanrep.md, Rôle) : « désignation du représentant des créanciers ».
            Valider = "Désigner le représentant",
            Rang = 6,
            Etats = EtatsEcran.Trois("À désigner", "Représentant choisi", "Désigné"),
            Groupes = ["Représentant des créanciers"],
            Sections =
            [
                Marche("Procédure", "La procédure dont les créanciers sont à représenter, choisie dans la liste.", true,
                    "Procédure", "Créanciers"),
                Marche("Sélection du représentant", "Choix du représentant des créanciers de la procédure.", true,
                    "Représentant", "Créanciers représentés"),
            ],
            Lignes =
            [
                Ligne("PC 2026-0101, Alpha Services SARL", "Représentant désigné · 2 créanciers", "Représentant des créanciers", "Validee", 0, 1),
                Ligne("PC 2026-0102, Bêta Industrie SAS", "Aucun représentant", "Représentant des créanciers", "AFaire"),
                Ligne("PC 2026-0107, Delta Transports SAS", "Représentant choisi, à enregistrer", "Représentant des créanciers", "EnCours", 0),
                Ligne("PC 2026-0111, Epsilon Habitat SARL", "Aucun représentant", "Représentant des créanciers", "AFaire"),
                Ligne("PC 2026-0115, Zêta Commerce SAS", "Représentant désigné · 1 créancier", "Représentant des créanciers", "Validee", 0, 1),
                Ligne("PC 2026-0120, Êta Boulangerie SARL", "Choix en cours", "Représentant des créanciers", "EnCours", 0),
            ],
        },

        // Le menu de RIG v2 disait « Extinction RCS », mais la fiche décrit l'édition interne d'un
        // extrait Kbis, sans clôture ni radiation (Détail OPE, MetierDescription). Décision de
        // l'utilisateur (24/11/2026) : l'entrée et l'écran prennent le nom de ce que fait la
        // procédure. Teinte 3 = verbe Terminer.
        new DescripteurEcran
        {
            Code = "PROC_XEX", Titre = "Édition interne d'un Kbis", Sujet = "dossiers RCS",
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher un dossier RCS, un numéro de gestion…",
            Criteres = ["Numéro de gestion du dossier RCS"],
            // Action citée par la fiche (proc_xex.md, Détail OPE) : « Edition interne d'un Kbis ».
            Valider = "Éditer le Kbis",
            Rang = 3,
            Etats = EtatsEcran.Trois("À rechercher", "Dossier choisi", "Kbis édité"),
            Groupes = ["Dossiers RCS"],
            Sections =
            [
                Marche("Dossier RCS", "Recherche par numéro de gestion ; le numéro d'identification n'est pas admis ici. Un dossier doit être choisi avant de valider.", true,
                    "Numéro de gestion", "Désignation"),
                Marche("Demande", "Renseignée à partir du dossier choisi : désignation, numéro de gestion, identifiant interne. Pas de saisie ; la date est toujours celle du jour.", false,
                    "Désignation", "Numéro de gestion", "Identifiant interne"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Dossier RCS 2026B00601 · Kbis édité", "Dossiers RCS", "Validee", 0, 1),
                Ligne("Bêta Industrie SAS", "Dossier RCS 2026B00602 · dossier choisi", "Dossiers RCS", "EnCours", 0),
                Ligne("Gamma Conseil SARL", "Dossier RCS 2026B00603", "Dossiers RCS", "AFaire"),
                Ligne("Delta Transports SAS", "Dossier RCS 2026B00604", "Dossiers RCS", "AFaire"),
                Ligne("Epsilon Habitat SARL", "Dossier RCS 2026B00605 · dossier choisi", "Dossiers RCS", "EnCours", 0),
                Ligne("Zêta Commerce SAS", "Dossier RCS 2026B00606 · Kbis édité", "Dossiers RCS", "Validee", 0, 1),
                Ligne("Êta Boulangerie SARL", "Dossier RCS 2026B00607", "Dossiers RCS", "AFaire"),
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params ChampEcran[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);

    /// Un montant tenu en centimes entiers (125000 = 1 250,00 €), affiché comme web/src/format.ts
    /// (euros) : espace fine insécable U+202F pour les milliers, virgule pour les centimes.
    private static string Euros(long centimes)
    {
        var signe = centimes < 0 ? "-" : "";
        var entiers = (Math.Abs(centimes) / 100).ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
        return $"{signe}{entiers},{Math.Abs(centimes) % 100:00} €";
    }
}
