// Ecran de la procedure PROC_RECHAFF — recherche d'affaires.
//
// Reference RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_RECHAFF) :
//   - Operations/OPE_CRITERES.cs : n° affaire, n° demande, n° facture, dates
//   - Ults/ULT_GRIDVIEW_RECHAFF.cs : colonnes N° Affaire, Libelle,
//     Date creation, Solde, Demandes, Factures.
//
// Les criteres d'INSTANCE (id instance, n° de role, code affaire, nom
// instance ou partie, etat, fenetre de saisine) et le panneau de suppression
// XXAFF viennent de l'ecran « Instances judiciaires » : la recherche
// d'affaire doit couvrir ces champs sans obliger a changer d'ecran.
//
// Deux differences ASSUMEES avec v2, parce que ce n'est pas un portage :
//   - aucun rattachement de demande a l'affaire. L'ecran ne fait que LIRE.
//   - noAffaire pre-remplit le critere et lance la recherche (tuile « Mes
//     affaires recentes »).
//
// Porte depuis Components/Ecrans/EcranRechAffaires.razor (lot 3) : meme
// balisage, memes classes CSS. Le fonds d'affaires, le filtre et le TRI (ordre
// des chaines .NET) sont faits par Pont/EcranRechAffaires.cs ; ici restent les
// criteres saisis, la colonne de tri choisie, la selection et la suppression
// simulee. La coquille .razor monte cet ecran et recoit ouvrir / ouvrirUnDossier.
//
// L'ANCIEN RIG (SOC-014, SOC-015 ; lot 3 de la phase 2, Pont/AncienRig.cs) : une affaire creee avant le
// passage du greffe a la V3 porte la mention « traitee dans la V2 » ; choisie, elle propose « Ouvrir dans
// l'ancien RIG » (la V2 du greffe, lancee ou ramenee, sur sa recherche d'affaires, numero copie), et sa
// suppression est refusee ici (elle se fait dans l'ancien RIG). Le double-clic l'ouvre toujours dans la
// visionneuse : la consulter dans la V3 reste permis. L'aiguillage est demande au pont quand la LISTE des
// numeros change (pas a chaque tri) ; s'il ne repond pas, l'ecran reste tel qu'avant ce lot.
// Lot 11 (REC-010 ; SOC-015 CA01 laisse ouvert par le lot 3, decision du chef) : le bloc propose aussi « Voir son historique
// (lecture seule) » -- ses affaires et demandes lues par le pont de l'historique (Pont/EcranHistoriqueV2.cs). fix-ok: ajout en
// quatre petits pas (import, etat, remise a zero a la selection, bouton), pas un correctif.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { EnteteTriable } from '../composants/EnteteTriable'
import { dateSaisie } from '../saisie'
import { toucheDeRecherche } from '../plan/clavier'
import type { VueEcranHistorique } from './EcranHistoriqueV2'

export interface NatureAffaire {
  code: string
  nom: string
}

export interface LigneAffaire {
  numero: string
  idInstance: string
  role: string
  codeAffaire: string
  nature: string
  nomNature: string
  libelle: string
  nom: string
  etat: string
  siren: string
  type: string
  saisine: string
  creation: string
  /** Le solde deja mis en forme (« 142,50 »), comme le ToString("N2") de Blazor. */
  solde: string
}

export interface AffaireChoisie {
  numero: string
  idInstance: string
  role: string
}

export interface VueAffaires {
  natures: NatureAffaire[]
  etats: string[]
  lignes: LigneAffaire[]
  /** « 7 instance, 3 procedure collective, … » */
  resume: string
  aucunCritere: boolean
  sirenCherche: string
  choisie: AffaireChoisie | null
}

/** Le logiciel d'une affaire, dit par l'aiguillage (Pont/AncienRig.cs, LigneAiguillee). */
export interface LigneAiguillee {
  numero: string
  /** « V2 » : creee avant le passage du greffe, traitee dans l'ancien RIG ; « V3 » sinon. */
  logiciel: string
  motif: string
}

export interface VueAiguillage {
  mode: string
  greffe: string | null
  passage: string | null
  message: string | null
  affaires: LigneAiguillee[]
}

/** Le compte rendu de « Ouvrir dans l'ancien RIG » (Pont/AncienRig.cs, ResultatAncienRig). */
export interface ResultatAncienRig {
  fait: boolean
  geste: string
  numero: string
  message: string
  ligneDeCommande: string | null
}

export interface ProprietesEcranRechAffaires {
  /** Numero d'affaire pre-rempli a l'ouverture (tuile de l'accueil). */
  noAffaire?: string | null
  /** Remonte le numero choisi ; la page ouvre la visionneuse. */
  ouvrir: (numero: string) => void
  /**
   * Remonte un SIREN a immatriculer : la recherche n'a rien trouve, c'est donc
   * ICI que l'agent constate qu'il faut ouvrir le dossier. Le renvoyer au menu
   * lui ferait retaper le numero.
   */
  ouvrirUnDossier: (siren: string) => void
}

/** Les colonnes de la grille : libelle affiche et cle de tri comprise par le pont. */
const Colonnes: ReadonlyArray<readonly [libelle: string, cle: string]> = [
  ['N° Affaire', 'Numero'],
  ['Id instance', 'IdInstance'],
  ['N° de role', 'Role'],
  ['Code affaire', 'CodeAffaire'],
  ['Nature', 'Nature'],
  ['Libelle', 'Libelle'],
  ['Nom', 'Nom'],
  ['Etat', 'Etat'],
  ['SIREN', 'Siren'],
  ['Domaine', 'Type'],
  ['Saisine', 'Saisine'],
  ['Date creation', 'Creation'],
  ['Solde', 'Solde'],
]

export function EcranRechAffaires({ noAffaire, ouvrir, ouvrirUnDossier }: ProprietesEcranRechAffaires) {
  // Le critere n'est pose qu'a l'OUVERTURE : ensuite l'utilisateur est maitre
  // de ses criteres, un nouveau rendu ne doit pas les ecraser.
  const [critNumero, setCritNumero] = useState(() => (noAffaire ?? '').trim())
  const [critLibelle, setCritLibelle] = useState('')
  const [critNature, setCritNature] = useState('')
  const [critType, setCritType] = useState('')
  const [critIdInstance, setCritIdInstance] = useState('')
  const [critRole, setCritRole] = useState('')
  const [critCodeAffaire, setCritCodeAffaire] = useState('')
  const [critEtat, setCritEtat] = useState('')
  const [critNom, setCritNom] = useState('')
  const [critSaisineDu, setCritSaisineDu] = useState('')
  const [critSaisineAu, setCritSaisineAu] = useState('')

  const [selection, setSelection] = useState<string | null>(null)
  const [libererNumeros, setLibererNumeros] = useState(true)
  const [message, setMessage] = useState<string | null>(null)

  // La colonne sur laquelle la grille est rangee ; null = ordre d'origine
  // (du plus recent au plus ancien).
  const [cleTri, setCleTri] = useState<string | null>(null)
  const [triAscendant, setTriAscendant] = useState(true)

  // null tant que le pont n'a pas repondu : ni ligne ni « aucune affaire ».
  const [vue, setVue] = useState<VueAffaires | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueAffaires>('EcranRechAffaires.Trouver', {
      critNumero, critLibelle, critType, critNature, critIdInstance, critRole, critCodeAffaire,
      critNom, critEtat, critSaisineDu: dateSaisie(critSaisineDu), critSaisineAu: dateSaisie(critSaisineAu),
    }, cleTri, triAscendant, selection).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critNumero, critLibelle, critType, critNature, critIdInstance, critRole, critCodeAffaire,
      critNom, critEtat, critSaisineDu, critSaisineAu, cleTri, triAscendant, selection])

  const trouvees = vue?.lignes ?? []
  const choisie = vue?.choisie ?? null

  // Le logiciel de chaque affaire affichee, par numero. La cle est l'ENSEMBLE des numeros : un tri ne change
  // rien, donc ne redemande rien.
  const [aiguillage, setAiguillage] = useState<Record<string, LigneAiguillee>>({})
  const [ancienRig, setAncienRig] = useState<ResultatAncienRig | null>(null)
  // SOC-015 CA01 (lot 11, REC-010) : l'historique d'une ancienne affaire, en lecture, par le pont de l'historique de l'ancien RIG.
  const [historique, setHistorique] = useState<VueEcranHistorique | null>(null)
  function voirHistorique(numero: string) {
    void appeler<VueEcranHistorique>('EcranRechAffaires.HistoriqueAncienne', numero).then((v) => setHistorique(v), () => setHistorique(null))
  }
  const cleNumeros = [...new Set(trouvees.map((a) => a.numero))].sort().join('\n')
  useEffect(() => {
    let perime = false
    if (cleNumeros === '') { setAiguillage({}); return }
    appeler<VueAiguillage>('EcranRechAffaires.Aiguiller', cleNumeros.split('\n')).then(
      (v) => { if (!perime) setAiguillage(Object.fromEntries(v.affaires.map((a) => [a.numero, a]))) },
      // Sans reponse, aucune mention : l'ecran reste celui d'avant l'aiguillage.
      () => { if (!perime) setAiguillage({}) })
    return () => { perime = true }
  }, [cleNumeros])

  /** L'affaire choisie se traite-t-elle dans l'ancien RIG ? Alors son aiguillage, sinon null. */
  const choisieV2 = choisie !== null && aiguillage[choisie.numero]?.logiciel === 'V2' ? aiguillage[choisie.numero]! : null

  /** SOC-015 CA02, CA03 : la V2 du greffe, lancee ou ramenee, numero copie. */
  function ouvrirDansAncienRig(numero: string) {
    void appeler<ResultatAncienRig>('EcranRechAffaires.OuvrirDansAncienRig', numero).then(
      (r) => setAncienRig(r),
      (e: unknown) => setAncienRig({ fait: false, geste: 'refuse', numero, ligneDeCommande: null,
                                     message: `L'ancien RIG n'a pas pu etre ouvert : ${e instanceof Error ? e.message : String(e)}` }))
  }

  /** Premier clic : ordre croissant. Second clic sur la MEME colonne : ordre inverse. C'est le geste de RIG v2. */
  function basculerTri(cle: string) {
    if (cleTri === cle) setTriAscendant(!triAscendant)
    else { setCleTri(cle); setTriAscendant(true) }
  }

  /** Le filtrage est continu ; le bouton efface seulement le compte rendu. */
  function rechercher() {
    setMessage(null)
  }

  function vider() {
    setCritNumero('')
    setCritLibelle('')
    setCritType('')
    setCritNature('')
    setCritIdInstance('')
    setCritRole('')
    setCritCodeAffaire('')
    setCritNom('')
    setCritEtat('')
    setCritSaisineDu('')
    setCritSaisineAu('')
    setSelection(null)
    setMessage(null)
  }

  /**
   * Ecran de demonstration : la suppression XXAFF n'ecrit rien, elle
   * rend compte de ce qui serait supprime et du sort des numeros metiers.
   */
  function supprimer() {
    if (choisie === null) return
    // SOC-015 CA04 : une ancienne affaire ne se modifie pas dans la V3 ; le bloc « Ancien RIG » propose de l'y ouvrir.
    if (choisieV2 !== null) {
      setMessage(`Instance ${choisie.idInstance} (${choisie.numero}) : affaire traitee dans la V2, elle ne se supprime pas ici.`
        + ' Ouvrez-la dans l\'ancien RIG.')
      return
    }
    setMessage(`Instance ${choisie.idInstance} (${choisie.numero}) : suppression simulee`
      + (libererNumeros ? ' — numeros metiers liberes.' : ' — numeros metiers conserves.'))
  }

  return (
    <div className="rig-proc__corps">

      {/* Entree (ou F5, F7) dans le bloc des criteres = clic sur Rechercher. C'est le
          geste de RIG v2 : on tape, on valide, sans lacher le clavier. */}
      <aside className="rig-criteres" onKeyDown={(e) => { if (toucheDeRecherche(e)) rechercher() }}>
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N° Affaire</span>
          <input className="rig-saisie-champ__saisie" value={critNumero} onChange={(e) => setCritNumero(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Libelle, SIREN</span>
          <input className="rig-saisie-champ__saisie" value={critLibelle} onChange={(e) => setCritLibelle(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nature</span>
          <select className="rig-saisie-champ__saisie" value={critNature} onChange={(e) => setCritNature(e.target.value)}>
            <option value="">Toutes</option>
            {(vue?.natures ?? []).map((n) => (
              <option key={n.code} value={n.code}>{n.nom}</option>
            ))}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Domaine</span>
          <select className="rig-saisie-champ__saisie" value={critType} onChange={(e) => setCritType(e.target.value)}>
            <option value="">Tous</option>
            <option value="RCS">RCS</option>
            <option value="JUD">Judiciaire</option>
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Instance judiciaire</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Id instance</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 55339"
                     value={critIdInstance} onChange={(e) => setCritIdInstance(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Numero de role</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 2024F00123"
                     value={critRole} onChange={(e) => setCritRole(e.target.value)} />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Code affaire</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 010"
                     value={critCodeAffaire} onChange={(e) => setCritCodeAffaire(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Etat</span>
              <select className="rig-saisie-champ__saisie" value={critEtat} onChange={(e) => setCritEtat(e.target.value)}>
                <option value="">Tous</option>
                {(vue?.etats ?? []).map((e) => (
                  <option key={e} value={e}>{e}</option>
                ))}
              </select>
            </label>
          </div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nom (instance ou partie)</span>
            <input className="rig-saisie-champ__saisie" placeholder="ex. DUPONT"
                   value={critNom} onChange={(e) => setCritNom(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Saisine</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date"
                     value={critSaisineDu} onChange={(e) => setCritSaisineDu(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date"
                     value={critSaisineAu} onChange={(e) => setCritSaisineAu(e.target.value)} />
            </label>
          </div>
        </div>

        <div className="rig-criteres__actions">
          <div className="rig-criteres__paire">
            <button className="rig-bouton rig-bouton--fort" onClick={rechercher}>Rechercher</button>
            <button className="rig-bouton" onClick={vider}>Reinitialiser</button>
          </div>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Affaires</div>
          <span className="rig-resultats-proc__compte">
            {/* Memes noeuds de texte que Razor (« Resultats ( », le nombre, « ) — », le
                resume) : un seul noeud deplacait le lissage des lettres de quelques
                pixels a la comparaison des captures. */}
            {vue && <>{'\n                Resultats ('}{trouvees.length}{') —\n                '}{vue.resume}</>}
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                {Colonnes.map(([libelle, cle]) => (
                  <EnteteTriable key={cle} libelle={libelle} cle={cle} cleActive={cleTri}
                                 ascendant={triAscendant} trier={basculerTri} />
                ))}
              </tr>
            </thead>
            <tbody>
              {trouvees.map((affaire, i) => (
                <tr key={`${i}:${affaire.numero}`}
                    className={affaire.numero === selection ? 'rig-ligne--valide' : undefined}
                    onClick={() => { setSelection(affaire.numero); setAncienRig(null); setHistorique(null) }}
                    onDoubleClick={() => ouvrir(affaire.numero)} style={{ cursor: 'pointer' }}>
                  <td className="rig-table__mono">
                    {affaire.numero}
                    {aiguillage[affaire.numero]?.logiciel === 'V2' && (
                      <span className="rig-badge" title={aiguillage[affaire.numero]!.motif}
                            style={{ marginLeft: 'var(--rig-esp-2)', whiteSpace: 'nowrap' }}>traitee dans la V2</span>
                    )}
                  </td>
                  <td className="rig-table__mono">{affaire.idInstance}</td>
                  <td className="rig-table__mono">{affaire.role}</td>
                  <td className="rig-table__mono">{affaire.codeAffaire}</td>
                  <td>
                    <span className={`rig-nature rig-nature--${affaire.nature}`}>
                      <span className="rig-nature__filet"></span>
                      <span className="rig-nature__nom">{affaire.nomNature}</span>
                    </span>
                  </td>
                  <td>{affaire.libelle}</td>
                  <td>{affaire.nom}</td>
                  <td>{affaire.etat}</td>
                  <td className="rig-table__mono">{affaire.siren}</td>
                  <td>{affaire.type}</td>
                  <td className="rig-table__mono">{affaire.saisine}</td>
                  <td className="rig-table__mono">{affaire.creation}</td>
                  <td className="rig-table__mono">{affaire.solde} €</td>
                </tr>
              ))}
              {vue && trouvees.length === 0 && (
                <tr><td colSpan={13} className="rig-table__vide">
                  {vue.aucunCritere ? (
                    'Saisissez au moins un critere, puis lancez la recherche.'
                  ) : (
                    <>
                      <div>Aucune affaire ne repond a ces criteres.</div>
                      <div style={{ marginTop: 'var(--rig-esp-3)' }}>
                        <button className="rig-bouton rig-bouton--fort"
                                onClick={() => ouvrirUnDossier(vue.sirenCherche)}>
                          Ouvrir un dossier
                        </button>
                        {/* Aucun blanc entre le bouton et le span (Razor retire celui qui
                            precede un @if) ; le blanc est DANS le span, a sa police. */}
                        {vue.sirenCherche.length > 0 && (
                          <span className="rig-resultats-proc__compte"
                                style={{ marginLeft: 'var(--rig-esp-3)' }}>
                            {' pour le SIREN '}{vue.sirenCherche}
                          </span>
                        )}
                      </div>
                    </>
                  )}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        {/* Le bloc n'apparait que pour une ancienne affaire choisie (ou le compte rendu du geste) : pour une
            affaire de la V3, l'ecran est celui d'avant. fix-ok: ajout SOC-015 T01 en plusieurs pas, pas un correctif. */}
        {(choisieV2 !== null || ancienRig !== null) && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3) var(--rig-esp-4)' }}>
            <div className="rig-kicker">Ancien RIG</div>
            {choisieV2 !== null && (
              <span className="rig-resultats-proc__compte">
                {choisieV2.numero}{' · '}{choisieV2.motif}
              </span>
            )}
            {choisieV2 !== null && (
              <button className="rig-bouton rig-bouton--fort" onClick={() => ouvrirDansAncienRig(choisieV2.numero)}>
                Ouvrir dans l'ancien RIG
              </button>
            )}
            {choisieV2 !== null && (
              <button className="rig-bouton" onClick={() => voirHistorique(choisieV2.numero)}>
                Voir son historique (lecture seule)
              </button>
            )}
            {historique !== null && (
              <span className="rig-resultats-proc__compte" role="status" aria-label="Historique de l'ancienne affaire">
                {historique.historique
                  ? `${historique.historique.titre ?? choisieV2?.numero ?? ''} : ` + (historique.historique.affaires.length === 0 ? 'aucune affaire trouvée'
                      : historique.historique.affaires.map((a) => `${a.libelle ?? a.id} (${a.marque}, ${a.etat}, ${a.demandes.length} demande(s) : `
                          + a.demandes.map((d) => `${d.numero ?? d.id} ${d.etatLibelle}`).join(', ') + ')').join(' ; '))
                  : historique.message}
              </span>
            )}
            {ancienRig !== null && (
              <span className="rig-resultats-proc__compte" role="status">{ancienRig.message}</span>
            )}
          </div>
        )}

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3) var(--rig-esp-4)' }}>
          <div className="rig-kicker">Suppression (XXAFF)</div>
          {choisie === null ? (
            <span className="rig-resultats-proc__compte">
              Selectionnez une instance dans les resultats.
            </span>
          ) : (
            <span className="rig-resultats-proc__compte">
              {choisie.numero}{' · instance '}{choisie.idInstance}{' · role '}{choisie.role}
            </span>
          )}
          <button className={`rig-bascule${libererNumeros ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setLibererNumeros(!libererNumeros)}>
            <span className="rig-bascule__temoin"></span>
            <span>Liberer les numeros metiers</span>
          </button>
          <button className="rig-bouton rig-bouton--fort" disabled={choisie === null}
                  onClick={supprimer}>Supprimer l'instance</button>
          {message !== null && message !== '' && (
            <span className="rig-resultats-proc__compte">{message}</span>
          )}
        </div>

        <div className="rig-resultats-proc__entete">
          <span className="rig-resultats-proc__compte">
            Double-clic sur une ligne pour ouvrir l'affaire dans la visionneuse.
          </span>
        </div>
      </section>
    </div>
  )
}
