// L'icone d'une nouveaute du widget « Nouveautes ».
// Porte en React (lot 6 de la migration, 19/11/2026) depuis
// Components/IconeNouveaute.razor, devenu une coquille pour Home.razor.
//
// Meme parti que IconeGed : un SVG INLINE trace en `currentColor`, donc
// l'icone prend la teinte de son etiquette (vert, bleu, or, violet) et suit le
// theme clair comme le theme sombre sans seconde image. Un emoji, lui, arrive
// avec ses propres couleurs et jure avec la charte des qu'on change de theme.
//
// Les traces sont les MEMES chaines que la MarkupString du .razor, posees
// telles quelles dans le <svg> (meme DOM). Constantes de ce fichier, jamais
// une donnee saisie.

/** Le dessin d'une icone (voir la liste Nouveautes de l'accueil) ; un nom inconnu ne dessine rien. */
export function dessinNouveaute(nom: string): string {
  switch (nom) {
    // Geste : la boussole — on ne cherche plus un module, on cherche une direction.
    case 'boussole': return `
            <circle cx='12' cy='12' r='9' />
            <path d='M15.5 8.5l-2 5-5 2 2-5z' />`
    // Profil : la silhouette, tete et epaules.
    case 'profil': return `
            <circle cx='12' cy='8' r='3.2' />
            <path d='M5.5 20a6.5 6.5 0 0 1 13 0' />`
    // Actualites : le megaphone qui annonce a tout le greffe.
    case 'megaphone': return `
            <path d='M4 10v4a1 1 0 0 0 1 1h3l7 4V5l-7 4H5a1 1 0 0 0-1 1z' />
            <path d='M18 9.5a3.5 3.5 0 0 1 0 5' />`
    // Affichage : la moitie soleil, moitie nuit — le clair et le sombre.
    case 'clair-sombre': return `
            <circle cx='12' cy='12' r='5' />
            <path d='M12 7a5 5 0 0 0 0 10z' fill='currentColor' stroke='none' />
            <path d='M12 1.5v2' /><path d='M12 20.5v2' />
            <path d='M1.5 12h2' /><path d='M20.5 12h2' />
            <path d='M4.6 4.6l1.4 1.4' /><path d='M18 18l1.4 1.4' />
            <path d='M19.4 4.6L18 6' /><path d='M6 18l-1.4 1.4' />`
    default: return ''
  }
}

export interface ProprietesIconeNouveaute {
  /** Le code de l'icone (voir la liste Nouveautes de l'accueil). */
  nom: string
  classe?: string
}

export function IconeNouveaute({ nom, classe = 'rig-nouv__icone' }: ProprietesIconeNouveaute) {
  // Rien autour du dessin : Razor retirait les blancs de debut et de fin d'element.
  return (
    <svg className={classe} viewBox="0 0 24 24" fill="none" stroke="currentColor"
         strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"
         aria-hidden="true" focusable="false"
         dangerouslySetInnerHTML={{ __html: dessinNouveaute(nom) }} />
  )
}
