namespace RigV3Desktop;

/// <summary>
/// Controles de saisie du dossier RCS.
///
/// Les regles ne sont pas inventees : la cle du SIREN reprend l'algorithme
/// de Luhn de RIG v2
/// (Source/Outils/DLL/RigMetier/Numerotation/Numero.cs, IsNumLuhnValide) :
/// chiffres uniquement, doublement d'un rang sur deux en partant de la fin,
/// retenue soustraite de 10, et somme finale multiple de 10.
///
/// Fonctions PURES : elles ne touchent ni l'ecran ni le disque, ce qui les
/// rend verifiables sans lancer l'application.
/// </summary>
public static class ValiditeSaisie
{
    /// <summary>Cle de controle d'un SIREN, algorithme de RIG v2.</summary>
    public static bool LuhnValide(string numero)
    {
        if (string.IsNullOrEmpty(numero)) return false;
        foreach (var c in numero)
        {
            if (!char.IsDigit(c)) return false;
        }

        var somme = 0;
        for (var index = numero.Length; index >= 1; index--)
        {
            int part;
            if ((numero.Length + 1 - index) % 2 == 0)
            {
                part = (numero[index - 1] - '0') * 2;
                if (part > 9)
                {
                    somme += 1;
                    part -= 10;
                }
            }
            else
            {
                part = numero[index - 1] - '0';
            }
            somme += part;
        }
        return somme % 10 == 0;
    }

    /// <summary>Les espaces de presentation ne comptent pas.</summary>
    private static string Chiffres(string valeur)
        => new(valeur.Where(char.IsDigit).ToArray());

    /// <summary>
    /// Rend le motif du refus, ou null quand la valeur est acceptable.
    /// Un champ VIDE n'est jamais en erreur ici : l'absence est traitee a
    /// part, par le compte des champs requis, sinon un formulaire encore
    /// vierge s'afficherait tout en rouge.
    /// </summary>
    public static string? Refus(string controle, string valeur, DateTime aujourdhui)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return null;

        switch (controle)
        {
            case "siren":
            {
                var brut = Chiffres(valeur);
                if (brut.Length != 9) return "le SIREN compte 9 chiffres";
                if (!LuhnValide(brut)) return "cle de controle du SIREN incorrecte";
                return null;
            }

            case "date":
            {
                if (!DateTime.TryParse(valeur, out _)) return "date illisible";
                return null;
            }

            case "date-passee":
            {
                if (!DateTime.TryParse(valeur, out var d)) return "date illisible";
                if (d.Date > aujourdhui.Date) return "date posterieure a aujourd'hui";
                if (d.Year < 1800) return "date anterieure au registre";
                return null;
            }

            case "naf":
            {
                var t = valeur.Trim().ToUpperInvariant();
                var bon = t.Length == 5
                          && t.Take(4).All(char.IsDigit)
                          && char.IsLetter(t[4]);
                return bon ? null : "code sur 4 chiffres et une lettre, par exemple 6202A";
            }

            case "montant":
            {
                if (!decimal.TryParse(valeur, out var m)) return "montant illisible";
                if (m < 0) return "un montant ne peut pas etre negatif";
                return null;
            }

            case "taux":
            {
                if (!decimal.TryParse(valeur, out var t)) return "taux illisible";
                if (t < 0 || t > 100) return "un taux va de 0 a 100";
                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Coherence ENTRE deux dates d'une meme fiche : la seconde ne peut pas
    /// preceder la premiere.
    /// </summary>
    public static string? RefusOrdre(string? avant, string? apres, string libelleAvant)
    {
        if (string.IsNullOrWhiteSpace(avant) || string.IsNullOrWhiteSpace(apres)) return null;
        if (!DateTime.TryParse(avant, out var a) || !DateTime.TryParse(apres, out var b)) return null;
        return b.Date < a.Date ? $"anterieure a {libelleAvant}" : null;
    }
}
