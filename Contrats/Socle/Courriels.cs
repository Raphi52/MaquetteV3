namespace RigV3.Contrats.Socle;

/// <summary>
/// Les adresses des courriels du greffe (SOC-006) : la liste des envois et leur trace, le detail d'un envoi, les
/// modeles qu'un agent peut choisir et leur apercu, les listes de distribution. Le serveur les publie
/// (Serveur/Socle/Courriels/ModuleCourriels.cs) ; l'ecran « Courriels envoyés » (Pont/EcranCourriels.cs) lit les envois,
/// l'ecran « Messagerie » (Pont/EcranMessagerie.cs) les modeles et les listes, et un agent qui a la fonction
/// <see cref="FonctionGestion"/> les y cree, les modifie, les desactive et les reactive (procedure V2 PROC_GESTEMAIL). L'ENVOI
/// n'est pas une route : un acte de la V3 qui envoie un courriel appelle le service du serveur (ServiceCourriels.EnvoyerAsync),
/// comme la V2 lancait RIG_Send_Email.
///
/// Chaque adresse porte le code du greffe (<c>{greffe}</c>) : l'agent est juge dans la base V3 de CE greffe (401 sans
/// session, 403 compte refuse, 404 greffe absent du catalogue, 503 base muette). L'adresse a appeler : <see cref="Pour"/>.
/// </summary>
public static class RoutesCourriels
{
    /// <summary>GET : les envois du greffe, un par numero de suivi, les plus recents d'abord (<see cref="CourrielSuivi"/>[]).
    /// Filtres facultatifs : <c>?du=2026-11-21&amp;au=2026-11-21&amp;destinataire=…&amp;modele=…&amp;etat=echec&amp;max=500</c>.
    /// Reserve a la fonction <see cref="FonctionCourrielsEnvoyes"/>.</summary>
    public const string Envois = "/socle/greffes/{greffe}/courriels";

    /// <summary>GET : un envoi, son texte et chacune de ses etapes (<see cref="DetailCourriel"/>) ; 404 s'il est inconnu.
    /// Reserve a la fonction <see cref="FonctionCourrielsEnvoyes"/>.</summary>
    public const string Envoi = "/socle/greffes/{greffe}/courriels/{suivi:guid}";

    /// <summary>GET : les modeles de courriel que l'agent peut choisir (<see cref="ModeleMessage"/>[]). Un modele
    /// reserve a Amitel n'y est pas pour un agent du greffe (SOC-006 CA05).
    /// POST : cree le modele <see cref="SaisieModele"/> -> <see cref="ModeleMessage"/> (201) ; reserve a <see cref="FonctionGestion"/>.</summary>
    public const string Modeles = "/socle/greffes/{greffe}/modeles-courriel";

    /// <summary>GET : un modele, son texte, ses adresses resolues pour le greffe, ses listes par defaut et son apercu
    /// (<see cref="ApercuModele"/>) ; 404 s'il n'existe pas OU s'il est reserve a Amitel et que l'agent est du greffe
    /// (CA05 : il ne le voit pas, meme en tapant son nom). L'adresse : <see cref="PourModele"/>.
    /// PUT : le modifie (<see cref="SaisieModele"/> -> <see cref="ModeleMessage"/>) ; reserve a <see cref="FonctionGestion"/> ; un modele
    /// reserve a Amitel n'est modifiable que par Amitel (404 pour un agent du greffe, qui ne le voit pas).</summary>
    public const string Modele = "/socle/greffes/{greffe}/modeles-courriel/{type}";

    /// <summary>PUT : active ou desactive un modele (<see cref="SaisieActivation"/> -> <see cref="ModeleMessage"/>), comme les boutons
    /// « Désactiver » / « Activer » de la V2 (OPE_EDIT_EMAILMODEL.cs:202-210) : un modele ne se supprime jamais. Reserve a
    /// <see cref="FonctionGestion"/>. L'adresse : <see cref="PourModele"/>.</summary>
    public const string ActivationModele = "/socle/greffes/{greffe}/modeles-courriel/{type}/actif";

    /// <summary>GET : les listes de distribution que l'agent peut voir (<see cref="ListeDiffusion"/>[]), actives ou non :
    /// les listes publiques (proprietaire « * ») et les siennes, comme la V2
    /// (ListeDistributionMail.GetAllListeDistributionMailByLogin, ListeDistributionMail.cs:63).</summary>
    public const string ListesDiffusion = "/socle/greffes/{greffe}/listes-diffusion";

    /// <summary>PUT : modifie une liste que l'agent voit (<see cref="SaisieListe"/> -> <see cref="ListeDiffusion"/>) ; 404 pour la liste
    /// privee d'un collegue. La creation est un POST sur <see cref="ListesDiffusion"/> (201). Reserve a <see cref="FonctionGestion"/>.
    /// L'adresse : <see cref="PourListe"/>.</summary>
    public const string ListeDiffusion = "/socle/greffes/{greffe}/listes-diffusion/{id:int}";

    /// <summary>PUT : active ou desactive une liste (<see cref="SaisieActivation"/> -> <see cref="ListeDiffusion"/>), comme la V2
    /// (OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:103-109). Reserve a <see cref="FonctionGestion"/>.</summary>
    public const string ActivationListe = "/socle/greffes/{greffe}/listes-diffusion/{id:int}/actif";

    /// <summary>GET : ce que le formulaire d'un modele propose pour CE greffe (<see cref="BoitesDuGreffe"/>) -- ses boites de courriel
    /// par leur ADRESSE (INFO_GREFFE, comme OPE_EDIT_EMAILMODEL_ONE_CHOICE.ConstructGroupRadioButton) et l'etat de son habillage
    /// (editeur mis en forme, $LOGO$). Lecture ; il suffit d'etre admis, comme les modeles.</summary>
    public const string Boites = "/socle/greffes/{greffe}/boites-courriel";

    /// <summary>
    /// La fonction (droit) qu'exigent la creation, la modification et l'activation des modeles et des listes : GESTEMAIL, le
    /// code processus de la procedure V2 « Modele de mail » (PROC_GESTEMAIL, proc_gestemail.md:6). Sans elle : 403, et
    /// l'ecran garde la lecture des modeles, sans bouton d'ecriture.
    /// </summary>
    public const string FonctionGestion = "GESTEMAIL";

    /// <summary>Le proprietaire d'une liste de distribution publique (LSTDM_PRIVE = « * », ListeDistributionMail.cs:31).</summary>
    public const string ListePublique = "*";

    /// <summary>
    /// La fonction (droit) qu'exigent <see cref="Envois"/> et <see cref="Envoi"/> : MAIL_LOT1, la procedure de la V2
    /// « Mails envoyés en Lot 1 » (PROCESSUS_ET_FONCTIONALITE de RIG_DEV, DLL PROC_MAIL_LOT1, lu le 2026-11-21), l'ecran
    /// de consultation des courriels partis. Sans elle : 403.
    /// </summary>
    public const string FonctionCourrielsEnvoyes = "MAIL_LOT1";

    /// <summary>Le nombre d'envois rendus au plus, comme le TOP 500 de la V2 (OPE_LISTE_MAILS_LOT1.cs:16), jamais illimite.</summary>
    public const int MaximumParDefaut = 500;

    /// <summary>L'adresse d'une de ces routes pour le greffe <paramref name="greffe"/> (et l'envoi <paramref name="suivi"/>).</summary>
    public static string Pour(string route, string greffe, Guid? suivi = null)
        => route.Replace("{greffe}", Uri.EscapeDataString(greffe))
                .Replace("{suivi:guid}", suivi?.ToString("D") ?? "");

    /// <summary>L'adresse de <see cref="Modele"/> (ou de <see cref="ActivationModele"/>) pour le modele <paramref name="type"/> du
    /// greffe <paramref name="greffe"/>.</summary>
    public static string PourModele(string greffe, string type, string route = Modele)
        => Pour(route, greffe).Replace("{type}", Uri.EscapeDataString(type));

    /// <summary>L'adresse de <see cref="ListeDiffusion"/> (ou de <see cref="ActivationListe"/>) pour la liste <paramref name="id"/>.</summary>
    public static string PourListe(string greffe, int id, string route = ListeDiffusion)
        => Pour(route, greffe).Replace("{id:int}", id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>L'adresse de <see cref="Envois"/> avec ses filtres (vides omis).</summary>
    public static string Filtrer(string greffe, DateOnly? du = null, DateOnly? au = null, string? destinataire = null,
                                 string? modele = null, string? etat = null, int? max = null)
    {
        var filtres = new List<string>();
        if (du is { } d) filtres.Add("du=" + d.ToString("yyyy-MM-dd"));
        if (au is { } a) filtres.Add("au=" + a.ToString("yyyy-MM-dd"));
        if (!string.IsNullOrWhiteSpace(destinataire)) filtres.Add("destinataire=" + Uri.EscapeDataString(destinataire.Trim()));
        if (!string.IsNullOrWhiteSpace(modele)) filtres.Add("modele=" + Uri.EscapeDataString(modele.Trim()));
        if (!string.IsNullOrWhiteSpace(etat)) filtres.Add("etat=" + Uri.EscapeDataString(etat.Trim()));
        if (max is { } m) filtres.Add("max=" + m);
        var adresse = Pour(Envois, greffe);
        return filtres.Count == 0 ? adresse : adresse + "?" + string.Join("&", filtres);
    }
}

/// <summary>
/// Ce qu'est devenu un envoi, deduit de ses etapes (<see cref="EtapesCourriel"/>).
/// </summary>
public static class EtatsCourriel
{
    /// <summary>Le serveur de messagerie l'a accepte (EMAIL_SEND_SUCCES sans SMTP_IGNORE_DEV).</summary>
    public const string Envoye = "envoye";

    /// <summary>Hors production : il n'a PAS ete remis au serveur de messagerie, il est dans la boite de depot (SMTP_IGNORE_DEV, CA06).</summary>
    public const string Ignore = "ignore";

    /// <summary>Le serveur de messagerie l'a refuse, ou il n'a pas pu etre prepare (EMAIL_SEND_ERREUR, PREPARATION_ERREUR).</summary>
    public const string Echec = "echec";

    /// <summary>Commence sans issue ecrite (l'envoi a ete interrompu).</summary>
    public const string EnCours = "en-cours";

    public static readonly string[] Tous = [Envoye, Ignore, Echec, EnCours];
}

/// <summary>
/// Les etapes ecrites dans la trace, celles de la V2 (envoi-mail-rig-send-email.md:92-97, EmailEnvoye.cs:130-175,
/// 391-408) plus une de la V3 : PREPARATION_ERREUR (modele inconnu, adresse introuvable : rien n'est remis).
/// </summary>
public static class EtapesCourriel
{
    public const string PreparationEmail = "PREPARATION_EMAIL";
    public const string PreparationErreur = "PREPARATION_ERREUR";
    public const string EnvoiDebut = "EMAIL_SEND_DEBUT";
    public const string AppelSmtp = "SMTP_APPEL";
    public const string IgnoreHorsProduction = "SMTP_IGNORE_DEV";
    public const string EnvoiSucces = "EMAIL_SEND_SUCCES";
    public const string EnvoiErreur = "EMAIL_SEND_ERREUR";
    public const string CourrielEnregistre = "EMAIL_ENVOYE";
}

/// <summary>
/// Un envoi, tel que la liste « Courriels envoyés » le montre : une ligne par numero de suivi (toutes ses etapes reunies).
/// </summary>
/// <param name="Suivi">Le numero de suivi de l'envoi (EMTRC_CORRELATION_ID de la V2).</param>
/// <param name="Debut">L'heure de sa premiere etape (heure du serveur de la base).</param>
/// <param name="Etat">Un de <see cref="EtatsCourriel"/>.</param>
/// <param name="TypeModele">Le modele de courriel (EMAILMOD_TYPE).</param>
/// <param name="NbPieces">Le nombre de documents (liens) joints.</param>
/// <param name="IdCourriel">Le courriel enregistre (soc.CourrielEnvoye) une fois parti ; null sinon.</param>
/// <param name="Erreur">Le message de l'erreur d'un echec ; null sinon.</param>
/// <param name="Agent">Le compte de l'agent qui l'a demande, s'il est connu.</param>
/// <param name="FichierDepot">Hors production : le fichier de la boite de depot ou il a ete range.</param>
public sealed record CourrielSuivi(
    Guid Suivi,
    DateTime Debut,
    string Etat,
    string? TypeModele,
    string? Expediteur,
    string? Destinataires,
    string? Copie,
    string? Sujet,
    int? NbPieces,
    int? IdCourriel,
    string? Erreur,
    string? Agent,
    string? FichierDepot);

/// <summary>Une etape de la trace d'un envoi (soc.TraceEnvoi).</summary>
public sealed record EtapeCourriel(
    DateTime Instant,
    string Etape,
    string? Mode,
    bool? Succes,
    string? SmtpHote,
    int? SmtpPort,
    string? ErreurType,
    string? ErreurMessage,
    string? FichierDepot);

/// <summary>Un envoi en detail : sa ligne, le texte du courriel parti (null s'il n'est pas parti) et ses etapes dans l'ordre.</summary>
public sealed record DetailCourriel(
    CourrielSuivi Envoi,
    string? Corps,
    bool? CorpsHtml,
    EtapeCourriel[] Etapes);

/// <summary>
/// Un modele de courriel du greffe (soc.ModeleMessage, EMAIL_MODEL de la V2), sans son texte : ce que montre une liste de choix.
/// Les cases « Utilisable … » de l'ecran V2 (OPE_EDIT_EMAILMODEL.cs:254-258) : <paramref name="Lot1"/> RCS,
/// <paramref name="Lot2"/> judiciaire, <paramref name="Inscription"/> privilege, <paramref name="Comptabilite"/> comptabilite.
/// </summary>
/// <param name="Expediteur">Tel qu'en base : une etiquette « [ADRESSE_MAIL_…] » pour une boite du greffe, sinon l'adresse.</param>
/// <param name="UtilisableEnSaisie">Faux : le modele est desactive (la V2 ne le supprime jamais, elle decoche cette case).</param>
public sealed record ModeleMessage(
    string Type,
    string? LibelleEcran,
    string Sujet,
    bool CorpsHtml,
    string? Expediteur,
    string? Reponse,
    string? Copie,
    string? DestinataireDefaut,
    bool ReserveAmitel,
    bool EnvoiAutomatique,
    bool? UtilisableEnSaisie,
    bool Lot1,
    bool Lot2,
    bool? Inscription = null,
    bool? Comptabilite = null);

/// <summary>Une liste de distribution du greffe (soc.ListeDiffusion, LISTE_DISTRIBUTION_MAIL de la V2).</summary>
/// <param name="Adresses">« a;b;c », comme LSTDM_ADR_MAILS.</param>
/// <param name="Proprietaire">« * » (<see cref="RoutesCourriels.ListePublique"/>) pour une liste de tous les agents, sinon le
/// compte de l'agent a qui elle est (LSTDM_PRIVE).</param>
public sealed record ListeDiffusion(
    int Id,
    string Code,
    string Libelle,
    string Adresses,
    bool Rcs,
    bool Judiciaire,
    string? Proprietaire,
    bool Actif);

/// <summary>
/// Un modele ouvert dans l'ecran « Messagerie » : son texte, ses adresses telles qu'elles partiraient pour CE greffe
/// (etiquettes « [ADRESSE_MAIL_…] » remplacees par INFO_GREFFE, comme SendEmail.filtrerMail), ses listes de distribution par
/// defaut, et le courriel rendu comme il partirait (habillage du greffe, jetons, logo), avec deux documents d'exemple
/// comme l'apercu de la V2 (OPE_EDIT_TEMPLATE_MAIL._Apercu). Rien n'est envoye ni ecrit pour le construire.
/// </summary>
/// <param name="Corps">Le texte du modele tel qu'en base (« | » = saut de ligne).</param>
/// <param name="Expediteur">L'expediteur resolu ; "" si son etiquette n'a pas de valeur dans le greffe.</param>
/// <param name="Rendu">Le courriel en HTML, logo inclus (data:), a montrer dans un cadre sans script.</param>
/// <param name="Habille">L'habillage du greffe a ete applique (sinon : le rendu historique de la V2).</param>
/// <param name="ValeursManquantes">Ce que le greffe n'a pas pour ce courriel (etiquette sans valeur, ville, greffier, logo).</param>
/// <param name="SignatureActive">EMAILMOD_SIGNATURE_ACTIVE (colonne de RIG_DEV, absente du projet V2 versionne).</param>
/// <param name="Signature">EMAILMOD_SIGNATURE, la signature gardee avec le modele.</param>
public sealed record ApercuModele(
    ModeleMessage Modele,
    string Corps,
    string Expediteur,
    string Reponse,
    string Copie,
    string DestinataireDefaut,
    string Rendu,
    bool Habille,
    string[] ValeursManquantes,
    ListeDiffusion[] ListesParDefaut,
    bool SignatureActive = false,
    string? Signature = null);

/// <summary>
/// Un modele de courriel tel que l'ecran « Messagerie » l'enregistre : POST <see cref="RoutesCourriels.Modeles"/> pour le creer,
/// PUT <see cref="RoutesCourriels.Modele"/> pour le modifier. Les champs de l'ecran V2 « Modele de mail » (OPE_EDIT_EMAILMODEL et
/// ses sous-operations). Le serveur controle chaque saisie et rend 400 avec le motif (<see cref="RefusGreffe"/>) : nom deja pris,
/// champ obligatoire vide, texte trop long, adresse invalide, $PieceJointe$ manquant, $LOGO$ sous un habillage.
/// </summary>
/// <param name="Type">Le nom du modele (EMAILMOD_TYPE, 40 caracteres au plus), unique ; il ne change plus une fois cree.</param>
/// <param name="Corps">Le texte (« | » = saut de ligne) ; il devient HTML s'il porte $LOGO$ (EmailModel.Sauve, EmailModel.cs:307-308).
/// Le texte d'un modele HTML est assaini a chaque enregistrement, regles de RigHtmlSanitizer (scripts, styles, commentaires,
/// balises font, gestionnaires on…, liens javascript: retires) : la base garde le texte assaini.</param>
/// <param name="Expediteur">Obligatoire : une boite du greffe en etiquette (« Greffe &lt;[ADRESSE_MAIL_RCS]&gt; ») ou une adresse ;
/// l'ecran fait choisir la boite par son adresse (<see cref="RoutesCourriels.Boites"/>) et envoie l'etiquette.</param>
/// <param name="Reponse">« Répondre à » : adresses ou etiquettes separees par « ; ».</param>
/// <param name="Copie">« Copie cachée » : adresses ou etiquettes separees par « ; ».</param>
/// <param name="EnvoiAutomatique">N'existe que pour un modele RCS (<paramref name="Lot1"/>) : sans lui, il est remis a faux, comme
/// la V2 (OPE_EDIT_EMAILMODEL._SetVisibilityChkEnvoiAuto).</param>
/// <param name="ReserveAmitel">« Modèle modifiable uniquement par Amitel » : un agent du greffe qui le coche est refuse (403).</param>
/// <param name="ListesParDefaut">Les listes de distribution par defaut, parmi les ACTIVES que l'agent voit (publiques et les
/// siennes). Comme la V2, le modele n'a plus ensuite QUE ces listes : un lien vers la liste privee d'un collegue ou vers une liste
/// desactivee part aussi (OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.cs:58-61).</param>
/// <param name="Ecran">L'ecran du geste, ecrit au journal des gestes.</param>
public sealed record SaisieModele(
    string Type,
    string? LibelleEcran,
    string Sujet,
    string Corps,
    string? Expediteur,
    string? Reponse,
    string? Copie,
    string? DestinataireDefaut,
    bool Lot1,
    bool Lot2,
    bool Inscription,
    bool Comptabilite,
    bool EnvoiAutomatique,
    bool ReserveAmitel,
    bool SignatureActive,
    string? Signature,
    int[]? ListesParDefaut = null,
    string? Ecran = null);

/// <summary>
/// Une liste de distribution telle que l'ecran « Messagerie » l'enregistre : POST <see cref="RoutesCourriels.ListesDiffusion"/>
/// pour la creer, PUT <see cref="RoutesCourriels.ListeDiffusion"/> pour la modifier (OPE_EDIT_LISTEDISTRIBUTIONMAIL de la V2).
/// </summary>
/// <param name="Code">Le code (LSTDM_CODE, 35 au plus), unique dans le greffe ; il ne change plus une fois cree.</param>
/// <param name="Adresses">Les adresses, une par ligne ou separees par « ; » ; chacune doit etre valide (ULT_MULTITEXT_MULTIMAIL).</param>
/// <param name="Publique">Vrai : liste de tous les agents (« * ») ; faux : liste privee de l'agent qui l'enregistre. Une liste
/// publique ne redevient pas privee (OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:198).</param>
public sealed record SaisieListe(
    string Code,
    string Libelle,
    string Adresses,
    bool Rcs,
    bool Judiciaire,
    bool Publique,
    string? Ecran = null);

/// <summary>Activer (vrai) ou desactiver (faux) un modele ou une liste.</summary>
public sealed record SaisieActivation(bool Actif, string? Ecran = null);

/// <summary>Une boite de courriel du greffe : son etiquette (la colonne d'INFO_GREFFE, « ADRESSE_MAIL_RCS ») et son adresse
/// dans CE greffe ("" s'il n'en a pas). Un modele enregistre l'etiquette (« [ADRESSE_MAIL_RCS] »), jamais l'adresse.</summary>
// fix-ok: lot 16 -- aucune route ne donnait a l'ecran les adresses des boites du greffe (VueEcranMessagerie lue avant le lot :
// modeles, listes, droit, identite seulement) : l'agent tapait l'etiquette au lieu de choisir l'adresse, comme dans la V2.
public sealed record BoiteCourriel(string Etiquette, string Adresse);

/// <summary>
/// Ce que le formulaire d'un modele propose pour CE greffe (GET <see cref="RoutesCourriels.Boites"/>).
/// </summary>
/// <param name="Boites">Les boites que la V2 propose (expediteur, « Répondre à », « Copie cachée » : OPE_EDIT_EMAILMODEL_ONE_CHOICE,
/// OPE_EDIT_EMAILMODEL_MANY_CHOICES), avec leur adresse d'INFO_GREFFE ; jamais une colonne de mot de passe.</param>
/// <param name="HabillageRegle">Le reglage SEND_EMAIL_TEMPLATE_PATH est rempli : un modele HTML s'edite dans l'editeur mis en forme
/// (OPE_EDIT_EMAILMODEL._TemplateActif) et doit porter $PieceJointe$.</param>
/// <param name="HabillagePresent">Le greffe a un habillage, en base ou en fichier : $LOGO$ est refuse (le logo est dans l'habillage).</param>
public sealed record BoitesDuGreffe(BoiteCourriel[] Boites, bool HabillageRegle, bool HabillagePresent)
{
    /// <summary>
    /// Les boites que le formulaire d'un modele propose, colonnes d'INFO_GREFFE : les cinq de l'expediteur et de « Répondre à »
    /// (OPE_EDIT_EMAILMODEL_ONE_CHOICE.ConstructGroupRadioButton), puis les quatre accuses d'envoi de « Copie cachée »
    /// (OPE_EDIT_EMAILMODEL_MANY_CHOICES.ConstructCheckBox). Jamais une colonne de mot de passe.
    /// </summary>
    public static readonly string[] Proposees =
    [
        "ADRESSE_MAIL_RCS", "ADRESSE_MAIL_JUDICIAIRE", "ADRESSE_MAIL_NANTISSEMENTS", "ADRESSE_MAIL_COMPTABILITE", "ADRESSE_MAIL_NO_REPLY",
        "ADRESSE_MAIL_ACCUSE_ENVOI_JUD", "ADRESSE_MAIL_ACCUSE_ENVOI_RCS", "ADRESSE_MAIL_ACCUSE_ENVOI_NANT", "ADRESSE_MAIL_ACCUSE_ENVOI_CPT",
    ];
}
