using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// L'ADRESSE D'UN DESTINATAIRE D'EDITION comme RIG v2 l'ecrit dans la liste LISTE_ENTITE_DESTINATION (plan « RIG v3 100 %
/// fonctionnel », lot 11, 27/11/2026) : pour un mandataire de procedure, ENTITE.ADRESSE = TransfoTexteEnXml(adr.AdresseMultiLignesPourEcran)
/// (Outils\DLL\RigMetier\JUDICIAIRE\DestinatairesEditionsLot2.cs:1637-1640, :1091-1103). Utilise par EcrivainPublicationsBodacc
/// (le certificat de parution au mandataire) ; l'adresse d'un huissier intervenant (EcrivainConvocations, en-tete) en a le meme besoin.
///
/// SOURCE (ancien RIG, Outils\DLL\RigMetier\ADRESSE\AdresseBase.cs) : AdresseMultiLignesPourEcran (:1081-1086) = _AdresseMultiLignes
/// (ReturnAdresseCompleteMinouMaj(0, false), « \n » retires, :1059-1067) dont chaque « \r » devient un saut de ligne ;
/// ReturnAdresseCompleteMinouMaj (:928-993), separateur « \r », une ligne par bloc non vide :
///   - le type special (T « Toque », C « Case », B « Boîte Greffe », O « Boîte Ordre ») suivi de « n° <valeur> » ;
///   - la pre-adresse ;
///   - numero, complement, libelle du type de voie (TYPE_VOIE.R_TYPEVOIE_LIBELLE_ECRAN, rien s'il n'est pas trouve), nom de voie ;
///   - « BP <boite postale> » et la suite d'adresse ;
///   - code postal, localite, puis « CEDEX » si le cedex est rempli, puis le cedex s'il n'est pas « . » (tel quel, :977-978) ;
///   - le libelle du pays (PAYS.R_PAYS_LIBELLE_ECRAN) s'il n'est ni vide ni « FRA » et qu'il est trouve, SANS separateur apres ;
/// puis Trim et les doubles espaces reduits. Une table de reference absente de la base du greffe (une base de banc) vaut « non
/// trouve », comme une valeur absente de la table dans RIG v2 (_TypeVoie / Pays null).
/// Dans la base V3, ADRESSE_BASE est la fiche rec.Adresse (Serveur\Recopie\FichesV3.cs, REC-016 : Id = ADRBS_ID_ADRBS).
/// </summary>
internal static class AdresseV2
{
    /// <summary>L'adresse <paramref name="idAdresse"/> sur plusieurs lignes (AdresseMultiLignesPourEcran) ; vide sans adresse.</summary>
    internal static async Task<string> MultiLignesAsync(GesteEnCours geste, int? idAdresse, CancellationToken ct)
    {
        if (idAdresse is not { } id) return "";
        var typeVoie = await TableAsync(geste, "v2.TYPE_VOIE", ct)
            ? "(SELECT TOP (1) tv.R_TYPEVOIE_LIBELLE_ECRAN FROM v2.TYPE_VOIE AS tv WHERE tv.R_TYPEVOIE_VALEUR = a.TypeVoie)" : "NULL";
        var pays = await TableAsync(geste, "v2.PAYS", ct)
            ? "(SELECT TOP (1) p.R_PAYS_LIBELLE_ECRAN FROM v2.PAYS AS p WHERE p.R_PAYS_VALEUR = a.Pays)" : "NULL";
        await using var r = await geste.LireAsync($"""
            SELECT a.TypeSpecial, a.ValeurSpecial, a.PreAdresse, a.NumeroVoie, a.ComplementNumero, {typeVoie} AS LibelleVoie, a.NomVoie,
                   a.BoitePostale, a.SuiteAdresse, a.CodePostal, a.Localite, a.Cedex, a.Pays, {pays} AS LibellePays
            FROM rec.Adresse AS a WHERE a.Id = @a
            """, ct, new SqlParameter("@a", id));
        if (!await r.ReadAsync(ct)) return "";
        string T(string c) => FormeLigne.Texte(r, c);
        var libellePays = r["LibellePays"] is DBNull ? null : T("LibellePays");
        return Composer(T("TypeSpecial"), T("ValeurSpecial"), T("PreAdresse"), T("NumeroVoie"), T("ComplementNumero"),
                        r["LibelleVoie"] is DBNull ? null : T("LibelleVoie"), T("NomVoie"), T("BoitePostale"), T("SuiteAdresse"),
                        T("CodePostal"), T("Localite"), T("Cedex"), T("Pays"), libellePays);
    }

    /// <summary>ReturnAdresseCompleteMinouMaj(0, false) puis AdresseMultiLignesPourEcran (en tete), champ par champ.</summary>
    internal static string Composer(string typeSpecial, string valeurSpecial, string preAdresse, string numeroVoie, string complementNumero,
        string? libelleVoie, string nomVoie, string boitePostale, string suiteAdresse, string codePostal, string localite, string cedex,
        string pays, string? libellePays)
    {
        const string Suite = "\r";
        var retour = "";
        var special = typeSpecial switch { "T" => "Toque", "C" => "Case", "B" => "Boîte Greffe", "O" => "Boîte Ordre", _ => "" };
        var temp = "";
        if (special != "")
        {
            temp = special;
            if (valeurSpecial != "") temp += " n° " + valeurSpecial;
        }
        if (temp != "") retour += temp.Trim() + Suite;
        if (preAdresse != "") retour += preAdresse + Suite;
        temp = "";
        if (numeroVoie != "") temp += numeroVoie + " ";
        if (complementNumero != "") temp += complementNumero + " ";
        if (libelleVoie is { Length: > 0 }) temp += libelleVoie + " ";
        if (nomVoie != "") temp += nomVoie;
        if (temp != "") retour += temp.Trim() + Suite;
        temp = "";
        if (boitePostale != "") temp += "BP " + boitePostale + " ";
        if (suiteAdresse != "") temp += suiteAdresse;
        if (temp != "") retour += temp.Trim() + Suite;
        temp = "";
        if (codePostal != "") temp += codePostal + " ";
        if (localite != "") temp += localite + " ";
        if (cedex != "") temp += "CEDEX ";
        if (cedex != ".") temp += cedex + " ";
        // Comme la V2 (:979) : un cedex vide laisse une espace dans temp, donc une ligne vide (avant le pays) sans code postal ni localite.
        if (temp != "") retour += temp.Trim() + Suite;
        if (pays != "" && pays != "FRA" && libellePays is not null) retour += libellePays;

        retour = retour.Trim();
        while (retour.IndexOf("  ", StringComparison.Ordinal) > 0) retour = retour.Replace("  ", " ", StringComparison.Ordinal);
        return retour.Replace("\n", "", StringComparison.Ordinal).Replace("\r", "\r\n", StringComparison.Ordinal);
    }

    /// <summary>TransfoTexteEnXml (DestinatairesEditionsLot2.cs:1091-1103) : le texte tel que RIG v2 le pose dans l'attribut ADRESSE
    /// d'une ENTITE (puis la serialisation XML l'echappe encore une fois, comme XElement ici).</summary>
    internal static string TexteEnXml(string texte)
    {
        if (string.IsNullOrEmpty(texte)) return texte;
        return texte.Replace("&", "&amp;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal)
                    .Replace("\"", "&quot;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
                    .Replace("<", "&lt;", StringComparison.Ordinal).Replace("\r\n", "&amp;crlf;", StringComparison.Ordinal)
                    .Replace("\r", "&amp;crlf;", StringComparison.Ordinal).Replace('\u001F', ' ');
    }

    private static async Task<bool> TableAsync(GesteEnCours geste, string table, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT CONVERT(bit, CASE WHEN OBJECT_ID(@t) IS NULL THEN 0 ELSE 1 END)", ct, new SqlParameter("@t", table));
        return await r.ReadAsync(ct) && r.GetBoolean(0);
    }
}
