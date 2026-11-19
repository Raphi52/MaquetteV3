// Contenu de la FENETRE DETACHEE du document : le meme apercu que la 3e zone
// de l'ecran VDossier, seul dans sa fenetre. Il suit la piece ouverte dans la
// fenetre principale (voir DocumentPartage).
//
// Porte en React (lot 6 de la migration, 19/11/2026) depuis
// Components/DocumentDetacheVue.razor, qui reste la racine de FenetreDocument :
// meme balisage, memes classes CSS. La coquille suit l'emplacement de SA
// fenetre et passe ici la piece calculee par Pont/DocumentDetacheVue.cs (null
// quand aucune n'est ouverte).

import { useState } from 'react'

/** Une valeur du formulaire portee au document (ValeurDocumentVue). */
export interface ValeurDocumentVue {
  libelle: string
  valeur: string
}

/** Une feuille de l'apercu (FeuilleDocumentVue). */
export interface FeuilleDocumentVue {
  numero: number
  texte: string
}

/** La piece affichee (PieceDetacheeVue). */
export interface PieceDetacheeVue {
  extension: string
  pages: number
  date: string
  libelle: string
  aPdf: boolean
  fichier: string
  valeurs: ValeurDocumentVue[]
  feuilles: FeuilleDocumentVue[]
}

export interface ProprietesDocumentDetacheVue {
  piece: PieceDetacheeVue | null
}

const FOND = { background: 'var(--rig-fond)', color: 'var(--rig-texte)', height: '100vh', overflow: 'auto' }

/** L'objectif suivant, borne a 4,0 et arrondi au centieme (Math.Round(x, 2) du .razor). */
export const zoomPlus = (zoom: number) => Math.min(4.0, Math.round((zoom + 0.25) * 100) / 100)

/** L'objectif precedent, borne a 0,5. */
export const zoomMoins = (zoom: number) => Math.max(0.5, Math.round((zoom - 0.25) * 100) / 100)

export function DocumentDetacheVue({ piece }: ProprietesDocumentDetacheVue) {
  // Objectif (zoom) PROPRE a cette fenetre : il vit dans CE composant, il n'est
  // PAS dans DocumentPartage, donc chaque fenetre detachee grossit le document
  // independamment de la principale (0,5 a 4,0).
  const [zoom, setZoom] = useState(1.0)

  return (
    <div data-rig-theme="sombre" style={FOND}>
      {piece === null ? (
        <div className="rig-guide__entete">
          <div>
            <div className="rig-kicker">Document</div>
            <h3 className="rig-guide__titre">Aucune piece ouverte</h3>
            <div className="rig-guide__source">Cliquez une piece dans l'arbre de la fenetre principale.</div>
          </div>
        </div>
      ) : (
        <div className="rig-apercu rig-apercu--detache">
          <div className="rig-apercu__entete">
            <div>
              <div className="rig-kicker">{piece.extension} &middot; {piece.pages} page(s) &middot; {piece.date}</div>
              <div className="rig-apercu__titre">{piece.libelle}</div>
            </div>
            {/* Les boutons « - » et « + » ont un nom (title, aria-label) : le .razor
                n'en avait pas, et tests/AriaCheck refuse un bouton-icone muet. */}
            <div className="rig-apercu__actions rig-zoom">
              <button className="rig-bouton" title="Reduire le document" aria-label="Reduire le document"
                      onClick={() => setZoom(zoomMoins)}>&minus;</button>
              <span className="rig-zoom__taux">{Math.round(zoom * 100)} %</span>
              <button className="rig-bouton" title="Agrandir le document" aria-label="Agrandir le document"
                      onClick={() => setZoom(zoomPlus)}>+</button>
              <button className="rig-bouton rig-bouton--plat" onClick={() => setZoom(1.0)}>100 %</button>
            </div>
          </div>
          {piece.aPdf ? (
            <div className="rig-apercu__pages rig-apercu__pages--pdf">
              <iframe className="rig-apercu__pdf" src={piece.fichier + '#view=FitH'} title={piece.libelle}></iframe>
            </div>
          ) : (
            <div className="rig-apercu__pages" style={{ zoom }}>
              {piece.feuilles.map((feuille) => (
                <div key={feuille.numero} className="rig-apercu__feuille">
                  {feuille.numero === 1 && (
                    /* Valeurs du formulaire, relues a chaque frappe (EcrireEnDirect). */
                    <dl className="rig-apercu__saisie">
                      {piece.valeurs.map((v, i) => [
                        <dt key={'t' + i}>{v.libelle}</dt>, <dd key={'d' + i}>{v.valeur}</dd>,
                      ])}
                    </dl>
                  )}
                  <div className="rig-apercu__feuille-texte">{feuille.texte}</div>
                  <div className="rig-apercu__folio">{feuille.numero} / {piece.feuilles.length}</div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
