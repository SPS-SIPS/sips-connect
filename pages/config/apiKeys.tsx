import React, { useEffect, useState } from 'react';
import Head from 'next/head';
import useConfigurationsAPIKeys, { ApiKeyConfiguration } from '../../api/hooks/useConfigurationsAPIKeys';
import Input from '../../component/common/Input/Input';
import RoleGuard from '../../auth/RoleGuard';
import ActionButton from '../../component/common/ActionButton/ActionButton';
import AlertModal from '../../component/common/AlertModal/AlertModal';
import { extractErrorMessage } from '../../utils/extractErrorMessage';
import Checkbox from "../../component/common/Checkbox/Checkbox";
import styles from '../../styles/ConfigurationsForm.module.css';
import apiKeysStyles from '../../styles/apiKeys.module.css';
import SpinLoading from "../../component/Loading/SpinLoading/SpinLoading";

const ApiKeysConfigForm = () => {
    const { getApiKeys, updateApiKey, deleteApiKey } = useConfigurationsAPIKeys();
    const [passwordVisibility, setPasswordVisibility] = useState<{
        [key: string]: {
            key: boolean,
            secret: boolean
        }
    }>({});
    const [apiKeys, setApiKeys] = useState<ApiKeyConfiguration[]>([]);
    const [isAddingNew, setIsAddingNew] = useState<boolean>(false);
    const [editCache, setEditCache] = useState<Partial<ApiKeyConfiguration>>({});
    const [loading, setLoading] = useState(false);
    const [fetching, setFetching] = useState(true);
    const [modal, setModal] = useState({
        show: false,
        title: '',
        message: '',
        error: false,
    });

    useEffect(() => {
        const fetchData = async () => {
            try {
                setFetching(true);
                const data = await getApiKeys();
                setApiKeys(data);

                // Initialize visibility state for each key
                const initialVisibility = data.reduce((acc, key) => ({
                    ...acc,
                    [key.key]: { key: false, secret: false }
                }), {});
                setPasswordVisibility(initialVisibility);
            } catch (error) {
                const errorMessage = extractErrorMessage(error);
                setModal({
                    show: true,
                    title: 'Error',
                    message: errorMessage,
                    error: true,
                });
            } finally {
                setFetching(false);
            }
        };

        fetchData();
    }, []);

    const handleCancel = () => {
        setIsAddingNew(false);
        setEditCache({});
        setPasswordVisibility(prev => ({
            ...prev,
            new: { key: false, secret: false }
        }));
    };

    const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value } = e.target;
        setEditCache(prev => ({ ...prev, [name]: value }));
    };


    const handleToggleVisibility = (e: React.ChangeEvent<HTMLInputElement>) => {
        const [apiKey, fieldType] = e.target.name.split('-') as [string, 'key' | 'secret'];
        setPasswordVisibility(prev => ({
            ...prev,
            [apiKey]: {
                ...prev[apiKey],
                [fieldType]: e.target.checked
            }
        }));
    };

    const handleSaveNew = async () => {
        if (!isFormValid()) {
            setModal({
                show: true,
                title: 'Error',
                message: "Please fill all required fields with valid values",
                error: true,
            });
            return;
        }

        try {
            setLoading(true);
            const newKey = {
                name: editCache.name?.trim() || '',
                key: editCache.key?.trim() || '',
                secret: editCache.secret?.trim() || ''
            } as ApiKeyConfiguration;

            await updateApiKey(newKey);
            setApiKeys(prev => [...prev, newKey]);

            // Add visibility state for the new key
            setPasswordVisibility(prev => ({
                ...prev,
                [newKey.key]: { key: false, secret: false }
            }));

            setIsAddingNew(false);
            setEditCache({});
        } catch (error) {
            const errorMessage = extractErrorMessage(error);
            setModal({
                show: true,
                title: 'Error',
                message: errorMessage,
                error: true,
            });
        } finally {
            setLoading(false);
        }
    };

    const handleDelete = async (key: string) => {
        try {
            setLoading(true);
            await deleteApiKey(key);
            setApiKeys(prev => prev.filter(k => k.key !== key));

            // Remove visibility state for deleted key
            setPasswordVisibility(prev => {
                const newState = { ...prev };
                delete newState[key];
                return newState;
            });
        } catch (error) {
            const errorMessage = extractErrorMessage(error);
            setModal({
                show: true,
                title: 'Error',
                message: errorMessage,
                error: true,
            });
        } finally {
            setLoading(false);
        }
    };

    const isFormValid = () => {
        return editCache.name?.trim() &&
            editCache.key?.trim() &&
            editCache.secret?.trim()
    };

    if (fetching) {
        return (
            <div className={apiKeysStyles.loadingContainer}>
                <SpinLoading />
                <p className={apiKeysStyles.loadingMessage}>Fetching API keys, please wait...</p>
            </div>
        );
    }

    return (
        <RoleGuard allowedRoles={['configuration']}>
            <Head>
                <title>API Key Configuration | SPS</title>
                <meta name="description" content="Manage API keys configuration" />
            </Head>

            <div className={styles.formContainer}>
                <div className={styles.header}>
                    <h1 className={styles.title}>API Key Configuration</h1>
                    <p className={styles.subtitle}>Add or manage your API keys securely.</p>
                </div>

                {/* Display existing API keys */}
                {apiKeys.length > 0 ? (
                    apiKeys.map((apiKey, index) => (
                        <div key={`${apiKey.key}-${index}`} className={`${styles.section} ${apiKeysStyles.section}`}>
                        <div className={apiKeysStyles.actions}>
                                <ActionButton
                                    onClick={() => handleDelete(apiKey.key)}
                                    disabled={loading}
                                    className={apiKeysStyles.deleteButton}
                                    aria-label={`Delete API key ${apiKey.name}`}
                                >
                                    Delete
                                </ActionButton>
                            </div>

                            <Input
                                label="Name"
                                name="name"
                                type="text"
                                value={apiKey.name || ''}
                                disabled
                                onChange={() => {}}
                                aria-label="API key name"
                            />

                            <div className={styles.passwordSection}>
                                <Input
                                    label="Key"
                                    name="key"
                                    type={passwordVisibility[apiKey.key]?.key ? 'text' : 'password'}
                                    value={apiKey.key || ''}
                                    disabled
                                    onChange={() => {}}
                                    aria-label="API key value"
                                />
                                <Checkbox
                                    label="Show Key"
                                    checked={passwordVisibility[apiKey.key]?.key || false}
                                    onChange={handleToggleVisibility}
                                    name={`${apiKey.key}-key`}
                                    id={`${apiKey.key}-key-checkbox`}
                                />
                            </div>

                            <div className={styles.passwordSection}>
                                <Input
                                    label="Secret"
                                    name="secret"
                                    type={passwordVisibility[apiKey.key]?.secret ? 'text' : 'password'}
                                    value={apiKey.secret || ''}
                                    disabled
                                    onChange={() => {}}
                                    aria-label="API key secret"
                                />
                                <Checkbox
                                    label="Show Secret"
                                    checked={passwordVisibility[apiKey.key]?.secret || false}
                                    onChange={handleToggleVisibility}
                                    name={`${apiKey.key}-secret`}
                                    id={`${apiKey.key}-secret-checkbox`}
                                />
                            </div>
                        </div>
                    ))
                ) : (
                    !isAddingNew && (
                        <div className={apiKeysStyles.noKeysMessage}>
                            No API keys configured yet.
                        </div>
                    )
                )}

                {isAddingNew && (
                    <div className={`${styles.section} ${apiKeysStyles.section}`}>
                        <div className={apiKeysStyles.actions}>
                            <ActionButton
                                onClick={handleSaveNew}
                                disabled={!isFormValid() || loading}
                                aria-label="Save new API key"
                                type={"button"}
                                className={apiKeysStyles.saveButton}
                            >
                                {loading ? 'Saving...' : 'Save'}
                            </ActionButton>
                            <ActionButton
                                onClick={handleCancel}
                                disabled={loading}
                                aria-label="Cancel adding new API key"
                                type={"button"}
                                className={apiKeysStyles.cancelButton}
                            >
                                Cancel
                            </ActionButton>
                        </div>

                        <Input
                            label="Name"
                            name="name"
                            type="text"
                            value={editCache.name || ''}
                            onChange={handleInputChange}
                            required
                            aria-label="New API key name"
                            errorMessage={!editCache.name?.trim() ? 'Name is required' : undefined}
                        />

                        <div className={styles.passwordSection}>
                            <Input
                                label="Key"
                                name="key"
                                type={passwordVisibility['new']?.key ? 'text' : 'password'}
                                value={editCache.key || ''}
                                onChange={(e) => {
                                    handleInputChange(e);
                                }}
                                required
                                aria-label="New API key value"
                                errorMessage={!editCache.key?.trim() ? 'Key is required': undefined}
                            />
                            <Checkbox
                                label="Show Key"
                                checked={passwordVisibility['new']?.key || false}
                                onChange={handleToggleVisibility}
                                name="new-key"
                                id="new-key-checkbox"
                            />
                        </div>

                        <div className={styles.passwordSection}>
                            <Input
                                label="Secret"
                                name="secret"
                                type={passwordVisibility['new']?.secret ? 'text' : 'password'}
                                value={editCache.secret || ''}
                                onChange={handleInputChange}
                                required
                                aria-label="New API key secret"
                                errorMessage={!editCache.secret?.trim() ? 'Secret is required' : undefined}
                            />
                            <Checkbox
                                label="Show Secret"
                                checked={passwordVisibility['new']?.secret || false}
                                onChange={handleToggleVisibility}
                                name="new-secret"
                                id="new-secret-checkbox"
                            />
                        </div>
                    </div>
                )}

                {!isAddingNew && (
                    <div className={apiKeysStyles.actions}>
                        <ActionButton
                            onClick={() => {
                                setIsAddingNew(true);
                                setEditCache({ name: '', key: '', secret: '' });
                                setPasswordVisibility(prev => ({
                                    ...prev,
                                    new: { key: false, secret: false }
                                }));
                            }}
                            aria-label="Add new API key"
                            className={apiKeysStyles.addNewButton}
                        >
                            Add New API Key
                        </ActionButton>
                    </div>
                )}
            </div>

            {modal.show && (
                <AlertModal
                    title={modal.title}
                    message={modal.message}
                    error={modal.error}
                    onConfirm={() => setModal(prev => ({ ...prev, show: false }))}
                    onClose={() => setModal(prev => ({ ...prev, show: false }))}
                    aria-labelledby="modal-title"
                    aria-describedby="modal-description"
                />
            )}
        </RoleGuard>
    );
};

export default ApiKeysConfigForm;