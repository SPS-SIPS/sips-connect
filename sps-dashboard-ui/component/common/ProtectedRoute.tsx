import { useRouter } from "next/router";
import React, { useEffect } from "react";
import { useAuthentication } from "../../auth/AuthProvider";
import BubbleLoading from "../Loading/BubbleLoading/BubbleLoading";

const ProtectedRoute = ({ children }: { children: React.ReactNode }) => {
    const { isAuthenticated, isLoading, login } = useAuthentication();
    const router = useRouter();

    useEffect(() => {
        if (!isLoading && !isAuthenticated) {
            login();
        }
    }, [isAuthenticated, isLoading, login, router]);

    if (isLoading || !isAuthenticated) {
        return <BubbleLoading />;
    }

    return <>{children}</>;
};

export default ProtectedRoute;