import React, {useEffect, useState} from 'react';
import Head from 'next/head';

import useConfigurationsCore, {ConfigurationsCore} from "../../api/hooks/useConfigurationsCore";

import Input from '../../component/common/Input/Input';
import Checkbox from '../../component/common/Checkbox/Checkbox';
import styles from '../../styles/ConfigurationsForm.module.css';
import ConfigFormWrapper from "../../component/ConfigFormWrapper/ConfigFormWrapper";
import {AxiosError} from "axios";
import RoleGuard from "../../auth/RoleGuard";

const ISO20022ConfigForm = () => {
    const {getConfigurations, updateConfigurations} = useConfigurationsCore();

    const [config, setConfig] = useState<ConfigurationsCore | null>(null);
    const [loading, setLoading] = useState(true);
    const [showPassword, setShowPassword] = useState(false);
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

    const showModalWithProps = (show: boolean, title: string, message: string, isError: boolean) => {
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
            showModalWithProps(true, "Success", response, false);
        } catch (error) {
            showModalWithProps(true, "Error", handleErrorMessage(error, 'Failed to update configuration'), true)
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
                <title>System Configuration | SPS</title>
            </Head>
            <ConfigFormWrapper
                title="Core Configuration"
                subtitle="Manage system-wide settings and API configurations"
                config={config}
                loading={loading}
                onChange={handleChange}
                onSubmit={handleSubmit}
                renderForm={(cfg, onChange) => (
                    <>
                        <div className={styles.section}>
                            <h2 className={styles.sectionTitle}>API Settings</h2>
                            <Input
                                label="Base URL"
                                type="text"
                                name="baseUrl"
                                value={cfg.baseUrl}
                                onChange={onChange}
                                placeholder="Enter Base URL"
                                required
                            />
                            <Input
                                label="Public Keys Repo URL"
                                type="text"
                                name="publicKeysRepUrl"
                                value={cfg.publicKeysRepUrl}
                                onChange={onChange}
                                placeholder="Enter Public Keys Repo URL"
                                required
                            />
                        </div>

                        <div className={styles.section}>
                            <h2 className={styles.sectionTitle}>Authentication</h2>
                            <Input
                                label="Username"
                                type="text"
                                name="username"
                                value={cfg.username}
                                onChange={onChange}
                                placeholder="Enter Username"
                            />
                            <div className={styles.passwordSection}>
                                <Input
                                    label="Password"
                                    type={showPassword ? 'text' : 'password'}
                                    name="password"
                                    value={cfg.password}
                                    onChange={onChange}
                                    placeholder="Enter Password"
                                />
                                <Checkbox
                                    label="Show Password"
                                    checked={showPassword}
                                    onChange={(e) => setShowPassword(e.target.checked)}
                                />
                            </div>
                            <Input
                                label="BIC"
                                type="text"
                                name="bic"
                                value={cfg.bic}
                                onChange={onChange}
                                placeholder="Enter BIC"
                            />
                        </div>

                        <div className={styles.section}>
                            <h2 className={styles.sectionTitle}>SAF Settings</h2>
                            <Input
                                label="SAF Expression"
                                type="text"
                                name="safExpression"
                                value={cfg.safExpression}
                                onChange={onChange}
                                placeholder="Enter SAF Expression"
                            />
                            <Input
                                label="SAF Page"
                                type="number"
                                name="safPage"
                                value={cfg.safPage.toString()}
                                onChange={onChange}
                                placeholder="Enter SAF Page"
                                min={1}
                            />
                            <Input
                                label="SAF Time Zone Info"
                                type="text"
                                name="safTimeZoneInfo"
                                value={cfg.safTimeZoneInfo}
                                onChange={onChange}
                                placeholder="Enter SAF Time Zone Info"
                            />
                            <Input
                                label="SAF Max Retries"
                                type="number"
                                name="safMaxRetries"
                                value={cfg.safMaxRetries.toString()}
                                onChange={onChange}
                                placeholder="Enter SAF Max Retries"
                                min={0}
                            />
                        </div>
                    </>
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