import QRForm from "../../../../component/QRForm/QRForm";
import {currencyOptions} from "../../../../data/currencyOptions";
import { baseURL } from "../../../../constants/constants";
import ProtectedRoute from "../../../../component/common/ProtectedRoute";
import RoleGuard from "../../../../auth/RoleGuard";

const Personal = () => {
    const personalInitialData = {
        amount: 0,
        accountName: "",
        iban: "",
        currencyCode: "",
        particulars: ""
    };

    const personalFormFields = [
        {
            label: 'Amount',
            name: 'amount',
            type: 'number',
            required: true,
            placeholder: 'Enter payment amount'
        },
        {
            label: 'Account Name',
            name: 'accountName',
            type: 'text',
            required: true,
            placeholder: 'Enter account holder name'
        },
        {
            label: 'IBAN',
            name: 'iban',
            type: 'text',
            required: true,
            placeholder: 'Enter account IBAN'
        },
        {
            label: 'Currency Code',
            name: 'currencyCode',
            type: 'select',
            options: currencyOptions,
            required: true,
            placeholder: 'Enter currency code (e.g. USD)'
        },
        {
            label: 'Payment Particulars',
            name: 'particulars',
            type: 'text',
            required: false,
            placeholder: 'Enter payment description'
        }
    ];

    return (
            <RoleGuard allowedRoles={['som_qr']}>
            <QRForm
                title="Personal Payment QR Generator"
                subtitle="Fill in payment details to generate a personal QR code"
                apiEndpoint={`${baseURL}/api/v1/SomQR/GeneratePersonQR`}
                initialData={personalInitialData}
                formFields={personalFormFields}
            />
            </RoleGuard>
    )
}

export default Personal;