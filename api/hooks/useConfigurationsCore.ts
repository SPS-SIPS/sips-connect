import useAxiosPrivate from "./useAxiosPrivate";


export type ConfigurationsCore = {
    baseUrl: string;
    publicKeysRepUrl: string;
    loginEndpoint: string;
    username: string;
    password: string;
    bic: string;
    safExpression: string;
    safPage: number;
    safTimeZoneInfo: string;
    safMaxRetries: number;
};

const BASE_URL = '/api/v1/Configurations/Core';

const useConfigurationsCore = () => {
    const axiosPrivate = useAxiosPrivate();

    const getConfigurations = async (): Promise<ConfigurationsCore> => {
        try {
            const response = await axiosPrivate.get<ConfigurationsCore>(BASE_URL);
            return response.data;
        } catch (error) {
            throw error;
        }
    };

    const updateConfigurations = async (
        data: ConfigurationsCore
    ): Promise<string> => {
        try {
            const response = await axiosPrivate.put<string>(BASE_URL, data);
            return response.data;
        } catch (error) {
            throw error;
        }
    };

    return {
        getConfigurations,
        updateConfigurations,
    };
};

export default useConfigurationsCore;
