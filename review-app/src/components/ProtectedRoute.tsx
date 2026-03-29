import { ReactNode } from "preact/compat";
import { Navigate } from "react-router-dom";

type ProtectedRouteProps = {
    children: ReactNode;
}

export function ProtectedRoute({ children }: ProtectedRouteProps) {
    const token = localStorage.getItem('jwt_token');

    if (!token)
        return <Navigate to="/login" replace />;

    return children;
}
