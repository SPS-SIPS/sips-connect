import { AxiosError } from "axios";
import useAxiosPrivate from "../api/hooks/useAxiosPrivate";
import {useState} from "react";

const handleErrorMessage = (error: unknown, defaultMessage: string): string => {
    if (typeof error === 'string') return error;
    if (error instanceof AxiosError) {
        return error.response?.data?.message || error.message || defaultMessage;
    }
    if (error instanceof Error) return error.message;
    return defaultMessage;
};

export const useApiRequest = () => {
    const axiosPrivate = useAxiosPrivate();
    const [loading, setLoading] = useState<boolean>(false);

    const makeApiRequest = async (config: {
        url: string;
        method: 'get' | 'post' | 'put' | 'patch' | 'delete';
        data?: unknown;
    }): Promise<{
        data: unknown;
        status: number;
        error?: string;
        success: boolean;
    }> => {
        setLoading(true);
        try {
            const response = await axiosPrivate({
                url: config.url,
                method: config.method,
                data: config.data
            });
            setLoading(false);
            return {
                data: response.data,
                status: response.status,
                success: true
            };
        } catch (error) {
            const errorMessage = handleErrorMessage(
                error,
                'An unexpected error occurred'
            );
            setLoading(false);
            return {
                data: null,
                status: (error as AxiosError)?.response?.status || 500,
                error: errorMessage,
                success: false
            };
        }
    };

    return { makeApiRequest, loading };
};