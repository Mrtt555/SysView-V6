// Design system Tailwind v3 — miroir de la config inline dans index.html.
// Les couleurs mappent les variables CSS root pour que les classes Tailwind
// (ex: text-primary, bg-secondary/20) restent cohérentes avec le thème WE.
export default {
  corePlugins: { preflight: false },
  theme: {
    extend: {
      colors: {
        primary:   'rgb(var(--p)  / <alpha-value>)',
        secondary: 'rgb(var(--s)  / <alpha-value>)',
        surface:   'rgb(var(--bg) / <alpha-value>)',
        ink:       'rgb(var(--tx) / <alpha-value>)',
      }
    }
  }
};
