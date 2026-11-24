// Ecran PROC_ACTUALITES : la REDACTION des actualites affichees sur l'accueil
// de tout le monde. Rien n'est calcule ici : quelqu'un ecrit (« L'INPI est
// HS »), et le texte part dans le fichier PARTAGE lu par les autres postes.
// C'est la destination du clic sur la tuile « Actualites ».
//
// Porte depuis Components/Ecrans/EcranActualites.razor (lot 3) : meme balisage,
// memes classes CSS. Ecran A SESSION (src/session.tsx) : la liste et le depot
// partage (DepotActualites) vivent dans Pont/EcranActualites.cs, qui ecrit le
// fichier ; React affiche la vue et remonte chaque geste.
//
// LISTE MODERNE (24/11/2026, plan des listes modernes, lot 7) : la liste des actualites publiees prend
// les briques de composants/ListeModerne.tsx -- tete (synthese : epinglees, alertes), type en pastille
// de la couleur de son filet, signature en avatar d'initiales, pied. Sans « Aller a… » : une actualite
// se retire seule apres 1 a 30 jours (Metier/Actualites.cs), la liste en compte quelques-unes. La
// redaction (gauche), « Publier » et « Retirer » ne changent pas.
// fix-ok: application du socle des listes modernes en plusieurs pas, pas un correctif.

import { useEffect, useRef, type TextareaHTMLAttributes } from 'react'
import { appeler } from '../pont'
import { Saisie, useSession, type PontDeSession, type VueDeSession } from '../session'
import { CelluleIdentite, EnteteListe, ListeModerne, Pastille, PiedListe, type Teinte } from '../composants/ListeModerne'

/** La pastille du type : la teinte la plus proche de la couleur de son filet (rig-charte.css, .rig-actualite__type--…).
 *  Le socle n'a pas de dore : Procedure prend l'orange, comme Panne. Le tableau d'avant les montrait deja en deux
 *  bruns voisins (#A4651F et #8A6D1F) ; le libelle de la pastille les distingue. */
const TeinteType: Record<string, Teinte> = {
  'act-fraude': 'rouge', 'act-panne': 'orange', 'act-procedure': 'orange', 'act-info': 'bleu', 'act-retabli': 'vert',
}
/** Les types qui signalent un service en defaut (le compte rendu les dit en « alerte », Pont/EcranActualites.cs). */
const FiletsAlerte = new Set(['act-fraude', 'act-panne'])
/** Les colonnes : Type, Titre, Message, Signature, Publie, Retire, le bouton « Retirer ». */
const NbColonnes = 7

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
  const n = vue.liste.length
  const epinglees = vue.liste.filter((b) => b.retire === '—').length
  const alertes = vue.liste.filter((b) => FiletsAlerte.has(b.filet)).length

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

      <ListeModerne etiquette="Actualites publiees">
        <EnteteListe titre="Actualites publiees" detail={vue.cheminPartage}
                     synthese={<><b>{n}</b>{n > 1 ? ' actualites · ' : ' actualite · '}<b>{epinglees}</b>{epinglees > 1 ? ' epinglees · ' : ' epinglee · '}
                       <b>{alertes}</b>{alertes > 1 ? ' alertes' : ' alerte'}</>} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 112 }} /><col style={{ width: '24%' }} /><col /><col style={{ width: '20%' }} />
              <col style={{ width: 96 }} /><col style={{ width: 64 }} /><col style={{ width: 84 }} />
            </colgroup>
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
                  <td title={b.libelle}><Pastille teinte={TeinteType[b.filet] ?? 'gris'}>{b.libelle}</Pastille></td>
                  <td title={b.titre}><b>{b.titre}</b></td>
                  <td title={b.message}>{b.message}</td>
                  <CelluleIdentite nom={b.auteur} titre={b.auteur} />
                  <td className="rig-table__mono">{b.horodatage}</td>
                  <td className="rig-table__mono">{b.retire}</td>
                  <td>
                    <button className="rig-lm-outil" title="Retirer cette actualite de l'accueil"
                            onClick={() => geste('Retirer', i)}>Retirer</button>
                  </td>
                </tr>
              ))}
              {vue.liste.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">Aucune actualite. La premiere publiee s'affichera sur l'accueil de tous les postes.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{n}</b>{n > 1 ? ' actualites affichees' : ' actualite affichee'}</span>
          <span>Epinglees <b>{epinglees}</b></span>
          <span>Alertes <b>{alertes}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
