import React from 'react';
import { FiAlertTriangle, FiLock } from 'react-icons/fi';
import ActionButton from '../component/common/ActionButton/ActionButton';
import styles from '../styles/Unauthorized.module.css';
import {useRouter} from "next/router";

const Unauthorized = () => {
    const router = useRouter();

    return (
        <div className={styles.container}>
            <div className={styles.content}>
                <div className={styles.iconContainer}>
                    <FiLock className={styles.lockIcon} />
                    <FiAlertTriangle className={styles.alertIcon} />
                </div>
                <h1 className={styles.title}>Unauthorized Access</h1>
                <p className={styles.message}>
                    You don&apos;t have permission to access this page. Please contact your administrator
                    if you believe this is an error.
                </p>
                <div className={styles.buttonContainer}>
                    <ActionButton
                        onClick={() => router.push('/')}
                        type="button"
                    >
                        Return to Home
                    </ActionButton>
                </div>
            </div>
        </div>
    );
};

export default Unauthorized;