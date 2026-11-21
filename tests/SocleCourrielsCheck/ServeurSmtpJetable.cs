using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SocleCourrielsCheck;

/// <summary>Un courriel recu par le serveur jetable : l'enveloppe (MAIL FROM, RCPT TO acceptes) et le message brut.</summary>
public sealed record CourrielRecu(string De, IReadOnlyList<string> Pour, string Donnees);

/// <summary>
/// UN SERVEUR DE MESSAGERIE JETABLE, en memoire, sur 127.0.0.1 et un port libre : le « serveur SMTP jetable » de la
/// regle du plan (aucun vrai courriel). Il parle juste assez de SMTP (RFC 5321) pour System.Net.Mail.SmtpClient :
/// 220, EHLO/HELO, MAIL FROM, RCPT TO, DATA, RSET, QUIT. Il ACCEPTE tout destinataire, sauf une adresse qui contient
/// « refuse », qu'il REFUSE comme un vrai serveur (550 5.1.1) : c'est le cas « le serveur de messagerie le refuse » de
/// SOC-006 CA03. Il ne transmet rien a personne : il garde ce qu'il recoit.
/// </summary>
public sealed class ServeurSmtpJetable : IAsyncDisposable
{
    private readonly TcpListener ecoute = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource fin = new();
    private readonly Task boucle;

    public ServeurSmtpJetable()
    {
        ecoute.Start();
        Port = ((IPEndPoint)ecoute.LocalEndpoint).Port;
        boucle = Task.Run(EcouterAsync);
    }

    public int Port { get; }

    /// <summary>Les courriels acceptes, dans l'ordre.</summary>
    public ConcurrentQueue<CourrielRecu> Recus { get; } = new();

    /// <summary>Les connexions ouvertes (un appel du serveur de la V3, accepte ou non).</summary>
    public int Connexions => connexions;
    private int connexions;

    private async Task EcouterAsync()
    {
        while (!fin.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await ecoute.AcceptTcpClientAsync(fin.Token); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            Interlocked.Increment(ref connexions);
            _ = Task.Run(() => ServirAsync(client));
        }
    }

    private async Task ServirAsync(TcpClient client)
    {
        using (client)
        {
            var flux = client.GetStream();
            var lecteur = new StreamReader(flux, Encoding.ASCII);
            var ecrivain = new StreamWriter(flux, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await ecrivain.WriteLineAsync("220 banc.local ESMTP jetable du banc SocleCourrielsCheck");
            var de = "";
            var pour = new List<string>();
            while (await lecteur.ReadLineAsync() is { } ligne)
            {
                var commande = ligne.ToUpperInvariant();
                if (commande.StartsWith("EHLO") || commande.StartsWith("HELO")) await ecrivain.WriteLineAsync("250 banc.local");
                else if (commande.StartsWith("MAIL FROM:")) { de = ligne[10..].Trim(); pour.Clear(); await ecrivain.WriteLineAsync("250 2.1.0 OK"); }
                else if (commande.StartsWith("RCPT TO:"))
                {
                    var adresse = ligne[8..].Trim();
                    if (adresse.Contains("refuse", StringComparison.OrdinalIgnoreCase))
                        await ecrivain.WriteLineAsync("550 5.1.1 adresse inconnue du serveur jetable");
                    else { pour.Add(adresse); await ecrivain.WriteLineAsync("250 2.1.5 OK"); }
                }
                else if (commande == "DATA")
                {
                    await ecrivain.WriteLineAsync("354 fin par <CRLF>.<CRLF>");
                    var donnees = new StringBuilder();
                    while (await lecteur.ReadLineAsync() is { } l && l != ".")
                        donnees.Append(l.StartsWith("..", StringComparison.Ordinal) ? l[1..] : l).Append("\r\n");
                    Recus.Enqueue(new CourrielRecu(de, [.. pour], donnees.ToString()));
                    await ecrivain.WriteLineAsync("250 2.0.0 recu");
                }
                else if (commande == "QUIT") { await ecrivain.WriteLineAsync("221 2.0.0 au revoir"); return; }
                else if (commande == "RSET") { pour.Clear(); await ecrivain.WriteLineAsync("250 2.0.0 OK"); }
                else await ecrivain.WriteLineAsync("250 2.0.0 OK");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        fin.Cancel();
        ecoute.Stop();
        try { await boucle; } catch (OperationCanceledException) { }
        fin.Dispose();
    }
}
