// Ecran PROC_ACTUALITES : la REDACTION des actualites affichees sur l'accueil
// de tout le monde. Rien n'est calcule ici : quelqu'un ecrit (« L'INPI est
// HS »), et le texte part dans le fichier PARTAGE lu par les autres postes.
// C'est la destination du clic sur la tuile « Actualites ».
//
// Porte depuis Components/Ecrans/EcranActualites.razor (lot 3) : meme balisage,
// memes classes CSS. Ecran A SESSION (src/session.tsx) : la liste et le depot
// partage (DepotActualites) vivent dans Pont/EcranActualites.cs, qui ecrit le
// fichier ; React affiche la vue et remonte chaque geste.

import { useEffect, useRef, type TextareaHTMLAttributes } from 'react'
import { appeler } from '../pont'
import { Saisie, useSession, type PontDeSession, type VueDeSession } from '../session'

export interface TypeActualiteVue {
  code: string
  libelle: string
  /** Le suffixe de classe de la couleur (rig-filet-v--…, rig-actualite__type--…). */
  filet: string
  /** L'infobulle : ce que le type veut dire ET combien de temps il tient. */
  aide: string
}

export interface LigneActualite {
  filet: string
  libelle: string
  titre: string
  message: string
  auteur: string
  horodatage: string
  /** « dd/MM », ou « — » pour une actualite epinglee. */
  retire: string
}

export interface VueActualites extends VueDeSession {
  titre: string
  message: string
  type: string
  auteur: string
  jours: string
  peutPublier: boolean
  types: TypeActualiteVue[]
  compteRendu: string
  statutRendu: string
  cheminPartage: string
  liste: LigneActualite[]
}

const pont: PontDeSession<VueActualites> = {
  ouvrir: () => appeler<VueActualites>('EcranActualites.Ouvrir'),
  agir: (session, geste, valeur) => appeler<VueActualites>('EcranActualites.Agir', session, geste, valeur),
  fermer: (session) => appeler<void>('EcranActualites.Fermer', session),
}

type AttributsZone = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'value' | 'defaultValue' | 'onChange' | 'onInput'>

/**
 * Le <textarea> du message, avec la regle de <Saisie> (src/session.tsx) : le
 * champ n'est reecrit QUE quand la valeur de la session CHANGE (« Publier » le
 * vide) ; la frappe n'est jamais ecrasee par une reponse en retard.
 */
function ZoneDeSaisie({ valeur, changer, ...attributs }: AttributsZone & { valeur: string; changer: (v: string) => void }) {
  const zone = useRef<HTMLTextAreaElement>(null)
  useEffect(() => {
    const el = zone.current
    if (el && el.value !== valeur) el.value = valeur
  }, [valeur])
  return <textarea ref={zone} defaultValue={valeur} {...attributs} onInput={(e) => changer(e.currentTarget.value)}></textarea>
}

export function EcranActualites() {
  const [vue, geste] = useSession(pont)
  if (vue === null) return null

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Nouvelle actualite</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Titre</span>
          <Saisie className="rig-saisie-champ__saisie" valeur={vue.titre} changer={(v) => geste('Titre', v)}
                  placeholder="Alerte CNG fraude : ..." />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Message</span>
          <ZoneDeSaisie className="rig-saisie-champ__saisie" rows={4} valeur={vue.message}
                        changer={(v) => geste('Message', v)}
                        placeholder="Ce que les utilisateurs doivent savoir." />
        </label>

        {/* Le TYPE se choisit en premier : il fixe la couleur du filet ET la
            duree de vie proposee. Une liste deroulante cacherait le code
            couleur ; des boutons colores le montrent au moment du choix. */}
        <div className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type</span>
          <div className="rig-actualite__types">
            {vue.types.map((t) => (
              <button key={t.code} type="button"
                      className={'rig-actualite__choix' + (t.code === vue.type ? ' rig-actualite__choix--pris' : '')}
                      title={t.aide}
                      onClick={() => geste('ChoisirType', t.code)}>
                <span className={`rig-filet-v rig-filet-v--${t.filet}`}></span>
                <span className={`rig-actualite__type rig-actualite__type--${t.filet}`}>{t.libelle}</span>
              </button>
            ))}
          </div>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Signature</span>
          <Saisie className="rig-saisie-champ__saisie" valeur={vue.auteur} changer={(v) => geste('Auteur', v)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">
            Retirer apres (jours) — 0 = epingle
          </span>
          <Saisie className="rig-saisie-champ__saisie" type="number" min="0" max="365" quand="change"
                  valeur={vue.jours} changer={(v) => geste('Jours', v)} />
        </label>

        <button className="rig-bouton rig-bouton--fort" onClick={() => geste('Publier')}
                disabled={!vue.peutPublier}>Publier l'actualite</button>

        {vue.compteRendu.length > 0 && (
          <div className="rig-alerte">
            <span className={`rig-filet-v rig-filet-v--${vue.statutRendu}`}></span>
            <span className="rig-alerte__corps">
              <span className="rig-alerte__detail">{vue.compteRendu}</span>
            </span>
          </div>
        )}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Source partagee</div>
          <div className="rig-alerte__detail">{vue.cheminPartage}</div>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Actualites publiees</div>
          <span className="rig-resultats-proc__compte">{vue.liste.length} actualite(s)</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Type</th>
                <th>Titre</th>
                <th>Message</th>
                <th>Signature</th>
                <th>Publie</th>
                <th>Retire</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {vue.liste.map((b, i) => (
                <tr key={i}>
                  <td>
                    <span className={`rig-actualite__type rig-actualite__type--${b.filet}`}>{b.libelle}</span>
                  </td>
                  <td>{b.titre}</td>
                  <td>{b.message}</td>
                  <td>{b.auteur}</td>
                  <td className="rig-table__mono">{b.horodatage}</td>
                  <td className="rig-table__mono">{b.retire}</td>
                  <td>
                    <button className="rig-bouton" title="Retirer cette actualite de l'accueil"
                            onClick={() => geste('Retirer', i)}>Retirer</button>
                  </td>
                </tr>
              ))}
              {vue.liste.length === 0 && (
                <tr><td colSpan={7} className="rig-table__vide">Aucune actualite. La premiere publiee s'affichera sur l'accueil de tous les postes.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
