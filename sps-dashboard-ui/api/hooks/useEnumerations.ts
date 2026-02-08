import useAxiosPrivate from "./useAxiosPrivate";
import { DataItem} from "../../types/types";

type ApiResponse = {
    data: DataItem[];
    isSuccess: boolean;
    message: string;
    statusCode: number;
};

const ENDPOINTS = {
    TRANSACTION_STATUSES: '/api/v1/Enumerations/TransactionStatuses',
    ISO_MESSAGE_TYPES: '/api/v1/Enumerations/ISOMessageTypes',
    TRANSACTION_TYPE: '/api/v1/Enumerations/TransactionType',
} as const;

type ResponseData = ApiResponse;

const useEnumerations = () => {
    const axiosPrivate = useAxiosPrivate();

    const fetchEnumeration = async (url: string): Promise<ResponseData> => {
        try {
            const response = await axiosPrivate.get<ResponseData>(url);
            return response.data;
        } catch (error) {
            console.error("API Error:", error);
            throw error;
        }
    };

    const createFetcher = (endpoint: string) => () => fetchEnumeration(endpoint);

    return {
        getTransactionStatuses: createFetcher(ENDPOINTS.TRANSACTION_STATUSES),
        getISOMessageTypes: createFetcher(ENDPOINTS.ISO_MESSAGE_TYPES),
        getTransactionType: createFetcher(ENDPOINTS.TRANSACTION_TYPE),
    };
};

export default useEnumerations;