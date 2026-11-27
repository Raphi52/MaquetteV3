using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurCodesChefsIp;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Codes chefs IP » (PROC_PARAM_IP) : AJOUTER un code, le RETIRER, le MONTER ou le DESCENDRE d'un rang dans la liste ordonnee du
/// reglage IP_CODE_CHEF. Chaque geste reecrit la liste ENTIERE dans le reglage, comme la V2 a l'enregistrement. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurCodesChefsIp.cs. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026.
///
/// OU IL ECRIT : le reglage est dans soc.Reglage (la reprise de PARAMETRAGE, RepriseReglages.cs:57), pas dans v2.* : l'ecriture
/// passe par geste.ModifierReglageAsync, la meme que la route des reglages (Serveur/Socle/Greffes/EcritureReglage.cs) -- droit de
/// modifier le greffe (ServiceDroits.PeutModifierLeGreffe), reglage existant, auteur = le nom de l'agent, date. Decision du chef
/// (msg-cab1a60f, 27/11/2026) : c'est l'equivalent exact de Parametrage.Sauve de la V2. ECART NOMME : la V2 ouvre l'ecran sous la
/// fonction de sa procedure, sans le droit « modifier le greffe » (ServiceDroits.cs:60-70) ; la V3 exige ce droit, celui de
/// l'ecriture des reglages.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_PARAM_IP\ETP_PARAM_IP, controle RigListViewUpDown
/// (D:\RigApplication\Source\Outils\DLL\RigControls\RigListViewUpDown.cs).
///   - LA LISTE (ETP_PARAM_IP.DoInitGraphique, l. 27-40) : le reglage coupe sur « ; », chaque code resolu dans CODE_CHEF ; un code
///     introuvable n'est pas dans la liste (l. 34). Le geste repart de cette liste RESOLUE : un code introuvable disparait a la
///     premiere ecriture, comme a la sauvegarde suivante de la V2 (LecteurCodesChefsIp.ResolusAsync, la meme lecture que l'ecran).
///   - AJOUTER (OPE_PARAM_IP.ult_btnAdd_ButtonClick, l. 46-60) : un code de la liste deroulante, qui ne propose que
///     « WHERE R_CODECHEF_VALEUR like 'IP%' » (l. 43) ; seulement s'il n'est pas deja dans la liste (GetItem, l. 49) et que CODE_CHEF
///     le trouve (l. 50-52) ; en FIN de liste (AddItem). Le libelle vient de CODE_CHEF (l. 54), pas de la saisie ; le rang aussi.
///   - RETIRER : le bouton de retrait de la liste (new RigListViewUpDown(true), OPE_PARAM_IP.cs:24 ; RemoveItem,
///     RigListViewUpDown.cs:152-160).
///   - MONTER / DESCENDRE (RigListViewUpDown._MoveItem, l. 120-149) : un rang vers le haut (index - 1) ou vers le bas (index + 1) ;
///     hors des bornes, rien ne bouge (l. 135).
///   - ENREGISTRER (ETP_PARAM_IP.DoVerifier, l. 43-50) : une liste vide est refusee -- titre « Liste sans code chef », message
///     « La liste ne contient aucun code chef », complement « il en faut au moins un » ; le refus rend le message. DoSauveAfterOperation
///     (l. 52-68) : les codes joints par « ; », sans « ; » final (l. 60-63), dans PARAM_VALEUR d'IP_CODE_CHEF.
/// MESSAGES SANS SOURCE V2 (nommes) : la V2 n'a pas de message quand un code est deja dans la liste ou hors de CODE_CHEF (le clic ne
/// fait rien), ni quand le premier code monte ou le dernier descend (le bouton ne fait rien) : la V3 le dit, car un geste qui n'ecrit
/// rien est un refus (EcrivainEcran.cs, point 5). Champ d'ajout vide : la liste deroulante de la V2 n'a pas de valeur vide. Le reglage
/// absent : la V2 leve (Parametrage.GetParametrage rend null, ETP_PARAM_IP.cs:65-66). Une liste de plus de 1 000 caracteres : la
/// colonne soc.Reglage.Valeur en tient 1 000 (SOC-003_reglages.sql:18) ; la V3 refuse au lieu de couper.
/// NON REPRIS (nomme) : les boutons « tout en haut » / « tout en bas » de RigListViewUpDown (Top, Bottom, l. 118-119), que l'ordre du
/// lot ne demande pas ; « Modifier » une ligne n'existe pas en V2 (la grille le propose, il reste « Demonstration »).
/// TRACE : la ligne que rend l'ecriture du reglage -- famille « reglage », « le reglage IP_CODE_CHEF », « a modifie », valeur avant /
/// apres.
/// </summary>
internal sealed class EcrivainCodesChefsIp : IEcrivainEcran
{
    public string Procedure => "PROC_PARAM_IP";
    public string Ecran => "Codes chefs IP";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Ajouter, TypeGeste.Supprimer, TypeGeste.Agir];

    internal const string Monter = "Monter", Descendre = "Descendre";

    /// <summary>ETP_PARAM_IP.DoVerifier (l. 47), le message au mot pres.</summary>
    internal const string ListeVide = "La liste ne contient aucun code chef";

    internal const string AjoutVide = "Choisissez le code chef à ajouter.";
    internal const string ReglageAbsent = "Le réglage IP_CODE_CHEF n'existe pas dans la base du greffe : RIG v2 ne le crée pas.";
    internal static string NonPropose(string code) => $"Le code chef « {code} » n'est pas proposé : RIG v2 ne propose que les codes chefs commençant par IP.";
    internal static string DejaDansLaListe(string code) => $"Le code chef « {code} » est déjà dans la liste.";
    internal static string HorsDeLaListe(string code) => $"Le code chef « {code} » n'est pas dans la liste.";
    internal static string DejaEnTete(string code) => $"Le code chef « {code} » est déjà en tête de liste.";
    internal static string DejaEnFin(string code) => $"Le code chef « {code} » est déjà en fin de liste.";
    internal const string TropLong = "La liste dépasse les 1 000 caractères du réglage IP_CODE_CHEF : retirez un code avant d'en ajouter un.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        Task<SqlDataReader> lire(string sql, SqlParameter[] p) => geste.LireAsync(sql, ct, p);
        // Le reglage verrouille jusqu'a la fin du geste : deux gestes au meme instant reecrivent la liste l'un apres l'autre.
        await using (var verrou = await geste.LireAsync("SELECT Code FROM soc.Reglage WITH (UPDLOCK) WHERE Code = @code", ct,
                         new SqlParameter("@code", Reglage)))
            if (!await verrou.ReadAsync(ct)) return IssueGeste.Refuser(ReglageAbsent);
        var liste = (await ResolusAsync(lire, (await CodesRangesAsync(lire, ct))!, ct)).Select(l => l.Code).ToList();
        int Rang(string code) => liste.FindIndex(c => string.Equals(c, code.Trim(), StringComparison.OrdinalIgnoreCase));

        string message, vise;
        switch (geste.Demande.Type)
        {
            case TypeGeste.Ajouter:
                var saisi = geste.Demande.Valeurs.TryGetValue(ColCode, out var v) ? v.Trim() : "";
                if (saisi.Length == 0) return IssueGeste.Refuser(AjoutVide);
                if (await ProposeAsync(geste, saisi, ct) is not { } code) return IssueGeste.Refuser(NonPropose(saisi));
                if (Rang(code) >= 0) return IssueGeste.Refuser(DejaDansLaListe(code));
                liste.Add(code);
                (vise, message) = (code, $"Code chef {code} ajouté en fin de liste (rang {liste.Count}).");
                break;
            case TypeGeste.Supprimer:
                vise = geste.Demande.Ids.FirstOrDefault() ?? "";
                var r = Rang(vise);
                if (r < 0) return IssueGeste.Refuser(HorsDeLaListe(vise));
                vise = liste[r];
                liste.RemoveAt(r);
                if (liste.Count == 0) return IssueGeste.Refuser(ListeVide);
                message = $"Code chef {vise} retiré de la liste.";
                break;
            case TypeGeste.Agir when geste.Demande.Nom is Monter or Descendre:
                vise = geste.Demande.Ids.FirstOrDefault() ?? "";
                var i = Rang(vise);
                if (i < 0) return IssueGeste.Refuser(HorsDeLaListe(vise));
                vise = liste[i];
                var cible = geste.Demande.Nom == Monter ? i - 1 : i + 1;
                // _MoveItem, l. 135 : hors des bornes, rien ne bouge.
                if (cible < 0) return IssueGeste.Refuser(DejaEnTete(vise));
                if (cible >= liste.Count) return IssueGeste.Refuser(DejaEnFin(vise));
                liste.RemoveAt(i);
                liste.Insert(cible, vise);
                message = $"Code chef {vise} {(cible < i ? "monté" : "descendu")} au rang {(cible + 1).ToString(CultureInfo.InvariantCulture)}.";
                break;
            default:
                return IssueGeste.Refuser($"Geste « {geste.Demande.Nom ?? geste.Demande.Type.ToString()} » inconnu de RIG v2 sur les codes chefs IP.");
        }

        // DoSauveAfterOperation (l. 55-63) : joints par « ; », sans « ; » final.
        var valeur = string.Join(";", liste);
        if (valeur.Length > 1000) return IssueGeste.Refuser(TropLong);
        return await geste.ModifierReglageAsync(Reglage, valeur, ct) switch
        {
            ReglageEcrit.Modifie ecrit => IssueGeste.Ecrit(message, [vise], ecrit.Trace),
            ReglageEcrit.SansLeDroit refus => IssueGeste.Refuser(refus.Motif),
            _ => IssueGeste.Refuser(ReglageAbsent),
        };
    }

    /// <summary>Le code tel que CODE_CHEF l'ecrit, s'il est dans la liste deroulante de la V2 (« like 'IP%' », OPE_PARAM_IP.cs:43) ;
    /// null sinon.</summary>
    private static async Task<string?> ProposeAsync(GesteEnCours geste, string saisi, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(
            "SELECT R_CODECHEF_VALEUR FROM v2.CODE_CHEF WHERE R_CODECHEF_VALEUR = @c AND R_CODECHEF_VALEUR LIKE 'IP%'", ct, new SqlParameter("@c", saisi));
        return await r.ReadAsync(ct) ? r.GetString(0).Trim() : null;
    }
}
