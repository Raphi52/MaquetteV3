namespace RigV3.Contrats.Socle;

// Le certificat NF525 de la V3 (SOC-019, lot 17 du plan « Reconstruction de RIG — phase 2 », 2026-11-21).
// Le certificat est delivre par l'organisme a Amitel, qui l'enregistre UNE fois dans la base commune
// (PUT RoutesCertification.Enregistrer, exploitation d'Amitel) ; chaque greffe le relit dans la V3, avec sa
// validite, sans passer par l'assistance (CA04 : GET RoutesCertification.Certificat, ecran « Certificat NF525 de la
// V3 », Pont/EcranCertificat.cs). La regle de validite est la MEME que celle du critere de demarrage de la beta
// (Outils/Passage/Beta.cs, CriteresBeta, SOC-019 CA01) : elle vit ici pour que l'outil, le serveur et l'ecran ne
// puissent pas diverger. Mode d'emploi : docs/reconstruction/socle-exploitation.md, partie du lot 17.

/// <summary>Un certificat (de la V3 ou de la V2) tel que l'organisme le delivre : SOC-019 CA01, CA04, CA05. C'est aussi
/// le format des fichiers certificat-v3.json / certificat-v2.json du dossier de la beta (Outils/Passage/Beta.cs).</summary>
/// <param name="Logiciel">« V3 » ou « V2 ».</param>
/// <param name="Accreditation">Le numero d'accreditation de l'organisme (COFRAC...) : seul un organisme accredite est admis
/// (BOI-TVA-DECLA-30-10-30).</param>
/// <param name="VersionCouverte">La version majeure couverte (« 2026.1 ») : une revision mineure n'en change pas.</param>
public sealed record Certificat(string Logiciel, string Organisme, string Accreditation, string Numero, string Referentiel,
                                string VersionCouverte, DateOnly DelivreLe, DateOnly ValideJusquau);

/// <summary>Les adresses du certificat.</summary>
public static class RoutesCertification
{
    /// <summary>GET : le certificat de la V3 et ce qu'il vaut aujourd'hui pour la version en service (<see cref="EtatCertificat"/>).
    /// Tout agent admis du greffe : le greffe le retrouve sans passer par l'assistance (SOC-019 CA04).</summary>
    public const string Certificat = "/socle/greffes/{greffe}/certificat";

    /// <summary>PUT <see cref="Socle.Certificat"/> : Amitel enregistre le certificat que l'organisme lui a delivre (exploitation
    /// d'Amitel seule, 403 sinon) ; rend son <see cref="EtatCertificat"/>. Un certificat incomplet est refuse (400).</summary>
    public const string Enregistrer = "/socle/certificat";

    /// <summary>Le logiciel dont la V3 montre le certificat : le sien (la V2 garde le sien, CA05).</summary>
    public const string LogicielV3 = "V3";

    /// <summary>L'adresse de <see cref="Certificat"/> pour un code greffe.</summary>
    public static string Pour(string greffe) => Certificat.Replace("{greffe}", Uri.EscapeDataString(greffe));
}

/// <summary>Ce que vaut le certificat aujourd'hui (<see cref="RegleCertificat.Juger"/>).</summary>
public static class EtatsCertificat
{
    /// <summary>Delivre par un organisme accredite, en cours de validite, couvre la version en service.</summary>
    public const string Valide = "valide";

    /// <summary>Aucun certificat n'est enregistre : la certification est en cours. Jamais un faux certificat a la place.</summary>
    public const string EnCours = "certification en cours";

    /// <summary>Sa date de validite est passee.</summary>
    public const string Expire = "expire";

    /// <summary>Delivre pour une date a venir.</summary>
    public const string PasEncoreValide = "pas encore valide";

    /// <summary>Il couvre une autre version majeure que celle en service (Directory.Build.props).</summary>
    public const string AutreVersion = "autre version";

    /// <summary>Organisme, accreditation ou numero absent : ce n'est pas le certificat d'un organisme accredite.</summary>
    public const string Incomplet = "incomplet";
}

/// <summary>Le certificat de la V3 vu par un greffe (SOC-019 CA04).</summary>
/// <param name="Greffe">Le greffe qui le demande ; null pour la reponse a l'enregistrement (commune).</param>
/// <param name="Etat">Voir <see cref="EtatsCertificat"/>.</param>
/// <param name="Certificat">Le certificat enregistre par Amitel ; null s'il n'y en a aucun (certification en cours).</param>
/// <param name="VersionEnService">La version de la V3 en service (celle du serveur, Directory.Build.props).</param>
/// <param name="Aujourdhui">Le jour du serveur auquel la validite est jugee.</param>
/// <param name="Motifs">Ce qui fait qu'il n'est pas valide, en clair ; vide s'il l'est.</param>
/// <param name="EnregistreLe">Quand Amitel l'a enregistre (UTC).</param>
public sealed record EtatCertificat(string? Greffe, string Etat, Certificat? Certificat, string VersionEnService, DateOnly Aujourdhui,
                                    string[] Motifs, DateTime? EnregistreLe);

/// <summary>
/// LA REGLE DE VALIDITE d'un certificat (SOC-019 CA01, CA04, CA05) : organisme et accreditation presents (seul un
/// organisme accredite est admis), un numero, delivre au plus tard aujourd'hui, valide jusqu'a aujourd'hui compris, et,
/// pour la V3, la version majeure en service couverte. Lue par le critere de demarrage de la beta (Outils/Passage) et par
/// la route du certificat (Serveur/Socle/Certification).
/// </summary>
public static class RegleCertificat
{
    /// <summary>Ce qui manque au certificat, en clair ; vide s'il est valide. <paramref name="version"/> null : la version
    /// n'est pas jugee (certificat de la V2, CA05).</summary>
    public static IReadOnlyList<string> Manques(Certificat cert, DateOnly aujourdhui, string? version)
        => [.. Juger(cert, aujourdhui, version).Motifs];

    /// <summary>L'etat du certificat (<see cref="EtatsCertificat"/>) et ses motifs ; sans certificat : « certification en cours ».</summary>
    public static (string Etat, string[] Motifs) Juger(Certificat? cert, DateOnly aujourdhui, string? version)
    {
        if (cert is null)
            return (EtatsCertificat.EnCours, ["Aucun certificat n'est encore enregistré par Amitel : la certification de la V3 est en cours."]);
        var motifs = new List<(string Etat, string Motif)>();
        if (string.IsNullOrWhiteSpace(cert.Organisme) || string.IsNullOrWhiteSpace(cert.Accreditation))
            motifs.Add((EtatsCertificat.Incomplet, "organisme ou accréditation absent : seul le certificat d'un organisme accrédité est admis (BOI-TVA-DECLA-30-10-30)"));
        if (string.IsNullOrWhiteSpace(cert.Numero))
            motifs.Add((EtatsCertificat.Incomplet, "numéro du certificat absent"));
        if (cert.ValideJusquau < aujourdhui)
            motifs.Add((EtatsCertificat.Expire, $"expiré le {cert.ValideJusquau:dd/MM/yyyy}"));
        if (cert.DelivreLe > aujourdhui)
            motifs.Add((EtatsCertificat.PasEncoreValide, $"délivré pour le {cert.DelivreLe:dd/MM/yyyy}, pas encore valide"));
        if (version is not null && !VersionCouverte(cert.VersionCouverte, version))
            motifs.Add((EtatsCertificat.AutreVersion, $"couvre la version {cert.VersionCouverte}, pas la version {version} en service"));
        return motifs.Count == 0 ? (EtatsCertificat.Valide, []) : (motifs[0].Etat, [.. motifs.Select(m => m.Motif)]);
    }

    /// <summary>« 2026.1 » couvre 2026.1.x : le certificat couvre une version majeure (annee.version), une revision
    /// mineure (evenement 255) n'en change pas, une majeure (250) passe par le suivi (SOC-019 CA03).</summary>
    public static bool VersionCouverte(string couverte, string version)
    {
        static string Majeure(string v) => string.Join('.', (v ?? string.Empty).Trim().Split('.').Take(2));
        return Majeure(couverte).Length > 0 && Majeure(couverte) == Majeure(version);
    }

    /// <summary>Le refus d'enregistrer un certificat qui n'en est pas un (motif en clair) ; null s'il peut l'etre. Un certificat
    /// expire ou d'une autre version S'ENREGISTRE (c'est un vrai certificat) : la route le dit tel quel aux greffes.</summary>
    public static string? RefusEnregistrement(Certificat? cert)
    {
        if (cert is null) return "Le corps doit être le certificat (format de certificat-v3.json).";
        if (cert.Logiciel != RoutesCertification.LogicielV3) return $"La V3 n'enregistre que son propre certificat (logiciel « {RoutesCertification.LogicielV3} »), pas « {cert.Logiciel} ».";
        var vides = new[] { ("organisme", cert.Organisme), ("accréditation", cert.Accreditation), ("numéro", cert.Numero),
                            ("référentiel", cert.Referentiel), ("version couverte", cert.VersionCouverte) }
            .Where(c => string.IsNullOrWhiteSpace(c.Item2)).Select(c => c.Item1).ToList();
        if (vides.Count > 0) return $"Certificat incomplet ({string.Join(", ", vides)}) : seul le certificat d'un organisme accrédité s'enregistre.";
        if (cert.ValideJusquau < cert.DelivreLe) return "La date de fin de validité précède la date de délivrance.";
        return null;
    }
}
