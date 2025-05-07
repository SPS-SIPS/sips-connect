import PaymentRequest from "../../component/RequestForm/PaymentRequest";
import {useRouter} from "next/router";
import React, {useEffect, useState} from "react";
import RoleGuard from "../../auth/RoleGuard";
import {mapToPrefilledValues} from "../../utils/endpointHelpers";
import SpinLoading from "../../component/Loading/SpinLoading/SpinLoading";
import Input from "../../component/common/Input/Input";
import ActionButton from "../../component/common/ActionButton/ActionButton";
import sharedStyles from "../../component/ApiRequestTester/ApiRequestTester.module.css";
import styles from "../../styles/VerificationRequestPage.module.css";
import {validateUrl} from "../../utils/validation";
import {useApiRequest} from "../../utils/apiService";
import { baseURL } from "../../constants/constants";

const PaymentPage = () => {
    const router = useRouter();
    const [internalData, setInternalData] = useState(null);
    const [loading, setLoading] = useState<boolean>(false);
    const [submittedData, setSubmittedData] = useState<Record<string, string> | null>(null);
    const [apiUrl, setApiUrl] = useState<string>(`${baseURL}/api/v1/Gateway/Payment`);
    const [response, setResponse] = useState<any>(null);
    const [urlError, setUrlError] = useState<string>("");
    const {makeApiRequest} = useApiRequest();
    useEffect(() => {
        if (router.query.data) {
            try {
                const parsed = JSON.parse(router.query.data as string);
                let currency = parsed?.Currency;
                if (!currency || currency.trim() === "") currency = "USD";

                setInternalData({...parsed, Currency: currency});
                console.log(parsed);
            } catch (e) {
                console.error("Failed to parse internalData:", e);
            }
        }
    }, [router.query.data]);

    const handleSubmit = async (formValues: Record<string, string>) => {
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
        setLoading(false);
    };


    const prefilledValues = internalData ? mapToPrefilledValues(internalData) : null;

    return (
        <RoleGuard allowedRoles={['gateway']}>
            <div className={sharedStyles.container}>
                <div className={sharedStyles.titleContainer}>
                    <h1 className={sharedStyles.mainTitle}>API Payment Request Tester</h1>
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
                                onChange={(e) => setApiUrl(e.target.value)}
                                type="url"
                                placeholder={"https://example.com/api/v1/Gateway/Payment"}
                                errorMessage={urlError}
                                required
                            />
                            <PaymentRequest onSubmit={handleSubmit} prefilledValues={prefilledValues} />
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
                        </div>
                    </div>
                )}
            </div>
        </RoleGuard>

    )
}
export default PaymentPage;