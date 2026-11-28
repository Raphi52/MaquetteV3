// Ecran « Certificat NF525 de la V3 » : « a maquetter » dans le carnet (SOC-019 T04). Sans origine RIG v2 : la V2 n'a pas
// d'ecran du certificat ; il circule en piece jointe, et un greffe qui doit le presenter a l'administration fiscale le
// demande a l'assistance d'Amitel.
//
// A gauche, ce que vaut le certificat aujourd'hui pour la version en service : valide, expire, autre version, ou
// « certification en cours » quand Amitel n'en a encore enregistre aucun -- jamais un faux certificat, pas meme en
// demonstration. A droite, le certificat tel que l'organisme accredite l'a delivre : organisme, accreditation, numero,
// referentiel, version couverte, delivre le, valide jusqu'au (SOC-019 CA04). La regle de validite vit dans
// Contrats/Socle/Certification.cs (la meme que le demarrage de la beta) et le serveur la joue
// (Serveur/Socle/Certification, prouve par tests/SocleBetaCheck) ; tout passe par Pont/EcranCertificat.cs.
// Un compte d'Amitel y ENREGISTRE le certificat recu (formulaire EnregistrerCertificat, SOC-019 T03 ; plan « RIG v3 complet »,
// lot 13). fix-ok: PUT RoutesCertification.Enregistrer n'etait joue que par tests/SocleBetaCheck (grep du 28/11) : aucun ecran
// ne l'offrait, le geste etait injouable depuis la V3.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateCourte } from '../criteres'
import { quand } from './EcranComptes'

export interface Certificat {
  logiciel: string
  organisme: string
  accreditation: string
  numero: string
  referentiel: string
  versionCouverte: string
  delivreLe: string
  valideJusquau: string
}

export interface EtatCertificat {
  greffe: string | null
  etat: 'valide' | 'certification en cours' | 'expire' | 'pas encore valide' | 'autre version' | 'incomplet'
  certificat: Certificat | null
  versionEnService: string
  aujourdhui: string
  motifs: string[]
  enregistreLe: string | null
}

export interface VueEcranCertificat {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  etat: EtatCertificat | null
  /** Compte d'Amitel : l'ecran offre d'enregistrer le certificat recu de l'organisme (le serveur le verifie : 403 sinon). */
  peutEnregistrer?: boolean
}

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const Aide = { margin: 'var(--rig-esp-2) 0 0', fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const
const GrilleRepartie = { tableLayout: 'fixed' } as const
/** La colonne des libelles de la fiche du certificat : l'entete de colonne des grilles, en ligne. */
const Libelle = { fontWeight: 600, color: 'var(--rig-texte-3)' } as const

/** La pastille de l'etat du certificat, et la phrase qui le dit au greffe. */
export function pastilleCertificat(etat: EtatCertificat['etat']): { classe: string; libelle: string; phrase: string } {
  switch (etat) {
    case 'valide':
      return { classe: 'ok', libelle: 'Valide', phrase: 'Le certificat couvre la version de la V3 en service : vous pouvez le présenter à l\'administration fiscale.' }
    case 'certification en cours':
      return { classe: 'attente', libelle: 'Certification en cours', phrase: 'Amitel n\'a pas encore enregistré de certificat pour la V3 : la certification par l\'organisme accrédité est en cours.' }
    case 'expire':
      return { classe: 'alerte', libelle: 'Expiré', phrase: 'Le dernier certificat enregistré n\'est plus valide : Amitel doit enregistrer son renouvellement.' }
    case 'autre version':
      return { classe: 'alerte', libelle: 'Autre version', phrase: 'Le certificat enregistré couvre une autre version de la V3 que celle en service.' }
    case 'pas encore valide':
      return { classe: 'attente', libelle: 'Pas encore valide', phrase: 'Le certificat enregistré prend effet à une date à venir.' }
    default:
      return { classe: 'alerte', libelle: 'Incomplet', phrase: 'Le certificat enregistré ne nomme pas un organisme accrédité.' }
  }
}

/** « 2026-11-21 » (DateOnly du serveur) en « 21/11/2026 ». */
function jour(d: string): string {
  return dateCourte(d.slice(0, 10))
}

/** Les champs du certificat que l'organisme delivre (certificat-v3.json du dossier de la beta), saisis par Amitel. */
const ChampsCertificat: [keyof Certificat, string, string][] = [
  ['organisme', 'Organisme', 'text'], ['accreditation', 'Accréditation', 'text'], ['numero', 'Numéro du certificat', 'text'],
  ['referentiel', 'Référentiel', 'text'], ['versionCouverte', 'Version couverte', 'text'],
  ['delivreLe', 'Délivré le', 'date'], ['valideJusquau', 'Valide jusqu\'au', 'date'],
]

/**
 * « Enregistrer le certificat » (SOC-019 T03), offert aux seuls comptes d'Amitel : le certificat recu de l'organisme accredite,
 * enregistre une fois dans la base commune pour les 40 greffes (Pont/EcranCertificat.cs, EnregistrerAsync). Un refus du serveur
 * (certificat incomplet, compte hors Amitel) est dit sous le formulaire ; rien n'est enregistre.
 */
function EnregistrerCertificat({ version, surFait }: { version: string; surFait: (v: VueEcranCertificat) => void }) {
  const [saisie, setSaisie] = useState<Certificat>({
    logiciel: 'V3', organisme: '', accreditation: '', numero: '', referentiel: 'NF525',
    versionCouverte: version.split('.').slice(0, 2).join('.'), delivreLe: '', valideJusquau: '',
  })
  const [refus, setRefus] = useState<string | null>(null)
  const [envoi, setEnvoi] = useState(false)
  const complet = ChampsCertificat.every(([cle]) => saisie[cle].trim() !== '')

  async function enregistrer() {
    setEnvoi(true)
    try {
      const v = await appeler<VueEcranCertificat>('EcranCertificat.Enregistrer', saisie)
      if (v.mode === 'serveur') { setRefus(null); surFait(v) } else setRefus(v.message)
    } catch {
      setRefus("Le pont n'a pas répondu : le certificat n'est pas enregistré.")
    } finally {
      setEnvoi(false)
    }
  }

  return (
    <div className="rig-criteres__bloc">
      <div className="rig-kicker">Enregistrer le certificat reçu (Amitel)</div>
      {ChampsCertificat.map(([cle, libelle, type]) => (
        <label key={cle} className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">{libelle}</span>
          <input className="rig-saisie-champ__saisie" type={type} value={saisie[cle]}
                 onChange={(e) => setSaisie({ ...saisie, [cle]: e.target.value })} />
        </label>
      ))}
      <button className="rig-bouton rig-bouton--fort" disabled={!complet || envoi} onClick={() => void enregistrer()}>
        Enregistrer le certificat
      </button>
      {refus && <p style={Aide} role="alert"><span className="rig-etat rig-etat--alerte">Refusé</span> {refus}</p>}
    </div>
  )
}

export function EcranCertificat() {
  const [vue, setVue] = useState<VueEcranCertificat | null>(null)

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranCertificat>('EcranCertificat.Ouvrir').then((v) => { if (vivant) setVue(v) })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Lecture du certificat…</p></div>

  if (!vue.etat) {
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete"><div className="rig-kicker">Certificat NF525 de la V3</div></div>
          <p style={Message} role="alert">
            <span className="rig-etat rig-etat--alerte">{vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe' : 'Serveur injoignable'}</span>
            {' '}{vue.message}
          </p>
        </section>
      </div>
    )
  }

  const etat = vue.etat
  const p = pastilleCertificat(etat.etat)
  const c = etat.certificat
  const lignes: [string, string][] = c ? [
    ['Organisme', c.organisme],
    ['Accréditation', c.accreditation],
    ['Numéro du certificat', c.numero],
    ['Référentiel', c.referentiel],
    ['Version couverte', c.versionCouverte],
    ['Délivré le', jour(c.delivreLe)],
    ['Valide jusqu\'au', jour(c.valideJusquau)],
  ] : []

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Ce qu'il vaut aujourd'hui</div>
        <p style={{ margin: 0 }} role="status" aria-label="État du certificat">
          <span className={`rig-etat rig-etat--${p.classe}`}>{p.libelle}</span>
        </p>
        <p style={Aide}>{p.phrase}</p>
        {etat.motifs.length > 0 && etat.etat !== 'certification en cours' && (
          <ul style={{ ...Aide, paddingLeft: 'var(--rig-esp-4)' }} aria-label="Ce qui manque">
            {etat.motifs.map((m) => <li key={m}>{m}</li>)}
          </ul>
        )}
        <p style={Aide}>
          {'Version de la V3 en service : '}<b>{etat.versionEnService}</b>{' — jugé le '}{jour(etat.aujourdhui)}
          {etat.greffe ? ` pour le greffe ${etat.greffe}` : ''}{'.'}
        </p>
        <p style={Aide}>
          La V3 et l'ancien RIG ont chacun leur certificat : celui de l'ancien RIG reste sur sa fiche habituelle.
        </p>
        {vue.mode === 'serveur' && vue.peutEnregistrer && <EnregistrerCertificat version={etat.versionEnService} surFait={setVue} />}
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Certificat NF525 de la V3</div>
          <span className="rig-resultats-proc__compte">
            {vue.mode === 'demo' ? 'Démonstration' : etat.enregistreLe ? `Enregistré par Amitel ${quand(etat.enregistreLe)}` : 'Aucun certificat enregistré'}
          </span>
        </div>

        {vue.message && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">{vue.mode === 'demo' ? 'Démonstration' : 'Information'}</span>
            {' '}{vue.message}
          </p>
        )}

        {c ? (
          <div className="rig-table-cadre">
            <table className="rig-table" style={GrilleRepartie} aria-label="Le certificat">
              <colgroup><col style={{ width: '190px' }} /><col /></colgroup>
              <tbody>
                {lignes.map(([libelle, valeur]) => (
                  <tr key={libelle}>
                    <td style={Libelle}>{libelle}</td>
                    <td className={libelle === 'Numéro du certificat' || libelle === 'Version couverte' ? 'rig-table__mono' : undefined}
                        title={valeur}>{valeur}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p style={Message} role="note">
            <span className={`rig-etat rig-etat--${p.classe}`}>{p.libelle}</span>
            {' '}Aucun certificat à montrer : la V3 n'en affiche jamais un d'exemple. Dès qu'Amitel aura enregistré le certificat
            délivré par l'organisme accrédité, il apparaîtra ici, avec sa date de validité.
          </p>
        )}
      </section>
    </div>
  )
}
