import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.tsx'
import { AuthProvider } from './AuthContext.tsx'

// <details> menus/dropdowns don't close on outside click natively
document.addEventListener('click', (e) => {
  for (const el of document.querySelectorAll<HTMLDetailsElement>('details.menu[open], details.dropdown[open]')) {
    if (!el.contains(e.target as Node)) el.open = false
  }
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
