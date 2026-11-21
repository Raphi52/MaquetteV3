// Ecran « Premiers pas » : « a maquetter » dans le carnet (SOC-026 T04). Sans origine RIG v2 : l'ancien RIG n'a pas d'aide a
// la premiere ouverture ; l'agent apprend ses gestes en formation, et ses raccourcis restent dans sa tete.
//
// A la premiere ouverture de la V3 par un agent, cet ecran s'ouvre de lui-meme (web/src/composants/Application.tsx) et
// montre les gestes principaux (SOC-026 CA06) : le menu geste › verbe, la recherche (Ctrl K ; Entree, F5, F7 dans un
// critere), les fenetres de document, Echap et « ? », signaler un defaut ; un bouton ouvre la fiche « Ancienne touche,
// nouvelle touche » (SOC-027). Il est alors « vu » pour l'agent, sur tous les serveurs RDS (preference AideVue,
// AccueilEtat) ; il se rouvre depuis le menu. Les gestes viennent du pont (Pont/EcranAide.cs), tires des regles du clavier
// et du menu de la V3 : l'aide ne promet pas une touche qui n'existe pas (tests/SocleBetaCheck le verifie).

import { useEffect, useState } from 'react'
import { appeler } from '../pont'

export interface GesteAide {
  titre: string
  touche: string | null
  comment: string
}

export interface VueEcranAide {
  premiereFois: boolean
  gestes: GesteAide[]
  ecranTouches: string
  nomEcranTouches: string
  cheminMenu: string
}

export interface ProprietesEcranAide {
  /** Ouvre un ecran par son code (le geste « ouvrirCode » du plan de travail). */
  ouvrir: (code: string) => void
}

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const Aide = { margin: 'var(--rig-esp-2) 0 0', fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const
const GrilleRepartie = { tableLayout: 'fixed' } as const
const Touche = {
  display: 'inline-block', padding: '1px 6px', border: '1px solid var(--rig-bord)', borderRadius: '4px',
  background: 'var(--rig-fond-2)', fontFamily: 'var(--rig-font-mono)', whiteSpace: 'nowrap',
} as const

export function EcranAide({ ouvrir }: ProprietesEcranAide) {
  const [vue, setVue] = useState<VueEcranAide | null>(null)

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranAide>('EcranAide.Ouvrir').then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Ouverture de l'aide…</p></div>

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Bienvenue dans la V3</div>
        {vue.premiereFois && (
          <p style={{ margin: 0 }} role="status">
            <span className="rig-etat rig-etat--action">Première ouverture</span>
          </p>
        )}
        <p style={Aide}>
          Les gestes de l'ancien RIG ont leur place dans la V3 : voici les principaux. Cette aide s'ouvre une seule fois,
          sur ce poste comme sur les autres ; elle se rouvre depuis le menu, {vue.cheminMenu}.
        </p>
        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" onClick={() => ouvrir(vue.ecranTouches)}>
            {'Ouvrir la fiche « '}{vue.nomEcranTouches}{' »'}
          </button>
        </div>
        <p style={Aide}>
          Pour chaque touche de l'ancien RIG, la fiche donne la touche ou le geste de la V3, écran par écran.
        </p>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Les gestes principaux</div>
          <span className="rig-resultats-proc__compte">{vue.gestes.length}{' gestes'}</span>
        </div>
        {vue.premiereFois && (
          <p style={Message}>
            <span className="rig-etat rig-etat--ok">Aide vue</span>
            {' '}Elle ne s'ouvrira plus d'elle-même, quel que soit le serveur où vous ouvrez la V3.
          </p>
        )}
        <div className="rig-table-cadre">
          <table className="rig-table" style={GrilleRepartie} aria-label="Les gestes principaux">
            <colgroup><col style={{ width: '34%' }} /><col style={{ width: '160px' }} /><col /></colgroup>
            <thead>
              <tr><th>Pour</th><th>Touche</th><th>Comment</th></tr>
            </thead>
            <tbody>
              {vue.gestes.map((g) => (
                <tr key={g.titre}>
                  <td><b>{g.titre}</b></td>
                  <td>{g.touche ? <span style={Touche}>{g.touche}</span> : <span className="rig-etat">Souris</span>}</td>
                  <td style={{ whiteSpace: 'normal' }}>{g.comment}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
