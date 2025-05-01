import React, { useEffect, useState } from 'react';
import Head from 'next/head';
import useConfigurationsHosts from '../../api/hooks/useConfigurationsHosts';
import ConfigFormWrapper from '../../component/ConfigFormWrapper/ConfigFormWrapper';
import Input from '../../component/common/Input/Input';
import styles from '../../styles/ConfigurationsForm.module.css';
import { AxiosError } from 'axios';
import ActionButton from '../../component/common/ActionButton/ActionButton';
import RoleGuard from "../../auth/RoleGuard";

const HostsConfigForm = () => {
    const { getHosts, updateHosts } = useConfigurationsHosts();
    const [hosts, setHosts] = useState<string[]>([]);
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
                const rawData = await getHosts(); // rawData is expected as string | null | string[]
                let parsedHosts: string[] = [];

                if (typeof rawData === 'string') {
                    parsedHosts = rawData
                        .split(';')
                        .map((host) => host.trim())
                        .filter(Boolean);
                } else if (Array.isArray(rawData)) {
                    parsedHosts = rawData;
                }

                setHosts(parsedHosts);
            } catch (error) {
                setErrorMessage(handleErrorMessage(error, 'Failed to load host configurations'));
            } finally {
                setLoading(false);
            }
        };

        fetchData();
    }, []);

    const handleChange = (index: number, value: string) => {
        const updated = [...hosts];
        updated[index] = value;
        setHosts(updated);
    };

    const handleAddHost = () => {
        setHosts([...hosts, '']);
    };

    const handleRemoveHost = (index: number) => {
        const updated = hosts.filter((_, i) => i !== index);
        setHosts(updated);
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);
        try {
            const response = await updateHosts(hosts);
            setModal({ show: true, title: 'Success', message: response, error: false });
        } catch (error) {
            setModal({ show: true, title: 'Error', message: handleErrorMessage(error, 'Failed to update hosts'), error: true });
        } finally {
            setLoading(false);
        }
    };

    if (errorMessage) return <div className={styles.errorContainer}>{errorMessage}</div>;

    return (
        <RoleGuard allowedRoles={['configuration']}>
            <Head>
                <title>Hosts Configuration | SPS</title>
            </Head>

            <ConfigFormWrapper
                title="Hosts Configuration"
                subtitle="Manage the list of host addresses used by the application."
                config={hosts}
                loading={loading}
                onChange={() => {}}
                onSubmit={handleSubmit}
                renderForm={(hostsConfig) => (
                    <div className={styles.section}>
                        {hostsConfig.map((host, index) => (
                            <div key={index} className={styles.inlineGroup}>
                                <Input
                                    label={`Host ${index + 1}`}
                                    name={`host-${index}`}
                                    type="text"
                                    value={host}
                                    onChange={(e) => handleChange(index, e.target.value)}
                                />
                                <ActionButton
                                    type="button"
                                    className={styles.removeButton}
                                    onClick={() => handleRemoveHost(index)}
                                >
                                    Remove
                                </ActionButton>
                            </div>
                        ))}
                        <ActionButton
                            type="button"
                            className={styles.addButton}
                            onClick={handleAddHost}
                        >
                            Add Host
                        </ActionButton>
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

export default HostsConfigForm;
