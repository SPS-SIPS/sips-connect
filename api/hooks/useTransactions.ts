import { useState, useEffect } from 'react';
import useAxiosPrivate from "./useAxiosPrivate";
import { Transaction, TransactionQuery } from "../../types/types";

export const useTransactions = (initialQuery: TransactionQuery = { page: 0, pageSize: 10 }) => {
    const axiosPrivate = useAxiosPrivate();
    const [query, setQuery] = useState<TransactionQuery>(initialQuery);
    const [transactions, setTransactions] = useState<Transaction[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const fetchTransactions = async () => {
        setLoading(true);
        setError(null);

        try {
            const params = {
              page: query.page,
              pageSize: query.pageSize,
              status: query.Status?.toString(),
              TransactionId: query.TransactionId,
              EndToEndId: query.EndToEndId,
              LocalInstrument: query.LocalInstrument,
              CategoryPurpose: query.CategoryPurpose,
              DebtorAccount: query.DebtorAccount,
              CreditorAccount: query.CreditorAccount,
              fromDate: query.FromDate,
              toDate: query.ToDate,
            };

            if (query.ISOMessageId) {
                // @ts-ignore
                params.ISOMessageId = query.ISOMessageId.toString();
            }

            const response = await axiosPrivate.get<Transaction[]>('/api/v1/Transactions/transactions', {
                params,
                paramsSerializer: { indexes: null }
            });

            setTransactions(response.data);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to fetch transactions');
        } finally {
            setLoading(false);
        }
    };

    // Fetch when query changes
    useEffect(() => {
        fetchTransactions();
    }, [query]);

    const updateQuery = (newQuery: Partial<TransactionQuery>) => {
        setQuery(prev => ({ ...prev, ...newQuery }));
    };

    return {
        transactions,
        loading,
        error,
        query,
        setQuery: updateQuery,
        refetch: fetchTransactions,
    };
};
