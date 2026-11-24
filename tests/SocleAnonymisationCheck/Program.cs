using System.Data;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
//     envoyes et leurs traces ; lots d'editions et des demandes qui les citent (cle etrangere en TEXTE) ; depuis le
//     refus du 2026-11-21, les prenoms d'etat civil (CASIER_JUDICIAIRE, INSCRIT_PP, BENEFICIAIRE_EFFECTIF et son
//     historique, REPRESENTANT_PM, BODACC_RCS_XML, NOTIFICATION_RCS_XML_DETAIL) et des noms de fichiers.
//     Il mesure aussi RIG_DEV ENTIER : aucune colonne que les regles gardent ne doit porter de prenoms.
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

    // Un nom de fichier : chaque mot remplace, l'extension connue gardee ; un « .JEAN » final n'est pas une extension.
    var fichier = p.Fichier("Kbis DUPONT Jean.pdf");
    var sansExtension = p.Fichier("dupont.jean");
    Exiger(fichier.EndsWith(".pdf", StringComparison.Ordinal) && Forme(fichier) == Forme("Kbis DUPONT Jean.pdf") && fichier.StartsWith(p.Texte("Kbis DUPONT Jean"), StringComparison.Ordinal)
           && sansExtension == p.Texte("dupont.jean") && p.Fichier(@"\\srv\ged\2024\A_B.DOCX; C.xml").EndsWith(".DOCX; " + p.Mot("C") + ".xml", StringComparison.Ordinal),
        $"SOC-021 CA01 : nom de fichier « Kbis DUPONT Jean.pdf » -> « {fichier} » (memes mots que les noms, extension gardee) ; « dupont.jean » -> « {sansExtension} » (pas d'extension connue : tout remplace)");
}

// ====================================================================================================
// 0 bis. Les regles sur les colonnes que la mesure de RIG_DEV a designees (2026-11-21, refus du chef au lot 6) :
// au point d'appel (CatalogueRegles.Pour), chaque refus de « garder » avec son jumeau garde (meme segment, autre cote).
// ====================================================================================================
{
    var cat = new CatalogueRegles();
    var attendues = new (string Table, string Colonne, string Type, string Traitement, string Pourquoi)[]
    {
        // Prenoms d'etat civil : 89 a 99 % des valeurs tirees portent un prenom de la base (mesure RIG_DEV).
        ("CASIER_JUDICIAIRE", "CSRJD_PRENOMS_ETAT_CIVIL", "varchar", "Mots", "prenoms d'etat civil (« ETAT » suivi de « CIVIL » n'est pas un segment technique)"),
        ("INSCRIT_PP", "INSPP_PRENOMS_ETAT_CIVIL", "varchar", "Mots", ""), ("INSCRIT_PP", "INSPP_PRENOMS_ETAT_CIVIL_ANC", "varchar", "Mots", ""),
        ("BENEFICIAIRE_EFFECTIF", "BENEF_PRENOM_ETAT_CIVIL", "varchar", "Mots", ""), ("BENEFICIAIRE_EFFECTIF_HISTO", "BENEFHI_PRENOM_ETAT_CIVIL", "varchar", "Mots", ""),
        ("REPRESENTANT_PM", "RPPM_PRENOMS_ETAT_CIVIL", "varchar", "Mots", ""), ("BODACC_RCS_XML", "BDCRCX_PRENOMS_ETAT_CIVIL", "varchar", "Mots", ""),
        ("NOTIFICATION_RCS_XML_DETAIL", "NTXDT_PRENOMS_ETAT_CIVIL", "varchar", "Mots", ""), ("NOTIFICATION_RCS_XML_DETAIL", "NTXDT_PRENOMS_ETAT_CIVIL_ANC", "varchar", "Mots", ""),
        ("UNE_TABLE", "X_ETAT_CIVIL", "varchar", "Mots", "l'etat civil seul"),
        // Jumeau : « ETAT » sans « CIVIL » reste technique (etat du nom de domaine : 3 valeurs, des codes).
        ("ETABLISSEMENT", "ETBLS_ETAT_NOM_DOMAINE", "varchar", "Garder", "etat d'un nom de domaine (code)"),
        // Jumeau : la date du changement de prenom est une date, pas un prenom.
        ("NOTIFICATION_RCS_XML_DETAIL", "NTXDT_PRENOMS_ETAT_CIVIL_DT", "datetime", "Garder", "date du changement"),
        // Un secret n'est jamais leve par un segment technique : INFO_GREFFE.PWD_SQL est un mot de passe.
        ("INFO_GREFFE", "PWD_SQL", "varchar", "Vider", "mot de passe SQL"),
        // Denomination statutaire d'une personne morale (25 % de prenoms, 61 % de noms) : « STATUT » y est « statutaire ».
        ("INSCRIT_PM", "INSPM_DENOMINATION_STATUT", "varchar", "Mots", "denomination statutaire"),
        ("DIRIGEANT_PM", "DRGPM_DENOMINATION_STATUT", "varchar", "Mots", ""),
        // Nom de domaine internet d'un etablissement (le nom de la societe s'y lit).
        ("ETABLISSEMENT", "ETBLS_NOM_DOMAINE", "varchar", "Mots", "nom de domaine internet"),
        // Les 38 colonnes NOM_FICHIER : 7 portent des noms sur RIG_DEV (jusqu'a 37 % de prenoms), toutes traitees, extension gardee.
        ("CREANCE_TRANSITOIRE", "CRTRA_NOM_FICHIER", "varchar", "Fichier", "nom de fichier"), ("DEMAT_CREFO_PJ", "DMCOPJ_NOM_FICHIER", "varchar", "Fichier", ""),
        ("PROCEDURE_COLLECTIVE", "PRCLL_NOM_AFFAIRE_FICHIER", "varchar", "Fichier", ""), ("GED_RIG", "GDRG_NOM_FICHIER", "varchar", "Fichier", ""),
        ("DEMAT_DCA", "DMTDCA_NOM_FICHIER_RAPPORT_CAC", "varchar", "Fichier", ""), ("CFE_LIASSE_XML", "LIAXML_PC_NOM_FICHIER", "varchar", "Fichier", ""),
        // Jumeaux gardes : segments techniques relus (parametrage), mesures a 0-13 % de prenoms sur RIG_DEV.
        ("ADAPTAT_LIGNE_EDIT", "ADLGE_NOM_LOGIQUE", "varchar", "Garder", "nom logique d'une etape"), ("DEMANDE_EDITION", "DMNED_TYPE_DESTINATAIRE", "varchar", "Garder", "type de destinataire"),
        ("ULT", "ULT_TEXTE_PARAMETRE", "varchar", "Garder", "parametre d'une tache"),
        // Noms d'imprimantes : 4 sur 26 portent le nom d'un agent (RIG_DEV) ; les quatre colonnes prennent le meme faux nom.
        ("GROUPE_IMPRESSION", "GPIMP_NOM_IMPRIMANTE", "varchar", "Mots", "nom d'imprimante"), ("TAG_POSTSCRIPT", "TAGPS_NOM_IMPRIMANTE", "varchar", "Mots", ""),
        ("LOT", "LOT_NOM_IMPRIMANTE", "varchar", "Mots", ""), ("IMPRIMANTE_EDITION", "IMPED_NOM_LOGIQUE", "varchar", "Mots", ""),
    };
    var fausses = attendues.Select(a => (a, r: cat.Pour(a.Table, a.Colonne, a.Type))).Where(x => x.r.Traitement.ToString() != x.a.Traitement)
        .Select(x => $"{x.a.Table}.{x.a.Colonne} : {x.r.Traitement} au lieu de {x.a.Traitement} ({x.r.Origine})").ToList();
    Exiger(fausses.Count == 0,
        $"SOC-021 CA01 : {attendues.Length} regles mesurees sur RIG_DEV -- prenoms d'etat civil, mot de passe SQL, denomination statutaire, nom de domaine et noms de fichiers traites ; " +
        $"noms d'imprimantes traites ; jumeaux gardes (etat d'un domaine, date d'un changement, nom logique, type, parametre){(fausses.Count == 0 ? "" : " -- FAUSSES : " + string.Join(" ; ", fausses))}");
    var relire = new[] { ("CASIER_JUDICIAIRE", "CSRJD_PRENOMS_ETAT_CIVIL", "varchar"), ("INFO_GREFFE", "PWD_SQL", "varchar"), ("CREANCE_TRANSITOIRE", "CRTRA_NOM_FICHIER", "varchar") }
        .Where(x => cat.Suspicion(x.Item1, x.Item2, x.Item3) is not null).ToList();
    Exiger(relire.Count == 0 && cat.Suspicion("ADAPTAT_LIGNE_EDIT", "ADLGE_NOM_LOGIQUE", "varchar") is not null
           && cat.Suspicion("DEMANDE_EDITION", "DMNED_ID_EMAIL_ENVOYE", "int") is null && cat.Suspicion("CASIER_JUDICIAIRE", "CSRJD_DATE_NAISS_FORCEE", "bit") is null,
        "SOC-021 T01 : la liste « gardees, a relire » de l'inventaire ne porte plus ces colonnes traitees ; elle garde les segments techniques en texte (nom logique) " +
        "et laisse les entiers et les indicateurs (une cle, un bit ne portent pas de nom)");
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
    // Les prenoms d'etat civil (refus du chef, 2026-11-21) : remplis sur RIG_DEV dans CASIER_JUDICIAIRE (173 812 lignes),
    // INSCRIT_PP (77 627), BENEFICIAIRE_EFFECTIF (74 535), REPRESENTANT_PM (6 183), leurs historiques et les echanges RCS.
    ("CASIER_JUDICIAIRE", Remplis("CASIER_JUDICIAIRE", "CSRJD_ID_CSRJD", "CSRJD_PRENOMS_ETAT_CIVIL")),
    ("INSCRIT_PP", Remplis("INSCRIT_PP", "INSPP_ID_INSPP", "INSPP_PRENOMS_ETAT_CIVIL") + " OR INSPP_PRENOMS_ETAT_CIVIL_ANC LIKE '%[a-z]%'"),
    ("BENEFICIAIRE_EFFECTIF", Remplis("BENEFICIAIRE_EFFECTIF", "BENEF_ID_BENEF", "BENEF_PRENOM_ETAT_CIVIL")),
    ("BENEFICIAIRE_EFFECTIF_HISTO", Remplis("BENEFICIAIRE_EFFECTIF_HISTO", "BENEFHI_ID_BENEFHI", "BENEFHI_PRENOM_ETAT_CIVIL")),
    ("REPRESENTANT_PM", Remplis("REPRESENTANT_PM", "RPPM_ID_RPPM", "RPPM_PRENOMS_ETAT_CIVIL")),
    ("BODACC_RCS_XML", Remplis("BODACC_RCS_XML", "BDCRCX_ID_BDCRCX", "BDCRCX_PRENOMS_ETAT_CIVIL")),
    ("NOTIFICATION_RCS_XML_DETAIL", Remplis("NOTIFICATION_RCS_XML_DETAIL", "NTXDT_ID_NTXDT", "NTXDT_PRENOMS_ETAT_CIVIL") +
                                    " OR NTXDT_ID_NTXDT IN (SELECT TOP 100 NTXDT_ID_NTXDT FROM dbo.NOTIFICATION_RCS_XML_DETAIL WITH (NOLOCK) ORDER BY NTXDT_ID_NTXDT DESC)"),
    // Un nom de fichier qui porte des noms (24 % de prenoms, 37 % de noms sur RIG_DEV) ; extension a garder.
    ("CREANCE_TRANSITOIRE", Remplis("CREANCE_TRANSITOIRE", "CRTRA_ID_CRTRA", "CRTRA_NOM_FICHIER")),
    // Des DOCUMENTS (regle des binaires) : les 40 derniers documents de la GED de 200 Ko au plus (RIG_DEV, 2026-11-23 :
    // 38 ZIP et 2 XML ; GDRG_FICHIER_PDF et _PS y sont NULL) et les 20 derniers Kbis en PDF (7 a 20 Ko).
    ("GED_RIG", "GDRG_NUM_DOCUMENT IN (SELECT TOP 40 GDRG_NUM_DOCUMENT FROM dbo.GED_RIG WITH (NOLOCK) WHERE GDRG_FICHIER IS NOT NULL " +
                "AND DATALENGTH(GDRG_FICHIER) BETWEEN 1 AND 200000 ORDER BY GDRG_NUM_DOCUMENT DESC)"),
    ("KBIS_XML", "KBIS_ID_KBIS IN (SELECT TOP 20 KBIS_ID_KBIS FROM dbo.KBIS_XML WITH (NOLOCK) WHERE KBIS_PDF IS NOT NULL ORDER BY KBIS_ID_KBIS DESC)"),
};
// Les 1 500 dernieres lignes ou le champ porte des lettres : assez de personnes pour que les prenoms courants s'y repetent
// (le lexique du controle ne retient qu'un mot vu 3 fois dans 2 tables ; a 300 lignes, 81 prenoms seulement).
static string Remplis(string table, string cle, string colonne)
    => $"{cle} IN (SELECT TOP 1500 {cle} FROM dbo.{table} WITH (NOLOCK) WHERE {colonne} LIKE '%[a-z]%' ORDER BY {cle} DESC)";
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
    ("prenom", "CASIER_JUDICIAIRE", "CSRJD_ID_CSRJD", "CSRJD_PRENOMS_ETAT_CIVIL"), ("prenom", "INSCRIT_PP", "INSPP_ID_INSPP", "INSPP_PRENOMS_ETAT_CIVIL"),
    ("prenom", "INSCRIT_PP", "INSPP_ID_INSPP", "INSPP_PRENOMS_ETAT_CIVIL_ANC"), ("prenom", "BENEFICIAIRE_EFFECTIF", "BENEF_ID_BENEF", "BENEF_PRENOM_ETAT_CIVIL"),
    ("prenom", "BENEFICIAIRE_EFFECTIF_HISTO", "BENEFHI_ID_BENEFHI", "BENEFHI_PRENOM_ETAT_CIVIL"), ("prenom", "REPRESENTANT_PM", "RPPM_ID_RPPM", "RPPM_PRENOMS_ETAT_CIVIL"),
    ("prenom", "BODACC_RCS_XML", "BDCRCX_ID_BDCRCX", "BDCRCX_PRENOMS_ETAT_CIVIL"), ("prenom", "NOTIFICATION_RCS_XML_DETAIL", "NTXDT_ID_NTXDT", "NTXDT_PRENOMS_ETAT_CIVIL"),
    ("fichier", "CREANCE_TRANSITOIRE", "CRTRA_ID_CRTRA", "CRTRA_NOM_FICHIER"),
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
        // Nom de fichier : une extension connue (« .pdf », « .DOC ») reste en fin de valeur.
        if (genre == "fichier" && System.Text.RegularExpressions.Regex.Match(vs.TrimEnd(), @"\.(pdf|docx?|xml|txt|zip)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } ext
            && !vc.TrimEnd().EndsWith(ext.Value, StringComparison.Ordinal)) autre++;
    }
    parGenre[genre] = parGenre.GetValueOrDefault(genre) + n;
    if (n == 0) { Info($"CA01 : {table}.{colonne} vide dans l'extrait de RIG_DEV"); continue; }
    Exiger(restees == 0 && longueur == 0 && forme == 0 && autre == 0,
        $"SOC-021 CA01 : {table}.{colonne} ({genre}) : {n} valeurs, {restees} restee(s), {longueur} longueur(s) changee(s), {forme} forme(s) changee(s)" +
        (genre == "courriel" ? $", {autre} courriel(s) sans @ ou extension changee" : genre == "telephone" ? $", {autre} indicatif(s) change(s)" :
         genre == "fichier" ? $", {autre} extension(s) changee(s)" : ""));
}
Exiger(new[] { "nom", "prenom", "naissance", "adresse", "courriel", "telephone", "fichier" }.All(g => parGenre.GetValueOrDefault(g) > 0),
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
var genresAdmis = new HashSet<string> { "empreinte", "nombre", "cle primaire", "format et taille" };
Exiger(controle.Vert && controle.EchantillonValeurs > 0 && controle.EchantillonMots > 0 && controle.GenresLusSource.IsSubsetOf(genresAdmis),
    $"SOC-021 CA06 : controle de la copie : {controle.ColonnesControlees} colonnes personnelles, {controle.EchantillonValeurs} valeurs et {controle.EchantillonMots} mots reels tires, " +
    $"retrouves : {(controle.Fuites.Count == 0 ? "aucun" : string.Join(", ", controle.Fuites))} ; volumes {controle.TablesComparees} tables ; {controle.LignesComparees} lignes gardees comparees ; " +
    $"sorti de la source : {string.Join(", ", controle.GenresLusSource)} (jamais une valeur)");
// fix-ok: refus du chef (2026-11-21) -- le controle ne tirait que des colonnes DEJA traitees : une regle fausse (« ETAT »
// pris pour technique) laissait 173 812 prenoms de CASIER_JUDICIAIRE en clair sans qu'il le voie. Mesure RIG_DEV : prenoms
// d'etat civil 79-100 % de valeurs a prenom frequent, colonnes techniques gardees 0-37 % (lexique a 2 tables).
var gardees = controle.Gardees.Where(g => g.Valeurs > 0).ToList();
Exiger(gardees.Count > 0 && controle.LexiquePrenoms > 0 && gardees.All(g => !g.Prenoms),
    $"SOC-021 CA06 : le controle tire aussi les {gardees.Count} colonnes GARDEES a segment personnel ({gardees.Sum(g => g.Valeurs)} valeurs ; lexique : {controle.LexiquePrenoms} prenoms " +
    $"frequents dans {controle.ColonnesLexique} colonnes de prenoms) : aucune n'atteint {ControleCopie.PartPrenomsMax:P0} de prenoms " +
    $"(la plus haute : {gardees.OrderByDescending(g => g.Part).Select(g => $"{g.Colonne} {g.Part:P0}").FirstOrDefault()})");

// CONTRE-EPREUVE ROUGE de la regle elle-meme : la copie ET le controle prennent l'ancienne regle (« ETAT » technique), ici
// en remplacements « Garder » sur les prenoms d'etat civil de l'extrait. Le controle ne peut plus les voir par les colonnes
// traitees : il doit les trouver par les colonnes gardees -- sinon un vert ne prouverait pas que la regle est juste.
var ancienneRegle = rapport.Colonnes.Where(c => c.Colonne.Contains("ETAT_CIVIL", StringComparison.Ordinal) && c.Valeurs > 0)
    .Select(c => new KeyValuePair<string, Traitement>($"{c.Table[4..]}.{c.Colonne}", Traitement.Garder)).ToList();
await using var etatCivil = await BaseJetable.CreerAsync("SocleAnonymisationEtatCivil");
await CopieAnonymisee.CopierAsync(new OptionsCopie { Source = v2.Connexion, Cible = etatCivil.Connexion, Graine = "SOC-021", Remplacements = ancienneRegle });
var controleEtatCivil = await ControleCopie.ControlerAsync(v2.Connexion, etatCivil.Connexion, null, 200, remplacements: ancienneRegle);
var vus = controleEtatCivil.Gardees.Where(g => g.Prenoms).Select(g => g.Colonne).ToHashSet();
var attendusVus = ancienneRegle.Select(r => "dbo." + r.Key).ToList();
Exiger(ancienneRegle.Count >= 7 && !controleEtatCivil.Vert && controleEtatCivil.Fuites.Count == 0 && attendusVus.All(vus.Contains),
    $"SOC-021 CA06 (contre-epreuve) : copie ET controle avec l'ancienne regle sur {ancienneRegle.Count} colonnes de prenoms d'etat civil -> aucune « fuite » par les " +
    $"colonnes traitees, mais le controle les trouve gardees en clair : {string.Join(", ", controleEtatCivil.Gardees.Where(g => g.Prenoms).Select(g => $"{g.Colonne[4..]} {g.Part:P0}"))}" +
    (attendusVus.All(vus.Contains) ? "" : $" -- MANQUENT : {string.Join(", ", attendusVus.Where(a => !vus.Contains(a)))}"));

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
// Sur RIG_DEV ENTIER (pas seulement l'extrait) : les colonnes que les regles GARDENT et dont le nom a un segment personnel,
// tirees au hasard par la porte de lecture (empreintes salees seulement). C'est la mesure qui aurait vu le refus du chef.
chrono.Restart();
var schemaRigDev = await SchemaSource.LireAsync(rigDev, CancellationToken.None);
var catalogueRigDev = new CatalogueRegles();
catalogueRigDev.AlignerSurLesCles(schemaRigDev);
var mesureRigDev = await ControleCopie.MesurerGardeesAsync(rigDev, schemaRigDev, catalogueRigDev, schemaRigDev.Tables, 200, CancellationToken.None);
var rempliesRigDev = mesureRigDev.Colonnes.Where(g => g.Valeurs > 0).ToList();
Exiger(rempliesRigDev.Count > 50 && mesureRigDev.MotsLexique > 0 && rempliesRigDev.All(g => !g.Prenoms) && mesureRigDev.GenresLus.IsSubsetOf(genresAdmis),
    $"SOC-021 CA01, CA06 : sur {baseRigDev} entier ({chrono.Elapsed.TotalSeconds:0} s), {rempliesRigDev.Count} colonnes gardees a segment personnel tirees ({rempliesRigDev.Sum(g => g.Valeurs)} valeurs, " +
    $"lexique {mesureRigDev.MotsLexique} prenoms de {mesureRigDev.ColonnesLexique} colonnes) : aucune n'atteint {ControleCopie.PartPrenomsMax:P0} de prenoms ; les plus hautes : " +
    string.Join(", ", rempliesRigDev.OrderByDescending(g => g.Part).Take(3).Select(g => $"{g.Colonne[4..]} {g.Part:P0}")) +
    (rempliesRigDev.Any(g => g.Prenoms) ? $" -- EN CLAIR : {string.Join(", ", rempliesRigDev.Where(g => g.Prenoms).Select(g => $"{g.Colonne[4..]} {g.Part:P0}"))}" : ""));
string[] etatCivilRigDev = ["CASIER_JUDICIAIRE.CSRJD_PRENOMS_ETAT_CIVIL", "INSCRIT_PP.INSPP_PRENOMS_ETAT_CIVIL", "BENEFICIAIRE_EFFECTIF.BENEF_PRENOM_ETAT_CIVIL",
                            "BENEFICIAIRE_EFFECTIF_HISTO.BENEFHI_PRENOM_ETAT_CIVIL", "REPRESENTANT_PM.RPPM_PRENOMS_ETAT_CIVIL", "BODACC_RCS_XML.BDCRCX_PRENOMS_ETAT_CIVIL"];
var catalogueAncien = new CatalogueRegles(etatCivilRigDev.Select(c => new KeyValuePair<string, Traitement>(c, Traitement.Garder)));
var mesureAncienne = await ControleCopie.MesurerGardeesAsync(rigDev, schemaRigDev, catalogueAncien,
    schemaRigDev.Tables.Where(t => etatCivilRigDev.Any(c => c.StartsWith(t.Nom + ".", StringComparison.Ordinal))).ToList(), 200, CancellationToken.None);
var vusRigDev = mesureAncienne.Colonnes.Where(g => g.Prenoms).Select(g => g.Colonne[4..]).ToHashSet();
Exiger(etatCivilRigDev.All(vusRigDev.Contains),
    $"SOC-021 CA06 (contre-epreuve sur {baseRigDev}) : avec l'ancienne regle, les prenoms d'etat civil gardes sont vus : " +
    string.Join(", ", mesureAncienne.Colonnes.Where(g => etatCivilRigDev.Contains(g.Colonne[4..])).Select(g => $"{g.Colonne[4..]} {g.Part:P0} de {g.Valeurs}")));

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
// SOC-021 CA01, CA03, CA04, CA06 -- DOCUMENTS (regle des binaires du lot 18) : chaque document de la source devient un
// document FICTIF du meme format et de la meme taille ; aucun n'est retrouve dans la copie
// ====================================================================================================
// fix-ok: lot 18 (2026-11-22) -- la copie recopiait les Kbis et les actes tels quels (GED_RIG.GDRG_FICHIER, KBIS_XML.KBIS_PDF :
// 1 367 documents, 531 Mo dans la copie reduite de RIG_DEV). Le banc compte LUI-MEME, source et copie etant sur ce serveur, sans
// se fier au rapport ni au controle de l'outil : empreinte SHA-256 de chaque document des deux cotes, taille, NULL ; puis il
// OUVRE chaque document de la copie avec un lecteur du format de la source (archive ZIP, XML, table xref du PDF).
var colonnesDocuments = new (string Table, string Cle, string Colonne)[]
{
    ("GED_RIG", "GDRG_NUM_DOCUMENT", "GDRG_FICHIER"), ("GED_RIG", "GDRG_NUM_DOCUMENT", "GDRG_FICHIER_PDF"),
    ("GED_RIG", "GDRG_NUM_DOCUMENT", "GDRG_FICHIER_PS"), ("KBIS_XML", "KBIS_ID_KBIS", "KBIS_PDF"),
};
async Task<BilanDocuments> DocumentsAsync(string chaineCopie, string? exemples = null)
{
    var nomC = new SqlConnectionStringBuilder(chaineCopie).InitialCatalog;
    var b = new BilanDocuments();
    var empreintesSource = new HashSet<string>();
    var empreintesCopie = new HashSet<string>();
    foreach (var (table, cle, col) in colonnesDocuments)
    {
        var lignes = await LireAsync(chaineCopie,
            $"SELECT CONVERT(varbinary(8), SUBSTRING(CONVERT(varbinary(max), s.[{col}]), 1, 8)), CONVERT(bigint, DATALENGTH(s.[{col}])), " +
            $"HASHBYTES('SHA2_256', CONVERT(varbinary(max), s.[{col}])), CONVERT(varbinary(max), c.[{col}]) " +
            $"FROM {S(table)} s JOIN [{nomC}].dbo.[{table}] c ON c.[{cle}] = s.[{cle}]");
        foreach (DataRow l in lignes.Rows)
        {
            var colonne = $"{table}.{col}";
            if (l[1] is DBNull != l[3] is DBNull) b.NullsDifferents.Add(colonne);
            if (l[1] is DBNull) { b.Nulls++; continue; }
            if (l[3] is not byte[] doc) continue;
            b.Documents++;
            var format = FormatSource((byte[])l[0]);
            b.Formats[format] = b.Formats.GetValueOrDefault(format) + 1;
            if (doc.LongLength != (long)l[1]) b.AutreTaille.Add(colonne);
            var empreinteSource = Convert.ToHexString((byte[])l[2]);
            var empreinteCopie = Convert.ToHexString(SHA256.HashData(doc));
            if (empreinteSource == empreinteCopie) b.ASaPlace.Add(colonne);
            empreintesSource.Add(empreinteSource);
            empreintesCopie.Add(empreinteCopie);
            if (!Lisible(format, doc, (byte[])l[0])) b.Illisibles.Add($"{colonne} ({format})");
            if (exemples is not null && !b.Exemples.ContainsKey(format))
            {
                b.Exemples[format] = Path.Combine(exemples, $"document-fictif-{format.ToLowerInvariant()}.{format.ToLowerInvariant()}");
                await File.WriteAllBytesAsync(b.Exemples[format], doc);
            }
        }
    }
    b.Ailleurs = empreintesCopie.Count(empreintesSource.Contains);
    return b;
}
var exemplesFictifs = Path.Combine(dossier, "documents-fictifs");
Directory.CreateDirectory(exemplesFictifs);
var documents = await DocumentsAsync(copie.Connexion, exemplesFictifs);
var sectionDocuments = texteRapport.Contains("## Documents remplacés") && texteRapport.Contains("## Champs libres")
    ? texteRapport[texteRapport.IndexOf("## Documents remplacés", StringComparison.Ordinal)..texteRapport.IndexOf("## Champs libres", StringComparison.Ordinal)] : "";
Exiger(documents.Documents >= 40 && documents.Formats.Count >= 3 && documents.Nulls > 0 && documents.ASaPlace.Count == 0 && documents.Ailleurs == 0
       && documents.AutreTaille.Count == 0 && documents.Illisibles.Count == 0 && documents.NullsDifferents.Count == 0
       && sectionDocuments.Contains("dbo.GED_RIG.GDRG_FICHIER") && sectionDocuments.Contains("dbo.KBIS_XML.KBIS_PDF"),
    $"SOC-021 CA01, CA03, CA04, CA06 : documents remplacés : aucun document de la source retrouvé, même format, même taille -- {documents.Documents} documents " +
    $"({string.Join(", ", documents.Formats.Select(f => $"{f.Key} {f.Value}"))}) : retrouves a leur place {documents.ASaPlace.Count}, ailleurs dans la copie {documents.Ailleurs} (empreintes SHA-256) ; " +
    $"autre taille {documents.AutreTaille.Count} ; illisibles dans le format de la source {documents.Illisibles.Count}{(documents.Illisibles.Count == 0 ? "" : " (" + string.Join(", ", documents.Illisibles.Distinct()) + ")")} " +
    $"(archive ZIP ouverte, XML lu, table xref du PDF suivie) ; {documents.Nulls} NULL restes NULL ; colonnes listees au rapport (CA04) : " +
    $"{(sectionDocuments.Contains("GDRG_FICHIER") && sectionDocuments.Contains("KBIS_PDF") ? "GED_RIG.GDRG_FICHIER, KBIS_XML.KBIS_PDF" : "NON")}");
Exiger(controle.ColonnesDocuments >= 2 && controle.DocumentsTires > 0 && controle.DocumentsRetrouves.Count == 0 && controle.DocumentsAutreForme.Count == 0
       && controle.GenresLusSource.Contains("format et taille"),
    $"SOC-021 CA06 : le controle de l'outil tire {controle.DocumentsTires} documents de {controle.ColonnesDocuments} colonnes « document » (empreinte calculee sur le serveur de la source) : " +
    $"retrouves {(controle.DocumentsRetrouves.Count == 0 ? "aucun" : string.Join(", ", controle.DocumentsRetrouves))}, autre forme " +
    $"{(controle.DocumentsAutreForme.Count == 0 ? "aucune" : string.Join(", ", controle.DocumentsAutreForme))}");

// CONTRE-EPREUVE ROUGE : la regle des binaires retiree (remplacements « Garder » sur les deux colonnes de documents). Le compte du
// banc DOIT retrouver les documents, le controle de l'outil aussi -- par les colonnes « document » avec ses regles, et par les
// binaires GARDES quand il prend la regle fausse : sinon le vert ci-dessus ne prouverait pas que les documents sont remplaces.
KeyValuePair<string, Traitement>[] sansRegleBinaires = [new("GED_RIG.GDRG_FICHIER", Traitement.Garder), new("KBIS_XML.KBIS_PDF", Traitement.Garder)];
await using var documentsEnClair = await BaseJetable.CreerAsync("SocleAnonymisationDocuments");
await CopieAnonymisee.CopierAsync(new OptionsCopie { Source = v2.Connexion, Cible = documentsEnClair.Connexion, Graine = "SOC-021", Remplacements = sansRegleBinaires });
var documentsSabot = await DocumentsAsync(documentsEnClair.Connexion);
var controleDocuments = await ControleCopie.ControlerAsync(v2.Connexion, documentsEnClair.Connexion, null, 200);
var controleRegleFausse = await ControleCopie.ControlerAsync(v2.Connexion, documentsEnClair.Connexion, null, 200, remplacements: sansRegleBinaires);
var gardesAvecDocuments = controleRegleFausse.BinairesGardes.Where(g => g.Documents > 0).Select(g => g.Colonne).ToList();
Exiger(documentsSabot.ASaPlace.Count == documentsSabot.Documents && documentsSabot.Documents >= 40
       && !controleDocuments.Vert && controleDocuments.DocumentsRetrouves.Any(d => d.StartsWith("dbo.GED_RIG.GDRG_FICHIER ", StringComparison.Ordinal))
       && controleDocuments.DocumentsRetrouves.Any(d => d.StartsWith("dbo.KBIS_XML.KBIS_PDF ", StringComparison.Ordinal))
       && !controleRegleFausse.Vert && gardesAvecDocuments.Contains("dbo.GED_RIG.GDRG_FICHIER") && gardesAvecDocuments.Contains("dbo.KBIS_XML.KBIS_PDF"),
    $"SOC-021 CA06 (contre-epreuve) : regle des binaires retiree -> le banc retrouve {documentsSabot.ASaPlace.Count} documents sur {documentsSabot.Documents} ; " +
    $"le controle de l'outil aussi : {string.Join(", ", controleDocuments.DocumentsRetrouves)} ; avec la regle fausse, il voit des documents dans les binaires gardes : " +
    $"{string.Join(", ", controleRegleFausse.BinairesGardes.Where(g => g.Documents > 0).Select(g => $"{g.Colonne} ({g.Documents} sur {g.Valeurs} : {g.Formats})"))}");

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
// L'inventaire des binaires (regle du lot 18) : CHAQUE colonne binary, varbinary ou image des tables de RIG_DEV, lue par le banc
// lui-meme, y est classee -- remplacee (document) ou gardee avec sa raison (binaire technique).
var binairesRigDev = (await rigDev.LignesAsync(
        "SELECT OBJECT_SCHEMA_NAME(c.object_id) + '.' + OBJECT_NAME(c.object_id) + '.' + c.name FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id " +
        "WHERE t.is_ms_shipped = 0 AND TYPE_NAME(c.system_type_id) IN ('binary', 'varbinary', 'image')", CancellationToken.None))
    .Select(l => (string)l[0]!).ToList();
var texteInventaire = File.Exists(inventaire) ? await File.ReadAllTextAsync(inventaire) : "";
var sectionBinaires = texteInventaire.Contains("## Colonnes binaires") ? texteInventaire[texteInventaire.IndexOf("## Colonnes binaires", StringComparison.Ordinal)..] : "";
var lignesBinaires = sectionBinaires.Split('\n').Where(l => l.StartsWith("| dbo.", StringComparison.Ordinal)).ToList();
var nonClassees = binairesRigDev.Where(b => !lignesBinaires.Any(l => l.StartsWith($"| {b} |", StringComparison.Ordinal)
    && (l.Contains("**remplacée**") || l.Contains("**gardée**")) && l.TrimEnd().TrimEnd('|').Split('|').Last().Trim().Length > 10)).ToList();
Exiger(binairesRigDev.Count > 0 && nonClassees.Count == 0,
    $"SOC-021 T01, CA04 : inventaire des binaires de {baseRigDev} : {binairesRigDev.Count} colonnes, chacune classee avec sa raison " +
    $"({lignesBinaires.Count(l => l.Contains("**remplacée**"))} remplacees, {lignesBinaires.Count(l => l.Contains("**gardée**"))} gardees)" +
    (nonClassees.Count == 0 ? "" : $" -- NON CLASSEES : {string.Join(", ", nonClassees)}"));
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
// Une source qui CACHE ses definitions -- compte de lecture sans VIEW DEFINITION, comme sur RIG_LE_PUY (SQL-PROD) le 2026-11-23 --
// est refusee AVANT toute base copie. Sans ce refus, la copie perdait en silence valeurs par defaut, contraintes, filtres d'index
// et modules, puis plantait apres 36 min de donnees sur « CHECK ; ». Ici, la vue de l'extrait est le module cache.
var loginLimite = $"banc_sans_definition_{Guid.NewGuid():N}"[..40];
var mdpLimite = $"Banc_{Guid.NewGuid():N}!";
await ExecuterAsync(copie.Connexion, $"CREATE LOGIN [{loginLimite}] WITH PASSWORD = N'{mdpLimite}', CHECK_POLICY = OFF;");
try
{
    await ExecuterAsync(v2.Connexion, $"CREATE USER [{loginLimite}] FOR LOGIN [{loginLimite}]; ALTER ROLE db_datareader ADD MEMBER [{loginLimite}];");
    var sourceLimitee = new SqlConnectionStringBuilder(v2.Connexion) { UserID = loginLimite, Password = mdpLimite }.ConnectionString;
    var nomCache = $"{BaseJetable.Prefixe}SocleAnonymisationCache_{Guid.NewGuid():N}"[..60];
    erreurs.GetStringBuilder().Clear();
    var codeCache = await Programme.ExecuterAsync(["copier", "--source", sourceLimitee, "--cible",
        new SqlConnectionStringBuilder(copie.Connexion) { InitialCatalog = nomCache }.ConnectionString, "--rapport", Path.Combine(dossier, "refus.md")], sortie, erreurs);
    var refusCache = erreurs.ToString();
    Exiger(codeCache == 1 && refusCache.Contains("VIEW DEFINITION") && refusCache.Contains("modules") && !await BaseJetable.ExisteAsync(nomCache),
        $"SOC-021 CA07 : une source qui cache ses definitions (compte sans VIEW DEFINITION) est refusee avant toute base copie (code {codeCache} : " +
        $"« {refusCache.Trim()[..Math.Min(140, refusCache.Trim().Length)]}… ») ; jumeau accepte : la copie par un compte qui les voit, ci-dessus");
}
finally
{
    // Les connexions du compte restent dans le pool : sans les fermer, SQL Server refuse de supprimer un compte connecte.
    SqlConnection.ClearAllPools();
    await ExecuterAsync(copie.Connexion, $"DROP LOGIN [{loginLimite}];");
}

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

// ====================================================================================================
// REC-008 (lot 18) -- copie REDUITE fermee sur ses cles etrangeres : aucune ligne n'y vise un parent absent
// ====================================================================================================
// L'extrait porte les vraies cles de RIG_DEV entre ses tables (PARTIE -> CLIENT, CLIENT -> UTILISATEUR, DEMANDE_EDITION -> LOT
// et vers elle-meme...). Ses 20 premieres lignes par table (ordre de la cle) ne sont pas fermees : la fermeture doit y ajouter les
// parents. Le banc compte LUI-MEME, cle par cle de la copie, les lignes dont toutes les colonnes de la cle sont remplies et qui ne
// trouvent pas leur parent (la regle de SQL Server) -- sans se fier au rapport de l'outil.
async Task<(int Cles, int NonVerifiees, long Orphelins, List<string> Detail)> OrphelinsAsync(string chaine)
{
    var cles = await LireAsync(chaine,
        "SELECT fk.object_id, QUOTENAME(SCHEMA_NAME(e.schema_id)) + '.' + QUOTENAME(e.name), QUOTENAME(SCHEMA_NAME(p.schema_id)) + '.' + QUOTENAME(p.name), " +
        "QUOTENAME(COL_NAME(fkc.parent_object_id, fkc.parent_column_id)), QUOTENAME(COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id)), fk.name, fk.is_not_trusted " +
        "FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
        "JOIN sys.tables e ON e.object_id = fk.parent_object_id JOIN sys.tables p ON p.object_id = fk.referenced_object_id ORDER BY fk.object_id, fkc.constraint_column_id");
    var parCle = cles.Rows.Cast<DataRow>().GroupBy(r => (int)r[0]).ToList();
    long orphelins = 0;
    var detail = new List<string>();
    foreach (var k in parCle)
    {
        var (enfant, parent) = ((string)k.First()[1], (string)k.First()[2]);
        var remplies = string.Join(" AND ", k.Select(r => $"c.{r[3]} IS NOT NULL"));
        var lien = string.Join(" AND ", k.Select(r => $"p.{r[4]} = c.{r[3]}"));
        var n = await EntierAsync(chaine, $"SELECT COUNT_BIG(*) FROM {enfant} c WHERE {remplies} AND NOT EXISTS (SELECT 1 FROM {parent} p WHERE {lien})");
        orphelins += n;
        if (n > 0) detail.Add($"{k.First()[5]} ({n})");
    }
    return (parCle.Count, parCle.Count(k => (bool)k.First()[6]), orphelins, detail);
}
const int Reduite = 20;
await using var fermee = await BaseJetable.CreerAsync("SocleAnonymisationFermee");
chrono.Restart();
var rapportFermee = await CopieAnonymisee.CopierAsync(new OptionsCopie { Source = v2.Connexion, Cible = fermee.Connexion, Graine = "SOC-021", MaxLignes = Reduite });
var dureeFermee = chrono.Elapsed;
var orphelinsFermee = await OrphelinsAsync(fermee.Connexion);
var volumesFermee = new List<string>();
foreach (var t in nomsExtrait.Order())
{
    var n = await EntierAsync(fermee.Connexion, $"SELECT COUNT_BIG(*) FROM dbo.[{t}]");
    var ajout = rapportFermee.AjoutsFermeture.FirstOrDefault(a => a.Table == $"dbo.{t}").Lignes;
    if (n != Math.Min(lignesSource[t], Reduite) + ajout || n > lignesSource[t]) volumesFermee.Add($"{t} ({n} : {lignesSource[t]} dans la source, {ajout} ajoutees)");
}
// La contre-epreuve : la reduction du lot 8 (les N premieres lignes seules) sur la meme source. Le compte du banc DOIT y trouver des
// lignes orphelines, sinon le vert du controle ci-dessous ne prouverait pas que la fermeture les a reparees.
await using var nonFermee = await BaseJetable.CreerAsync("SocleAnonymisationNonFermee");
var rapportNonFermee = await CopieAnonymisee.CopierAsync(new OptionsCopie
{
    Source = v2.Connexion, Cible = nonFermee.Connexion, Graine = "SOC-021", MaxLignes = Reduite, FermerSurLesCles = false,
});
var orphelinsNonFermee = await OrphelinsAsync(nonFermee.Connexion);
Exiger(orphelinsNonFermee.Orphelins > 0 && !rapportNonFermee.Fermee && rapportNonFermee.ClesVerifiees.Count == 0,
    $"REC-008 (contre-epreuve) : la reduction NON fermee ({Reduite} premieres lignes par table) laisse {orphelinsNonFermee.Orphelins} lignes orphelines " +
    $"sur {orphelinsNonFermee.Detail.Count} cles ({string.Join(", ", orphelinsNonFermee.Detail.Take(4))}) ; ses {rapportNonFermee.ClesNonVerifiees.Count} cles posees sans verification");
Exiger(rapportFermee.Fermee && rapportFermee.Anomalies.Count == 0 && orphelinsFermee.Orphelins == 0 && orphelinsFermee.NonVerifiees == 0
       && orphelinsFermee.Cles == clesSource.Count && rapportFermee.ClesVerifiees.Count == clesSource.Count
       && rapportFermee.AjoutsFermeture.Sum(a => a.Lignes) > 0 && volumesFermee.Count == 0,
    $"REC-008 : copie réduite fermée : aucune clé étrangère orpheline -- {Reduite} premieres lignes par table puis {rapportFermee.AjoutsFermeture.Sum(a => a.Lignes)} lignes parentes " +
    $"({string.Join(", ", rapportFermee.AjoutsFermeture.Select(a => $"{a.Table} +{a.Lignes}"))}) en {rapportFermee.ToursFermeture} tours, {dureeFermee.TotalSeconds:0.0} s ; " +
    $"compte du banc : {orphelinsFermee.Cles} cles, {orphelinsFermee.Orphelins} orphelines{(orphelinsFermee.Detail.Count == 0 ? "" : " (" + string.Join(", ", orphelinsFermee.Detail) + ")")}, " +
    $"{orphelinsFermee.NonVerifiees} non verifiees ; anomalies : {(rapportFermee.Anomalies.Count == 0 ? "aucune" : string.Join(" ; ", rapportFermee.Anomalies))} ; " +
    $"volumes = min(source, {Reduite}) + ajouts : {(volumesFermee.Count == 0 ? "tous" : "SAUF " + string.Join(", ", volumesFermee))}");
// La copie fermee passe le controle de l'outil comme une autre (exige avant usage par la repetition) : aucune valeur reelle retrouvee.
var controleFermee = await ControleCopie.ControlerAsync(v2.Connexion, fermee.Connexion, null, 200, CancellationToken.None);
Exiger(controleFermee.Fuites.Count == 0 && controleFermee.NonVidees.Count == 0 && controleFermee.Marquee && controleFermee.EchantillonValeurs > 0,
    $"REC-008 / SOC-021 CA06 : le controle de la copie reduite fermee ne retrouve aucune des {controleFermee.EchantillonValeurs} valeurs reelles tirees " +
    $"(retrouvees : {(controleFermee.Fuites.Count == 0 ? "aucune" : string.Join(", ", controleFermee.Fuites))}) ; marque presente, colonnes a vider vides");

// Une copie reduite qui garde des tables EN ENTIER (OptionsCopie.TablesEntieres : les donnees de developpement, dont les comptes,
// droits et compteurs doivent etre complets) : la plus grosse table de l'extrait a toutes ses lignes, les autres restent reduites,
// la copie reste fermee. Jumeau : dans la copie reduite sans l'option (ci-dessus), la meme table n'a pas toutes ses lignes.
var entiere = nomsExtrait.Where(t => lignesSource[t] > Reduite).OrderByDescending(t => lignesSource[t]).First();
await using var avecEntiere = await BaseJetable.CreerAsync("SocleAnonymisationEntiere");
var rapportEntiere = await CopieAnonymisee.CopierAsync(new OptionsCopie
{
    Source = v2.Connexion, Cible = avecEntiere.Connexion, Graine = "SOC-021", MaxLignes = Reduite, TablesEntieres = [entiere],
});
var lignesEntiere = await EntierAsync(avecEntiere.Connexion, $"SELECT COUNT_BIG(*) FROM dbo.[{entiere}]");
var lignesSansOption = await EntierAsync(fermee.Connexion, $"SELECT COUNT_BIG(*) FROM dbo.[{entiere}]");
var autresReduites = new List<string>();
foreach (var t in nomsExtrait.Where(t => t != entiere && lignesSource[t] > Reduite))
{
    var n = await EntierAsync(avecEntiere.Connexion, $"SELECT COUNT_BIG(*) FROM dbo.[{t}]");
    if (n >= lignesSource[t]) autresReduites.Add($"{t} ({n} sur {lignesSource[t]})");
}
var orphelinsEntiere = await OrphelinsAsync(avecEntiere.Connexion);
Exiger(lignesEntiere == lignesSource[entiere] && lignesSansOption < lignesSource[entiere] && rapportEntiere.TablesEntieres.SequenceEqual([$"dbo.{entiere}"])
       && autresReduites.Count == 0 && orphelinsEntiere.Orphelins == 0 && rapportEntiere.Anomalies.Count == 0,
    $"Donnees de dev : copie reduite a {Reduite} lignes avec {entiere} EN ENTIER : {lignesEntiere} lignes sur {lignesSource[entiere]} " +
    $"(sans l'option : {lignesSansOption}) ; les autres tables restent reduites{(autresReduites.Count == 0 ? "" : " SAUF " + string.Join(", ", autresReduites))} ; " +
    $"{orphelinsEntiere.Orphelins} ligne(s) orpheline(s), rapport : {string.Join(", ", rapportEntiere.TablesEntieres)}");

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
/// La table telle que RIG_DEV la declare (colonnes, types, collation, identite, cle primaire), ecrite par le banc LUI-MEME
/// (independamment de la reecriture de l'outil, Outils/Anonymisation/Schema.cs, qu'il controle). La collation de chaque texte
/// est reprise : KBIS_XML.KBIS_XML est en French_CI_AS dans une base en SQL_Latin1_General_CP1_CI_AS, et la copie en masse
/// refuse deux colonnes de langues differentes (« L'ID '1036' ... ne correspond pas a l'ID '1033' »).
/// </summary>
static async Task<string> DdlBancAsync(PorteSource rigDev, string table)
{
    var colonnes = await rigDev.LignesAsync(
        "SELECT c.name, TYPE_NAME(c.system_type_id), CONVERT(int, c.max_length), CONVERT(int, c.precision), CONVERT(int, c.scale), c.is_nullable, c.is_identity, c.collation_name " +
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
        return $"[{nom}] {t}{(c[7] is string collation ? $" COLLATE {collation}" : "")}{((bool)c[6]! ? " IDENTITY(1,1)" : "")}{((bool)c[5]! ? " NULL" : " NOT NULL")}";
    });
    var pk = cle.Count > 0 ? $", CONSTRAINT [PK_{table}] PRIMARY KEY CLUSTERED ({string.Join(", ", cle.Select(k => $"[{k[0]}]"))})" : "";
    return $"CREATE TABLE dbo.[{table}] ({string.Join(", ", defs)}{pk});";
}

// ----------------------------------------------------------------------------------------------------
// Les documents, lus par le banc LUI-MEME (independamment de Outils/Anonymisation/Documents.cs, qu'il controle).

/// <summary>Le format d'un document de la source, reconnu a ses premiers octets.</summary>
static string FormatSource(byte[] tete)
{
    ReadOnlySpan<byte> t = tete;
    if (t.StartsWith("%PDF"u8)) return "Pdf";
    if (t.StartsWith("%!"u8)) return "PostScript";
    if (t.StartsWith("PK\u0003\u0004"u8) || t.StartsWith("PK\u0005\u0006"u8)) return "Zip";
    ReadOnlySpan<byte> marqueUtf8 = [0xEF, 0xBB, 0xBF];
    if (t.StartsWith(marqueUtf8)) t = t[3..];
    return t.StartsWith("<"u8) ? "Xml" : "Autre";
}

/// <summary>
/// Le document de la copie se lit-il comme un document du format de la source, et ne dit-il que le texte fictif ? ZIP : l'archive
/// s'ouvre et son unique fichier le porte ; XML : il se charge et sa racine le porte ; PDF : voir <see cref="PdfLisible"/> ;
/// PostScript : il l'imprime ; autre format : il garde la signature de la source.
/// </summary>
static bool Lisible(string format, byte[] doc, byte[] teteSource)
{
    const string texte = "Document anonymisé — copie de travail";
    try
    {
        switch (format)
        {
            case "Pdf":
                return PdfLisible(doc);
            case "Zip":
            {
                using var zip = new ZipArchive(new MemoryStream(doc), ZipArchiveMode.Read);
                if (zip.Entries.Count != 1) return false;
                using var lu = new StreamReader(zip.Entries[0].Open(), Encoding.UTF8);
                return lu.ReadToEnd().StartsWith(texte, StringComparison.Ordinal);
            }
            case "Xml":
                return XDocument.Load(new MemoryStream(doc)).Root?.Value.Contains(texte, StringComparison.Ordinal) == true;
            case "PostScript":
                var ps = Encoding.Latin1.GetString(doc);
                return ps.StartsWith("%!PS", StringComparison.Ordinal) && ps.Contains("copie de travail) show", StringComparison.Ordinal) && ps.EndsWith("%%EOF\n", StringComparison.Ordinal);
            default:
                return doc.AsSpan().StartsWith(teteSource.AsSpan(0, Math.Min(4, teteSource.Length)));
        }
    }
    catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or IOException)
    {
        return false;
    }
}

/// <summary>
/// Un PDF lisible : en-tete « %PDF-1. », « startxref » qui mene a la table xref, chaque objet « n » de la table a sa position
/// exacte, racine /Catalog, et le flux de la page (de longueur /Length exacte) ecrit le texte fictif. C'est ce que suit un lecteur
/// pour ouvrir le fichier sans le reparer.
/// </summary>
static bool PdfLisible(byte[] pdf)
{
    var s = Encoding.Latin1.GetString(pdf);
    var fin = Regex.Match(s, @"startxref\n(\d+)\n%%EOF\n?$");
    if (!s.StartsWith("%PDF-1.", StringComparison.Ordinal) || !fin.Success) return false;
    var xref = long.Parse(fin.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    if (xref >= s.Length) return false;
    var table = Regex.Match(s[(int)xref..], @"^xref\n0 (\d+)\n((?:\d{10} \d{5} [nf] \n)+)trailer\n<<(.*?)>>", RegexOptions.Singleline);
    if (!table.Success) return false;
    var entrees = table.Groups[2].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    if (entrees.Length != int.Parse(table.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)) return false;
    var objets = new Dictionary<int, string>();
    for (var i = 1; i < entrees.Length; i++)
    {
        var pos = int.Parse(entrees[i][..10], System.Globalization.CultureInfo.InvariantCulture);
        if (entrees[i][17] != 'n' || pos >= s.Length || !s.AsSpan(pos).StartsWith($"{i} 0 obj")) return false;
        var finObjet = s.IndexOf("endobj", pos, StringComparison.Ordinal);
        if (finObjet < 0) return false;
        objets[i] = s[pos..finObjet];
    }
    var racine = Regex.Match(table.Groups[3].Value, @"/Root (\d+) 0 R");
    if (!racine.Success || !objets.TryGetValue(int.Parse(racine.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), out var catalogue)
        || !catalogue.Contains("/Type /Catalog", StringComparison.Ordinal)) return false;
    var flux = objets.Values.Select(o => Regex.Match(o, @"<< /Length (\d+) >>\nstream\n")).FirstOrDefault(m => m.Success);
    if (flux is null) return false;
    var objet = objets.Values.First(o => o.Contains(flux.Value, StringComparison.Ordinal));
    var debut = objet.IndexOf(flux.Value, StringComparison.Ordinal) + flux.Length;
    var longueur = int.Parse(flux.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    return debut + longueur <= objet.Length && objet.AsSpan(debut + longueur).StartsWith("\nendstream")
           && objet.Substring(debut, longueur).Contains(@"(Document anonymis\351 \227 copie de travail) Tj", StringComparison.Ordinal);
}

/// <summary>Ce que le banc a vu des documents d'une copie (section « DOCUMENTS »).</summary>
sealed class BilanDocuments
{
    public long Documents { get; set; }
    public long Nulls { get; set; }
    public SortedDictionary<string, long> Formats { get; } = new(StringComparer.Ordinal);
    /// <summary>Colonnes dont un document de la copie a l'empreinte de celui de la source, a sa place.</summary>
    public List<string> ASaPlace { get; } = [];
    /// <summary>Documents de la copie dont l'empreinte est celle d'un document de la source, ou qu'il soit.</summary>
    public int Ailleurs { get; set; }
    public List<string> AutreTaille { get; } = [];
    public List<string> Illisibles { get; } = [];
    public List<string> NullsDifferents { get; } = [];
    /// <summary>Un document fictif par format, ecrit a cote du banc pour qu'on l'ouvre a la main.</summary>
    public Dictionary<string, string> Exemples { get; } = [];
}
