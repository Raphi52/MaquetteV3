// Mise en forme des nombres, a l'identique du C# des ecrans Blazor.
//
// Les ecrans .razor ecrivaient `x.ToString("N2")` sous la culture du poste,
// fr-FR : separateur decimal « , » et groupe de milliers par l'espace fine
// insecable U+202F (donnees ICU, les memes pour .NET et pour Intl). La culture
// est ecrite ici en dur, et non lue dans le navigateur : RIG est une
// application francaise, et une WebView reglee en anglais afficherait sinon
// « 1,234.50 » la ou Blazor affichait « 1 234,50 ».

const formats = new Map<number, Intl.NumberFormat>()

/** L'equivalent de `ToString("N<decimales>")` en fr-FR. */
export function nombre(valeur: number, decimales: number): string {
  let f = formats.get(decimales)
  if (!f) {
    f = new Intl.NumberFormat('fr-FR', {
      minimumFractionDigits: decimales,
      maximumFractionDigits: decimales,
      useGrouping: true,
    })
    formats.set(decimales, f)
  }
  return f.format(valeur)
}

/**
 * Un montant en euros, tenu en CENTIMES entiers. Le C# calculait en decimal,
 * exact ; en nombres flottants, 458,63 - 458,63 peut valoir 1e-13 et afficher
 * « Solde restant 0,00 » au lieu de « Facture soldee ».
 */
export function centimes(euros: number): number {
  return Math.round(euros * 100)
}

/** Des centimes affiches comme le `ToString("N2")` d'un decimal en euros. */
export function euros(centimesEntiers: number): string {
  return nombre(centimesEntiers / 100, 2)
}
