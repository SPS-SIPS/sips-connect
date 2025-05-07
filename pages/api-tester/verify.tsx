import React, {useState} from "react";
import {useApiRequest} from "../../utils/apiService";
import SpinLoading from "../../component/Loading/SpinLoading/SpinLoading";
import VerificationRequest from "../../component/RequestForm/VerificationRequest";
import Input from "../../component/common/Input/Input";
import {validateUrl} from "../../utils/validation";
import ActionButton from "../../component/common/ActionButton/ActionButton";
import {EndpointsData} from "../../api/hooks/useEndpoints";
import {
    extractFieldsFromData,
    getUserFieldFromEndpoint,
    getUserFieldsFromEndpoint,
    remapToInternalFields
} from "../../utils/endpointHelpers";
import sharedStyles  from "../../component/ApiRequestTester/ApiRequestTester.module.css";
import styles from "../../styles/VerificationRequestPage.module.css"
import RoleGuard from "../../auth/RoleGuard";
import {useRouter} from "next/router";
import { baseURL } from "../../constants/constants";

const VerificationRequestPage: React.FC = () => {
    const [submittedData, setSubmittedData] = useState<Record<string, string> | null>(null);
    const [apiUrl, setApiUrl] = useState<string>(`${baseURL}/api/v1/Gateway/Verify`);
    const [urlError, setUrlError] = useState<string>("");
    const [response, setResponse] = useState<any>(null);
    const [loading, setLoading] = useState<boolean>(false);
    const [endpointsData, setEndpointsData] = useState<EndpointsData | null>(null);
    const router = useRouter();
    const {makeApiRequest} = useApiRequest();

    const handleApiUrlChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        setApiUrl(e.target.value);
    };

    const handleSubmit = async (formValues: Record<string, string>, endpoints: EndpointsData | null) => {
        setUrlError("");
        setLoading(true);
        if (!validateUrl(apiUrl, {allowLocalhost: true})) {
            setUrlError("Invalid URL. Please enter a valid API endpoint.");
            setLoading(false);
            return;
        }

        const response = await makeApiRequest({
            url: apiUrl,
            method: "post",
            data: formValues,
        });

        setResponse(response);
        setSubmittedData(formValues);
        setEndpointsData(endpoints);
        setLoading(false);
    };

    const handleSendPaymentRequest = () => {
        if (!submittedData || !response?.data || !endpointsData) return;

        const verificationResponseFields = getUserFieldsFromEndpoint(
            endpointsData,
            "VerificationResponse",
            ["Id", "Type", "Name", "Currency"]
        );
        const verificationRequestFields = getUserFieldsFromEndpoint(
            endpointsData,
            "VerificationRequest",
            ["ToBIC"]
        );

        const submittedFieldValues = extractFieldsFromData(submittedData, Object.values(verificationRequestFields));
        const responseFieldValues = extractFieldsFromData(response.data, Object.values(verificationResponseFields));

        const combinedFields = {
            ...submittedFieldValues,
            ...responseFieldValues,
        };

        const internalData = remapToInternalFields(combinedFields, {
            ...verificationResponseFields,
            ...verificationRequestFields
        });
        router.push({
            pathname: '/api-tester/payment',
            query: {
                data: JSON.stringify(internalData),
            },
        });
    };

    const isVerified = (() => {
        const isVerifiedField = getUserFieldFromEndpoint(endpointsData, "VerificationResponse", "IsVerified");
        return isVerifiedField ? response?.data?.[isVerifiedField] : false;
    })();

    return (
        <RoleGuard allowedRoles={['gateway']}>
            <div className={sharedStyles.container}>
                <div className={sharedStyles.titleContainer}>
                    <h1 className={sharedStyles.mainTitle}>API Verification Request Tester</h1>
                    <p className={sharedStyles.subTitle}>
                        Validate and test your API endpoints by sending properly formatted status requests
                    </p>
                </div>
                {loading ? (
                    <div className={styles.loadingContainer}>
                        <SpinLoading/>
                        <p className={styles.loadingText}>Submitting your verification request...</p>
                    </div>
                ) : (
                    !submittedData && (
                        <>
                            <Input
                                label="Enter API Endpoint URL"
                                value={apiUrl}
                                onChange={handleApiUrlChange}
                                type="url"
                                placeholder={"https://example.com/api/v1/Gateway/Verify"}
                                errorMessage={urlError}
                                required
                            />
                            <VerificationRequest onSubmit={handleSubmit}/>
                        </>
                    )
                )}

                {submittedData && !loading && (
                    <div>
                        <div className={styles.resultsContainer}>
                            <div className={styles.apiUrlSection}>
                                <h2 className={styles.apiUrlTitle}>API Endpoint URL</h2>
                                <pre className={styles.apiUrlBlock}>{apiUrl}</pre>
                            </div>

                            <div className={styles.resultSection}>
                                <h2 className={styles.sectionTitle}>Submitted Form Data</h2>
                                <pre className={styles.jsonBlock}>{JSON.stringify(submittedData, null, 2)}</pre>
                            </div>

                            <div className={styles.resultSection}>
                                <h2 className={styles.sectionTitle}>API Response</h2>
                                <pre className={styles.jsonBlock}>{JSON.stringify(response, null, 2)}</pre>
                            </div>
                        </div>

                        <div className={styles.buttonGroup}>
                            <ActionButton
                                type="button"
                                className={styles.clearButton}
                                onClick={() => setSubmittedData(null)}
                            >
                                Clear & Retry
                            </ActionButton>

                            {response?.success && isVerified && (
                                <ActionButton
                                    type="button"
                                    className={styles.sendPaymentButton}
                                    onClick={handleSendPaymentRequest}
                                >
                                    Process Payment
                                </ActionButton>
                            )}
                        </div>
                    </div>

                )}
            </div>
        </RoleGuard>

    );
};

export default VerificationRequestPage;
