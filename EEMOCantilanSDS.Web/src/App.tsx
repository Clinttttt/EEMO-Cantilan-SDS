import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { QueryClient, QueryClientProvider} from "@tanstack/react-query";
import { AuthProvider } from "./context/AuthContext";
import { Menu  } from "./pages/Menu";
import {Collectors} from "./pages/Collectors";
import { Login } from "./pages/Login";
import { AccountSetup } from "./pages/AccountSetup";
import { Vendors } from "./pages/Vendors";
import { Profile } from "./pages/Profile";


const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus :false,
      retry :1,
    },
  }
});

function App() {

  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/dashboard" element={<Menu />} />
            <Route path="/" element={<Navigate to="/dashboard" replace />} />
            <Route path="/collectors" element={<Collectors />} />
            <Route path="/login" element={<Login />} />
            <Route path="/account-setup" element={<AccountSetup />} />
            <Route path="/vendors" element={<Vendors />} />
            <Route path="/profile/:facilityId/:stallNo" element={<Profile />} />
    



          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;