namespace RigV3.Contrats.Socle;

// LE CHOIX DU GREFFE A L'OUVERTURE DE RIG (lot 10 du plan de reconstruction, 2026-11-21 ;
// mode d'emploi : docs/reconstruction/socle-identite.md, section « Choix du greffe »).
//
// La V2 (RigFormLogin.cs:282-319, _ChargerListeCodeGreffe :158-241) propose les greffes declares pour le
// poste (cles de registre HKLM, une par code greffe), puis ne garde que ceux ou le compte Windows de
// l'agent existe et n'est pas desactive. En V3, le poste declare ses codes (Pont/SessionAgent.cs) et le
// serveur, qui seul atteint les bases, dit ce que l'agent peut ouvrir parmi eux.

/// <summary>Les adresses du choix du greffe.</summary>
public static class RoutesOuverture
{
    /// <summary>GET <c>?codes=3801,8002</c> : pour chaque code demande, dans l'ordre, ce que l'appelant peut en
    /// ouvrir (<see cref="ChoixDuGreffe"/>). Il suffit d'une session Windows (401 sans elle) : le compte est
    /// cherche dans la base V3 de CHAQUE greffe, comme la V2 (Utilisateur.GetUtilisateurByName). Rien n'est ecrit.
    /// 400 sans code, ou au-dela de <see cref="CodesAuPlus"/>.</summary>
    public const string GreffesDuPoste = "/socle/ouverture/greffes";

    /// <summary>Le parametre de <see cref="GreffesDuPoste"/> : les codes separes par des virgules.</summary>
    public const string ParametreCodes = "codes";

    /// <summary>Au plus autant de codes par demande (une base ouverte par code).</summary>
    public const int CodesAuPlus = 100;

    /// <summary>L'adresse de <see cref="GreffesDuPoste"/> pour <paramref name="codes"/>.</summary>
    public static string Pour(IEnumerable<string> codes)
        => $"{GreffesDuPoste}?{ParametreCodes}={Uri.EscapeDataString(string.Join(",", codes))}";
}

/// <summary>Les valeurs de <see cref="GreffeDuPoste.Etat"/>, dans l'ordre des filtres de la V2.</summary>
public static class EtatsGreffeDuPoste
{
    /// <summary>Le compte existe dans la base du greffe et n'est pas desactive : la V2 le propose. L'admission
    /// (role, service) est jugee APRES le choix, par l'identite du greffe, comme CheckUser apres la connexion.</summary>
    public const string Ouvrable = "ouvrable";

    /// <summary>Le code n'est pas au catalogue commun des greffes.</summary>
    public const string HorsCatalogue = "hors-catalogue";

    /// <summary>La base V3 du greffe ne repond pas (V2 : « Config ... Erronée », la base n'est pas en ligne).</summary>
    public const string BaseInjoignable = "base-injoignable";

    /// <summary>Le compte Windows n'est pas declare dans la base du greffe (V2 : <c>user == null</c>).</summary>
    public const string CompteAbsent = "compte-absent";

    /// <summary>Le compte est desactive dans la base du greffe (V2 : <c>UTLST_DESACTIVE</c>).</summary>
    public const string CompteDesactive = "compte-desactive";
}

/// <summary>Un greffe declare pour le poste, et ce que l'appelant peut en faire.</summary>
/// <param name="Code">Le code greffe demande (« 3801 »).</param>
/// <param name="Nom">Son nom au catalogue, pour un greffe <see cref="EtatsGreffeDuPoste.Ouvrable"/> seulement : un greffe
/// qui ne connait pas l'agent ne lui dit rien de plus que son code (esprit de SOC-001 CA03).</param>
/// <param name="Etat">Une valeur de <see cref="EtatsGreffeDuPoste"/>.</param>
public sealed record GreffeDuPoste(string Code, string? Nom, string Etat)
{
    /// <summary>Vrai quand l'ecran de choix le propose.</summary>
    public bool Ouvrable => Etat == EtatsGreffeDuPoste.Ouvrable;
}

/// <summary>La reponse de <see cref="RoutesOuverture.GreffesDuPoste"/>.</summary>
/// <param name="Compte">Le compte Windows de l'appelant, tel que le serveur l'a recu (sans domaine).</param>
/// <param name="Greffes">Un element par code demande, dans l'ordre de la demande.</param>
public sealed record ChoixDuGreffe(string Compte, GreffeDuPoste[] Greffes);

// fix-ok: pas un correctif -- fichier neuf du lot 10, ecrit en trois temps (le contrat, le nom donne pour un greffe ouvrable seulement, la reponse ChoixDuGreffe qui porte le compte vu par le serveur).
