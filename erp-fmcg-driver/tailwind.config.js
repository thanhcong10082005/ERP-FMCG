/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        primary: { DEFAULT: '#1E40AF', 50: '#EFF6FF', 100: '#DBEAFE', 800: '#1E3A8A' },
        success: { DEFAULT: '#059669', 50: '#ECFDF5', 100: '#D1FAE5', 600: '#047857' },
        warning: { DEFAULT: '#D97706', 50: '#FFFBEB', 100: '#FEF3C7', 600: '#B45309' },
        danger: { DEFAULT: '#DC2626', 50: '#FEF2F2', 100: '#FEE2E2', 600: '#B91C1C' },
        surface: '#FFFFFF',
        background: '#F1F5F9',
        'text-primary': '#0F172A',
        'text-secondary': '#64748B',
        border: '#E2E8F0',
      },
      fontFamily: {
        sans: ['Inter', '-apple-system', 'BlinkMacSystemFont', 'sans-serif'],
      },
    },
  },
  plugins: [],
};
