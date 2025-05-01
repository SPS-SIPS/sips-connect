import React from 'react';
import {QRParser} from "../../../../component/QRParser/QRParser";
import {useAuthentication} from "../../../../auth/AuthProvider";
import {makeApiRequest} from "../../../../utils/apiService";
import { baseURL } from "../../../../constants";
import RoleGuard from "../../../../auth/RoleGuard";

const MerchantQRParser: React.FC = () => {
    const { authToken } = useAuthentication();

    const handleParse = async (qrCode: string) => {
        const response = await makeApiRequest({
            url: `${baseURL}/api/v1/SomQR/ParseMerchantQR?code=${encodeURIComponent(qrCode)}`,
            method: 'GET',
            token: authToken!,
        });
        return response.data;
    };

    return (
        <RoleGuard allowedRoles={['som_qr']}>
            <QRParser
                title="Merchant QR Code Parser"
                subtitle="Parse merchant QR codes to view transaction details"
                onParse={handleParse}
            />
        </RoleGuard>
    );
};

export default MerchantQRParser;