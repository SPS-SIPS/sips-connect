import React, { useEffect } from 'react';
import { useRouter } from 'next/router';
import SpinLoading from "../component/Loading/SpinLoading/SpinLoading";
import {useAuthentication} from "./AuthProvider";

interface RoleGuardProps {
    allowedRoles: string[];
    children: React.ReactNode;
}

const RoleGuard: React.FC<RoleGuardProps> = ({ allowedRoles, children }) => {
    const { isAuthenticated, isLoading, roles } = useAuthentication();
    const router = useRouter();

    useEffect(() => {
        if (!isLoading && isAuthenticated) {
            const hasRequiredRole = allowedRoles.some(role =>
                roles.includes(role)
            );
            if (!hasRequiredRole) {
                router.replace('/unauthorized');
            }
        }
    }, [isAuthenticated, isLoading, roles, allowedRoles, router]);

    if (isLoading) return <SpinLoading/>;
    if (!isAuthenticated) return null;

    const hasRequiredRole = allowedRoles.some(role =>
        roles.includes(role)
    );

    return hasRequiredRole ? <>{children}</> : null;
};

export default RoleGuard;