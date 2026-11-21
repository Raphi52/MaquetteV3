using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Identite;
using RigV3.Serveur.Socle.Supervision;
using RigV3.Serveur.Socle.Versions;

namespace RigV3.Serveur.Socle.Certification;

/// <summary>
/// SOC-019 CA04, T03 (lot 17 du plan « Reconstruction de RIG — phase 2 », 2026-11-21) : le certificat NF525 de la V3.
/// <list type="bullet">
/// <item>GET /socle/greffes/{greffe}/certificat : tout agent admis du greffe retrouve le certificat que l'organisme accredite
/// a delivre a Amitel, avec sa date de validite, et ce qu'il vaut aujourd'hui pour la version en service (valide, expire,
/// autre version...) ; sans certificat enregistre : « certification en cours », jamais un faux certificat.</item>
/// <item>PUT /socle/certificat : l'exploitation d'Amitel enregistre le certificat recu (le fichier certificat-v3.json du dossier
/// de la beta), au journal des gestes de la base commune. Un certificat incomplet est refuse.</item>
/// </list>
/// La regle de validite est celle du critere de demarrage de la beta (<see cref="RegleCertificat"/>, Contrats). Le certificat
/// vit dans la base commune (soc.CertificatLogiciel, Base\migrations\SOC-019_certificat.sql) : un seul pour les 40 greffes.
/// </summary>
internal sealed class ModuleCertification : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        // L'agent est juge dans la base de SON greffe ({greffe}) ; le certificat se lit dans la base commune.
        routes.MapGet(RoutesCertification.Certificat, async (string greffe, BaseRig commune, CancellationToken ct) =>
            {
                var (certificat, enregistreLe) = await LireAsync(commune, ct);
                return Results.Ok(Etat(greffe, certificat, enregistreLe));
            })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapPut(RoutesCertification.Enregistrer, async (Certificat c, HttpContext h, BaseRig commune, ServiceDroits droits, CancellationToken ct) =>
            {
                if (RegleCertificat.RefusEnregistrement(c) is { } refus) return Results.BadRequest(new RefusGreffe(refus));
                await using var cn = await commune.OuvrirAsync(ct);
                var auteur = (await droits.AppelantAsync(h, commune, cn, ct))!;
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
                await using (var cmd = new SqlCommand("""
                    INSERT INTO soc.CertificatLogiciel (Logiciel, Organisme, Accreditation, Numero, Referentiel, VersionCouverte, DelivreLe, ValideJusquau, EnregistrePar)
                    VALUES (@logiciel, @organisme, @accreditation, @numero, @referentiel, @version, @delivre, @valide, @par);
                    """, cn, tx))
                {
                    cmd.Parameters.Add(new SqlParameter("@logiciel", SqlDbType.VarChar, 10) { Value = c.Logiciel });
                    cmd.Parameters.Add(new SqlParameter("@organisme", SqlDbType.NVarChar, 200) { Value = c.Organisme.Trim() });
                    cmd.Parameters.Add(new SqlParameter("@accreditation", SqlDbType.NVarChar, 100) { Value = c.Accreditation.Trim() });
                    cmd.Parameters.Add(new SqlParameter("@numero", SqlDbType.NVarChar, 100) { Value = c.Numero.Trim() });
                    cmd.Parameters.Add(new SqlParameter("@referentiel", SqlDbType.NVarChar, 100) { Value = c.Referentiel.Trim() });
                    cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.VarChar, 20) { Value = c.VersionCouverte.Trim() });
                    cmd.Parameters.Add(new SqlParameter("@delivre", SqlDbType.Date) { Value = c.DelivreLe.ToDateTime(TimeOnly.MinValue) });
                    cmd.Parameters.Add(new SqlParameter("@valide", SqlDbType.Date) { Value = c.ValideJusquau.ToDateTime(TimeOnly.MinValue) });
                    cmd.Parameters.Add(new SqlParameter("@par", SqlDbType.NVarChar, 80) { Value = auteur.NomAffiche });
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await JournalGestes.EcrireAsync(cn, tx, auteur, "Certificat NF525 de la V3", "certificat", $"le certificat {c.Numero.Trim()}",
                    c.Numero.Trim(), "a cree", null, c, ct);
                await tx.CommitAsync(ct);
                var (certificat, enregistreLe) = await LireAsync(commune, ct);
                return Results.Ok(Etat(null, certificat, enregistreLe));
            })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis, PolitiquesSupervision.ExploitationAmitel);
    }

    /// <summary>Ce que vaut le certificat aujourd'hui (jour du serveur), pour la version de ce serveur.</summary>
    private static EtatCertificat Etat(string? greffe, Certificat? certificat, DateTime? enregistreLe)
    {
        var aujourdhui = DateOnly.FromDateTime(DateTime.Today);
        var (etat, motifs) = RegleCertificat.Juger(certificat, aujourdhui, ModuleVersions.Version);
        return new EtatCertificat(greffe, etat, certificat, ModuleVersions.Version, aujourdhui, motifs, enregistreLe);
    }

    /// <summary>Le dernier certificat de la V3 enregistre dans la base commune ; null s'il n'y en a aucun.</summary>
    private static async Task<(Certificat? Certificat, DateTime? EnregistreLe)> LireAsync(BaseRig commune, CancellationToken ct)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) Logiciel, Organisme, Accreditation, Numero, Referentiel, VersionCouverte, DelivreLe, ValideJusquau, EnregistreLe
            FROM soc.CertificatLogiciel WHERE Logiciel = @logiciel
            ORDER BY EnregistreLe DESC, Id DESC;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@logiciel", SqlDbType.VarChar, 10) { Value = RoutesCertification.LogicielV3 });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return (null, null);
        return (new Certificat(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                               DateOnly.FromDateTime(r.GetDateTime(6)), DateOnly.FromDateTime(r.GetDateTime(7))),
                DateTime.SpecifyKind(r.GetDateTime(8), DateTimeKind.Utc));
    }
}
