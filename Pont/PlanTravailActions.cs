namespace RigV3Desktop.Pont;

// ============================================================
// LA BARRE D'ACTIONS DU DOSSIER, cote session (plan « Vue du dossier = coeur des
// actions », lot 1, 20/11/2026). Le catalogue est Metier/ActionsDossier.cs ; la
// barre, web/src/plan/ActionsDossier.tsx. Un seul geste, « actionDossier » :
//   - une action LOCALE se joue dans la page de l'affaire : le Kbis a jour dans la
//     colonne du document, son impression, l'impression de l'arborescence, l'envoi
//     du dossier entier par mail (les modales existantes) ;
//   - une action d'ECRAN ouvre l'ecran dans son onglet « pour ce dossier »
//     (OuvrirProcDepuisAffaire, PlanTravail.cs) : l'onglet retient l'affaire, l'ecran
//     en recoit le contexte (EcranProcVue.Dossier) et montre la puce « ← Dossier »,
//     qui y ramene (geste « retourDossier ») ; fermer l'onglet y ramene aussi ;
//   - un COURRIER (lot 9) : les modeles de courrier divers qui valent pour la nature de
//     l'affaire remplacent la famille « courriers » de la barre ; en choisir un produit le
//     courrier rempli avec l'affaire et le montre dans la colonne du document, ou les
//     boutons Imprimer et Envoyer par mail du document valent pour lui.
// Le pre-remplissage de chaque ecran par ce contexte est le lot 2 du plan.
// ============================================================
public sealed partial class SessionPlanTravail
{
    /// <summary>Ce que porte la modale d'impression quand ce n'est pas la piece ouverte :
    /// l'arborescence du dossier. Nul = la piece ouverte (OuvrirImpression le remet a nul).</summary>
    private string? ImpressionDe;

    /// <summary>Ce que porte la modale d'envoi par mail quand ce n'est pas la piece ouverte :
    /// le dossier entier. Nul = la piece ouverte (OuvrirEnvoiMail le remet a nul).</summary>
    private string? EnvoiDe;

    /// <summary>Le dernier courrier produit depuis la barre : un document de la page, hors du
    /// classeur. Tant qu'il est ouvert, la colonne du document ne lui accole aucune valeur de
    /// formulaire (VueApercu) : un courrier n'est pas une piece d'une etape de saisie.</summary>
    private NoeudGed? CourrierRedige;

    /// <summary>Vrai pour le courrier produit : il n'est la piece d'aucune etape de saisie. Son
    /// libelle le rattacherait a une etape au hasard (EtapeDuNoeud : « mandataire » = CONTROLE) :
    /// ni valeur de formulaire (apercu, fenetre detachee : ValeursDocument), ni « Editer ».</summary>
    // fix-ok: PlanTravailCheck rouge sur main 71f90e9 -- « Editer » sur le courrier ouvrait CONTROLE.
    private bool HorsSaisie(NoeudGed n) => ReferenceEquals(n, CourrierRedige);

    /// <summary>Les actions que la barre montre sur cette affaire : celles de son type,
    /// moins celles qui n'y auraient pas d'effet ou pas de droit ; la famille des courriers
    /// cede sa place aux modeles qui valent pour la nature de l'affaire -- aucun, rien.</summary>
    private IEnumerable<ActionDossier> ActionsDisponibles(Dossier d)
    {
        var actions = ActionsDossier.Pour(d.Type).Where(a => Disponible(a, d)).ToList();
        if (!actions.Any(a => a.Code == ActionsDossier.Courriers)) return actions;
        var courriers = ActionsDossier.DesCourriers(CourriersDivers.Pour(EcranRechAffaires.NatureDe(d.Numero)),
                                                    actions.Where(a => a.Code != ActionsDossier.Courriers).Select(a => a.Touche));
        return actions.SelectMany(a => a.Code == ActionsDossier.Courriers ? courriers : [a]);
    }

    private bool Disponible(ActionDossier a, Dossier d)
    {
        // Un ecran : seulement dans la fenetre principale -- celle d'un onglet SORTI n'a
        // ni barre d'onglets ni accueil ou l'ouvrir --, et dans le perimetre du login.
        if (a.Ecran is { } ecran) return EcranPourLeDossier(ecran);
        return a.Code switch
        {
            // Une affaire purement judiciaire n'a pas de Kbis ; un login sans le RCS ne le voit pas.
            ActionsDossier.Kbis or ActionsDossier.ImprimerKbis => KbisVisible() is not null,
            ActionsDossier.CopierSiren => d.Siren.Length > 0,
            _ => true
        };
    }

    /// <summary>Un ecran peut s'ouvrir « pour ce dossier » : dans la fenetre principale et dans
    /// le perimetre du login. La barre et la colonne de suivi (PlanTravailSuivi.cs) lisent cette regle.</summary>
    private bool EcranPourLeDossier(string ecran) => !ModeFenetreVue && PerimetreProfil.Visible(ecran, Domaines);

    /// <summary>Le Kbis a jour, s'il est dans l'arbre que ce login a le droit de voir.</summary>
    private NoeudGed? KbisVisible()
        => ArbreVisible().FirstOrDefault(n => n.EstPiece && n.Libelle == ArbreDemo.LibelleKbisAJour);

    /// <summary>La barre : un groupe par verbe, dans l'ordre du catalogue ; un verbe sans
    /// action disponible n'apparait pas.</summary>
    private VerbeActionsVue[] VueActions(Dossier d)
    {
        var disponibles = ActionsDisponibles(d).ToList();
        return ActionsDossier.Verbes
            .Select(verbe => new VerbeActionsVue(verbe, disponibles
                .Where(a => a.Verbe == verbe)
                .Select(a => new ActionDossierVue(a.Code, a.Libelle, a.Touche.ToString(), a.Detail,
                                                  !a.EstLocale, TexteACopier(a, d)))
                .ToArray()))
            .Where(g => g.Actions.Length > 0)
            .ToArray();
    }

    /// <summary>Le texte qu'une action de copie met dans le presse-papiers ; nul pour les autres.</summary>
    private static string? TexteACopier(ActionDossier a, Dossier d) => a.Code switch
    {
        ActionsDossier.CopierNumero => d.Numero,
        ActionsDossier.CopierSiren => d.Siren,
        _ => null
    };

    /// <summary>Le geste « actionDossier » : joue l'action de ce code sur l'affaire ouverte.</summary>
    private void AgirSurDossier(string code)
    {
        // Jouee depuis la palette Ctrl K (« Sur ce dossier », lot 5) : elle se ferme, comme
        // apres « paletteAller », pour laisser voir ce que l'action ouvre.
        PaletteOuverte = false;
        // L'affaire a ete fermee entre le clic et son traitement : il n'y a plus rien ou agir.
        if (DossierOuvert is not { } d) return;
        var action = ActionsDisponibles(d).FirstOrDefault(a => a.Code == code)
            ?? throw new InvalidOperationException($"Action « {code} » indisponible sur l'affaire {d.Numero}.");
        switch (action.Code)
        {
            case ActionsDossier.Kbis:
                AfficherKbis();
                break;
            case ActionsDossier.ImprimerKbis:
                AfficherKbis();
                OuvrirImpression();
                break;
            case ActionsDossier.ImprimerArborescence:
                OuvrirImpression();
                ImpressionDe = $"Arborescence du dossier {d.Numero}";
                break;
            case ActionsDossier.EnvoyerDossier:
                OuvrirEnvoiMail();
                var pieces = NbPiecesVisibles();
                EnvoiDe = $"Dossier entier {d.Numero} ({pieces} {(pieces == 1 ? "piece" : "pieces")})";
                break;
            case ActionsDossier.CopierNumero or ActionsDossier.CopierSiren:
                // Le presse-papiers est celui de la page : elle copie sans remonter le geste.
                throw new InvalidOperationException($"L'action « {code} » se joue dans la page, pas dans la session.");
            case var courrier when courrier.StartsWith(ActionsDossier.PrefixeCourrier, StringComparison.Ordinal):
                // Disponible, donc un modele actif de la nature de l'affaire (ActionsDisponibles).
                ProduireCourrier(d, CourriersDivers.DeCode(courrier[ActionsDossier.PrefixeCourrier.Length..])!);
                break;
            default:
                var proc = Groupes.SelectMany(g => g.Procs).FirstOrDefault(p => p.Code == action.Ecran)
                    ?? throw new InvalidOperationException($"L'ecran {action.Ecran} de l'action « {code} » n'est pas au catalogue.");
                OuvrirProcDepuisAffaire(proc);
                break;
        }
    }

    /// <summary>« Kbis a jour » : la piece choisie dans l'arbre (sa branche depliee, comme
    /// depuis l'historique) et montree dans la colonne du document.</summary>
    private void AfficherKbis()
    {
        if (KbisVisible() is not { } kbis) return;
        OuvrirDepuisHistorique(kbis);
        // En mode tache, l'arbre replie sur la recette ne la liste pas : le document
        // s'ouvre quand meme -- le droit de le voir est deja verifie (KbisVisible).
        if (!GuideVisible || !ReferenceEquals(ApercuNoeud, kbis)) OuvrirApercu(kbis);
    }

    /// <summary>
    /// Ecrit un courrier divers pour l'affaire : rempli avec elle (n°, denomination, adresse
    /// de sa fiche sommaire), adresse au destinataire que le modele demande, sous l'en-tete
    /// du greffe du dossier (la regle du Kbis, Pont/ApercuKbis.cs), et montre dans la colonne
    /// du document. L'etape de saisie et la piece choisie dans le classeur ne bougent pas.
    /// </summary>
    private void ProduireCourrier(Dossier d, ModeleCourrier modele)
    {
        var sommaire = SommaireDu(d);
        var texte = CourriersDivers.Rediger(modele,
            new AffaireCourrier(d.Numero, sommaire.Designation, sommaire.Adresse),
            DestinataireDu(modele, sommaire),
            Greffes.Trouver(LireSaisie("SIEGE", "ETBLS_CODE_GREFFE")), DateTime.Today);
        // La nature « Courrier » du bouton Nouveau : son dessin et son extension.
        CourrierRedige = AccesGed.NouveauSelonNature("courrier", DateTime.Today.ToString("dd/MM/yyyy")) with
        {
            Libelle = CourriersDivers.Titre(modele, d.Numero),
            Origine = CourriersDivers.Origine,
            Apercu = texte,
            Pages = DocumentPartage.PagesDuTexte(texte)
        };
        ApercuNoeud = CourrierRedige;
        ApercuMessage = "";
        ApercuPage = 1;
        ImpressionOuverte = false;
        GuideVisible = true;
    }

    /// <summary>Le destinataire qu'un modele demande : la partie (la fiche sommaire de
    /// l'affaire), ou le mandataire judiciaire du tableau des editions de l'affaire
    /// (ArbreDemo.Editions) ; null si le modele n'en demande pas ou si le dossier ne le connait pas.</summary>
    private AdresseCourrier? DestinataireDu(ModeleCourrier modele, Sommaire sommaire) => modele.Destinataire switch
    {
        DestinataireCourrier.Partie => new AdresseCourrier(sommaire.Designation, sommaire.Adresse),
        DestinataireCourrier.Mandataire => Editions
            .Select(l => l.Detail)
            .FirstOrDefault(x => x is not null && x.AdressePostale.StartsWith("Mandataire judiciaire", StringComparison.Ordinal))
            is { } m ? new AdresseCourrier(m.Designation, m.AdressePostale) : null,
        _ => null
    };

    /// <summary>Le contexte de l'affaire d'ou un ecran a ete ouvert, pour cet ecran.</summary>
    private ContexteDossierVue? ContexteDeLEcran(string code)
    {
        if (!AffaireDesEcrans.TryGetValue(code, out var d)) return null;
        var nom = SommaireDu(d).Designation;
        return new ContexteDossierVue(d.Numero, d.Siren, d.Objet, d.Type, nom.Length > 0 ? nom : d.Objet);
    }

    /// <summary>La puce « ← Dossier » de l'ecran montre : retour a son affaire.</summary>
    private void RetournerAuDossierDeLEcran()
    {
        if (ProcOuverte is { } p && AffaireDesEcrans.TryGetValue(p.Code, out var d))
            RevenirAuDossier(d, rouvrir: true);
    }

    /// <summary>
    /// Ramene a l'affaire d'un ecran : son onglet, retrouve tel qu'on l'a laisse. S'il a ete
    /// ferme entre-temps, la puce la rouvre (<paramref name="rouvrir"/>) ; la fermeture de
    /// l'ecran, elle, ne rouvre pas une affaire qu'on a fermee. Rend faux si rien n'est fait.
    /// </summary>
    private bool RevenirAuDossier(Dossier d, bool rouvrir)
    {
        var onglet = Onglets.Onglets.FirstOrDefault(o => o.Code == CodeOngletAffaire(d.Numero));
        if (onglet is null)
        {
            if (!rouvrir) return false;
            OuvrirParNumero(d.Numero);
            return true;
        }
        // Sortie dans sa fenetre : celle-ci passe devant, la principale montre l'onglet voisin.
        if (onglet.Detache) SuivreOngletActif();
        ActiverOnglet(onglet);
        return true;
    }
}
