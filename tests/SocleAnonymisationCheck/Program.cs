using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Outils.Anonymisation;

// Banc de SOC-021 « fabriquer une copie anonymisee de la base d'un greffe pour les tests » (lot 6 du plan de
// reconstruction, phase 2, 2026-11-21). Un controle par critere CAxx, qui cite son id ; la contre-epreuve
// rouge porte sur le controle cle (CA06 : une colonne laissee en clair DOIT etre trouvee).
//
//  1. RIG_DEV (SQL-DEV\DEV) est lu en LECTURE SEULE, par la porte de l'outil (PorteSource : SELECT seul,
//     accord « RIG_DEV ») : un extrait COHERENT -- factures recentes (signees) de leurs clients, et leur
//     journal AUDIT ; parties et leurs clients et adresses ; dirigeants ; tous les agents ; courriels
//     envoyes et leurs traces ; lots d'editions et des demandes qui les citent (cle etrangere en TEXTE).
//  2. Cet extrait devient une base V2 JETABLE sur le SQL Server local, avec les vraies cles etrangeres de
//     RIG_DEV entre ces tables (verifiees), l'index unique des comptes d'agents et une vue.
//  3. L'outil la copie (Outils/Anonymisation), puis le banc compare source et copie, ligne a ligne.
//
// Il lui faut le SQL Server de developpement (docker compose up -d) et la lecture de RIG_DEV (compte
// Windows ; autre serveur : RIGV3_RIG_DEV). Doc : docs/reconstruction/socle-anonymisation.md.

var chaineRigDev = Environment.GetEnvironmentVariable("RIGV3_RIG_DEV") is { Length: > 0 } c0 ? c0 :
    @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=20";
var baseRigDev = new SqlConnectionStringBuilder(chaineRigDev).InitialCatalog;
var dossier = AppContext.BaseDirectory;

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);
void Info(string quoi) => Console.WriteLine("        " + quoi);

// ====================================================================================================
// 0. Les valeurs fictives elles-memes (Pseudonymes) : bijection, derangement, forme, casse, format
// ====================================================================================================
using (var p = Pseudonymes.DepuisGraine("banc"))
{
    var lettres = Enumerable.Range(0, 26 * 26).Select(i => $"{(char)('A' + i / 26)}{(char)('A' + i % 26)}").ToList();
    var images = lettres.Select(p.Mot).ToList();
    Exiger(images.Distinct().Count() == lettres.Count && lettres.Zip(images).All(x => x.First != x.Second),
        "SOC-021 CA01/CA02 : les 676 mots de deux lettres ont 676 images distinctes, aucune egale a son mot (bijection sans point fixe)");
    var nombres = Enumerable.Range(0, 1000).Select(i => i.ToString("000")).ToList();
    var imagesN = nombres.Select(p.Mot).ToList();
    Exiger(imagesN.Distinct().Count() == 1000 && nombres.Zip(imagesN).All(x => x.First != x.Second && x.Second.All(char.IsAsciiDigit)),
        "SOC-021 CA01 : les 1 000 nombres de trois chiffres ont 1 000 images distinctes de trois chiffres, aucune egale");
    var dupont = p.Texte("Dupont-Hélène d'Arc");
    // « Dupont-Hélène » : le « é » est le 9e caractere (rang 8).
    Exiger(Forme(dupont) == Forme("Dupont-Hélène d'Arc") && p.Texte("DUPONT") == p.Texte("Dupont").ToUpperInvariant()
           && "àáâãäåæçèéêëìíîïðñòóôõöøùúûüýþœšžÿ".Contains(dupont[8]),
        $"SOC-021 CA01/CA02 : « Dupont-Hélène d'Arc » -> « {Forme(dupont)} » meme forme (casse, accent, tiret, apostrophe) ; " +
        "« DUPONT » et « Dupont » donnent les memes lettres (recherche insensible a la casse)");
    var tel = p.Telephone("06 12 34 56 78");
    Exiger(tel.StartsWith("06 ") && Forme(tel) == Forme("06 12 34 56 78") && tel != "06 12 34 56 78"
           && tel.Replace(" ", "") == p.Telephone("0612345678"),
        $"SOC-021 CA01 : telephone « 06 12 34 56 78 » -> « {tel} » : deux premiers chiffres et separateurs gardes, memes chiffres ecrit sans espace");
    var mail = p.Courriel("jean.dupont@orange.fr; Jean.Dupont@Orange.FR");
    var longue = p.Courriel("cabinet@dupont.avocat");
    Exiger(mail.Contains('@') && mail.EndsWith(".FR") && mail.Contains(".fr;") && Forme(mail) == Forme("jean.dupont@orange.fr; Jean.Dupont@Orange.FR")
           && mail.StartsWith(p.Mot("jean") + "." + p.Mot("dupont") + "@") && !longue.EndsWith(".avocat") && Forme(longue) == Forme("cabinet@dupont.avocat"),
        $"SOC-021 CA01/CA02 : courriels -> « {mail} » : extension de 2 a 4 lettres gardee, memes mots que les noms (JEAN, DUPONT) ; " +
        $"une extension longue qui peut designer quelqu'un est remplacee (« {longue} »)");
    var naissance = new DateTime(1975, 3, 12, 0, 0, 0);
    var fausse = p.Date(naissance);
    var bissextile = p.Date(new DateTime(1976, 2, 29, 8, 30, 0));
    Exiger(fausse.Year == 1975 && fausse != naissance && fausse.TimeOfDay == TimeSpan.Zero && p.Date(naissance) == fausse
           && bissextile.Year == 1976 && bissextile.TimeOfDay == new TimeSpan(8, 30, 0)
           && Enumerable.Range(0, 366).Select(j => new DateTime(1976, 1, 1).AddDays(j)).Select(p.Date).Distinct().Count() == 366,
        $"SOC-021 CA01 : date de naissance 1975-03-12 -> {fausse:yyyy-MM-dd} (meme annee, heure gardee, jamais elle-meme ; les 366 jours de 1976 restent distincts)");
    using var autre = Pseudonymes.DepuisGraine("autre");
    using var hasard1 = Pseudonymes.Aleatoire();
    using var hasard2 = Pseudonymes.Aleatoire();
    Exiger(autre.Texte("DUPONT") != p.Texte("DUPONT") && hasard1.Texte("DUPONT MARTIN DURAND") != hasard2.Texte("DUPONT MARTIN DURAND"),
        "SOC-021 T01 : deux cles donnent deux copies differentes ; la cle aleatoire (par defaut) n'est jamais la meme");
    var anonymiseur = new Anonymiseur(p, new CatalogueRegles());
    const string journal = "<deleted FACTU_NUMERO_FACTURE=\"26-022764\" FACTU_MONTANT_TTC=\"12.5000\" FACTU_DESIGNATION_FACTURATION=\"DUPONT JEAN\" FACTU_ID_CLIEN=\"42\"/>" +
                           "<deleted FACTU_NUMERO_FACTURE=\"26-022765\" FACTU_DESIGNATION_FACTURATION=\"MARTIN\"/>";
    var xml = anonymiseur.Xml(journal, "FACTURE");
    Exiger(xml.Contains("FACTU_NUMERO_FACTURE=\"26-022764\"") && xml.Contains("FACTU_MONTANT_TTC=\"12.5000\"") && xml.Contains("FACTU_ID_CLIEN=\"42\"")
           && xml.Contains($"FACTU_DESIGNATION_FACTURATION=\"{p.Texte("DUPONT JEAN")}\"") && xml.Contains($"\"{p.Texte("MARTIN")}\"")
           && !xml.Contains("DUPONT") && xml.Split("<deleted").Length == 3,
        "SOC-021 CA04 : un XML du journal (deux lignes) garde ses noms, ses numeros et montants ; la designation du client prend la regle de sa colonne");
}

// ====================================================================================================
// 1. RIG_DEV, en lecture seule, par la porte de l'outil
// ====================================================================================================
PorteSource rigDev;
try
{
    rigDev = await PorteSource.OuvrirAsync(chaineRigDev, baseRigDev, CancellationToken.None);
    Exiger(true, $"RIG_DEV lu en lecture seule par la porte de l'outil (accord « {baseRigDev} ») : {rigDev.Serveur} / {rigDev.Base}");
}
catch (Exception e)
{
    Exiger(false, $"RIG_DEV lisible ({new SqlConnectionStringBuilder(chaineRigDev).DataSource}) : {e.Message} -- il faut l'acces en lecture a SQL-DEV\\DEV (ou RIGV3_RIG_DEV)");
    return 1;
}

var chrono = Stopwatch.StartNew();
await using var v2 = await BaseJetable.CreerAsync("SocleAnonymisationV2");
async Task<List<int>> Ids(string sql) => [.. (await rigDev.LignesAsync(sql, CancellationToken.None)).Where(l => l[0] is not null).Select(l => Convert.ToInt32(l[0]))];
string Liste(IEnumerable<int> ids) => string.Join(", ", ids.Distinct().DefaultIfEmpty(-1));

// Chaque champ que nomme la story doit etre REMPLI dans l'extrait, sinon son controle ne prouve rien : en plus des
// lignes recentes, des lignes ou il l'est (RIG_DEV, 2026-11-21 : 2 105 traces avec destinataires, 11 365 factures
// commentees, 75 415 parties « es qualite » ; aucun texte RPPP de dirigeant, un seul portable de client).
var factures = await rigDev.LignesAsync("SELECT TOP 400 FACTU_NUMERO_FACTURE, FACTU_ID_CLIEN, FACTU_ID_ADRBS FROM dbo.FACTURE WITH (NOLOCK) " +
                                        "ORDER BY FACTU_DATE_CREATION DESC, FACTU_NUMERO_FACTURE DESC", CancellationToken.None);
factures.AddRange(await rigDev.LignesAsync("SELECT TOP 40 FACTU_NUMERO_FACTURE, FACTU_ID_CLIEN, FACTU_ID_ADRBS FROM dbo.FACTURE WITH (NOLOCK) " +
                                           "WHERE FACTU_COMMENTAIRE LIKE '%[a-z]%' ORDER BY FACTU_DATE_CREATION DESC", CancellationToken.None));
var numerosFactures = factures.Select(f => (string)f[0]!).Distinct().ToList();
var parties = new List<int>();
foreach (var filtre in new[] { "PRT_ID_CLIEN IS NOT NULL", "PRT_ID_CLIEN IS NULL", "PRT_DATE_NAISSANCE IS NOT NULL", "PRT_ADRESSE_MAIL LIKE '%@%'", "PRT_TELEPHONE LIKE '%[0-9]%'",
                               "PRT_TEXTE_ESQUALITE LIKE '%[a-z]%'", "PRT_TEXTE_ASSISTANCE LIKE '%[a-z]%'" })
    parties.AddRange(await Ids($"SELECT TOP {(filtre == "PRT_ID_CLIEN IS NOT NULL" ? 400 : 60)} PRT_ID_PRT FROM dbo.PARTIE WITH (NOLOCK) WHERE {filtre} ORDER BY PRT_ID_PRT DESC"));
var liensParties = await rigDev.LignesAsync($"SELECT PRT_ID_CLIEN, PRT_ID_ADRBS FROM dbo.PARTIE WITH (NOLOCK) WHERE PRT_ID_PRT IN ({Liste(parties)})", CancellationToken.None);
var clients = new List<int>();
clients.AddRange(factures.Where(f => f[1] is not null).Select(f => Convert.ToInt32(f[1])));
clients.AddRange(liensParties.Where(l => l[0] is not null).Select(l => Convert.ToInt32(l[0])));
foreach (var filtre in new[] { "1 = 1", "CLIEN_DATE_NAISSANCE IS NOT NULL", "CLIEN_EMAIL LIKE '%@%'", "CLIEN_TELEPHONE LIKE '%[0-9]%'", "CLIEN_PRENOM LIKE '%[a-z]%'",
                               "CLIEN_BLOC_NOTES LIKE '%[a-z]%'", "CLIEN_TELEPHONE_PORTABLE LIKE '%[0-9]%'" })
    clients.AddRange(await Ids($"SELECT TOP {(filtre == "1 = 1" ? 300 : 60)} CLIEN_ID_CLIEN FROM dbo.CLIENT WITH (NOLOCK) WHERE {filtre} ORDER BY CLIEN_ID_CLIEN DESC"));
var adresses = new List<int>();
adresses.AddRange(liensParties.Where(l => l[1] is not null).Select(l => Convert.ToInt32(l[1])));
adresses.AddRange(factures.Where(f => f[2] is not null).Select(f => Convert.ToInt32(f[2])));
adresses.AddRange(await Ids("SELECT TOP 200 ADRBS_ID_ADRBS FROM dbo.ADRESSE_BASE WITH (NOLOCK) ORDER BY ADRBS_ID_ADRBS DESC"));
var dirigeants = await Ids("SELECT TOP 300 DRGPP_ID_DRGPP FROM dbo.DIRIGEANT_PP WITH (NOLOCK) ORDER BY DRGPP_ID_DRGPP DESC");
dirigeants.AddRange(await Ids("SELECT TOP 60 DRGPP_ID_DRGPP FROM dbo.DIRIGEANT_PP WITH (NOLOCK) WHERE DRGPP_DATE_NAISSANCE IS NOT NULL ORDER BY DRGPP_ID_DRGPP DESC"));
var numerosSql = string.Join(", ", numerosFactures.Select(n => "'" + n.Replace("'", "''") + "'"));
var audits = await Ids($"SELECT AUDIT_ID_AUDIT FROM dbo.AUDIT WITH (NOLOCK) WHERE AUDIT_TABLE = 'FACTURE' AND AUDIT_ID_TABLE IN ({numerosSql})");
audits.AddRange(await Ids("SELECT TOP 100 AUDIT_ID_AUDIT FROM dbo.AUDIT WITH (NOLOCK) WHERE AUDIT_TABLE = 'ENCAISSEMENT' ORDER BY AUDIT_ID_AUDIT DESC"));
var demandes = await Ids("SELECT TOP 300 DMNED_ID_DMNED FROM dbo.DEMANDE_EDITION WITH (NOLOCK) WHERE DMNED_LOT IS NOT NULL ORDER BY DMNED_ID_DMNED DESC");

var extrait = new (string Table, string? Filtre)[]
{
    ("UTILISATEUR", null),
    ("CLIENT", $"CLIEN_ID_CLIEN IN ({Liste(clients)})"),
    ("ADRESSE_BASE", $"ADRBS_ID_ADRBS IN ({Liste(adresses)})"),
    ("PARTIE", $"PRT_ID_PRT IN ({Liste(parties)})"),
    ("DIRIGEANT_PP", $"DRGPP_ID_DRGPP IN ({Liste(dirigeants)})"),
    ("FACTURE", $"FACTU_NUMERO_FACTURE IN ({numerosSql})"),
    ("AUDIT", $"AUDIT_ID_AUDIT IN ({Liste(audits)})"),
    ("ENCAISSEMENT", "ENCMT_ID_ENCMT IN (SELECT TOP 200 ENCMT_ID_ENCMT FROM dbo.ENCAISSEMENT WITH (NOLOCK) ORDER BY ENCMT_ID_ENCMT DESC)"),
    ("EMAIL_ENVOI_TRACE", "EMTRC_ID_EMTRC IN (SELECT TOP 300 EMTRC_ID_EMTRC FROM dbo.EMAIL_ENVOI_TRACE WITH (NOLOCK) ORDER BY EMTRC_ID_EMTRC DESC) " +
                          "OR EMTRC_ID_EMTRC IN (SELECT TOP 200 EMTRC_ID_EMTRC FROM dbo.EMAIL_ENVOI_TRACE WITH (NOLOCK) WHERE EMTRC_TO LIKE '%@%' ORDER BY EMTRC_ID_EMTRC DESC)"),
    ("EMAIL_ENVOYE", "EMAILENV_ID_EMAILENV IN (SELECT TOP 300 EMAILENV_ID_EMAILENV FROM dbo.EMAIL_ENVOYE WITH (NOLOCK) ORDER BY EMAILENV_ID_EMAILENV DESC)"),
    ("LOT", null),
    ("DEMANDE_EDITION", $"DMNED_ID_DMNED IN ({Liste(demandes)})"),
};
var nomsExtrait = extrait.Select(e => e.Table).ToHashSet(StringComparer.OrdinalIgnoreCase);
var lignesSource = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
foreach (var (table, filtre) in extrait)
{
    await ExecuterAsync(v2.Connexion, await DdlBancAsync(rigDev, table));
    var colonnes = (await rigDev.LignesAsync($"SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.{table}') AND c.is_computed = 0 ORDER BY c.column_id", CancellationToken.None))
        .Select(l => $"[{l[0]}]").ToList();
    await using var lecteur = await rigDev.LireAsync($"SELECT {string.Join(", ", colonnes)} FROM dbo.{table} WITH (NOLOCK){(filtre is null ? "" : " WHERE " + filtre)}", CancellationToken.None);
    using var bulk = new SqlBulkCopy(v2.Connexion, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock)
    { DestinationTableName = $"dbo.[{table}]", BulkCopyTimeout = 600 };
    for (var i = 0; i < lecteur.FieldCount; i++) bulk.ColumnMappings.Add(lecteur.GetName(i), lecteur.GetName(i));
    await bulk.WriteToServerAsync(lecteur);
    lignesSource[table] = await EntierAsync(v2.Connexion, $"SELECT COUNT_BIG(*) FROM dbo.[{table}]");
}
// L'index unique des comptes d'agents (UTILISATEUR.sql) et les vraies cles etrangeres de RIG_DEV entre ces tables.
await ExecuterAsync(v2.Connexion, "CREATE UNIQUE NONCLUSTERED INDEX [IFK_UTLST_LOGIN] ON dbo.UTILISATEUR ([UTLST_LOGIN] ASC);");
var clesRigDev = await rigDev.LignesAsync(
    "SELECT fk.name, OBJECT_NAME(fk.parent_object_id), OBJECT_NAME(fk.referenced_object_id), COL_NAME(fkc.parent_object_id, fkc.parent_column_id), " +
    "COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
    "WHERE fk.is_not_trusted = 0 AND fk.is_disabled = 0", CancellationToken.None);
var clesSource = new List<string>();
foreach (var k in clesRigDev.Where(k => nomsExtrait.Contains((string)k[1]!) && nomsExtrait.Contains((string)k[2]!)))
{
    try
    {
        await ExecuterAsync(v2.Connexion, $"ALTER TABLE dbo.[{k[1]}] WITH CHECK ADD CONSTRAINT [{k[0]}] FOREIGN KEY ([{k[3]}]) REFERENCES dbo.[{k[2]}] ([{k[4]}]);");
        clesSource.Add((string)k[0]!);
    }
    catch (SqlException) { Info($"cle {k[0]} : l'extrait ne la verifie pas (ligne referencee hors extrait), non posee"); }
}
await ExecuterAsync(v2.Connexion, "CREATE VIEW dbo.V_CLIENT_FACTURE AS SELECT c.CLIEN_ID_CLIEN, c.CLIEN_NOM, f.FACTU_NUMERO_FACTURE FROM dbo.CLIENT c JOIN dbo.FACTURE f ON f.FACTU_ID_CLIEN = c.CLIEN_ID_CLIEN;");
string[] clesVoulues = ["FK_PARTIE_PRT_ID_CLIEN_CLIENT_CLIEN_ID_CLIEN", "FK_DEMANDE_EDITION_DMNED_LOT_LOT_LOT_NOM", "FK_CLIENT_CLIEN_ID_UTLST_UTILISATEUR_UTLST_ID_UTLST"];
Exiger(clesVoulues.All(clesSource.Contains),
    $"base V2 jetable : extrait de RIG_DEV en {chrono.Elapsed.TotalSeconds:0} s ({string.Join(", ", lignesSource.Select(x => $"{x.Key} {x.Value}"))}) ; " +
    $"{clesSource.Count} cles etrangeres de RIG_DEV verifiees sur l'extrait, dont {string.Join(", ", clesVoulues)}");

// ====================================================================================================
// 2. La copie (graine fixe : le banc se rejoue a l'identique), la source observee avant et apres
// ====================================================================================================
var nomV2 = new SqlConnectionStringBuilder(v2.Connexion).InitialCatalog;
async Task<(long Ecritures, string Empreinte)> EtatSource()
{
    var ecritures = await EntierAsync(v2.Connexion, "SELECT ISNULL(SUM(user_updates), 0) FROM sys.dm_db_index_usage_stats WHERE database_id = DB_ID()");
    var parts = new List<string>();
    foreach (var t in nomsExtrait.Order())
        parts.Add($"{t}:{await EntierAsync(v2.Connexion, $"SELECT COUNT_BIG(*) FROM dbo.[{t}]")}:{await EntierAsync(v2.Connexion, $"SELECT ISNULL(CHECKSUM_AGG(BINARY_CHECKSUM(*)), 0) FROM dbo.[{t}]")}");
    parts.Add("xml:" + await EntierAsync(v2.Connexion, "SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(CONVERT(nvarchar(max), AUDIT_XML), CONVERT(nvarchar(max), AUDIT_XML_NEW))), 0) FROM dbo.AUDIT"));
    return (ecritures, string.Join("|", parts));
}
var avant = await EtatSource();
await using var copie = await BaseJetable.CreerAsync("SocleAnonymisationCopie");
var nomCopie = new SqlConnectionStringBuilder(copie.Connexion).InitialCatalog;
var fichierRapport = Path.Combine(dossier, "rapport-anonymisation.md");
chrono.Restart();
var rapport = await CopieAnonymisee.CopierAsync(new OptionsCopie
{
    Source = v2.Connexion, Cible = copie.Connexion, Graine = "SOC-021", FichierRapport = fichierRapport,
});
var dureeCopie = chrono.Elapsed;
var apres = await EtatSource();
Exiger(rapport.Anomalies.Count == 0 && rapport.Volumes.Count == nomsExtrait.Count,
    $"copie faite en {dureeCopie.TotalSeconds:0.0} s : {rapport.Volumes.Count} tables, {rapport.Colonnes.Count} colonnes traitees, anomalies : {(rapport.Anomalies.Count == 0 ? "aucune" : string.Join(" ; ", rapport.Anomalies))}");
string S(string t) => $"[{nomV2}].dbo.[{t}]";
string C(string t) => $"[{nomCopie}].dbo.[{t}]";

// ====================================================================================================
// CA01 -- noms, prenoms, naissances, adresses, courriels, telephones remplaces, meme longueur, meme format
// ====================================================================================================
var personnelles = new (string Genre, string Table, string Cle, string Colonne)[]
{
    ("nom", "CLIENT", "CLIEN_ID_CLIEN", "CLIEN_NOM"), ("prenom", "CLIENT", "CLIEN_ID_CLIEN", "CLIEN_PRENOM"),
    ("naissance", "CLIENT", "CLIEN_ID_CLIEN", "CLIEN_DATE_NAISSANCE"), ("courriel", "CLIENT", "CLIEN_ID_CLIEN", "CLIEN_EMAIL"),
    ("telephone", "CLIENT", "CLIEN_ID_CLIEN", "CLIEN_TELEPHONE"), ("telephone", "CLIENT", "CLIEN_ID_CLIEN", "CLIEN_TELEPHONE_PORTABLE"),
    ("nom", "DIRIGEANT_PP", "DRGPP_ID_DRGPP", "DRGPP_PATRONYME"), ("prenom", "DIRIGEANT_PP", "DRGPP_ID_DRGPP", "DRGPP_PRENOMS_ETAT_CIVIL"),
    ("nom", "DIRIGEANT_PP", "DRGPP_ID_DRGPP", "DRGPP_NOM_USAGE"), ("naissance", "DIRIGEANT_PP", "DRGPP_ID_DRGPP", "DRGPP_DATE_NAISSANCE"),
    ("adresse", "DIRIGEANT_PP", "DRGPP_ID_DRGPP", "DRGPP_LIEU_NAISSANCE"),
    ("nom", "PARTIE", "PRT_ID_PRT", "PRT_NOM"), ("nom", "PARTIE", "PRT_ID_PRT", "PRT_DESIGNATION"),
    ("naissance", "PARTIE", "PRT_ID_PRT", "PRT_DATE_NAISSANCE"), ("courriel", "PARTIE", "PRT_ID_PRT", "PRT_ADRESSE_MAIL"),
    ("telephone", "PARTIE", "PRT_ID_PRT", "PRT_TELEPHONE"),
    ("nom", "UTILISATEUR", "UTLST_ID_UTLST", "UTLST_NOM"), ("prenom", "UTILISATEUR", "UTLST_ID_UTLST", "UTLST_PRENOM"),
    ("nom", "UTILISATEUR", "UTLST_ID_UTLST", "UTLST_LOGIN"), ("telephone", "UTILISATEUR", "UTLST_ID_UTLST", "UTLST_TEL_DIRECT"),
    ("adresse", "ADRESSE_BASE", "ADRBS_ID_ADRBS", "ADRBS_NUM_VOIE"), ("adresse", "ADRESSE_BASE", "ADRBS_ID_ADRBS", "ADRBS_NOM_VOIE"),
    ("adresse", "ADRESSE_BASE", "ADRBS_ID_ADRBS", "ADRBS_SUITE_ADRESSE"),
    ("adresse", "FACTURE", "FACTU_NUMERO_FACTURE", "FACTU_LIGNE_ADRESSE_FACTURATION"),
};
var parGenre = new Dictionary<string, long>();
foreach (var (genre, table, cle, colonne) in personnelles)
{
    var paires = await LireAsync(copie.Connexion, $"SELECT s.[{colonne}], c.[{colonne}] FROM {S(table)} s JOIN {C(table)} c ON c.[{cle}] = s.[{cle}] WHERE s.[{colonne}] IS NOT NULL");
    long n = 0, restees = 0, longueur = 0, forme = 0, autre = 0;
    foreach (DataRow l in paires.Rows)
    {
        if (l[0] is DateTime ds)
        {
            var dc = (DateTime)l[1];
            n++;
            if (dc == ds) restees++;
            if (dc.Year != ds.Year || dc.TimeOfDay != ds.TimeOfDay) forme++;
            continue;
        }
        var vs = (string)l[0];
        var vc = l[1] as string ?? "";
        if (!vs.Any(char.IsLetterOrDigit)) continue;
        n++;
        if (string.Equals(vs.Trim(), vc.Trim(), StringComparison.OrdinalIgnoreCase)) restees++;
        if (vs.Length != vc.Length) longueur++;
        if (Forme(vs) != Forme(vc)) forme++;
        // Courriel : l'@ reste ; une vraie extension (2 a 4 lettres) reste. RIG_DEV a des fautes (« …@domaine.a ») : rien a garder.
        // fix-ok: mesure du 2026-11-21 -- PARTIE.PRT_ADRESSE_MAIL de RIG_DEV a une adresse de forme « aaaaaaaa@aaaaaa.a »
        // (extension d'une lettre) ; le banc exigeait de la garder, l'outil la remplace a raison.
        if (genre == "courriel" && vs.Contains('@') && (!vc.Contains('@')
            || (System.Text.RegularExpressions.Regex.IsMatch(vs.TrimEnd(), @"\.\p{L}{2,4}$") && vs.TrimEnd()[(vs.TrimEnd().LastIndexOf('.') + 1)..] != vc.TrimEnd()[(vc.TrimEnd().LastIndexOf('.') + 1)..]))) autre++;
        if (genre == "telephone" && vs.Count(char.IsAsciiDigit) >= 6 && new string([.. vs.Where(char.IsAsciiDigit).Take(2)]) != new string([.. vc.Where(char.IsAsciiDigit).Take(2)])) autre++;
    }
    parGenre[genre] = parGenre.GetValueOrDefault(genre) + n;
    if (n == 0) { Info($"CA01 : {table}.{colonne} vide dans l'extrait de RIG_DEV"); continue; }
    Exiger(restees == 0 && longueur == 0 && forme == 0 && autre == 0,
        $"SOC-021 CA01 : {table}.{colonne} ({genre}) : {n} valeurs, {restees} restee(s), {longueur} longueur(s) changee(s), {forme} forme(s) changee(s)" +
        (genre == "courriel" ? $", {autre} courriel(s) sans @ ou extension changee" : genre == "telephone" ? $", {autre} indicatif(s) change(s)" : ""));
}
Exiger(new[] { "nom", "prenom", "naissance", "adresse", "courriel", "telephone" }.All(g => parGenre.GetValueOrDefault(g) > 0),
    $"SOC-021 CA01 : chaque genre est present dans l'extrait et remplace ({string.Join(", ", parGenre.Select(g => $"{g.Key} {g.Value}"))}) ; clients, dirigeants, parties et agents");
var secrets = await EntierAsync(copie.Connexion, $"SELECT (SELECT COUNT(*) FROM {C("CLIENT")} WHERE CLIEN_MDP IS NOT NULL) + (SELECT COUNT(*) FROM {C("UTILISATEUR")} WHERE UTLST_PASSWORD IS NOT NULL)");
var secretsSource = await EntierAsync(copie.Connexion, $"SELECT (SELECT COUNT(*) FROM {S("CLIENT")} WHERE CLIEN_MDP IS NOT NULL) + (SELECT COUNT(*) FROM {S("UTILISATEUR")} WHERE UTLST_PASSWORD IS NOT NULL)");
Exiger(secrets == 0, $"SOC-021 CA01 : mots de passe des clients et des agents vides dans la copie ({secretsSource} dans l'extrait, {secrets} dans la copie)");

// ====================================================================================================
// CA02 -- une personne presente dans plusieurs tables recoit partout la meme valeur fictive
// ====================================================================================================
async Task<(long Source, long Copie)> Paire(string sql) => (await EntierAsync(copie.Connexion, sql.Replace("{B}", nomV2)), await EntierAsync(copie.Connexion, sql.Replace("{B}", nomCopie)));
var nomSurFacture = await Paire("SELECT COUNT(*) FROM [{B}].dbo.FACTURE f JOIN [{B}].dbo.CLIENT c ON c.CLIEN_ID_CLIEN = f.FACTU_ID_CLIEN " +
                                "WHERE ' ' + f.FACTU_DESIGNATION_FACTURATION + ' ' LIKE '% ' + c.CLIEN_NOM + ' %'");
Exiger(nomSurFacture.Source > 0 && nomSurFacture.Source == nomSurFacture.Copie,
    $"SOC-021 CA02 : le nom du client se retrouve, mot pour mot, dans la designation de ses factures : {nomSurFacture.Source} factures dans la source, {nomSurFacture.Copie} dans la copie");
var journalFacture = await Paire("SELECT COUNT(*) FROM [{B}].dbo.AUDIT a JOIN [{B}].dbo.FACTURE f ON a.AUDIT_TABLE = 'FACTURE' AND a.AUDIT_ID_TABLE = f.FACTU_NUMERO_FACTURE " +
                                 "WHERE a.AUDIT_XML.value('(/*/@FACTU_DESIGNATION_FACTURATION)[1]', 'varchar(255)') = f.FACTU_DESIGNATION_FACTURATION");
Exiger(journalFacture.Source > 0 && journalFacture.Source == journalFacture.Copie,
    $"SOC-021 CA02 : le journal AUDIT (XML) porte la meme designation que la facture qu'il decrit : {journalFacture.Source} lignes dans la source, {journalFacture.Copie} dans la copie");
var comptes = await Paire("SELECT COUNT(*) FROM [{B}].dbo.AUDIT a WHERE EXISTS (SELECT 1 FROM [{B}].dbo.UTILISATEUR u " +
                          "WHERE ' ' + TRANSLATE(a.AUDIT_LOGIN, '\\.-_@()', '       ') + ' ' LIKE '% ' + TRANSLATE(u.UTLST_LOGIN, '\\.-_@()', '       ') + ' %')");
Exiger(comptes.Source > 0 && comptes.Source == comptes.Copie,
    $"SOC-021 CA02 : le compte de l'agent se retrouve dans le journal (AUDIT_LOGIN) : {comptes.Source} lignes dans la source, {comptes.Copie} dans la copie");
var distincts = await Paire("SELECT COUNT(DISTINCT CLIEN_NOM) + COUNT(DISTINCT CLIEN_EMAIL) FROM [{B}].dbo.CLIENT");
var logins = await Paire("SELECT COUNT(DISTINCT UTLST_LOGIN) FROM [{B}].dbo.UTILISATEUR");
var indexLogin = await EntierAsync(copie.Connexion, $"SELECT COUNT(*) FROM [{nomCopie}].sys.indexes WHERE name = 'IFK_UTLST_LOGIN' AND is_unique = 1");
Exiger(distincts.Source == distincts.Copie && logins.Source == logins.Copie && indexLogin == 1,
    $"SOC-021 CA02 : deux personnes ne fusionnent pas : {distincts.Source} noms et courriels de clients distincts (copie {distincts.Copie}), {logins.Source} comptes d'agents (copie {logins.Copie}), index unique IFK_UTLST_LOGIN refait");
var cleLot = await EntierAsync(copie.Connexion, $"SELECT COUNT(*) FROM [{nomCopie}].sys.foreign_keys WHERE name = 'FK_DEMANDE_EDITION_DMNED_LOT_LOT_LOT_NOM' AND is_not_trusted = 0");
var lotsChanges = rapport.Colonnes.FirstOrDefault(c => c.Table == "dbo.LOT" && c.Colonne == "LOT_NOM")?.Modifiees ?? 0;
Exiger(cleLot == 1 && lotsChanges > 0 && clesSource.All(k => rapport.ClesVerifiees.Any(v => v.EndsWith("." + k))),
    $"SOC-021 CA02 : les {clesSource.Count} cles etrangeres sont refaites ET verifiees sur la copie, dont la cle en TEXTE DMNED_LOT -> LOT_NOM ({lotsChanges} noms de lot remplaces des deux cotes)");

// ====================================================================================================
// CA03 -- numeros, dates des affaires, volumes inchanges
// ====================================================================================================
var volumesOk = rapport.Volumes.All(v => lignesSource.GetValueOrDefault(v.Table[4..]) == v.Lignes);
Exiger(volumesOk, $"SOC-021 CA03 : memes volumes : {string.Join(", ", rapport.Volumes.Select(v => $"{v.Table[4..]} {v.Lignes}"))}");
var gardeesDifferentes = new List<string>();
var gardeesComparees = 0;
foreach (var table in nomsExtrait.Order())
{
    var traiteesTable = rapport.Colonnes.Where(c => c.Table == "dbo." + table).Select(c => c.Colonne).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var cols = (await LireAsync(copie.Connexion, $"SELECT name, TYPE_NAME(system_type_id) FROM [{nomV2}].sys.columns WHERE object_id = OBJECT_ID(N'[{nomV2}].dbo.[{table}]') ORDER BY column_id")).Rows
        .Cast<DataRow>().Where(r => !traiteesTable.Contains((string)r[0]) && (string)r[1] is not ("xml" or "text" or "ntext" or "image"))
        .Select(r => $"[{r[0]}]").ToList();
    gardeesComparees += cols.Count;
    var liste = string.Join(", ", cols);
    var ecart = await EntierAsync(copie.Connexion, $"SELECT (SELECT COUNT(*) FROM (SELECT {liste} FROM {S(table)} EXCEPT SELECT {liste} FROM {C(table)}) x) + (SELECT COUNT(*) FROM (SELECT {liste} FROM {C(table)} EXCEPT SELECT {liste} FROM {S(table)}) y)");
    if (ecart > 0) gardeesDifferentes.Add($"{table} ({ecart})");
}
Exiger(gardeesDifferentes.Count == 0,
    $"SOC-021 CA03 : les {gardeesComparees} colonnes gardees (SIREN, numeros de gestion, de facture, de demande, dates des affaires, montants, identifiants) sont identiques ligne a ligne" +
    (gardeesDifferentes.Count == 0 ? "" : $" -- ecarts : {string.Join(", ", gardeesDifferentes)}"));
var identites = await LireAsync(copie.Connexion,
    $"SELECT s.name, CONVERT(decimal(38,0), s.last_value), CONVERT(decimal(38,0), c.last_value) FROM [{nomV2}].sys.identity_columns s " +
    $"JOIN [{nomV2}].sys.tables ts ON ts.object_id = s.object_id JOIN [{nomCopie}].sys.tables tc ON tc.name = ts.name " +
    $"JOIN [{nomCopie}].sys.identity_columns c ON c.object_id = tc.object_id");
Exiger(identites.Rows.Count > 0 && identites.Rows.Cast<DataRow>().All(r => Equals(r[1], r[2])),
    $"SOC-021 CA03 : les compteurs d'identite reprennent a la meme valeur ({identites.Rows.Count} tables, ex. {identites.Rows.Cast<DataRow>().Select(r => $"{r[0]} {r[1]}").FirstOrDefault()})");

// ====================================================================================================
// CA04 -- champs libres vides ou anonymises, et le rapport dit lesquels
// ====================================================================================================
var libres = new (string Table, string Cle, string Colonne)[]
{
    ("AUDIT", "AUDIT_ID_AUDIT", "AUDIT_XML"), ("AUDIT", "AUDIT_ID_AUDIT", "AUDIT_XML_NEW"), ("AUDIT", "AUDIT_ID_AUDIT", "AUDIT_FULL_TEXT_MODIF"),
    ("EMAIL_ENVOI_TRACE", "EMTRC_ID_EMTRC", "EMTRC_TO"), ("EMAIL_ENVOI_TRACE", "EMTRC_ID_EMTRC", "EMTRC_FROM"), ("EMAIL_ENVOI_TRACE", "EMTRC_ID_EMTRC", "EMTRC_SUJET"),
    ("EMAIL_ENVOI_TRACE", "EMTRC_ID_EMTRC", "EMTRC_COMMAND_LINE"), ("EMAIL_ENVOYE", "EMAILENV_ID_EMAILENV", "EMAILENV_CORPS"),
    ("EMAIL_ENVOYE", "EMAILENV_ID_EMAILENV", "EMAILENV_SUJET"), ("CLIENT", "CLIEN_ID_CLIEN", "CLIEN_BLOC_NOTES"),
    ("EMAIL_ENVOI_TRACE", "EMTRC_ID_EMTRC", "EMTRC_BCC"), ("EMAIL_ENVOI_TRACE", "EMTRC_ID_EMTRC", "EMTRC_REPLY"),
    ("PARTIE", "PRT_ID_PRT", "PRT_TEXTE_ESQUALITE"), ("PARTIE", "PRT_ID_PRT", "PRT_TEXTE_ASSISTANCE"),
    ("DIRIGEANT_PP", "DRGPP_ID_DRGPP", "DRGPP_TEXTE_RPPP"), ("FACTURE", "FACTU_NUMERO_FACTURE", "FACTU_COMMENTAIRE"),
};
var texteRapport = File.Exists(fichierRapport) ? await File.ReadAllTextAsync(fichierRapport) : "";
var sectionLibres = texteRapport.Contains("## Champs libres") ? texteRapport[texteRapport.IndexOf("## Champs libres", StringComparison.Ordinal)..] : "";
sectionLibres = sectionLibres.Contains("\n## ", StringComparison.Ordinal) ? sectionLibres[..sectionLibres.IndexOf("\n## ", 5, StringComparison.Ordinal)] : sectionLibres;
foreach (var (table, cle, colonne) in libres)
{
    var paires = await LireAsync(copie.Connexion, $"SELECT CONVERT(nvarchar(max), s.[{colonne}]), CONVERT(nvarchar(max), c.[{colonne}]) FROM {S(table)} s JOIN {C(table)} c ON c.[{cle}] = s.[{cle}] " +
                                                   $"WHERE s.[{colonne}] IS NOT NULL");
    var n = paires.Rows.Cast<DataRow>().Count(r => ((string)r[0]).Any(char.IsLetter));
    var intacts = paires.Rows.Cast<DataRow>().Count(r => ((string)r[0]).Any(char.IsLetter) && r[1] is string vc && vc == (string)r[0]);
    var ligneRapport = sectionLibres.Split('\n').FirstOrDefault(l => l.StartsWith($"| dbo.{table}.{colonne} |", StringComparison.Ordinal));
    if (n == 0) { Info($"CA04 : {table}.{colonne} vide dans l'extrait"); continue; }
    Exiger(intacts == 0 && ligneRapport is not null && (ligneRapport.Contains("anonymisé") || ligneRapport.Contains("vidé")),
        $"SOC-021 CA04 : champ libre {table}.{colonne} : {n} valeurs, {intacts} intacte(s) ; le rapport le liste : « {ligneRapport?.Split('|')[2].Trim() ?? "ABSENT"} »");
}
var xmlGarde = await Paire("SELECT COUNT(*) FROM [{B}].dbo.AUDIT WHERE AUDIT_XML.value('local-name(/*[1])', 'varchar(50)') IN ('deleted', 'inserted') " +
                           "AND AUDIT_XML.value('(/*/@FACTU_NUMERO_FACTURE)[1]', 'varchar(25)') = AUDIT_ID_TABLE");
var xmlNomsChanges = await EntierAsync(copie.Connexion,
    $"SELECT COUNT(*) FROM {S("AUDIT")} s JOIN {C("AUDIT")} c ON c.AUDIT_ID_AUDIT = s.AUDIT_ID_AUDIT " +
    "WHERE s.AUDIT_XML.value('(/*/@FACTU_DESIGNATION_FACTURATION)[1]', 'varchar(255)') = c.AUDIT_XML.value('(/*/@FACTU_DESIGNATION_FACTURATION)[1]', 'varchar(255)')");
Exiger(xmlGarde.Source > 0 && xmlGarde.Source == xmlGarde.Copie && xmlNomsChanges == 0,
    $"SOC-021 CA04 : le XML du journal garde sa forme (element, numero de facture : {xmlGarde.Source} lignes, copie {xmlGarde.Copie}) et aucune designation d'origine ({xmlNomsChanges})");

// ====================================================================================================
// CA05 -- facture signee dont le client est anonymise : copie marquee « chaines invalidees »
// ====================================================================================================
var signees = await EntierAsync(copie.Connexion, $"SELECT COUNT(*) FROM {S("FACTURE")} WHERE (FACTU_SIGN_SEQ IS NOT NULL OR FACTU_SIGN_ELEC IS NOT NULL) " +
                                                 "AND (FACTU_DESIGNATION_FACTURATION LIKE '%[0-9A-Za-z]%' OR FACTU_LIGNE_ADRESSE_FACTURATION LIKE '%[0-9A-Za-z]%' " +
                                                 "OR FACTU_SUITE_ADRESSE_FACTURATION LIKE '%[0-9A-Za-z]%' OR FACTU_ALATTENTION LIKE '%[0-9A-Za-z]%' " +
                                                 "OR FACTU_REFERENCES_CLIENT LIKE '%[0-9A-Za-z]%' OR FACTU_COMMENTAIRE LIKE '%[0-9A-Za-z]%')");
var marque = await LireAsync(copie.Connexion, $"SELECT ChainesInvalidees, Motif, LignesSigneesTouchees, TablesSigneesTouchees FROM [{nomCopie}].anonymisation.Copie");
var propriete = (string?)await ScalaireAsync(copie.Connexion, $"SELECT CONVERT(nvarchar(200), value) FROM [{nomCopie}].sys.extended_properties WHERE class = 0 AND name = 'RIGV3_ANONYMISATION'");
var factureTouchee = rapport.TablesSigneesTouchees.FirstOrDefault(t => t.Table == "dbo.FACTURE").Lignes;
Exiger(signees > 0 && marque.Rows.Count == 1 && (bool)marque.Rows[0][0] && (string)marque.Rows[0][1] == Rapport.MotifChaines
       && factureTouchee == signees && propriete == Rapport.MotifChaines && texteRapport.Contains("INVALIDÉES par l'anonymisation"),
    $"SOC-021 CA05 : {signees} factures signees au client anonymise -> copie marquee « {marque.Rows[0][1]} » (anonymisation.Copie : {marque.Rows[0][3]} ; " +
    $"propriete de base RIGV3_ANONYMISATION : « {propriete} » ; dit en tete du rapport)");

// ====================================================================================================
// CA06 -- un controle automatique ne trouve aucune valeur reelle d'un echantillon ; comparaison chez la source
// ====================================================================================================
var controle = await ControleCopie.ControlerAsync(v2.Connexion, copie.Connexion, null, 200);
var genresAdmis = new HashSet<string> { "empreinte", "nombre", "cle primaire" };
Exiger(controle.Vert && controle.EchantillonValeurs > 0 && controle.EchantillonMots > 0 && controle.GenresLusSource.IsSubsetOf(genresAdmis),
    $"SOC-021 CA06 : controle de la copie : {controle.ColonnesControlees} colonnes personnelles, {controle.EchantillonValeurs} valeurs et {controle.EchantillonMots} mots reels tires, " +
    $"retrouves : {(controle.Fuites.Count == 0 ? "aucun" : string.Join(", ", controle.Fuites))} ; volumes {controle.TablesComparees} tables ; {controle.LignesComparees} lignes gardees comparees ; " +
    $"sorti de la source : {string.Join(", ", controle.GenresLusSource)} (jamais une valeur)");

// CONTRE-EPREUVE ROUGE : une copie ou CLIENT.CLIEN_NOM est laisse en clair (remplacement « Garder »). Le controle,
// qui ne se fie pas a la liste de la copie, DOIT trouver les noms reels ; sans quoi un vert ne prouverait rien.
await using var saboteur = await BaseJetable.CreerAsync("SocleAnonymisationSabot");
var rapportSabot = await CopieAnonymisee.CopierAsync(new OptionsCopie
{
    Source = v2.Connexion, Cible = saboteur.Connexion, Graine = "SOC-021",
    Remplacements = [new("CLIENT.CLIEN_NOM", Traitement.Garder)],
});
var controleSabot = await ControleCopie.ControlerAsync(v2.Connexion, saboteur.Connexion, null, 200);
Exiger(!controleSabot.Vert && controleSabot.Fuites.Any(f => f.StartsWith("dbo.CLIENT.CLIEN_NOM ", StringComparison.Ordinal)),
    $"SOC-021 CA06 (contre-epreuve) : CLIEN_NOM laisse en clair -> le controle le trouve : {string.Join(", ", controleSabot.Fuites)}");
// La meme graine donne la meme copie (regles deterministes) : hors CLIEN_NOM, la copie sabotee est celle du banc.
var memes = new List<string>();
foreach (var (table, cols) in new[] { ("CLIENT", "CLIEN_ID_CLIEN, CLIEN_PRENOM, CLIEN_EMAIL, CLIEN_TELEPHONE, CLIEN_DATE_NAISSANCE"),
                                      ("FACTURE", "FACTU_NUMERO_FACTURE, FACTU_DESIGNATION_FACTURATION, FACTU_LIGNE_ADRESSE_FACTURATION"),
                                      ("PARTIE", "PRT_ID_PRT, PRT_NOM, PRT_DESIGNATION, PRT_ADRESSE_MAIL"),
                                      ("AUDIT", "AUDIT_ID_AUDIT, CONVERT(nvarchar(max), AUDIT_XML) AS AUDIT_XML, AUDIT_LOGIN") })
{
    var nomSabot = new SqlConnectionStringBuilder(saboteur.Connexion).InitialCatalog;
    var ecart = await EntierAsync(copie.Connexion, $"SELECT COUNT(*) FROM (SELECT {cols} FROM {C(table)} EXCEPT SELECT {cols} FROM [{nomSabot}].dbo.[{table}]) x");
    if (ecart > 0) memes.Add($"{table} ({ecart})");
}
Exiger(memes.Count == 0, $"SOC-021 T02 : regles deterministes : meme graine, memes valeurs fictives dans les deux copies (CLIENT, FACTURE, PARTIE, AUDIT){(memes.Count == 0 ? "" : " -- ecarts : " + string.Join(", ", memes))}");

// ====================================================================================================
// CA07 -- la source n'est lue qu'en lecture seule, avec l'accord donne pour elle ; on n'ecrit que la copie
// ====================================================================================================
Exiger(avant == apres && rapport.EcrituresSourceAvant == rapport.EcrituresSourceApres && rapport.EcrituresSourceAvant is not null,
    $"SOC-021 CA07, T05 : aucune ecriture sur la source pendant la copie : journal du serveur (user_updates) {avant.Ecritures} avant, {apres.Ecritures} apres ; " +
    $"contenu des {nomsExtrait.Count} tables identique (empreinte) ; {rapport.RequetesSource} requetes, toutes par la porte de lecture");
var sortie = new StringWriter();
var erreurs = new StringWriter();
var horsPoste = $"Server=SQL-DEV\\DEV;Database=RIG_ANONYME_{Guid.NewGuid():N}"[..60] + ";Integrated Security=True;TrustServerCertificate=True";
var nomInterdit = $"{BaseJetable.Prefixe}SocleAnonymisationRefus_{Guid.NewGuid():N}"[..60];
var cibleInterdite = new SqlConnectionStringBuilder(copie.Connexion) { InitialCatalog = nomInterdit }.ConnectionString;
var chronoRefus = Stopwatch.StartNew();
var codeCible = await Programme.ExecuterAsync(["copier", "--source", v2.Connexion, "--cible", horsPoste, "--rapport", Path.Combine(dossier, "refus.md")], sortie, erreurs);
var refusCible = erreurs.ToString();
Exiger(codeCible == 1 && refusCible.Contains("Serveur refuse") && chronoRefus.Elapsed < TimeSpan.FromSeconds(5),
    $"SOC-021 CA07 : une copie vers SQL-DEV est refusee AVANT toute connexion (code {codeCible}, {chronoRefus.ElapsedMilliseconds} ms) ; jumeau accepte : la copie vers ce poste, ci-dessus");
erreurs.GetStringBuilder().Clear();
var codeSansAccord = await Programme.ExecuterAsync(["copier", "--source", chaineRigDev, "--cible", cibleInterdite, "--rapport", Path.Combine(dossier, "refus.md")], sortie, erreurs);
var refusAccord = erreurs.ToString();
erreurs.GetStringBuilder().Clear();
var codeAutreAccord = await Programme.ExecuterAsync(["copier", "--source", chaineRigDev, "--cible", cibleInterdite, "--accord", "RIG_AMIENS", "--rapport", Path.Combine(dossier, "refus.md")], sortie, erreurs);
var refusAutre = erreurs.ToString();
erreurs.GetStringBuilder().Clear();
var codeGraine = await Programme.ExecuterAsync(["copier", "--source", chaineRigDev, "--cible", cibleInterdite, "--accord", baseRigDev, "--graine", "x", "--rapport", Path.Combine(dossier, "refus.md")], sortie, erreurs);
var refusGraine = erreurs.ToString();
Exiger(codeSansAccord == 1 && refusAccord.Contains($"--accord {baseRigDev}") && codeAutreAccord == 1 && refusAutre.Contains("RIG_AMIENS")
       && codeGraine == 1 && refusGraine.Contains("Graine refusee") && !await BaseJetable.ExisteAsync(nomInterdit),
    $"SOC-021 CA07 : RIG_DEV (hors poste) refuse sans accord, avec l'accord d'une autre base (RIG_AMIENS), et avec une graine ; aucune base copie creee");
var inventaire = Path.Combine(dossier, "inventaire-rig-dev.md");
var codeInventaire = await Programme.ExecuterAsync(["inventaire", "--source", chaineRigDev, "--accord", baseRigDev, "--sortie", inventaire], sortie, erreurs);
Exiger(codeInventaire == 0 && File.Exists(inventaire),
    $"SOC-021 CA07 (jumeau accepte), T01 : avec l'accord donne pour {baseRigDev}, l'outil la lit -- inventaire du schema (aucune donnee) : {sortie.ToString().Trim().Split('\n').Last()}");
await using (var porte = await PorteSource.OuvrirAsync(v2.Connexion, null, CancellationToken.None))
{
    string? refus = null;
    try { await using var _ = await porte.LireAsync("UPDATE dbo.CLIENT SET CLIEN_NOM = 'X'", CancellationToken.None); }
    catch (InvalidOperationException e) { refus = e.Message; }
    var compte = await porte.ValeurAsync("SELECT COUNT(*) FROM dbo.CLIENT", CancellationToken.None);
    Exiger(refus is not null && Convert.ToInt64(compte) == lignesSource["CLIENT"],
        $"SOC-021 CA07 : la porte de la source refuse une ecriture (« {refus?[..Math.Min(60, refus.Length)]}… ») ; jumeau : un SELECT passe ({compte} clients)");
}
erreurs.GetStringBuilder().Clear();
var codeMeme = await Programme.ExecuterAsync(["copier", "--source", v2.Connexion, "--cible", v2.Connexion, "--rapport", Path.Combine(dossier, "refus.md")], sortie, erreurs);
var refusMeme = erreurs.ToString();
erreurs.GetStringBuilder().Clear();
var codePleine = await Programme.ExecuterAsync(["copier", "--source", v2.Connexion, "--cible", copie.Connexion, "--rapport", Path.Combine(dossier, "refus.md")], sortie, erreurs);
var refusPleine = erreurs.ToString();
Exiger(codeMeme == 1 && refusMeme.Contains("meme base") && codePleine == 1 && refusPleine.Contains("n'est pas vide"),
    "SOC-021 CA07 : l'outil n'ecrit que dans une copie neuve : refus de copier sur la source elle-meme, et sur une base deja remplie (jumeau accepte : base vide, ci-dessus)");

// Une copie qui ne touche aucune ligne signee porte « chaines intactes » (jumeau de CA05).
await using var sansSignature = await BaseJetable.CreerAsync("SocleAnonymisationLots");
var rapportLots = await CopieAnonymisee.CopierAsync(new OptionsCopie { Source = v2.Connexion, Cible = sansSignature.Connexion, Tables = ["LOT", "DEMANDE_EDITION"] });
var marqueLots = (bool?)await ScalaireAsync(sansSignature.Connexion, "SELECT ChainesInvalidees FROM anonymisation.Copie");
// DEMANDE_EDITION porte aussi une cle vers elle-meme (demande reeditee) : toutes doivent etre verifiees, aucune posee sans.
Exiger(marqueLots == false && !rapportLots.ChainesInvalidees && !rapportLots.Reproductible && rapportLots.ClesNonVerifiees.Count == 0
       && rapportLots.ClesVerifiees.Any(c => c.EndsWith(".FK_DEMANDE_EDITION_DMNED_LOT_LOT_LOT_NOM", StringComparison.Ordinal)),
    $"SOC-021 CA05 (jumeau) : une copie des seuls lots d'editions (aucune ligne signee) porte « chaines intactes » (marque : {marqueLots}, " +
    $"tables signees touchees : {(rapportLots.TablesSigneesTouchees.Count == 0 ? "aucune" : string.Join(", ", rapportLots.TablesSigneesTouchees.Select(t => t.Table)))}) ; " +
    $"cle aleatoire (reproductible : {rapportLots.Reproductible}), cles verifiees : {string.Join(", ", rapportLots.ClesVerifiees)}");

Reste("SOC-021 T01 : relecture de l'inventaire par le delegue a la protection des donnees (fichier inventaire-rig-dev.md a cote du banc) -- hors de ce poste");
Reste("SOC-021 T03, CA07 : lancer l'outil base par base avec l'accord donne pour chaque base de production (40 greffes, SQL-PROD) -- aucun accord sur ce poste");
Reste("SOC-021 T05 : sur un serveur de production, d'autres sessions ecrivent : le journal du serveur ne prouve plus seul l'absence d'ecriture de l'outil (la porte de lecture reste la garantie)");
Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "SocleAnonymisationCheck : VERT" : $"SocleAnonymisationCheck : {echecs.Count} ECHEC(S)");
return echecs.Count == 0 ? 0 : 1;

// ----------------------------------------------------------------------------------------------------
static string Forme(string s) => new([.. s.Select(c => char.IsDigit(c) ? '9' : char.IsLetter(c) ? (char.IsLower(c) ? 'a' : 'A') : c)]);

static async Task ExecuterAsync(string chaine, string sql)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    await cmd.ExecuteNonQueryAsync();
}
static async Task<object?> ScalaireAsync(string chaine, string sql)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    var v = await cmd.ExecuteScalarAsync();
    return v is DBNull ? null : v;
}
static async Task<long> EntierAsync(string chaine, string sql) => Convert.ToInt64(await ScalaireAsync(chaine, sql) ?? 0);
static async Task<DataTable> LireAsync(string chaine, string sql)
{
    await using var cn = new SqlConnection(chaine);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
    var t = new DataTable();
    await using var l = await cmd.ExecuteReaderAsync();
    t.Load(l);
    return t;
}

/// <summary>
/// La table telle que RIG_DEV la declare (colonnes, types, identite, cle primaire), ecrite par le banc LUI-MEME
/// (independamment de la reecriture de l'outil, Outils/Anonymisation/Schema.cs, qu'il controle).
/// </summary>
static async Task<string> DdlBancAsync(PorteSource rigDev, string table)
{
    var colonnes = await rigDev.LignesAsync(
        "SELECT c.name, TYPE_NAME(c.system_type_id), CONVERT(int, c.max_length), CONVERT(int, c.precision), CONVERT(int, c.scale), c.is_nullable, c.is_identity " +
        $"FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.{table}') AND c.is_computed = 0 ORDER BY c.column_id", CancellationToken.None);
    var cle = await rigDev.LignesAsync(
        "SELECT COL_NAME(ic.object_id, ic.column_id) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " +
        $"WHERE i.is_primary_key = 1 AND i.object_id = OBJECT_ID('dbo.{table}') ORDER BY ic.key_ordinal", CancellationToken.None);
    var defs = colonnes.Select(c =>
    {
        var (nom, type, lg, pr, ec) = ((string)c[0]!, (string)c[1]!, (int)c[2]!, (int)c[3]!, (int)c[4]!);
        var t = type switch
        {
            "varchar" or "char" or "varbinary" or "binary" => $"{type}({(lg < 0 ? "max" : lg)})",
            "nvarchar" or "nchar" => $"{type}({(lg < 0 ? "max" : lg / 2)})",
            "decimal" or "numeric" => $"{type}({pr},{ec})",
            "datetime2" or "time" or "datetimeoffset" => $"{type}({ec})",
            "timestamp" => "binary(8)",
            _ => type,
        };
        return $"[{nom}] {t}{((bool)c[6]! ? " IDENTITY(1,1)" : "")}{((bool)c[5]! ? " NULL" : " NOT NULL")}";
    });
    var pk = cle.Count > 0 ? $", CONSTRAINT [PK_{table}] PRIMARY KEY CLUSTERED ({string.Join(", ", cle.Select(k => $"[{k[0]}]"))})" : "";
    return $"CREATE TABLE dbo.[{table}] ({string.Join(", ", defs)}{pk});";
}
