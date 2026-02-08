import React, {useState, useCallback} from "react";
import styles from './ApiRequestTester.module.css';
import {APIError, APIResponse} from "../../types/types";
import {validateUrl} from "../../utils/validation";
import Input from "../../component/common/Input/Input";
import RequestFormWrapper from "../../component/RequestFormWrapper/RequestFormWrapper";
import {useApiRequest} from "../../utils/apiService";
import SpinLoading from "../Loading/SpinLoading/SpinLoading";

interface StatusProps {
    title: string;
    placeholder: string;
    selectedRequest: string;
    initialUrl: string;
}

interface ApiConfig {
    apiUrl: string;
    urlError: string;
}

const ApiRequestTester: React.FC<StatusProps> = ({ title, placeholder, selectedRequest, initialUrl }) => {
    const [apiConfig, setApiConfig] = useState<ApiConfig>({
        apiUrl: initialUrl,
        urlError: ''
    });
    const [response, setResponse] = useState<APIResponse | null>(null);
    const [error, setError] = useState<APIError | null>(null);
    const { makeApiRequest, loading } = useApiRequest();

    const handleApiUrlChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const {value} = e.target;
        setApiConfig(prev => ({
            ...prev,
            apiUrl: value,
            urlError: value ? "" : prev.urlError
        }));
    }, []);

    const handleSubmit = useCallback(async (formData: Record<string, string>) => {
        if (!validateUrl(apiConfig.apiUrl, { allowLocalhost: true })) {
            setApiConfig(prev => ({
                ...prev,
                urlError: "Please enter a valid API URL."
            }));
            return;
        }

        try {
            const result = await makeApiRequest({
                url: apiConfig.apiUrl,
                method: "post",
                data: formData
            });

            if (result.success) {
                setResponse({
                    data: result.data,
                    status: result.status,
                    success: true
                });
                setError(null);
            } else {
                setError({
                    message: 'Request failed',
                    details: result.error || 'Unknown error occurred',
                    statusCode: result.status
                });
                setResponse(null);
            }
        } catch (err) {
            setError({
                message: 'Unexpected error',
                details: (err as Error).message,
                statusCode: 500
            });
            setResponse(null);
        }
    }, [apiConfig.apiUrl, makeApiRequest]);

    return (
        <div className={styles.container}>
            <div className={styles.titleContainer}>
                <h1 className={styles.mainTitle}>{title}</h1>
                <p className={styles.subTitle}>
                    Validate and test your API endpoints by sending properly formatted status requests
                </p>
            </div>

            {loading ? (
                <div className={styles.loadingContainer}>
                    <SpinLoading />
                    <p className={styles.loadingText}>Processing request...</p>
                </div>
            ) : (
                <>
                    <Input
                        label="Enter API Endpoint URL"
                        value={apiConfig.apiUrl}
                        onChange={handleApiUrlChange}
                        type="url"
                        placeholder={placeholder}
                        errorMessage={apiConfig.urlError}
                        required
                    />

                    <RequestFormWrapper
                        selectedRequest={selectedRequest}
                        onSubmit={handleSubmit}
                    />
                </>
            )}

            {response && (
                <section className={styles.responseSection}>
                    <div className={styles.responseMessage}>
                        <h3 className={styles.responseTitle}>API Response</h3>
                        <pre className={styles.responseText}>
                            {JSON.stringify(response, null, 2)}
                        </pre>
                    </div>
                </section>
            )}

            {error && (
                <section className={styles.errorSection}>
                    <div className={styles.errorMessage}>
                        <h3 className={styles.errorTitle}>Request Failed</h3>
                        <pre className={styles.errorText}>
                            {JSON.stringify(error, null, 2)}
                        </pre>
                    </div>
                </section>
            )}
        </div>
    );
};

export default ApiRequestTester;