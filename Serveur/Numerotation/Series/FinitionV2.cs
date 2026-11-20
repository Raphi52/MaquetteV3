using System.Text;

namespace RigV3.Serveur.Numerotation.Series;

/// <summary>
/// Ce que la DLL de la V2 fait d'un numero APRES GENERER_NUMERO, recopie a la lettre pour que la V3
/// ecrive le meme numero (NUM-017 CA03, NUM-019 CA02). Source : RigMetier\Numerotation\Numerotation.cs
/// (D:\RigApplication\Source\Outils\DLL\RigMetier). Les cas d'erreur rendent ce que rend la V2 (chaine vide).
/// </summary>
public static class FinitionV2
{
    /// <summary>
    /// Le numero de liaison (serie LIAI, format « LGACK ») : l'etoile « * » est remplacee par la cle
    /// (Numerotation.cs:61-63).
    /// </summary>
    public static string AvecCleLiaison(string numero)
        => numero.Length > 0 && numero.Contains('*') ? numero.Replace("*", CleLiaison(numero)) : numero;

    /// <summary>
    /// La cle d'un numero de liaison « LGGGGCCCCCC » (Numerotation.cs:439-537) : la lettre devient un
    /// chiffre, un 0 est insere apres le premier chiffre du code greffe (« A » / « B » de la Corse : 10 / 11),
    /// puis une cle de Luhn (rangs impairs doubles, depuis la droite).
    /// </summary>
    public static string CleLiaison(string numero)
    {
        var propre = numero.Replace("*", "");
        if (propre.Length < 6) return "";
        var calcul = new StringBuilder();
        calcul.Append(propre[..1] switch
        {
            "G" or "V" => "1",
            "M" or "B" or "W" or "O" => "2",
            "I" or "X" => "3",
            "R" or "Y" => "4",
            "U" or "Z" => "5",
            "C" => "6",
            "S" => "7",
            "Q" => "8",
            "D" => "9",
            _ => "0",                       // « P » et toute autre lettre
        });
        calcul.Append(propre.Substring(1, 1));
        calcul.Append(propre.Substring(2, 1) switch { "A" => "10", "B" => "11", var c => "0" + c });
        calcul.Append(propre[3..]);
        if (!ulong.TryParse(calcul.ToString(), out _)) return "";
        var somme = 0;
        for (var i = 1; i <= calcul.Length; i++)
        {
            var chiffre = calcul[calcul.Length - i] - '0';
            if ((i & 1) == 0)
                somme += chiffre;
            else
            {
                chiffre *= 2;
                somme += chiffre / 10 + chiffre % 10;
            }
        }
        var multiple = (somme + 9) / 10 * 10;
        return (multiple - somme).ToString();
    }

    /// <summary>
    /// La cle d'une lettre recommandee : les 10 chiffres apres le prefixe de La Poste, somme ponderee
    /// (3 x rangs pairs + rangs impairs, depuis la droite) modulo 10 (Numerotation.cs:147-180).
    /// Comme la V2 (<c>int.TryParse</c>), 10 chiffres au-dela de 2 147 483 647 ne donnent pas de cle.
    /// </summary>
    public static string CleLettreRecommandee(string dixChiffres)
    {
        if (dixChiffres.Length != 10 || !int.TryParse(dixChiffres, out _)) return "";
        bool impaire = false;
        int sommePaire = 0, sommeImpaire = 0;
        for (var i = 0; i <= 9; i++)
        {
            var chiffre = dixChiffres[9 - i] - '0';
            if (impaire) { sommeImpaire += chiffre; impaire = false; }
            else { sommePaire += chiffre; impaire = true; }
        }
        var reste = (3 * sommePaire + sommeImpaire) % 10;
        return (reste == 0 ? 0 : 10 - reste).ToString();
    }

    /// <summary>
    /// Le numero d'une lettre recommandee au format national « CP NNN NNN NNNN C » a partir du numero
    /// de la serie LRN / LRARN (« 2C0347399241 ») ; chaine vide si la cle ne se calcule pas, comme
    /// GenererLR (Numerotation.cs:133-142).
    /// </summary>
    public static string LettreRecommandee(string numero)
    {
        if (numero.Length < 12) return "";
        var cle = CleLettreRecommandee(numero[2..]);
        if (cle == "") return "";
        return $"{numero[..2]} {numero.Substring(2, 3)} {numero.Substring(5, 3)} {numero.Substring(8, 4)} {cle}";
    }
}
