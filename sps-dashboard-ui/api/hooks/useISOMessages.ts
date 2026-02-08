import { useState, useEffect } from 'react';
import useAxiosPrivate from "./useAxiosPrivate";
import {ISOMessage, MessageQuery} from "../../types/types";

export const useISOMessages = (initialQuery: MessageQuery = { page: 0, pageSize: 10 }) => {
    const axiosPrivate = useAxiosPrivate();
    const [query, setQuery] = useState<MessageQuery>(initialQuery);
    const [messages, setMessages] = useState<ISOMessage[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const fetchMessages = async () => {
        setLoading(true);
        setError(null);

        try {
            const params = {
                ...query,
                status: query.status?.toString(),
                type: query.type?.toString(),
            };

            const response = await axiosPrivate.get<ISOMessage[]>('/api/v1/Transactions/iso-messages', {
                params,
                paramsSerializer: { indexes: null }
            });

            setMessages(response.data);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to fetch messages');
        } finally {
            setLoading(false);
        }
    };

    // Fetch when query changes
    useEffect(() => {
        fetchMessages();
    }, [query]);

    const updateQuery = (newQuery: Partial<MessageQuery>) => {
        setQuery(prev => ({ ...prev, ...newQuery }));
    };

    return {
        messages,
        loading,
        error,
        query,
        setQuery: updateQuery,
        refetch: fetchMessages,
    };
};