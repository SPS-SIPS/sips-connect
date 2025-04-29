import React, { useEffect, useState } from 'react';
import Head from 'next/head';
import useConfigurationsOrigins, { ConfigurationsOrigins } from '../../api/hooks/useConfigurationsOrigins';
import Input from '../../component/common/Input/Input';
import ConfigFormWrapper from '../../component/ConfigFormWrapper/ConfigFormWrapper';
import { AxiosError } from 'axios';
import ActionButton from "../../component/common/ActionButton/ActionButton";
import styles from '../../styles/ConfigurationsForm.module.css';

const OriginsConfigForm = () => {
    const { getConfigurations, updateConfigurations } = useConfigurationsOrigins();

    const [config, setConfig] = useState<ConfigurationsOrigins | null>(null);
    const [loading, setLoading] = useState(true);
    const [modal, setModal] = useState({
        show: false,
        title: '',
        message: '',
        error: false,
    });
    const [errorMessage, setErrorMessage] = useState<string | null>(null);

    const handleErrorMessage = (error: unknown, defaultMessage: string): string => {
        if (typeof error === 'string') return error;
        if (error instanceof AxiosError) return error.response?.data?.message || error.message || defaultMessage;
        if (error instanceof Error) return error.message;
        return defaultMessage;
    };

    useEffect(() => {
        const fetchData = async () => {
            try {
                const data = await getConfigurations();
                setConfig({ origins: data.origins ?? [] });
            } catch (error) {
                setErrorMessage(handleErrorMessage(error, 'Failed to load configurations'));
            } finally {
                setLoading(false);
            }
        };

        fetchData();
    }, []);

    const handleOriginChange = (index: number, value: string) => {
        if (!config) return;
        const updatedOrigins = [...config.origins];
        updatedOrigins[index] = value;
        setConfig({ ...config, origins: updatedOrigins });
    };

    const addOrigin = () => {
        if (!config) return;
        setConfig({ ...config, origins: [...config.origins, ''] });
    };

    const removeOrigin = (index: number) => {
        if (!config) return;
        const updatedOrigins = config.origins.filter((_, i) => i !== index);
        setConfig({ ...config, origins: updatedOrigins });
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!config) return;
        setLoading(true);
        try {
            const response = await updateConfigurations(config);
            setModal({ show: true, title: 'Success', message: response, error: false });
        } catch (error) {
            setModal({
                show: true,
                title: 'Error',
                message: handleErrorMessage(error, 'Failed to update configuration'),
                error: true,
            });
        } finally {
            setLoading(false);
        }
    };

    if (errorMessage) return <div className={styles.errorContainer}>{errorMessage}</div>;

    return (
        <>
            <Head>
                <title>Origins Configuration | SPS</title>
            </Head>

            <ConfigFormWrapper
                title="Origins Configuration"
                subtitle="Manage allowed origins for your services."
                config={config}
                loading={loading}
                onChange={() => {}}
                onSubmit={handleSubmit}
                renderForm={() => (
                    <div className={styles.section}>
                        {config?.origins.map((origin, index) => (
                            <div key={index} className={styles.inlineGroup}>
                                <Input
                                    label={`Origin ${index + 1}`}
                                    name={`origin-${index}`}
                                    type="text"
                                    value={origin}
                                    onChange={(e) => handleOriginChange(index, e.target.value)}
                                />
                                <ActionButton
                                    type="button"
                                    onClick={() => removeOrigin(index)}
                                    className={styles.removeButton}
                                >
                                    Remove
                                </ActionButton>
                            </div>
                        ))}
                        <ActionButton type="button" onClick={addOrigin} className={styles.addButton}>
                            Add Origin
                        </ActionButton>
                    </div>
                )}
                modalProps={{
                    ...modal,
                    onClose: () => setModal({ show: false, title: '', message: '', error: false }),
                }}
            />
        </>
    );
};

export default OriginsConfigForm;
