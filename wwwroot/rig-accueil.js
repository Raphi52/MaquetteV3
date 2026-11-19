// ============================================================
// RIG V3 - les gestes du plan de travail que seul le navigateur sait faire.
//
// La pose des tuiles de l'accueil (glisser-deposer, redimensionnement), le
// signalement d'une tuile et le theme pose sur <html> sont passes en React
// (lot 1 du plan « finir le passage a React », 19/11/2026) :
// web/src/accueil/Grille.tsx, web/src/accueil/pose.ts, web/src/composants/ThemeRig.tsx.
// Restent ici la pagination de la saisie guidee, le clavier et la barre
// d'onglets, du ressort du plan de travail (lot 2).
// ============================================================

// ============================================================
// SAISIE GUIDEE - les etapes a l'horizontale, une page par etape.
// Chacune se cale comme une page (scroll-snap, cote CSS). Le
// navigateur est le seul a savoir laquelle est reellement visible :
// c'est lui qui le dit a Blazor, pour que la frise suive.
// ============================================================

let detacherPagination = null;

export function suivrePagination(zone, dotnet) {
  arreterPagination();
  if (!zone) return;

  // Les etapes sont COTE A COTE : chaque page fait la largeur de la zone et
  // se cale d'elle-meme (scroll-snap). L'etape visible se deduit donc de la
  // position horizontale, arrondie a la page la plus proche.
  let dernier = -1;
  let enAttente = false;
  const indexVisible = () =>
    Math.round(zone.scrollLeft / Math.max(1, zone.clientWidth));

  const mesurer = () => {
    enAttente = false;
    const index = indexVisible();
    if (index === dernier) return;
    dernier = index;
    dotnet.invokeMethodAsync("EtapeVisible", index);
  };
  const auDefilement = () => {
    if (enAttente) return;
    enAttente = true;
    requestAnimationFrame(mesurer);
  };

  // La molette verticale : elle fait d'abord defiler l'etape elle-meme
  // (formulaire plus haut que la vue), et seulement arrivee au bout elle
  // TOURNE la page vers l'etape suivante ou precedente. Un geste = une page :
  // un verrou court absorbe l'inertie des pave tactiles.
  let verrouJusqu = 0;
  const aLaMolette = (ev) => {
    // Ctrl+molette = ZOOM de la fenetre, prioritaire : on ne touche a rien,
    // sinon le preventDefault plus bas annule le zoom natif (defaut du 15/09).
    if (ev.ctrlKey) return;
    if (Math.abs(ev.deltaX) > Math.abs(ev.deltaY)) return; // geste deja horizontal
    const page = ev.target.closest && ev.target.closest("[data-etape]");
    const sens = ev.deltaY > 0 ? 1 : -1;
    if (page && page.scrollHeight > page.clientHeight + 1) {
      const auBout = sens > 0
        ? page.scrollTop + page.clientHeight >= page.scrollHeight - 1
        : page.scrollTop <= 0;
      if (!auBout) return;
    }
    ev.preventDefault();
    const maintenant = performance.now();
    if (maintenant < verrouJusqu) return;
    verrouJusqu = maintenant + 450;
    const total = zone.querySelectorAll("[data-etape]").length;
    const cible = Math.min(total - 1, Math.max(0, indexVisible() + sens));
    allerEtape(zone, cible, true);
  };

  zone.addEventListener("scroll", auDefilement, { passive: true });
  zone.addEventListener("wheel", aLaMolette, { passive: false });
  detacherPagination = () => {
    zone.removeEventListener("scroll", auDefilement);
    zone.removeEventListener("wheel", aLaMolette);
  };
  mesurer();
}

export function arreterPagination() {
  if (detacherPagination) { detacherPagination(); detacherPagination = null; }
}

export function allerEtape(zone, index, doux = true) {
  if (!zone) return;
  zone.scrollTo({ left: index * zone.clientWidth, behavior: doux ? "smooth" : "auto" });
}


// ============ CLAVIER ============
// RIG v2 se conduit au clavier. Premier pas ici : Echap ferme ce qui est
// ouvert. L'ecoute est posee sur le DOCUMENT (et non sur un element focusable)
// parce que le focus peut etre n'importe ou -- dans une grille, un champ, ou
// nulle part apres un clic sur le fond.
let detacherClavier = null;

export function activerClavier(dotnet) {
  arreterClavier();
  const surTouche = (e) => {
    if (e.key !== "Escape") return;
    // Un champ en cours d'edition garde Echap pour lui (annuler la saisie)
    // seulement s'il est dans une liste deroulante ouverte ; sinon, fermer
    // l'ecran est le geste attendu.
    dotnet.invokeMethodAsync("ToucheEchap");
  };
  document.addEventListener("keydown", surTouche);
  detacherClavier = () => document.removeEventListener("keydown", surTouche);
}

export function arreterClavier() {
  if (detacherClavier) { detacherClavier(); detacherClavier = null; }
}


// ============ BARRE D ONGLETS ============
// Le glisser d un onglet doit continuer a etre suivi QUAND LE POINTEUR SORT
// de la fenetre : c est justement le geste qui sort l onglet sur un autre
// ecran. Sans capture du pointeur, le navigateur cesse d envoyer les
// mouvements des qu on quitte l element, et l arrachage serait impossible.
document.addEventListener("pointerdown", (e) => {
  const onglet = e.target instanceof Element ? e.target.closest("[data-rig-onglet]") : null;
  if (!onglet) return;
  // La croix n est PAS un debut de glisser : capturer le pointeur sur
  // l onglet redirigerait le clic vers l onglet, et la croix ne fermerait rien.
  if (e.target.closest(".rig-vonglet__fermer")) return;
  try { onglet.setPointerCapture(e.pointerId); } catch { /* pointeur deja relache */ }
  const r = onglet.getBoundingClientRect();
  glisseVisuel = { code: onglet.dataset.rigOnglet, prise: e.clientX - r.left, departX: e.clientX, departY: e.clientY, actif: false };
}, true);

// RETOUR VISUEL du glisser, comme un navigateur : l onglet SUIT le pointeur.
// Fait ici, en JS, a chaque mouvement : un aller-retour Blazor par pixel
// donnerait un onglet saccade. Le calcul repart de la place NATURELLE de
// l onglet a chaque mouvement, donc il reste juste meme quand Blazor le
// deplace dans la barre (reordonnancement).
let glisseVisuel = null;
function ongletGlisse() {
  return glisseVisuel
    ? document.querySelector(`[data-rig-onglet="${CSS.escape(glisseVisuel.code)}"]`)
    : null;
}
document.addEventListener("pointermove", (e) => {
  if (!glisseVisuel) return;
  if (!glisseVisuel.actif
      && Math.abs(e.clientX - glisseVisuel.departX) + Math.abs(e.clientY - glisseVisuel.departY) < 6) return;
  glisseVisuel.actif = true;
  const onglet = ongletGlisse();
  if (!onglet) return;
  onglet.style.transform = "";
  const naturel = onglet.getBoundingClientRect().left;
  onglet.style.transform = `translateX(${e.clientX - glisseVisuel.prise - naturel}px)`;
  onglet.classList.add("rig-vonglet--suit");
}, true);
function finVisuel() {
  const onglet = ongletGlisse();
  if (onglet) { onglet.style.transform = ""; onglet.classList.remove("rig-vonglet--suit"); }
  glisseVisuel = null;
}
document.addEventListener("pointerup", finVisuel, true);
document.addEventListener("pointercancel", finVisuel, true);
/** Appele par le C# quand l onglet vient de partir dans sa fenetre. */
export function finirGlisseOnglet() { finVisuel(); }

// Mesure, au debut du geste : la taille de la zone visible (pour savoir quand
// le pointeur en sort) et la place de chaque onglet (pour savoir ou l inserer).
// Le calcul, lui, reste en C# : c est la qu il est verifiable.
export function mesurerOnglets() {
  const cases = [];
  document.querySelectorAll("[data-rig-onglet]").forEach((el) => {
    const r = el.getBoundingClientRect();
    cases.push([r.left, r.right]);
  });
  return {
    // 3e valeur : le bas de la barre d onglets -- au-dela, l onglet s arrache.
    page: [document.documentElement.clientWidth, document.documentElement.clientHeight,
           document.getElementById("rig-vonglets")?.getBoundingClientRect().bottom ?? 1e9],
    cases: cases,
  };
}
