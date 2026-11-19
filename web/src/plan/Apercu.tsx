// ZONE 3 de la visionneuse : LE DOCUMENT (Components/Pages/Home.razor, portee
// au lot 2 du plan « finir le passage a React », 19/11/2026). « Detacher »
// sort la piece dans une fenetre a part (FenetreDocument), posable sur un
// autre ecran ; PLUSIEURS documents peuvent l'etre en meme temps, un rappel
// liste les fenetres ouvertes.

import { Fragment } from 'react'
import { ApercuKbis } from '../composants/ApercuKbis'
import type { ApercuColonneVue, GestePlan } from './types'

export function Apercu({ colonne, plan }: { colonne: ApercuColonneVue, plan: GestePlan }) {
  const a = colonne.apercu
  return (
    <div className="rig-vged__guide rig-vged__apercu">
      {colonne.detachees > 0 && (
        <div className="rig-guide__entete rig-vged__detachees">
          <div>
            <div className="rig-kicker">Documents sortis</div>{' '}
            <div className="rig-guide__source">{colonne.detachees}{' fenetre(s) ouverte(s) — chacune revient toujours a la meme place.'}</div>
          </div>{' '}
          <button className="rig-bouton" onClick={() => plan('rattacher')}>Tout rattacher</button>
        </div>
      )}
      {a && (
        <div className="rig-apercu">
          <div className="rig-apercu__entete">
            <div>
              <div className="rig-kicker">{a.extension}{' · '}{a.pages}{' page(s) · '}{a.date}</div>{' '}
              <div className="rig-apercu__titre">{a.libelle}</div>
            </div>{' '}
            <div className="rig-apercu__actions">
              {/* Icones + info-bulle (demande du 15/09/2026 : les libelles en
                  toutes lettres etaient juges laids). */}
              <button className="rig-bouton rig-bouton--icone rig-bouton--fort" title="Imprimer" aria-label="Imprimer" onClick={() => plan('imprimer')}>
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 9V3h10v6"></path><rect x="3" y="9" width="18" height="8" rx="2"></rect><rect x="7" y="14" width="10" height="7"></rect></svg>
              </button>{' '}
              <button className="rig-bouton rig-bouton--icone" title="Editer" aria-label="Editer" onClick={() => plan('editer')}>
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16z"></path><path d="M13.5 6.5l4 4"></path></svg>
              </button>{' '}
              <button className="rig-bouton rig-bouton--icone" title="Detacher dans une fenetre" aria-label="Detacher" onClick={() => plan('detacher')}>
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M14 4h6v6"></path><path d="M20 4l-9 9"></path><path d="M18 14v5a1 1 0 01-1 1H5a1 1 0 01-1-1V7a1 1 0 011-1h5"></path></svg>
              </button>{' '}
              <button className="rig-bouton rig-bouton--icone" title="Sauvegarder" aria-label="Sauvegarder" onClick={() => plan('sauvegarder')}>
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 3h11l4 4v13a1 1 0 01-1 1H5a1 1 0 01-1-1V4a1 1 0 011-1z"></path><path d="M8 3v5h7V3"></path><rect x="7" y="13" width="10" height="8"></rect></svg>
              </button>{' '}
              <button className="rig-bouton rig-bouton--icone" title="Envoyer par mail" aria-label="Envoyer par mail" onClick={() => plan('envoiMail')}>
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 3L10 14"></path><path d="M21 3l-7 18-4-7-7-4z"></path></svg>
              </button>{' '}
              <button className="rig-bouton rig-bouton--icone rig-bouton--plat" title="Fermer l'apercu" aria-label="Fermer" onClick={() => plan('fermerApercu')}>
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12"></path><path d="M18 6L6 18"></path></svg>
              </button>
            </div>
          </div>
          {a.message.length > 0 && <div className="rig-apercu__message">{a.message}</div>}
          <div className={'rig-apercu__pages ' + (a.pdf ? 'rig-apercu__pages--pdf' : '')}>
            {a.pdf
              ? <iframe className="rig-apercu__pdf" src={a.fichier + '#view=FitH'} title={a.libelle}></iframe>
              : a.kbis
                ? (
                  <div className="rig-apercu__feuille rig-apercu__feuille--kbis">
                    <ApercuKbis vue={a.kbis} />
                  </div>
                )
                : a.feuilles.map((f) => (
                  <div key={f.numero} className="rig-apercu__feuille">
                    {/* Valeurs du formulaire, relues a chaque frappe (EcrireEnDirect). */}
                    {f.valeurs && (
                      <dl className="rig-apercu__saisie">
                        {f.valeurs.map((v, i) => <Fragment key={i}><dt>{v.libelle}</dt><dd>{v.valeur}</dd></Fragment>)}
                      </dl>
                    )}
                    <div className="rig-apercu__feuille-texte">{f.texte}</div>{' '}
                    <div className="rig-apercu__folio">{f.numero}{' / '}{f.total}</div>
                  </div>
                ))}
          </div>
        </div>
      )}
    </div>
  )
}
