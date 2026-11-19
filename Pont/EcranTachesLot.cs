using System.Text.Json;

namespace RigV3Desktop.Pont;

/// <summary>Une tache en attente (colonne de gauche) : une recette et le nombre de ses dossiers.</summary>
public sealed record LotVue(string Code, string Titre, int Dossiers, bool ARepetition, bool Actif);

/// <summary>Une colonne de saisie : le libelle du champ, et la colonne REELLE de la base en infobulle.</summary>
public sealed record ChampLotVue(string Libelle, string Colonne);

/// <summary>La recette choisie, telle que l'en-tete et le tableau l'affichent.</summary>
/// <param name="Evenements">Les codes evenement, joints par « / ».</param>
/// <param name="LibelleLigne">« dirigeant » ou « ligne », en minuscules (refus de la saisie en lot).</param>
public sealed record RecetteLotVue(string Code, string Titre, string Evenements, bool ARepetition,
                                   string LibelleLigne, ChampLotVue[] Champs, int Effets);

/// <summary>Un dossier du lot : ses valeurs saisies, et ce qui reste a faire avant de terminer.</summary>
public sealed record DossierLotVue(string Dossier, string Nom, string[] Valeurs, int Restants,
                                   int EffetsRestants, bool Pret);

/// <summary>Ce que l'ecran affiche.</summary>
/// <param name="Recette">null tant qu'aucune tache n'est choisie.</param>
public sealed record VueTachesLot(string Session, LotVue[] Lots, RecetteLotVue? Recette,
                                  DossierLotVue[] Dossiers, string DernierMessage);

/// <summary>
/// Pont de l'ecran PROC_TACHES_LOT (web/src/ecrans/EcranTachesLot.tsx) : la MEME
/// recette appliquee a PLUSIEURS affaires.
///
/// Ecran A SESSION, ouverte par la COQUILLE et non par React : l'ecran ecrit
/// dans l'etat VIVANT de l'accueil (AccueilEtat : trace des modifications,
/// publications, clotures), un objet qui ne traverse pas vers JavaScript.
/// Components/Ecrans/EcranTachesLot.razor ouvre donc la session avec cet etat
/// (<see cref="Ouvrir"/>), passe son identifiant a React, lui transmet ses
/// parametres a chaque rendu (<see cref="Parametrer"/>) et la ferme quand
/// l'ecran se ferme. React lit la vue et remonte les gestes par les deux
/// methodes [JSInvokable].
/// </summary>
public static class EcranTachesLot
{
    internal static readonly SessionsEcran<SessionTachesLot> Sessions = new();

    /// <summary>Appele par la coquille : une session sur l'etat de l'accueil.</summary>
    public static string Ouvrir(AccueilEtat etat, string utilisateur, string codeRecette)
        => Sessions.Ouvrir(new SessionTachesLot(etat, utilisateur, codeRecette));

    /// <summary>
    /// Appele par la coquille a chaque rendu de l'accueil. Rend vrai si la
    /// recette affichee a change : la coquille le signale alors a React.
    /// </summary>
    public static bool Parametrer(string session, AccueilEtat etat, string utilisateur, string codeRecette)
        => Sessions.Lire(session).Parametrer(etat, utilisateur, codeRecette);

    [JSInvokable("EcranTachesLot.Vue")]
    public static VueTachesLot Vue(string session) => Sessions.Lire(session).Vue(session);

    [JSInvokable("EcranTachesLot.Agir")]
    public static VueTachesLot Agir(string session, string geste, JsonElement valeur)
    {
        var s = Sessions.Lire(session);
        s.Agir(geste, valeur);
        return s.Vue(session);
    }

    /// <summary>Appele par la coquille quand l'ecran se ferme.</summary>
    public static void Fermer(string session) => Sessions.Fermer(session);
}

/// <summary>
/// L'etat de l'ecran de traitement en lot : le @code d'EcranTachesLot.razor,
/// deplace TEL QUEL (memes lots, meme choix d'ouverture, meme cloture PAR
/// DOSSIER). Metier/ et AccueilEtat ne sont pas modifies.
/// </summary>
public sealed class SessionTachesLot
{
    /// <summary>L'etat de l'accueil, repris de la coquille a chaque rendu (Home peut le recharger).</summary>
    private AccueilEtat Etat;

    /// <summary>Qui traite : son nom part dans la trace et dans la cloture.</summary>
    private string Utilisateur;

    /// <summary>
    /// La recette a ouvrir d'office, quand on vient d'une alerte de lot
    /// (« 2 dossiers : renommer la societe »). Vide = choix automatique.
    /// </summary>
    private string CodeRecette;

    private RecetteTache? RecetteChoisie;
    private string DernierMessage = "";

    private sealed record DossierDuLot(string Dossier, string Nom, DateTime Signale);

    internal SessionTachesLot(AccueilEtat etat, string utilisateur, string codeRecette)
    {
        Etat = etat;
        Utilisateur = utilisateur;
        CodeRecette = codeRecette;
        // OnInitialized du .razor.
        Poser();
    }

    /// <summary>OnParametersSet du .razor : rouvrir l'ecran sur une AUTRE recette doit changer la vue.</summary>
    internal bool Parametrer(AccueilEtat etat, string utilisateur, string codeRecette)
    {
        Etat = etat;
        Utilisateur = utilisateur;
        CodeRecette = codeRecette;
        var avant = RecetteChoisie;
        Poser();
        return !ReferenceEquals(avant, RecetteChoisie);
    }

    internal VueTachesLot Vue(string session)
    {
        var lots = Lots;
        return new VueTachesLot(
            session,
            lots.Select(l => new LotVue(l.Recette.Code, l.Recette.Titre, l.Dossiers.Length,
                                        l.Recette.ARepetition, ReferenceEquals(l.Recette, RecetteChoisie)))
                .ToArray(),
            RecetteChoisie is null
                ? null
                : new RecetteLotVue(
                    RecetteChoisie.Code, RecetteChoisie.Titre,
                    string.Join(" / ", RecetteChoisie.Evenements),
                    RecetteChoisie.ARepetition,
                    RecetteChoisie.LibelleLigne.ToLowerInvariant(),
                    // L'infobulle montre la colonne REELLE de la base (prefixe
                    // compris), pas le nom du referentiel : c'est celle-la qu'il
                    // faudra ecrire au branchement.
                    RecetteChoisie.ChampsObligatoires
                        .Select(c => new ChampLotVue(c.Libelle, ColonnesRig.Resoudre(c).ToString()))
                        .ToArray(),
                    RecetteChoisie.EffetsObligatoires.Length),
            DossiersDuLot.Select(d =>
            {
                var restants = Restants(d.Dossier);
                var effets = EffetsRestants(d.Dossier);
                return new DossierLotVue(
                    d.Dossier, d.Nom,
                    (RecetteChoisie?.ChampsObligatoires ?? [])
                        .Select(c => Etat.DerniereValeur(d.Dossier, RecetteTache.Cle(c)) ?? "")
                        .ToArray(),
                    restants, effets, restants == 0 && effets == 0);
            }).ToArray(),
            DernierMessage);
    }

    internal void Agir(string geste, JsonElement valeur)
    {
        switch (geste)
        {
            case "Choisir":
                var code = Texte(valeur);
                Choisir(Lots.First(l => l.Recette.Code == code).Recette);
                break;
            case "Ecrire":
            {
                var d = DossierNomme(Texte(valeur, "dossier"));
                var c = RecetteChoisie!.ChampsObligatoires[valeur.GetProperty("champ").GetInt32()];
                if (d is not null) Ecrire(d, c, RecetteTache.Cle(c), Texte(valeur, "valeur"));
                break;
            }
            case "ToutPropager": ToutPropager(Texte(valeur)); break;
            case "Terminer":
            {
                var d = DossierNomme(Texte(valeur));
                if (d is not null) Terminer(d);
                break;
            }
            default: throw new ArgumentException($"Geste inconnu de l'ecran de traitement en lot : {geste}.", nameof(geste));
        }
    }

    private DossierDuLot? DossierNomme(string dossier) => DossiersDuLot.FirstOrDefault(d => d.Dossier == dossier);

    private static string Texte(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
    private static string Texte(JsonElement v, string propriete) => Texte(v.GetProperty(propriete));

    /// <summary>
    /// Les taches en attente, groupees par recette. Source : les evenements
    /// attendus du jeu de demonstration, MOINS ceux dont la tache est deja
    /// close -- exactement la meme regle que la file d'alertes.
    /// </summary>
    private (RecetteTache Recette, DossierDuLot[] Dossiers)[] Lots
        => JeuDemo.EvenementsAttendus
            .Select(e => (Evt: e, Recette: RecettesTaches.ParCode(e.Code)))
            .Where(x => x.Recette is not null)
            .Where(x => !Etat.TacheFaite(x.Recette!.Code, x.Evt.Dossier))
            .GroupBy(x => x.Recette!)
            .Select(g => (g.Key, g.Select(x => Ligne(x.Evt))
                                  .Where(d => d is not null).Select(d => d!)
                                  .OrderBy(d => d.Signale).ToArray()))
            .Where(x => x.Item2.Length > 0)
            .ToArray();

    private DossierDuLot[] DossiersDuLot
        => RecetteChoisie is null
            ? []
            : Lots.FirstOrDefault(l => ReferenceEquals(l.Recette, RecetteChoisie)).Dossiers ?? [];

    private static DossierDuLot? Ligne(EvenementAttendu e)
    {
        var demande = JeuDemo.Demandes.FirstOrDefault(d => d.Dossier == e.Dossier);
        return demande is null ? null : new DossierDuLot(e.Dossier, demande.Nom, e.Signale);
    }

    /// <summary>
    /// A l'ouverture : la recette DEMANDEE si l'alerte en nomme une, sinon
    /// le lot le PLUS GROS parmi ceux qui se traitent en tableau. Arriver
    /// sur un ecran vide obligeait a un clic pour ne rien apprendre.
    /// </summary>
    private void Poser()
    {
        if (CodeRecette.Length > 0
            && Lots.FirstOrDefault(l => l.Recette.Code == CodeRecette) is { Recette: not null } demande)
        {
            RecetteChoisie = demande.Recette;
            return;
        }
        RecetteChoisie ??= Lots.Where(l => !l.Recette.ARepetition)
                               .OrderByDescending(l => l.Dossiers.Length)
                               .Select(l => l.Recette)
                               .FirstOrDefault();
    }

    private void Choisir(RecetteTache r)
    {
        RecetteChoisie = r;
        DernierMessage = "";
    }

    /// <summary>Champs exiges encore vides sur CE dossier.</summary>
    private int Restants(string dossier)
        => RecetteChoisie?.Manquants(cle => Etat.DerniereValeur(dossier, cle)).Length ?? 0;

    /// <summary>Publications encore a traiter sur CE dossier.</summary>
    private int EffetsRestants(string dossier)
        => RecetteChoisie?.EffetsRestants(
               canal => Etat.EffetTraite(RecetteChoisie.Code, dossier, canal)).Length ?? 0;

    /// <summary>
    /// Ecrire dans le tableau note la modification DANS LE DOSSIER concerne :
    /// la trace reste par affaire, le lot n'est qu'une facon de saisir.
    /// </summary>
    private void Ecrire(DossierDuLot d, ChampRecette c, string cle, string apres)
    {
        var avant = Etat.DerniereValeur(d.Dossier, cle) ?? string.Empty;
        if (avant == apres) return;
        Etat.NoterModification(d.Dossier, new ModificationChamp(
            DateTime.Now, Utilisateur, $"Traitement en lot > {RecetteChoisie!.Titre}",
            c.Libelle, cle, avant, apres));
    }

    private void ToutPropager(string dossier)
    {
        if (RecetteChoisie is null) return;
        foreach (var e in RecetteChoisie.EffetsObligatoires)
        {
            if (!Etat.EffetTraite(RecetteChoisie.Code, dossier, e.Canal))
                Etat.BasculerEffet(RecetteChoisie.Code, dossier, e.Canal);
        }
        Etat.Journaliser(dossier,
            $"Tache {RecetteChoisie.Code} (lot) : publications marquees traitees par {Utilisateur}");
    }

    /// <summary>
    /// Cloture PAR DOSSIER. Il n'y a volontairement PAS de « tout terminer » :
    /// un bouton global fermerait aussi les dossiers incomplets, et c'est
    /// precisement ce que le controle de completude sert a empecher.
    /// </summary>
    private void Terminer(DossierDuLot d)
    {
        if (RecetteChoisie is null) return;
        if (Restants(d.Dossier) > 0 || EffetsRestants(d.Dossier) > 0) return;

        var valeurs = string.Join(", ", RecetteChoisie.ChampsObligatoires
            .Select(c => $"{c.Libelle} = {Etat.DerniereValeur(d.Dossier, RecetteTache.Cle(c))}"));
        Etat.CloreTache(RecetteChoisie.Code, d.Dossier, Utilisateur);
        Etat.Journaliser(d.Dossier,
            $"Tache {RecetteChoisie.Code} terminee en lot par {Utilisateur} : {valeurs}");
        DernierMessage = $"{d.Dossier} termine. Son alerte ne reviendra plus.";
    }
}
