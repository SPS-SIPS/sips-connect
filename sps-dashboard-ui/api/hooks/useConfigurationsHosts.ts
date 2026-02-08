import useAxiosPrivate from "./useAxiosPrivate";

const BASE_URL = 'api/v1/Configurations/Hosts';

const useConfigurationsHosts = () => {
    const axiosPrivate = useAxiosPrivate();

    const getHosts = async (): Promise<string | string[] | null> => {
        try {
            const response = await axiosPrivate.get<string | string[] | null>(BASE_URL);
            return response.data;
        } catch (error) {
            throw error;
        }
    };

    const updateHosts = async (data: string[]): Promise<string> => {
        try {
            const response = await axiosPrivate.put<string>(BASE_URL, data);
            return response.data;
        } catch (error) {
            throw error;
        }
    };

    return {
        getHosts,
        updateHosts,
    };
};

export default useConfigurationsHosts;