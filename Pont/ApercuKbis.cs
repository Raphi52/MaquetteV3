using System.Globalization;

namespace RigV3Desktop.Pont;

/// <summary>Une ligne de l'extrait : son titre (colonne en italique) et sa valeur.</summary>
public sealed record LigneKbisVue(string Titre, string Libelle);

/// <summary>Une section de l'extrait, avec ses seules lignes renseignees.</summary>
public sealed record SectionKbisVue(string Titre, LigneKbisVue[] Lignes);

/// <summary>Ce que l'apercu Kbis affiche, valeurs deja mises en forme.</summary>
/// <param name="Fraicheur">La date du jour, « dd/MM/yyyy ».</param>
/// <param name="Horodatage">L'heure du rendu, « dd/MM/yyyy HH:mm:ss », au pied de page.</param>
public sealed record VueKbis(string GreffeLibelle, string GreffeAdresse, string Gestion, string Fraicheur,
                             SectionKbisVue[] Sections, string VilleRcs, string Horodatage, string Numero);

/// <summary>
/// Pont de l'apercu d'un extrait Kbis (web/src/composants/ApercuKbis.tsx), mise
/// en page NATIONALE de RIG v2.
///
/// Les regles du @code de Components/ApercuKbis.razor sont DEPLACEES ici telles
/// quelles : greffe tire du code greffe saisi, dates relues en culture invariante,
/// capital en « N2 » fr-FR, section sans ligne renseignee non affichee (comme la
/// XSL). Les VALEURS viennent du formulaire par la fonction <c>Lire</c> de
/// l'accueil, qui ne traverse pas vers React : la coquille appelle <see cref="Vue"/>
/// a chaque rendu (chaque frappe re-rend l'extrait) et passe le resultat a React.
/// Metier/ n'est pas modifie.
/// </summary>
public static class ApercuKbis
{
    /// <param name="forme">La forme juridique de l'affaire (INSCRIT_PM.FORME_JURIDIQUE_UTI), celle que
    /// la puce « Forme juridique » du bandeau montre : la fiche sommaire, ecrasee par ce qu'une tache a
    /// ecrit. Vide si l'affaire n'en a pas -- la ligne disparait. Le « Type de dossier » du formulaire
    /// (Personne morale...) n'est PAS une forme juridique : le Kbis l'imprimait a sa place jusqu'au
    /// 20/11/2026 (004176 : « Personne morale » sur le Kbis, « Societe civile immobiliere » au bandeau).</param>
    public static VueKbis Vue(string numero, string siren, string denomination, string forme,
                              Func<string, string, string> lire)
    {
        string Val(string etape, string colonne, string defaut = "")
        {
            var v = lire(etape, colonne);
            return string.IsNullOrWhiteSpace(v) ? defaut : v;
        }

        // L'entete vient du champ « Code greffe » du formulaire (colonne v2
        // ETABLISSEMENT.CODE_GREFFE). Tant qu'il n'est pas saisi, le greffe par
        // defaut (Amiens) tient lieu de repli : l'entete ne se vide jamais.
        var greffeDuDossier = Greffes.Trouver(Val("SIEGE", "ETBLS_CODE_GREFFE"));
        var villeRcs = greffeDuDossier.Ville;

        string Capital()
        {
            var montant = Val("CAPITAL", "CPTL_MONTANT");
            if (montant.Length == 0) return "";
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var texte = decimal.TryParse(montant, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)
                ? m.ToString("N2", fr) : montant;
            var variable = Val("CAPITAL", "CPTL_VARIABLE") == "Oui" ? " (variable)" : "";
            return $"{texte} {Val("CAPITAL", "CPTL_MONNAIE", "EUR")}{variable}";
        }

        var sections = new[]
        {
            Section("IDENTIFICATION DE LA PERSONNE MORALE",
            [
                ("Immatriculation au RCS, numero", siren + " R.C.S. " + villeRcs),
                ("Date d'immatriculation", Date(Val("IDENTITE", "DSSRC_DATE_IMMAT"))),
                ("Denomination ou raison sociale", Val("IDENTITE", "INSPM_DENOMINATION_STATUT", denomination)),
                ("Forme juridique", forme),
                ("Capital social", Capital()),
                ("Adresse du siege", Val("SIEGE", "ETBLS_ID_ADRBS")),
                ("Date de debut", Date(Val("SIEGE", "ETBLS_DATE_DEBUT"))),
            ]),
            Section("GESTION, DIRECTION, ADMINISTRATION, CONTROLE, ASSOCIES OU MEMBRES",
            [
                ("Qualite", Val("DIRIGEANTS", "DRGNT_FONCTION_DIRIGEANT")),
                ("Date d'entree en fonction", Date(Val("DIRIGEANTS", "DRGNT_DATE_ENTREE"))),
                ("SIREN", Val("DIRIGEANTS", "DRGNT_NUM_SIREN")),
            ]),
            Section("RENSEIGNEMENTS RELATIFS A L'ACTIVITE ET A L'ETABLISSEMENT PRINCIPAL",
            [
                ("Activite(s) exercee(s)", Val("ACTIVITE", "ACTVT_ACTIVITE_DECLAREE")),
                ("Date de commencement d'activite", Date(Val("ACTIVITE", "ACTVT_DATE_DEBUT"))),
                ("Code NAF", Val("ACTIVITE", "ACTVT_CODE_NAF")),
                ("Origine du fonds ou de l'activite", Val("SIEGE", "ETBLS_ORIGINE_FONDS")),
                ("Mode d'exploitation", Val("SIEGE", "ETBLS_MODE_EXPLOITATION")),
            ]),
        };

        return new VueKbis(
            greffeDuDossier.Libelle, greffeDuDossier.Adresse,
            Val("IDENTITE", "DSSRC_NUM_GESTION", "—"),
            DateTime.Today.ToString("dd/MM/yyyy"),
            sections.OfType<SectionKbisVue>().ToArray(),
            villeRcs, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), numero);
    }

    private static string Date(string v)
        => DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy") : v;

    // Comme la XSL : une section sans aucune ligne renseignee n'est pas affichee,
    // et une ligne sans libelle non plus.
    private static SectionKbisVue? Section(string titre, (string Titre, string Libelle)[] lignes)
    {
        var pleines = lignes.Where(l => !string.IsNullOrWhiteSpace(l.Libelle))
                            .Select(l => new LigneKbisVue(l.Titre, l.Libelle)).ToArray();
        return pleines.Length == 0 ? null : new SectionKbisVue(titre, pleines);
    }
}
