// LA COLONNE DE SUIVI de la vue du dossier (proposition 3 « Onglets Suivi / Fil »
// du /draft du 19/11/2026, conv-2009), toujours a droite du document. Deux
// onglets, un seul visible a la fois : le fil ne s'affiche que si on le choisit.
//   - Suivi : RESTE A FAIRE (les points a corriger du dossier, une ligne par
//     etape ; un clic ouvre l'etape) puis l'HISTORIQUE des pieces ;
//   - Fil : les notes ecrites sur le dossier et les valeurs changees dans le
//     formulaire, du plus ancien au plus recent, et le champ pour ecrire.
// L'onglet choisi est un etat de l'ecran seulement : rien ne part au C#. Le REPLI
// a droite, lui, est tenu par le C# (SuiviReplie) : la colonne du corps change.

import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { HistoriqueDossier } from './Historique'
import type { BandeauDefautsVue, GestePlan, LienDefautVue, MessageFilVue, PieceHistoriqueVue } from './types'

export type OngletSuivi = 'suivi' | 'fil'

export interface ProprietesSuivi {
  bandeau: BandeauDefautsVue | null
  historique: PieceHistoriqueVue[]
  fil: MessageFilVue[]
  /** Repliee a droite (geste « suiviReplier ») : il n'en reste qu'un rail pour la rouvrir. */
  replie?: boolean
  plan: GestePlan
}

export function SuiviDossier({ bandeau, historique, fil, replie = false, plan }: ProprietesSuivi) {
  const [onglet, setOnglet] = useState<OngletSuivi>('suivi')
  const reste = bandeau?.liens ?? []
  // REPLIEE A DROITE (demande du 19/11/2026) : un rail, tout entier bouton pour la
  // rouvrir, qui garde le nombre d'etapes a corriger -- replier ne cache pas l'alerte.
  if (replie) {
    return (
      <aside className="rig-vged__suivi rig-vged__suivi--replie" aria-label="Suivi du dossier">
        <button type="button" className="rig-suivi__rouvrir" title="Rouvrir le suivi" aria-label="Rouvrir le suivi"
                onClick={() => plan('suiviDeplier')}>
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M15 6l-6 6 6 6"></path></svg>
          <span className="rig-suivi__rouvrir-libelle">Suivi</span>
          {reste.length > 0 && (
            <b className="rig-suivi__nombre rig-suivi__nombre--alerte"
               title={reste.length + (reste.length === 1 ? ' etape a corriger' : ' etapes a corriger')}>{reste.length}</b>
          )}
        </button>
      </aside>
    )
  }
  // Le NOMBRE d'un onglet dit ce qui attend : les points a corriger sur « Suivi »
  // (en couleur d'alerte), les messages sur « Fil ». Zero : rien d'affiche.
  // (« Suivi 45 » additionnait les points a corriger et les pieces de
  // l'historique : un nombre qui ne disait rien.)
  const bouton = (code: OngletSuivi, libelle: string, nombre: number, titre: string, alerte = false) => (
    <button type="button" role="tab" aria-selected={onglet === code}
            className={'rig-suivi__onglet' + (onglet === code ? ' rig-suivi__onglet--actif' : '')}
            title={nombre > 0 ? titre : undefined}
            onClick={() => setOnglet(code)}>
      {libelle}
      {nombre > 0 && <>{' '}<b className={'rig-suivi__nombre' + (alerte ? ' rig-suivi__nombre--alerte' : '')}>{nombre}</b></>}
    </button>
  )
  return (
    <aside className="rig-vged__suivi" aria-label="Suivi du dossier">
      {/* Le bouton de repli au bord INTERIEUR, comme celui de l'arbre : en miroir. */}
      <div className="rig-suivi__tete">
        <button type="button" className="rig-suivi__replier" title="Replier le suivi a droite"
                aria-label="Replier le suivi" onClick={() => plan('suiviReplier')}>
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 6l6 6-6 6"></path></svg>
        </button>
        <div className="rig-suivi__onglets" role="tablist">
          {bouton('suivi', 'Suivi', reste.length,
                  reste.length + (reste.length === 1 ? ' etape a corriger' : ' etapes a corriger'), true)}
          {bouton('fil', 'Fil', fil.length, fil.length + (fil.length === 1 ? ' message' : ' messages'))}
        </div>
      </div>
      {onglet === 'suivi'
        ? (
          <div className="rig-suivi__panneau" role="tabpanel" aria-label="Suivi">
            <ResteAFaire liens={reste} plan={plan} />
            <HistoriqueDossier pieces={historique} plan={plan} />
          </div>
        )
        : <FilDossier messages={fil} plan={plan} />}
    </aside>
  )
}

/** Les points a corriger, une ligne par etape (les memes liens que la pastille de l'entete). */
export function ResteAFaire({ liens, plan }: { liens: LienDefautVue[], plan: GestePlan }) {
  return (
    <section className="rig-reste" aria-label="Reste a faire">
      <div className="rig-suivi__titre">Reste a faire · {liens.length}</div>
      {liens.length === 0
        ? <div className="rig-suivi__vide">Rien a corriger.</div>
        : (
          <ul className="rig-reste__liste">
            {liens.map((l, i) => (
              <li key={i}>
                <button type="button" className={'rig-reste__ligne' + (l.refuse ? ' rig-reste__ligne--refuse' : '')}
                        title={l.titre}
                        onClick={() => plan('allerAuDefautBandeau', { etape: l.indexEtape, fiche: l.fiche })}>
                  <span className="rig-reste__case" aria-hidden="true"></span>
                  <span className="rig-reste__etape">{l.etape}{l.suffixe}</span>
                  <span className="rig-reste__nombre">{l.nombre}{l.nombre === 1 ? ' point' : ' points'}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
    </section>
  )
}

/** Le fil : les messages, puis le champ pour ecrire (Entree envoie, Maj+Entree passe a la ligne). */
export function FilDossier({ messages, plan }: { messages: MessageFilVue[], plan: GestePlan }) {
  const [texte, setTexte] = useState('')
  const liste = useRef<HTMLOListElement>(null)
  // Le dernier message en vue, a l'ouverture de l'onglet et a chaque nouveau message.
  useEffect(() => {
    const l = liste.current
    if (l) l.scrollTop = l.scrollHeight
  }, [messages.length])
  const envoyer = () => {
    if (texte.trim().length === 0) return
    plan('filEcrire', texte)
    setTexte('')
  }
  const touche = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); envoyer() }
  }
  return (
    <div className="rig-suivi__panneau rig-fil" role="tabpanel" aria-label="Fil">
      {messages.length === 0
        ? <div className="rig-suivi__vide rig-fil__vide">Aucun message sur ce dossier. Une note ecrite ici reste attachee au dossier ; les valeurs changees dans le formulaire s'y ajoutent seules.</div>
        : (
          <ol className="rig-fil__liste" ref={liste}>
            {messages.map((m, i) => (
              <li key={i} className={'rig-fil__message' + (m.modification ? ' rig-fil__message--modif' : '')}>
                <span className="rig-fil__avatar" aria-hidden="true">{m.initiales}</span>
                <div className="rig-fil__corps">
                  <div className="rig-fil__tete">
                    {m.modification
                      ? <span><b>{m.auteur}</b>{' a modifie '}<b>{m.champ}</b></span>
                      : <b>{m.auteur}</b>}
                    <span className="rig-fil__quand">{m.quand}</span>
                  </div>
                  {m.modification
                    ? (
                      <div className="rig-fil__diff">
                        <s>{m.avant}</s>{' → '}<b>{m.apres}</b>
                      </div>
                    )
                    : <div className="rig-fil__bulle">{m.texte}</div>}
                </div>
              </li>
            ))}
          </ol>
        )}
      <div className="rig-fil__saisie">
        <textarea className="rig-fil__champ" rows={2} placeholder="Ecrire au fil..." aria-label="Ecrire au fil"
                  value={texte} onChange={(e) => setTexte(e.target.value)} onKeyDown={touche}></textarea>
        <button type="button" className="rig-bouton rig-bouton--fort rig-fil__envoyer"
                disabled={texte.trim().length === 0} onClick={envoyer}>Envoyer</button>
      </div>
    </div>
  )
}
