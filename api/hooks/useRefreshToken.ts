import {apiAuth} from "../../constants/constants";
const useRefreshToken = () => {
    // For now, return the hardcoded mock token
    const refresh = async () => {
        return apiAuth;
    };

    return { refresh };
};

export default useRefreshToken;