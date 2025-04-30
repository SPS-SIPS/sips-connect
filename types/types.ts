export interface BaseQuery {
    page: number;
    pageSize: number;
}

// Query types
export interface TransactionQuery extends BaseQuery {
    ISOMessageId?: number;
    TransactionId?: string;
    EndToEndId?: string;
    LocalInstrument?: string;
    CategoryPurpose?: string;
    DebtorAccount?: string;
    CreditorAccount?: string;
    Status?: TransactionStatus;
    FromDate?: string;
    ToDate?: string;
}

export interface MessageQuery extends BaseQuery {
    msgId?: string;
    bizMsgIdr?: string;
    msgDefIdr?: string;
    status?: TransactionStatus;
    type?: ISOMessageType;
    fromDate?: string;
    toDate?: string;
}

export interface Transaction {
    id: number;
    type: TransactionType;
    isoMessageId: number;
    fromBIC?: string;
    localInstrument?: string;
    categoryPurpose?: string;
    endToEndId?: string;
    txId?: string;
    amount?: number;
    currency?: string;
    debtorName?: string;
    debtorAccount?: string;
    debtorAccountType?: string;
    debtorAgentBIC?: string;
    debtorIssuer: string;
    creditorName?: string;
    creditorAccount?: string;
    creditorAccountType?: string;
    creditorAgentBIC?: string;
    creditorIssuer?: string;
    remittanceInformation?: string;
}

export interface ISOMessage {
    id: number;
    messageType: ISOMessageType;
    status: TransactionStatus;
    msgId: string;
    bizMsgIdr: string;
    msgDefIdr: string;
    round: number;
    txId?: string;
    endToEndId?: string;
    reason?: string;
    additionalInfo?: string;
    date: string;
    fromBIC: string;
    toBIC: string;
}

export enum TransactionStatus {
    Success = 1,
    Failed = 2,
    Pending = 3
}

export enum ISOMessageType {
    VerificationRequest = 1,
    TransactionRequest = 2,
    StatusRequest = 3,
    ReturnRequest = 4,
    VerificationResponse = 5,
    TransactionResponse = 6,
    StatusResponse = 7,
    ReturnResponse = 8,
    InvalidMessageType = 9
}

export enum TransactionType {
    Deposit = 1,
    Withdrawal = 2,
    ReturnDeposit = 3,
    ReturnWithdrawal = 4
}


export const getISOMessageTypeText = (type: ISOMessageType): string => {
    return ISOMessageType[type] ?? 'Unknown';
};

export const getTransactionStatusText = (status: TransactionStatus): string => {
    return TransactionStatus[status] ?? 'Unknown';
};

export const getTransactionStatusType = (type: TransactionType): string => {
    return TransactionType[type] ?? 'Unknown';
};

export type DataItem = {
    value: number;
    label: string;
    groupId: string;
    ratioOrBalance: string | null;
    isDefault: boolean;
    children: null | unknown;
};