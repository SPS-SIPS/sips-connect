import React, { createContext, useContext, useEffect, useState } from "react";
import Keycloak from "keycloak-js";

type AuthContextType = {
    isAuthenticated: boolean;
    isLoading: boolean;
    keycloak: Keycloak | null;
    login: () => void;
    logout: () => void;
};

const AuthContext = createContext<AuthContextType>({} as AuthContextType);

export const KeycloakAuthProvider = ({ children }: { children: React.ReactNode }) => {
    const [isAuthenticated, setIsAuthenticated] = useState(false);
    const [isLoading, setIsLoading] = useState(true);
    const [keycloak, setKeycloak] = useState<Keycloak | null>(null);

    useEffect(() => {
        const initKeycloak = async () => {
            const keycloakInstance = new Keycloak({
                url: process.env.NEXT_PUBLIC_KEYCLOAK_URL!,
                realm: process.env.NEXT_PUBLIC_KEYCLOAK_REALM!,
                clientId: process.env.NEXT_PUBLIC_KEYCLOAK_CLIENT_ID!,
            });

            try {
                const authenticated = await keycloakInstance.init({
                    onLoad: "check-sso",
                    silentCheckSsoRedirectUri: window.location.origin + "/silent-check-sso.html",
                });

                setIsAuthenticated(authenticated);
                setKeycloak(keycloakInstance);

                if (!authenticated) {
                    keycloakInstance.login();
                }
            } catch (error) {
                console.error("Keycloak initialization failed:", error);
            } finally {
                setIsLoading(false);
            }
        };

        initKeycloak();
    }, []);

    const login = () => keycloak?.login();
    const logout = () => keycloak?.logout();

    return (
        <AuthContext.Provider value={{ isAuthenticated, isLoading, keycloak, login, logout }}>
            {children}
        </AuthContext.Provider>
    );
};

export const useAuthentication = () => useContext(AuthContext);