import styles from "./QRForm.module.css";
import React, { useState } from "react";
import Input from "../common/Input/Input";
import ActionButton from "../common/ActionButton/ActionButton";
import QRCodeGenerator from "../QRCodeGenerator/QRCodeGenerator";
import SelectInput from "../common/SelectInput/SelectInput";
import { useApiRequest } from "../../utils/apiService";
import SpinLoading from "../Loading/SpinLoading/SpinLoading";

interface FormField {
    label: string;
    name: string;
    type: string;
    required: boolean;
    placeholder: string;
    options?: { value: string; label: string }[];
}

interface QRFormProps {
    title: string;
    subtitle: string;
    apiEndpoint: string;
    initialData: Record<string, unknown>;
    formFields: FormField[];
}

const QRForm: React.FC<QRFormProps> = ({
                                           title,
                                           subtitle,
                                           apiEndpoint: defaultApiEndpoint,
                                           initialData,
                                           formFields,
                                       }) => {
    const [responseData, setResponseData] = useState<string>("");
    const [error, setError] = useState<string>("");
    const [apiEndpoint, setApiEndpoint] = useState<string>(defaultApiEndpoint);
    const [formData, setFormData] = useState<Record<string, unknown>>(initialData);
    const { makeApiRequest, loading } = useApiRequest();

    const handleInputChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
        const { name, value } = e.target;
        setFormData((prev) => ({ ...prev, [name]: value }));
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");
        setResponseData("");

        const result = await makeApiRequest({
            url: apiEndpoint,
            method: "post",
            data: formData
        });


        if (result.success) {
            try {
                // @ts-ignore
                const qrData = result.data?.data;
                if (typeof qrData === "string") {
                    setResponseData(qrData);
                } else {
                    throw new Error("Invalid QR data format in response");
                }
            } catch (err) {
                setError(err instanceof Error ? err.message : "Failed to generate QR code");
            }
        } else {
            setError(result.error || "Failed to generate QR code");
        }
    };

    return (
        <div className={styles.container}>
            <form onSubmit={handleSubmit} className={styles.formContainer}>
                <div className={styles.formHeader}>
                    <h2 className={styles.title}>{title}</h2>
                    <p className={styles.subtitle}>{subtitle}</p>
                </div>

                <div className={styles.apiEndpoint}>
                    <Input
                        label="API Endpoint"
                        value={apiEndpoint}
                        onChange={(e) => setApiEndpoint(e.target.value)}
                        type="url"
                        required
                        placeholder="https://your-api-server.com/api/v1/endpoint"
                    />
                </div>
                
                {formFields.map((field) => (
                    <div key={field.name} className={styles.inputWrapper}>
                        {field.type === 'select' ? (
                            <SelectInput
                                label={field.label}
                                name={field.name}
                                value={String(formData[field.name] || "")}
                                onChange={handleInputChange}
                                required={field.required}
                                options={field.options || []}
                            />
                        ) : (
                            <Input
                                label={field.label}
                                name={field.name}
                                type={field.type}
                                value={String(formData[field.name] || "")}
                                onChange={handleInputChange}
                                required={field.required}
                                placeholder={field.placeholder}
                            />
                        )}
                    </div>
                ))}
                <ActionButton
                    type="submit"
                    className={styles.submitButton}
                    disabled={loading}
                >
                    {loading ? "Generating..." : "Generate QR Code"}
                </ActionButton>
            </form>


            <div className={styles.responseContainer}>
                {loading && (
                    <div className={styles.loadingContainer}>
                        <SpinLoading />
                        <p className={styles.loadingText}>Processing request...</p>
                    </div>
                )}

                {error && <div className={styles.errorMessage}>Error: {error}</div>}

                {responseData && (
                    <div className={styles.qrSection}>
                        <h3 className={styles.qrTitle}>Generated QR Code</h3>
                        <div className={styles.qrCodeWrapper}>
                            <QRCodeGenerator data={responseData} />
                        </div>
                        <div className={styles.qrData}>
                            <pre>{responseData}</pre>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};

export default QRForm;