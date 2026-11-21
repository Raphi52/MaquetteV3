// Ecran « Messagerie » (SOC-006 T04, CA05). Procedure RIG v2 : PROC_GESTEMAIL (« Modele de mail », code GESTEMAIL) :
// la recherche des modeles (OPE_RECHERCHE_EMAILMODEL : colonnes nom, libelle ecran, objet ; filtre sur le nom au fil de
// la saisie ; les modeles desactives caches, « Afficher les éléments inactifs »), la fiche d'un modele (OPE_EDIT_EMAILMODEL
// et ses sous-operations : nom, libelle ecran, objet, texte, expediteur, « Répondre à », « Copie cachée », destinataire
// par defaut et ses listes, cases « Utilisable … », envoi automatique pour un modele RCS) et les listes de distribution
// (OPE_RECHERCHE_LISTEDISTRIBUTIONMAIL : code, libelle, RCS, judiciaire, proprietaire, actif).
//
// Tout vient de la base DU GREFFE, par Pont/EcranMessagerie.cs et le serveur (RoutesCourriels.Modeles, .Modele,
// .ListesDiffusion). CA05 : un agent du greffe ne recoit aucun modele reserve a Amitel, ni par la liste ni par son nom,
// et n'a pas la bascule « Amitel » (la V2 ne montre CheckBoxAmitel qu'a un agent d'Amitel). L'apercu est le courriel tel
// qu'il partirait de ce greffe (habillage, jetons, logo, adresses du greffe), avec deux documents d'exemple comme
// l'apercu de la V2 ; il s'affiche dans un cadre sans script. Lecture seule : creer, modifier ou desactiver un modele
// reste a faire (socle-courriels.md). Sans serveur : la demonstration du pont, avec son bandeau.
// fix-ok: capture claire du 2026-11-21 sur bureau cache -- les cases du modele en rig-chip (puce de code, police a chasse
// fixe) sortaient en gros caracteres : pastilles rig-etat ; le bouton des inactifs dans rig-criteres__actions (margin-top:
// auto) repoussait la liste des resultats au bas du volet : il passe dans un bloc ordinaire.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { contientSansCasse, estBlanc } from '../criteres'

export interface ModeleMessage {
  type: string
  libelleEcran: string | null
  sujet: string
  corpsHtml: boolean
  expediteur: string | null
  reponse: string | null
  copie: string | null
  destinataireDefaut: string | null
  reserveAmitel: boolean
  envoiAutomatique: boolean
  utilisableEnSaisie: boolean | null
  lot1: boolean
  lot2: boolean
  inscription: boolean | null
  comptabilite: boolean | null
}

export interface ListeDiffusion {
  id: number
  code: string
  libelle: string
  adresses: string
  rcs: boolean
  judiciaire: boolean
  proprietaire: string | null
  actif: boolean
}

export interface ApercuModele {
  modele: ModeleMessage
  corps: string
  expediteur: string
  reponse: string
  copie: string
  destinataireDefaut: string
  rendu: string
  habille: boolean
  valeursManquantes: string[]
  listesParDefaut: ListeDiffusion[]
}

export interface VueEcranMessagerie {
  mode: 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'
  message: string | null
  greffe: string | null
  voitReservesAmitel: boolean
  modeles: ModeleMessage[]
  listes: ListeDiffusion[]
}

export interface VueModeleMessagerie {
  mode: string
  message: string | null
  apercu: ApercuModele | null
}

/** Les cases « Utilisable … » de la V2 (OPE_EDIT_EMAILMODEL.Designer.cs : CheckBoxRCS, Judiciaire, INS, CPT) et ce qu'elles lisent. */
const Domaines: { nom: string; libelle: string; vrai: (m: ModeleMessage) => boolean }[] = [
  { nom: 'RCS', libelle: 'Utilisable RCS', vrai: (m) => m.lot1 },
  { nom: 'Judiciaire', libelle: 'Utilisable judiciaire', vrai: (m) => m.lot2 },
  { nom: 'Privilège', libelle: 'Utilisable privilège', vrai: (m) => m.inscription === true },
  { nom: 'Comptabilité', libelle: 'Utilisable comptabilité', vrai: (m) => m.comptabilite === true },
]
/** CheckBoxAmitel de la V2, visible pour un agent d'Amitel seulement (OPE_EDIT_EMAILMODEL.cs:333). */
const DomaineAmitel = { nom: 'Amitel', libelle: 'Modèle modifiable uniquement par Amitel', vrai: (m: ModeleMessage) => m.reserveAmitel }

const Message = { margin: 0, padding: 'var(--rig-esp-2) var(--rig-esp-3)' } as const
const Fiche = { padding: 'var(--rig-esp-3)', display: 'flex', flexDirection: 'column', gap: 'var(--rig-esp-2)', overflow: 'auto' } as const
const Note = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)', overflowWrap: 'anywhere' } as const
const Cadre = { width: '100%', height: '260px', border: '1px solid var(--rig-bord)', borderRadius: 'var(--rig-rayon, 4px)', background: '#fff' } as const
const Coupe = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Liste = { display: 'flex', flexDirection: 'column', gap: '2px' } as const
const Pastilles = { display: 'flex', flexWrap: 'wrap', gap: 'var(--rig-esp-2)' } as const

const ouiNon = (b: boolean) => (b ? 'Oui' : 'Non')
const proprietaire = (p: string | null) => (p === '*' ? 'Tous les agents' : p ?? '—')
const adresses = (a: string) => a.split(';').map((x) => x.trim()).filter((x) => x.length > 0).join(' ; ')
const actif = (m: ModeleMessage) => m.utilisableEnSaisie !== false

function Champ({ libelle, valeur, titre }: { libelle: string; valeur: string; titre?: string }) {
  return (
    <label className="rig-saisie-champ">
      <span className="rig-saisie-champ__libelle">{libelle}</span>
      <input className="rig-saisie-champ__saisie" value={valeur} title={titre} readOnly />
    </label>
  )
}

export function EcranMessagerie() {
  const [vue, setVue] = useState<VueEcranMessagerie | null>(null)
  const [recherche, setRecherche] = useState('')
  const [critDomaines, setCritDomaines] = useState<ReadonlySet<string>>(new Set())
  const [inactifs, setInactifs] = useState(false)
  const [onglet, setOnglet] = useState<'modele' | 'listes'>('modele')
  const [choisi, setChoisi] = useState<string | null>(null)
  const [fiche, setFiche] = useState<VueModeleMessagerie | null>(null)

  const ouvrir = async (type: string) => {
    setChoisi(type)
    setFiche(null)
    setFiche(await appeler<VueModeleMessagerie>('EcranMessagerie.Modele', type))
  }

  useEffect(() => {
    let vivant = true
    void appeler<VueEcranMessagerie>('EcranMessagerie.Ouvrir').then((v) => {
      if (!vivant) return
      setVue(v)
      const premier = v.modeles.find(actif)
      if (premier && (v.mode === 'serveur' || v.mode === 'demo')) void ouvrir(premier.type)
    })
    return () => { vivant = false }
  }, [])

  if (!vue) return <div className="rig-proc__corps"><p className="rig-vide">Chargement des modèles de courriel…</p></div>

  if (vue.mode !== 'serveur' && vue.mode !== 'demo') {
    const libelle = vue.mode === 'refuse' ? 'Accès refusé' : vue.mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Serveur injoignable'
    return (
      <div className="rig-proc__corps">
        <section className="rig-resultats-proc">
          <div className="rig-resultats-proc__entete"><div className="rig-kicker">Messagerie</div></div>
          <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">{libelle}</span>{' '}{vue.message}</p>
        </section>
      </div>
    )
  }

  const demo = vue.mode === 'demo'
  const domaines = vue.voitReservesAmitel ? [...Domaines, DomaineAmitel] : Domaines
  const basculerDomaine = (d: string) => setCritDomaines((avant) => {
    const s = new Set(avant)
    if (!s.delete(d)) s.add(d)
    return s
  })

  const presents = vue.modeles.filter((m) => inactifs || actif(m))
  let trouves = presents
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((m) => contientSansCasse(m.type, t) || contientSansCasse(m.libelleEcran ?? '', t))
  }
  if (critDomaines.size > 0) trouves = trouves.filter((m) => domaines.some((d) => critDomaines.has(d.nom) && d.vrai(m)))
  const listes = vue.listes.filter((l) => inactifs || l.actif)
  const a = fiche?.apercu

  return (
    <div className="rig-proc__corps">
      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Modèles de mail</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom ou libellé</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Domaine</div>
          {domaines.map((d) => (
            <button key={d.nom} type="button" className={`rig-bascule${critDomaines.has(d.nom) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerDomaine(d.nom)}>
              <span className="rig-bascule__temoin"></span><span>{d.nom}</span>
            </button>
          ))}
        </div>

        {/* Le bouton de la V2 (OPE_RECHERCHE_EMAILMODEL, ACTUALISER) : il vaut pour les modeles et pour les listes. Pas dans
            rig-criteres__actions, qui pousse son contenu au bas du volet et la liste des resultats avec lui. */}
        <div className="rig-criteres__bloc">
          <button type="button" className="rig-bouton" onClick={() => setInactifs((x) => !x)}>
            {inactifs ? 'Masquer les éléments inactifs' : 'Afficher les éléments inactifs'}
          </button>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Résultats ({trouves.length})</div>
          <div role="list" aria-label="Modèles" style={Liste}>
            {trouves.map((m) => (
              <div role="listitem" key={m.type}>
                <button type="button" className={`rig-bascule${choisi === m.type ? ' rig-bascule--actif' : ''}`}
                        title={m.sujet} onClick={() => { setOnglet('modele'); void ouvrir(m.type) }}>
                  <span className="rig-bascule__temoin"></span>
                  <span>{m.type} — {m.libelleEcran ?? m.sujet}</span>
                </button>
              </div>
            ))}
          </div>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button type="button" className={`rig-onglet${onglet === 'modele' ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setOnglet('modele')}>Modèle</button>
            <button type="button" className={`rig-onglet${onglet === 'listes' ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setOnglet('listes')}>Listes de distribution</button>
          </div>
          <span className="rig-resultats-proc__compte">
            {demo ? 'Démonstration' : `${presents.length} modèle(s) — greffe ${vue.greffe ?? ''}`}
          </span>
        </div>
        {demo && (
          <p style={Message}>
            <span className="rig-etat rig-etat--attente">Démonstration</span>
            {' '}Sans serveur, des modèles d'exemple s'affichent : noms, objets et cases de la base de développement, textes et adresses fictifs.
          </p>
        )}

        {onglet === 'listes' ? (
          <div className="rig-table-cadre">
            <table className="rig-table">
              <thead>
                <tr>{['Code', 'Libellé', 'RCS', 'Judiciaire', 'Propriétaire', 'Actif', 'Adresses'].map((t) => <th key={t}>{t}</th>)}</tr>
              </thead>
              <tbody>
                {listes.map((l) => (
                  <tr key={l.id}>
                    <td className="rig-table__mono">{l.code}</td>
                    <td>{l.libelle}</td>
                    <td>{ouiNon(l.rcs)}</td>
                    <td>{ouiNon(l.judiciaire)}</td>
                    <td>{proprietaire(l.proprietaire)}</td>
                    <td>{ouiNon(l.actif)}</td>
                    <td className="rig-table__mono">{adresses(l.adresses)}</td>
                  </tr>
                ))}
                {listes.length === 0 && (
                  <tr><td colSpan={7} className="rig-table__vide">Aucune liste de distribution dans ce greffe.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        ) : !choisi ? (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>Choisissez un modèle dans la liste de gauche.</div>
        ) : !fiche ? (
          <p className="rig-vide">Chargement du modèle {choisi}…</p>
        ) : !a ? (
          <p style={Message} role="alert"><span className="rig-etat rig-etat--alerte">Modèle</span>{' '}{fiche.message}</p>
        ) : (
          <div style={Fiche}>
            <div className="rig-criteres__paire">
              <Champ libelle="Nom du modèle" valeur={a.modele.type} />
              <Champ libelle="Libellé écran" valeur={a.modele.libelleEcran ?? ''} />
            </div>
            <Champ libelle="Sujet" valeur={a.modele.sujet} />
            <div className="rig-criteres__paire">
              <Champ libelle="Expéditeur" valeur={a.expediteur} titre={a.modele.expediteur ?? ''} />
              <Champ libelle="Répondre à" valeur={a.reponse} titre={a.modele.reponse ?? ''} />
            </div>
            <div className="rig-criteres__paire">
              <Champ libelle="Copie cachée" valeur={a.copie} titre={a.modele.copie ?? ''} />
              <Champ libelle="Destinataire par défaut" valeur={a.destinataireDefaut} titre={a.modele.destinataireDefaut ?? ''} />
            </div>
            <p style={Note} aria-label="Listes par défaut">
              Listes par défaut : {a.listesParDefaut.length === 0 ? 'aucune' : a.listesParDefaut.map((l) => `${l.code} (${l.libelle})`).join(', ')}
            </p>
            <div style={Pastilles} aria-label="Cases du modèle">
              {domaines.filter((d) => d.vrai(a.modele)).map((d) => <span key={d.nom} className="rig-etat rig-etat--action">{d.libelle}</span>)}
              {a.modele.utilisableEnSaisie === false && <span className="rig-etat rig-etat--attente">Désactivé</span>}
            </div>
            {/* V2 _SetVisibilityChkEnvoiAuto : l'envoi automatique n'existe que pour un modele RCS. */}
            {a.modele.lot1 && (
              <span className={`rig-etat rig-etat--${a.modele.envoiAutomatique ? 'ok' : 'attente'}`}>
                {a.modele.envoiAutomatique ? 'Mail envoyé automatiquement (sans affichage de l\'écran de mail)' : 'Envoi manuel : l\'écran de mail s\'affiche'}
              </span>
            )}
            {a.valeursManquantes.length > 0 && (
              <div style={Note}>
                <span className="rig-etat rig-etat--attente">À compléter</span>
                <ul style={{ margin: 'var(--rig-esp-1) 0 0', paddingLeft: '1.2em' }}>{a.valeursManquantes.map((v) => <li key={v}>{v}</li>)}</ul>
              </div>
            )}
            <div className="rig-kicker">Aperçu du courriel{a.habille ? ' — habillage du greffe' : ''}</div>
            <iframe title="Aperçu du courriel" sandbox="" srcDoc={a.rendu} style={Cadre} />
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Texte du modèle</span>
              <textarea className="rig-saisie-champ__saisie" rows={6} readOnly value={a.corps.split('|').join('\n')} />
            </label>
            <p style={{ ...Note, ...Coupe }} title={a.modele.type}>Format : {a.modele.corpsHtml ? 'HTML' : 'texte'}</p>
          </div>
        )}
      </section>
    </div>
  )
}
