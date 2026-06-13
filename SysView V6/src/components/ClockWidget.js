// =============================================================
// ClockWidget.js — SysView V6
// Formatage de l'heure et de la date.
// La date et l'heure partagent le même tick — un seul setInterval.
// Exports : formatClock, formatDate, startClock
// =============================================================

function pad(n) { return String(n).padStart(2, '0'); }

// ─── Formate l'heure ─────────────────────────────────────────
export function formatClock(date, timeFormat) {
  var h = date.getHours(), m = date.getMinutes(), s = date.getSeconds();
  if (timeFormat === '12h') {
    var ap = h >= 12 ? 'PM' : 'AM';
    h = h % 12 || 12;
    return pad(h) + ':' + pad(m) + ':' + pad(s) + ' ' + ap;
  }
  return pad(h) + ':' + pad(m) + ':' + pad(s);
}

// ─── Formate la date complète ─────────────────────────────────
// days   : tableau de 7 chaînes (LANG[lang].days)
// months : tableau de 12 chaînes (LANG[lang].months)
export function formatDate(date, days, months) {
  return days[date.getDay()] + ' · ' + date.getDate() + ' ' +
         months[date.getMonth()] + ' ' + date.getFullYear();
}

// ─── Lance l'horloge (appelle onTick immédiatement puis chaque seconde)
// onTick({ clock, dateStr }) → utilisé par Alpine pour mettre à jour les props
// Retourne le setTimeout ID pour pouvoir annuler si besoin.
// Auto-correction : chaque tick se recale sur le début de la prochaine seconde,
// évitant la dérive cumulative d'un setInterval fixe.
export function startClock(getTimeFormat, getDays, getMonths, onTick) {
  function tick() {
    var n = new Date();
    onTick({
      clock:   formatClock(n, getTimeFormat()),
      dateStr: formatDate(n, getDays(), getMonths()),
    });
    return setTimeout(tick, 1000 - n.getMilliseconds());
  }
  return tick();
}
