// L'icone d'une nature d'element GED, dessinee en SVG inline.
// Porte en React (lot 6 de la migration, 19/11/2026) depuis
// Components/IconeGed.razor, devenu une coquille pour Home.razor.
//
// Inline et non fichier : la fenetre de creation en affiche une quinzaine
// d'un coup, et un trait `currentColor` suit la couleur du texte, donc le
// theme clair comme le theme sombre, sans seconde image.
//
// Les traces sont les MEMES chaines que la MarkupString du .razor, posees
// telles quelles dans le <svg> : meme DOM, blancs compris. Ce sont des
// constantes de ce fichier, jamais une donnee saisie.

const CHEMISE = `
        <path d='M3 7.5A1.5 1.5 0 014.5 6h4l2 2.5h9A1.5 1.5 0 0121 10v8.5A1.5 1.5 0 0119.5 20h-15A1.5 1.5 0 013 18.5z' />`

const FEUILLE = `
        <path d='M14 3H7.5A1.5 1.5 0 006 4.5v15A1.5 1.5 0 007.5 21h9a1.5 1.5 0 001.5-1.5V7z' />
        <path d='M14 3v4h4' />`

/** Le dessin d'une nature (voir `AccesGed.Natures`) ; tout le reste (PDF compris) : la feuille. */
export function dessinGed(nature: string): string {
  switch (nature) {
    // Dossier : la chemise a onglet.
    case 'dossier': return CHEMISE
    // Sous-dossier : la meme chemise, marquee d'un plus.
    case 'sous-dossier': return CHEMISE + `
            <path d='M12 11v5' /><path d='M9.5 13.5h5' />`
    // Image : cadre, soleil et montagne.
    case 'jpg': return `
            <rect x='3' y='4' width='18' height='16' rx='2' />
            <circle cx='8.5' cy='9.5' r='1.5' />
            <path d='M3 17l5-4 3.5 3 3-2.5L21 18' />`
    // Numerisation : la vitre du scanner et sa barre lumineuse.
    case 'tif': return `
            <rect x='3' y='4' width='18' height='11' rx='1.5' />
            <path d='M2 18h20' /><path d='M7 21h10' />`
    // Courrier : l'enveloppe.
    case 'courrier': return `
            <rect x='2.5' y='5' width='19' height='14' rx='2' />
            <path d='M3 7l9 6 9-6' />`
    // Note interne : une feuille au coin corne, avec ses lignes.
    case 'note': return FEUILLE + `
            <path d='M9 13h6' /><path d='M9 16h4' />`
    // Ordonnance : le marteau du juge.
    case 'ordonnance': return `
            <path d='M4 20h9' />
            <path d='M6.5 16.5l6-6' />
            <path d='M9.5 6.5l5 5' /><path d='M13 3l5 5' />
            <path d='M11.2 4.8l-1.7 1.7 5 5 1.7-1.7z' />`
    // Assignation : l'acte scelle.
    case 'assignation': return FEUILLE + `
            <circle cx='16' cy='17' r='3' /><path d='M14.5 19.5L14 22l2-1 2 1-.5-2.5' />`
    // Statuts : le livre relie.
    case 'statuts': return `
            <path d='M4 4.5A1.5 1.5 0 015.5 3H19v18H5.5A1.5 1.5 0 014 19.5z' />
            <path d='M4 17.5h15' /><path d='M8 7h7' /><path d='M8 10h5' />`
    // Extrait Kbis : le certificat a cachet.
    case 'kbis': return `
            <rect x='3' y='4' width='18' height='13' rx='1.5' />
            <path d='M6.5 8h7' /><path d='M6.5 11h5' />
            <circle cx='17' cy='12' r='2.2' />
            <path d='M15.5 14l-.5 3 2-1 2 1-.5-3' />`
    // Piece d'identite : la carte a photo.
    case 'identite': return `
            <rect x='2.5' y='5' width='19' height='14' rx='2' />
            <circle cx='8.5' cy='11' r='2' />
            <path d='M5.5 16c.6-1.6 1.7-2.4 3-2.4s2.4.8 3 2.4' />
            <path d='M14.5 10h4.5' /><path d='M14.5 13h4.5' />`
    // Facture : le recu dentele.
    case 'facture': return `
            <path d='M5.5 3h13v18l-2.2-1.4-2.2 1.4-2.1-1.4L9.7 21l-2.1-1.4L5.5 21z' />
            <path d='M9 8h6' /><path d='M9 11.5h6' /><path d='M9 15h3.5' />`
    // Proces-verbal : le presse-papier.
    case 'pv': return `
            <path d='M9 4.5H7.5A1.5 1.5 0 006 6v13.5A1.5 1.5 0 007.5 21h9a1.5 1.5 0 001.5-1.5V6a1.5 1.5 0 00-1.5-1.5H15' />
            <rect x='9' y='2.8' width='6' height='3.4' rx='1' />
            <path d='M9 11h6' /><path d='M9 14.5h4' />`
    default: return FEUILLE
  }
}

export interface ProprietesIconeGed {
  /** Le code de nature (voir `AccesGed.Natures`). */
  nature: string
  classe?: string
}

export function IconeGed({ nature, classe = 'rig-icone' }: ProprietesIconeGed) {
  // Rien autour du dessin : Razor retirait les blancs de debut et de fin d'element.
  return (
    <svg className={classe} viewBox="0 0 24 24" fill="none" stroke="currentColor"
         strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"
         aria-hidden="true" focusable="false"
         dangerouslySetInnerHTML={{ __html: dessinGed(nature) }} />
  )
}
