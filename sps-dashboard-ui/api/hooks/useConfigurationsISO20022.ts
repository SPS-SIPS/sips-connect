import useAxiosPrivate from "./useAxiosPrivate";

export type ConfigurationsISO20022 = {
    verification: string;
    transfer: string;
    return: string;
    status: string;
    sips: string;
    bic: string;
    agent: string;
    key: string;
    secret: string;
};

const BASE_URL = 'api/v1/Configurations/ISO20022';

const useConfigurationsISO20022 = () => {
    const axiosPrivate = useAxiosPrivate();

    const getConfigurations = async (): Promise<ConfigurationsISO20022> => {
        try {
            const response = await axiosPrivate.get<ConfigurationsISO20022>(BASE_URL);
            return response.data;
        } catch (error) {
            throw error;
        }
    };

    const updateConfigurations = async (
        data: ConfigurationsISO20022
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

export default useConfigurationsISO20022;
