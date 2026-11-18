// En-tete de colonne qui TRIE au clic. Composant partage : une seule facon de
// ranger une grille dans toute la maquette, et un seul endroit a corriger.
// Jumeau React de Components/EnteteTriable.razor, meme balisage, memes classes.
//
// Le sens courant est porte par aria-sort, l'attribut que lisent les lecteurs
// d'ecran ET les tests : la fleche seule ne se mesure pas.
//
// Difference voulue avec le .razor : le libelle est du TEXTE, pas du HTML.
// Le .razor recevait « N&deg; Affaire » et l'injectait brut ; son infobulle
// montrait alors « Trier par N&deg; Affaire » tel quel. Ici on ecrit
// « N° Affaire » et l'infobulle dit « Trier par N° Affaire ».

export interface ProprietesEnteteTriable {
  /** Le titre affiche. */
  libelle: string
  /** Le nom de la colonne, tel que l'ecran le comprend. */
  cle: string
  /** La colonne sur laquelle la grille est rangee en ce moment. */
  cleActive?: string | null
  ascendant: boolean
  classe?: string
  trier: (cle: string) => void
}

export function EnteteTriable({ libelle, cle, cleActive, ascendant, classe, trier }: ProprietesEnteteTriable) {
  const actif = cleActive === cle
  return (
    <th className={classe ? `${classe} rig-th--triable` : 'rig-th--triable'}
        aria-sort={actif ? (ascendant ? 'ascending' : 'descending') : 'none'}
        title={`Trier par ${libelle}`}
        onClick={() => trier(cle)}>{libelle}<span className="rig-th__fleche">{actif ? (ascendant ? '▲' : '▼') : ''}</span></th>
  )
}
