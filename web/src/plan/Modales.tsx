// LES MODALES du plan de travail (Components/Pages/Home.razor, portees au lot
// 2 du plan « finir le passage a React », 19/11/2026) : imprimer la piece,
// l'envoyer par mail, creer un element dans l'arbre, et le menu du clic droit.
// Un clic sur le voile ferme ; un clic dans le cadre ne remonte pas au voile
// (@onclick:stopPropagation du .razor).

import type { MouseEvent } from 'react'
import { Liste } from '../accueil/Liste'
import { IconeGed } from '../composants/IconeGed'
import { Saisie } from '../session'
import type { EnvoiMailVue, GestePlan, ImpressionVue, MenuNoeudVue, NouveauVue } from './types'

function retenir(e: MouseEvent): void { e.stopPropagation() }

export function Impression({ vue, plan }: { vue: ImpressionVue, plan: GestePlan }) {
  return (
    <div className="rig-voile rig-voile--haut" onClick={() => plan('fermerImpression')}>
      <div className="rig-impression" onClick={retenir}>
        <div className="rig-kicker">{'Imprimer « '}{vue.libelle}{' »'}</div>{' '}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">File ou imprimante</span>{' '}
          <Liste className="rig-saisie-champ__saisie" valeur={vue.imprimante} changer={(v) => plan('imprimante', v)}>
            {vue.imprimantes.map((i) => <option key={i} value={i}>{i}</option>)}
          </Liste>
        </label>{' '}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">File differee</span>{' '}
          <Liste className="rig-saisie-champ__saisie" valeur={vue.file} changer={(v) => plan('fileDifferee', v)}>
            {vue.files.map((f) => <option key={f} value={f}>{f}</option>)}
          </Liste>
        </label>{' '}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type de papier</span>{' '}
          <Liste className="rig-saisie-champ__saisie" valeur={vue.papier} changer={(v) => plan('papier', v)}>
            {vue.papiers.map((t) => <option key={t} value={t}>{t}</option>)}
          </Liste>
        </label>{' '}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Exemplaires</span>{' '}
          <Saisie className="rig-saisie-champ__saisie" type="number" min="1" max="99" quand="change"
                  valeur={String(vue.exemplaires)} changer={(v) => plan('exemplaires', v)} />
        </label>{' '}

        <button className={'rig-bascule ' + (vue.rectoVerso ? 'rig-bascule--actif' : '')}
                onClick={() => plan('rectoVerso')}>
          <span className="rig-bascule__temoin"></span>{' '}
          <span>Recto-verso</span>
        </button>{' '}

        <div className="rig-impression__pied">
          <button className="rig-bouton rig-bouton--fort" onClick={() => plan('lancerImpression')}>Imprimer</button>{' '}
          <button className="rig-bouton" onClick={() => plan('fermerImpression')}>Annuler</button>
        </div>
      </div>
    </div>
  )
}

/** « Envoyer par mail » : destinataire + message type (demande du 15/09/2026, remplace « Transferer »). */
export function EnvoiMail({ vue, plan }: { vue: EnvoiMailVue, plan: GestePlan }) {
  return (
    <div className="rig-voile rig-voile--haut" onClick={() => plan('fermerMail')}>
      <div className="rig-impression" onClick={retenir}>
        <div className="rig-kicker">{'Envoyer par mail « '}{vue.libelle}{' »'}</div>{' '}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Destinataire</span>{' '}
          <Saisie className="rig-saisie-champ__saisie" type="email" list="rig-mail-destinataires"
                  placeholder="nom@exemple.fr" valeur={vue.destinataire}
                  changer={(v) => plan('destinataire', v)} />{' '}
          <datalist id="rig-mail-destinataires">
            {vue.destinataires.map((d) => <option key={d} value={d}></option>)}
          </datalist>
        </label>{' '}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Message</span>{' '}
          <Liste className="rig-saisie-champ__saisie" valeur={String(vue.message)} changer={(v) => plan('message', v)}>
            {vue.messages.map((titre, i) => <option key={i} value={String(i)}>{titre}</option>)}
          </Liste>
        </label>{' '}

        <div className="rig-apercu__message">{vue.corps}</div>{' '}

        <div className="rig-impression__pied">
          <button className="rig-bouton rig-bouton--fort" disabled={!vue.peutEnvoyer}
                  onClick={() => plan('envoyerMail')}>Envoyer</button>{' '}
          <button className="rig-bouton" onClick={() => plan('fermerMail')}>Annuler</button>
        </div>
      </div>
    </div>
  )
}

/** Bouton « Nouveau » de l'arbre : on choisit une NATURE, l'element est cree aussitot et se nomme SUR PLACE. */
export function Nouveau({ vue, plan }: { vue: NouveauVue, plan: GestePlan }) {
  return (
    <div className="rig-voile rig-voile--haut" onClick={() => plan('fermerNouveau')}>
      <div className="rig-impression rig-impression--large" onClick={retenir}>
        <div className="rig-kicker">{'Nouvel element dans « '}{vue.cible}{' »'}</div>{' '}
        <div className="rig-natures">
          {vue.natures.map((n) => (
            <button key={n.code} type="button" className="rig-tuile-nature" title={n.titre}
                    onClick={() => plan('ajouterElement', n.code)}>
              <IconeGed nature={n.code} classe="rig-nature__icone" />{' '}
              <span className="rig-nature__titre">{n.titre}</span>
              {n.extension.length > 0 && <span className="rig-nature__ext">{n.extension}</span>}
            </button>
          ))}
        </div>{' '}
        <div className="rig-impression__pied">
          <button className="rig-bouton" onClick={() => plan('fermerNouveau')}>Annuler</button>
        </div>
      </div>
    </div>
  )
}

/** Clic droit dans l'arbre : toutes les operations sur le noeud. */
export function MenuNoeud({ vue, plan }: { vue: MenuNoeudVue, plan: GestePlan }) {
  return (
    <>
      <div className="rig-voile rig-voile--transparent" onClick={() => plan('fermerMenu')}
           onContextMenu={(e) => { e.preventDefault(); plan('fermerMenu') }}></div>{' '}
      <div className="rig-ctx" style={{ left: `${vue.x}px`, top: `${vue.y}px` }} onClick={retenir}>
        <div className="rig-ctx__titre">{vue.titre}</div>{' '}
        <button className="rig-ctx__item" onClick={() => plan('menuRenommer')}>Renommer</button>
        {!vue.estPiece && (
          <>
            <button className="rig-ctx__item" onClick={() => plan('menuNouveauSous')}>{'Nouvel element ici…'}</button>{' '}
            <button className="rig-ctx__item" onClick={() => plan('menuDeplier')}>Tout deplier</button>{' '}
            <button className="rig-ctx__item" onClick={() => plan('menuReplier')}>Tout replier</button>
          </>
        )}
        <button className="rig-ctx__item" onClick={() => plan('menuDupliquer')}>Dupliquer</button>{' '}
        <div className="rig-ctx__filet"></div>{' '}
        <button className="rig-ctx__item rig-ctx__item--danger"
                onClick={() => plan('menuSupprimer')}>Supprimer</button>
      </div>
    </>
  )
}
