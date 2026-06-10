// =============================================================
// WallpaperAPI.js — SysView V6
// Wrapper des événements Wallpaper Engine.
// Isole toutes les références à l'API globale WE pour que les
// autres modules restent testables hors de l'environnement WE.
// =============================================================

export class WallpaperAPI {
  static registerAudio(callback) {
    if (typeof window.wallpaperRegisterAudioListener === 'function') {
      window.wallpaperRegisterAudioListener(callback);
    }
  }

  static isWallpaperEngine() {
    return typeof window.wallpaperRegisterAudioListener === 'function';
  }
}
