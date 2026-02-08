import React from 'react';
import {QRParser} from "../../../../component/QRParser/QRParser";
import {useApiRequest} from "../../../../utils/apiService";
import {baseURL} from "../../../../constants/constants";
import RoleGuard from "../../../../auth/RoleGuard";

const PersonalQRParser: React.FC = () => {
    const {makeApiRequest} = useApiRequest();
    const handleParse = async (qrCode: string) => {
        const response = await makeApiRequest({
            url: `${baseURL}/api/v1/somqr/ParsePersonQR?code=${encodeURIComponent(qrCode)}`,
            method: 'get',
        });
        return response.data;
    };

    return (
        <RoleGuard allowedRoles={['som_qr']}>
            <QRParser
                title="Personal QR Code Parser"
                subtitle="Parse personal payment QR codes to view transaction details"
                onParse={handleParse}
                qrType={'personal'}
            />
        </RoleGuard>
    );
};
export default PersonalQRParser;