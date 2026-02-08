import { useRouter } from "next/router";
import React, { useEffect } from "react";
import { useAuthentication } from "../../auth/AuthProvider";
import BubbleLoading from "../Loading/BubbleLoading/BubbleLoading";

const ProtectedAuthRoute = ({ children }: { children: React.ReactNode }) => {
    const { isAuthenticated, isLoading } = useAuthentication();
    const router = useRouter();

    useEffect(() => {
        if (!isLoading && isAuthenticated) {
            router.replace("/");
        }
    }, [isAuthenticated, isLoading, router]);

    if (isLoading || isAuthenticated) {
        return <BubbleLoading />;
    }

    return <>{children}</>;
};

export default ProtectedAuthRoute;