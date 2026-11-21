using System.Data;
using System.Net.Mail;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>
/// Un courriel a envoyer depuis un acte de la V3 (refus, certificat, document...). C'est ce que RIG_Send_Email recevait
/// de sa ligne d'edition (SendEmail.LoadFromLigneEdition, SendEmail.cs:520-560).
/// </summary>
/// <param name="TypeModele">Le modele du greffe (EMAILMOD_TYPE) : objet, texte, expediteur, reponse, copie, destinataire par defaut.</param>
/// <param name="Destinataires">L'adresse du destinataire de l'acte (le client, le declarant) ; elle s'ajoute au destinataire
/// par defaut du modele, comme la V2 (SendEmail.cs:547-554). « a;b » pour plusieurs.</param>
/// <param name="ComplementObjet">Ajoute a l'objet du modele (la V2 : « [ref : n° de demande…] »).</param>
/// <param name="Documents">Les documents envoyes par lien.</param>
/// <param name="Mode">MANUEL, AUTO, REPRISE, RENVOI_FORCE (ModeEnvoi de la V2), garde dans la trace.</param>
/// <param name="Origine">Ce qui demande l'envoi (l'acte, l'ecran) ; la V2 gardait la ligne de commande de RIG_Send_Email.</param>
/// <param name="Agent">Le compte Windows de l'agent qui fait l'acte ; null pour un traitement sans agent.</param>
/// <param name="Application">Le nom garde dans la trace (EMTRC_APPLICATION).</param>
public sealed record DemandeCourriel(
    string TypeModele,
    string? Destinataires = null,
    string? ComplementObjet = null,
    IReadOnlyList<DocumentCourriel>? Documents = null,
    string? Mode = null,
    string? Origine = null,
    string? Agent = null,
    int? IdLigneEdition = null,
    string? IdsLignesEdition = null,
    string Application = ServiceCourriels.ApplicationParDefaut);

/// <summary>Ce qu'est devenu un envoi : son numero de suivi (sa trace), son etat (<see cref="EtatsCourriel"/>), le courriel
/// enregistre s'il est parti, l'erreur sinon, et hors production le fichier de la boite de depot.</summary>
public sealed record ResultatEnvoi(Guid Suivi, string Etat, int? IdCourriel, string? Erreur, string? FichierDepot);

/// <summary>
/// L'ENVOI DES COURRIELS DU GREFFE (SOC-006 T02), par la partie serveur. Recopie du chemin de la V2
/// RIG_Send_Email -> EmailEnvoye.Send (D:\RigApplication\Source\Outils\Exe\RIG\RIG_Send_Email\SendEmail.cs,
/// D:\RigApplication\Source\Outils\DLL\RigMetier\Mail\EmailEnvoye.cs) :
///  1. le modele du greffe (soc.ModeleMessage) donne l'objet, le texte et les adresses ; les etiquettes [ADRESSE_…]
///     des adresses prennent la valeur du greffe (SendEmail.filtrerMail) ;
///  2. le texte est rendu avec l'habillage du greffe et ses jetons (<see cref="HabillageCourriel"/>, CA02) ;
///  3. le courriel est remis au serveur de messagerie -- ou, hors production, range dans la boite de depot
///     (<see cref="OptionsCourriels.Decider"/>, CA06) ;
///  4. chaque etape s'ecrit dans la trace du greffe, succes ou erreur (<see cref="TracesCourriels"/>, CA03) ; un courriel
///     parti s'enregistre dans soc.CourrielEnvoye (SendEmail.cs:845-846 : apres l'envoi, jamais avant).
/// La base du greffe s'ouvre par le catalogue ; la trace s'y ecrit hors de toute transaction de l'appelant, comme la
/// V2 (connexion a part) : un acte annule ne fait pas disparaitre la trace d'un courriel deja parti.
/// </summary>
public sealed class ServiceCourriels(OptionsCourriels options, CatalogueGreffes catalogue, BaseRig commune, ILogger<ServiceCourriels> journal)
{
    public const string ApplicationParDefaut = "RigV3.Serveur";

    /// <summary>Les options lues au demarrage (lecture seule).</summary>
    public OptionsCourriels Options => options;

    /// <summary>
    /// Envoie le courriel <paramref name="demande"/> du greffe <paramref name="code"/>. Ne leve pas pour un echec d'envoi :
    /// le resultat le dit et la trace le garde. Leve <see cref="GreffeInconnu"/> ou <see cref="BaseGreffeInjoignable"/> si
    /// la base du greffe ne s'ouvre pas (aucune trace n'est alors possible : elle vit dans cette base).
    /// </summary>
    public async Task<ResultatEnvoi> EnvoyerAsync(string code, DemandeCourriel demande, CancellationToken ct = default)
    {
        await using var cn = await catalogue.OuvrirBaseV3Async(code, ct);
        var suivi = Guid.NewGuid();
        var documents = demande.Documents ?? [];
        LigneTrace Trace(string etape) => new(suivi, code, demande.Application, demande.Mode, etape, demande.TypeModele,
            IdLigneEdition: demande.IdLigneEdition, IdsLignesEdition: demande.IdsLignesEdition, Origine: demande.Origine,
            Agent: demande.Agent, NbPieces: documents.Count,
            Pieces: documents.Count == 0 ? null : string.Join(";", documents.Select(d => d.Fichier)),
            Urls: documents.Count == 0 ? null : string.Join(";", documents.Select(d => d.Url)));

        // 1-2. Preparation : le modele, les adresses, le rendu.
        CourrielPrepare prepare;
        try
        {
            prepare = await PreparerAsync(code, cn, demande, documents, ct);
        }
        catch (PreparationImpossible e)
        {
            await TracesCourriels.EcrireAsync(cn, Trace(EtapesCourriel.PreparationErreur) with
            {
                Succes = false, ErreurType = nameof(PreparationImpossible), ErreurMessage = e.Message,
            }, journal, ct);
            return new ResultatEnvoi(suivi, EtatsCourriel.Echec, null, e.Message, null);
        }
        var adresses = Trace(EtapesCourriel.PreparationEmail) with
        {
            Expediteur = prepare.Expediteur, Destinataires = prepare.Destinataires, Copie = prepare.Copie,
            Reponse = prepare.Reponse, Sujet = prepare.Sujet, EstHtml = prepare.CorpsHtml,
        };
        await TracesCourriels.EcrireAsync(cn, adresses, journal, ct);
        await TracesCourriels.EcrireAsync(cn, adresses with { Etape = EtapesCourriel.EnvoiDebut }, journal, ct);

        // 3. Remise, ou depot hors production. Une exception ici est un ECHEC d'envoi (EmailEnvoye.cs:168-190).
        var decision = options.Decider();
        string? fichier = null;
        try
        {
            using var message = HabillageCourriel.Message(prepare.Expediteur, prepare.Reponse, prepare.Destinataires, prepare.Copie,
                                                          prepare.Sujet, prepare.Rendu);
            if (decision.Remettre)
            {
                await TracesCourriels.EcrireAsync(cn, adresses with
                {
                    Etape = EtapesCourriel.AppelSmtp, SmtpHote = decision.Hote, SmtpPort = decision.Port,
                }, journal, ct);
                await RemiseCourriel.RemettreAsync(message, decision.Hote!, decision.Port!.Value, ct);
            }
            else
            {
                fichier = await RemiseCourriel.DeposerAsync(message, options.Depot, suivi, ct);
                await TracesCourriels.EcrireAsync(cn, adresses with
                {
                    Etape = EtapesCourriel.IgnoreHorsProduction, Succes = true, FichierDepot = fichier,
                }, journal, ct);
                journal.LogInformation("Courriel {Suivi} du greffe {Greffe} non remis ({Raison}) : {Fichier}", suivi, code, decision.Raison, fichier);
            }
        }
        catch (Exception e) when (e is SmtpException or FormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await TracesCourriels.EcrireAsync(cn, adresses with
            {
                Etape = EtapesCourriel.EnvoiErreur, Succes = false, SmtpHote = decision.Hote, SmtpPort = decision.Port,
                ErreurType = e.GetType().FullName, ErreurMessage = Message(e), Pile = e.StackTrace,
            }, journal, ct);
            return new ResultatEnvoi(suivi, EtatsCourriel.Echec, null, Message(e), null);
        }
        await TracesCourriels.EcrireAsync(cn, adresses with
        {
            Etape = EtapesCourriel.EnvoiSucces, Succes = true, SmtpHote = decision.Hote, SmtpPort = decision.Port, FichierDepot = fichier,
        }, journal, ct);

        // 4. Le courriel parti s'enregistre, puis la trace le cite (etape EMAIL_ENVOYE de RIG_Send_Email, SendEmail.cs:847-860).
        var id = await EnregistrerAsync(cn, prepare, suivi, ct);
        await TracesCourriels.EcrireAsync(cn, adresses with { Etape = EtapesCourriel.CourrielEnregistre, IdCourriel = id, Succes = true }, journal, ct);
        return new ResultatEnvoi(suivi, decision.Remettre ? EtatsCourriel.Envoye : EtatsCourriel.Ignore, id, null, fichier);
    }

    /// <summary>
    /// Le courriel tel qu'il partirait, sans l'envoyer ni rien ecrire : le modele, les adresses resolues, le rendu.
    /// Leve <see cref="PreparationImpossible"/> si le modele manque ou si une adresse indispensable reste vide.
    /// </summary>
    public async Task<CourrielPrepare> PreparerAsync(string code, SqlConnection cn, DemandeCourriel demande,
                                                     IReadOnlyList<DocumentCourriel> documents, CancellationToken ct)
    {
        var modele = await ModeleAsync(cn, demande.TypeModele, ct)
            ?? throw new PreparationImpossible($"Le modele de courriel « {demande.TypeModele} » n'existe pas dans la base du greffe {code}.");
        var valeurs = await LectureValeursGreffe.LireAsync(code, cn, catalogue, commune, ct);
        foreach (var manque in valeurs.Manques)
            journal.LogWarning(EvenementValeurManquante, "Courriel {Modele} du greffe {Greffe} : {Manque}", demande.TypeModele, code, manque);

        // SendEmail.cs:547-559 : destinataire de l'acte + destinataire par defaut du modele ; expediteur, reponse et copie du modele.
        var inconnues = new List<string>();
        string Adresses(string? liste) => HabillageCourriel.Nettoyer(LectureValeursGreffe.ResoudreAdresses(liste, valeurs.InfoGreffe, inconnues));
        var destinataires = Adresses(string.Join(";", new[] { demande.Destinataires, modele.DestinataireDefaut }.Where(a => !string.IsNullOrWhiteSpace(a))));
        var expediteur = Adresses(modele.Expediteur);
        var reponse = Adresses(modele.Reponse);
        var copie = Adresses(modele.Copie);
        if (expediteur.Length == 0 || destinataires.Length == 0)
        {
            var quoi = expediteur.Length == 0 ? "aucun expediteur" : "aucun destinataire";
            var etiquettes = inconnues.Count == 0 ? "" : $" ; etiquette(s) sans valeur dans INFO_GREFFE : {string.Join(", ", inconnues.Distinct())}";
            throw new PreparationImpossible($"Courriel « {modele.Type} » : {quoi}{etiquettes}.");
        }

        var rendu = HabillageCourriel.Rendre(modele.Corps, modele.CorpsHtml, documents, valeurs.Habillage, valeurs.Jetons, valeurs.Logo,
            cause => journal.LogWarning(EvenementValeurManquante, "Courriel {Modele} du greffe {Greffe} : {Cause}", modele.Type, code, cause));
        return new CourrielPrepare(modele.Type, expediteur, reponse.Length == 0 ? null : reponse, destinataires, copie.Length == 0 ? null : copie,
            modele.Sujet + (demande.ComplementObjet ?? ""), modele.Corps, modele.CorpsHtml, rendu);
    }

    /// <summary>L'evenement de journal d'une valeur du greffe absente du courriel (la V2 : « Donnée greffe indisponible »).</summary>
    public static readonly EventId EvenementValeurManquante = new(51062, "ValeurCourrielManquante");

    private static async Task<int> EnregistrerAsync(SqlConnection cn, CourrielPrepare c, Guid suivi, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            INSERT INTO soc.CourrielEnvoye (Sujet, Corps, CorpsHtml, Expediteur, Reponse, Copie, Destinataires, DateEnvoi, Suivi, TypeModele)
            OUTPUT inserted.Id
            VALUES (@sujet, @corps, @html, @de, @reponse, @copie, @a, GETDATE(), @suivi, @modele);
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@sujet", SqlDbType.VarChar, 200) { Value = Couper(c.Sujet, 200) });
        cmd.Parameters.Add(new SqlParameter("@corps", SqlDbType.VarChar, 4000) { Value = Couper(c.Corps, 4000) });
        cmd.Parameters.Add(new SqlParameter("@html", SqlDbType.Bit) { Value = c.CorpsHtml });
        cmd.Parameters.Add(new SqlParameter("@de", SqlDbType.VarChar, 200) { Value = Couper(c.Expediteur, 200) });
        cmd.Parameters.Add(new SqlParameter("@reponse", SqlDbType.VarChar, 200) { Value = (object?)Couper(c.Reponse, 200) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@copie", SqlDbType.VarChar, 200) { Value = (object?)Couper(c.Copie, 200) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@a", SqlDbType.VarChar, 2000) { Value = Couper(c.Destinataires, 2000) });
        cmd.Parameters.Add(new SqlParameter("@suivi", SqlDbType.UniqueIdentifier) { Value = suivi });
        cmd.Parameters.Add(new SqlParameter("@modele", SqlDbType.VarChar, 40) { Value = c.TypeModele });
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Un modele du greffe, texte compris ; null s'il n'existe pas.</summary>
    public static async Task<ModeleComplet?> ModeleAsync(SqlConnection cn, string type, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT Type, Sujet, Corps, CorpsHtml, Expediteur, Reponse, Copie, DestinataireDefaut, ReserveAmitel, SignatureActive, Signature
            FROM soc.ModeleMessage WHERE Type = @t;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.VarChar, 40) { Value = type });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ModeleComplet(r.GetString(0), r.GetString(1), r.GetString(2), r.GetBoolean(3),
            r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7), r.GetBoolean(8), r.GetBoolean(9), r.IsDBNull(10) ? null : r.GetString(10));
    }

    /// <summary>
    /// Les modeles que l'appelant peut choisir (CA05) : un agent du greffe ne voit pas un modele reserve a Amitel, comme la
    /// liste des modeles du tableau des editions de la V2 (TreeviewEditions._GetMails, TreeviewEditions.cs:701-704).
    /// <paramref name="type"/> : ce seul modele (l'apercu passe par la meme regle, il n'en a pas d'autre).
    /// </summary>
    public static async Task<ModeleMessage[]> ModelesVisiblesAsync(SqlConnection cn, bool estAmitel, CancellationToken ct, string? type = null)
    {
        await using var cmd = new SqlCommand("""
            SELECT Type, LibelleEcran, Sujet, CorpsHtml, Expediteur, Reponse, Copie, DestinataireDefaut, ReserveAmitel,
                   EnvoiAutomatique, UtilisableEnSaisie, Lot1, Lot2, Inscription, Comptabilite
            FROM soc.ModeleMessage WHERE (@amitel = 1 OR ReserveAmitel = 0) AND (@type IS NULL OR Type = @type) ORDER BY Type;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@amitel", SqlDbType.Bit) { Value = estAmitel });
        cmd.Parameters.Add(new SqlParameter("@type", SqlDbType.VarChar, 40) { Value = (object?)type ?? DBNull.Value });
        var modeles = new List<ModeleMessage>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            modeles.Add(new ModeleMessage(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetBoolean(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.GetBoolean(8), r.GetBoolean(9), r.IsDBNull(10) ? null : r.GetBoolean(10),
                r.GetBoolean(11), r.GetBoolean(12), r.IsDBNull(13) ? null : r.GetBoolean(13), r.IsDBNull(14) ? null : r.GetBoolean(14)));
        return [.. modeles];
    }

    /// <summary>
    /// Les listes de distribution que l'agent <paramref name="compte"/> voit : les publiques (« * ») et les siennes, actives
    /// ou non (l'ecran cache les inactives, comme la V2). ListeDistributionMail.GetAllListeDistributionMailByLogin
    /// (ListeDistributionMail.cs:63) : <c>LSTDM_PRIVE = login OR LSTDM_PRIVE = '*'</c> -- la liste privee d'un collegue n'y est pas.
    /// </summary>
    public static async Task<ListeDiffusion[]> ListesVisiblesAsync(SqlConnection cn, string compte, CancellationToken ct, string? typeModele = null)
    {
        await using var cmd = new SqlCommand("""
            SELECT l.Id, l.Code, l.Libelle, l.Adresses, l.Rcs, l.Judiciaire, l.Prive, l.Actif
            FROM soc.ListeDiffusion l
            WHERE (l.Prive = @moi OR l.Prive = @publique)
              AND (@modele IS NULL OR EXISTS (SELECT 1 FROM soc.ModeleMessageListe ml WHERE ml.IdListe = l.Id AND ml.TypeModele = @modele))
            ORDER BY l.Code;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@moi", SqlDbType.VarChar, 200) { Value = compte });
        cmd.Parameters.Add(new SqlParameter("@publique", SqlDbType.VarChar, 200) { Value = RoutesCourriels.ListePublique });
        cmd.Parameters.Add(new SqlParameter("@modele", SqlDbType.VarChar, 40) { Value = (object?)typeModele ?? DBNull.Value });
        var listes = new List<ListeDiffusion>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            listes.Add(new ListeDiffusion(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetBoolean(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.GetBoolean(7)));
        return [.. listes];
    }

    /// <summary>Les deux documents de l'apercu, ceux de l'apercu de la V2 (OPE_EDIT_TEMPLATE_MAIL._Apercu) : ou les liens tombent.</summary>
    public static readonly IReadOnlyList<DocumentCourriel> DocumentsExemple =
        [new("Document n°1 (exemple)", "#"), new("Document n°2 (exemple)", "#")];

    /// <summary>
    /// Le modele <paramref name="type"/> tel qu'il partirait du greffe <paramref name="code"/>, sans rien envoyer ni ecrire :
    /// adresses resolues, listes par defaut, rendu avec l'habillage et les valeurs du greffe (ecran « Messagerie »).
    /// null s'il n'existe pas ou si l'appelant ne le voit pas (CA05, la meme regle que la liste).
    /// </summary>
    public async Task<ApercuModele?> ApercuAsync(string code, SqlConnection cn, string type, bool estAmitel, string compte, CancellationToken ct)
    {
        if ((await ModelesVisiblesAsync(cn, estAmitel, ct, type)).SingleOrDefault() is not { } modele) return null;
        if (await ModeleAsync(cn, modele.Type, ct) is not { } complet) return null;
        var valeurs = await LectureValeursGreffe.LireAsync(code, cn, catalogue, commune, ct);
        var manques = new List<string>(valeurs.Manques);

        var inconnues = new List<string>();
        string Adresses(string? liste) => HabillageCourriel.Nettoyer(LectureValeursGreffe.ResoudreAdresses(liste, valeurs.InfoGreffe, inconnues));
        var expediteur = Adresses(complet.Expediteur);
        var reponse = Adresses(complet.Reponse);
        var copie = Adresses(complet.Copie);
        var destinataire = Adresses(complet.DestinataireDefaut);
        manques.AddRange(inconnues.Distinct().Select(e => $"l'étiquette [{e}] n'a pas de valeur dans INFO_GREFFE : l'adresse reste vide"));

        var rendu = HabillageCourriel.Rendre(complet.Corps, complet.CorpsHtml, DocumentsExemple, valeurs.Habillage, valeurs.Jetons, valeurs.Logo,
            cause => manques.Add(cause));
        return new ApercuModele(modele, complet.Corps, expediteur, reponse, copie, destinataire,
            HabillageCourriel.Apercu(rendu, manques.Add), rendu.Habille, [.. manques],
            await ListesVisiblesAsync(cn, compte, ct, modele.Type), complet.SignatureActive, complet.Signature);
    }

    /// <summary>Le message d'une exception, et celui de sa cause s'il dit autre chose (SmtpException enveloppe souvent la vraie).</summary>
    private static string Message(Exception e)
        => e.InnerException is { } i && !e.Message.Contains(i.Message, StringComparison.Ordinal) ? $"{e.Message} ({i.Message})" : e.Message;

    private static string? Couper(string? s, int taille) => s is not null && s.Length > taille ? s[..taille] : s;
}

/// <summary>Un modele du greffe, texte et signature compris (soc.ModeleMessage).</summary>
public sealed record ModeleComplet(string Type, string Sujet, string Corps, bool CorpsHtml, string? Expediteur, string? Reponse,
                                   string? Copie, string? DestinataireDefaut, bool ReserveAmitel, bool SignatureActive = false,
                                   string? Signature = null);

/// <summary>Le courriel pret a partir : ce que la trace et soc.CourrielEnvoye retiennent, et son rendu.</summary>
/// <param name="Corps">Le texte du modele, tel que soc.CourrielEnvoye le garde (EMAILENV_CORPS : avant habillage).</param>
public sealed record CourrielPrepare(string TypeModele, string Expediteur, string? Reponse, string Destinataires, string? Copie,
                                     string Sujet, string Corps, bool CorpsHtml, RenduCourriel Rendu);

/// <summary>Le courriel ne peut pas etre prepare (modele inconnu, adresse vide) : rien n'est remis, la trace le dit.</summary>
public sealed class PreparationImpossible(string message) : Exception(message);
