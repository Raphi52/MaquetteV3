// Ecran de la procedure PROC_GCLIENT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_GCLIENT) :
//   - OPE_MOTCLE        : ult_TxtMotCle
//   - OPE_STATUTCLIENT  : ult_ChckBxActif, ult_ChckBxBloque, ult_ChckBxSommeil
//   - OPE_TYPECLIENT    : ult_ChckBxCompte, ult_ChckBxGreffe, ult_ChckBxPC,
//                         ult_ChckBxPreventions, ult_ChckBxWeb
//   - OPE_RESULTAT      : grille ult_DGVResultat, colonnes Id Client,
//                         N Compte, Abrege, Nom, Groupe, Statut, Type
//
// Porte depuis Components/Ecrans/EcranClients.razor (lot 4) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses clients de
// demonstration l'ont suivi ici. La coquille .razor recoit selectionner.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 4) : le resultat prend les briques de
// composants/ListeModerne.tsx. Tete (compte, actifs, « Aller a un client… » Ctrl K, Entree choisit le client reste
// seul ; ce champ EST le critere « Mot cle » de gauche -- nom, abrege ou compte, meme etat, il pose aussi les vues :
// un seul champ pour un seul filtre), deux cartes qui POSENT les criteres existants (Actifs, Type de client),
// des vues = jeux de criteres (statut, type) dont « + Nouvelle vue » pour la session, une ligne par client
// (avatar, nom, abrege · compte ; statut en point, type en pastille), pied de totaux. Les autres criteres de gauche
// et le clic qui remonte l'abrege ne changent pas. Aucun montant dans cette liste : pas de carte en euros.

import { useState } from 'react'
import { contientSansCasse, estBlanc } from '../criteres'
import {
  CartesChiffrees, CelluleIdentite, EnteteListe, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, useVuesDeSession, type Teinte, type VueEnregistree,
} from '../composants/ListeModerne'

interface ClientRcs {
  id: number
  compte: string
  abrege: string
  nom: string
  groupe: string
  statut: string
  type: string
}

// v2 : ult_ChckBxActif, ult_ChckBxBloque, ult_ChckBxSommeil.
const Statuts = ['Actif', 'Bloque', 'En sommeil']
const Types = ['En compte', 'Greffe', 'Prevention', 'PC', 'Web']

const TeinteStatut: Record<string, Teinte> = { Actif: 'vert', Bloque: 'rouge', 'En sommeil': 'gris' }
const TeinteType: Record<string, Teinte> = { 'En compte': 'bleu', Greffe: 'ardoise', Prevention: 'violet', PC: 'orange', Web: 'vert' }

const Clients: ClientRcs[] = [
  { id: 1042, compte: 'C-000142', abrege: 'LEROY', nom: 'SELARL LEROY & ASSOCIES', groupe: 'Mandataires', statut: 'Actif', type: 'En compte' },
  { id: 1088, compte: 'C-000188', abrege: 'BLANC', nom: 'CABINET BLANC', groupe: 'Mandataires', statut: 'Actif', type: 'En compte' },
  { id: 1130, compte: 'C-000231', abrege: 'DUPONT', nom: 'SCP DUPONT-MARTIN', groupe: 'Avocats', statut: 'Bloque', type: 'En compte' },
  { id: 1204, compte: 'W-004412', abrege: 'WEBPRO', nom: 'Deposant en ligne', groupe: '', statut: 'Actif', type: 'Web' },
  { id: 1250, compte: 'G-000012', abrege: 'GRFCPY', nom: 'Greffe de Compiegne', groupe: 'Greffes', statut: 'Actif', type: 'Greffe' },
  { id: 1305, compte: 'P-000031', abrege: 'PREV51', nom: 'Cellule prevention 51', groupe: 'Prevention', statut: 'En sommeil', type: 'Prevention' },
]

export interface ProprietesEcranClients {
  /** Remonte l'abrege du client choisi (v2 : bouton « Selectionner »). */
  selectionner: (abrege: string) => void
}

function basculer(ensemble: ReadonlySet<string>, valeur: string): Set<string> {
  const s = new Set(ensemble)
  if (!s.delete(valeur)) s.add(valeur)
  return s
}

/** Les criteres de gauche, tels que saisis. Une vue en est un jeu complet. */
export interface CriteresClients {
  motCle: string
  statuts: ReadonlySet<string>
  types: ReadonlySet<string>
}

const SansCritere: CriteresClients = { motCle: '', statuts: new Set(), types: new Set() }

/** Les clients qui repondent aux criteres (le filtre de l'ecran, inchange). */
function filtrer({ motCle, statuts, types }: CriteresClients): ClientRcs[] {
  let trouves = Clients
  if (!estBlanc(motCle)) {
    const t = motCle.trim()
    trouves = trouves.filter((c) => contientSansCasse(c.nom, t) || contientSansCasse(c.abrege, t) || contientSansCasse(c.compte, t))
  }
  if (statuts.size > 0) trouves = trouves.filter((c) => statuts.has(c.statut))
  if (types.size > 0) trouves = trouves.filter((c) => types.has(c.type))
  return trouves
}

const memeEnsemble = (a: ReadonlySet<string>, b: ReadonlySet<string>) => a.size === b.size && [...a].every((v) => b.has(v))
const memesCriteres = (a: CriteresClients, b: CriteresClients) =>
  a.motCle === b.motCle && memeEnsemble(a.statuts, b.statuts) && memeEnsemble(a.types, b.types)

/** Les vues toujours la : les criteres de statut et de type de l'ecran. */
export const VuesPrereglees: VueEnregistree<CriteresClients>[] = [
  { cle: 'tous', libelle: 'Tous', criteres: SansCritere },
  { cle: 'actifs', libelle: 'Actifs', criteres: { ...SansCritere, statuts: new Set(['Actif']) } },
  { cle: 'bloques-sommeil', libelle: 'Bloques ou en sommeil', criteres: { ...SansCritere, statuts: new Set(['Bloque', 'En sommeil']) } },
  { cle: 'en-compte', libelle: 'En compte', criteres: { ...SansCritere, types: new Set(['En compte']) } },
]

export function EcranClients({ selectionner }: ProprietesEcranClients) {
  const [motCle, setMotCle] = useState('')
  const [critStatuts, setCritStatuts] = useState<ReadonlySet<string>>(new Set())
  const [critTypes, setCritTypes] = useState<ReadonlySet<string>>(new Set())
  const criteres: CriteresClients = { motCle, statuts: critStatuts, types: critTypes }

  function poserCriteres(c: CriteresClients) {
    setMotCle(c.motCle)
    setCritStatuts(new Set(c.statuts))
    setCritTypes(new Set(c.types))
  }

  function effacer() {
    poserCriteres(SansCritere)
  }

  const trouves = filtrer(criteres)

  const { vues, active: vueActive, enregistrer: enregistrerVue } = useVuesDeSession('Clients', VuesPrereglees, criteres, memesCriteres)

  const actifs = trouves.filter((c) => c.statut === 'Actif').length
  const parType = (t: string) => trouves.filter((c) => c.type === t).length

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Statut du client</div>
          {Statuts.map((v) => (
            <button key={v} className={`rig-bascule${critStatuts.has(v) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setCritStatuts((avant) => basculer(avant, v))}>
              <span className="rig-bascule__temoin"></span>
              <span>{v}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Type de client</div>
          {Types.map((v) => (
            <button key={v} className={`rig-bascule${critTypes.has(v) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setCritTypes((avant) => basculer(avant, v))}>
              <span className="rig-bascule__temoin"></span>
              <span>{v}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <ListeModerne etiquette="Resultat">
        <EnteteListe titre="Resultat"
                     synthese={<>{trouves.length}{trouves.length > 1 ? ' clients' : ' client'}{` sur ${Clients.length} · `}<b>{actifs}</b>
                       {actifs > 1 ? ' actifs' : ' actif'}</>}
                     filtre={{ valeur: motCle, changer: setMotCle, indication: 'Aller a un client…',
                               valider: () => { if (trouves.length === 1) selectionner(trouves[0]!.abrege) } }} />

        <CartesChiffrees cartes={[
          { cle: 'actifs', titre: 'Actifs', valeur: actifs, complement: `sur ${trouves.length}`,
            anneau: { part: actifs, total: trouves.length, teinte: 'vert' },
            actif: critStatuts.has('Actif'), choisir: () => setCritStatuts((avant) => basculer(avant, 'Actif')),
            indication: critStatuts.has('Actif') ? 'Retirer le statut Actif des criteres' : 'Ajouter le statut Actif aux criteres' },
          { cle: 'types', titre: 'Type de client', valeur: trouves.length, complement: trouves.length > 1 ? 'clients' : 'client',
            legende: true,
            ...(critTypes.size > 0 ? { choisir: () => setCritTypes(new Set()), indication: 'Tous les types' } : {}),
            segments: Types.map((t) => ({
              cle: t, libelle: t, valeur: parType(t), texte: String(parType(t)), teinte: TeinteType[t] ?? 'gris',
              actif: critTypes.has(t), choisir: () => setCritTypes((avant) => basculer(avant, t)),
            })) },
        ]} />

        <VuesListe vues={vues.map((v) => ({ cle: v.cle, libelle: v.libelle, compte: filtrer(v.criteres).length }))} active={vueActive}
                   choisir={(cle) => { const v = vues.find((x) => x.cle === cle); if (v) poserCriteres(v.criteres) }}
                   enregistrer={enregistrerVue} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col /><col style={{ width: '12%' }} /><col style={{ width: '18%' }} /><col style={{ width: '15%' }} /><col style={{ width: '17%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Client</th>
                <th>Id Client</th>
                <th>Groupe</th>
                <th>Statut</th>
                <th>Type</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((cli) => (
                <tr key={cli.id} onClick={() => selectionner(cli.abrege)}>
                  <CelluleIdentite nom={cli.nom} objet={`${cli.abrege} · ${cli.compte}`} titre={`${cli.nom} — ${cli.abrege} · ${cli.compte}`} />
                  <td className="rig-table__mono">{cli.id}</td>
                  <td title={cli.groupe}>{cli.groupe}</td>
                  <td><PointEtat teinte={TeinteStatut[cli.statut] ?? 'gris'}>{cli.statut}</PointEtat></td>
                  <td title={cli.type}><Pastille teinte={TeinteType[cli.type] ?? 'gris'}>{cli.type}</Pastille></td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr><td colSpan={5} className="rig-table__vide">Aucun client ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe aide="Clic sur une ligne pour choisir le client.">
          <span><b>{trouves.length}</b>{trouves.length > 1 ? ' clients affiches' : ' client affiche'}</span>
          <span>Actifs <b>{actifs}</b></span>
          <span>Fichier <b>{Clients.length}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
