using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>La fiche d'un client, aux champs de l'ecran (web/src/ecrans/EcranFicheClient.tsx, FicheCli) ; provision en centimes.</summary>
public sealed record FicheClientVue(string Abrege, string Nom, string Compte, string Siren, string Adresse, string Type, string EtatCompte, string Groupe,
                                    string Naissance, string NumPc, string Mail, string TelPrincipal, string TelPortable, string ModePaiement,
                                    long Provision, bool ReleveFacture, string FactuPremiere, string FactuDerniere, string EncaissPremier,
                                    string EncaissDernier, int NbFactures, int NbEncaissements, string DerniereModif, string SynchroInfogreffe,
                                    string Notes);

/// <summary>Une ligne du compte (Mouvement de l'ecran) : en base, un solde de CLIENT_SOLDE (date et solde, les deux colonnes de RIG v2).</summary>
public sealed record MouvementClientVue(string Abrege, string Date, string Libelle, long Debit, long Credit, long Solde);

/// <summary>Un contact (Contact de l'ecran) ; RIG v2 n'a pas de contact « principal » : toujours faux en base.</summary>
public sealed record ContactClientVue(string Abrege, string Nom, string Fonction, string Tel, string Mail, bool Principal);

/// <summary>La fiche lue, et d'ou (Pont/LectureEcranMain.cs).</summary>
public sealed record VueFicheClient(string Mode, string? Message, FicheClientVue? Fiche, MouvementClientVue[] Mouvements, ContactClientVue[] Contacts);

/// <summary>
/// L'entree de l'ecran « Fiche client » (PROC_FCLIENT). Plan « RIG v3 100 % fonctionnel », lot 14 (27/11/2026), sur la marche du lot 17.
/// Le choix du client : la liste d'EcranClients.Clients. Branche au serveur, la fiche est celle de la base du greffe
/// (Serveur/Socle/EcransMain/LectureClients.cs, FicheAsync : identification, parametrage, informations, contacts et les six derniers soldes
/// de RIG v2) ; l'ecran n'ecrit rien. Le compte montre les SOLDES de RIG v2 (date et solde) : les colonnes Mouvement, Debit et Credit de
/// l'ecran sont un ajout de la maquette, vides en base. SANS serveur : mode « demo », aucune fiche (l'ecran garde ses exemples, marques).
/// </summary>
public static class EcranFicheClient
{
    // fix-ok: mesure le 27/11/2026 -- web/src/regles.test.ts rouge (« ./ecrans/EcranFicheClient.tsx: expected 'EcranClients.Clients' to
    // match /^EcranFicheClient\.\w+$/ ») : la fiche appelait le pont d'un autre ecran ; elle a le sien, meme lecture. Les deux autres
    // retouches de ce fichier : Enumerable.Reverse (le Reverse d'un tableau est celui de Span, sans retour) et cette methode.
    /// <summary>La liste du choix : celle de l'ecran Clients (un ecran n'appelle que son pont : web/src/regles.test.ts).</summary>
    [JSInvokable("EcranFicheClient.Clients")]
    public static Task<VueClients> Clients() => EcranClients.ClientsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    [JSInvokable("EcranFicheClient.Fiche")]
    public static Task<VueFicheClient> Fiche(int id) => FicheAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id);

    public static async Task<VueFicheClient> FicheAsync(ClientServeur? serveur, string? greffe, int id)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "la fiche du client", (s, g) =>
            s.LireAsync<FicheClientLue>(RoutesEcransMain.Pour(RoutesClients.Fiche, g).Replace("{id}", id.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)));
        if (lu.Valeur is not { } f) return new VueFicheClient(lu.Mode, lu.Message, null, [], []);
        var fiche = new FicheClientVue(f.Abrege, f.Nom, f.Compte, f.Siren, f.Adresse, EcranClients.Type(f.Type), EcranClients.Statut(f.EtatCompte), f.Groupe,
            f.Naissance, NumPc: "", f.Mail, f.TelPrincipal, f.TelPortable, f.ModePaiement, f.Provision, f.ReleveFacture, f.FactuPremiere,
            f.FactuDerniere, f.EncaissPremier, f.EncaissDernier, f.NbFactures, f.NbEncaissements, f.DerniereModif, f.SynchroInfogreffe, f.Notes);
        // Les soldes du plus ancien au plus recent : l'ecran prend le dernier comme solde actuel.
        var mouvements = Enumerable.Reverse(f.Soldes).Select(s => new MouvementClientVue(f.Abrege, s.Date, "Solde", 0, 0, s.Solde)).ToArray();
        return new VueFicheClient(lu.Mode, null, fiche, mouvements,
            [.. f.Contacts.Select(c => new ContactClientVue(f.Abrege, c.Nom, c.Fonction, c.Telephone, c.Mail, Principal: false))]);
    }
}
