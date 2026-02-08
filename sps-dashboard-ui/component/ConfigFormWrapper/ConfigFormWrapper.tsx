import React from 'react';
import SpinLoading from '../Loading/SpinLoading/SpinLoading';
import AlertModal from '../common/AlertModal/AlertModal';
import ActionButton from '../common/ActionButton/ActionButton';
import styles from '../../styles/ConfigurationsForm.module.css';

type ConfigFormWrapperProps<T> = {
    title: string;
    subtitle: string;
    config: T | null;
    loading: boolean;
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
    onSubmit: (e: React.FormEvent) => void;
    renderForm: (config: T, onChange: (e: React.ChangeEvent<HTMLInputElement>) => void) => React.ReactNode;
    modalProps?: {
        show: boolean;
        title: string;
        message: string;
        error: boolean;
        onClose: () => void;
    };
};

function ConfigFormWrapper<T>({
                                  title,
                                  subtitle,
                                  config,
                                  loading,
                                  onChange,
                                  onSubmit,
                                  renderForm,
                                  modalProps,
                              }: ConfigFormWrapperProps<T>) {
    return (
        <div className={styles.formContainer}>
            <div className={styles.header}>
                <h1 className={styles.title}>{title}</h1>
                <p className={styles.subtitle}>{subtitle}</p>
            </div>

            {loading ? (
                <div className={styles.loadingContainer}><SpinLoading /></div>
            ) : config ? (
                <form onSubmit={onSubmit} className={styles.form}>
                    {renderForm(config, onChange)}
                    <div className={styles.actions}>
                        <ActionButton type="submit" disabled={loading} className={styles.button}>
                            {loading ? 'Saving...' : 'Save Changes'}
                        </ActionButton>
                    </div>
                </form>
            ) : (
                <div className={styles.errorContainer}>No configuration data available.</div>
            )}

            {modalProps?.show && (
                <AlertModal
                    title={modalProps.title}
                    message={modalProps.message}
                    onConfirm={modalProps.onClose}
                    error={modalProps.error}
                    buttonText="OK"
                />
            )}
        </div>
    );
}

export default ConfigFormWrapper;
