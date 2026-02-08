import useAxiosPrivate from "./useAxiosPrivate";

export type ConfigurationsOrigins = {
    origins: string[];
};

const BASE_URL = 'api/v1/Configurations/Origins';

const useConfigurationsOrigins = () => {
    const axiosPrivate = useAxiosPrivate();

    const getConfigurations = async (): Promise<ConfigurationsOrigins> => {
        try {
            const response = await axiosPrivate.get<ConfigurationsOrigins>(BASE_URL);
            return response.data;
        } catch (error) {
            throw error;
        }
    };

    const updateConfigurations = async (
        data: ConfigurationsOrigins
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

export default useConfigurationsOrigins;