import React, { useState } from 'react';
import { FiSettings, FiX, FiCheck } from 'react-icons/fi';

import {useISOMessages} from "../../api/hooks/useISOMessages";
import {
    getISOMessageTypeText,
    getTransactionStatusText,
    ISOMessage,
    ISOMessageType,
    TransactionStatus
} from "../../types/types";
import SelectInput from "../../component/common/SelectInput/SelectInput";
import SearchInput from "../../component/common/SearchInput/SearchInput";
import ActionButton from "../../component/common/ActionButton/ActionButton";

import styles from '../../styles/ISOMessagesList.module.css';
import RoleGuard from "../../auth/RoleGuard";

const allColumns = [
    { id: 'msgId', label: 'Message ID' },
    { id: 'messageType', label: 'Type' },
    { id: 'status', label: 'Status' },
    { id: 'date', label: 'Date' },
    { id: 'fromBIC', label: 'From BIC' },
    { id: 'toBIC', label: 'To BIC' },
    { id: 'txId', label: 'Transaction ID' },
    { id: 'endToEndId', label: 'End To End ID' },
    { id: 'bizMsgIdr', label: 'Business Msg ID' },
    { id: 'msgDefIdr', label: 'Msg Definition ID' },
    { id: 'reason', label: 'Reason' },
    { id: 'additionalInfo', label: 'Additional Info' },
];

const ISOMessagesList = () => {
    const {
        messages,
        loading,
        error,
        query,
        setQuery,
        refetch
    } = useISOMessages();

    const [showColumnSettings, setShowColumnSettings] = useState(false);
    const [visibleColumns, setVisibleColumns] = useState<string[]>([
        'msgId', 'messageType', 'status', 'date', 'fromBIC', 'toBIC', 'txId'
    ]);

    const toggleColumnVisibility = (columnId: string) => {
        setVisibleColumns(prev =>
            prev.includes(columnId)
                ? prev.filter(id => id !== columnId)
                : [...prev, columnId]
        );
    };
    const [filters, setFilters] = useState({
        msgId: '',
        bizMsgIdr: '',
        msgDefIdr: '',
        status: '',
        type: '',
        fromDate: '',
        toDate: ''
    });

    const handleFilterChange = (field: keyof typeof filters, value: string) => {
        const newFilters = { ...filters, [field]: value };
        setFilters(newFilters);
        // @ts-ignore
        setQuery({
            ...newFilters,
            page: 0
        });
    };

    const formatDate = (dateString: string) => {
        return new Date(dateString).toLocaleString();
    };

    const getStatusClass = (status: TransactionStatus) => {
        switch(status) {
            case TransactionStatus.Success: return styles.success;
            case TransactionStatus.Failed: return styles.error;
            case TransactionStatus.Pending: return styles.warning;
            default: return '';
        }
    };

    const renderCellContent = (message: ISOMessage, columnId: string) => {
        switch(columnId) {
            case 'messageType':
                return getISOMessageTypeText(message.messageType);
            case 'status':
                return (
                    <span className={`${styles.statusBadge} ${getStatusClass(message.status)}`}>
            {getTransactionStatusText(message.status)}
          </span>
                );
            case 'date':
                return formatDate(message.date);
            default:
                return message[columnId as keyof ISOMessage] || '-';
        }
    };

    return (
   <RoleGuard allowedRoles={['iso_messages']}>
       <div className={styles.container}>
           <div className={styles.header}>
               <h2 className={styles.title}>ISO Messages</h2>
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
               {/* First Line - Search Inputs Only */}
               <div className={styles.searchInputRow}>
                   <SearchInput
                       value={filters.msgId}
                       onChange={(e) => handleFilterChange('msgId', e.target.value)}
                       placeholder="Message ID"
                   />
                   <SearchInput
                       value={filters.bizMsgIdr}
                       onChange={(e) => handleFilterChange('bizMsgIdr', e.target.value)}
                       placeholder="Business Msg ID"
                   />
                   <SearchInput
                       value={filters.msgDefIdr}
                       onChange={(e) => handleFilterChange('msgDefIdr', e.target.value)}
                       placeholder="Msg Definition ID"
                   />
               </div>

               {/* Second Line - Selects, Dates, and Button */}
               <div className={styles.filterControlsRow}>
                   <div className={styles.selectGroup}>
                       <SelectInput
                           label=""
                           value={filters.status}
                           onChange={(e) => handleFilterChange('status', e.target.value)}
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
                       <SelectInput
                           label=""
                           value={filters.type}
                           onChange={(e) => handleFilterChange('type', e.target.value)}
                           options={[
                               { value: '', label: 'All Types' },
                               ...Object.entries(ISOMessageType)
                                   .filter(([key]) => isNaN(Number(key)))
                                   .map(([key, value]) => ({
                                       value: String(value),
                                       label: key
                                   }))
                           ]}
                           placeholder="-- Select a Type --"
                       />
                   </div>

                   <div className={styles.dateGroup}>
                       <div className={styles.dateInput}>
                           <label>From:</label>
                           <input
                               type="date"
                               value={filters.fromDate}
                               onChange={(e) => handleFilterChange('fromDate', e.target.value)}
                           />
                       </div>
                       <div className={styles.dateInput}>
                           <label>To:</label>
                           <input
                               type="date"
                               value={filters.toDate}
                               onChange={(e) => handleFilterChange('toDate', e.target.value)}
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
               <div className={styles.loadingMessage}>Loading messages...</div>
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
                           {messages.map((message) => (
                               <tr key={message.id} className={styles.tableRow}>
                                   {visibleColumns.map(columnId => (
                                       <td key={`${message.id}-${columnId}`}>
                                           {renderCellContent(message, columnId)}
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
                           disabled={messages.length < query.pageSize || loading}
                           className={styles.paginationButton}
                       >
                           Next
                       </button>
                   </div>
               </>
           )}

           {loading && query.page > 0 && (
               <div className={styles.loadingMessage}>Loading more messages...</div>
           )}
       </div>
   </RoleGuard>
    );
};

export default ISOMessagesList;