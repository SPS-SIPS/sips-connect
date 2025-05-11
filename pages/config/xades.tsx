import React, { useEffect, useState } from 'react';
import Head from 'next/head';
import useConfigurationsXade, { ConfigurationsXade } from '../../api/hooks/useConfigurationsXade';
import ConfigFormWrapper from '../../component/ConfigFormWrapper/ConfigFormWrapper';
import Input from '../../component/common/Input/Input';
import Checkbox from '../../component/common/Checkbox/Checkbox';
import styles from '../../styles/ConfigurationsForm.module.css';
import { AxiosError } from 'axios';
import RoleGuard from "../../auth/RoleGuard";
import MultiSelectDropdown, {OptionType} from "../../component/common/MultiSelectDropdown/MultiSelectDropdown";

const XadesConfigForm = () => {
    const { getConfigurations, updateConfigurations } = useConfigurationsXade();
    const [config, setConfig] = useState<ConfigurationsXade | null>(null);
    const [loading, setLoading] = useState(true);
    const [selectedOptions, setSelectedOptions] = useState<OptionType[]>([]);
    const [availableAlgorithms, setAvailableAlgorithms] = useState<OptionType[]>([]);
    const [showPassphrase, setShowPassphrase] = useState(false);
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

                // Transform available algorithms from API to OptionType
                const algorithmOptions = data.algorithms.map(algo => ({
                    value: algo,
                    label: algo
                }));
                setAvailableAlgorithms(algorithmOptions);

                // Set initially selected algorithms
                const initialSelected = algorithmOptions.filter(option =>
                    data.algorithms.includes(option.value)
                );
                setSelectedOptions(initialSelected);
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
        const { name, value, type, checked } = e.target;

        let updatedValue: string | number | boolean = value;

        if (type === 'checkbox') {
            updatedValue = checked;
        } else if (name === 'verificationWindowMinutes') {
            updatedValue = Number(value);
        }

        setConfig({ ...config, [name]: updatedValue });
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!config) return;
        setLoading(true);
        try {
            const response = await updateConfigurations(config);
            setModal({ show: true, title: 'Success', message: response, error: false });
        } catch (error) {
            setModal({ show: true, title: 'Error', message: handleErrorMessage(error, 'Failed to update configuration'), error: true });
        } finally {
            setLoading(false);
        }
    };


    const handleAlgorithmChange = (selectedOptions: OptionType[] | null) => {
        if (!config) return;

        const selected = selectedOptions || [];
        setSelectedOptions(selected);

        setConfig({
            ...config,
            algorithms: selected.map(option => option.value)
        });
    };

    if (errorMessage) return <div className={styles.errorContainer}>{errorMessage}</div>;

    return (
        <RoleGuard allowedRoles={['configuration']}>
            <Head>
                <title>Xades Configuration | SPS</title>
            </Head>

            <ConfigFormWrapper
                title="Xades Configuration"
                subtitle="Configure digital signature and verification settings using Xades."
                config={config}
                loading={loading}
                onChange={handleChange}
                onSubmit={handleSubmit}
                renderForm={(cfg, onChange) => (
                    <div className={styles.section}>
                        <Input label="Certificate Path" name="certificatePath" type="text" value={cfg.certificatePath} onChange={onChange} />
                        <Input label="Private Key Path" name="privateKeyPath" type="text" value={cfg.privateKeyPath} onChange={onChange} />
                        <div className={styles.passwordSection}>
                            <Input
                                label="Private Key Passphrase"
                                name="privateKeyPassphrase"
                                type={showPassphrase ? 'text' : 'password'}
                                value={cfg.privateKeyPassphrase}
                                onChange={onChange}
                            />
                            <Checkbox
                                label="Show Passphrase"
                                checked={showPassphrase}
                                onChange={(e) => setShowPassphrase(e.target.checked)}
                            />
                        </div>
                        <Input label="Chain Path" name="chainPath" type="text" value={cfg.chainPath} onChange={onChange} />

                        <MultiSelectDropdown
                            label="Algorithm"
                            value={selectedOptions}
                            onChange={handleAlgorithmChange}
                            options={availableAlgorithms}
                            required
                        />
                        <Input label="Verification Window (minutes)" name="verificationWindowMinutes" type="number" value={cfg.verificationWindowMinutes.toString()} onChange={onChange} min={1} />
                        <Input label="BIC" name="bic" type="text" value={cfg.bic} onChange={onChange} />
                        <Checkbox label="Without PKI" name="withoutPKI" checked={cfg.withoutPKI} onChange={onChange} />
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

export default XadesConfigForm;
