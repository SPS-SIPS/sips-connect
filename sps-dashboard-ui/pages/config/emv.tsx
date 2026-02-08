import React, { useEffect, useState } from 'react';
import Head from 'next/head';

import Input from '../../component/common/Input/Input';
import styles from '../../styles/ConfigurationsForm.module.css';
import ConfigFormWrapper from '../../component/ConfigFormWrapper/ConfigFormWrapper';
import { AxiosError } from 'axios';
import useConfigurationsEmv, {ConfigurationsEmv} from "../../api/hooks/useConfigurationsEmv";
import RoleGuard from "../../auth/RoleGuard";

const EmvConfigForm = () => {
    const { getConfigurations, updateConfigurations } = useConfigurationsEmv();

    const [config, setConfig] = useState<ConfigurationsEmv | null>(null);
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
                setConfig(data);
            } catch (error) {
                setErrorMessage(handleErrorMessage(error, 'Failed to load configurations'));
            } finally {
                setLoading(false);
            }
        };

        fetchData();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (!config) return;
        const { name, value } = e.target;

        // Nested tag handling
        if (name in config.tags) {
            setConfig({
                ...config,
                tags: {
                    ...config.tags,
                    [name]: parseInt(value, 10)
                }
            });
        } else {
            setConfig({ ...config, [name]: value });
        }
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
                error: true
            });
        } finally {
            setLoading(false);
        }
    };

    if (errorMessage) return <div className={styles.errorContainer}>{errorMessage}</div>;

    return (
        <RoleGuard allowedRoles={['configuration']}>
            <Head>
                <title>EMV Configuration | SPS</title>
            </Head>

            <ConfigFormWrapper
                title="EMV Configuration"
                subtitle="Configure EMV acquirer and tag settings."
                config={config}
                loading={loading}
                onChange={handleChange}
                onSubmit={handleSubmit}
                renderForm={(cfg, onChange) => (
                    <div className={styles.section}>
                        <Input
                            label="Acquirer ID"
                            name="acquirerId"
                            type="text"
                            value={cfg.acquirerId}
                            onChange={onChange}
                            placeholder="Enter Acquirer ID"
                        />
                        <Input
                            label="FI Type"
                            name="fiType"
                            type="text"
                            value={cfg.fiType}
                            onChange={onChange}
                            placeholder="Enter Financial Institution Type"
                        />
                        <Input
                            label="FI Name"
                            name="fiName"
                            type="text"
                            value={cfg.fiName}
                            onChange={onChange}
                            placeholder="Enter Financial Institution Name"
                        />
                        <Input
                            label="Version"
                            name="version"
                            type="text"
                            value={cfg.version}
                            onChange={onChange}
                            placeholder="Enter Version"
                        />
                        <Input
                            label="Country Code"
                            name="countryCode"
                            type="text"
                            value={cfg.countryCode}
                            onChange={onChange}
                            placeholder="Enter Country Code"
                        />
                        <Input
                            label="Merchant Identifier Tag"
                            name="merchantIdentifier"
                            type="number"
                            value={cfg.tags.merchantIdentifier.toString()}
                            onChange={onChange}
                            placeholder="e.g. 9F16"
                        />
                        <Input
                            label="Acquirer Tag"
                            name="acquirerTag"
                            type="number"
                            value={cfg.tags.acquirerTag.toString()}
                            onChange={onChange}
                            placeholder="e.g. 9F01"
                        />
                        <Input
                            label="Merchant ID Tag"
                            name="merchantIdTag"
                            type="number"
                            value={cfg.tags.merchantIdTag.toString()}
                            onChange={onChange}
                            placeholder="e.g. 9F10"
                        />
                    </div>
                )}
                modalProps={{
                    ...modal,
                    onClose: () => setModal({ show: false, title: '', message: '', error: false }),
                }}
            />
        </RoleGuard>
    );
};

export default EmvConfigForm;
