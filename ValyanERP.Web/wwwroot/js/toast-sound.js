// Lightweight toast sound helper using WebAudio API
window.playToastSound = (type) => {
    try {
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        if (!AudioContext) return;
        const ctx = new AudioContext();

        // Frequencies per type (short pleasant beep)
        const freqMap = {
            success: 880,
            warning: 660,
            error: 440,
            info: 740
        };
        const freq = freqMap[type] || 740;

        const o = ctx.createOscillator();
        const g = ctx.createGain();
        o.type = 'sine';
        o.frequency.value = freq;

        o.connect(g);
        g.connect(ctx.destination);

        const now = ctx.currentTime;
        g.gain.setValueAtTime(0, now);
        g.gain.linearRampToValueAtTime(0.12, now + 0.01);
        o.start(now);
        g.gain.linearRampToValueAtTime(0, now + 0.18);
        o.stop(now + 0.2);

        // Close the audio context after the sound finishes to avoid leaking resources
        setTimeout(() => {
            try { ctx.close(); } catch(e) { /* ignore */ }
        }, 500);
    }
    catch (e) {
        // No-op - don't break app if audio is unsupported
        console.warn('playToastSound failed', e);
    }
};