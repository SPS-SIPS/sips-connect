import QRForm from "../../../../component/QRForm/QRForm";
import {currencyOptions} from "../../../../data/currencyOptions";
import {mccOptions} from "../../../../data/mccOptions";
import { baseURL } from "../../../../constants/constants";
import RoleGuard from "../../../../auth/RoleGuard";

const Merchant = () => {
    const merchantInitialData = {
        type: 1,
        method: 1,
        merchantId: "",
        merchantCategoryCode: "",
        currencyCode: "",
        merchantName: "",
        merchantCity: "",
        postalCode: "",
        storeLabel: "",
        terminalLabel: "",
    };
    const merchantFormFields = [
        {label: 'Type', name: 'type', type: 'number', required: true, placeholder: 'Enter Type'},
        {label: 'Method', name: 'method', type: 'number', required: true, placeholder: 'Enter Method'},
        {label: 'Merchant ID', name: 'merchantId', type: 'text', required: true, placeholder: 'Enter Merchant ID'},
        {
            label: 'Merchant Category',
            name: 'merchantCategoryCode',
            type: 'select',
            options: mccOptions,
            required: true,
            placeholder: 'Select MCC code'
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
            label: 'Merchant Name',
            name: 'merchantName',
            type: 'text',
            required: true,
            placeholder: 'Enter Merchant Name'
        },
        {
            label: 'Merchant City',
            name: 'merchantCity',
            type: 'text',
            required: true,
            placeholder: 'Enter Merchant City'
        },
        {label: 'Postal Code', name: 'postalCode', type: 'text', required: false, placeholder: 'Enter Postal Code'},
        {label: 'Store Label', name: 'storeLabel', type: 'text', required: false, placeholder: 'Enter Store Label'},
        {
            label: 'Terminal Label',
            name: 'terminalLabel',
            type: 'text',
            required: false,
            placeholder: 'Enter Terminal Label'
        },
    ];
  return(
      <RoleGuard allowedRoles={['som_qr']}>
          <QRForm
              title="Merchant QR Code Generator"
              subtitle="Fill in merchant details to generate a payment QR code"
              apiEndpoint={`${baseURL}/api/v1/SomQR/GenerateMerchantQR`}
              initialData={merchantInitialData}
              formFields={merchantFormFields}
          />
      </RoleGuard>
  )
}
export default Merchant;