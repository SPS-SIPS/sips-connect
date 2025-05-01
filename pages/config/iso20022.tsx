import React, {useEffect, useState} from 'react';
import Head from 'next/head';
import useConfigurationsISO20022, {ConfigurationsISO20022} from '../../api/hooks/useConfigurationsISO20022';

import Input from '../../component/common/Input/Input';
import Checkbox from '../../component/common/Checkbox/Checkbox';
import styles from '../../styles/ConfigurationsForm.module.css';
import ConfigFormWrapper from "../../component/ConfigFormWrapper/ConfigFormWrapper";
import {AxiosError} from "axios";
import RoleGuard from "../../auth/RoleGuard";

const ISO20022ConfigForm = () => {
    const {getConfigurations, updateConfigurations} = useConfigurationsISO20022();

    const [config, setConfig] = useState<ConfigurationsISO20022 | null>(null);
    const [loading, setLoading] = useState(true);
    const [showKey, setShowKey] = useState(false);
    const [showSecret, setShowSecret] = useState(false);
    const [modal, setModal] = useState({
        show: false,
        title: '',
        message: '',
        error: false,
    });
    const [errorMessage, setErrorMessage] = useState<string | null>(null);

    const handleErrorMessage = (error: unknown, defaultMessage: string): string => {
        if (typeof error === 'string') return error;
        if (error instanceof AxiosError) {
            return error.response?.data?.message || error.message || defaultMessage;
        }
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
    }, []);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (!config) return;
        const {name, value} = e.target;
        setConfig({...config, [name]: value});
    };

    const showModalWithProps = (show: boolean,title: string, message: string, isError: boolean) => {
        setModal({
            show,
            title,
            message,
            error: isError,
        });

    };
    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!config) return;
        setLoading(true);
        try {
            const response = await updateConfigurations(config);
            showModalWithProps(true,"Success",response,false);
        } catch (error) {
            showModalWithProps(true,"Error",handleErrorMessage(error, 'Failed to update configuration'),true)
        } finally {
            setLoading(false);
        }
    };

    if (errorMessage) return (
        <div className={styles.errorContainer}>
            {errorMessage}
        </div>
    );

    return (
        <RoleGuard allowedRoles={['configuration']}>
            <Head>
                <title>ISO20022 Configuration | SPS</title>
            </Head>
            <ConfigFormWrapper
                title="ISO20022 Configuration"
                subtitle="Configure ISO20022 endpoints and credentials for secure financial message exchange."
                config={config}
                loading={loading}
                onChange={handleChange}
                onSubmit={handleSubmit}
                renderForm={(cfg, onChange) => (
                    <div className={styles.section}>
                        <Input label="Verification Endpoint" name="verification" type="url" value={cfg.verification}
                               onChange={onChange} placeholder="e.g. https://api.example.com/api/cb/verify"/>
                        <Input label="Transfer Endpoint" name="transfer" type="url" value={cfg.transfer}
                               onChange={onChange} placeholder="e.g. https://api.example.com/api/CB/Transfer"/>
                        <Input label="Return Endpoint" name="return" type="url" value={cfg.return} onChange={onChange}
                               placeholder="e.g. https://api.example.com/api/CB/Return"/>
                        <Input label="Status Endpoint" name="status" type="url" value={cfg.status} onChange={onChange}
                               placeholder="e.g. https://api.example.com/api/CB/Status"/>
                        <Input label="SIPS Endpoint" name="sips" type="url" value={cfg.sips} onChange={onChange}
                               placeholder="e.g. https://api.example.com/api/v1/Incoming"/>
                        <Input label="BIC" name="bic" type="text" value={cfg.bic} onChange={onChange}
                               placeholder="Enter BIC"/>
                        <Input label="Agent" name="agent" type="text" value={cfg.agent} onChange={onChange}
                               placeholder="Enter agent"/>

                        <div className={styles.passwordSection}>
                            <Input label="Key" name="key" type={showKey ? 'text' : 'password'} value={cfg.key}
                                   onChange={onChange} placeholder="Enter key"/>
                            <Checkbox label="Show Key" checked={showKey}
                                      onChange={(e) => setShowKey(e.target.checked)}/>
                        </div>

                        <div className={styles.passwordSection}>
                            <Input label="Secret" name="secret" type={showSecret ? 'text' : 'password'}
                                   value={cfg.secret} onChange={onChange} placeholder="Enter secret"/>
                            <Checkbox label="Show Secret" checked={showSecret}
                                      onChange={(e) => setShowSecret(e.target.checked)}/>
                        </div>
                    </div>
                )}
                modalProps={{
                    ...modal,
                    onClose: () => setModal({show: false, title: '', message: '', error: false}),
                }}
            />
        </RoleGuard>
    );
};

export default ISO20022ConfigForm;