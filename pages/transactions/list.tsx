import React, { useState } from 'react';
import { FiSettings, FiX, FiCheck } from 'react-icons/fi';
import { useTransactions } from '../../api/hooks/useTransactions';
import {
    Transaction,
    getTransactionStatusType, TransactionType, TransactionStatus
} from '../../types/types';
import styles from '../../styles/TransactionsList.module.css';
import SearchInput from "../../component/common/SearchInput/SearchInput";
import SelectInput from "../../component/common/SelectInput/SelectInput";
import ActionButton from "../../component/common/ActionButton/ActionButton";
import RoleGuard from "../../auth/RoleGuard";

// Define all possible columns
const allColumns = [
    { id: 'txId', label: 'Transaction ID' },
    { id: 'type', label: 'Transaction type' },
    { id: 'isoMessageId', label: 'ISO Message ID' },
    { id: 'fromBIC', label: 'Debtor Agent BIC' },
    { id: 'localInstrument', label: 'Local Instrument' },
    { id: 'categoryPurpose', label: 'Category Purpose' },
    { id: 'endToEndId', label: 'End To End ID' },
    { id: 'amount', label: 'Amount' },
    { id: 'currency', label: 'Currency' },
    { id: 'debtorName', label: 'Debtor Name' },
    { id: 'debtorAccount', label: 'Debtor Account' },
    { id: 'debtorAccountType', label: 'Debtor Account Type' },
    { id: 'debtorAgentBIC', label: 'Debtor Agent BIC' },
    { id: 'debtorIssuer', label: 'Debtor Issuer' },
    { id: 'creditorName', label: 'Creditor Name' },
    { id: 'creditorAccount', label: 'Creditor Account' },
    { id: 'creditorAccountType', label: 'Creditor Account Type' },
    { id: 'creditorAgentBIC', label: 'Creditor Agent BIC' },
    { id: 'creditorIssuer', label: 'Creditor Issuer' },
    { id: 'remittanceInformation', label: 'Remittance Information' },
];

const TransactionsList = () => {
    const {
        transactions,
        loading,
        error,
        query,
        setQuery,
        refetch
    } = useTransactions();

    // const [searchTerm, setSearchTerm] = useState('');
    const [showColumnSettings, setShowColumnSettings] = useState(false);
    const [visibleColumns, setVisibleColumns] = useState<string[]>([
        'txId', 'type', 'amount', 'currency', 'debtorAccount', 'creditorAccount'
    ]);

    const [filters, setFilters] = useState({
        ISOMessageId: '',
        TransactionId: '',
        EndToEndId: '',
        LocalInstrument: '',
        CategoryPurpose: '',
        DebtorAccount: '',
        CreditorAccount: '',
        Status:"",
        FromDate: '',
        ToDate: ''
    });

    const handleFilterChange = (field: keyof typeof filters, value: string) => {
        const newFilters = { ...filters, [field]: value };
        setFilters(newFilters);
        setQuery({
            ...newFilters,
            page: 0,
            ISOMessageId: newFilters.ISOMessageId ? Number(newFilters.ISOMessageId) : undefined,
            Status: newFilters.Status ? (newFilters.Status as unknown as TransactionStatus) : undefined
        });
    };

    const toggleColumnVisibility = (columnId: string) => {
        setVisibleColumns(prev =>
            prev.includes(columnId)
                ? prev.filter(id => id !== columnId)
                : [...prev, columnId]
        );
    };

    const getStatusClass = (status: TransactionType) => {
        switch(status) {
            case TransactionType.Deposit: return styles.transactionDeposit;
            case TransactionType.Withdrawal: return styles.Withdrawal;
            case TransactionType.ReadyForReturn: return styles.ReadyForReturn;
            case TransactionType.ReturnWithdrawal: return styles.transactionReturnWithdrawal;
            default: return '';
        }
    };

    const renderCellContent = (transaction: Transaction, columnId: string) => {
        switch(columnId) {
            case 'amount':
                return transaction.amount ? `${transaction.amount} ${transaction.currency || ''}` : '-';
            case 'type':
                return (
                    <span className={`${styles.statusBadge} ${getStatusClass(transaction.type)}`}>
            {getTransactionStatusType(transaction.type)}
          </span>);
            default:
                return transaction[columnId as keyof Transaction] || '-';
        }
    };

    return (
       <RoleGuard allowedRoles={['transactions']}>
           <div className={styles.container}>
               <div className={styles.header}>
                   <h2 className={styles.title}>Transactions</h2>
                   <button
                       onClick={() => setShowColumnSettings(!showColumnSettings)}
                       className={styles.settingsButton}
                   >
                       <FiSettings />
                   </button>
               </div>

               {error && (
                   <div className={styles.errorMessage}>
                       {error}
                   </div>
               )}

               <div className={styles.searchContainer}>
                   <div className={styles.searchInputRow}>
                       <SearchInput
                           placeholder="Search by Transaction ID"
                           value={filters.TransactionId}
                           onChange={(e) => handleFilterChange('TransactionId', e.target.value)}
                       />
                       <SearchInput
                           placeholder="Search by ISO Message ID"
                           value={filters.ISOMessageId}
                           onChange={(e) => handleFilterChange('ISOMessageId', e.target.value)}
                       />
                       <SearchInput
                           placeholder="Search by End-to-End ID"
                           value={filters.EndToEndId}
                           onChange={(e) => handleFilterChange('EndToEndId', e.target.value)}
                       />
                       <SearchInput
                           placeholder="Search by Local Instrument"
                           value={filters.LocalInstrument}
                           onChange={(e) => handleFilterChange('LocalInstrument', e.target.value)}
                       />
                   </div>
                   <div className={styles.searchInputRow}>
                       <SearchInput
                           placeholder="Search by Category Purpose"
                           value={filters.CategoryPurpose}
                           onChange={(e) => handleFilterChange('CategoryPurpose', e.target.value)}
                       />
                       <SearchInput
                           placeholder="Search by Debtor Account"
                           value={filters.DebtorAccount}
                           onChange={(e) => handleFilterChange('DebtorAccount', e.target.value)}
                       />
                       <SearchInput
                           placeholder="Search by Creditor Account"
                           value={filters.CreditorAccount}
                           onChange={(e) => handleFilterChange('CreditorAccount', e.target.value)}
                       />
                   </div>
                   <div className={styles.filterControlsRow}>
                       <div className={styles.selectGroup}>
                           <SelectInput
                               label=""
                               value={filters.Status}
                               onChange={(e) => handleFilterChange('Status', e.target.value)}
                               options={[
                                   { value: '', label: 'All Statuses' },
                                   ...Object.entries(TransactionStatus)
                                       .filter(([key]) => isNaN(Number(key)))
                                       .map(([key, value]) => ({
                                           value: String(value),
                                           label: key
                                       }))
                               ]}
                               placeholder="-- Select a Status --"
                           />
                       </div>

                       <div className={styles.dateGroup}>
                           <div className={styles.dateInput}>
                               <label>From:</label>
                               <input
                                   type="date"
                                   value={query.FromDate || ''}
                                   onChange={(e) => handleFilterChange('FromDate', e.target.value)}
                                   className={styles.datePicker}
                               />
                           </div>
                           <div className={styles.dateInput}>
                               <label>To:</label>
                               <input
                                   type="date"
                                   value={query.ToDate || ''}
                                   onChange={(e) => handleFilterChange('ToDate', e.target.value)}
                                   className={styles.datePicker}
                               />
                           </div>
                       </div>

                       <ActionButton
                           onClick={refetch}
                           disabled={loading}
                           type={"button"}
                           className={styles.refreshButton}
                       >
                           Refresh
                       </ActionButton>
                   </div>
               </div>

               {showColumnSettings && (
                   <div className={styles.columnSettings}>
                       <h3>Visible Columns</h3>
                       <div className={styles.columnOptions}>
                           {allColumns.map(column => (
                               <label key={column.id} className={styles.columnOption}>
                                   <span>{column.label}</span>
                                   <button
                                       onClick={() => toggleColumnVisibility(column.id)}
                                       className={`${styles.toggleButton} ${
                                           visibleColumns.includes(column.id) ? styles.active : ''
                                       }`}
                                   >
                                       {visibleColumns.includes(column.id) ? <FiCheck /> : <FiX />}
                                   </button>
                               </label>
                           ))}
                       </div>
                   </div>
               )}

               {loading && query.page === 0 ? (
                   <div className={styles.loadingMessage}>Loading transactions...</div>
               ) : (
                   <>
                       <div className={styles.tableWrapper}>
                           <table className={styles.table}>
                               <thead className={styles.tableHeader}>
                               <tr>
                                   {visibleColumns.map(columnId => {
                                       const column = allColumns.find(c => c.id === columnId);
                                       return column ? <th key={columnId}>{column.label}</th> : null;
                                   })}
                               </tr>
                               </thead>
                               <tbody>
                               {transactions.map((transaction) => (
                                   <tr key={transaction.id} className={styles.tableRow}>
                                       {visibleColumns.map(columnId => (
                                           <td key={`${transaction.id}-${columnId}`}>
                                               {renderCellContent(transaction, columnId)}
                                           </td>
                                       ))}
                                   </tr>
                               ))}
                               </tbody>
                           </table>
                       </div>

                       <div className={styles.paginationContainer}>
                           <button
                               onClick={() => setQuery({ page: query.page - 1 })}
                               disabled={query.page === 0 || loading}
                               className={styles.paginationButton}
                           >
                               Previous
                           </button>
                           <span>Page {query.page + 1}</span>
                           <button
                               onClick={() => setQuery({ page: query.page + 1 })}
                               disabled={transactions.length < query.pageSize || loading}
                               className={styles.paginationButton}
                           >
                               Next
                           </button>
                       </div>
                   </>
               )}

               {loading && query.page > 0 && (
                   <div className={styles.loadingMessage}>Loading more transactions...</div>
               )}
           </div>
       </RoleGuard>
    );
};

export default TransactionsList;