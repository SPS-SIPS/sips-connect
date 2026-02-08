import { useState } from 'react';
import useAxiosPrivate from "./useAxiosPrivate";

interface FieldMapping {
    InternalField: string;
    UserField: string;
    Type?: string;
}

interface Endpoint {
    FieldMappings: FieldMapping[];
}

interface EndpointsConfig {
    Endpoints: {
        [key: string]: Endpoint;
    };
}

const useUpdateEndpoints = () => {
    const [loading, setLoading] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);
    const axiosPrivate = useAxiosPrivate();

    const updateEndpoints = async (updatedConfig: EndpointsConfig) => {
        setLoading(true);
        setError(null);
        setSuccessMessage(null);

        try {
            const response = await axiosPrivate.put<string>('/api/v1/adapter', updatedConfig);
            setSuccessMessage(response.data);
            setLoading(false);
            return response.data;
        } catch (err: unknown) {
            let errorMessage = 'Something went wrong';
            if (err instanceof Error) {
                errorMessage = err.message;
            } else if (typeof err === 'string') {
                errorMessage = err;
            }
            setError(errorMessage);
            setLoading(false);
        }
    };

    return { updateEndpoints, loading, error, successMessage };
};

export default useUpdateEndpoints;