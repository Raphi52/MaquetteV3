// LA COLONNE DE SUIVI de la vue du dossier (proposition 3 « Onglets Suivi / Fil »
// du /draft du 19/11/2026, conv-2009), toujours a droite du document. Deux
// onglets, un seul visible a la fois : le fil ne s'affiche que si on le choisit.
//   - Suivi : EN COURS (les demandes, commandes web, courriers et actes attendus
//     sur l'affaire, et ses derniers gestes ; chaque ligne ouvre son ecran pour ce
//     dossier, « M'attribuer » la prend -- plan « Vue du dossier = coeur des
//     actions », lot 3), RESTE A FAIRE (les points a corriger du dossier, une ligne
//     par etape ; un clic ouvre l'etape) puis l'HISTORIQUE des pieces ;
//   - Fil : les notes ecrites sur le dossier et les valeurs changees dans le
//     formulaire, du plus ancien au plus recent, et le champ pour ecrire.
// L'onglet choisi est un etat de l'ecran seulement : rien ne part au C#. Le REPLI
// a droite, lui, est tenu par le C# (SuiviReplie) : la colonne du corps change.

import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { HistoriqueDossier } from './Historique'
import type {
  BandeauDefautsVue, EnCoursDossierVue, GestePlan, LienDefautVue, MessageFilVue, PieceHistoriqueVue, TravailDossierVue,
} from './types'

export type OngletSuivi = 'suivi' | 'fil'

export interface ProprietesSuivi {
  bandeau: BandeauDefautsVue | null
  historique: PieceHistoriqueVue[]
  fil: MessageFilVue[]
  /** Ce qui est en cours sur l'affaire (absent : rien). */
  enCours?: EnCoursDossierVue
  /** Repliee a droite (geste « suiviReplier ») : il n'en reste qu'un rail pour la rouvrir. */
  replie?: boolean
  plan: GestePlan
}

const RIEN_EN_COURS: EnCoursDossierVue = { travail: [], horsDelai: 0, activite: [] }

/** « 4 en cours, dont 1 hors delai » : l'infobulle du nombre d'« En cours ». */
function titreEnCours(e: EnCoursDossierVue): string {
  const n = e.travail.length
  return n + ' en cours' + (e.horsDelai > 0 ? ', dont ' + e.horsDelai + ' hors delai' : '')
}

export function SuiviDossier({ bandeau, historique, fil, enCours = RIEN_EN_COURS, replie = false, plan }: ProprietesSuivi) {
  const [onglet, setOnglet] = useState<OngletSuivi>('suivi')
  const reste = bandeau?.liens ?? []
  // REPLIEE A DROITE (demande du 19/11/2026) : un rail, tout entier bouton pour la
  // rouvrir, qui garde le nombre d'etapes a corriger -- replier ne cache pas l'alerte --
  // et celui du travail en cours (une horloge le distingue), en alerte s'il y a du hors delai.
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
          {enCours.travail.length > 0 && (
            <b className={'rig-suivi__nombre rig-suivi__nombre--encours' + (enCours.horsDelai > 0 ? ' rig-suivi__nombre--alerte' : '')}
               title={titreEnCours(enCours)}>
              <svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="6"></circle><path d="M8 4.5V8l2.5 1.5"></path></svg>
              {enCours.travail.length}
            </b>
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
            <EnCoursDossier enCours={enCours} plan={plan} />
            <ResteAFaire liens={reste} plan={plan} />
            <HistoriqueDossier pieces={historique} plan={plan} />
          </div>
        )
        : <FilDossier messages={fil} plan={plan} />}
    </aside>
  )
}

/** Le nom d'une nature de ligne, en tete de la ligne. */
const NATURES: Record<string, string> = { dem: 'Demande', cmd: 'Commande web', cou: 'Courrier', doc: 'Acte' }

/**
 * EN COURS sur l'affaire : le travail qui la porte, le plus urgent d'abord (un clic
 * ouvre son ecran pour ce dossier, geste « enCoursOuvrir »), puis les derniers gestes.
 */
export function EnCoursDossier({ enCours, plan }: { enCours: EnCoursDossierVue, plan: GestePlan }) {
  const { travail, activite } = enCours
  return (
    <section className="rig-encours" aria-label="En cours sur le dossier">
      <div className="rig-suivi__titre" title={travail.length > 0 ? titreEnCours(enCours) : undefined}>
        En cours · {travail.length}
      </div>
      {travail.length === 0
        ? <div className="rig-suivi__vide">Rien en cours sur ce dossier.</div>
        : (
          <ul className="rig-encours__liste">
            {travail.map((t, i) => <LigneEnCours key={t.nature + t.numero + i} t={t} plan={plan} />)}
          </ul>
        )}
      {activite.length > 0 && (
        <>
          <div className="rig-suivi__titre rig-encours__sous-titre">Dernieres actions</div>
          <ol className="rig-encours__activite">
            {activite.map((a, i) => (
              <li key={i} className="rig-encours__geste">
                <span><b>{a.agent}</b> {a.verbe} {a.objet}</span>
                <span className="rig-encours__quand">{a.depuis}</span>
              </li>
            ))}
          </ol>
        </>
      )}
    </section>
  )
}

/** Une ligne d'« En cours » : nature, numero, etat, titre, delai ; puis qui s'en occupe et « M'attribuer ». */
function LigneEnCours({ t, plan }: { t: TravailDossierVue, plan: GestePlan }) {
  const corps = (
    <>
      <span className="rig-encours__tete">
        <span className="rig-encours__nature">{NATURES[t.nature] ?? t.nature}</span>
        <span className="rig-encours__numero">{t.numero}</span>
        <span className={'rig-encours__etat' + (t.ton ? ' rig-encours__etat--' + t.ton : '')}>{t.etat}</span>
      </span>
      <span className="rig-encours__titre" title={t.detail}>{t.titre}</span>
      {t.delai && <span className="rig-encours__delai">{t.delai}</span>}
    </>
  )
  return (
    <li className={'rig-encours__ligne' + (t.ton === 'al' ? ' rig-encours__ligne--alerte' : '')}>
      <span className={'rig-filet-v rig-filet-v--' + t.filet} aria-hidden="true"></span>
      <div className="rig-encours__corps">
        {/* L'ecran de la ligne s'ouvre POUR ce dossier (l'onglet retient l'affaire) ; sans
            ecran ouvrable ici (perimetre du login, onglet sorti), la ligne se lit seulement. */}
        {t.ecranLibelle
          ? (
            <button type="button" className="rig-encours__ouvrir" title={'Ouvrir l\'ecran ' + t.ecranLibelle + ' pour ce dossier'}
                    onClick={() => plan('enCoursOuvrir', { nature: t.nature, numero: t.numero })}>
              {corps}
            </button>
          )
          : <div className="rig-encours__ouvrir rig-encours__ouvrir--fixe">{corps}</div>}
        <div className="rig-encours__pied">
          {t.qui
            ? (
              <span className="rig-encours__qui" title={t.aMoi ? 'A moi' : t.qui.nom + ' s\'en occupe'}>
                <span className={'rig-encours__avatar' + (t.aMoi ? ' rig-encours__avatar--moi' : '')} aria-hidden="true">{t.qui.initiales}</span>
                {t.qui.signature}
              </span>
            )
            : <span className="rig-encours__qui rig-encours__qui--personne">Personne</span>}
          {t.attribuable && (
            <button type="button" className="rig-encours__attribuer" title="Passer cette demande a mon nom"
                    onClick={() => plan('paletteAttribuer', { numero: t.numero, signature: null })}>
              M'attribuer
            </button>
          )}
        </div>
      </div>
    </li>
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
