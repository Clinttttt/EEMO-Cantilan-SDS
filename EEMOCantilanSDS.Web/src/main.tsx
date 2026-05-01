import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/global.css'
import './styles/Vendors.css'
import './styles/Profile.css'
import './styles/AddVendorModal.css'
import './styles/PaymentHistoryModal.css'
import './styles/FacilityPaymentModal.css'
import './styles/FacilityStallsTable.css'
import './styles/VendorDetailPanel.css'
import './app.css'
import App from './App.tsx'



createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
