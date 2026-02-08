import { useEffect } from "react";
import { useAuthentication } from "../../auth/AuthProvider";
import { axiosPrivate } from "../axios";

const useAxiosPrivate = () => {
    const { keycloak } = useAuthentication();

    useEffect(() => {
        if (!keycloak) return;

        const requestIntercept = axiosPrivate.interceptors.request.use(
            async (config) => {
                if (!keycloak.authenticated || !keycloak.token) {
                    return config;
                }

                try {
                    // Refresh token if it's expired or about to expire (within 30 seconds)
                    await keycloak.updateToken(30);
                    config.headers.Authorization = `Bearer ${keycloak.token}`;
                } catch (error) {
                    console.error("Failed to refresh token:", error);
                    await keycloak.logout();
                    throw error;
                }

                return config;
            },
            (error) => Promise.reject(error)
        );

        const responseIntercept = axiosPrivate.interceptors.response.use(
            (response) => response,
            async (error) => {
                const prevRequest = error?.config;

                if (error?.response?.status === 401 && !prevRequest?._retry) {
                    prevRequest._retry = true;

                    try {
                        await keycloak.updateToken();
                        prevRequest.headers.Authorization = `Bearer ${keycloak.token}`;
                        return axiosPrivate(prevRequest);
                    } catch (refreshError) {
                        console.error("Token refresh failed:", refreshError);
                        await keycloak.logout();
                        return Promise.reject(refreshError);
                    }
                }

                return Promise.reject(error);
            }
        );

        return () => {
            axiosPrivate.interceptors.request.eject(requestIntercept);
            axiosPrivate.interceptors.response.eject(responseIntercept);
        };
    }, [keycloak]);

    return axiosPrivate;
};

export default useAxiosPrivate;