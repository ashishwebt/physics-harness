/** @type {import('tailwindcss').Config} */
module.exports = {
  darkMode: 'class',
  content: ['./index.html', './src/**/*.{js,jsx,ts,tsx}'],
  theme: {
    extend: {
      colors: {
        paper: 'var(--paper)',
        surface: 'var(--surface)',
        ink: 'var(--ink)',
        'ink-soft': 'var(--ink-soft)',
        line: 'var(--line)',
        accent: 'var(--accent)',
        'accent-ink': 'var(--accent-ink)',
        slate: 'var(--slate)',
        'slate-ink': 'var(--slate-ink)',
        danger: 'var(--danger)',
      },
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        wordmark: ['Fraunces', 'serif'],
      },
      boxShadow: {
        panel: '0 1px 2px rgba(30, 33, 40, 0.04)',
      },
    },
  },
  plugins: [require('@tailwindcss/typography')],
};
