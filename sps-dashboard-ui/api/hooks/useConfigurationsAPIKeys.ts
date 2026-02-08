import useAxiosPrivate from "./useAxiosPrivate";

export type ApiKeyConfiguration = {
    name: string;
    key: string;
    secret: string;
};

const BASE_URL = '/api/v1/Configurations/APIKeys';

const useConfigurationsAPIKeys = () => {
    const axiosPrivate = useAxiosPrivate();

    const getApiKeys = async (): Promise<ApiKeyConfiguration[]> => {
        try {
            const response = await axiosPrivate.get<ApiKeyConfiguration[]>(BASE_URL);
            return response.data;
        } catch (error) {
            throw error;
        }
    };

    const updateApiKey = async (
        data: ApiKeyConfiguration
    ): Promise<string> => {
        try {
            const response = await axiosPrivate.put<string>(BASE_URL, data);
            return response.data;
        } catch (error) {
            throw error;
        }
    };

    const deleteApiKey = async (key: string): Promise<string> => {
        try {
            const response = await axiosPrivate.delete<string>(BASE_URL, {
                params: { key },
            });
            return response.data;
        } catch (error) {
            throw error;
        }
    };


    return {
        getApiKeys,
        updateApiKey,
        deleteApiKey,
    };
};

export default useConfigurationsAPIKeys;