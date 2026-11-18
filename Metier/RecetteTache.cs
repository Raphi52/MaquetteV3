namespace RigV3Desktop;

/// <summary>
/// Un champ que la tache demande de modifier, nomme comme la base : la
/// TABLE et la COLONNE de RIG. C'est la meme paire que celle declaree en
/// base v2 dans ULT_OP_ET_EVENEMENT_RCS (colonnes TABLE / ATTRIBUT), lue
/// sur RIG_AMIENS@SQL-PROD\PROD le 16/09/2026.
/// </summary>
/// <remarks>
/// Trois qualites ont ete AJOUTEES en accueillant la 2e recette (changement
/// de dirigeant, 16/09/2026) -- la 1re, DENO, ne les demandait pas :
///   <paramref name="Obligatoire"/> : la date de fin de fonction n'existe que
///     s'il y a un sortant. Avec DENO seul, TOUT champ etait exige.
///   <paramref name="Repetable"/> : un dirigeant est une LIGNE, pas une case.
///     Une societe peut en changer trois d'un coup. Une denomination, non.
///   <paramref name="Declare"/> existait deja : il dit si la base rattache
///     vraiment ce champ a l'evenement.
/// </remarks>
/// <remarks>
/// <paramref name="ObligatoireSiRenseigne"/> AJOUTE le 16/09/2026 apres
/// mesure en base : le nom de domaine n'existe que sur 1 072 des 239 564
/// etablissements d'Amiens (0,45 %). L'exiger TOUJOURS bloquerait la
/// cloture sur 99,5 % des dossiers et pousserait a inventer une donnee ;
/// ne jamais l'exiger laisserait passer le vrai risque -- un domaine qui
/// EXISTE et qu'on oublie de reprendre apres un changement de nom. Un
/// champ marque ainsi n'est exige QUE si le dossier en porte deja un.
/// </remarks>
public sealed record ChampRecette(string Table, string Colonne, string Libelle,
                                  bool Declare = true, bool Obligatoire = true,
                                  bool Repetable = false,
                                  bool ObligatoireSiRenseigne = false);

/// <summary>
/// Un EFFET de l'evenement AILLEURS que dans les champs : ce qui se
/// propage hors du dossier quand la modification est faite. Renommer une
/// societe ne s'arrete pas au champ -- il faut publier, notifier, reprendre
/// ce qui porte l'ancien nom. Un effet non fait est un dossier a moitie
/// traite, et personne ne s'en apercoit avant la reclamation.
///
/// Les effets de DENO ne sont pas inventes : ils sont LUS dans le
/// referentiel CODE_EVENEMENT_RCS de RIG (RIG_AMIENS@SQL-PROD\PROD,
/// R_CODEEVENEM_VALEUR = 'DENO', lecture du 16/09/2026) --
/// BODACC B1/C1/D1 = B0111/B0112/B0113, INPI 0F, Infogreffe 4028,
/// notification aux etablissements SECONDAIRES (principal : non).
/// </summary>
/// <remarks>
/// <paramref name="Piece"/> AJOUTE le 17/09/2026 : la PREUVE de l'effet est
/// la piece deposee dans l'arbre (l'avis BODACC, l'accuse INPI). Sans elle,
/// la case se cochait a la main -- une coche de complaisance ne prouve rien.
/// Vide = aucun document n'atteste ce canal (une notification, par exemple) :
/// la case reste alors a la main, avec son motif au journal.
/// </remarks>
public sealed record EffetRecette(string Canal, string Libelle, string Code,
                                  bool Obligatoire, string Source, string Piece = "");

/// <summary>
/// Une RECETTE : ce qu'un evenement recurrent demande de faire, sous une
/// forme DECLARATIVE (des donnees, pas du code). L'arbre et la fiche ne
/// decident rien : ils rendent ce que la recette declare.
///
/// Recette du prototype, « DENO » (modification de la denomination) :
///   - INSCRIT_PM.DENOMINATION_USAGE  -- DECLARE en base (ULT_OP_ET_EVENEMENT_RCS,
///     seul champ que la base rattache a DENO, lecture du 16/09/2026).
///   - ETABLISSEMENT.NOM_DOMAINE      -- NON declare en base : la base ne
///     rattache aucun nom de domaine a DENO. Le champ existe bien dans la
///     fiche (Metier/InformationsDossier.cs, section ETABLISSEMENT), mais
///     c'est la DEMANDE metier qui l'ajoute ici, pas le referentiel.
/// </summary>
/// <remarks>
/// <paramref name="CodesEvenement"/> a ete AJOUTE pour la 2e recette : le
/// changement de dirigeant porte DEUX codes RCS selon la forme de la
/// societe -- DPER (societes de personnes : SNC, SCP) et DSTE (societes de
/// capitaux : SARL et assimilees), lus en base le 16/09/2026. C'est UNE
/// tache pour l'utilisateur et DEUX evenements pour le registre : le Code
/// reste l'identite de la recette, CodesEvenement dit ce qu'elle couvre.
/// </remarks>
public sealed record RecetteTache(string Code, string Titre, string Detail,
                                  ChampRecette[] Champs, string[] Branches,
                                  EffetRecette[] Effets,
                                  string[]? CodesEvenement = null,
                                  string[]? PiecesGardees = null)
{
    /// <summary>Les codes RCS que cette tache couvre (au moins le sien).</summary>
    public string[] Evenements => CodesEvenement is { Length: > 0 } c ? c : [Code];

    /// <summary>Les champs exiges INCONDITIONNELLEMENT.</summary>
    public ChampRecette[] ChampsObligatoires => Champs.Where(c => c.Obligatoire).ToArray();

    /// <summary>
    /// Un champ est-il exige pour CE dossier ? Les champs marques
    /// « obligatoire si renseigne » ne le sont que si le dossier porte deja
    /// une valeur -- c'est ce que <paramref name="valeurDorigine"/> dit.
    /// Sans source d'origine (maquette non branchee), ils sont PROPOSES :
    /// on ne bloque jamais sur une donnee dont on ignore si elle existe.
    /// </summary>
    public bool EstExige(ChampRecette c, Func<string, string?>? valeurDorigine)
        => c.Obligatoire
           || c.ObligatoireSiRenseigne
              && valeurDorigine is not null
              && !string.IsNullOrWhiteSpace(valeurDorigine(Cle(c)));

    /// <summary>Les champs reellement exiges pour ce dossier.</summary>
    public ChampRecette[] ChampsExiges(Func<string, string?>? valeurDorigine)
        => Champs.Where(c => EstExige(c, valeurDorigine)).ToArray();

    /// <summary>Les effets qu'il faut avoir traites pour pouvoir clore.</summary>
    public EffetRecette[] EffetsObligatoires => Effets.Where(e => e.Obligatoire).ToArray();

    /// <summary>
    /// Les effets obligatoires qui ne sont PAS encore coches. Tant qu'il en
    /// reste un, la tache ne se clot pas : c'est la difference entre
    /// « le champ est change » et « le dossier est traite ».
    /// </summary>
    public EffetRecette[] EffetsRestants(Func<string, bool> estCoche)
        => EffetsObligatoires.Where(e => !estCoche(e.Canal)).ToArray();

    /// <summary>
    /// Un DOSSIER de l'arbre se montre s'il est nomme par la recette. Une
    /// piece ou une fiche, elle, suit son parent : la recette declare des
    /// BRANCHES, pas chaque document. Rien n'est supprime -- seulement
    /// replie, et le bouton « Afficher tout le dossier » le rouvre.
    /// </summary>
    /// <remarks>
    /// Volontairement exprime en LIBELLES et pas en NoeudGed : la recette ne
    /// depend d'aucun autre type du projet, donc chaque banc de controle peut
    /// la compiler seule.
    /// </remarks>
    public bool GardeBranche(string libelleParent, string libelleEnfant, bool estDocument)
        => Branches.Length == 0
           || Branches.Contains(libelleEnfant)
           // Un DOSSIER ne se montre que s'il est nomme par la recette ; une
           // piece ou une fiche, elle, suit son parent. La recette declare
           // des BRANCHES, pas chaque document.
           || estDocument && Branches.Contains(libelleParent);

    /// <summary>
    /// Les pieces que la tache garde ouvertes. Declarees a part des
    /// BRANCHES : jusqu'ici le repli s'arretait au dossier, donc le dossier
    /// entier restait a l'ecran (37 pieces visibles pour une tache qui n'en
    /// vise que trois). Vide = aucune piece n'est nommee, et toutes suivent
    /// leur branche -- le comportement d'avant, garde pour les recettes qui
    /// n'ont pas encore nomme leurs pieces.
    /// </summary>
    public string[] PiecesAttendues => PiecesGardees ?? [];

    /// <summary>
    /// Cette PIECE reste-t-elle visible ? Le test porte sur le libelle, par
    /// morceau : « Avis BODACC » retrouve « Avis BODACC - immatriculation »,
    /// dont le suffixe change a chaque dossier.
    /// </summary>
    public bool GardePiece(string libelle)
        => PiecesAttendues.Length == 0
           || PiecesAttendues.Any(p => libelle.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>L'effet de ce canal, s'il appartient a la recette.</summary>
    public EffetRecette? Effet(string canal)
        => Effets.FirstOrDefault(e => e.Canal == canal);

    /// <summary>
    /// La MEME recette, restreinte aux pieces d'UN effet : cliquer une ligne
    /// de la checklist filtre l'arbre sur ce qu'elle attend. Un effet sans
    /// piece attestante ne restreint rien (la recette est rendue telle quelle).
    /// </summary>
    public RecetteTache PourEffet(string canal)
        => Effet(canal) is { Piece.Length: > 0 } e ? this with { PiecesGardees = [e.Piece] } : this;

    /// <summary>
    /// Cet effet est-il PROUVE ? La preuve n'est pas la case cochee mais la
    /// presence de la piece dans l'arbre du dossier.
    /// </summary>
    public bool EffetAtteste(string canal, Func<string, bool> piecePresente)
        => Effet(canal) is { Piece.Length: > 0 } e && piecePresente(e.Piece);

    /// <summary>Vrai si ce champ de la fiche est l'un de ceux de la tache.</summary>
    public bool GardeChamp(string table, string colonne)
        => Champs.Any(c => c.Table == table && c.Colonne == colonne);

    /// <summary>
    /// La cle d'un champ, telle que la fiche la note dans la trace.
    /// Un champ REPETABLE porte en plus son rang de ligne :
    /// « DIRIGEANT[2].FONCTION_DIRIGEANT ». Sans ce rang, le 2e dirigeant
    /// ECRASERAIT le 1er dans la trace, et la tache paraitrait complete
    /// alors qu'une seule ligne aurait ete saisie.
    /// </summary>
    public static string Cle(ChampRecette c, int ligne = 1)
        => c.Repetable ? $"{c.Table}[{ligne}].{c.Colonne}" : $"{c.Table}.{c.Colonne}";

    /// <summary>Vrai si cette tache se saisit en plusieurs lignes.</summary>
    public bool ARepetition => Champs.Any(c => c.Repetable);

    /// <summary>Ce qu'une ligne represente, pour l'afficher (« Dirigeant 2 »).</summary>
    public string LibelleLigne => Code == RecettesTaches.CodeDirigeant ? "Dirigeant" : "Ligne";

    /// <summary>
    /// Les champs de la tache qui ne sont PAS encore renseignes. La source
    /// n'est pas l'ecran mais la TRACE des modifications du dossier : ce qui
    /// prouve la fin de la tache est ce qui a ete reellement ecrit, avec son
    /// auteur et son horodatage. Une valeur effacee redevient manquante.
    /// </summary>
    /// <remarks>
    /// <paramref name="lignes"/> : les champs repetables sont exiges sur
    /// CHACUNE des lignes ouvertes. Ajouter un 2e dirigeant rouvre donc la
    /// tache -- c'est voulu : une ligne vide ajoutee par erreur doit se voir,
    /// pas se laisser clore en silence.
    /// </remarks>
    public ChampRecette[] Manquants(Func<string, string?> derniereValeur, int lignes = 1,
                                    Func<string, string?>? valeurDorigine = null)
        => ManquantsParLigne(derniereValeur, lignes, valeurDorigine)
            .Select(x => x.Champ).Distinct().ToArray();

    /// <summary>Les champs manquants AVEC le rang de ligne ou ils manquent.</summary>
    public (ChampRecette Champ, int Ligne)[] ManquantsParLigne(
        Func<string, string?> derniereValeur, int lignes = 1,
        Func<string, string?>? valeurDorigine = null)
    {
        var trous = new List<(ChampRecette, int)>();
        foreach (var c in ChampsExiges(valeurDorigine))
        {
            var rangs = c.Repetable ? Enumerable.Range(1, Math.Max(1, lignes)) : [1];
            foreach (var rang in rangs)
                if (string.IsNullOrWhiteSpace(derniereValeur(Cle(c, rang))))
                    trous.Add((c, rang));
        }
        return trous.ToArray();
    }

    /// <summary>Vrai quand tous les champs exiges de toutes les lignes sont ecrits.</summary>
    public bool EstComplete(Func<string, string?> derniereValeur, int lignes = 1,
                           Func<string, string?>? valeurDorigine = null)
        => ManquantsParLigne(derniereValeur, lignes, valeurDorigine).Length == 0;

    /// <summary>Vrai si une section de la fiche porte au moins un champ de la tache.</summary>
    public bool GardeTable(string table)
        => Champs.Any(c => c.Table == table);
}

/// <summary>Les recettes connues. Une seule pour l'instant : le prototype DENO.</summary>
public static class RecettesTaches
{
    // ============================================================
    // PREFIXES DU REFERENTIEL -- elucide le 16/09/2026 sur RIG_AMIENS.
    //
    // Dans ULT_OP_ET_EVENEMENT_RCS, la colonne TABLE porte parfois un
    // prefixe. Les deux ne veulent PAS dire la meme chose, et les confondre
    // fait soit perdre de vrais champs, soit en inventer :
    //
    //   « # » (#TITRE, #CXML, #SOUS_ADRESSE, #INSCRIT_PP)
    //     = un TITRE de bloc ou un controle d'ecran, PAS un champ. Aucune
    //       colonne de la base ne porte ces noms. Exemple : #TITRE /
    //       #PRESENCE_SALARIES revient sur 10 evenements de radiation --
    //       c'est l'intitule du bloc « presence de salaries », dont les
    //       vraies colonnes sont ETABLISSEMENT.PRESENCE_SALARIE_OUI/NON et
    //       INSCRIT_PM.PRESENCE_SALARIE_RAD_OUI/NON. A ignorer comme champ.
    //
    //   « X » (XINSCRIT_PP)
    //     = un prefixe de PROCESSUS v2, la table est bien REELLE. Les deux
    //       attributs de XINSCRIT_PP (INSAI_AUTRES_BIENS_LIEU et
    //       INSAI_AUTRES_BIENS_RENON_LIEU) existent dans INSCRIT_PP. Les
    //       ignorer perdrait de VRAIS champs. Ne concerne aujourd'hui que
    //       l'evenement INSAI (insaisissabilite), qui n'a pas de recette.
    //
    // AUTRE PIEGE, de la meme lecture : l'ATTRIBUT du referentiel est la
    // colonne SANS son prefixe de table. « PATRONYME » se lit
    // INSCRIT_PP.INSPP_PATRONYME, « DENOMINATION_USAGE » se lit
    // INSCRIT_PM.INSPM_DENOMINATION_USAGE, « PRESENCE_SALARIE_OUI » se lit
    // ETABLISSEMENT.ETBLS_PRESENCE_SALARIE_OUI. Le jour ou ces recettes
    // seront branchees sur la vraie base, c'est cette regle qu'il faudra
    // appliquer -- chercher la colonne telle quelle ne trouve RIEN.
    // ============================================================

    /// <summary>Code de l'evenement RCS « modification de la denomination ».</summary>
    public const string CodeDeno = "DENO";

    public static readonly RecetteTache Deno = new(
        CodeDeno,
        "Renommer la societe et son nom de domaine",
        "Evenement DENO - modification de la denomination",
        [
            new("INSCRIT_PM", "DENOMINATION_USAGE", "Denomination d'usage"),
            // DECIDE le 16/09/2026, apres mesure : exige UNIQUEMENT si le
            // dossier porte deja un nom de domaine (0,45 % des
            // etablissements). Le demander partout aurait bloque la cloture
            // de 99,5 % des dossiers ; ne jamais le demander aurait laisse
            // passer le cas qui compte -- un domaine existant, oublie apres
            // le changement de nom. Reversible : passer Obligatoire: true
            // le rend exige partout, Obligatoire: false le rend propose.
            new("ETABLISSEMENT", "NOM_DOMAINE", "Nom de domaine",
                Declare: false, Obligatoire: false, ObligatoireSiRenseigne: true),
        ],
        // Les branches que la tache garde ouvertes : le chemin vers la
        // fiche, la formalite qui porte l'evenement, et l'endroit ou
        // deposer la preuve. Le reste se replie.
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        // CE QUI SE PROPAGE. Valeurs LUES en base pour DENO le 16/09/2026
        // (RIG_AMIENS, CODE_EVENEMENT_RCS, R_CODEEVENEM_VALEUR = 'DENO') :
        // BODACC B1 = B0111, C1 = B0112, D1 = B0113 ; INPI 0F ;
        // Infogreffe 4028 ; notification au principal = NON, au
        // secondaire = OUI. Rien ici n'est invente.
        [
            // La PIECE en fin de ligne est ce qui atteste l'effet dans
            // l'arbre : c'est elle qui coche la case, pas un clic.
            new("bodacc", "Publication BODACC (annonce de modification)", "B0111 / B0112 / B0113",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_BODACC_SI_B1/C1/D1", "Avis BODACC"),
            new("inpi", "Transmission INPI (registre national)", "0F",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4028",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            // La base dit NON pour le principal : l'effet est montre, mais il
            // n'est PAS exige -- montrer ce qui ne s'applique pas evite qu'on
            // se demande s'il a ete oublie.
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    /// <summary>Code de la 2e recette : changement de dirigeant.</summary>
    public const string CodeDirigeant = "DSTE";

    /// <summary>
    /// 2e recette : CHANGEMENT DE DIRIGEANT. Choisie parce que c'est le 2e
    /// volume reel de 2025 a Amiens apres les beneficiaires effectifs.
    ///
    /// Valeurs LUES en base le 16/09/2026 (CODE_EVENEMENT_RCS) :
    ///   DSTE « Modification des dirigeants societe de capitaux » --
    ///     BODACC B1 = B0111, C1 = B0112, D1 = B0113 ; INPI 0F ;
    ///     Infogreffe 4206 ; AUCUNE notification (ni principal ni secondaire).
    ///   DPER « Modification de dirigeant d'une personne morale (societes de
    ///     personnes) » -- memes canaux SAUF C1, qui est VIDE.
    /// NGER « Nomination de gerant » n'en fait PAS partie : sa consigne en
    /// base dit « uniquement pour la saisie des depots d'actes », et il n'a
    /// ni BODACC ni Infogreffe. Le compter comme un changement de dirigeant
    /// aurait gonfle la tache d'un evenement qui ne modifie pas le dossier.
    ///
    /// Les CHAMPS, eux, ne sont PAS declares en base : ULT_OP_ET_EVENEMENT_RCS
    /// ne rattache aucune table ni colonne a DPER ni a DSTE (mesure du
    /// 16/09/2026). Ils sont donc pris dans la fiche du dossier
    /// (Metier/InformationsDossier.cs, sections DIRIGEANT et DIRIGEANT_PP) et
    /// marques « non declares » -- la vue le dit, elle ne fait pas semblant.
    /// </summary>
    public static readonly RecetteTache Dirigeant = new(
        CodeDirigeant,
        "Changer un dirigeant",
        "Evenements DSTE / DPER - modification des dirigeants",
        [
            new("DIRIGEANT_PP", "PATRONYME", "Patronyme du dirigeant",
                Declare: false, Repetable: true),
            new("DIRIGEANT_PP", "PRENOMS_ETAT_CIVIL", "Prenoms du dirigeant",
                Declare: false, Repetable: true),
            new("DIRIGEANT", "FONCTION_DIRIGEANT", "Fonction du dirigeant",
                Declare: false, Repetable: true),
            new("DIRIGEANT", "DATE_ENTREE", "Date d'entree en fonction",
                Declare: false, Repetable: true),
            // Le sortant n'existe pas toujours : une nomination sans depart
            // est un changement de dirigeant valide. Champ propose, PAS exige.
            new("DIRIGEANT", "SUPPRIM_DATE_FIN", "Date de fin de fonction du sortant",
                Declare: false, Obligatoire: false, Repetable: true),
        ],
        ["Dossier RCS", "Formalites", "Pieces jointes", "Depots d'actes"],
        [
            new("bodacc", "Publication BODACC (annonce de modification)", "B0111 / B0112 / B0113",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_BODACC_SI_B1/C1/D1", "Avis BODACC"),
            new("inpi", "Transmission INPI (registre national)", "0F",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4206",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            // La base dit NON aux deux notifications pour DSTE comme pour
            // DPER : montre, jamais exige.
            new("notif", "Notification aux etablissements", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL/SECONDAIR"),
        ],
        // UNE tache, DEUX codes RCS selon la forme de la societe.
        CodesEvenement: ["DSTE", "DPER"],
        // AJOUTE le 17/09/2026 : cette recette etait la seule a ne nommer
        // AUCUNE piece, donc le dossier entier restait a l'ecran. Memes
        // preuves que les autres, plus l'acte qui porte la nomination.
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI",
                        "Recepisse Infogreffe", "Depot d'acte"]);

    /// <summary>
    /// Les AUTRES evenements recurrents du RCS. Ils ne sont pas devines : le
    /// code de RIG v2 LIE lui-meme chaque code d'evenement a une colonne, via
    /// la fonction CFE_CodeEvtRcsLike, qui bascule une rubrique CFE sur
    /// l'ANCIENNE valeur (_ANC) de la colonne quand l'evenement est present.
    ///
    ///   Source/RCS/requetes SQL/requetes de select et conditions/_PM (M).sql
    ///     l.34  DENO  -> INSPM_DENOMINATION_USAGE_ANC
    ///     l.35  SIGL  -> INSPM_SIGLE_ANC
    ///     l.36  FORM  -> INSPM_FORME_JURIDIQUE_UTI(_2)
    ///   ... / _PP (P).sql
    ///     l.41  NOPA  -> INSPP_PATRONYME_ANC
    ///     l.43  NUSA  -> INSPP_NOM_USAGE_ANC
    ///     l.44  PRENO -> INSPP_PRENOMS_ETAT_CIVIL_ANC
    ///     l.45  PSEU  -> INSPP_PSEUDONYME_ANC
    ///     l.90  COLL  -> INSPP_CONJOINT_COLLABO
    ///
    /// LEURS EFFETS SONT LUS, PAS DEDUITS. Les canaux de propagation viennent
    /// de CODE_EVENEMENT_RCS sur RIG_AMIENS@SQL-PROD\PROD, lecture du
    /// 16/09/2026 (colonnes BODACC_SI_B1/C1/D1, CODE_INPI, INFOGREFFE,
    /// NOTIF_A_PRINCIPAL, NOTIF_A_SECONDAIR) :
    ///   SIGL  BODACC B0111/B0112/B0113  INPI 0F  Infogreffe 4011  notif sec. OUI
    ///   FORM  BODACC B0111/B0112/B0113  INPI 0F  Infogreffe 4012  notif sec. OUI
    ///   NUSA  PAS de BODACC (les 3 colonnes sont vides) INPI 0F  Infogreffe 4035
    ///                                                            notif sec. OUI
    /// Les libelles d'ecran viennent de la meme lecture
    /// (R_CODEEVENEM_LIBELLE_ECRAN). Le principal n'est JAMAIS notifie pour
    /// ces trois codes : l'effet est montre, jamais exige.
    ///
    /// NUSA n'a AUCUNE publication BODACC : c'est une valeur LUE, pas un
    /// oubli. Un changement de nom d'usage ne s'annonce pas au BODACC.
    /// </summary>
    public static readonly RecetteTache Sigle = new(
        "SIGL",
        "Modification du sigle",
        "Evenement SIGL - modification du sigle",
        // RELU EN BASE le 16/09/2026 : SIGL ne declare QUE ce champ, et il
        // est obligatoire en modification ulterieure. Exact et complet.
        [new("INSCRIT_PM", "SIGLE", "Sigle")],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("bodacc", "Publication BODACC (annonce de modification)", "B0111 / B0112 / B0113",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_BODACC_SI_B1/C1/D1", "Avis BODACC"),
            new("inpi", "Transmission INPI (registre national)", "0F",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4011",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    public static readonly RecetteTache FormeJuridique = new(
        "FORM",
        "Modification de la forme juridique",
        "Evenement FORM - modification de la forme juridique",
        // RELU EN BASE le 16/09/2026 (ULT_OP_ET_EVENEMENT_RCS, colonne
        // R_ULTOPETEVE_VALEUR = 'FORM') : la base rattache QUATRE champs a
        // cet evenement, pas un seul. Les trois autres manquaient -- la vue
        // focalisee n'ouvrait qu'une case sur quatre.
        //
        // NUANCE IMPORTANTE, lue dans la meme table : en modification
        // ULTERIEURE (R_ULTOPETEVE_MOD_ULT_OBLIG), la base n'exige AUCUN de
        // ces quatre champs -- pas meme la forme juridique. Les exiger tous
        // bloquerait des dossiers legitimes ; n'en exiger aucun laisserait
        // clore une tache ou rien n'a ete saisi. CHOIX PRODUIT ASSUME : la
        // forme juridique reste exigee (sans elle l'evenement n'a pas eu
        // lieu), les trois autres sont proposes. Ce n'est pas la base qui le
        // dit, c'est nous -- et c'est ecrit ici.
        [
            new("INSCRIT_PM", "FORME_JURIDIQUE_UTI", "Forme juridique"),
            new("INSCRIT_PM", "TEXTE_FORME", "Texte de la forme", Obligatoire: false),
            new("INSCRIT_PM", "ASSOCIE_UNIQUE", "Associe unique", Obligatoire: false),
            new("INSCRIT_PM", "BODACC_OUI", "Publier au BODACC", Obligatoire: false),
        ],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("bodacc", "Publication BODACC (annonce de modification)", "B0111 / B0112 / B0113",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_BODACC_SI_B1/C1/D1", "Avis BODACC"),
            new("inpi", "Transmission INPI (registre national)", "0F",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4012",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    public static readonly RecetteTache NomUsage = new(
        "NUSA",
        "Modification du nom d'usage",
        "Evenement NUSA - modification du nom d'usage",
        // RELU EN BASE le 16/09/2026 : NUSA declare DEUX champs, pas un.
        // NOM_USAGE est obligatoire en modification ulterieure, CIVILITE ne
        // l'est pas (R_ULTOPETEVE_MOD_ULT_OBLIG) -- il est donc propose.
        [
            new("INSCRIT_PP", "NOM_USAGE", "Nom d'usage"),
            new("INSCRIT_PP", "CIVILITE", "Civilite", Obligatoire: false),
        ],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("inpi", "Transmission INPI (registre national)", "0F",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4035",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    /// <summary>
    /// L'ETAT CIVIL de la personne physique : quatre evenements de plus, dont
    /// les champs viennent des sources RIG v2 (_PP (P).sql, fonction
    /// CFE_CodeEvtRcsLike, lignes 41, 44, 45 et 90) et dont les effets ET les
    /// libelles sont LUS dans CODE_EVENEMENT_RCS sur RIG_AMIENS@SQL-PROD\PROD
    /// (lecture du 16/09/2026).
    ///
    /// CE QUE LA LECTURE APPREND, et qu'on aurait mal devine :
    ///   - AUCUN des quatre ne publie au BODACC (les 3 colonnes B1/C1/D1 sont
    ///     vides). L'etat civil d'une personne ne s'annonce pas.
    ///   - le code INPI DIFFERE pour chacun : NOPA 10P, PSEU 15P, COLL 30P,
    ///     mais PRENO reste sur le 0F generique. Recopier un seul code aurait
    ///     transmis trois evenements sous une mauvaise reference.
    ///   - COLL est le SEUL a ne declencher AUCUNE notification (principal ET
    ///     secondaire a 0), et le seul dont le code Infogreffe n'est pas 4035.
    /// </summary>
    public static readonly RecetteTache Patronyme = new(
        "NOPA",
        "Modification du nom patronymique",
        "Evenement NOPA - modification du patronyme",
        [new("INSCRIT_PP", "PATRONYME", "Nom patronymique")],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("inpi", "Transmission INPI (registre national)", "10P",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4035",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    public static readonly RecetteTache Prenoms = new(
        "PRENO",
        "Modification du prenom d'une personne physique",
        "Evenement PRENO - modification des prenoms",
        // RELU EN BASE le 16/09/2026 : PRENO declare DEUX champs. Les
        // prenoms d'etat civil ET le prenom d'usage sont obligatoires en
        // modification ulterieure (R_ULTOPETEVE_MOD_ULT_OBLIG).
        [
            new("INSCRIT_PP", "PRENOMS_ETAT_CIVIL", "Prenoms d'etat civil"),
            new("INSCRIT_PP", "PRENOM_USAGE", "Prenom d'usage"),
        ],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("inpi", "Transmission INPI (registre national)", "0F",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4035",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    public static readonly RecetteTache Pseudonyme = new(
        "PSEU",
        "Modification du pseudonyme",
        "Evenement PSEU - modification du pseudonyme",
        // RELU EN BASE le 16/09/2026 : PSEU declare QUATRE champs. Le
        // pseudonyme est obligatoire ; le prenom pseudonymique et le surnom
        // ne le sont pas -- ils sont proposes. La 4e ligne porte la
        // 4e ligne : « #CXML / #CC_PSEUDO_PP ». ELUCIDE le 16/09/2026 (voir
        // la note « PREFIXES DU REFERENTIEL » plus bas) : un attribut prefixe
        // « # » n'est PAS un champ, c'est un TITRE de bloc ou un controle
        // d'ecran. Aucune colonne de la base ne porte ce nom. Ignoree a bon
        // droit -- ce n'est pas un oubli.
        [
            new("INSCRIT_PP", "PSEUDONYME", "Pseudonyme"),
            new("INSCRIT_PP", "PRENOM_PSEUDO", "Prenom pseudonymique", Obligatoire: false),
            new("INSCRIT_PP", "SURNOM", "Surnom", Obligatoire: false),
        ],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("inpi", "Transmission INPI (registre national)", "15P",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4035",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    public static readonly RecetteTache ConjointCollaborateur = new(
        "COLL",
        "Modification conjoint collaborateur",
        "Evenement COLL - statut du conjoint collaborateur",
        // RELU EN BASE le 16/09/2026 : COLL declare TROIS champs, et la
        // base n'en exige AUCUN en modification ulterieure. Meme arbitrage
        // que FORM : le statut de conjoint collaborateur reste exige (sans
        // lui l'evenement n'a pas eu lieu), les deux autres sont proposes.
        // C'est un choix produit, pas une lecture -- et il est ecrit ici.
        [
            new("INSCRIT_PP", "CONJOINT_COLLABO", "Conjoint collaborateur"),
            new("INSCRIT_PP", "CONJOINT_SALARIE", "Conjoint salarie", Obligatoire: false),
            new("INSCRIT_PP", "CODE_SITUATION", "Situation matrimoniale", Obligatoire: false),
        ],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("inpi", "Transmission INPI (registre national)", "30P",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4500",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            // Seul code des quatre dont la base dit NON aux DEUX notifications.
            new("notif", "Notification aux etablissements", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL/SECONDAIR"),
        ]);

    /// <summary>
    /// LES PLUS FREQUENTS AU GREFFE. Les trois recettes qui suivent ne sont
    /// pas choisies par gout : elles viennent d'un COMPTAGE sur douze mois
    /// glissants dans EVENEMENT_RCS (RIG_AMIENS@SQL-PROD\PROD, 601 792
    /// lignes, lecture du 16/09/2026) -- DISS 569 occurrences, CESM 530,
    /// RADI 797.
    ///
    /// Leurs CHAMPS sont DECLARES en base, dans ULT_OP_ET_EVENEMENT_RCS
    /// (colonnes TABLE / ATTRIBUT) : c'est le referentiel qui dit quel champ
    /// chaque evenement fait bouger, pas une supposition. Leurs EFFETS
    /// viennent de CODE_EVENEMENT_RCS, meme lecture.
    ///
    /// UN CHAMP DECLARE A ETE ECARTE : DISS rattache aussi ADRESSE_BASE
    /// .ID_ADRBS (le lieu de liquidation est une adresse complete, pas une
    /// zone de saisie) et une pseudo-entree « #SOUS_ADRESSE ». Une adresse se
    /// saisit dans le bloc adresse du dossier, pas dans une ligne de tache :
    /// la recette garde les champs SIMPLES et laisse l'adresse a son ecran.
    /// </summary>
    public static readonly RecetteTache Dissolution = new(
        "DISS",
        "Dissolution de la personne morale",
        "Evenement DISS - dissolution",
        [
            // RELU EN BASE le 16/09/2026 : DISS declare ces quatre champs
            // PLUS l'adresse de liquidation (ADRESSE_BASE.ID_ADRBS), et n'en
            // exige AUCUN en modification ulterieure. Exiger le journal
            // d'annonces bloquerait une dissolution dont la parution n'est
            // pas encore connue : seule la DATE reste exigee, le reste est
            // propose. Choix produit, ecrit ici.
            new("INSCRIT_PM", "DATE_DISSOLUTION", "Date de dissolution"),
            new("INSCRIT_PM", "JOURNAL_ANNONCES", "Journal d'annonces legales", Obligatoire: false),
            new("INSCRIT_PM", "DISSO_JAL_DATE", "Date de parution au JAL", Obligatoire: false),
            new("INSCRIT_PM", "LIEU_LIQUIDATION_CFE", "Lieu de liquidation", Obligatoire: false),
            new("ADRESSE_BASE", "ID_ADRBS", "Adresse de liquidation", Obligatoire: false),
        ],
        ["Dossier RCS", "Formalites", "Pieces jointes", "Depots d'actes"],
        [
            new("bodacc", "Publication BODACC (annonce de modification)", "B0111 / B0112 / B0113",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_BODACC_SI_B1/C1/D1", "Avis BODACC"),
            new("inpi", "Transmission INPI (registre national)", "0F",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4024",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    public static readonly RecetteTache CessationActivite = new(
        "CESM",
        "Cessation totale d'activite d'une personne morale",
        "Evenement CESM - cessation totale d'activite",
        [new("DOSSIER_RCS", "DATE_CESSATION", "Date de cessation")],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("bodacc", "Publication BODACC (annonce de modification)", "B0111 / B0112 / B0113",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_BODACC_SI_B1/C1/D1", "Avis BODACC"),
            new("inpi", "Transmission INPI (registre national)", "0F",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4026",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            // La base dit NON aux DEUX notifications pour ce code.
            new("notif", "Notification aux etablissements", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL/SECONDAIR"),
        ]);

    /// <summary>
    /// RADI vise l'EXPLOITANT, donc une personne physique : ses deux champs
    /// propres sont sur INSCRIT_PP, et le troisieme sur le dossier. Le deces
    /// et la fin d'emploi salarie ne sont pas toujours renseignes -- une
    /// radiation pour simple arret d'activite n'en a aucun : ils sont donc
    /// PROPOSES, jamais exiges, et seule la date de cessation est obligatoire.
    /// </summary>
    public static readonly RecetteTache RadiationExploitant = new(
        "RADI",
        "Radiation de l'exploitant",
        "Evenement RADI - radiation de l'exploitant",
        [
            new("DOSSIER_RCS", "DATE_CESSATION", "Date de cessation"),
            new("INSCRIT_PP", "CESS_DECES", "Cessation pour deces", Obligatoire: false),
            new("INSCRIT_PP", "DATE_CES_EMPLOI_SALARIE", "Fin d'emploi salarie",
                Obligatoire: false),
        ],
        ["Dossier RCS", "Formalites", "Pieces jointes"],
        [
            new("inpi", "Transmission INPI (registre national)", "41P",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_CODE_INPI", "Accuse INPI"),
            new("infogreffe", "Diffusion Infogreffe", "4004",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_INFOGREFFE", "Recepisse Infogreffe"),
            new("notif-secondaire", "Notification aux etablissements secondaires", "oui",
                true, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_SECONDAIR"),
            new("notif-principal", "Notification a l'etablissement principal", "non requis",
                false, "CODE_EVENEMENT_RCS.R_CODEEVENEM_NOTIF_A_PRINCIPAL"),
        ],
        // Les PIECES que la tache garde ouvertes : la piece de reference du
        // dossier, puis les trois preuves de publication. Tout le reste du
        // dossier se replie -- et se rouvre par « Afficher tout le dossier ».
        PiecesGardees: ["Extrait Kbis", "Avis BODACC", "Accuse INPI", "Recepisse Infogreffe"]);

    public static readonly RecetteTache[] Liste =
        [Deno, Dirigeant, Sigle, FormeJuridique, NomUsage,
         Patronyme, Prenoms, Pseudonyme, ConjointCollaborateur,
         Dissolution, CessationActivite, RadiationExploitant];

    /// <summary>
    /// La recette qui traite ce code RCS. Elle se retrouve par son propre
    /// code OU par l'un des evenements qu'elle couvre : depuis la 2e recette,
    /// DPER et DSTE menent tous deux a la meme tache « changer un dirigeant ».
    /// </summary>
    public static RecetteTache? ParCode(string code)
        => Liste.FirstOrDefault(r => r.Code == code)
           ?? Liste.FirstOrDefault(r => r.Evenements.Contains(code));
}

/// <summary>
/// Une tache TERMINEE : qui l'a close et quand. Conservee dans accueil.json
/// pour que l'alerte correspondante ne revienne pas apres coup -- une file
/// d'alertes deja traitees detruit la confiance en deux semaines.
/// </summary>
public sealed class TacheClose
{
    public string Code { get; set; } = "";
    public string Affaire { get; set; } = "";
    public string Auteur { get; set; } = "";
    public DateTime Quand { get; set; }

    /// <summary>La cle d'une tache : sa recette SUR une affaire precise.</summary>
    public static string CleDe(string code, string affaire) => code + ":" + affaire;
}
