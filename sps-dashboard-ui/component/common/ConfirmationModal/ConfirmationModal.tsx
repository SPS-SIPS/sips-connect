import React from 'react';
import styles from './ConfirmationModal.module.css';

interface ConfirmationModalProps {
    message: string;
    onConfirm: () => void;
    onCancel: () => void;
}

const ConfirmationModal: React.FC<ConfirmationModalProps> = ({ message, onConfirm, onCancel }) => {
    return (
        <div className={styles.modalOverlay}>
            <div className={styles.modal}>
                <h3>Confirmation</h3>
                <p>{message}</p>
                <div className={styles.buttons}>
                    <button onClick={onConfirm} className={styles.confirmButton}>Yes</button>
                    <button onClick={onCancel} className={styles.cancelButton}>Cancel</button>
                </div>
            </div>
        </div>
    );
};

export default ConfirmationModal;
