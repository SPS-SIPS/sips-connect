import React from 'react';
import {useAuthentication} from "../../../../auth/AuthProvider";
import {QRParser} from "../../../../component/QRParser/QRParser";
import {makeApiRequest} from "../../../../utils/apiService";
import { baseURL } from "../../../../constants";
import RoleGuard from "../../../../auth/RoleGuard";

 const PersonalQRParser: React.FC = () => {
    const { authToken } = useAuthentication();

    const handleParse = async (qrCode: string) => {
        const response = await makeApiRequest({
            url: `${baseURL}/api/v1/somqr/ParsePersonQR?code=${encodeURIComponent(qrCode)}`,
            method: 'GET',
            token: authToken!,
        });
        return response.data;
    };

    return (
        <RoleGuard allowedRoles={['som_qr']}>
            <QRParser
                title="Personal QR Code Parser"
                subtitle="Parse personal payment QR codes to view transaction details"
                onParse={handleParse}
            />
        </RoleGuard>
    );
};
 export default PersonalQRParser;