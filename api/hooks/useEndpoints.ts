import { useState, useEffect } from 'react';
import useAxiosPrivate from "./useAxiosPrivate";

type FieldMapping = {
    internalField: string;
    userField: string;
    type: string;
};

type Endpoint = {
    fieldMappings: FieldMapping[];
};

type EndpointsData = {
    [key: string]: Endpoint;
};

const useEndpoints = () => {
    const [endpoints, setEndpoints] = useState<EndpointsData | null>(null);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const axiosPrivate = useAxiosPrivate();

    useEffect(() => {
        const fetchEndpoints = async () => {
            try {
                const response = await axiosPrivate.get<EndpointsData>('/api/v1/Adapter');
                // const response = await axiosPrivate.get<EndpointsData>('/api/v1/Configurations/Core');
                console.log(response);
                setEndpoints(response.data);
                setLoading(false);
            } catch (err: unknown) {
                let errorMessage = 'Something went wrong';
                if (err instanceof Error) {
                    errorMessage = err.message;
                }
                setError(errorMessage);
                setLoading(false);
            }
        };

        fetchEndpoints();
    }, []);

    return { endpoints, loading, error };
};

export default useEndpoints;