// Sound and vibration for an accepted scan. The beep is made with Web Audio
// (no audio file); both are skipped quietly where the browser has no support.

let context = null;

/** Call from a click handler or when scanning starts, so the browser lets the beep play. */
export function prepareFeedback() {
  try {
    context ??= new AudioContext();
    context.resume?.();
  } catch {
    // No Web Audio: the scan is silent.
  }
}

/** A short beep and a brief vibration. */
export function scanFeedback() {
  try {
    if (context) {
      const oscillator = context.createOscillator();
      const gain = context.createGain();
      oscillator.type = "sine";
      oscillator.frequency.value = 1200;
      gain.gain.value = 0.15;
      oscillator.connect(gain).connect(context.destination);
      oscillator.start();
      oscillator.stop(context.currentTime + 0.12);
    }
    navigator.vibrate?.(60);
  } catch {
    // Feedback is a courtesy; never let it break a scan.
  }
}
