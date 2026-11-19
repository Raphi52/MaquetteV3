// Une VUE D'ACCUEIL d'exemple pour les tests (forme de Pont/Accueil.cs, VueAccueil) :
// petite, mais chaque bloc y est present, pour que chaque test n'ecrive que ce qu'il change.

import type { AlerteLigneVue, TuileVue, VueBarre, VuePlanAccueil } from '../accueil/types'

export function ligne(id: string, nombre: number, titre: string): AlerteLigneVue {
  return { id, nombre, palier: nombre > 10 ? 'fort' : 'faible', titre, detail: 'detail ' + id, libelleCible: 'Ecran ' + id }
}

export function tuile(id: string, partiel: Partial<TuileVue> = {}): TuileVue {
  return {
    id, titre: 'Titre ' + id, maitresse: false, devant: false,
    colonne: 1, ligne: 1, largeur: 3, hauteur: 2, rang: 1,
    avecTitre: !id.startsWith('metier-'), reglagesOuverts: false, reglages: null,
    performance: null, alertes: null, alertesBis: null, taches: null, nouveautes: null,
    actualites: null, activite: null, metier: null,
    ...partiel,
  }
}

export function vueBarre(partiel: Partial<VueBarre> = {}): VueBarre {
  return {
    version: '3.0.14', reglages: false, theme: 'clair',
    themes: [{ code: 'clair', libelle: 'Clair' }, { code: 'sombre', libelle: 'Sombre' }],
    profil: null,
    ...partiel,
  }
}

export function vuePlan(partiel: Partial<VuePlanAccueil> = {}): VuePlanAccueil {
  return {
    menuReduit: false,
    menu: {
      favorisOuverts: true,
      favoris: [],
      moments: [{
        titre: 'Ouvrir', teinte: 'rcs', signe: '+', compte: 3, ouvert: true,
        sous: [{
          titre: 'Creer une affaire', ouvert: true, compte: 2,
          entrees: [
            { code: 'CODE_A', libelle: 'Nouvelle instance', detail: 'saisir une affaire', actif: false, favori: false },
            { code: 'CODE_B', libelle: 'Ouvrir un dossier', detail: 'une societe arrive', actif: true, favori: true },
          ],
        }, { titre: 'Recevoir', ouvert: false, compte: 1, entrees: [] }],
      }, { titre: 'Trouver', teinte: 'jud', signe: '?', compte: 7, ouvert: false, sous: [] }],
    },
    entete: {
      date: 'jeudi 19 novembre', utilisateur: 'Raphael',
      phrase: { horsDelaiConnu: true, horsDelai: 2, arrivees: 66, traites: '4 / 11' },
      panneauOuvert: false, panneau: null,
      profil: {
        dansLaBarre: false, ouvert: false, initiales: 'RV', titre: 'rvilain · Greffier (RCS) : changer de login',
        login: 'rvilain', comptes: [],
      },
    },
    recherche: { texte: '', traitements: [], affaires: [], elements: [] },
    chiffres: {
      deplie: null, arrivees: 66, detail1: '22 : DAS', titreDetail1: 'DAS', anneau1: 'var(--rig-anneau-piste)',
      cible2: 44, valeur2: '4 / 11', objectif2: 'obj. 9', alerte3: true, cible3: 30, valeur3: '2', objectif3: 'obj. 0',
    },
    proue: null,
    tuiles: [
      tuile('alertes', { colonne: 7, largeur: 6, hauteur: 3, rang: 2, alertes: {
        vide: false, afficherDetail: false,
        groupes: [{ categorie: 'Arrivees', repliee: false, charge: 66, lignes: [ligne('a1', 22, 'DAS a numeriser')] }],
      } }),
      tuile('performance', { colonne: 1, ligne: 4, largeur: 6, hauteur: 2, rang: 3, performance: [
        { libelle: 'Dossiers traites', valeur: '4 / 11', part: 36, statut: 'alerte', objectif: 82, objectifTexte: 'obj. 9', atteinte: 44 },
      ] }),
    ],
    signal: null,
    ...partiel,
  }
}
