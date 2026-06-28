import { AuthGuard } from "./components/AuthGuard";
import { Home } from "./pages/Home";

export function App() {
  return (
    <AuthGuard>
      <Home />
    </AuthGuard>
  );
}
