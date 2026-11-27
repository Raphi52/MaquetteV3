using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Clients » (PROC_GCLIENT, web/src/ecrans/EcranClients.tsx) et « Fiche client » (PROC_FCLIENT, web/src/ecrans/EcranFicheClient.tsx) :
/// les clients en compte du greffe et la fiche de l'un d'eux, lus dans la base V3 du greffe (rec.Client recopie CLIENT ; rec.ClientAdresse,
/// v2.CLIENT_SOLDE, v2.CLIENT_CONTACT, v2.FACTURE, v2.VENTILATION, v2.ENCAISSEMENT). LECTURE SEULE (LectureV2) : les deux écrans V3 n'éditent
/// pas. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026, marche du lot 17.
///
/// SOURCE (ancien RIG) :
///   - LA LISTE : PROC_GCLIENT\OPE_CRITERES.cs:42-123 -- CLIENT INNER JOIN CLIENT_GROUPE_REF (libelle du groupe), CLIEN_TYPE = 'C', tri par
///     abrege ; les criteres de statut (A, S, B) et de type (en compte, greffe, prevention, web, PC : les combinaisons de CLIEN_IS_COMPTE_WEB,
///     CLIEN_IS_COMPTE_PC, CLIEN_CODE_GREFFE et CLIEN_GROUPE, l. 72-111) et le mot cle (abrege, nom, prenom, compte) : l'ecran V3 les applique
///     sur la liste lue, comme ses vues. Nom = « nom prénom » et type = DgvRow.GetTypeClient (PROC_GCLIENT\DgvRow.cs:21-37).
///   - LA FICHE : PROC_FCLIENT -- OPE_IDENTIFICATION.cs:90-105 (etat du compte, abrege, nom, compte, courriel, SIREN, naissance, groupe),
///     OPE_ADRESSE.cs:101 (CLIENT_ADRESSE, l'adresse par defaut), OPE_PARAMETRAGE.cs:23-28 (provision = CLIEN_SEUIL_COMPTE, releve de factures,
///     mode de paiement), OPE_CONTACT.cs:35 (CLIENT_CONTACT), OPE_INFORMATIONS.cs:40-75 (synchronisation Infogreffe par CLIEN_ETAT_EDI,
///     derniere modification, factures du client FACTU_ID_CLIEN, encaissements par ses ventilations VENTI_ID_CLIEN -> ENCAISSEMENT, les six
///     derniers soldes CLIENT_SOLDE par date decroissante).
/// NON LU (nomme) : le numero de PC ou de prevention d'un compte PC (NUMERO_METIER, OPE_IDENTIFICATION.cs:233-288) ; le mot de passe web.
/// </summary>
public static class LectureClients
{
    public const int Plafond = 2000;

    private static async Task<bool> TableAsync(SqlConnection baseGreffe, string table, CancellationToken ct)
    {
        await using var o = await LectureV2.LireAsync(baseGreffe, "SELECT OBJECT_ID(@t)", ct, new SqlParameter("@t", table));
        return await o.ReadAsync(ct) && !o.IsDBNull(0);
    }

    /// <summary>DgvRow.GetTypeClient (PROC_GCLIENT\DgvRow.cs:21-37) ; « » hors des cinq cas.</summary>
    public static string TypeClient(bool web, bool pc, string codeGreffe, string groupe) => (web, pc) switch
    {
        (false, false) => codeGreffe.Length == 0 ? "En compte" : "Greffe",
        (false, true) when groupe == "PR" => "Prévention",
        (false, true) when groupe == "PC" => "PC",
        (true, false) when codeGreffe.Length == 0 => "Web",
        _ => "",
    };

    private static string T(SqlDataReader r, string c) => r[c] is DBNull or null ? "" : Convert.ToString(r[c], CultureInfo.InvariantCulture)!.Trim();

    /// <summary>Les clients en compte (CLIEN_TYPE = 'C'), par abrege ; le groupe par son libelle quand la base a CLIENT_GROUPE_REF.</summary>
    public static async Task<ClientsLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var avecGroupes = await TableAsync(baseGreffe, "v2.CLIENT_GROUPE_REF", ct);
        var sql = $"""
            SELECT TOP (@plafond) c.Id, c.NumeroCompte, c.Abrege, c.Nom, c.Prenom, c.Groupe, c.Statut, c.EstCompteWeb, c.EstComptePc, c.CodeGreffe,
                   {(avecGroupes ? "g.R_CLIENTGROU_LIBELLE_ECRAN" : "c.Groupe")} AS LibelleGroupe, COUNT(*) OVER () AS Total
            FROM rec.Client AS c {(avecGroupes ? "INNER JOIN v2.CLIENT_GROUPE_REF AS g ON g.R_CLIENTGROU_VALEUR = c.Groupe" : "")}
            WHERE c.Type = 'C'
            ORDER BY c.Abrege, c.Id
            """;
        var lignes = new List<ClientLu>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, sql, ct, new SqlParameter("@plafond", plafond));
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new ClientLu(Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), T(r, "NumeroCompte"), T(r, "Abrege"),
                $"{T(r, "Nom")} {T(r, "Prenom")}".Trim(), T(r, "LibelleGroupe"), T(r, "Statut"),
                TypeClient(r["EstCompteWeb"] is true, r["EstComptePc"] is true, T(r, "CodeGreffe"), T(r, "Groupe"))));
        }
        return new ClientsLus([.. lignes], total);
    }

    /// <summary>La fiche du client <paramref name="id"/> (en compte) ; null s'il n'existe pas.</summary>
    public static async Task<FicheClientLue?> FicheAsync(SqlConnection baseGreffe, int id, CancellationToken ct)
    {
        var avecGroupes = await TableAsync(baseGreffe, "v2.CLIENT_GROUPE_REF", ct);
        var avecFactures = await TableAsync(baseGreffe, "v2.FACTURE", ct);
        var avecEncaissements = await TableAsync(baseGreffe, "v2.VENTILATION", ct) && await TableAsync(baseGreffe, "v2.ENCAISSEMENT", ct);
        var sql = $"""
            SELECT c.Id, c.Abrege, c.Nom, c.Prenom, c.NumeroCompte, c.Siren, c.Groupe, c.Statut, c.EstCompteWeb, c.EstComptePc, c.CodeGreffe,
                   c.DateNaissance, c.Email, c.Telephone, c.TelephonePortable, c.ModePaiement, c.SeuilCompte, c.ReleveFacture,
                   c.DateDerniereModification, c.EtatEdi, c.DateEdi, c.BlocNotes,
                   {(avecGroupes ? "(SELECT TOP (1) g.R_CLIENTGROU_LIBELLE_ECRAN FROM v2.CLIENT_GROUPE_REF AS g WHERE g.R_CLIENTGROU_VALEUR = c.Groupe)" : "c.Groupe")} AS LibelleGroupe,
                   (SELECT TOP (1) CONCAT_WS(' ', NULLIF(a.LigneAdresse, ''), NULLIF(CONCAT_WS(' ', a.CodePostal, a.Localite), ''))
                    FROM rec.ClientAdresse AS a WHERE a.IdClient = c.Id ORDER BY a.ParDefaut DESC, a.Id) AS Adresse,
                   {(avecFactures ? "(SELECT COUNT(*) FROM v2.FACTURE AS f WHERE f.FACTU_ID_CLIEN = c.Id)" : "0")} AS NbFactures,
                   {(avecFactures ? "(SELECT MIN(f.FACTU_DATE_CREATION) FROM v2.FACTURE AS f WHERE f.FACTU_ID_CLIEN = c.Id)" : "NULL")} AS FactuPremiere,
                   {(avecFactures ? "(SELECT MAX(f.FACTU_DATE_CREATION) FROM v2.FACTURE AS f WHERE f.FACTU_ID_CLIEN = c.Id)" : "NULL")} AS FactuDerniere,
                   {(avecEncaissements ? "(SELECT COUNT(*) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_CLIEN = c.Id)" : "0")} AS NbEncaissements,
                   {(avecEncaissements ? "(SELECT MIN(e.ENCMT_DATE_ENCAISSEMENT) FROM v2.VENTILATION AS v JOIN v2.ENCAISSEMENT AS e ON e.ENCMT_ID_ENCMT = v.VENTI_ID_ENCMT WHERE v.VENTI_ID_CLIEN = c.Id)" : "NULL")} AS EncaissPremier,
                   {(avecEncaissements ? "(SELECT MAX(e.ENCMT_DATE_ENCAISSEMENT) FROM v2.VENTILATION AS v JOIN v2.ENCAISSEMENT AS e ON e.ENCMT_ID_ENCMT = v.VENTI_ID_ENCMT WHERE v.VENTI_ID_CLIEN = c.Id)" : "NULL")} AS EncaissDernier
            FROM rec.Client AS c WHERE c.Id = @id AND c.Type = 'C'
            """;
        static string Jour(object v) => v is DateTime d ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        FicheClientLue? fiche;
        await using (var r = await LectureV2.LireAsync(baseGreffe, sql, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return null;
            // OPE_INFORMATIONS.cs:40-46 : « 1 » en attente, « 2 » la date de transmission, rien = non transmis.
            var etatEdi = T(r, "EtatEdi");
            var synchro = etatEdi switch { "1" => "En attente de transmission", "2" => Jour(r["DateEdi"]), "" => "Non transmis", _ => etatEdi };
            fiche = new FicheClientLue(id, T(r, "Abrege"), $"{T(r, "Nom")} {T(r, "Prenom")}".Trim(), T(r, "NumeroCompte"), T(r, "Siren"), T(r, "Adresse"),
                TypeClient(r["EstCompteWeb"] is true, r["EstComptePc"] is true, T(r, "CodeGreffe"), T(r, "Groupe")), T(r, "Statut"), T(r, "LibelleGroupe"),
                Jour(r["DateNaissance"]), T(r, "Email"), T(r, "Telephone"), T(r, "TelephonePortable"), T(r, "ModePaiement"),
                r["SeuilCompte"] is DBNull ? 0 : (long)Math.Round(Convert.ToDecimal(r["SeuilCompte"], CultureInfo.InvariantCulture) * 100m),
                r["ReleveFacture"] is true, Jour(r["FactuPremiere"]), Jour(r["FactuDerniere"]), Jour(r["EncaissPremier"]), Jour(r["EncaissDernier"]),
                Convert.ToInt32(r["NbFactures"], CultureInfo.InvariantCulture), Convert.ToInt32(r["NbEncaissements"], CultureInfo.InvariantCulture),
                Jour(r["DateDerniereModification"]), synchro, T(r, "BlocNotes"), [], []);
        }
        var soldes = new List<SoldeClientLu>();
        if (await TableAsync(baseGreffe, "v2.CLIENT_SOLDE", ct))
            await using (var r = await LectureV2.LireAsync(baseGreffe, """
                             SELECT TOP (6) CLISO_DATE_SOLDE, CLISO_MONTANT FROM v2.CLIENT_SOLDE WHERE CLISO_ID_CLIEN = @id
                             ORDER BY CLISO_DATE_SOLDE DESC, CLISO_ID_CLISO DESC
                             """, ct, new SqlParameter("@id", id)))
                while (await r.ReadAsync(ct))
                    soldes.Add(new SoldeClientLu(r.GetDateTime(0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        r.IsDBNull(1) ? 0 : (long)Math.Round(Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture) * 100m)));
        var contacts = new List<ContactClientLu>();
        if (await TableAsync(baseGreffe, "v2.CLIENT_CONTACT", ct))
            await using (var r = await LectureV2.LireAsync(baseGreffe, """
                             SELECT CLICO_NOM, CLICO_PRENOM, CLICO_FONCTION, CLICO_TELEPHONE, CLICO_PORTABLE, CLICO_EMAIL FROM v2.CLIENT_CONTACT
                             WHERE CLICO_ID_CLIEN = @id ORDER BY CLICO_ID_CLICO
                             """, ct, new SqlParameter("@id", id)))
                while (await r.ReadAsync(ct))
                {
                    var tel = T(r, "CLICO_TELEPHONE");
                    contacts.Add(new ContactClientLu($"{T(r, "CLICO_NOM")} {T(r, "CLICO_PRENOM")}".Trim(), T(r, "CLICO_FONCTION"),
                        tel.Length > 0 ? tel : T(r, "CLICO_PORTABLE"), T(r, "CLICO_EMAIL")));
                }
        return fiche with { Soldes = [.. soldes], Contacts = [.. contacts] };
    }
}
